using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public static class SqlDialect
{
    public static DbConnection CreateConnection(DataSource dataSource)
    {
        if (!DataSourceProvider.IsSql(dataSource.Provider))
        {
            throw new UserFriendlyException(
                $"Provider '{dataSource.Provider}' is not a SQL engine."
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

    public static string QuoteIdent(string provider, string name)
    {
        if (!SqlIdentifier.IsValid(name))
        {
            throw new UserFriendlyException($"Invalid SQL identifier: {name}");
        }

        return NormalizeProvider(provider) switch
        {
            DataSourceProvider.MySql => $"`{name}`",
            DataSourceProvider.SqlServer => $"[{name}]",
            DataSourceProvider.Oracle => $"\"{name.ToUpperInvariant()}\"",
            _ => $"\"{name}\""
        };
    }

    public static string ParamName(string provider, string name)
    {
        return NormalizeProvider(provider) switch
        {
            DataSourceProvider.Oracle => ":" + name,
            _ => "@" + name
        };
    }

    public static string MapColumnType(string provider, TableColumn column)
    {
        var p = NormalizeProvider(provider);
        var type = TablePlatformType.Normalize(column.PlatformType);
        var length = column.Length is > 0 and <= 4000 ? column.Length.Value : 256;
        var precision = column.Precision is > 0 ? column.Precision.Value : 18;
        var scale = column.Scale is >= 0 ? column.Scale.Value : 2;

        return p switch
        {
            DataSourceProvider.Postgres => type switch
            {
                TablePlatformType.Guid => "uuid",
                TablePlatformType.String or TablePlatformType.Enum => $"varchar({length})",
                TablePlatformType.Text => "text",
                TablePlatformType.Int => "integer",
                TablePlatformType.Long => "bigint",
                TablePlatformType.Decimal => $"numeric({precision},{scale})",
                TablePlatformType.Boolean => "boolean",
                TablePlatformType.Date => "date",
                TablePlatformType.DateTime => "timestamptz",
                TablePlatformType.Json => "jsonb",
                _ => "text"
            },
            DataSourceProvider.MySql => type switch
            {
                TablePlatformType.Guid => "char(36)",
                TablePlatformType.String or TablePlatformType.Enum => $"varchar({length})",
                TablePlatformType.Text => "text",
                TablePlatformType.Int => "int",
                TablePlatformType.Long => "bigint",
                TablePlatformType.Decimal => $"decimal({precision},{scale})",
                TablePlatformType.Boolean => "tinyint(1)",
                TablePlatformType.Date => "date",
                TablePlatformType.DateTime => "datetime(3)",
                TablePlatformType.Json => "json",
                _ => "text"
            },
            DataSourceProvider.SqlServer => type switch
            {
                TablePlatformType.Guid => "uniqueidentifier",
                TablePlatformType.String or TablePlatformType.Enum => $"nvarchar({length})",
                TablePlatformType.Text => "nvarchar(max)",
                TablePlatformType.Int => "int",
                TablePlatformType.Long => "bigint",
                TablePlatformType.Decimal => $"decimal({precision},{scale})",
                TablePlatformType.Boolean => "bit",
                TablePlatformType.Date => "date",
                TablePlatformType.DateTime => "datetimeoffset",
                TablePlatformType.Json => "nvarchar(max)",
                _ => "nvarchar(max)"
            },
            DataSourceProvider.Oracle => type switch
            {
                TablePlatformType.Guid => "RAW(16)",
                TablePlatformType.String or TablePlatformType.Enum => $"VARCHAR2({length})",
                TablePlatformType.Text => "CLOB",
                TablePlatformType.Int => "NUMBER(10)",
                TablePlatformType.Long => "NUMBER(19)",
                TablePlatformType.Decimal => $"NUMBER({precision},{scale})",
                TablePlatformType.Boolean => "NUMBER(1)",
                TablePlatformType.Date or TablePlatformType.DateTime => "TIMESTAMP",
                TablePlatformType.Json => "CLOB",
                _ => "VARCHAR2(4000)"
            },
            _ => "text"
        };
    }

    public static string? MapDefault(string provider, TableColumn column)
    {
        var p = NormalizeProvider(provider);
        if (column.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) &&
            column.PlatformType == TablePlatformType.Guid)
        {
            return p switch
            {
                DataSourceProvider.Postgres => "gen_random_uuid()",
                DataSourceProvider.SqlServer => "NEWID()",
                _ => null
            };
        }

        if (column.PlatformType == TablePlatformType.Boolean &&
            string.Equals(column.Default, "false", StringComparison.OrdinalIgnoreCase))
        {
            return p switch
            {
                DataSourceProvider.Postgres => "FALSE",
                DataSourceProvider.SqlServer => "0",
                DataSourceProvider.MySql => "0",
                DataSourceProvider.Oracle => "0",
                _ => "FALSE"
            };
        }

        return null;
    }

    public static string BuildCreateTable(string provider, TableDefinition table)
    {
        var sb = new StringBuilder();
        var t = QuoteIdent(provider, table.TableName);
        sb.Append(CultureInfo.InvariantCulture, $"CREATE TABLE {t} (");
        var parts = new List<string>();
        foreach (var col in table.Columns)
        {
            var line = $"{QuoteIdent(provider, col.Name)} {MapColumnType(provider, col)}";
            if (!col.Nullable)
            {
                line += " NOT NULL";
            }

            var def = MapDefault(provider, col);
            if (!string.IsNullOrWhiteSpace(def))
            {
                line += $" DEFAULT {def}";
            }

            parts.Add(line);
        }

        var pk = table.Indexes.FirstOrDefault(x => x.IsPrimary);
        if (pk != null)
        {
            var pkCols = string.Join(", ", pk.Columns.Select(c => QuoteIdent(provider, c.Name)));
            parts.Add($"CONSTRAINT {QuoteIdent(provider, pk.Name)} PRIMARY KEY ({pkCols})");
        }

        sb.Append(string.Join(", ", parts));
        sb.Append(')');
        return sb.ToString();
    }

    public static List<string> BuildCreateIndexes(string provider, TableDefinition table)
    {
        var list = new List<string>();
        var t = QuoteIdent(provider, table.TableName);
        foreach (var ix in table.Indexes.Where(x => !x.IsPrimary))
        {
            var cols = string.Join(
                ", ",
                ix.Columns.Select(c =>
                {
                    var ident = QuoteIdent(provider, c.Name);
                    return c.Descending ? $"{ident} DESC" : ident;
                })
            );
            var unique = ix.Unique ? "UNIQUE " : "";
            list.Add($"CREATE {unique}INDEX {QuoteIdent(provider, ix.Name)} ON {t} ({cols})");
        }

        return list;
    }

    public static string BuildDropIndex(string provider, string tableName, string indexName)
    {
        var ix = QuoteIdent(provider, indexName);
        var t = QuoteIdent(provider, tableName);
        return NormalizeProvider(provider) switch
        {
            DataSourceProvider.Postgres => $"DROP INDEX IF EXISTS {ix}",
            DataSourceProvider.Oracle => $"DROP INDEX {ix}",
            _ => $"DROP INDEX {ix} ON {t}"
        };
    }

    public static string BuildColumnSpec(string provider, TableColumn column)
    {
        var spec = MapColumnType(provider, column);
        spec += column.Nullable ? " NULL" : " NOT NULL";
        var def = MapDefault(provider, column);
        if (!string.IsNullOrWhiteSpace(def))
        {
            spec += $" DEFAULT {def}";
        }

        if (NormalizeProvider(provider) == DataSourceProvider.MySql)
        {
            spec += $" COMMENT {SqlLiteral(column.Comment ?? string.Empty)}";
        }

        return spec;
    }

    public static string BuildAddColumn(string provider, string tableName, TableColumn column)
    {
        if (!column.Nullable)
        {
            var def = MapDefault(provider, column);
            if (string.IsNullOrWhiteSpace(def) && column.PlatformType != TablePlatformType.Boolean)
            {
                throw new UserFriendlyException(
                    $"Cannot add non-nullable column '{column.Name}' without a default."
                );
            }
        }

        var t = QuoteIdent(provider, tableName);
        var line = $"{QuoteIdent(provider, column.Name)} {BuildColumnSpec(provider, column)}";
        return NormalizeProvider(provider) switch
        {
            DataSourceProvider.SqlServer => $"ALTER TABLE {t} ADD {line}",
            DataSourceProvider.Oracle => $"ALTER TABLE {t} ADD ({line})",
            _ => $"ALTER TABLE {t} ADD COLUMN {line}"
        };
    }

    public static string BuildDropColumn(string provider, string tableName, string columnName)
    {
        var t = QuoteIdent(provider, tableName);
        var c = QuoteIdent(provider, columnName);
        return $"ALTER TABLE {t} DROP COLUMN {c}";
    }

    public static string BuildRenameColumn(string provider, string tableName, string fromName, string toName)
    {
        var t = QuoteIdent(provider, tableName);
        var from = QuoteIdent(provider, fromName);
        var to = QuoteIdent(provider, toName);
        return NormalizeProvider(provider) switch
        {
            DataSourceProvider.SqlServer =>
                $"EXEC sp_rename N'{tableName}.{fromName}', N'{toName}', N'COLUMN'",
            _ => $"ALTER TABLE {t} RENAME COLUMN {from} TO {to}"
        };
    }

    public static List<string> BuildAlterColumn(
        string provider,
        string tableName,
        TableColumn desired,
        PhysicalColumn physical
    )
    {
        var p = NormalizeProvider(provider);
        var t = QuoteIdent(provider, tableName);
        var rename = !physical.Name.Equals(desired.Name, StringComparison.OrdinalIgnoreCase);
        var list = new List<string>();

        switch (p)
        {
            case DataSourceProvider.MySql:
                list.Add(
                    rename
                        ? $"ALTER TABLE {t} CHANGE COLUMN {QuoteIdent(provider, physical.Name)} {QuoteIdent(provider, desired.Name)} {BuildColumnSpec(provider, desired)}"
                        : $"ALTER TABLE {t} MODIFY COLUMN {QuoteIdent(provider, desired.Name)} {BuildColumnSpec(provider, desired)}"
                );
                break;
            case DataSourceProvider.Postgres:
                if (rename)
                {
                    list.Add(BuildRenameColumn(provider, tableName, physical.Name, desired.Name));
                }

                var ident = QuoteIdent(provider, desired.Name);
                if (!ColumnTypeEquals(provider, desired, physical))
                {
                    list.Add($"ALTER TABLE {t} ALTER COLUMN {ident} TYPE {MapColumnType(provider, desired)}");
                }

                if (desired.Nullable != physical.Nullable)
                {
                    list.Add(
                        desired.Nullable
                            ? $"ALTER TABLE {t} ALTER COLUMN {ident} DROP NOT NULL"
                            : $"ALTER TABLE {t} ALTER COLUMN {ident} SET NOT NULL"
                    );
                }

                break;
            case DataSourceProvider.SqlServer:
                if (rename)
                {
                    list.Add(BuildRenameColumn(provider, tableName, physical.Name, desired.Name));
                }

                var nullSql = desired.Nullable ? "NULL" : "NOT NULL";
                list.Add(
                    $"ALTER TABLE {t} ALTER COLUMN {QuoteIdent(provider, desired.Name)} {MapColumnType(provider, desired)} {nullSql}"
                );
                break;
            case DataSourceProvider.Oracle:
                if (rename)
                {
                    list.Add(BuildRenameColumn(provider, tableName, physical.Name, desired.Name));
                }

                var oracleNull = desired.Nullable ? "NULL" : "NOT NULL";
                list.Add(
                    $"ALTER TABLE {t} MODIFY ({QuoteIdent(provider, desired.Name)} {MapColumnType(provider, desired)} {oracleNull})"
                );
                break;
        }

        return list;
    }

    public static bool ColumnTypeEquals(string provider, TableColumn desired, PhysicalColumn physical)
    {
        return MapColumnType(provider, desired)
            .Equals(CanonicalPhysicalType(provider, physical), StringComparison.OrdinalIgnoreCase);
    }

    public static bool NeedsColumnAlter(string provider, TableColumn desired, PhysicalColumn physical)
    {
        if (!ColumnTypeEquals(provider, desired, physical))
        {
            return true;
        }

        if (desired.Nullable != physical.Nullable)
        {
            return true;
        }

        return NormalizeProvider(provider) == DataSourceProvider.MySql &&
               !string.Equals(
                   desired.Comment?.Trim() ?? string.Empty,
                   physical.Comment?.Trim() ?? string.Empty,
                   StringComparison.Ordinal
               );
    }

    public static bool IsColumnAlterDestructive(string provider, TableColumn desired, PhysicalColumn physical)
    {
        if (!desired.Nullable && physical.Nullable)
        {
            return true;
        }

        if (ColumnTypeEquals(provider, desired, physical) || IsStringWidening(desired, physical))
        {
            return false;
        }

        return true;
    }

    public static string CanonicalPhysicalType(string provider, PhysicalColumn column)
    {
        var dt = (column.DataType ?? string.Empty).Trim().ToLowerInvariant();
        return NormalizeProvider(provider) switch
        {
            DataSourceProvider.MySql => dt switch
            {
                "varchar" => $"varchar({column.CharLength ?? 256})",
                "char" when column.CharLength == 36 => "char(36)",
                "char" => $"varchar({column.CharLength ?? 256})",
                "tinytext" or "text" or "mediumtext" or "longtext" => "text",
                "int" or "integer" or "smallint" or "mediumint" => "int",
                "bigint" => "bigint",
                "decimal" or "numeric" => $"decimal({column.Precision ?? 18},{column.Scale ?? 2})",
                "tinyint" => "tinyint(1)",
                "date" => "date",
                "datetime" or "timestamp" => "datetime(3)",
                "json" => "json",
                _ => (column.ColumnType ?? dt).ToLowerInvariant()
            },
            DataSourceProvider.Postgres => dt switch
            {
                "character varying" or "varchar" => $"varchar({column.CharLength ?? 256})",
                "uuid" => "uuid",
                "text" => "text",
                "integer" or "int" or "int4" => "integer",
                "bigint" or "int8" => "bigint",
                "numeric" or "decimal" => $"numeric({column.Precision ?? 18},{column.Scale ?? 2})",
                "boolean" or "bool" => "boolean",
                "date" => "date",
                "timestamp with time zone" or "timestamptz" => "timestamptz",
                "timestamp without time zone" or "timestamp" => "timestamptz",
                "jsonb" or "json" => "jsonb",
                _ => dt
            },
            DataSourceProvider.SqlServer => dt switch
            {
                "nvarchar" when column.CharLength is null or < 0 => "nvarchar(max)",
                "nvarchar" => $"nvarchar({column.CharLength ?? 256})",
                "varchar" => $"nvarchar({column.CharLength ?? 256})",
                "uniqueidentifier" => "uniqueidentifier",
                "int" => "int",
                "bigint" => "bigint",
                "decimal" or "numeric" => $"decimal({column.Precision ?? 18},{column.Scale ?? 2})",
                "bit" => "bit",
                "date" => "date",
                "datetimeoffset" or "datetime2" or "datetime" => "datetimeoffset",
                "ntext" or "text" => "nvarchar(max)",
                _ => dt
            },
            DataSourceProvider.Oracle => dt switch
            {
                "varchar2" or "nvarchar2" => $"VARCHAR2({column.CharLength ?? 256})",
                "raw" => "RAW(16)",
                "clob" or "nclob" => "CLOB",
                "number" when column.Scale is 0 && column.Precision is 10 => "NUMBER(10)",
                "number" when column.Scale is 0 && column.Precision is 19 => "NUMBER(19)",
                "number" when column.Scale is 0 or null && column.Precision is 1 => "NUMBER(1)",
                "number" => $"NUMBER({column.Precision ?? 18},{column.Scale ?? 2})",
                "date" or "timestamp" => "TIMESTAMP",
                _ => dt.ToUpperInvariant()
            },
            _ => dt
        };
    }

    public static string SqlLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    public static bool TryParseTemporal(string? text, out DateTime value, out bool dateOnly)
    {
        dateOnly = false;
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s.Length == 10 &&
            DateTime.TryParseExact(
                s,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            ))
        {
            dateOnly = true;
            value = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
            return true;
        }

        if (!DateTimeOffset.TryParse(
                s,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var dto))
        {
            return false;
        }

        var hasZone = s.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || HasNumericOffset(s);
        value = hasZone
            ? dto.UtcDateTime
            : DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);
        return true;
    }

    public static DateTime ToProviderDateTime(string? provider, DateTime value)
    {
        var boxed = ToProviderTemporal(provider, value, dateOnly: false);
        return boxed switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            _ => value
        };
    }

    public static object ToProviderTemporal(string? provider, DateTime value, bool dateOnly)
    {
        if (dateOnly)
        {
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
        }

        var p = NormalizeProvider(provider ?? string.Empty);
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Utc => value,
            _ => value
        };

        return p switch
        {
            DataSourceProvider.Postgres => DateTime.SpecifyKind(
                value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                    : utc,
                DateTimeKind.Utc),
            DataSourceProvider.SqlServer => value.Kind == DateTimeKind.Unspecified
                ? new DateTimeOffset(value, TimeSpan.Zero)
                : new DateTimeOffset(utc, TimeSpan.Zero),
            _ => DateTime.SpecifyKind(
                value.Kind == DateTimeKind.Unspecified ? value : utc,
                DateTimeKind.Unspecified)
        };
    }

    public static object ToProviderBoolean(string? provider, bool value)
    {
        return NormalizeProvider(provider ?? string.Empty) == DataSourceProvider.Oracle
            ? value ? 1 : 0
            : value;
    }

    public static object ToProviderGuid(string? provider, Guid value)
    {
        return NormalizeProvider(provider ?? string.Empty) switch
        {
            DataSourceProvider.Oracle => value.ToByteArray(),
            DataSourceProvider.Postgres or DataSourceProvider.SqlServer => value,
            _ => value.ToString()
        };
    }

    public static bool IsDateOnlyDbType(string? provider, string? dbType)
    {
        if (string.IsNullOrWhiteSpace(dbType))
        {
            return false;
        }

        if (NormalizeProvider(provider ?? string.Empty) == DataSourceProvider.Oracle)
        {
            return false;
        }

        return dbType.Trim().Equals("date", StringComparison.OrdinalIgnoreCase);
    }

    public static string IsEmptySql(string provider, string quotedColumn, string platformType)
    {
        if (IsNullOnlyEmpty(platformType) ||
            NormalizeProvider(provider) == DataSourceProvider.Oracle)
        {
            return $"{quotedColumn} IS NULL";
        }

        var empty = NormalizeProvider(provider) == DataSourceProvider.SqlServer ? "N''" : "''";
        return $"({quotedColumn} IS NULL OR {quotedColumn} = {empty})";
    }

    public static string IsNotEmptySql(string provider, string quotedColumn, string platformType)
    {
        if (IsNullOnlyEmpty(platformType) ||
            NormalizeProvider(provider) == DataSourceProvider.Oracle)
        {
            return $"{quotedColumn} IS NOT NULL";
        }

        var empty = NormalizeProvider(provider) == DataSourceProvider.SqlServer ? "N''" : "''";
        return $"({quotedColumn} IS NOT NULL AND {quotedColumn} <> {empty})";
    }

    private static bool IsNullOnlyEmpty(string platformType)
    {
        var type = TablePlatformType.Normalize(platformType);
        return type is TablePlatformType.Date or TablePlatformType.DateTime
            or TablePlatformType.Boolean or TablePlatformType.Guid
            or TablePlatformType.Int or TablePlatformType.Long
            or TablePlatformType.Decimal or TablePlatformType.Json;
    }

    private static bool HasNumericOffset(string text)
    {
        var tIndex = text.LastIndexOf('T');
        var start = tIndex >= 0 ? tIndex : 10;
        for (var i = text.Length - 1; i > start; i--)
        {
            if (text[i] is '+' or '-')
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeProvider(string provider) =>
        DataSourceProvider.Normalize(provider);

    private static bool IsStringWidening(TableColumn desired, PhysicalColumn physical)
    {
        var want = TablePlatformType.Normalize(desired.PlatformType);
        if (want is not (TablePlatformType.String or TablePlatformType.Enum or TablePlatformType.Text))
        {
            return false;
        }

        var have = (physical.DataType ?? string.Empty).Trim().ToLowerInvariant();
        if (have is not (
            "varchar" or "nvarchar" or "character varying" or "char" or "nchar" or
            "text" or "tinytext" or "mediumtext" or "longtext" or "clob" or "ntext" or "varchar2"))
        {
            return false;
        }

        if (want == TablePlatformType.Text)
        {
            return true;
        }

        if (have is "text" or "tinytext" or "mediumtext" or "longtext" or "clob" or "ntext")
        {
            return false;
        }

        var wantLen = desired.Length is > 0 and <= 4000 ? desired.Length.Value : 256;
        return wantLen >= (physical.CharLength ?? 0);
    }
}

public sealed class PhysicalColumn
{
    public string Name { get; init; } = null!;

    public string DataType { get; init; } = string.Empty;

    public string? ColumnType { get; init; }

    public int? CharLength { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public bool Nullable { get; init; }

    public string? Comment { get; init; }
}
