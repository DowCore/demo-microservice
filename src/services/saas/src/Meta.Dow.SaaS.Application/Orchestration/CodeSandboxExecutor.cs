using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public sealed class CodeSandboxRequest
{
    public required string Script { get; init; }

    public required JsonObject NodeInput { get; init; }

    public required JsonObject Input { get; init; }

    public required JsonObject Sys { get; init; }

    public JsonObject? Results { get; init; }

    public DataSource? DataSource { get; init; }

    public bool IsDryRun { get; init; }

    public bool CanWriteSql { get; init; }
}

public interface ICodeSandboxExecutor
{
    Task<JsonNode?> ExecuteAsync(CodeSandboxRequest request, CancellationToken cancellationToken = default);
}

public class CodeSandboxExecutor : ICodeSandboxExecutor, ITransientDependency
{
    private static readonly Regex AwaitDbRegex = new(
        @"\bawait\s+(?=db\s*\.)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private readonly IParameterizedSqlExecutor _sqlExecutor;
    private readonly IRedisDataExecutor _redisExecutor;
    private readonly IMongoDataExecutor _mongoExecutor;

    public CodeSandboxExecutor(
        IParameterizedSqlExecutor sqlExecutor,
        IRedisDataExecutor redisExecutor,
        IMongoDataExecutor mongoExecutor
    )
    {
        _sqlExecutor = sqlExecutor;
        _redisExecutor = redisExecutor;
        _mongoExecutor = mongoExecutor;
    }

    public Task<JsonNode?> ExecuteAsync(CodeSandboxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var script = request.Script?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(script))
        {
            return Task.FromResult<JsonNode?>(request.NodeInput.DeepClone());
        }

        CodeScriptStaticAnalyzer.Validate(script, request.DataSource != null);

        script = AwaitDbRegex.Replace(script, "");

        using var engine = new Engine(options =>
        {
            options.TimeoutInterval(TimeSpan.FromSeconds(OrchestrationConsts.CodeScriptTimeoutSeconds));
            options.MaxStatements(OrchestrationConsts.CodeMaxStatements);
            options.LimitRecursion(64);
            options.Strict = true;
            options.CancellationToken(cancellationToken);
        });

        engine.SetValue("fetch", JsValue.Undefined);
        engine.SetValue("XMLHttpRequest", JsValue.Undefined);
        engine.SetValue("eval", JsValue.Undefined);
        engine.SetValue("Function", JsValue.Undefined);

        engine.SetValue("nodeInput", JsonToJs(engine, request.NodeInput));
        engine.SetValue("input", JsonToJs(engine, request.Input));
        engine.SetValue("sys", JsonToJs(engine, request.Sys));
        if (request.Results != null)
        {
            engine.SetValue("results", JsonToJs(engine, request.Results));
        }

        if (request.DataSource != null)
        {
            var host = new CodeDbHost(
                request.DataSource,
                _sqlExecutor,
                _redisExecutor,
                _mongoExecutor,
                request.IsDryRun,
                request.CanWriteSql,
                cancellationToken
            );
            engine.SetValue("db", host);
        }

        var wrapped = "(() => {\n" + script + "\n})()";
        try
        {
            var completion = engine.Evaluate(wrapped);
            return Task.FromResult(JsToJson(completion));
        }
        catch (JavaScriptException ex)
        {
            throw new UserFriendlyException($"Code script error: {ex.Message}");
        }
        catch (TimeoutException)
        {
            throw new UserFriendlyException("Code script execution timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UserFriendlyException("Code script execution timed out.");
        }
    }

    private static JsValue JsonToJs(Engine engine, JsonNode? node)
    {
        if (node == null)
        {
            return JsValue.Null;
        }

        return engine.Evaluate("(" + node.ToJsonString(FlowExecutor.JsonOptions) + ")");
    }

    private static JsonNode? JsToJson(JsValue value)
    {
        if (value.IsNull() || value.IsUndefined())
        {
            return null;
        }

        if (value.IsBoolean())
        {
            return value.AsBoolean();
        }

        if (value.IsNumber())
        {
            return value.AsNumber();
        }

        if (value.IsString())
        {
            return value.AsString();
        }

        try
        {
            var json = value.ToObject();
            if (json == null)
            {
                return null;
            }

            return JsonSerializer.SerializeToNode(json, FlowExecutor.JsonOptions);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException or NotSupportedException)
        {
            return JsonValue.Create(value.ToString());
        }
    }
}

/// <summary>
/// 沙箱 db 宿主：按数据源类型分流。
/// SQL → query/execute/batch；Redis → get/set/mget/mset/del；Mongo → find/insertOne/insertMany/...
/// </summary>
public sealed class CodeDbHost
{
    private readonly DataSource _dataSource;
    private readonly IParameterizedSqlExecutor _sqlExecutor;
    private readonly IRedisDataExecutor _redisExecutor;
    private readonly IMongoDataExecutor _mongoExecutor;
    private readonly bool _isDryRun;
    private readonly bool _canWrite;
    private readonly CancellationToken _cancellationToken;
    private readonly string _family;

    public CodeDbHost(
        DataSource dataSource,
        IParameterizedSqlExecutor sqlExecutor,
        IRedisDataExecutor redisExecutor,
        IMongoDataExecutor mongoExecutor,
        bool isDryRun,
        bool canWrite,
        CancellationToken cancellationToken
    )
    {
        _dataSource = dataSource;
        _sqlExecutor = sqlExecutor;
        _redisExecutor = redisExecutor;
        _mongoExecutor = mongoExecutor;
        _isDryRun = isDryRun;
        _canWrite = canWrite;
        _cancellationToken = cancellationToken;
        _family = DataSourceProvider.GetFamily(dataSource.Provider);
    }

    // —— SQL ——
    public object query(string sql, object? args = null) => Query(sql, args);
    public object execute(string sql, object? args = null) => Execute(sql, args);
    public object batch(string sql, object? paramList = null) => Batch(sql, paramList);

    public object Query(string sql, object? args = null)
    {
        EnsureFamily(DataSourceProvider.FamilySql, "db.query");
        var result = _sqlExecutor
            .QueryAsync(_dataSource, sql, ToJsonObject(args), _isDryRun, _cancellationToken)
            .GetAwaiter().GetResult();
        return ToSqlResult(result, includeRows: true);
    }

    public object Execute(string sql, object? args = null)
    {
        EnsureFamily(DataSourceProvider.FamilySql, "db.execute");
        EnsureSqlWrite(sql);
        var result = _sqlExecutor
            .ExecuteAsync(_dataSource, sql, ToJsonObject(args), _isDryRun, _cancellationToken)
            .GetAwaiter().GetResult();
        return ToSqlResult(result, includeRows: false);
    }

    public object Batch(string sql, object? paramList = null)
    {
        EnsureFamily(DataSourceProvider.FamilySql, "db.batch");
        EnsureSqlWrite(sql);
        var result = _sqlExecutor
            .BatchAsync(_dataSource, sql, ToJsonArray(paramList), _isDryRun, _cancellationToken)
            .GetAwaiter().GetResult();
        return ToSqlResult(result, includeRows: false);
    }

    // —— Redis ——
    public object? get(string key) => Get(key);
    public object set(string key, object? value, object? options = null) => Set(key, value, options);
    public object del(object keys) => Del(keys);
    public object mget(object keys) => MGet(keys);
    public object mset(object map) => MSet(map);

    public object? Get(string key)
    {
        EnsureFamily(DataSourceProvider.FamilyRedis, "db.get");
        return _redisExecutor.GetAsync(_dataSource, key, _isDryRun, _cancellationToken).GetAwaiter().GetResult();
    }

    public object Set(string key, object? value, object? options = null)
    {
        EnsureFamily(DataSourceProvider.FamilyRedis, "db.set");
        int? ttl = null;
        if (options != null)
        {
            var opt = ToJsonObject(options);
            ttl = JsonNodeNumbers.ToInt32(opt?["ttl"]);
        }

        return _redisExecutor
            .SetAsync(_dataSource, key, value, ttl, _isDryRun, _canWrite, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    public object Del(object keys)
    {
        EnsureFamily(DataSourceProvider.FamilyRedis, "db.del");
        return _redisExecutor
            .DelAsync(_dataSource, ToStringList(keys), _isDryRun, _canWrite, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    public object MGet(object keys)
    {
        EnsureFamily(DataSourceProvider.FamilyRedis, "db.mget");
        return _redisExecutor
            .MGetAsync(_dataSource, ToStringList(keys), _isDryRun, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    public object MSet(object map)
    {
        EnsureFamily(DataSourceProvider.FamilyRedis, "db.mset");
        var obj = ToJsonObject(map) ?? throw new UserFriendlyException("db.mset expects an object map.");
        return _redisExecutor
            .MSetAsync(_dataSource, obj, _isDryRun, _canWrite, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    // —— MongoDB ——
    public object find(string collection, object? filter = null, object? options = null) =>
        Find(collection, filter, options);

    public object? findOne(string collection, object? filter = null) => FindOne(collection, filter);

    public object insertOne(string collection, object doc) => InsertOne(collection, doc);

    public object insertMany(string collection, object docs) => InsertMany(collection, docs);

    public object updateOne(string collection, object filter, object update) =>
        UpdateOne(collection, filter, update);

    public object deleteMany(string collection, object filter) => DeleteMany(collection, filter);

    public object Find(string collection, object? filter = null, object? options = null)
    {
        EnsureFamily(DataSourceProvider.FamilyMongo, "db.find");
        return _mongoExecutor
            .FindAsync(
                _dataSource,
                collection,
                ToJsonObject(filter),
                ToJsonObject(options),
                _isDryRun,
                _cancellationToken
            )
            .GetAwaiter().GetResult();
    }

    public object? FindOne(string collection, object? filter = null)
    {
        EnsureFamily(DataSourceProvider.FamilyMongo, "db.findOne");
        return _mongoExecutor
            .FindOneAsync(_dataSource, collection, ToJsonObject(filter), _isDryRun, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    public object InsertOne(string collection, object doc)
    {
        EnsureFamily(DataSourceProvider.FamilyMongo, "db.insertOne");
        var o = ToJsonObject(doc) ?? throw new UserFriendlyException("insertOne doc must be an object.");
        return _mongoExecutor
            .InsertOneAsync(_dataSource, collection, o, _isDryRun, _canWrite, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    public object InsertMany(string collection, object docs)
    {
        EnsureFamily(DataSourceProvider.FamilyMongo, "db.insertMany");
        var arr = ToJsonArray(docs) ?? throw new UserFriendlyException("insertMany expects an array.");
        return _mongoExecutor
            .InsertManyAsync(_dataSource, collection, arr, _isDryRun, _canWrite, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    public object UpdateOne(string collection, object filter, object update)
    {
        EnsureFamily(DataSourceProvider.FamilyMongo, "db.updateOne");
        var f = ToJsonObject(filter) ?? [];
        var u = ToJsonObject(update) ?? throw new UserFriendlyException("update must be an object.");
        return _mongoExecutor
            .UpdateOneAsync(_dataSource, collection, f, u, _isDryRun, _canWrite, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    public object DeleteMany(string collection, object filter)
    {
        EnsureFamily(DataSourceProvider.FamilyMongo, "db.deleteMany");
        var f = ToJsonObject(filter) ?? [];
        return _mongoExecutor
            .DeleteManyAsync(_dataSource, collection, f, _isDryRun, _canWrite, _cancellationToken)
            .GetAwaiter().GetResult();
    }

    private void EnsureFamily(string expected, string api)
    {
        if (!string.Equals(_family, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(
                $"{api} requires a '{expected}' data source. Current '{_dataSource.Provider}' is '{_family}'."
            );
        }
    }

    private void EnsureSqlWrite(string sql)
    {
        var trimmed = sql.TrimStart();
        var isWrite = !(trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("with", StringComparison.OrdinalIgnoreCase));
        if (isWrite && !_canWrite)
        {
            throw new UserFriendlyException(
                "Missing permission Orchestration.Sql.Write for database write operations."
            );
        }
    }

    private static JsonObject? ToJsonObject(object? args)
    {
        if (args == null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(args, FlowExecutor.JsonOptions);
        return node as JsonObject
               ?? throw new UserFriendlyException("Expected a plain object argument.");
    }

    private static JsonArray? ToJsonArray(object? paramList)
    {
        if (paramList == null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(paramList, FlowExecutor.JsonOptions);
        return node as JsonArray
               ?? throw new UserFriendlyException("Expected an array argument.");
    }

    private static List<string> ToStringList(object keys)
    {
        if (keys is string s)
        {
            return [s];
        }

        var arr = ToJsonArray(keys)
                  ?? throw new UserFriendlyException("Expected string or string[].");
        return arr.Select(x => x?.GetValue<string>()
                               ?? throw new UserFriendlyException("Keys must be strings.")).ToList();
    }

    private static object ToSqlResult(ParameterizedSqlResult result, bool includeRows)
    {
        if (includeRows)
        {
            return new
            {
                rows = result.Rows == null
                    ? Array.Empty<object>()
                    : JsonSerializer.Deserialize<object[]>(result.Rows.ToJsonString(), FlowExecutor.JsonOptions),
                affectedRows = result.AffectedRows,
                sqlFingerprint = result.SqlFingerprint
            };
        }

        return new
        {
            affectedRows = result.AffectedRows,
            sqlFingerprint = result.SqlFingerprint
        };
    }
}
