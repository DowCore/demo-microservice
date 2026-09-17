using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

[Authorize(OrchestrationPermissions.Definitions.Default)]
public class FlowDefinitionAppService : SaaSAppService, IFlowDefinitionAppService
{
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IRepository<FlowVersion, Guid> _versionRepository;
    private readonly PublishedFlowCache _publishedFlowCache;

    public FlowDefinitionAppService(
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        PublishedFlowCache publishedFlowCache
    )
    {
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _publishedFlowCache = publishedFlowCache;
    }

    public async Task<PagedResultDto<FlowDefinitionDto>> GetListAsync(FlowDefinitionGetListInput input)
    {
        var query = await _definitionRepository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.Name.Contains(filter) || x.Code.Contains(filter));
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

        return new PagedResultDto<FlowDefinitionDto>(
            total,
            ObjectMapper.Map<List<FlowDefinition>, List<FlowDefinitionDto>>(items)
        );
    }

    public async Task<FlowDefinitionDto> GetAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Create)]
    public async Task<FlowDefinitionDto> CreateAsync(CreateFlowDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = input.Code.Trim();
        if (await _definitionRepository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:DefinitionCodeAlreadyExists", code]);
        }

        var entity = new FlowDefinition(
            GuidGenerator.Create(),
            input.Name.Trim(),
            code,
            input.Category?.Trim(),
            CurrentTenant.Id
        );
        entity.UpdateDraft(input.Name.Trim(), input.Category?.Trim(), input.GraphJson, input.DslJson);

        await _definitionRepository.InsertAsync(entity, autoSave: true);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Update)]
    public async Task<FlowDefinitionDto> UpdateAsync(Guid id, UpdateFlowDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entity = await _definitionRepository.GetAsync(id);
        entity.UpdateDraft(input.Name.Trim(), input.Category?.Trim(), input.GraphJson, input.DslJson);
        await _definitionRepository.UpdateAsync(entity, autoSave: true);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _definitionRepository.DeleteAsync(id, autoSave: true);
    }

    [Authorize(OrchestrationPermissions.Definitions.Publish)]
    public async Task<FlowVersionDto> PublishAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        if (string.IsNullOrWhiteSpace(entity.DslJson))
        {
            throw new UserFriendlyException(L["Orchestration:DslRequired"]);
        }

        try
        {
            var dsl = System.Text.Json.JsonSerializer.Deserialize<FlowDslDocument>(
                entity.DslJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (dsl == null)
            {
                throw new UserFriendlyException(L["Orchestration:DslRequired"]);
            }

            FlowExecutor.ValidateDsl(dsl);
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException(L["Orchestration:InvalidDsl", ex.Message]);
        }

        var nextVersion = (entity.PublishedVersion ?? 0) + 1;
        var version = new FlowVersion(
            GuidGenerator.Create(),
            entity.Id,
            nextVersion,
            entity.GraphJson ?? "{}",
            entity.DslJson!,
            CurrentTenant.Id
        );

        await _versionRepository.InsertAsync(version, autoSave: true);
        entity.MarkPublished(nextVersion);
        await _definitionRepository.UpdateAsync(entity, autoSave: true);
        _publishedFlowCache.InvalidateByDefinition(entity);

        return ObjectMapper.Map<FlowVersion, FlowVersionDto>(version);
    }

    public async Task<ListResultDto<FlowVersionDto>> GetVersionsAsync(Guid id)
    {
        await _definitionRepository.GetAsync(id);
        var query = await _versionRepository.GetQueryableAsync();
        var list = query
            .Where(x => x.DefinitionId == id)
            .OrderByDescending(x => x.Version)
            .ToList();

        return new ListResultDto<FlowVersionDto>(
            ObjectMapper.Map<List<FlowVersion>, List<FlowVersionDto>>(list)
        );
    }
}
