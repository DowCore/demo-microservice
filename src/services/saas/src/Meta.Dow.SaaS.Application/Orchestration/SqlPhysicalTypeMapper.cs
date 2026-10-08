namespace Meta.Dow.SaaS.Orchestration;

public static class SqlPhysicalTypeMapper
{
    public static string ToPlatformType(string? dataType)
    {
        var t = (dataType ?? "").Trim().ToLowerInvariant();
        var paren = t.IndexOf('(', StringComparison.Ordinal);
        if (paren > 0)
        {
            t = t[..paren];
        }

        return t switch
        {
            "uuid" or "uniqueidentifier" => TablePlatformType.Guid,
            "text" or "ntext" or "clob" or "nclob" or "longtext" or "mediumtext" or "tinytext" or "xml"
                => TablePlatformType.Text,
            "int" or "integer" or "int2" or "int4" or "smallint" or "tinyint" or "serial" or "smallserial"
                => TablePlatformType.Int,
            "bigint" or "int8" or "bigserial" => TablePlatformType.Long,
            "numeric" or "decimal" or "money" or "smallmoney" or "float" or "real" or "double"
                or "double precision" or "number" => TablePlatformType.Decimal,
            "bit" or "boolean" or "bool" => TablePlatformType.Boolean,
            "date" => TablePlatformType.Date,
            "timestamp" or "timestamptz" or "datetime" or "datetime2" or "datetimeoffset"
                or "smalldatetime" or "time" => TablePlatformType.DateTime,
            "json" or "jsonb" => TablePlatformType.Json,
            _ when t.StartsWith("timestamp", StringComparison.Ordinal) => TablePlatformType.DateTime,
            _ => TablePlatformType.String
        };
    }

    public static TableColumn ToColumn(PhysicalColumn physical)
    {
        var name = physical.Name?.Trim() ?? "";
        return new TableColumn
        {
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(physical.Comment) ? name : physical.Comment.Trim(),
            PlatformType = ToPlatformType(physical.ColumnType ?? physical.DataType),
            Length = physical.CharLength,
            Precision = physical.Precision,
            Scale = physical.Scale,
            Nullable = physical.Nullable,
            Comment = physical.Comment,
            Origin = TableOrigin.User,
            AppliedName = name
        };
    }
}
