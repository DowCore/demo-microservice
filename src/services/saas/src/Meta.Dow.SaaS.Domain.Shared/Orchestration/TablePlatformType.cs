using System;
using System.Linq;

namespace Meta.Dow.SaaS.Orchestration;

public static class TablePlatformType
{
    public const string Guid = "guid";
    public const string String = "string";
    public const string Text = "text";
    public const string Int = "int";
    public const string Long = "long";
    public const string Decimal = "decimal";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string DateTime = "datetime";
    public const string Json = "json";
    public const string Enum = "enum";

    public static readonly string[] All =
    [
        Guid, String, Text, Int, Long, Decimal, Boolean, Date, DateTime, Json, Enum
    ];

    public static bool IsSupported(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var t = type.Trim().ToLowerInvariant();
        return All.Contains(t);
    }

    public static string Normalize(string type) => type.Trim().ToLowerInvariant();
}

public static class TableOrigin
{
    public const string Convention = "convention";
    public const string User = "user";
}

public static class TableSyncState
{
    public const string Draft = "draft";
    public const string InSync = "inSync";
    public const string LocalAhead = "localAhead";
    public const string RemoteAhead = "remoteAhead";
    public const string Conflict = "conflict";
}

public static class AppResourceStatus
{
    public const int Draft = 0;
    public const int Published = 1;
}

public static class SystemResourceFlowKeys
{
    public const string Query = "sys.resource.query";
    public const string Get = "sys.resource.get";
    public const string Create = "sys.resource.create";
    public const string Update = "sys.resource.update";
    public const string Delete = "sys.resource.delete";

    public static readonly string[] All = [Query, Get, Create, Update, Delete];

    public static bool IsSystem(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        All.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class QueryObjectKind
{
    public const string View = "view";
    public const string Procedure = "procedure";

    public static bool IsSupported(string? kind)
    {
        var value = Normalize(kind);
        return value is View or Procedure;
    }

    public static string Normalize(string? kind)
    {
        var value = (kind ?? "").Trim().ToLowerInvariant();
        return value is "proc" or "storedprocedure" or "stored_procedure" ? Procedure : value;
    }
}

public static class AppResourceSourceKind
{
    public const string Table = "table";
    public const string View = "view";
    public const string Procedure = "procedure";

    public static bool IsQueryCatalog(string? kind)
    {
        var value = (kind ?? Table).Trim().ToLowerInvariant();
        return value is View or Procedure;
    }

    public static string Normalize(string? kind)
    {
        var value = (kind ?? Table).Trim().ToLowerInvariant();
        return value switch
        {
            View => View,
            Procedure or "proc" => Procedure,
            _ => Table
        };
    }
}

public static class QueryRoutineKind
{
    public const string View = "view";
    public const string Procedure = "procedure";
    public const string Function = "function";
}

public static class QueryParameterDirection
{
    public const string In = "in";
    public const string Out = "out";
    public const string InOut = "inout";

    public static bool IsInput(string? direction)
    {
        var value = (direction ?? In).Trim().ToLowerInvariant();
        return value is In or InOut or "in/out";
    }

    public static string Normalize(string? direction)
    {
        var value = (direction ?? In).Trim().ToLowerInvariant();
        return value switch
        {
            "out" or "output" => Out,
            "inout" or "in/out" or "in out" => InOut,
            _ => In
        };
    }
}

public static class SqlIdentifier
{
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > OrchestrationConsts.MaxTableNameLength)
        {
            return false;
        }

        if (name[0] is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_'))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>列名引用：允许 Unicode 字母，拒绝引号、点号和空白，避免拼进 SQL。</summary>
    public static bool IsQuotable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        {
            return false;
        }

        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
