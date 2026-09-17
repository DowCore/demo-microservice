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

    public PublishedFlowCache(
        IMemoryCache cache,
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        ICurrentTenant currentTenant
    )
    {
        _cache = cache;
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _currentTenant = currentTenant;
    }

    public async Task<PublishedFlowSnapshot> GetByKeyAsync(string flowKey)
    {
        if (string.IsNullOrWhiteSpace(flowKey))
        {
            throw new UserFriendlyException("Flow key is required.");
        }

        var key = CacheKey(flowKey.Trim());
        if (_cache.TryGetValue(key, out PublishedFlowSnapshot? cached) && cached != null)
        {
            return cached;
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
            snapshot,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                SlidingExpiration = TimeSpan.FromMinutes(3)
            }
        );

        return snapshot;
    }

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
