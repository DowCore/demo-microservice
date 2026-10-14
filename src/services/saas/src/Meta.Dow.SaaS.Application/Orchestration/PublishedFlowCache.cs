using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

public sealed class PublishedFlowSnapshot
{
    public Guid DefinitionId { get; init; }

    public string DefinitionName { get; init; } = null!;

    public string FlowKey { get; init; } = null!;

    public int Version { get; init; }

    public string DslJson { get; init; } = null!;

    /// <summary>已校验的 DSL；执行前须 DeepClone 节点树或仅只读使用。</summary>
    public FlowDslDocument Dsl { get; init; } = null!;
}

/// <summary>
/// 已发布流程热路径缓存：避免每次 Logic API 两次 Mongo 查询 + 重复反序列化/校验 DSL。
///
/// <para><b>A5：本地快照与跨副本失效</b>。每份快照记住自己加载时的全局失效版本号
/// （见 <see cref="FlowDefinitionInvalidationBroadcaster"/>）。读取前先比对版本号，
/// 不一致就丢弃本地快照重新加载，从而让发布/停用/删除在多副本下都能生效，
/// 而不必把 DSL 本体放进分布式缓存。</para>
/// </summary>
public class PublishedFlowCache : ITransientDependency
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IMemoryCache _cache;
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IRepository<FlowVersion, Guid> _versionRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly FlowDefinitionInvalidationBroadcaster _broadcaster;

    public PublishedFlowCache(
        IMemoryCache cache,
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        ICurrentTenant currentTenant,
        FlowDefinitionInvalidationBroadcaster broadcaster
    )
    {
        _cache = cache;
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _currentTenant = currentTenant;
        _broadcaster = broadcaster;
    }

    public async Task<PublishedFlowSnapshot> GetByKeyAsync(string flowKey)
    {
        if (string.IsNullOrWhiteSpace(flowKey))
        {
            throw new UserFriendlyException("Flow key is required.");
        }

        var key = CacheKey(flowKey.Trim());

        // A5：命中本地快照前，先确认它不是被其他副本的发布/停用/删除作废的旧版本。
        var generation = await _broadcaster.GetGenerationAsync();

        if (_cache.TryGetValue(key, out CachedFlow? cached) && cached != null)
        {
            if (cached.Generation == generation)
            {
                return cached.Snapshot;
            }

            _cache.Remove(key);
        }

        var definition = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == flowKey.Trim());
        if (definition == null)
        {
            throw new UserFriendlyException($"Flow '{flowKey}' was not found.");
        }

        if (definition.Status != FlowDefinitionStatus.Published || !definition.PublishedVersion.HasValue)
        {
            throw new UserFriendlyException("Flow is not published.");
        }

        var versionNo = definition.PublishedVersion.Value;
        var version = await _versionRepository.FirstOrDefaultAsync(x =>
            x.DefinitionId == definition.Id && x.Version == versionNo
        );
        if (version == null || string.IsNullOrWhiteSpace(version.DslJson))
        {
            throw new UserFriendlyException($"Published version {versionNo} was not found.");
        }

        var dsl =
            JsonSerializer.Deserialize<FlowDslDocument>(version.DslJson, JsonOptions)
            ?? throw new UserFriendlyException("DSL is empty.");
        FlowExecutor.ValidateDsl(dsl);

        var snapshot = new PublishedFlowSnapshot
        {
            DefinitionId = definition.Id,
            DefinitionName = definition.Name,
            FlowKey = definition.Code,
            Version = versionNo,
            DslJson = version.DslJson,
            Dsl = dsl
        };

        _cache.Set(
            key,
            new CachedFlow(snapshot, generation),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                SlidingExpiration = TimeSpan.FromMinutes(3)
            }
        );

        return snapshot;
    }

    /// <summary>本地缓存条目：快照 + 加载时的全局失效版本号。</summary>
    private sealed record CachedFlow(PublishedFlowSnapshot Snapshot, long Generation);

    public void Invalidate(string flowKey)
    {
        if (string.IsNullOrWhiteSpace(flowKey))
        {
            return;
        }

        _cache.Remove(CacheKey(flowKey.Trim()));
    }

    public void InvalidateByDefinition(FlowDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Code))
        {
            return;
        }

        Invalidate(definition.Code);
    }

    private string CacheKey(string flowKey) =>
        $"orch:pub:{_currentTenant.Id?.ToString() ?? "host"}:{flowKey}";
}
