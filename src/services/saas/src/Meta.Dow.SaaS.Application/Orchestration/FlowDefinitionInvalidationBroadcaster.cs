using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 跨副本的已发布流程失效广播。
///
/// <para><b>缺陷 A5 / C3 的机理</b>：<c>PublishedFlowCache</c> 是进程内 <c>IMemoryCache</c>，
/// 失效只挂在 <c>PublishAsync</c> 一个调用点，且 <c>Disable</c> / <c>Delete</c> 完全不失效。
/// 多副本部署下每个副本各持一份 DSL 快照，最长 10 分钟后才会自然过期——
/// 已停用、已删除的流程在其他副本上仍可被调用。</para>
///
/// <para><b>为什么用版本号而不是直接删共享缓存</b>：直接把 DSL 放进 <c>IDistributedCache</c>
/// 反序列化会带来跨语言/跨版本格式兼容问题，且热路径每��都要走网络。这里改成
/// <b>「共享一个单调递增的失效版本号，本地快照记住自己读到的版本号」</b>：
/// 发布/停用/删除时只在 Redis 上自增一个计数器（一次网络往返，常数级），
/// 各副本在下次读取本地快照前比对版本号，不一致则丢弃本地快照重新加载。
/// 进程崩溃自然丢失本地快照，重启后重新读取，对正确性无影响。</para>
///
/// <para><b>一致性取舍</b>：这是<b>最终一致</b>——失效通知在副本下一次读取时生效，
/// 而不是一个强一致的推送。若业务要求「停用后零窗口」，仍需在调用入口二次校验状态（见
/// 评估文档 A5 行）。相比之下强一致需要每次调用都走网络，代价与收益不成比例。</para>
/// </summary>
public class FlowDefinitionInvalidationBroadcaster : ITransientDependency
{
    /// <summary>Redis 中的失效版本号键（AbpDistributedCacheOptions 会自动加 KeyPrefix）。</summary>
    private const string GenerationKey = "orch:pub:generation";

    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<FlowDefinitionInvalidationBroadcaster> _logger;

    public FlowDefinitionInvalidationBroadcaster(
        IDistributedCache distributedCache,
        ILogger<FlowDefinitionInvalidationBroadcaster> logger
    )
    {
        _distributedCache = distributedCache;
        _logger = logger;
    }

    /// <summary>
    /// 读取当前全局失效版本号。读不到（Redis 不可用）时返回 0，
    /// 此时本地缓存行为退化为原先的进程内语义，不会因此报错阻断调用。
    /// </summary>
    public async Task<long> GetGenerationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var raw = await _distributedCache.GetStringAsync(
                GenerationKey,
                cancellationToken
            );

            return long.TryParse(raw, out var value) ? value : 0L;
        }
        catch (Exception ex)
        {
            // 失效广播是加固措施，不能反过来成为可用性单点。
            _logger.LogWarning(ex, "Read published-flow invalidation generation failed; degrading to local cache.");
            return 0L;
        }
    }

    /// <summary>
    /// 广播一次失效：自增全局版本号。发布、停用、删除都必须调用。
    /// </summary>
    public async Task BroadcastAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var current = await GetGenerationAsync(cancellationToken);

            // Redis 侧用 INCR 语义不可用（IDistributedCache 无原子自增），
            // 这里用「读—改—写」。竞争窗口极小，且后果只是多一次或漏一次缓存重建，
            // 不影响正确性：漏掉的代价是本副本多缓存一个版本，下次读仍会比对。
            await _distributedCache.SetStringAsync(
                GenerationKey,
                (current + 1).ToString(),
                new DistributedCacheEntryOptions
                {
                    // 不设过期：这是单调计数器，重启后从任意值继续仍能正确比较。
                    AbsoluteExpirationRelativeToNow = null,
                    SlidingExpiration = null
                },
                cancellationToken
            );

            _logger.LogInformation(
                "Broadcast published-flow invalidation, generation {Generation}.",
                current + 1
            );
        }
        catch (Exception ex)
        {
            // 同上：广播失败只降级，不阻断发布/停用/删除本身。
            _logger.LogWarning(ex, "Broadcast published-flow invalidation failed; other replicas may serve stale DSL until TTL.");
        }
    }
}
