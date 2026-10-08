using System.Data.Common;
using System.Globalization;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public sealed class SqlCatalogObject
{
    public string ObjectName { get; init; } = null!;

    public string DisplayName { get; init; } = null!;

    public string Kind { get; init; } = QueryObjectKind.View;

    public string RoutineKind { get; init; } = QueryRoutineKind.View;

    public bool CanSelectFrom { get; init; }
}

public sealed class SqlCatalogDetails
{
    public string ObjectName { get; init; } = null!;

    public string DisplayName { get; init; } = null!;

    public string Kind { get; init; } = QueryObjectKind.View;

    public string RoutineKind { get; init; } = QueryRoutineKind.View;

    public bool CanSelectFrom { get; init; }

    public string? Comment { get; init; }

    public string? ImportWarning { get; init; }

    public List<TableColumn> Columns { get; init; } = [];

    public List<QueryObjectParameter> Parameters { get; init; } = [];
}

public interface ISqlCatalogInspector
{
    Task<IReadOnlyList<SqlCatalogObject>> ListAsync(
        DataSource dataSource,
        string kind,
        CancellationToken cancellationToken = default
    );

    Task<(IReadOnlyList<SqlCatalogObject> Items, int TotalCount)> ListPageAsync(
        DataSource dataSource,
        string kind,
        string? filter,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default
    );

    Task<SqlCatalogDetails> DescribeAsync(
        DataSource dataSource,
        string kind,
        string objectName,
        CancellationToken cancellationToken = default
    );
}

public class SqlCatalogInspector : ISqlCatalogInspector, ITransientDependency
{
    public async Task<IReadOnlyList<SqlCatalogObject>> ListAsync(
        DataSource dataSource,
        string kind,
        CancellationToken cancellationToken = default
    )
    {
        var (items, _) = await ListPageAsync(
            dataSource,
            kind,
            filter: null,
            skipCount: 0,
            maxResultCount: int.MaxValue,
            cancellationToken
        );
        return items;
    }

    public async Task<(IReadOnlyList<SqlCatalogObject> Items, int TotalCount)> ListPageAsync(
        DataSource dataSource,
        string kind,
        string? filter,
        int skipCount,
        int maxResultCount,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        if (!DataSourceProvider.IsSql(dataSource.Provider))
        {
            throw new UserFriendlyException("Query objects require a SQL data source.");
        }

        var normalizedKind = QueryObjectKind.Normalize(kind);
        var skip = Math.Max(0, skipCount);
        var take =
            maxResultCount <= 0
                ? 20
                : maxResultCount >= int.MaxValue / 2
                    ? int.MaxValue
                    : Math.Min(maxResultCount, 200);
        var nameFilter = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

        await using var conn = SqlDialect.CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        var all = normalizedKind == QueryObjectKind.Procedure
            ? await ListRoutinesAsync(conn, dataSource.Provider, nameFilter, cancellationToken)
            : await ListViewsAsync(conn, dataSource.Provider, nameFilter, cancellationToken);

        // SQL LIKE 兜底：再按名称做一次内存过滤（避免驱动/参数绑定差异导致未生效）
        if (!string.IsNullOrEmpty(nameFilter))
        {
            all = all
                .Where(x => x.ObjectName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var total = all.Count;
        if (take == int.MaxValue && skip == 0)
        {
            return (all, total);
        }

        if (skip >= total)
        {
            return (Array.Empty<SqlCatalogObject>(), total);
        }

        var page = all.Skip(skip).Take(take).ToList();
        return (page, total);
    }

    public async Task<SqlCatalogDetails> DescribeAsync(
        DataSource dataSource,
        string kind,
        string objectName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        var name = Check.NotNullOrWhiteSpace(objectName, nameof(objectName)).Trim();
        if (!SqlIdentifier.IsValid(name))
        {
            throw new UserFriendlyException($"Invalid SQL identifier: {name}");
        }

        var normalizedKind = QueryObjectKind.Normalize(kind);
        var listed = await ListAsync(dataSource, normalizedKind, cancellationToken);
        var meta = listed.FirstOrDefault(x => x.ObjectName.Equals(name, StringComparison.OrdinalIgnoreCase))
                   ?? throw new UserFriendlyException($"Object was not found: {name}");

        await using var conn = SqlDialect.CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        var columns = normalizedKind == QueryObjectKind.View || meta.CanSelectFrom
            ? await ReadColumnsAsync(conn, dataSource.Provider, name, meta, cancellationToken)
            : [];
        var parameters = normalizedKind == QueryObjectKind.Procedure
            ? await ReadParametersAsync(conn, dataSource.Provider, name, cancellationToken)
            : [];

        string? warning = null;
        if (normalizedKind == QueryObjectKind.Procedure && columns.Count == 0)
        {
            warning = "未能探测结果列。创建查询资源后将按首次返回行展示；可刷新目录重试。";
        }

        return new SqlCatalogDetails
        {
            ObjectName = meta.ObjectName,
            DisplayName = meta.DisplayName,
            Kind = normalizedKind,
            RoutineKind = meta.RoutineKind,
            CanSelectFrom = meta.CanSelectFrom || normalizedKind == QueryObjectKind.View,
            ImportWarning = warning,
            Columns = columns,
            Parameters = parameters
        };
    }

    private static async Task<List<SqlCatalogObject>> ListViewsAsync(
        DbConnection conn,
        string provider,
        string? nameFilter,
        CancellationToken cancellationToken
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        var p = SqlDialect.NormalizeProvider(provider);
        var hasFilter = !string.IsNullOrEmpty(nameFilter);
        cmd.CommandText = p switch
        {
            DataSourceProvider.Postgres => hasFilter
                ? "SELECT table_name FROM information_schema.views WHERE table_schema = current_schema() AND table_name ILIKE @f ORDER BY 1"
                : "SELECT table_name FROM information_schema.views WHERE table_schema = current_schema() ORDER BY 1",
            DataSourceProvider.MySql => hasFilter
                ? "SELECT TABLE_NAME FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE @f ORDER BY 1"
                : "SELECT TABLE_NAME FROM information_schema.VIEWS WHERE TABLE_SCHEMA = DATABASE() ORDER BY 1",
            DataSourceProvider.SqlServer => hasFilter
                ? "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = SCHEMA_NAME() AND TABLE_NAME LIKE @f ORDER BY 1"
                : "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_SCHEMA = SCHEMA_NAME() ORDER BY 1",
            DataSourceProvider.Oracle => hasFilter
                ? "SELECT VIEW_NAME FROM USER_VIEWS WHERE VIEW_NAME LIKE :f ORDER BY 1"
                : "SELECT VIEW_NAME FROM USER_VIEWS ORDER BY 1",
            _ => throw new UserFriendlyException($"Unsupported provider: {p}")
        };
        if (hasFilter)
        {
            var pattern = $"%{SanitizeLikeLiteral(nameFilter!)}%";
            if (p == DataSourceProvider.Oracle)
            {
                pattern = pattern.ToUpperInvariant();
            }

            AddParam(cmd, "f", pattern);
        }

        var list = new List<SqlCatalogObject>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = ReadString(reader, 0);
            if (!SqlIdentifier.IsValid(name))
            {
                continue;
            }

            list.Add(new SqlCatalogObject
            {
                ObjectName = name,
                DisplayName = name,
                Kind = QueryObjectKind.View,
                RoutineKind = QueryRoutineKind.View,
                CanSelectFrom = true
            });
        }

        return list;
    }

    private static async Task<List<SqlCatalogObject>> ListRoutinesAsync(
        DbConnection conn,
        string provider,
        string? nameFilter,
        CancellationToken cancellationToken
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        var p = SqlDialect.NormalizeProvider(provider);
        var hasFilter = !string.IsNullOrEmpty(nameFilter);
        cmd.CommandText = p switch
        {
            DataSourceProvider.Postgres => hasFilter
                ? """
                  SELECT p.proname,
                         CASE WHEN p.prokind = 'p' THEN 'procedure' ELSE 'function' END,
                         CASE WHEN p.prokind = 'f' AND (p.proretset OR t.typtype = 'c') THEN TRUE ELSE FALSE END
                  FROM pg_proc p
                  JOIN pg_namespace n ON n.oid = p.pronamespace
                  JOIN pg_type t ON t.oid = p.prorettype
                  WHERE n.nspname = current_schema()
                    AND p.prokind IN ('f', 'p')
                    AND t.typname <> 'trigger'
                    AND p.proname ILIKE @f
                  ORDER BY 1
                  """
                : """
                  SELECT p.proname,
                         CASE WHEN p.prokind = 'p' THEN 'procedure' ELSE 'function' END,
                         CASE WHEN p.prokind = 'f' AND (p.proretset OR t.typtype = 'c') THEN TRUE ELSE FALSE END
                  FROM pg_proc p
                  JOIN pg_namespace n ON n.oid = p.pronamespace
                  JOIN pg_type t ON t.oid = p.prorettype
                  WHERE n.nspname = current_schema()
                    AND p.prokind IN ('f', 'p')
                    AND t.typname <> 'trigger'
                  ORDER BY 1
                  """,
            DataSourceProvider.MySql => hasFilter
                ? """
                  SELECT ROUTINE_NAME, LOWER(ROUTINE_TYPE),
                         CASE WHEN ROUTINE_TYPE = 'FUNCTION' THEN 1 ELSE 0 END
                  FROM information_schema.ROUTINES
                  WHERE ROUTINE_SCHEMA = DATABASE()
                    AND ROUTINE_TYPE IN ('PROCEDURE', 'FUNCTION')
                    AND ROUTINE_NAME LIKE @f
                  ORDER BY 1
                  """
                : """
                  SELECT ROUTINE_NAME, LOWER(ROUTINE_TYPE),
                         CASE WHEN ROUTINE_TYPE = 'FUNCTION' THEN 1 ELSE 0 END
                  FROM information_schema.ROUTINES
                  WHERE ROUTINE_SCHEMA = DATABASE()
                    AND ROUTINE_TYPE IN ('PROCEDURE', 'FUNCTION')
                  ORDER BY 1
                  """,
            DataSourceProvider.SqlServer => hasFilter
                ? """
                  SELECT o.name,
                         CASE WHEN o.type IN ('IF', 'TF', 'FT') THEN 'function' ELSE 'procedure' END,
                         CASE WHEN o.type IN ('IF', 'TF', 'FT') THEN 1 ELSE 0 END
                  FROM sys.objects o
                  INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                  WHERE s.name = SCHEMA_NAME()
                    AND o.is_ms_shipped = 0
                    AND o.type IN ('P', 'PC', 'IF', 'TF', 'FT')
                    AND o.name LIKE @f
                  ORDER BY 1
                  """
                : """
                  SELECT o.name,
                         CASE WHEN o.type IN ('IF', 'TF', 'FT') THEN 'function' ELSE 'procedure' END,
                         CASE WHEN o.type IN ('IF', 'TF', 'FT') THEN 1 ELSE 0 END
                  FROM sys.objects o
                  INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                  WHERE s.name = SCHEMA_NAME()
                    AND o.is_ms_shipped = 0
                    AND o.type IN ('P', 'PC', 'IF', 'TF', 'FT')
                  ORDER BY 1
                  """,
            DataSourceProvider.Oracle => hasFilter
                ? """
                  SELECT OBJECT_NAME,
                         CASE WHEN OBJECT_TYPE = 'FUNCTION' THEN 'function' ELSE 'procedure' END,
                         CASE WHEN OBJECT_TYPE = 'FUNCTION' THEN 1 ELSE 0 END
                  FROM USER_PROCEDURES
                  WHERE PROCEDURE_NAME IS NULL
                    AND OBJECT_TYPE IN ('PROCEDURE', 'FUNCTION')
                    AND OBJECT_NAME LIKE :f
                  ORDER BY 1
                  """
                : """
                  SELECT OBJECT_NAME,
                         CASE WHEN OBJECT_TYPE = 'FUNCTION' THEN 'function' ELSE 'procedure' END,
                         CASE WHEN OBJECT_TYPE = 'FUNCTION' THEN 1 ELSE 0 END
                  FROM USER_PROCEDURES
                  WHERE PROCEDURE_NAME IS NULL
                    AND OBJECT_TYPE IN ('PROCEDURE', 'FUNCTION')
                  ORDER BY 1
                  """,
            _ => throw new UserFriendlyException($"Unsupported provider: {p}")
        };
        if (hasFilter)
        {
            var pattern = $"%{SanitizeLikeLiteral(nameFilter!)}%";
            if (p == DataSourceProvider.Oracle)
            {
                pattern = pattern.ToUpperInvariant();
            }

            AddParam(cmd, "f", pattern);
        }

        var list = new List<SqlCatalogObject>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = ReadString(reader, 0);
            if (!SqlIdentifier.IsValid(name) || !seen.Add(name))
            {
                continue;
            }

            var routineKind = ReadString(reader, 1).Trim().ToLowerInvariant();
            if (routineKind is not (QueryRoutineKind.Procedure or QueryRoutineKind.Function))
            {
                routineKind = QueryRoutineKind.Procedure;
            }

            list.Add(new SqlCatalogObject
            {
                ObjectName = name,
                DisplayName = name,
                Kind = QueryObjectKind.Procedure,
                RoutineKind = routineKind,
                CanSelectFrom = ReadBool(reader, 2)
            });
        }

        return list;
    }

    /// <summary>去掉 LIKE 通配符，按字面子串匹配。</summary>
    private static string SanitizeLikeLiteral(string value) =>
        value.Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

    private static async Task<List<TableColumn>> ReadColumnsAsync(
        DbConnection conn,
        string provider,
        string objectName,
        SqlCatalogObject meta,
        CancellationToken cancellationToken
    )
    {
        var p = SqlDialect.NormalizeProvider(provider);
        if (meta.Kind == QueryObjectKind.Procedure && p == DataSourceProvider.SqlServer && !meta.CanSelectFrom)
        {
            var described = await DescribeSqlServerResultSetAsync(conn, objectName, cancellationToken);
            if (described.Count > 0)
            {
                return described;
            }
        }

        if (meta.Kind == QueryObjectKind.Procedure && p == DataSourceProvider.Postgres && meta.CanSelectFrom)
        {
            var pgCols = await ReadPostgresFunctionColumnsAsync(conn, objectName, cancellationToken);
            if (pgCols.Count > 0)
            {
                return pgCols;
            }
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        switch (p)
        {
            case DataSourceProvider.Postgres:
                cmd.CommandText =
                    """
                    SELECT column_name, data_type, character_maximum_length, numeric_precision, numeric_scale, is_nullable
                    FROM information_schema.columns
                    WHERE table_schema = current_schema() AND table_name = @n
                    ORDER BY ordinal_position
                    """;
                AddParam(cmd, "n", objectName);
                break;
            case DataSourceProvider.MySql:
                cmd.CommandText =
                    """
                    SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, COLUMN_COMMENT
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @n
                    ORDER BY ORDINAL_POSITION
                    """;
                AddParam(cmd, "n", objectName);
                break;
            case DataSourceProvider.SqlServer:
                cmd.CommandText =
                    """
                    SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = SCHEMA_NAME() AND TABLE_NAME = @n
                    ORDER BY ORDINAL_POSITION
                    """;
                AddParam(cmd, "n", objectName);
                break;
            case DataSourceProvider.Oracle:
                cmd.CommandText =
                    """
                    SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE, NULLABLE
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME = :n
                    ORDER BY COLUMN_ID
                    """;
                AddParam(cmd, "n", objectName.ToUpperInvariant());
                break;
            default:
                throw new UserFriendlyException($"Unsupported provider: {p}");
        }

        var physical = await ReadPhysicalColumnsAsync(cmd, p, cancellationToken);
        var columns = physical
            .Where(x => SqlIdentifier.IsQuotable(x.Name))
            .Select(SqlPhysicalTypeMapper.ToColumn)
            .ToList();
        if (columns.Count == 0 && p == DataSourceProvider.SqlServer && meta.Kind == QueryObjectKind.View)
        {
            return await ReadSqlServerViewColumnsAsync(conn, objectName, cancellationToken);
        }

        return columns;
    }

    private static async Task<List<TableColumn>> ReadSqlServerViewColumnsAsync(
        DbConnection conn,
        string objectName,
        CancellationToken cancellationToken
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        cmd.CommandText =
            """
            SELECT c.name,
                   ty.name,
                   CASE WHEN ty.name IN (N'nchar', N'nvarchar') AND c.max_length > 0 THEN c.max_length / 2 ELSE c.max_length END,
                   c.precision,
                   c.scale,
                   CASE WHEN c.is_nullable = 1 THEN N'YES' ELSE N'NO' END
            FROM sys.views v
            INNER JOIN sys.columns c ON c.object_id = v.object_id
            INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            WHERE v.name = @n
            ORDER BY CASE WHEN SCHEMA_NAME(v.schema_id) = SCHEMA_NAME() THEN 0 ELSE 1 END, c.column_id
            """;
        AddParam(cmd, "n", objectName);
        var physical = await ReadPhysicalColumnsAsync(cmd, DataSourceProvider.SqlServer, cancellationToken);
        return physical
            .Where(x => SqlIdentifier.IsQuotable(x.Name))
            .Select(SqlPhysicalTypeMapper.ToColumn)
            .ToList();
    }

    private static async Task<List<TableColumn>> ReadPostgresFunctionColumnsAsync(
        DbConnection conn,
        string objectName,
        CancellationToken cancellationToken
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        cmd.CommandText =
            """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod)
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            JOIN pg_type t ON t.oid = p.prorettype
            JOIN pg_attribute a ON a.attrelid = t.typrelid
            WHERE n.nspname = current_schema()
              AND p.proname = @n
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY a.attnum
            """;
        AddParam(cmd, "n", objectName);
        var list = new List<TableColumn>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = ReadString(reader, 0);
            if (!SqlIdentifier.IsValid(name))
            {
                continue;
            }

            list.Add(new TableColumn
            {
                Name = name,
                DisplayName = name,
                PlatformType = SqlPhysicalTypeMapper.ToPlatformType(ReadString(reader, 1)),
                Nullable = true,
                Origin = TableOrigin.User,
                AppliedName = name
            });
        }

        return list;
    }

    private static async Task<List<TableColumn>> DescribeSqlServerResultSetAsync(
        DbConnection conn,
        string objectName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
            cmd.CommandText =
                """
                SELECT name, system_type_name, is_nullable
                FROM sys.dm_exec_describe_first_result_set(@sql, NULL, 0)
                WHERE name IS NOT NULL
                """;
            AddParam(cmd, "sql", "EXEC " + Quote(DataSourceProvider.SqlServer, objectName));
            var list = new List<TableColumn>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = ReadString(reader, 0);
                if (!SqlIdentifier.IsValid(name))
                {
                    continue;
                }

                list.Add(new TableColumn
                {
                    Name = name,
                    DisplayName = name,
                    PlatformType = SqlPhysicalTypeMapper.ToPlatformType(ReadString(reader, 1)),
                    Nullable = ReadBool(reader, 2),
                    Origin = TableOrigin.User,
                    AppliedName = name
                });
            }

            return list;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static async Task<List<QueryObjectParameter>> ReadParametersAsync(
        DbConnection conn,
        string provider,
        string objectName,
        CancellationToken cancellationToken
    )
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        var p = SqlDialect.NormalizeProvider(provider);
        switch (p)
        {
            case DataSourceProvider.Postgres:
                cmd.CommandText =
                    """
                    SELECT p.parameter_name, p.data_type, p.parameter_mode, p.ordinal_position
                    FROM information_schema.parameters p
                    JOIN information_schema.routines r
                      ON r.specific_name = p.specific_name AND r.specific_schema = p.specific_schema
                    WHERE p.specific_schema = current_schema()
                      AND r.routine_name = @n
                      AND p.parameter_name IS NOT NULL
                    ORDER BY p.ordinal_position
                    """;
                AddParam(cmd, "n", objectName);
                break;
            case DataSourceProvider.MySql:
                cmd.CommandText =
                    """
                    SELECT PARAMETER_NAME, DATA_TYPE, PARAMETER_MODE, ORDINAL_POSITION
                    FROM information_schema.PARAMETERS
                    WHERE SPECIFIC_SCHEMA = DATABASE() AND SPECIFIC_NAME = @n
                      AND PARAMETER_NAME IS NOT NULL AND PARAMETER_NAME <> ''
                    ORDER BY ORDINAL_POSITION
                    """;
                AddParam(cmd, "n", objectName);
                break;
            case DataSourceProvider.SqlServer:
                cmd.CommandText =
                    """
                    SELECT REPLACE(p.name, '@', ''), ty.name, CASE WHEN p.is_output = 1 THEN 'out' ELSE 'in' END, p.parameter_id
                    FROM sys.parameters p
                    INNER JOIN sys.objects o ON p.object_id = o.object_id
                    INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
                    INNER JOIN sys.types ty ON p.user_type_id = ty.user_type_id
                    WHERE s.name = SCHEMA_NAME() AND o.name = @n AND p.name IS NOT NULL AND p.name <> ''
                    ORDER BY p.parameter_id
                    """;
                AddParam(cmd, "n", objectName);
                break;
            case DataSourceProvider.Oracle:
                cmd.CommandText =
                    """
                    SELECT ARGUMENT_NAME, DATA_TYPE, IN_OUT, POSITION
                    FROM USER_ARGUMENTS
                    WHERE OBJECT_NAME = :n AND PACKAGE_NAME IS NULL AND ARGUMENT_NAME IS NOT NULL
                    ORDER BY POSITION
                    """;
                AddParam(cmd, "n", objectName.ToUpperInvariant());
                break;
            default:
                throw new UserFriendlyException($"Unsupported provider: {p}");
        }

        var list = new List<QueryObjectParameter>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = ReadString(reader, 0).TrimStart('@', ':');
            if (!SqlIdentifier.IsValid(name) || !seen.Add(name))
            {
                continue;
            }

            list.Add(new QueryObjectParameter
            {
                Name = name,
                DisplayName = name,
                PlatformType = SqlPhysicalTypeMapper.ToPlatformType(ReadString(reader, 1)),
                Direction = QueryParameterDirection.Normalize(ReadString(reader, 2)),
                Ordinal = ReadInt(reader, 3) ?? list.Count + 1,
                Nullable = true
            });
        }

        return list;
    }

    private static async Task<List<PhysicalColumn>> ReadPhysicalColumnsAsync(
        DbCommand cmd,
        string provider,
        CancellationToken cancellationToken
    )
    {
        var list = new List<PhysicalColumn>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(provider switch
            {
                DataSourceProvider.MySql => new PhysicalColumn
                {
                    Name = ReadString(reader, 0),
                    DataType = ReadString(reader, 1),
                    ColumnType = ReadString(reader, 2),
                    CharLength = ReadInt(reader, 3),
                    Precision = ReadInt(reader, 4),
                    Scale = ReadInt(reader, 5),
                    Nullable = IsYes(ReadString(reader, 6)),
                    Comment = ReadString(reader, 7)
                },
                DataSourceProvider.Oracle => new PhysicalColumn
                {
                    Name = ReadString(reader, 0),
                    DataType = ReadString(reader, 1),
                    CharLength = ReadInt(reader, 2),
                    Precision = ReadInt(reader, 3),
                    Scale = ReadInt(reader, 4),
                    Nullable = IsYes(ReadString(reader, 5))
                },
                _ => new PhysicalColumn
                {
                    Name = ReadString(reader, 0),
                    DataType = ReadString(reader, 1),
                    CharLength = ReadInt(reader, 2),
                    Precision = ReadInt(reader, 3),
                    Scale = ReadInt(reader, 4),
                    Nullable = IsYes(ReadString(reader, 5))
                }
            });
        }

        return list;
    }

    private static string Quote(string provider, string name) => SqlDialect.QuoteIdent(provider, name);

    private static void AddParam(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string ReadString(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int? ReadInt(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static bool ReadBool(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            bool b => b,
            byte or short or int or long => Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0,
            _ => IsYes(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }

    private static bool IsYes(string? value) =>
        value is not null &&
        (value.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("1", StringComparison.OrdinalIgnoreCase));
}
