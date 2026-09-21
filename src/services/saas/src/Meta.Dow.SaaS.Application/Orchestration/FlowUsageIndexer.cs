using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

public interface IFlowUsageIndexer
{
    Task RebuildForDefinitionAsync(FlowDefinition definition, CancellationToken cancellationToken = default);

    Task ClearForDefinitionAsync(Guid definitionId, CancellationToken cancellationToken = default);
}

public class FlowUsageIndexer : IFlowUsageIndexer, ITransientDependency
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRepository<FlowUsage, Guid> _usageRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public FlowUsageIndexer(
        IRepository<FlowUsage, Guid> usageRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant
    )
    {
        _usageRepository = usageRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    public async Task RebuildForDefinitionAsync(
        FlowDefinition definition,
        CancellationToken cancellationToken = default
    )
    {
        await ClearForDefinitionAsync(definition.Id, cancellationToken);

        var refs = ExtractSubFlowRefs(definition.DslJson);
        if (refs.Count == 0)
        {
            return;
        }

        foreach (var item in refs)
        {
            await _usageRepository.InsertAsync(
                new FlowUsage(
                    _guidGenerator.Create(),
                    definition.Id,
                    definition.Code,
                    definition.Name,
                    item.FlowKey,
                    item.NodeId,
                    item.NodeRef,
                    _currentTenant.Id
                ),
                autoSave: false
            );
        }
    }

    public async Task ClearForDefinitionAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default
    )
    {
        var query = await _usageRepository.GetQueryableAsync();
        var existing = query.Where(x => x.CallerDefinitionId == definitionId).ToList();
        foreach (var row in existing)
        {
            await _usageRepository.DeleteAsync(row, autoSave: false);
        }
    }

    public static List<(string FlowKey, string? NodeId, string? NodeRef)> ExtractSubFlowRefs(
        string? dslJson
    )
    {
        var list = new List<(string, string?, string?)>();
        if (string.IsNullOrWhiteSpace(dslJson))
        {
            return list;
        }

        try
        {
            var dsl = JsonSerializer.Deserialize<FlowDslDocument>(dslJson, JsonOptions);
            if (dsl?.Nodes == null)
            {
                return list;
            }

            foreach (var node in dsl.Nodes)
            {
                var type = (node.Type ?? "").Trim().ToLowerInvariant();
                if (type is not ("subflow" or "logiccomponent" or "component"))
                {
                    continue;
                }

                var key = (node.SubFlowKey ?? node.Url ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                list.Add((key, node.Id, node.Ref));
            }
        }
        catch
        {
            // 草稿 DSL 可能暂时非法；引用索引跳过即可
        }

        return list;
    }
}
