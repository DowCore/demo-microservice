using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowTriggerAppService : SaaSAppService, IFlowTriggerAppService
{
    private readonly IRepository<FlowTrigger, Guid> _repository;
    private readonly IRepository<MessageSource, Guid> _messageSourceRepository;
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;

    public FlowTriggerAppService(
        IRepository<FlowTrigger, Guid> repository,
        IRepository<MessageSource, Guid> messageSourceRepository,
        IRepository<FlowDefinition, Guid> definitionRepository
    )
    {
        _repository = repository;
        _messageSourceRepository = messageSourceRepository;
        _definitionRepository = definitionRepository;
    }

    [Authorize(OrchestrationPermissions.Triggers.Default)]
    public async Task<PagedResultDto<FlowTriggerDto>> GetListAsync(FlowTriggerGetListInput input)
    {
        var query = await _repository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            query = query.Where(x =>
                x.Name.Contains(f) || x.Code.Contains(f) || x.FlowKey.Contains(f) || x.Queue.Contains(f)
            );
        }

        if (input.EnabledOnly == true)
        {
            query = query.Where(x => x.IsEnabled);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();
        return new PagedResultDto<FlowTriggerDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.Triggers.Default)]
    public async Task<FlowTriggerDto> GetAsync(Guid id) => Map(await _repository.GetAsync(id));

    [Authorize(OrchestrationPermissions.Triggers.Create)]
    public async Task<FlowTriggerDto> CreateAsync(CreateFlowTriggerDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:TriggerCodeAlreadyExists", code]);
        }

        await EnsureMessageSourceAsync(input.MessageSourceCode);
        await EnsurePublishedFlowAsync(input.FlowKey);

        var entity = new FlowTrigger(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            input.FlowKey.Trim(),
            input.MessageSourceCode.Trim(),
            input.Queue.Trim(),
            input.Exchange,
            input.ExchangeType,
            input.RoutingKey,
            input.Description,
            CurrentTenant.Id
        );
        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Triggers.Update)]
    public async Task<FlowTriggerDto> UpdateAsync(Guid id, UpdateFlowTriggerDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        await EnsureMessageSourceAsync(input.MessageSourceCode);
        await EnsurePublishedFlowAsync(input.FlowKey);

        entity.Update(
            input.Name.Trim(),
            input.FlowKey.Trim(),
            input.MessageSourceCode.Trim(),
            input.Queue.Trim(),
            input.IsEnabled,
            input.Exchange,
            input.ExchangeType,
            input.RoutingKey,
            input.Description
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Triggers.Delete)]
    public async Task DeleteAsync(Guid id) => await _repository.DeleteAsync(id, autoSave: true);

    private async Task EnsureMessageSourceAsync(string code)
    {
        var c = code.Trim();
        var ms = await _messageSourceRepository.FirstOrDefaultAsync(x => x.Code == c);
        if (ms == null)
        {
            throw new UserFriendlyException(L["Orchestration:MessageSourceNotFound", c]);
        }

        if (!ms.IsEnabled)
        {
            throw new UserFriendlyException(L["Orchestration:MessageSourceDisabled", c]);
        }

        if (!MessageSourceProvider.IsSupportedForConsume(ms.Provider))
        {
            throw new UserFriendlyException(
                L["Orchestration:MessageSourceConsumeNotSupported", ms.Provider]
            );
        }
    }

    private async Task EnsurePublishedFlowAsync(string flowKey)
    {
        var key = flowKey.Trim();
        var def = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == key);
        if (def == null || def.Status != FlowDefinitionStatus.Published || !def.PublishedVersion.HasValue)
        {
            throw new UserFriendlyException(L["Orchestration:FlowNotFound", key]);
        }
    }

    private static FlowTriggerDto Map(FlowTrigger x) =>
        new()
        {
            Id = x.Id,
            Code = x.Code,
            Name = x.Name,
            TriggerType = x.TriggerType,
            FlowKey = x.FlowKey,
            MessageSourceCode = x.MessageSourceCode,
            Queue = x.Queue,
            Exchange = x.Exchange,
            ExchangeType = x.ExchangeType,
            RoutingKey = x.RoutingKey,
            IsEnabled = x.IsEnabled,
            Description = x.Description,
            CreationTime = x.CreationTime,
            LastModificationTime = x.LastModificationTime
        };
}
