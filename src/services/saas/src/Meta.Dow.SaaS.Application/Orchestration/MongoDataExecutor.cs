using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public interface IMongoDataExecutor
{
    Task<object> FindAsync(
        DataSource ds,
        string collection,
        JsonObject? filter,
        JsonObject? options,
        bool isDryRun,
        CancellationToken ct = default
    );

    Task<object?> FindOneAsync(
        DataSource ds,
        string collection,
        JsonObject? filter,
        bool isDryRun,
        CancellationToken ct = default
    );

    Task<object> InsertOneAsync(
        DataSource ds,
        string collection,
        JsonObject doc,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    );

    Task<object> InsertManyAsync(
        DataSource ds,
        string collection,
        JsonArray docs,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    );

    Task<object> UpdateOneAsync(
        DataSource ds,
        string collection,
        JsonObject filter,
        JsonObject update,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    );

    Task<object> DeleteManyAsync(
        DataSource ds,
        string collection,
        JsonObject filter,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    );
}

public class MongoDataExecutor : IMongoDataExecutor, ITransientDependency
{
    private readonly ILogger<MongoDataExecutor> _logger;

    public MongoDataExecutor(ILogger<MongoDataExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<object> FindAsync(
        DataSource ds,
        string collection,
        JsonObject? filter,
        JsonObject? options,
        bool isDryRun,
        CancellationToken ct = default
    )
    {
        EnsureMongo(ds);
        EnsureOp(ds, "find");
        collection = RequireLiteralName(collection, "collection");
        if (isDryRun)
        {
            return new { rows = Array.Empty<object>(), affectedRows = 0, dryRun = true };
        }

        var coll = GetCollection(ds, collection);
        var filterDoc = ToBson(filter) ?? new BsonDocument();
        var find = coll.Find(filterDoc);
        if (options != null)
        {
            if (options["limit"] is JsonValue lim && lim.TryGetValue<int>(out var limit) && limit > 0)
            {
                find = find.Limit(Math.Min(limit, OrchestrationConsts.CodeDbQueryMaxRows));
            }
            else
            {
                find = find.Limit(OrchestrationConsts.CodeDbQueryMaxRows);
            }

            if (options["sort"] is JsonObject sort)
            {
                find = find.Sort(ToBson(sort));
            }

            if (options["projection"] is JsonObject proj)
            {
                find = find.Project(ToBson(proj));
            }
        }
        else
        {
            find = find.Limit(OrchestrationConsts.CodeDbQueryMaxRows);
        }

        var list = await find.ToListAsync(ct);
        var rows = list.Select(BsonToObject).ToArray();
        _logger.LogInformation("[CodeMongo] find ds={DataSource} coll={Coll} count={Count}", ds.Code, collection, rows.Length);
        return new { rows, affectedRows = rows.Length };
    }

    public async Task<object?> FindOneAsync(
        DataSource ds,
        string collection,
        JsonObject? filter,
        bool isDryRun,
        CancellationToken ct = default
    )
    {
        EnsureMongo(ds);
        EnsureOp(ds, "findone");
        collection = RequireLiteralName(collection, "collection");
        if (isDryRun)
        {
            return null;
        }

        var coll = GetCollection(ds, collection);
        var doc = await coll.Find(ToBson(filter) ?? new BsonDocument()).Limit(1).FirstOrDefaultAsync(ct);
        _logger.LogInformation("[CodeMongo] findOne ds={DataSource} coll={Coll} hit={Hit}", ds.Code, collection, doc != null);
        return doc == null ? null : BsonToObject(doc);
    }

    public async Task<object> InsertOneAsync(
        DataSource ds,
        string collection,
        JsonObject doc,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    )
    {
        EnsureMongo(ds);
        EnsureOp(ds, "insertone");
        EnsureWrite(canWrite);
        collection = RequireLiteralName(collection, "collection");
        ArgumentNullException.ThrowIfNull(doc);
        if (isDryRun)
        {
            return new { affectedRows = 0, dryRun = true };
        }

        var coll = GetCollection(ds, collection);
        var bson = ToBson(doc) ?? new BsonDocument();
        await coll.InsertOneAsync(bson, cancellationToken: ct);
        _logger.LogInformation("[CodeMongo] insertOne ds={DataSource} coll={Coll}", ds.Code, collection);
        return new
        {
            affectedRows = 1,
            insertedId = bson.Contains("_id") ? bson["_id"].ToString() : null
        };
    }

    public async Task<object> InsertManyAsync(
        DataSource ds,
        string collection,
        JsonArray docs,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    )
    {
        EnsureMongo(ds);
        EnsureOp(ds, "insertmany");
        EnsureWrite(canWrite);
        collection = RequireLiteralName(collection, "collection");
        ArgumentNullException.ThrowIfNull(docs);
        if (docs.Count > OrchestrationConsts.CodeDbBatchMaxRows)
        {
            throw new UserFriendlyException($"insertMany exceeds limit {OrchestrationConsts.CodeDbBatchMaxRows}.");
        }

        if (docs.Count == 0)
        {
            return new { affectedRows = 0 };
        }

        if (isDryRun)
        {
            return new { affectedRows = 0, dryRun = true };
        }

        var coll = GetCollection(ds, collection);
        var list = docs
            .Select(x => ToBson(x as JsonObject) ?? throw new UserFriendlyException("insertMany items must be objects."))
            .ToList();
        await coll.InsertManyAsync(list, cancellationToken: ct);
        _logger.LogInformation("[CodeMongo] insertMany ds={DataSource} coll={Coll} count={Count}", ds.Code, collection, list.Count);
        return new { affectedRows = list.Count };
    }

    public async Task<object> UpdateOneAsync(
        DataSource ds,
        string collection,
        JsonObject filter,
        JsonObject update,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    )
    {
        EnsureMongo(ds);
        EnsureOp(ds, "updateone");
        EnsureWrite(canWrite);
        collection = RequireLiteralName(collection, "collection");
        if (isDryRun)
        {
            return new { affectedRows = 0, dryRun = true };
        }

        var coll = GetCollection(ds, collection);
        var result = await coll.UpdateOneAsync(
            ToBson(filter) ?? new BsonDocument(),
            ToBson(update) ?? new BsonDocument(),
            cancellationToken: ct
        );
        _logger.LogInformation(
            "[CodeMongo] updateOne ds={DataSource} coll={Coll} matched={Matched} modified={Modified}",
            ds.Code,
            collection,
            result.MatchedCount,
            result.ModifiedCount
        );
        return new { affectedRows = (int)result.ModifiedCount, matched = (int)result.MatchedCount };
    }

    public async Task<object> DeleteManyAsync(
        DataSource ds,
        string collection,
        JsonObject filter,
        bool isDryRun,
        bool canWrite,
        CancellationToken ct = default
    )
    {
        EnsureMongo(ds);
        EnsureOp(ds, "deletemany");
        EnsureWrite(canWrite);
        collection = RequireLiteralName(collection, "collection");
        if (isDryRun)
        {
            return new { affectedRows = 0, dryRun = true };
        }

        var coll = GetCollection(ds, collection);
        var result = await coll.DeleteManyAsync(ToBson(filter) ?? new BsonDocument(), ct);
        _logger.LogInformation("[CodeMongo] deleteMany ds={DataSource} coll={Coll} deleted={Deleted}", ds.Code, collection, result.DeletedCount);
        return new { affectedRows = (int)result.DeletedCount };
    }

    private static void EnsureMongo(DataSource ds)
    {
        if (!DataSourceProvider.IsMongo(ds.Provider))
        {
            throw new UserFriendlyException($"Data source '{ds.Code}' is not MongoDB.");
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
            throw new UserFriendlyException("Missing permission Orchestration.Sql.Write for MongoDB write.");
        }
    }

    private static string RequireLiteralName(string name, string label)
    {
        var n = Check.NotNullOrWhiteSpace(name, label).Trim();
        if (n.Length > 128 || !System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_\.\-]*$"))
        {
            throw new UserFriendlyException($"Invalid {label} name.");
        }

        return n;
    }

    private static IMongoCollection<BsonDocument> GetCollection(DataSource ds, string collection)
    {
        var url = MongoUrl.Create(ds.ConnectionString);
        var client = new MongoClient(url);
        var dbName = string.IsNullOrWhiteSpace(url.DatabaseName) ? "admin" : url.DatabaseName;
        return client.GetDatabase(dbName).GetCollection<BsonDocument>(collection);
    }

    private static BsonDocument? ToBson(JsonObject? obj)
    {
        if (obj == null)
        {
            return null;
        }

        return BsonDocument.Parse(obj.ToJsonString(FlowExecutor.JsonOptions));
    }

    private static object BsonToObject(BsonDocument doc)
    {
        var json = doc.ToJson();
        return JsonSerializer.Deserialize<object>(json, FlowExecutor.JsonOptions) ?? new { };
    }
}
