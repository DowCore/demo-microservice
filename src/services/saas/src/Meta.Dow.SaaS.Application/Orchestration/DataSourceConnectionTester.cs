using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public interface IDataSourceConnectionTester
{
    Task<DataSourceTestResultDto> TestAsync(
        string provider,
        string connectionString,
        CancellationToken cancellationToken = default
    );
}

public class DataSourceConnectionTester : IDataSourceConnectionTester, ITransientDependency
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public async Task<DataSourceTestResultDto> TestAsync(
        string provider,
        string connectionString,
        CancellationToken cancellationToken = default
    )
    {
        var sw = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var p = DataSourceProvider.Normalize(provider);
            var family = DataSourceProvider.GetFamily(p);
            if (family == DataSourceProvider.FamilySql)
            {
                await ProbeSqlAsync(p, connectionString, timeout.Token);
            }
            else if (family == DataSourceProvider.FamilyRedis)
            {
                await ProbeRedisAsync(connectionString);
            }
            else if (family == DataSourceProvider.FamilyMongo)
            {
                await ProbeMongoAsync(connectionString, timeout.Token);
            }
            else
            {
                throw new UserFriendlyException($"Unsupported data source provider: {provider}");
            }

            sw.Stop();
            return new DataSourceTestResultDto
            {
                Success = true,
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                Message = $"已连通 {DataSourceProvider.DisplayName(p)}"
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return Fail(sw, "连接超时（8 秒）。请检查主机、端口与防火墙。");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Fail(sw, Sanitize(ex));
        }
    }

    private static async Task ProbeSqlAsync(string provider, string connectionString, CancellationToken cancellationToken)
    {
        var probe = new DataSource(
            Guid.Empty,
            "probe",
            "probe",
            provider,
            connectionString,
            DataSourceAccessMode.ReadWrite
        );
        await using var conn = SqlDialect.CreateConnection(probe);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = (int)Timeout.TotalSeconds;
        cmd.CommandText = DataSourceProvider.Normalize(provider) == DataSourceProvider.Oracle
            ? "SELECT 1 FROM DUAL"
            : "SELECT 1";
        await cmd.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task ProbeRedisAsync(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = true;
        options.ConnectTimeout = (int)Timeout.TotalMilliseconds;
        options.SyncTimeout = (int)Timeout.TotalMilliseconds;
        using var mux = await ConnectionMultiplexer.ConnectAsync(options);
        if (!mux.IsConnected)
        {
            throw new UserFriendlyException("Redis 未能建立连接。");
        }

        await mux.GetDatabase().PingAsync();
    }

    private static async Task ProbeMongoAsync(string connectionString, CancellationToken cancellationToken)
    {
        var url = MongoUrl.Create(connectionString);
        var settings = MongoClientSettings.FromUrl(url);
        settings.ConnectTimeout = Timeout;
        settings.ServerSelectionTimeout = Timeout;
        settings.SocketTimeout = Timeout;
        var client = new MongoClient(settings);
        var dbName = string.IsNullOrWhiteSpace(url.DatabaseName) ? "admin" : url.DatabaseName;
        await client
            .GetDatabase(dbName)
            .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
    }

    private static DataSourceTestResultDto Fail(Stopwatch sw, string message)
    {
        return new DataSourceTestResultDto
        {
            Success = false,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
            Message = message
        };
    }

    private static string Sanitize(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null && current is not UserFriendlyException)
        {
            current = current.InnerException;
        }

        var text = current.Message?.Trim() ?? "连接失败。";
        if (text.Length > 400)
        {
            text = text[..400] + "…";
        }

        return text;
    }
}
