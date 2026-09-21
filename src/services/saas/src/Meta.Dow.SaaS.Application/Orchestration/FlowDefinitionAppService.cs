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
    private readonly IRepository<FlowUsage, Guid> _usageRepository;
    private readonly PublishedFlowCache _publishedFlowCache;
    private readonly IFlowUsageIndexer _usageIndexer;

    public FlowDefinitionAppService(
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        IRepository<FlowUsage, Guid> usageRepository,
        PublishedFlowCache publishedFlowCache,
        IFlowUsageIndexer usageIndexer
    )
    {
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _usageRepository = usageRepository;
        _publishedFlowCache = publishedFlowCache;
        _usageIndexer = usageIndexer;
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

        if (input.IsReusable.HasValue)
        {
            query = query.Where(x => x.IsReusable == input.IsReusable.Value);
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
        if (SystemResourceFlowKeys.IsSystem(code) || code.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        if (await _definitionRepository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:DefinitionCodeAlreadyExists", code]);
        }

        var entity = new FlowDefinition(
            GuidGenerator.Create(),
            input.Name.Trim(),
            code,
            input.Category?.Trim(),
            CurrentTenant.Id,
            input.IsReusable
        );
        entity.UpdateDraft(
            input.Name.Trim(),
            input.Category?.Trim(),
            input.GraphJson,
            input.DslJson,
            input.IsReusable
        );

        await _definitionRepository.InsertAsync(entity, autoSave: true);
        await _usageIndexer.RebuildForDefinitionAsync(entity);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Update)]
    public async Task<FlowDefinitionDto> UpdateAsync(Guid id, UpdateFlowDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entity = await _definitionRepository.GetAsync(id);
        if (entity.IsSystem)
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        entity.UpdateDraft(
            input.Name.Trim(),
            input.Category?.Trim(),
            input.GraphJson,
            input.DslJson,
            input.IsReusable
        );
        await _definitionRepository.UpdateAsync(entity, autoSave: true);
        await _usageIndexer.RebuildForDefinitionAsync(entity);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        if (entity.IsSystem)
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        await _usageIndexer.ClearForDefinitionAsync(id);
        await _definitionRepository.DeleteAsync(id, autoSave: true);
    }

    [Authorize(OrchestrationPermissions.Definitions.Publish)]
    public async Task<FlowVersionDto> PublishAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        if (entity.IsSystem)
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

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
        await _usageIndexer.RebuildForDefinitionAsync(entity);
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

    public async Task<ListResultDto<ReusableFlowLookupDto>> GetReusableLookupAsync(string? filter = null)
    {
        var query = await _definitionRepository.GetQueryableAsync();
        query = query.Where(x =>
            x.IsReusable &&
            x.Status == FlowDefinitionStatus.Published &&
            x.PublishedVersion != null
        );

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            query = query.Where(x => x.Name.Contains(f) || x.Code.Contains(f));
        }

        var list = query
            .OrderBy(x => x.Name)
            .Take(200)
            .ToList()
            .Select(x => new ReusableFlowLookupDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Category = x.Category,
                PublishedVersion = x.PublishedVersion ?? 0
            })
            .ToList();

        return new ListResultDto<ReusableFlowLookupDto>(list);
    }

    public async Task<ListResultDto<FlowUsageDto>> GetUsagesByCalleeAsync(string flowKey)
    {
        if (string.IsNullOrWhiteSpace(flowKey))
        {
            throw new UserFriendlyException(L["Orchestration:FlowKeyRequired"]);
        }

        var key = flowKey.Trim();
        var query = await _usageRepository.GetQueryableAsync();
        var list = query
            .Where(x => x.CalleeFlowKey == key)
            .OrderByDescending(x => x.CreationTime)
            .Take(500)
            .ToList();

        return new ListResultDto<FlowUsageDto>(
            ObjectMapper.Map<List<FlowUsage>, List<FlowUsageDto>>(list)
        );
    }

    public async Task<ListResultDto<FlowUsageDto>> GetUsagesByCallerAsync(Guid definitionId)
    {
        await _definitionRepository.GetAsync(definitionId);
        var query = await _usageRepository.GetQueryableAsync();
        var list = query
            .Where(x => x.CallerDefinitionId == definitionId)
            .OrderBy(x => x.CalleeFlowKey)
            .Take(500)
            .ToList();

        return new ListResultDto<FlowUsageDto>(
            ObjectMapper.Map<List<FlowUsage>, List<FlowUsageDto>>(list)
        );
    }
}
