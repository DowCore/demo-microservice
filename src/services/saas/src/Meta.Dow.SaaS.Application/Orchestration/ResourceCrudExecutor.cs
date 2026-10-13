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

    /// <summary>
    /// 带节点级弹性开关的重载：<paramref name="allowWriteRetry"/> = DSL 的 retryOnWrite，
    /// 仅在调用方已完成幂等校验时才应传 true。
    /// </summary>
    Task<JsonObject> ExecuteAsync(
        string op,
        JsonObject nodeInput,
        bool isDryRun,
        bool allowWriteRetry,
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
    private readonly INodeResiliencePolicy _resilience;

    public ResourceCrudExecutor(
        IRepository<AppResource, Guid> resourceRepository,
        IRepository<TableDefinition, Guid> tableRepository,
        IRepository<DbQueryObject, Guid> queryObjectRepository,
        IDataSourceResolver dataSourceResolver,
        IParameterizedSqlExecutor sqlExecutor,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IGuidGenerator guidGenerator,
        IPermissionChecker permissionChecker,
        INodeResiliencePolicy resilience
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
        _resilience = resilience;
    }

    public Task<JsonObject> ExecuteAsync(
        string op,
        JsonObject nodeInput,
        bool isDryRun,
        CancellationToken cancellationToken = default
    ) => ExecuteAsync(op, nodeInput, isDryRun, allowWriteRetry: false, cancellationToken);

    public async Task<JsonObject> ExecuteAsync(
        string op,
        JsonObject nodeInput,
        bool isDryRun,
        bool allowWriteRetry,
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

        // 读写分离弹性：读操作（query/get）遇瞬态连接故障自动重试；
        // 写操作默认不重试 —— 写重试需先有幂等保护（R6 尚未落地），此处守住安全默认值。
        // 唯一包裹点放在 op 分派处，新增 op 无需重复接线，避免"配了不生效"重演。
        var isWrite = normalized is not ("query" or "get");
        return await _resilience.ExecuteAsync(
            nodeId: resourceCode ?? tableName ?? normalized,
            operation: normalized,
            isWrite: isWrite,
            allowWriteRetry: allowWriteRetry,
            action: async ct => normalized switch
            {
                "query" => await QueryAsync(
                    provider, dataSource, resource, schema, nodeInput, tableOverride, isDryRun, ct),
                "get" => await GetAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
                "create" => await CreateAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
                "update" => await UpdateAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
                "delete" => await DeleteAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
                // 批量写：单连接 + 单事务（复用 ParameterizedSqlExecutor.BatchAsync 的既有事务能力）。
                // 只支持 SQL 家族 —— MongoDB 单机无跨文档事务，显式拒绝而不是假装原子。
                "batchcreate" => await BatchWriteAsync(
                    BatchOp.Create, provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
                "batchupdate" => await BatchWriteAsync(
                    BatchOp.Update, provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
                "batchdelete" => await BatchWriteAsync(
                    BatchOp.Delete, provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
                _ => throw new UserFriendlyException($"Unsupported resource op: {op}")
            },
            cancellationToken: cancellationToken
        );
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

    private enum BatchOp
    {
        Create,
        Update,
        Delete
    }

    /// <summary>
    /// 批量写：单连接 + 单事务，失败整体回滚。
    /// 复用既有 SQL 构造（与单条 <see cref="CreateAsync"/>/<see cref="UpdateAsync"/>/<see cref="DeleteAsync"/> 同源），
    /// 以及 <see cref="IParameterizedSqlExecutor.BatchAsync"/> 的既有事务实现 —— 不新建事务机制。
    /// 输入：&#123; items: [ ... ], &#125;，items 为每条记录（create/update 为 record，delete 为 id 或含 id 的对象）。
    /// 输出：&#123; op, affected, count &#125;（全事务语义：要么全成功，要么抛错回滚）。
    /// </summary>
    private async Task<JsonObject> BatchWriteAsync(
        BatchOp op,
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

        if (!DataSourceProvider.IsSql(provider))
        {
            throw new UserFriendlyException(
                $"batch{op.ToString().ToLowerInvariant()} 仅支持 SQL 家族数据源；" +
                $"{provider} 无跨文档事务，请改用 ForEach + itemErrorMode=continueOnError 逐条处理。"
            );
        }

        var items = input["items"] as JsonArray
                    ?? throw new UserFriendlyException("批量写需要 items 数组。");
        if (items.Count == 0)
        {
            return new JsonObject { ["op"] = op.ToString().ToLowerInvariant(), ["affected"] = 0, ["count"] = 0 };
        }

        if (items.Count > OrchestrationConsts.CodeDbBatchMaxRows)
        {
            throw new UserFriendlyException(
                $"批量写条目数 {items.Count} 超过上限 {OrchestrationConsts.CodeDbBatchMaxRows}。"
            );
        }

        var table = schema.RequireTable();
        var filter = FilterSqlBuilder.Build(provider, table, null, null, _currentTenant.Id);
        var t = SqlDialect.QuoteIdent(provider, table.TableName);
        var pk = SqlDialect.QuoteIdent(provider, resource.PrimaryKey);
        var softDelete = table.Columns.Any(c => c.Name == resource.SoftDeleteField);

        var paramList = new JsonArray();

        switch (op)
        {
            case BatchOp.Create:
            {
                // 与单条 CreateAsync 同源：同一套列填充 + 平台字段
                var (colSql, valSql, columns) = BuildInsertShape(provider, table);
                foreach (var item in items)
                {
                    var record = item as JsonObject
                                 ?? throw new UserFriendlyException("batchcreate 的 items 每项必须是对象。");
                    var args = new JsonObject();
                    foreach (var col in columns)
                    {
                        args["p" + Sanitize(col.Name)] = ResolveColumnValue(record, col);
                    }

                    args["pId"] = ReadString(record, "id") ?? _guidGenerator.Create().ToString();
                    args["pTenantId"] = _currentTenant.Id?.ToString();
                    args["pConcurrencyStamp"] = NewStamp();
                    args["pCreationTime"] = DateTime.UtcNow.ToString("O");
                    args["pCreatorId"] = _currentUser.Id?.ToString();
                    args["pIsDeleted"] = false;
                    paramList.Add(args);
                }

                var sql = $"INSERT INTO {t} ({colSql}) VALUES ({valSql})";
                return await RunBatchAsync(op, dataSource, sql, paramList, items.Count, isDryRun, cancellationToken);
            }

            case BatchOp.Update:
            {
                var updatable = table.Columns
                    .Where(c => c.Origin != TableOrigin.Convention)
                    .ToList();
                var setSql = string.Join(
                    ", ",
                    updatable.Select(c => $"{SqlDialect.QuoteIdent(provider, c.Name)} = {SqlDialect.ParamName(provider, "p" + Sanitize(c.Name))}")
                );
                if (string.IsNullOrWhiteSpace(setSql))
                {
                    throw new UserFriendlyException("batchupdate 找不到可更新的列。");
                }

                foreach (var item in items)
                {
                    var record = item as JsonObject
                                 ?? throw new UserFriendlyException("batchupdate 的 items 每项必须是对象。");
                    var id = ReadString(record, "id")
                             ?? throw new UserFriendlyException("batchupdate 的每项都需要 id。");
                    var args = new JsonObject();
                    foreach (var col in updatable)
                    {
                        args["p" + Sanitize(col.Name)] = ResolveColumnValue(record, col);
                    }

                    args["pConcurrencyStamp"] = NewStamp();
                    args["pLastModificationTime"] = DateTime.UtcNow.ToString("O");
                    args["pLastModifierId"] = _currentUser.Id?.ToString();
                    args["pId"] = id;
                    paramList.Add(args);
                }

                // 乐观并发：ConcurrencyStamp 变化后旧版本不再匹配 —— 与单条 update 语义一致
                var stampSql = table.Columns.Any(c => c.Name == "ConcurrencyStamp")
                    ? $" AND {SqlDialect.QuoteIdent(provider, "ConcurrencyStamp")} = {SqlDialect.ParamName(provider, "pConcurrencyStamp")}"
                    : "";
                var stampSet = table.Columns.Any(c => c.Name == "ConcurrencyStamp")
                    ? $", {SqlDialect.QuoteIdent(provider, "ConcurrencyStamp")} = {SqlDialect.ParamName(provider, "pConcurrencyStamp")}"
                    : "";
                var sql =
                    $"UPDATE {t} SET {setSql}{stampSet} WHERE {filter.WhereSql} AND {pk} = {SqlDialect.ParamName(provider, "pId")}";
                return await RunBatchAsync(op, dataSource, sql, paramList, items.Count, isDryRun, cancellationToken);
            }

            case BatchOp.Delete:
            {
                foreach (var item in items)
                {
                    var record = item as JsonObject;
                    var id = record != null
                        ? ReadString(record, "id")
                        : item?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new UserFriendlyException("batchdelete 的每项需要 id（字符串或含 id 的对象）。");
                    }

                    var args = filter.Args.DeepClone()!.AsObject();
                    args["pId"] = id;
                    if (softDelete)
                    {
                        args["pDeleted"] = true;
                        args["pDeleter"] = _currentUser.Id?.ToString();
                        args["pDeletionTime"] = DateTime.UtcNow.ToString("O");
                    }

                    paramList.Add(args);
                }

                var sql = softDelete
                    ? $"UPDATE {t} SET {SqlDialect.QuoteIdent(provider, "IsDeleted")} = {SqlDialect.ParamName(provider, "pDeleted")}, " +
                      $"{SqlDialect.QuoteIdent(provider, "DeleterId")} = {SqlDialect.ParamName(provider, "pDeleter")}, " +
                      $"{SqlDialect.QuoteIdent(provider, "DeletionTime")} = {SqlDialect.ParamName(provider, "pDeletionTime")} " +
                      $"WHERE {filter.WhereSql} AND {pk} = {SqlDialect.ParamName(provider, "pId")}"
                    : $"DELETE FROM {t} WHERE {filter.WhereSql} AND {pk} = {SqlDialect.ParamName(provider, "pId")}";
                return await RunBatchAsync(op, dataSource, sql, paramList, items.Count, isDryRun, cancellationToken);
            }

            default:
                throw new UserFriendlyException($"不支持的批量操作：{op}");
        }
    }

    /// <summary>与单条 CreateAsync 同源的插入列形状（列名 / 值占位符 / 参与列）。</summary>
    private (string ColSql, string ValSql, List<TableColumn> Columns) BuildInsertShape(string provider, TableDefinition table)
    {
        var columns = table.Columns.Where(c => c.Origin != TableOrigin.Convention).ToList();
        var names = new List<string>(columns.Select(c => c.Name))
        {
            "Id",
            "TenantId",
            "ConcurrencyStamp",
            "CreationTime",
            "CreatorId",
            "IsDeleted"
        };
        var colSql = string.Join(", ", names.Select(n => SqlDialect.QuoteIdent(provider, n)));
        var valSql = string.Join(
            ", ",
            names.Select(n => SqlDialect.ParamName(provider, "p" + Sanitize(n)))
        );
        return (colSql, valSql, columns);
    }

    /// <summary>按列名（大小写不敏感）取记录值；空值按列可空性处理。与单条 CreateAsync 取值规则一致。</summary>
    private static JsonNode? ResolveColumnValue(JsonObject record, TableColumn col)
    {
        JsonNode? value = record[col.Name];
        if (value == null)
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

        if (IsBlankNode(value))
        {
            return col.PlatformType is TablePlatformType.Date or TablePlatformType.DateTime && !col.Nullable
                ? throw new UserFriendlyException($"Column '{col.DisplayName}' is required.")
                : null;
        }

        return value?.DeepClone();
    }

    private async Task<JsonObject> RunBatchAsync(
        BatchOp op,
        DataSource dataSource,
        string sql,
        JsonArray paramList,
        int count,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var result = await _sqlExecutor.BatchAsync(dataSource, sql, paramList, isDryRun, cancellationToken);
        return new JsonObject
        {
            ["op"] = op.ToString().ToLowerInvariant(),
            ["count"] = count,
            ["affected"] = result.AffectedRows,
            // 全事务语义：能返回就代表整批已提交（失败会抛错并回滚）
            ["committed"] = !isDryRun
        };
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
