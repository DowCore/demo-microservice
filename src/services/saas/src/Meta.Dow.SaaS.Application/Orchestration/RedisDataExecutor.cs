using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public interface IRedisDataExecutor
{
    Task<object?> GetAsync(DataSource ds, string key, bool isDryRun, CancellationToken ct = default);

    Task<object> SetAsync(
        DataSource ds,
        string key,
        object? value,
        int? ttlSeconds,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    );

    Task<object> DelAsync(DataSource ds, IReadOnlyList<string> keys, bool isDryRun, bool canWrite, CancellationToken ct = default);

    Task<object> MGetAsync(DataSource ds, IReadOnlyList<string> keys, bool isDryRun, CancellationToken ct = default);

    Task<object> MSetAsync(
        DataSource ds,
        JsonObject map,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    );
}

public class RedisDataExecutor : IRedisDataExecutor, ITransientDependency
{
    private readonly ILogger<RedisDataExecutor> _logger;

    public RedisDataExecutor(ILogger<RedisDataExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<object?> GetAsync(DataSource ds, string key, bool isDryRun, CancellationToken ct = default)
    {
        EnsureRedis(ds);
        EnsureOp(ds, "get");
        key = Check.NotNullOrWhiteSpace(key, nameof(key));
        if (isDryRun)
        {
            return null;
        }

        var mux = await ConnectAsync(ds, ct);
        try
        {
            var db = mux.GetDatabase();
            var value = await db.StringGetAsync(key);
            _logger.LogInformation("[CodeRedis] get ds={DataSource} keyLen={Len} hit={Hit}", ds.Code, key.Length, !value.IsNull);
            return value.IsNull ? null : value.ToString();
        }
        finally
        {
            mux.Dispose();
        }
    }

    public async Task<object> SetAsync(
        DataSource ds,
        string key,
        object? value,
        int? ttlSeconds,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    )
    {
        EnsureRedis(ds);
        EnsureOp(ds, "set");
        EnsureWrite(canWrite);
        key = Check.NotNullOrWhiteSpace(key, nameof(key));
        if (isDryRun)
        {
            return new { ok = true, dryRun = true, affectedRows = 0 };
        }

        var mux = await ConnectAsync(ds, ct);
        try
        {
            var db = mux.GetDatabase();
            var text = SerializeValue(value);
            bool ok;
            if (ttlSeconds is > 0)
            {
                ok = await db.StringSetAsync(key, text, TimeSpan.FromSeconds(ttlSeconds.Value));
            }
            else
            {
                ok = await db.StringSetAsync(key, text);
            }

            _logger.LogInformation("[CodeRedis] set ds={DataSource} keyLen={Len} ttl={Ttl} ok={Ok}", ds.Code, key.Length, ttlSeconds, ok);
            return new { ok, affectedRows = ok ? 1 : 0 };
        }
        finally
        {
            mux.Dispose();
        }
    }

    public async Task<object> DelAsync(
        DataSource ds,
        IReadOnlyList<string> keys,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    )
    {
        EnsureRedis(ds);
        EnsureOp(ds, "del");
        EnsureWrite(canWrite);
        if (keys.Count == 0)
        {
            return new { affectedRows = 0 };
        }

        if (keys.Count > OrchestrationConsts.CodeDbBatchMaxRows)
        {
            throw new UserFriendlyException($"Too many Redis keys (max {OrchestrationConsts.CodeDbBatchMaxRows}).");
        }

        if (isDryRun)
        {
            return new { affectedRows = 0, dryRun = true };
        }

        var mux = await ConnectAsync(ds, ct);
        try
        {
            var db = mux.GetDatabase();
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            var n = await db.KeyDeleteAsync(redisKeys);
            _logger.LogInformation("[CodeRedis] del ds={DataSource} count={Count} affected={Affected}", ds.Code, keys.Count, n);
            return new { affectedRows = (int)n };
        }
        finally
        {
            mux.Dispose();
        }
    }

    public async Task<object> MGetAsync(DataSource ds, IReadOnlyList<string> keys, bool isDryRun, CancellationToken ct = default)
    {
        EnsureRedis(ds);
        EnsureOp(ds, "mget");
        if (keys.Count == 0)
        {
            return new { rows = Array.Empty<object>(), affectedRows = 0 };
        }

        if (keys.Count > OrchestrationConsts.CodeDbQueryMaxRows)
        {
            throw new UserFriendlyException($"Too many Redis keys (max {OrchestrationConsts.CodeDbQueryMaxRows}).");
        }

        if (isDryRun)
        {
            return new { rows = Array.Empty<object>(), affectedRows = 0, dryRun = true };
        }

        var mux = await ConnectAsync(ds, ct);
        try
        {
            var db = mux.GetDatabase();
            var values = await db.StringGetAsync(keys.Select(k => (RedisKey)k).ToArray());
            var rows = new List<object>();
            for (var i = 0; i < keys.Count; i++)
            {
                rows.Add(new
                {
                    key = keys[i],
                    value = values[i].IsNull ? null : values[i].ToString()
                });
            }

            _logger.LogInformation("[CodeRedis] mget ds={DataSource} count={Count}", ds.Code, keys.Count);
            return new { rows, affectedRows = rows.Count };
        }
        finally
        {
            mux.Dispose();
        }
    }

    public async Task<object> MSetAsync(
        DataSource ds,
        JsonObject map,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    )
    {
        EnsureRedis(ds);
        EnsureOp(ds, "mset");
        EnsureWrite(canWrite);
        ArgumentNullException.ThrowIfNull(map);
        if (map.Count > OrchestrationConsts.CodeDbBatchMaxRows)
        {
            throw new UserFriendlyException($"db.mset exceeds limit {OrchestrationConsts.CodeDbBatchMaxRows}.");
        }

        if (isDryRun)
        {
            return new { ok = true, affectedRows = 0, dryRun = true };
        }

        var mux = await ConnectAsync(ds, ct);
        try
        {
            var db = mux.GetDatabase();
            var pairs = map
                .Select(kv => new KeyValuePair<RedisKey, RedisValue>(kv.Key, SerializeValue(kv.Value)))
                .ToArray();
            var ok = await db.StringSetAsync(pairs);
            _logger.LogInformation("[CodeRedis] mset ds={DataSource} count={Count} ok={Ok}", ds.Code, pairs.Length, ok);
            return new { ok, affectedRows = ok ? pairs.Length : 0 };
        }
        finally
        {
            mux.Dispose();
        }
    }

    private static void EnsureRedis(DataSource ds)
    {
        if (!DataSourceProvider.IsRedis(ds.Provider))
        {
            throw new UserFriendlyException($"Data source '{ds.Code}' is not Redis.");
        }
    }

    private static void EnsureOp(DataSource ds, string op)
    {
        if (!ds.AllowsOp(op))
        {
            throw new UserFriendlyException($"Data source '{ds.Code}' does not allow '{op}'.");
        }
    }

    private static void EnsureWrite(bool canWrite)
    {
        if (!canWrite)
        {
            throw new UserFriendlyException("Missing permission Orchestration.Sql.Write for Redis write.");
        }
    }

    private static async Task<ConnectionMultiplexer> ConnectAsync(DataSource ds, CancellationToken ct)
    {
        var options = ConfigurationOptions.Parse(ds.ConnectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds * 1000;
        options.SyncTimeout = OrchestrationConsts.CodeDbCommandTimeoutSeconds * 1000;
        return await ConnectionMultiplexer.ConnectAsync(options);
    }

    private static string SerializeValue(object? value)
    {
        if (value == null)
        {
            return "";
        }

        if (value is string s)
        {
            return s;
        }

        if (value is JsonNode node)
        {
            return node is JsonValue jv && jv.GetValueKind() == JsonValueKind.String
                ? jv.GetValue<string>()
                : node.ToJsonString(FlowExecutor.JsonOptions);
        }

        if (value is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
        {
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }

        return JsonSerializer.Serialize(value, FlowExecutor.JsonOptions);
    }
}
