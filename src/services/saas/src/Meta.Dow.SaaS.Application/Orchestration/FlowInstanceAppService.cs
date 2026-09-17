using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

[Authorize(OrchestrationPermissions.Instances.Default)]
public class FlowInstanceAppService : SaaSAppService, IFlowInstanceAppService
{
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IRepository<FlowVersion, Guid> _versionRepository;
    private readonly IRepository<FlowInstance, Guid> _instanceRepository;
    private readonly FlowExecutor _flowExecutor;

    public FlowInstanceAppService(
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        IRepository<FlowInstance, Guid> instanceRepository,
        FlowExecutor flowExecutor
    )
    {
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _instanceRepository = instanceRepository;
        _flowExecutor = flowExecutor;
    }

    public async Task<PagedResultDto<FlowInstanceDto>> GetListAsync(FlowInstanceGetListInput input)
    {
        var query = await _instanceRepository.GetQueryableAsync();

        if (input.DefinitionId.HasValue)
        {
            query = query.Where(x => x.DefinitionId == input.DefinitionId.Value);
        }

        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new PagedResultDto<FlowInstanceDto>(
            total,
            ObjectMapper.Map<List<FlowInstance>, List<FlowInstanceDto>>(items)
        );
    }

    public async Task<FlowInstanceDto> GetAsync(Guid id)
    {
        var entity = await _instanceRepository.GetAsync(id);
        return ObjectMapper.Map<FlowInstance, FlowInstanceDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Instances.Run)]
    public async Task<FlowInstanceDto> StartAsync(StartFlowInstanceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var definition = await _definitionRepository.GetAsync(input.DefinitionId);
        if (!definition.PublishedVersion.HasValue)
        {
            throw new UserFriendlyException(L["Orchestration:NotPublished"]);
        }

        var versionNo = input.Version ?? definition.PublishedVersion.Value;
        var versionQuery = await _versionRepository.GetQueryableAsync();
        var version = versionQuery.FirstOrDefault(x =>
            x.DefinitionId == definition.Id && x.Version == versionNo
        );
        if (version == null)
        {
            throw new UserFriendlyException(L["Orchestration:VersionNotFound", versionNo]);
        }

        var instance = new FlowInstance(
            GuidGenerator.Create(),
            definition.Id,
            definition.Name,
            version.Version,
            isDryRun: false,
            input.VariablesJson,
            CurrentTenant.Id
        );

        var execution = await _flowExecutor.ExecuteAsync(
            version.DslJson,
            input.VariablesJson,
            isDryRun: false,
            filterOutputsByRole: false,
            currentUser: CurrentUser
        );

        foreach (var node in execution.Nodes)
        {
            instance.AddNode(node);
        }

        if (execution.Succeeded)
        {
            instance.Succeed(execution.VariablesJson);
        }
        else
        {
            instance.Fail(execution.Error ?? "Failed", execution.VariablesJson);
        }

        await _instanceRepository.InsertAsync(instance, autoSave: true);
        return ObjectMapper.Map<FlowInstance, FlowInstanceDto>(instance);
    }

    [Authorize(OrchestrationPermissions.Instances.Run)]
    public async Task<FlowInstanceDto> DryRunAsync(DryRunFlowInstanceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var definition = await _definitionRepository.GetAsync(input.DefinitionId);
        var dslJson = !string.IsNullOrWhiteSpace(input.DslJson)
            ? input.DslJson
            : definition.DslJson;

        if (string.IsNullOrWhiteSpace(dslJson))
        {
            throw new UserFriendlyException(L["Orchestration:DslRequired"]);
        }

        var instance = new FlowInstance(
            GuidGenerator.Create(),
            definition.Id,
            definition.Name,
            definition.PublishedVersion ?? 0,
            isDryRun: true,
            input.VariablesJson,
            CurrentTenant.Id
        );

        var execution = await _flowExecutor.ExecuteAsync(
            dslJson!,
            input.VariablesJson,
            isDryRun: true,
            filterOutputsByRole: false,
            currentUser: CurrentUser
        );

        foreach (var node in execution.Nodes)
        {
            instance.AddNode(node);
        }

        if (execution.Succeeded)
        {
            instance.Succeed(execution.VariablesJson);
        }
        else
        {
            instance.Fail(execution.Error ?? "Failed", execution.VariablesJson);
        }

        await _instanceRepository.InsertAsync(instance, autoSave: true);
        return ObjectMapper.Map<FlowInstance, FlowInstanceDto>(instance);
    }

    [Authorize(OrchestrationPermissions.Instances.Cancel)]
    public async Task CancelAsync(Guid id)
    {
        var entity = await _instanceRepository.GetAsync(id);
        if (entity.Status != FlowInstanceStatus.Running)
        {
            throw new UserFriendlyException(L["Orchestration:InstanceNotRunning"]);
        }

        entity.Cancel();
        await _instanceRepository.UpdateAsync(entity, autoSave: true);
    }
}
