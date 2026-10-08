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
    private readonly IRepository<DbQueryObject, Guid> _queryObjectRepository;
    private readonly IDataSourceResolver _dataSourceResolver;
    private readonly IParameterizedSqlExecutor _sqlExecutor;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IPermissionChecker _permissionChecker;

    public ResourceCrudExecutor(
        IRepository<AppResource, Guid> resourceRepository,
        IRepository<TableDefinition, Guid> tableRepository,
        IRepository<DbQueryObject, Guid> queryObjectRepository,
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
        _queryObjectRepository = queryObjectRepository;
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
        var normalized = op.Trim().ToLowerInvariant();
        var resourceCode = ReadString(nodeInput, "resourceCode");
        var dataSourceCode = ReadString(nodeInput, "dataSourceCode");
        var tableName = ReadString(nodeInput, "tableName");
        var tableOverride = normalized == "query" &&
                            !string.IsNullOrWhiteSpace(dataSourceCode) &&
                            !string.IsNullOrWhiteSpace(tableName);

        AppResource? resource = null;
        if (!string.IsNullOrWhiteSpace(resourceCode))
        {
            resource = await _resourceRepository.FirstOrDefaultAsync(
                x => x.Code == resourceCode,
                cancellationToken
            ) ?? throw new UserFriendlyException($"Resource was not found: {resourceCode}");
        }

        QuerySchema schema;
        DataSource dataSource;
        if (tableOverride)
        {
            var table = await _tableRepository.FirstOrDefaultAsync(
                x => x.DataSourceCode == dataSourceCode && x.TableName == tableName,
                cancellationToken
            ) ?? throw new UserFriendlyException($"Table was not found: {tableName}");
            dataSource = await _dataSourceResolver.ResolveEnabledAsync(dataSourceCode!, cancellationToken);
            schema = QuerySchema.FromTable(table);
        }
        else if (resource != null)
        {
            dataSource = await _dataSourceResolver.ResolveEnabledAsync(resource.DataSourceCode, cancellationToken);
            schema = await ResolveSchemaAsync(resource, cancellationToken);
        }
        else
        {
            throw new UserFriendlyException(
                normalized == "query"
                    ? "query requires resourceCode or dataSourceCode+tableName."
                    : "resourceCode is required."
            );
        }

        if (normalized != "query" && resource == null)
        {
            throw new UserFriendlyException("resourceCode is required.");
        }

        if (normalized != "query" && schema.IsQueryCatalog)
        {
            throw new UserFriendlyException("View and procedure resources support query only.");
        }

        var provider = dataSource.Provider;
        return normalized switch
        {
            "query" => await QueryAsync(
                provider, dataSource, resource, schema, nodeInput, tableOverride, isDryRun, cancellationToken),
            "get" => await GetAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, cancellationToken),
            "create" => await CreateAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, cancellationToken),
            "update" => await UpdateAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, cancellationToken),
            "delete" => await DeleteAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, cancellationToken),
            _ => throw new UserFriendlyException($"Unsupported resource op: {op}")
        };
    }

    private async Task<JsonObject> QueryAsync(
        string provider,
        DataSource dataSource,
        AppResource? resource,
        QuerySchema schema,
        JsonObject input,
        bool tableOverride,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var page = Math.Max(1, ReadInt(input, "page") ?? 1);
        var pageSize = Math.Clamp(
            ReadInt(input, "pageSize") ?? resource?.ListView.PageSize ?? 20,
            1,
            OrchestrationConsts.ResourceQueryMaxPageSize
        );

        if (schema.Kind == QueryObjectKind.Procedure && !schema.CanSelectFrom)
        {
            return await QueryExecAsync(
                provider, dataSource, schema, input, page, pageSize, isDryRun, cancellationToken);
        }

        var filter = FilterSqlBuilder.Build(
            provider,
            schema.Columns,
            tableOverride || schema.Kind == QueryObjectKind.Procedure ? null : resource?.Filter,
            schema.Kind == QueryObjectKind.Procedure ? null : input["filter"] ?? input["filters"],
            _currentTenant.Id
        );
        if (schema.Kind == QueryObjectKind.Procedure)
        {
            MergeRoutineArgs(filter.Args, schema, input);
        }

        var fromSql = BuildFromSql(provider, schema);
        var columns = ResolveSelectColumns(schema.Columns, input["columns"]);
        var selectList = columns.Count == 0
            ? "*"
            : string.Join(", ", columns.Select(c => SqlDialect.QuoteIdent(provider, c)));
        var orderBy = BuildOrderBy(
            provider,
            schema.Columns,
            ReadString(input, "sorting") ?? resource?.ListView.DefaultSorting
        );
        var countSql =
            $"SELECT COUNT(1) AS {SqlDialect.QuoteIdent(provider, "cnt")} FROM {fromSql} WHERE {filter.WhereSql}";
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
        var pageSql = BuildPagedSelect(provider, fromSql, selectList, filter.WhereSql, orderBy);
        var data = await _sqlExecutor.QueryAsync(dataSource, pageSql, pageArgs, isDryRun, cancellationToken);
        var result = new JsonObject
        {
            ["items"] = data.Rows?.DeepClone() ?? new JsonArray(),
            ["total"] = total
        };

        var summary = await QuerySummaryAsync(
            provider,
            dataSource,
            schema.Columns,
            fromSql,
            filter,
            input["summaryFields"],
            tableOverride ? [] : resource?.ListView.Columns ?? [],
            isDryRun,
            cancellationToken
        );
        if (summary != null)
        {
            result["summary"] = summary;
        }

        return result;
    }

    private async Task<JsonObject> QueryExecAsync(
        string provider,
        DataSource dataSource,
        QuerySchema schema,
        JsonObject input,
        int page,
        int pageSize,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var args = new JsonObject();
        MergeRoutineArgs(args, schema, input);
        var sql = BuildExecSql(provider, schema);
        var data = await _sqlExecutor.QueryRoutineAsync(dataSource, sql, args, isDryRun, cancellationToken);
        var rows = data.Rows?.OfType<JsonObject>().Select(x => x.DeepClone()!.AsObject()).ToList() ?? [];
        var total = rows.Count;
        var offset = (page - 1) * pageSize;
        var pageRows = rows.Skip(offset).Take(pageSize).ToList();
        return new JsonObject
        {
            ["items"] = new JsonArray(pageRows.Select(x => (JsonNode)x).ToArray()),
            ["total"] = total
        };
    }

    private async Task<JsonObject> GetAsync(
        string provider,
        DataSource dataSource,
        AppResource resource,
        QuerySchema schema,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var id = ReadString(input, "id") ?? throw new UserFriendlyException("id is required.");
        var filter = FilterSqlBuilder.Build(provider, schema.Columns, null, null, _currentTenant.Id);
        var args = filter.Args.DeepClone()!.AsObject();
        args["id"] = id;
        var t = SqlDialect.QuoteIdent(provider, schema.ObjectName);
        var pk = SqlDialect.QuoteIdent(provider, resource.PrimaryKey);
        var cols = schema.Columns.Count == 0
            ? "*"
            : string.Join(", ", schema.Columns.Select(c => SqlDialect.QuoteIdent(provider, c.Name)));
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
        QuerySchema schema,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        await EnsureWriteAsync();
        var table = schema.RequireTable();
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
        QuerySchema schema,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        await EnsureWriteAsync();
        var table = schema.RequireTable();
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
        QuerySchema schema,
        JsonObject input,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        await EnsureWriteAsync();
        var table = schema.RequireTable();
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

    private static List<string> ResolveSelectColumns(IReadOnlyList<TableColumn> columns, JsonNode? columnsNode)
    {
        var all = columns.Select(c => c.Name).ToList();
        if (columnsNode is JsonArray arr && arr.Count > 0)
        {
            var requested = arr
                .Select(x => x?.GetValue<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
            var allowed = all.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cols = requested.Where(allowed.Contains).ToList();
            if (all.Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase)) &&
                !cols.Any(c => c.Equals("Id", StringComparison.OrdinalIgnoreCase)))
            {
                cols.Insert(0, "Id");
            }

            return cols.Count == 0 ? all : cols;
        }

        return all;
    }

    private static string BuildOrderBy(string provider, IReadOnlyList<TableColumn> columns, string? sorting)
    {
        var names = columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallback = names.Contains("CreationTime")
            ? "CreationTime"
            : columns.FirstOrDefault()?.Name ?? "Id";
        var text = (sorting ?? fallback + " desc").Trim();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var field = parts.Length > 0 ? parts[0] : fallback;
        if (!names.Contains(field))
        {
            field = fallback;
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
        IReadOnlyList<TableColumn> tableColumns,
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

        var colMap = tableColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
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

    private async Task<QuerySchema> ResolveSchemaAsync(AppResource resource, CancellationToken cancellationToken)
    {
        if (resource.IsQueryCatalog())
        {
            DbQueryObject? queryObject = null;
            if (resource.QueryObjectId.HasValue)
            {
                queryObject = await _queryObjectRepository.FirstOrDefaultAsync(
                    x => x.Id == resource.QueryObjectId.Value,
                    cancellationToken
                );
            }

            queryObject ??= await _queryObjectRepository.FirstOrDefaultAsync(
                x => x.DataSourceCode == resource.DataSourceCode &&
                     x.ObjectName == resource.TableName &&
                     x.Kind == resource.SourceKind,
                cancellationToken
            );

            if (queryObject == null)
            {
                throw new UserFriendlyException($"Query object was not found: {resource.TableName}");
            }

            return QuerySchema.FromQueryObject(queryObject);
        }

        var table = await _tableRepository.FirstOrDefaultAsync(
            x => x.DataSourceCode == resource.DataSourceCode && x.TableName == resource.TableName,
            cancellationToken
        ) ?? throw new UserFriendlyException($"Table was not found: {resource.TableName}");
        return QuerySchema.FromTable(table);
    }

    private static string BuildFromSql(string provider, QuerySchema schema)
    {
        var quoted = SqlDialect.QuoteIdent(provider, schema.ObjectName);
        if (schema.Kind != QueryObjectKind.Procedure || !schema.CanSelectFrom)
        {
            return quoted;
        }

        var inputs = schema.Parameters.Where(p => QueryParameterDirection.IsInput(p.Direction)).ToList();
        var callArgs = string.Join(
            ", ",
            inputs.Select(p => SqlDialect.ParamName(provider, ParamKey(p.Name)))
        );
        return $"{quoted}({callArgs})";
    }

    private static string BuildExecSql(string provider, QuerySchema schema)
    {
        var p = SqlDialect.NormalizeProvider(provider);
        var quoted = SqlDialect.QuoteIdent(provider, schema.ObjectName);
        var inputs = schema.Parameters.Where(x => QueryParameterDirection.IsInput(x.Direction)).ToList();
        if (p == DataSourceProvider.SqlServer)
        {
            if (inputs.Count == 0)
            {
                return "EXEC " + quoted;
            }

            var parts = inputs.Select(x => SqlDialect.ParamName(provider, ParamKey(x.Name)));
            return "EXEC " + quoted + " " + string.Join(", ", parts);
        }

        var callArgs = string.Join(
            ", ",
            inputs.Select(x => SqlDialect.ParamName(provider, ParamKey(x.Name)))
        );
        return $"CALL {quoted}({callArgs})";
    }

    private static void MergeRoutineArgs(JsonObject args, QuerySchema schema, JsonObject input)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        CollectEqValues(input["filter"] ?? input["filters"], values);
        if (input["parameters"] is JsonObject parameters)
        {
            foreach (var prop in parameters)
            {
                values[prop.Key] = prop.Value?.DeepClone();
            }
        }

        foreach (var param in schema.Parameters.Where(x => QueryParameterDirection.IsInput(x.Direction)))
        {
            var key = ParamKey(param.Name);
            if (values.TryGetValue(param.Name, out var value) || values.TryGetValue(key, out value))
            {
                args[key] = value?.DeepClone();
            }
            else if (!args.ContainsKey(key))
            {
                args[key] = null;
            }
        }
    }

    private static void CollectEqValues(JsonNode? node, Dictionary<string, JsonNode?> values)
    {
        if (node is JsonObject obj)
        {
            var kind = obj["kind"]?.GetValue<string>();
            if (string.Equals(kind, "rule", StringComparison.OrdinalIgnoreCase))
            {
                var left = obj["left"]?.GetValue<string>() ?? obj["field"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(left) && obj["right"] != null)
                {
                    values[left] = obj["right"]?.DeepClone();
                }
            }

            if (obj["children"] is JsonArray children)
            {
                foreach (var child in children)
                {
                    CollectEqValues(child, values);
                }
            }

            if (obj["items"] is JsonArray items)
            {
                foreach (var item in items)
                {
                    CollectEqValues(item, values);
                }
            }

            return;
        }

        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                CollectEqValues(item, values);
            }
        }
    }

    private static string ParamKey(string name)
    {
        var key = Sanitize(name);
        return string.IsNullOrWhiteSpace(key) ? "p" : key;
    }

    private sealed class QuerySchema
    {
        public string ObjectName { get; init; } = null!;

        public string Kind { get; init; } = AppResourceSourceKind.Table;

        public bool CanSelectFrom { get; init; } = true;

        public bool IsQueryCatalog { get; init; }

        public IReadOnlyList<TableColumn> Columns { get; init; } = [];

        public IReadOnlyList<QueryObjectParameter> Parameters { get; init; } = [];

        public TableDefinition? Table { get; init; }

        public static QuerySchema FromTable(TableDefinition table)
        {
            return new QuerySchema
            {
                ObjectName = table.TableName,
                Kind = AppResourceSourceKind.Table,
                CanSelectFrom = true,
                IsQueryCatalog = false,
                Columns = table.Columns,
                Table = table
            };
        }

        public static QuerySchema FromQueryObject(DbQueryObject queryObject)
        {
            return new QuerySchema
            {
                ObjectName = queryObject.ObjectName,
                Kind = queryObject.Kind,
                CanSelectFrom = queryObject.CanSelectFrom || queryObject.Kind == QueryObjectKind.View,
                IsQueryCatalog = true,
                Columns = queryObject.Columns,
                Parameters = queryObject.Parameters
            };
        }

        public TableDefinition RequireTable()
        {
            return Table ?? throw new UserFriendlyException("View and procedure resources support query only.");
        }
    }
}
