using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meta.Dow.SaaS.Permissions;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Meta.Dow.SaaS.Orchestration;

public interface IResourceCrudExecutor
{
    Task<JsonObject> ExecuteAsync(
        string op,
        JsonObject nodeInput,
        bool isDryRun,
        CancellationToken cancellationToken = default
    );
}

public class ResourceCrudExecutor : IResourceCrudExecutor, ITransientDependency
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IRepository<AppResource, Guid> _resourceRepository;
    private readonly IRepository<TableDefinition, Guid> _tableRepository;
    private readonly IDataSourceResolver _dataSourceResolver;
    private readonly IParameterizedSqlExecutor _sqlExecutor;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IPermissionChecker _permissionChecker;

    public ResourceCrudExecutor(
        IRepository<AppResource, Guid> resourceRepository,
        IRepository<TableDefinition, Guid> tableRepository,
        IDataSourceResolver dataSourceResolver,
        IParameterizedSqlExecutor sqlExecutor,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IGuidGenerator guidGenerator,
        IPermissionChecker permissionChecker
    )
    {
        _resourceRepository = resourceRepository;
        _tableRepository = tableRepository;
        _dataSourceResolver = dataSourceResolver;
        _sqlExecutor = sqlExecutor;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _guidGenerator = guidGenerator;
        _permissionChecker = permissionChecker;
    }

    public async Task<JsonObject> ExecuteAsync(
        string op,
        JsonObject nodeInput,
        bool isDryRun,
        CancellationToken cancellationToken = default
    )
    {
        var resourceCode = ReadString(nodeInput, "resourceCode")
                           ?? throw new UserFriendlyException("resourceCode is required.");
        var resource = await _resourceRepository.FirstOrDefaultAsync(
            x => x.Code == resourceCode,
            cancellationToken
        ) ?? throw new UserFriendlyException($"Resource was not found: {resourceCode}");

        var table = await _tableRepository.FirstOrDefaultAsync(
            x => x.DataSourceCode == resource.DataSourceCode && x.TableName == resource.TableName,
            cancellationToken
        ) ?? throw new UserFriendlyException($"Table was not found: {resource.TableName}");

        var dataSource = await _dataSourceResolver.ResolveEnabledAsync(resource.DataSourceCode, cancellationToken);
        var provider = dataSource.Provider;
        var normalized = op.Trim().ToLowerInvariant();

        return normalized switch
        {
            "query" => await QueryAsync(provider, dataSource, resource, table, nodeInput, isDryRun, cancellationToken),
            "get" => await GetAsync(provider, dataSource, resource, table, nodeInput, isDryRun, cancellationToken),
            "create" => await CreateAsync(provider, dataSource, resource, table, nodeInput, isDryRun, cancellationToken),
            "update" => await UpdateAsync(provider, dataSource, resource, table, nodeInput, isDryRun, cancellationToken),
            "delete" => await DeleteAsync(provider, dataSource, resource, table, nodeInput, isDryRun, cancellationToken),
            _ => throw new UserFriendlyException($"Unsupported resource op: {op}")
        };
    }

    private async Task<JsonObject> QueryAsync(
        string provider,
        DataSource dataSource,
        AppResource resource,
        TableDefinition table,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var page = Math.Max(1, ReadInt(input, "page") ?? 1);
        var pageSize = Math.Clamp(
            ReadInt(input, "pageSize") ?? resource.ListView.PageSize,
            1,
            OrchestrationConsts.ResourceQueryMaxPageSize
        );
        var filter = FilterSqlBuilder.Build(
            provider,
            table,
            resource.Filter,
            input["filter"] ?? input["filters"],
            _currentTenant.Id
        );
        var t = SqlDialect.QuoteIdent(provider, table.TableName);
        var columns = ResolveSelectColumns(table, input["columns"]);
        var selectList = string.Join(", ", columns.Select(c => SqlDialect.QuoteIdent(provider, c)));
        var orderBy = BuildOrderBy(provider, table, ReadString(input, "sorting") ?? resource.ListView.DefaultSorting);
        var countSql =
            $"SELECT COUNT(1) AS {SqlDialect.QuoteIdent(provider, "cnt")} FROM {t} WHERE {filter.WhereSql}";
        var count = await _sqlExecutor.QueryAsync(dataSource, countSql, filter.Args, isDryRun, cancellationToken);
        long total = 0;
        if (count.Rows is { Count: > 0 } && count.Rows[0] is JsonObject row)
        {
            total = JsonNodeNumbers.ToInt64(FindIgnoreCase(row, "cnt")) ?? 0;
        }

        var offset = (page - 1) * pageSize;
        var pageArgs = filter.Args.DeepClone()!.AsObject();
        pageArgs["take"] = pageSize;
        pageArgs["skip"] = offset;
        var pageSql = BuildPagedSelect(provider, t, selectList, filter.WhereSql, orderBy);
        var data = await _sqlExecutor.QueryAsync(dataSource, pageSql, pageArgs, isDryRun, cancellationToken);
        var result = new JsonObject
        {
            ["items"] = data.Rows?.DeepClone() ?? new JsonArray(),
            ["total"] = total
        };

        var summary = await QuerySummaryAsync(
            provider,
            dataSource,
            table,
            t,
            filter,
            input["summaryFields"],
            resource.ListView.Columns,
            isDryRun,
            cancellationToken
        );
        if (summary != null)
        {
            result["summary"] = summary;
        }

        return result;
    }

    private async Task<JsonObject> GetAsync(
        string provider,
        DataSource dataSource,
        AppResource resource,
        TableDefinition table,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var id = ReadString(input, "id") ?? throw new UserFriendlyException("id is required.");
        var filter = FilterSqlBuilder.Build(provider, table, null, null, _currentTenant.Id);
        var args = filter.Args.DeepClone()!.AsObject();
        args["id"] = id;
        var t = SqlDialect.QuoteIdent(provider, table.TableName);
        var pk = SqlDialect.QuoteIdent(provider, resource.PrimaryKey);
        var cols = string.Join(", ", table.Columns.Select(c => SqlDialect.QuoteIdent(provider, c.Name)));
        var sql =
            $"SELECT {cols} FROM {t} WHERE {filter.WhereSql} AND {pk} = {SqlDialect.ParamName(provider, "id")}";
        var result = await _sqlExecutor.QueryAsync(dataSource, sql, args, isDryRun, cancellationToken);
        var record = result.Rows?.FirstOrDefault() as JsonObject
                     ?? throw new UserFriendlyException("Record was not found.");
        return new JsonObject { ["record"] = record.DeepClone() };
    }

    private async Task<JsonObject> CreateAsync(
        string provider,
        DataSource dataSource,
        AppResource resource,
        TableDefinition table,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        await EnsureWriteAsync();
        var record = input["record"] as JsonObject ?? input;
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in table.Columns.Where(c => c.Origin != TableOrigin.Convention))
        {
            JsonNode? value = null;
            if (record[col.Name] != null)
            {
                value = record[col.Name]?.DeepClone();
            }
            else
            {
                foreach (var prop in record)
                {
                    if (string.Equals(prop.Key, col.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value?.DeepClone();
                        break;
                    }
                }
            }

            if (IsBlankNode(value))
            {
                if (col.PlatformType is TablePlatformType.Date or TablePlatformType.DateTime)
                {
                    if (!col.Nullable)
                    {
                        throw new UserFriendlyException($"Column '{col.DisplayName}' is required.");
                    }

                    values[col.Name] = null;
                }

                continue;
            }

            values[col.Name] = value;
        }

        var id = ReadString(record, "id") ?? _guidGenerator.Create().ToString();
        values["Id"] = id;
        values["TenantId"] = _currentTenant.Id?.ToString();
        values["ConcurrencyStamp"] = NewStamp();
        values["CreationTime"] = DateTime.UtcNow.ToString("O");
        values["CreatorId"] = _currentUser.Id?.ToString();
        values["IsDeleted"] = false;

        var args = new JsonObject();
        foreach (var (k, v) in values)
        {
            args["p" + Sanitize(k)] = v?.DeepClone();
        }

        var t = SqlDialect.QuoteIdent(provider, table.TableName);
        var colSql = string.Join(", ", values.Keys.Select(k => SqlDialect.QuoteIdent(provider, k)));
        var valSql = string.Join(
            ", ",
            values.Keys.Select(k => SqlDialect.ParamName(provider, "p" + Sanitize(k)))
        );
        var sql = $"INSERT INTO {t} ({colSql}) VALUES ({valSql})";
        await _sqlExecutor.ExecuteAsync(dataSource, sql, args, isDryRun, cancellationToken);
        return new JsonObject { ["id"] = id };
    }

    private async Task<JsonObject> UpdateAsync(
        string provider,
        DataSource dataSource,
        AppResource resource,
        TableDefinition table,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        await EnsureWriteAsync();
        var id = ReadString(input, "id") ?? throw new UserFriendlyException("id is required.");
        var stamp = ReadString(input, "concurrencyStamp");
        var record = input["record"] as JsonObject ?? input;
        var sets = new List<string>();
        var args = new JsonObject { ["id"] = id };

        foreach (var col in table.Columns.Where(c => c.Origin != TableOrigin.Convention))
        {
            JsonNode? value = null;
            if (record.TryGetPropertyValue(col.Name, out var direct))
            {
                value = direct;
            }
            else
            {
                foreach (var prop in record)
                {
                    if (string.Equals(prop.Key, col.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        break;
                    }
                }
            }

            if (value == null)
            {
                continue;
            }

            if (IsBlankNode(value))
            {
                if (col.PlatformType is not (TablePlatformType.Date or TablePlatformType.DateTime))
                {
                    continue;
                }

                if (!col.Nullable)
                {
                    throw new UserFriendlyException($"Column '{col.DisplayName}' is required.");
                }

                value = null;
            }

            var pn = "p" + Sanitize(col.Name);
            args[pn] = value.DeepClone();
            sets.Add($"{SqlDialect.QuoteIdent(provider, col.Name)} = {SqlDialect.ParamName(provider, pn)}");
        }

        var newStamp = NewStamp();
        args["newStamp"] = newStamp;
        args["lastMod"] = DateTime.UtcNow.ToString("O");
        args["lastModBy"] = _currentUser.Id?.ToString();
        sets.Add($"{SqlDialect.QuoteIdent(provider, "ConcurrencyStamp")} = {SqlDialect.ParamName(provider, "newStamp")}");
        sets.Add($"{SqlDialect.QuoteIdent(provider, "LastModificationTime")} = {SqlDialect.ParamName(provider, "lastMod")}");
        sets.Add($"{SqlDialect.QuoteIdent(provider, "LastModifierId")} = {SqlDialect.ParamName(provider, "lastModBy")}");

        if (sets.Count == 3)
        {
            throw new UserFriendlyException("No updatable fields were provided.");
        }

        var filter = FilterSqlBuilder.Build(provider, table, null, null, _currentTenant.Id);
        foreach (var prop in filter.Args)
        {
            args[prop.Key] = prop.Value?.DeepClone();
        }

        var t = SqlDialect.QuoteIdent(provider, table.TableName);
        var pk = SqlDialect.QuoteIdent(provider, resource.PrimaryKey);
        var sql =
            $"UPDATE {t} SET {string.Join(", ", sets)} WHERE {filter.WhereSql} AND {pk} = {SqlDialect.ParamName(provider, "id")}";
        if (!string.IsNullOrWhiteSpace(stamp) && table.Columns.Any(c => c.Name == "ConcurrencyStamp"))
        {
            args["stamp"] = stamp;
            sql += $" AND {SqlDialect.QuoteIdent(provider, "ConcurrencyStamp")} = {SqlDialect.ParamName(provider, "stamp")}";
        }

        var result = await _sqlExecutor.ExecuteAsync(dataSource, sql, args, isDryRun, cancellationToken);
        if (!isDryRun && result.AffectedRows == 0)
        {
            throw new UserFriendlyException("Update conflict or record was not found.");
        }

        return new JsonObject { ["id"] = id, ["concurrencyStamp"] = newStamp };
    }

    private async Task<JsonObject> DeleteAsync(
        string provider,
        DataSource dataSource,
        AppResource resource,
        TableDefinition table,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        await EnsureWriteAsync();
        var id = ReadString(input, "id") ?? throw new UserFriendlyException("id is required.");
        var filter = FilterSqlBuilder.Build(provider, table, null, null, _currentTenant.Id);
        var args = filter.Args.DeepClone()!.AsObject();
        args["id"] = id;
        var t = SqlDialect.QuoteIdent(provider, table.TableName);
        var pk = SqlDialect.QuoteIdent(provider, resource.PrimaryKey);
        string sql;
        if (table.Columns.Any(c => c.Name == resource.SoftDeleteField))
        {
            args["deleted"] = true;
            args["deleter"] = _currentUser.Id?.ToString();
            args["deletionTime"] = DateTime.UtcNow.ToString("O");
            sql =
                $"UPDATE {t} SET {SqlDialect.QuoteIdent(provider, "IsDeleted")} = {SqlDialect.ParamName(provider, "deleted")}, " +
                $"{SqlDialect.QuoteIdent(provider, "DeleterId")} = {SqlDialect.ParamName(provider, "deleter")}, " +
                $"{SqlDialect.QuoteIdent(provider, "DeletionTime")} = {SqlDialect.ParamName(provider, "deletionTime")} " +
                $"WHERE {filter.WhereSql} AND {pk} = {SqlDialect.ParamName(provider, "id")}";
        }
        else
        {
            sql =
                $"DELETE FROM {t} WHERE {filter.WhereSql} AND {pk} = {SqlDialect.ParamName(provider, "id")}";
        }

        await _sqlExecutor.ExecuteAsync(dataSource, sql, args, isDryRun, cancellationToken);
        return new JsonObject { ["id"] = id, ["deleted"] = true };
    }

    private async Task EnsureWriteAsync()
    {
        if (!await _permissionChecker.IsGrantedAsync(OrchestrationPermissions.Sql.Write))
        {
            throw new UserFriendlyException("Write to data source is not allowed.");
        }
    }

    private static List<string> ResolveSelectColumns(TableDefinition table, JsonNode? columnsNode)
    {
        var all = table.Columns.Select(c => c.Name).ToList();
        if (columnsNode is JsonArray arr && arr.Count > 0)
        {
            var requested = arr
                .Select(x => x?.GetValue<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
            var allowed = all.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cols = requested.Where(allowed.Contains).ToList();
            if (!cols.Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase)))
            {
                cols.Insert(0, "Id");
            }

            return cols.Count == 0 ? all : cols;
        }

        return all;
    }

    private static string BuildOrderBy(string provider, TableDefinition table, string? sorting)
    {
        var names = table.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var text = (sorting ?? "CreationTime desc").Trim();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var field = parts.Length > 0 ? parts[0] : "CreationTime";
        if (!names.Contains(field))
        {
            field = names.Contains("CreationTime") ? "CreationTime" : table.Columns[0].Name;
        }

        var desc = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return $"{SqlDialect.QuoteIdent(provider, field)} {(desc ? "DESC" : "ASC")}";
    }

    private static string BuildPagedSelect(
        string provider,
        string quotedTable,
        string selectList,
        string whereSql,
        string orderBy
    )
    {
        var p = SqlDialect.NormalizeProvider(provider);
        var take = SqlDialect.ParamName(provider, "take");
        var skip = SqlDialect.ParamName(provider, "skip");
        if (p == DataSourceProvider.SqlServer)
        {
            return
                $"SELECT {selectList} FROM {quotedTable} WHERE {whereSql} ORDER BY {orderBy} OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY";
        }

        if (p == DataSourceProvider.Oracle)
        {
            return
                $"SELECT {selectList} FROM {quotedTable} WHERE {whereSql} ORDER BY {orderBy} OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY";
        }

        return
            $"SELECT {selectList} FROM {quotedTable} WHERE {whereSql} ORDER BY {orderBy} LIMIT {take} OFFSET {skip}";
    }

    private async Task<JsonObject?> QuerySummaryAsync(
        string provider,
        DataSource dataSource,
        TableDefinition table,
        string quotedTable,
        FilterSqlResult filter,
        JsonNode? summaryNode,
        IReadOnlyList<ListColumnDef> columns,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var requests = ParseSummaryFields(summaryNode, columns);
        if (requests.Count == 0)
        {
            return null;
        }

        var colMap = table.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var selectParts = new List<string>
        {
            $"COUNT(1) AS {SqlDialect.QuoteIdent(provider, "cnt")}"
        };
        var meta = new List<(string Field, string Fn, string Alias)>();
        for (var i = 0; i < requests.Count; i++)
        {
            var (field, fn) = requests[i];
            if (!colMap.TryGetValue(field, out var col))
            {
                continue;
            }

            if (fn is "sum" or "avg" or "min" or "max" &&
                col.PlatformType is not (
                    TablePlatformType.Int or TablePlatformType.Long or TablePlatformType.Decimal
                    or TablePlatformType.Date or TablePlatformType.DateTime))
            {
                continue;
            }

            var alias = "a" + i.ToString(CultureInfo.InvariantCulture);
            var ident = SqlDialect.QuoteIdent(provider, col.Name);
            var expr = fn switch
            {
                "avg" => $"AVG({ident})",
                "min" => $"MIN({ident})",
                "max" => $"MAX({ident})",
                "count" => $"COUNT({ident})",
                _ => $"SUM({ident})"
            };
            selectParts.Add($"{expr} AS {SqlDialect.QuoteIdent(provider, alias)}");
            meta.Add((col.Name, fn, alias));
        }

        var sql = $"SELECT {string.Join(", ", selectParts)} FROM {quotedTable} WHERE {filter.WhereSql}";
        var query = await _sqlExecutor.QueryAsync(dataSource, sql, filter.Args, isDryRun, cancellationToken);
        if (query.Rows is not { Count: > 0 } || query.Rows[0] is not JsonObject row)
        {
            return null;
        }

        var summary = new JsonObject
        {
            ["_count"] = JsonNodeNumbers.ToInt64(FindIgnoreCase(row, "cnt")) ?? 0
        };
        foreach (var (field, fn, alias) in meta)
        {
            if (summary[field] is not JsonObject slot)
            {
                slot = new JsonObject();
                summary[field] = slot;
            }

            slot[fn] = FindIgnoreCase(row, alias)?.DeepClone();
        }

        return summary;
    }

    private static List<(string Field, string Fn)> ParseSummaryFields(
        JsonNode? node,
        IReadOnlyList<ListColumnDef> columns
    )
    {
        var list = new List<(string, string)>();
        if (node is JsonArray arr && arr.Count > 0)
        {
            foreach (var el in arr)
            {
                if (el is JsonObject obj)
                {
                    var field = obj["field"]?.GetValue<string>();
                    var fn = (obj["fn"]?.GetValue<string>() ?? "sum").Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(field))
                    {
                        list.Add((field, NormalizeSummaryFn(fn)));
                    }
                }
            }
        }

        if (list.Count == 0)
        {
            foreach (var col in columns.Where(c => !string.IsNullOrWhiteSpace(c.SummaryFn)))
            {
                list.Add((col.Field, NormalizeSummaryFn(col.SummaryFn!)));
            }
        }

        return list;
    }

    private static string NormalizeSummaryFn(string fn) =>
        fn.Trim().ToLowerInvariant() switch
        {
            "avg" or "average" => "avg",
            "min" => "min",
            "max" => "max",
            "count" => "count",
            _ => "sum"
        };

    private static string? ReadString(JsonObject obj, string name)
    {
        foreach (var prop in obj)
        {
            if (!string.Equals(prop.Key, name, StringComparison.OrdinalIgnoreCase) || prop.Value == null)
            {
                continue;
            }

            if (prop.Value is JsonValue jv)
            {
                if (jv.TryGetValue<string>(out var s))
                {
                    return s;
                }

                return jv.ToString();
            }

            return prop.Value.ToString();
        }

        return null;
    }

    private static JsonNode? FindIgnoreCase(JsonObject obj, string name)
    {
        foreach (var prop in obj)
        {
            if (string.Equals(prop.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value;
            }
        }

        return obj.Count > 0 ? obj.First().Value : null;
    }

    private static bool IsBlankNode(JsonNode? node)
    {
        if (node is null)
        {
            return true;
        }

        if (node is JsonValue value)
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.Null => true,
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetValue<string>()),
                _ => false
            };
        }

        return false;
    }

    private static int? ReadInt(JsonObject obj, string name)
    {
        foreach (var prop in obj)
        {
            if (!string.Equals(prop.Key, name, StringComparison.OrdinalIgnoreCase) || prop.Value == null)
            {
                continue;
            }

            var n = JsonNodeNumbers.ToInt32(prop.Value);
            if (n.HasValue)
            {
                return n;
            }
        }

        return null;
    }

    private static string Sanitize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray());

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
