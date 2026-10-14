using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 编排节点级弹性策略（非 HTTP 通道）。
///
/// 背景：HTTP 出站走 <c>AddStandardResilienceHandler</c>（ServiceDefaults 全局注册），
/// 但 DB / Redis / Mongo 节点不经过 HttpClient，因此完全不受该策略保护。
/// 本策略为这些通道提供**同一份**读写分离的弹性语义，避免四个执行器各写一套。
///
/// 语义（与 R1 的安全默认值保持一致）：
/// - **读操作**（query/get/find/mget…）：瞬态异常自动重试（默认 2 次，指数退避 + jitter）。
/// - **写操作**（create/update/delete/set/del…）：**默认不重试**。写重试必须先有幂等保护，
///   该前提由 R6（幂等键）提供；在此之前不允许隐式重试，以免放大重复副作用。
/// </summary>
public interface INodeResiliencePolicy
{
    /// <summary>
    /// 在弹性和可观测之下执行一次外部数据访问。
    /// </summary>
    /// <param name="nodeId">节点 Id，仅用于日志定位。</param>
    /// <param name="operation">操作名（如 query/create），用于日志与策略选择。</param>
    /// <param name="isWrite">true = 写操作（默认不重试）；false = 读操作（可重试）。</param>
    /// <param name="action">实际动作。</param>
    /// <param name="onTransientRetry">重试前的回调（可用于记录一条节点级提示）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<T> ExecuteAsync<T>(
        string nodeId,
        string operation,
        bool isWrite,
        Func<CancellationToken, Task<T>> action,
        Action<Exception, int>? onTransientRetry = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 同上，但允许调用方显式覆盖「写操作是否可重试」。
    /// 仅供已确认具备幂等保护的场景（DSL 的 retryOnWrite + idempotencyKey 校验通过）使用。
    /// </summary>
    Task<T> ExecuteAsync<T>(
        string nodeId,
        string operation,
        bool isWrite,
        bool allowWriteRetry,
        Func<CancellationToken, Task<T>> action,
        Action<Exception, int>? onTransientRetry = null,
        CancellationToken cancellationToken = default
    );
}

public class NodeResiliencePolicy : INodeResiliencePolicy, ITransientDependency
{
    /// <summary>读操作的默认重试次数（不含首次）。</summary>
    public const int DefaultReadMaxRetries = 2;

    /// <summary>指数退避基数。</summary>
    public const int BaseRetryDelayMs = 500;

    private readonly ILogger<NodeResiliencePolicy> _logger;

    public NodeResiliencePolicy(ILogger<NodeResiliencePolicy> logger)
    {
        _logger = logger;
    }

    public Task<T> ExecuteAsync<T>(
        string nodeId,
        string operation,
        bool isWrite,
        Func<CancellationToken, Task<T>> action,
        Action<Exception, int>? onTransientRetry = null,
        CancellationToken cancellationToken = default
    ) => ExecuteAsync(nodeId, operation, isWrite, allowWriteRetry: false, action, onTransientRetry, cancellationToken);

    public async Task<T> ExecuteAsync<T>(
        string nodeId,
        string operation,
        bool isWrite,
        bool allowWriteRetry,
        Func<CancellationToken, Task<T>> action,
        Action<Exception, int>? onTransientRetry = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(action);

        // 写操作仅在显式允许（且已通过幂等校验）时才重试；读操作默认可重试。
        var maxRetries = isWrite && !allowWriteRetry ? 0 : DefaultReadMaxRetries;
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(cancellationToken);
            }
            catch (Exception ex) when (
                attempt < maxRetries &&
                IsTransient(ex) &&
                !cancellationToken.IsCancellationRequested
            )
            {
                attempt++;
                var delay = ComputeDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "编排节点瞬时故障，正在重试。Node={NodeId} Op={Op} IsWrite={IsWrite} Attempt={Attempt}/{Max} DelayMs={Delay}",
                    nodeId,
                    operation,
                    isWrite,
                    attempt,
                    maxRetries,
                    delay
                );
                onTransientRetry?.Invoke(ex, attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>指数退避 + 全抖动，抑制重试风暴（与 R1 的 UseJitter 一致）。</summary>
    private static int ComputeDelay(int attempt)
    {
        var exponential = BaseRetryDelayMs * (int)Math.Pow(2, attempt - 1);
        var capped = Math.Min(exponential, 8_000);
        // 全抖动：0 ~ capped
        return Random.Shared.Next(0, capped + 1);
    }

    /// <summary>
    /// 判定是否为「重试有意义」的瞬态异常。
    /// 刻意**不重试** UserFriendlyException（业务语义失败：字段校验、权限、唯一约束），
    /// 也不重试取消异常。
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is UserFriendlyException or OperationCanceledException)
        {
            return false;
        }

        return ex switch
        {
            // SQL 家族：连接中断 / 超时 / 死锁 / 快照隔离冲突，由驱动抛出可重试错误码
            System.Data.Common.DbException dbEx => IsTransientDbError(dbEx),
            // Mongo：连接类 / 可重试写冲突 / 网络超时
            MongoException mongoEx => IsTransientMongoError(mongoEx),
            // Redis：连接断开 / 超时 / 集群重定向
            RedisException => true,
            // 网络类
            System.Net.Sockets.SocketException => true,
            TimeoutException => true,
            IOException => true,
            _ => false
        };
    }

    private static bool IsTransientDbError(System.Data.Common.DbException ex)
    {
        // PostgreSQL 的 SqlState 是 5 位**字母数字**串（40001 / 40P01 / 08006），
        // 不能走 int.TryParse —— "40P01" 会解析失败退化成 0，导致死锁/序列化冲突/断连永不重试。
        // 这里单独按字符串前缀分类，不走数值通道。
        if (ex is Npgsql.PostgresException pg)
        {
            return IsTransientPostgresSqlState(pg.SqlState);
        }

        // 其余家族统一按「连接/超时/死锁/序列化冲突」这类可重试错误码判断，跨库取并集。
        var number = ex switch
        {
            Microsoft.Data.SqlClient.SqlException sql => sql.Number,
            MySqlConnector.MySqlException my => my.Number,
            Npgsql.NpgsqlException => -1, // 非 PostgresException：网络/连接层
            Oracle.ManagedDataAccess.Client.OracleException ora => ora.Number,
            _ => 0
        };

        return number switch
        {
            // SQL Server: 超时、死锁、连接中断、传输级错误
            -2 or 1205 or 4060 or 233 or 64 or 10053 or 10054 or 10060 => true,
            // MySQL: 死锁、锁等待超时、连接丢失、过多连接
            1213 or 1205 or 2006 or 2013 or 1040 => true,
            // Oracle（ORA-xxxxx）：死锁、无法序列化、通信通道中断、未连接、TNS 超时/无监听、实例关停中
            60 or 8177 or 3113 or 3114 or 12170 or 12541 or 12571 or 12547 or 12537 or 1033 or 1089 => true,
            -1 => true, // Npgsql 非 PostgresException（网络/连接层）
            0 => false,
            _ => false
        };
    }

    /// <summary>
    /// PostgreSQL SqlState 分类（SQLSTATE 规范 + PG 扩展）。可重试集合：
    /// <c>08xxx</c> 连接异常；<c>40001</c> 序列化失败；<c>40P01</c> 死锁；
    /// <c>53xxx</c> 资源不足（内存/连接数/配置上限）；<c>55P03</c> 锁不可用；
    /// <c>57P01~57P03</c> 实例关停/崩溃恢复/暂不可连接。
    /// </summary>
    /// <remarks>internal 便于单测直测（Npgsql 的 PostgresException 无法在测试中构造）。</remarks>
    internal static bool IsTransientPostgresSqlState(string? sqlState)
    {
        if (string.IsNullOrWhiteSpace(sqlState) || sqlState.Length < 2)
        {
            return false;
        }

        // 连接异常类（08 前缀：08000/08001/08003/08006/08004/08007…）
        if (sqlState.StartsWith("08", StringComparison.Ordinal))
        {
            return true;
        }

        return sqlState switch
        {
            "40001" => true, // serialization_failure
            "40P01" => true, // deadlock_detected
            "53200" => true, // out_of_memory
            "53300" => true, // too_many_connections
            "53400" => true, // configuration_limit_exceeded
            "55P03" => true, // lock_not_available
            "57P01" => true, // admin_shutdown
            "57P02" => true, // crash_shutdown
            "57P03" => true, // cannot_connect_now
            _ => false
        };
    }

    private static bool IsTransientMongoError(MongoException ex)
    {
        // Mongo 的可重试错误类型（Network / NotPrimary / ShutdownInProgress / HostUnreachable /
        // HostNotFound / SocketException / ExceededTimeLimit）以及写冲突（DuplicateKey 除外）。
        var type = ex.GetType().Name;
        return ex switch
        {
            MongoConnectionException => true,
            MongoNotPrimaryException => true,
            MongoNodeIsRecoveringException => true,
            MongoCommandException cmd when cmd.Code is 11600 or 11602 or 10107 or 13435 or 13436 or 189 or 91 or 7 or 6 => true,
            _ => type.Contains("RetryableWrite", StringComparison.OrdinalIgnoreCase)
        };
    }
}
