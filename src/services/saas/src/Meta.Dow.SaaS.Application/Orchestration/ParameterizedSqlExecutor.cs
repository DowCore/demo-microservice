using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public sealed class ParameterizedSqlResult
{
    public int AffectedRows { get; init; }

    public JsonArray? Rows { get; init; }

    public string SqlFingerprint { get; init; } = "";

    public string Op { get; init; } = "";
}

public interface IParameterizedSqlExecutor
{
    Task<ParameterizedSqlResult> QueryAsync(
        DataSource dataSource,
        string sql,
        JsonObject? args,
        bool isDryRun,
        CancellationToken cancellationToken = default
    );

    Task<ParameterizedSqlResult> ExecuteAsync(
        DataSource dataSource,
        string sql,
        JsonObject? args,
        bool isDryRun,
        CancellationToken cancellationToken = default
    );

    Task<ParameterizedSqlResult> BatchAsync(
        DataSource dataSource,
        string sql,
        JsonArray? paramList,
        bool isDryRun,
        CancellationToken cancellationToken = default
    );
}

public class ParameterizedSqlExecutor : IParameterizedSqlExecutor, ITransientDependency
{
    private static readonly Regex NamedParamRegex = new(
        @"[@:](?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private readonly ILogger<ParameterizedSqlExecutor> _logger;

    public ParameterizedSqlExecutor(ILogger<ParameterizedSqlExecutor> logger)
    {
        _logger = logger;
    }

    public Task<ParameterizedSqlResult> QueryAsync(
        DataSource dataSource,
        string sql,
        JsonObject? args,
        bool isDryRun,
        CancellationToken cancellationToken = default
    )
    {
        return RunSingleAsync(dataSource, sql, args, isDryRun, expectRows: true, cancellationToken);
    }

    public Task<ParameterizedSqlResult> ExecuteAsync(
        DataSource dataSource,
        string sql,
        JsonObject? args,
        bool isDryRun,
        CancellationToken cancellationToken = default
    )
    {
        return RunSingleAsync(dataSource, sql, args, isDryRun, expectRows: false, cancellationToken);
    }

    public async Task<ParameterizedSqlResult> BatchAsync(
        DataSource dataSource,
        string sql,
        JsonArray? paramList,
        bool isDryRun,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var text = NormalizeSql(sql);
        var op = DetectOp(text);
        EnsureAllowed(dataSource, op);

        paramList ??= [];
        if (paramList.Count > OrchestrationConsts.CodeDbBatchMaxRows)
        {
            throw new UserFriendlyException(
                $"db.batch parameter list exceeds the limit of {OrchestrationConsts.CodeDbBatchMaxRows}."
            );
        }

        var fingerprint = Fingerprint(text);
        if (isDryRun)
        {
            _logger.LogInformation(
                "[CodeDb] dry-run batch ds={DataSource} op={Op} fingerprint={Fingerprint} rows={Rows}",
                dataSource.Code,
                op,
                fingerprint,
                paramList.Count
            );
            return new ParameterizedSqlResult
            {
                AffectedRows = 0,
                Rows = null,
                SqlFingerprint = fingerprint,
                Op = op
            };
        }

        await using var conn = CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        var total = 0;
        try
        {
            foreach (var item in paramList)
            {
                var args = item as JsonObject
                           ?? throw new UserFriendlyException("db.batch paramList items must be objects.");
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = text;
                cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
                BindParameters(cmd, text, args, dataSource.Provider);
                total += await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "[CodeDb] batch ds={DataSource} op={Op} fingerprint={Fingerprint} affected={Affected} paramShape={Shape}",
            dataSource.Code,
            op,
            fingerprint,
            total,
            DescribeParamShape(paramList.FirstOrDefault() as JsonObject)
        );

        return new ParameterizedSqlResult
        {
            AffectedRows = total,
            SqlFingerprint = fingerprint,
            Op = op
        };
    }

    private async Task<ParameterizedSqlResult> RunSingleAsync(
        DataSource dataSource,
        string sql,
        JsonObject? args,
        bool isDryRun,
        bool expectRows,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var text = NormalizeSql(sql);
        var op = DetectOp(text);
        EnsureAllowed(dataSource, op);

        if (expectRows && op != "select")
        {
            throw new UserFriendlyException("db.query only allows SELECT/WITH statements.");
        }

        if (!expectRows && op == "select")
        {
            throw new UserFriendlyException("db.execute does not allow SELECT; use db.query.");
        }

        var fingerprint = Fingerprint(text);
        if (isDryRun)
        {
            _logger.LogInformation(
                "[CodeDb] dry-run {Mode} ds={DataSource} op={Op} fingerprint={Fingerprint}",
                expectRows ? "query" : "execute",
                dataSource.Code,
                op,
                fingerprint
            );
            return new ParameterizedSqlResult
            {
                AffectedRows = 0,
                Rows = expectRows ? [] : null,
                SqlFingerprint = fingerprint,
                Op = op
            };
        }

        await using var conn = CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = text;
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        BindParameters(cmd, text, args, dataSource.Provider);

        if (expectRows)
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var rows = await ReadRowsAsync(reader, dataSource.Provider, cancellationToken);
            _logger.LogInformation(
                "[CodeDb] query ds={DataSource} fingerprint={Fingerprint} rowCount={Count} paramShape={Shape}",
                dataSource.Code,
                fingerprint,
                rows.Count,
                DescribeParamShape(args)
            );
            return new ParameterizedSqlResult
            {
                AffectedRows = rows.Count,
                Rows = rows,
                SqlFingerprint = fingerprint,
                Op = op
            };
        }

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation(
            "[CodeDb] execute ds={DataSource} op={Op} fingerprint={Fingerprint} affected={Affected} paramShape={Shape}",
            dataSource.Code,
            op,
            fingerprint,
            affected,
            DescribeParamShape(args)
        );
        return new ParameterizedSqlResult
        {
            AffectedRows = affected,
            SqlFingerprint = fingerprint,
            Op = op
        };
    }

    private static void EnsureAllowed(DataSource dataSource, string op)
    {
        if (!dataSource.AllowsOp(op))
        {
            throw new UserFriendlyException(
                $"Data source '{dataSource.Code}' does not allow '{op}' (accessMode={dataSource.AccessMode})."
            );
        }
    }

    private static string NormalizeSql(string sql)
    {
        var text = Check.NotNullOrWhiteSpace(sql, nameof(sql)).Trim();
        if (text.Length > 8_000)
        {
            throw new UserFriendlyException("SQL text is too long.");
        }

        // 禁止多语句
        var withoutStrings = StripStringLiterals(text);
        if (withoutStrings.Contains(';'))
        {
            throw new UserFriendlyException("Multiple SQL statements are not allowed.");
        }

        return text;
    }

    private static string DetectOp(string sql)
    {
        var trimmed = sql.TrimStart();
        if (trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("with", StringComparison.OrdinalIgnoreCase))
        {
            return "select";
        }

        if (trimmed.StartsWith("insert", StringComparison.OrdinalIgnoreCase))
        {
            return "insert";
        }

        if (trimmed.StartsWith("update", StringComparison.OrdinalIgnoreCase))
        {
            return "update";
        }

        if (trimmed.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
        {
            return "delete";
        }

        throw new UserFriendlyException("Only SELECT/INSERT/UPDATE/DELETE (and WITH…SELECT) are allowed.");
    }

    private static DbConnection CreateConnection(DataSource dataSource)
    {
        if (!DataSourceProvider.IsSql(dataSource.Provider))
        {
            throw new UserFriendlyException(
                $"Provider '{dataSource.Provider}' is not a SQL engine. Use Redis/Mongo APIs instead."
            );
        }

        return dataSource.Provider.ToLowerInvariant() switch
        {
            DataSourceProvider.Postgres => new NpgsqlConnection(dataSource.ConnectionString),
            DataSourceProvider.MySql => new MySqlConnection(dataSource.ConnectionString),
            DataSourceProvider.SqlServer => new SqlConnection(dataSource.ConnectionString),
            DataSourceProvider.Oracle => new OracleConnection(dataSource.ConnectionString),
            _ => throw new UserFriendlyException($"Unsupported data source provider: {dataSource.Provider}")
        };
    }

    private static void BindParameters(DbCommand cmd, string sql, JsonObject? args, string provider)
    {
        var names = NamedParamRegex.Matches(sql)
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        args ??= [];

        foreach (var name in names)
        {
            if (!TryGetArg(args, name, out var value))
            {
                throw new UserFriendlyException($"Missing SQL parameter '{name}'.");
            }

            var p = cmd.CreateParameter();
            // SQL Server / Oracle 习惯带前缀；Npgsql/MySql 用裸名
            p.ParameterName = provider.ToLowerInvariant() switch
            {
                DataSourceProvider.SqlServer => "@" + name,
                DataSourceProvider.Oracle => name, // OracleParameter 通常不带冒号
                _ => name
            };
            p.Value = ToDbValue(value, provider) ?? DBNull.Value;
            p.DbType = p.Value switch
            {
                DateTimeOffset => DbType.DateTimeOffset,
                DateTime dt when dt.Kind == DateTimeKind.Unspecified && dt.TimeOfDay == TimeSpan.Zero
                    => DbType.Date,
                DateTime => DbType.DateTime,
                Guid => DbType.Guid,
                bool => DbType.Boolean,
                byte[] => DbType.Binary,
                byte => DbType.Byte,
                short => DbType.Int16,
                int => DbType.Int32,
                long => DbType.Int64,
                decimal => DbType.Decimal,
                double => DbType.Double,
                float => DbType.Single,
                _ => p.DbType
            };

            cmd.Parameters.Add(p);
        }

        // 多余参数：严格模式报错，避免静默拼错
        foreach (var prop in args)
        {
            if (names.All(n => !string.Equals(n, prop.Key, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UserFriendlyException($"Unexpected SQL parameter '{prop.Key}'.");
            }
        }
    }

    private static bool TryGetArg(JsonObject args, string name, out JsonNode? value)
    {
        foreach (var (k, v) in args)
        {
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? ToDbValue(JsonNode? node, string? provider = null)
    {
        if (node == null || node is JsonValue jv && jv.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.String => CoerceString(value.GetValue<string>(), provider),
                JsonValueKind.Number => JsonNodeNumbers.ToNumberObject(value)
                    ?? throw new UserFriendlyException("Invalid numeric SQL parameter."),
                JsonValueKind.True => SqlDialect.ToProviderBoolean(provider, true),
                JsonValueKind.False => SqlDialect.ToProviderBoolean(provider, false),
                JsonValueKind.Null => null,
                _ => value.ToJsonString()
            };
        }

        if (node is JsonArray arr)
        {
            return arr.Select(x => ToDbValue(x, provider)).ToArray();
        }

        return node.ToJsonString();
    }

    private static object? CoerceString(string? text, string? provider)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (SqlDialect.TryParseTemporal(text, out var parsed, out var dateOnly))
        {
            return SqlDialect.ToProviderTemporal(provider, parsed, dateOnly);
        }

        if (Guid.TryParse(text, out var guid) &&
            SqlDialect.NormalizeProvider(provider ?? string.Empty) == DataSourceProvider.Oracle)
        {
            return SqlDialect.ToProviderGuid(provider, guid);
        }

        return text;
    }

    private static async Task<JsonArray> ReadRowsAsync(
        DbDataReader reader,
        string provider,
        CancellationToken cancellationToken
    )
    {
        var rows = new JsonArray();
        var count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (++count > OrchestrationConsts.CodeDbQueryMaxRows)
            {
                throw new UserFriendlyException(
                    $"db.query result exceeds the limit of {OrchestrationConsts.CodeDbQueryMaxRows} rows."
                );
            }

            var obj = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                obj[name] = reader.IsDBNull(i)
                    ? null
                    : FromClr(reader.GetValue(i), reader.GetDataTypeName(i), provider);
            }

            rows.Add(obj);
        }

        return rows;
    }

    private static JsonNode? FromClr(object value, string? dbType, string? provider)
    {
        return value switch
        {
            null or DBNull => null,
            string s => s,
            bool b => b,
            byte or sbyte or short or ushort or int => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            uint or long or ulong => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            decimal d => d,
            float or double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            DateTime dt => FormatTemporal(dt, dbType, provider),
            DateTimeOffset dto => FormatTemporal(dto.UtcDateTime, dbType, provider),
            Guid g => g.ToString(),
            byte[] bytes when SqlDialect.NormalizeProvider(provider ?? string.Empty) == DataSourceProvider.Oracle &&
                              bytes.Length == 16
                => new Guid(bytes).ToString(),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }

    private static string FormatTemporal(DateTime value, string? dbType, string? provider)
    {
        if (SqlDialect.IsDateOnlyDbType(provider, dbType))
        {
            return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static string Fingerprint(string sql)
    {
        var normalized = Regex.Replace(sql, @"\s+", " ").Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string DescribeParamShape(JsonObject? args)
    {
        if (args == null || args.Count == 0)
        {
            return "{}";
        }

        var parts = args.Select(kv =>
        {
            var t = kv.Value switch
            {
                null => "null",
                JsonArray => "array",
                JsonObject => "object",
                JsonValue jv => jv.GetValueKind().ToString().ToLowerInvariant(),
                _ => "unknown"
            };
            return $"{kv.Key}:{t}";
        });
        return "{" + string.Join(",", parts) + "}";
    }

    private static string StripStringLiterals(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (!inDouble && c == '\'' && (i == 0 || sql[i - 1] != '\\'))
            {
                inSingle = !inSingle;
                sb.Append(' ');
                continue;
            }

            if (!inSingle && c == '"' && (i == 0 || sql[i - 1] != '\\'))
            {
                inDouble = !inDouble;
                sb.Append(' ');
                continue;
            }

            sb.Append(inSingle || inDouble ? ' ' : c);
        }

        return sb.ToString();
    }
}
