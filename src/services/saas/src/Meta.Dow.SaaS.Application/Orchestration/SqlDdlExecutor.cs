using System.Data.Common;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public class DdlPreviewItem
{
    public string Kind { get; set; } = null!;

    public string Sql { get; set; } = null!;

    public bool Destructive { get; set; }
}

public interface ISqlDdlExecutor
{
    List<DdlPreviewItem> Preview(
        DataSource dataSource,
        TableDefinition table,
        IReadOnlyList<PhysicalColumn> physicalColumns,
        HashSet<string>? existingIndexes = null
    );

    Task ApplyAsync(
        DataSource dataSource,
        IReadOnlyList<DdlPreviewItem> statements,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<PhysicalColumn>> GetPhysicalColumnsAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    );

    Task<HashSet<string>> GetExistingColumnsAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    );

    Task<HashSet<string>> GetExistingIndexesAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    );

    Task<bool> TableExistsAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    );
}

public class SqlDdlExecutor : ISqlDdlExecutor, ITransientDependency
{
    private readonly ILogger<SqlDdlExecutor> _logger;

    public SqlDdlExecutor(ILogger<SqlDdlExecutor> logger)
    {
        _logger = logger;
    }

    public List<DdlPreviewItem> Preview(
        DataSource dataSource,
        TableDefinition table,
        IReadOnlyList<PhysicalColumn> physicalColumns,
        HashSet<string>? existingIndexes = null
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(physicalColumns);

        if (!dataSource.AllowsWrite())
        {
            throw new UserFriendlyException($"Data source '{dataSource.Code}' is not writable.");
        }

        var provider = dataSource.Provider;
        var items = new List<DdlPreviewItem>();
        if (physicalColumns.Count == 0)
        {
            items.Add(new DdlPreviewItem
            {
                Kind = "createTable",
                Sql = SqlDialect.BuildCreateTable(provider, table)
            });
            foreach (var sql in SqlDialect.BuildCreateIndexes(provider, table))
            {
                items.Add(new DdlPreviewItem { Kind = "createIndex", Sql = sql });
            }

            return items;
        }

        var physicalByName = physicalColumns.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var dropIndexes = new List<DdlPreviewItem>();
        var renameCols = new List<DdlPreviewItem>();
        var alterCols = new List<DdlPreviewItem>();
        var addCols = new List<DdlPreviewItem>();
        var dropCols = new List<DdlPreviewItem>();
        var createIndexes = new List<DdlPreviewItem>();
        var droppedIndexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var knownIndexes = existingIndexes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var desiredIndexNames = table.Indexes
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (table.Indexes.Any(x => x.IsPrimary))
        {
            desiredIndexNames.Add("PRIMARY");
        }

        foreach (var existing in knownIndexes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (desiredIndexNames.Contains(existing) || IsProtectedIndex(existing))
            {
                continue;
            }

            dropIndexes.Add(DropIndexItem(provider, table.TableName, existing));
            droppedIndexNames.Add(existing);
        }

        foreach (var col in table.Columns)
        {
            var from = ResolvePhysicalName(col, physicalByName);
            if (from == null)
            {
                addCols.Add(new DdlPreviewItem
                {
                    Kind = "addColumn",
                    Sql = SqlDialect.BuildAddColumn(provider, table.TableName, col)
                });
                continue;
            }

            var physical = physicalByName[from];
            var rename = !from.Equals(col.Name, StringComparison.OrdinalIgnoreCase);
            if (AbpConventionColumns.IsConvention(from) || AbpConventionColumns.IsConvention(col.Name))
            {
                if (rename)
                {
                    throw new UserFriendlyException($"Convention column '{from}' cannot be renamed.");
                }

                continue;
            }

            if (rename && physicalByName.ContainsKey(col.Name))
            {
                throw new UserFriendlyException(
                    $"Cannot rename '{from}' to '{col.Name}': target column already exists."
                );
            }

            var alter = SqlDialect.NeedsColumnAlter(provider, col, physical);
            if (!rename && !alter)
            {
                continue;
            }

            if (rename && !alter)
            {
                renameCols.Add(new DdlPreviewItem
                {
                    Kind = "renameColumn",
                    Sql = SqlDialect.BuildRenameColumn(provider, table.TableName, from, col.Name),
                    Destructive = true
                });
                continue;
            }

            var destructive = rename || SqlDialect.IsColumnAlterDestructive(provider, col, physical);
            foreach (var sql in SqlDialect.BuildAlterColumn(provider, table.TableName, col, physical))
            {
                alterCols.Add(new DdlPreviewItem
                {
                    Kind = "alterColumn",
                    Sql = sql,
                    Destructive = destructive
                });
            }
        }

        foreach (var dbCol in physicalByName.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (AbpConventionColumns.IsConvention(dbCol) || IsKeptColumn(table, dbCol))
            {
                continue;
            }

            dropCols.Add(new DdlPreviewItem
            {
                Kind = "dropColumn",
                Sql = SqlDialect.BuildDropColumn(provider, table.TableName, dbCol),
                Destructive = true
            });
        }

        if (dropCols.Count > 0)
        {
            foreach (var existing in knownIndexes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (IsProtectedIndex(existing) || droppedIndexNames.Contains(existing))
                {
                    continue;
                }

                dropIndexes.Add(DropIndexItem(provider, table.TableName, existing));
                droppedIndexNames.Add(existing);
            }
        }

        foreach (var sql in SqlDialect.BuildCreateIndexes(provider, table))
        {
            var ix = table.Indexes.FirstOrDefault(x =>
                !x.IsPrimary && sql.Contains(SqlDialect.QuoteIdent(provider, x.Name), StringComparison.Ordinal)
            );
            if (ix != null && knownIndexes.Contains(ix.Name) && !droppedIndexNames.Contains(ix.Name))
            {
                continue;
            }

            createIndexes.Add(new DdlPreviewItem { Kind = "createIndex", Sql = sql });
        }

        items.AddRange(dropIndexes);
        items.AddRange(renameCols);
        items.AddRange(alterCols);
        items.AddRange(addCols);
        items.AddRange(dropCols);
        items.AddRange(createIndexes);
        return items;
    }

    public async Task ApplyAsync(
        DataSource dataSource,
        IReadOnlyList<DdlPreviewItem> statements,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        if (statements.Count == 0)
        {
            return;
        }

        if (statements.Any(x => x.Kind is "dropTable" or "truncate"))
        {
            throw new UserFriendlyException("DROP TABLE / TRUNCATE is disabled.");
        }

        foreach (var item in statements)
        {
            EnsureSafeDdl(item.Sql);
        }

        await using var conn = SqlDialect.CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in statements)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = item.Sql;
                cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation(
                    "[TableDdl] ds={DataSource} kind={Kind} sql={Sql}",
                    dataSource.Code,
                    item.Kind,
                    item.Sql
                );
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PhysicalColumn>> GetPhysicalColumnsAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    )
    {
        if (!await TableExistsAsync(dataSource, tableName, cancellationToken))
        {
            return [];
        }

        await using var conn = SqlDialect.CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        var provider = SqlDialect.NormalizeProvider(dataSource.Provider);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        switch (provider)
        {
            case DataSourceProvider.Postgres:
                cmd.CommandText =
                    """
                    SELECT column_name, data_type, character_maximum_length, numeric_precision, numeric_scale, is_nullable
                    FROM information_schema.columns
                    WHERE table_schema = current_schema() AND table_name = @n
                    """;
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.MySql:
                cmd.CommandText =
                    """
                    SELECT COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE, COLUMN_COMMENT
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @n
                    """;
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.SqlServer:
                cmd.CommandText =
                    """
                    SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = SCHEMA_NAME() AND TABLE_NAME = @n
                    """;
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.Oracle:
                cmd.CommandText =
                    """
                    SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, DATA_PRECISION, DATA_SCALE, NULLABLE
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME = :n
                    """;
                AddParam(cmd, "n", tableName.ToUpperInvariant());
                break;
            default:
                throw new UserFriendlyException($"Unsupported provider: {provider}");
        }

        var list = new List<PhysicalColumn>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadPhysicalColumn(provider, reader));
        }

        return list;
    }

    public async Task<HashSet<string>> GetExistingColumnsAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    )
    {
        var cols = await GetPhysicalColumnsAsync(dataSource, tableName, cancellationToken);
        return cols.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HashSet<string>> GetExistingIndexesAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    )
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!await TableExistsAsync(dataSource, tableName, cancellationToken))
        {
            return names;
        }

        await using var conn = SqlDialect.CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        var provider = SqlDialect.NormalizeProvider(dataSource.Provider);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        switch (provider)
        {
            case DataSourceProvider.Postgres:
                cmd.CommandText =
                    "SELECT indexname FROM pg_indexes WHERE schemaname = current_schema() AND tablename = @n";
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.MySql:
                cmd.CommandText =
                    "SELECT DISTINCT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @n";
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.SqlServer:
                cmd.CommandText =
                    """
                    SELECT i.name
                    FROM sys.indexes i
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE t.name = @n AND s.name = SCHEMA_NAME() AND i.name IS NOT NULL
                    """;
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.Oracle:
                cmd.CommandText = "SELECT INDEX_NAME FROM USER_INDEXES WHERE TABLE_NAME = :n";
                AddParam(cmd, "n", tableName.ToUpperInvariant());
                break;
            default:
                throw new UserFriendlyException($"Unsupported provider: {provider}");
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async Task<bool> TableExistsAsync(
        DataSource dataSource,
        string tableName,
        CancellationToken cancellationToken = default
    )
    {
        await using var conn = SqlDialect.CreateConnection(dataSource);
        await conn.OpenAsync(cancellationToken);
        var provider = SqlDialect.NormalizeProvider(dataSource.Provider);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds;
        switch (provider)
        {
            case DataSourceProvider.Postgres:
                cmd.CommandText =
                    "SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @n";
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.MySql:
                cmd.CommandText =
                    "SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @n";
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.SqlServer:
                cmd.CommandText =
                    "SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = SCHEMA_NAME() AND TABLE_NAME = @n";
                AddParam(cmd, "n", tableName);
                break;
            case DataSourceProvider.Oracle:
                cmd.CommandText = "SELECT 1 FROM USER_TABLES WHERE TABLE_NAME = :n";
                AddParam(cmd, "n", tableName.ToUpperInvariant());
                break;
            default:
                throw new UserFriendlyException($"Unsupported provider: {provider}");
        }

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value;
    }

    private static bool IsProtectedIndex(string name)
    {
        return name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("PK_", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolvePhysicalName(
        TableColumn column,
        IReadOnlyDictionary<string, PhysicalColumn> existing
    )
    {
        if (!string.IsNullOrWhiteSpace(column.AppliedName) && existing.ContainsKey(column.AppliedName))
        {
            return existing[column.AppliedName].Name;
        }

        return existing.TryGetValue(column.Name, out var physical) ? physical.Name : null;
    }

    private static bool IsKeptColumn(TableDefinition table, string dbColumn)
    {
        return table.Columns.Any(c =>
            c.Name.Equals(dbColumn, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(c.AppliedName) &&
             c.AppliedName.Equals(dbColumn, StringComparison.OrdinalIgnoreCase)));
    }

    private static DdlPreviewItem DropIndexItem(string provider, string tableName, string indexName)
    {
        return new DdlPreviewItem
        {
            Kind = "dropIndex",
            Sql = SqlDialect.BuildDropIndex(provider, tableName, indexName),
            Destructive = true
        };
    }

    private static void EnsureSafeDdl(string sql)
    {
        var text = sql.TrimStart();
        var head = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
        if (text.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException("DROP TABLE and TRUNCATE are not allowed.");
        }

        if (head is "CREATE" or "ALTER")
        {
            return;
        }

        if (head == "DROP")
        {
            if (!text.StartsWith("DROP INDEX", StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException("Only DROP INDEX is allowed as a standalone DROP.");
            }

            return;
        }

        if (head == "EXEC" &&
            text.StartsWith("EXEC sp_rename", StringComparison.OrdinalIgnoreCase) &&
            text.Contains(", N'COLUMN'", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new UserFriendlyException("Only CREATE/ALTER/DROP INDEX/sp_rename COLUMN DDL is allowed.");
    }

    private static PhysicalColumn ReadPhysicalColumn(string provider, DbDataReader reader)
    {
        return provider switch
        {
            DataSourceProvider.MySql => new PhysicalColumn
            {
                Name = ReadString(reader, 0),
                DataType = ReadString(reader, 1),
                ColumnType = ReadString(reader, 2),
                CharLength = ReadInt(reader, 3),
                Precision = ReadInt(reader, 4),
                Scale = ReadInt(reader, 5),
                Nullable = IsNullableFlag(ReadString(reader, 6)),
                Comment = ReadString(reader, 7)
            },
            DataSourceProvider.Oracle => new PhysicalColumn
            {
                Name = ReadString(reader, 0),
                DataType = ReadString(reader, 1),
                CharLength = ReadInt(reader, 2),
                Precision = ReadInt(reader, 3),
                Scale = ReadInt(reader, 4),
                Nullable = IsNullableFlag(ReadString(reader, 5))
            },
            _ => new PhysicalColumn
            {
                Name = ReadString(reader, 0),
                DataType = ReadString(reader, 1),
                CharLength = ReadInt(reader, 2),
                Precision = ReadInt(reader, 3),
                Scale = ReadInt(reader, 4),
                Nullable = IsNullableFlag(ReadString(reader, 5))
            }
        };
    }

    private static string ReadString(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int? ReadInt(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static bool IsNullableFlag(string value) =>
        value.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Y", StringComparison.OrdinalIgnoreCase);

    private static void AddParam(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
