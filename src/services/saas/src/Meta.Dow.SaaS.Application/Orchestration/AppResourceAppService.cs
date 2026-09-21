using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class AppResourceAppService : SaaSAppService, IAppResourceAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IRepository<AppResource, Guid> _repository;
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IPublishedFlowInvoker _invoker;

    public AppResourceAppService(
        IRepository<AppResource, Guid> repository,
        IRepository<FlowDefinition, Guid> definitionRepository,
        IPublishedFlowInvoker invoker
    )
    {
        _repository = repository;
        _definitionRepository = definitionRepository;
        _invoker = invoker;
    }

    [Authorize(OrchestrationPermissions.Resources.Default)]
    public async Task<PagedResultDto<AppResourceDto>> GetListAsync(AppResourceGetListInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var query = await _repository.GetQueryableAsync();
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
        return new PagedResultDto<AppResourceDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.Resources.Default)]
    public async Task<AppResourceDto> GetAsync(Guid id) => Map(await _repository.GetAsync(id));

    [Authorize(OrchestrationPermissions.Resources.Default)]
    public async Task<AppResourceDto> GetByCodeAsync(string code)
    {
        var entity = await FindByCodeAsync(code);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Resources.Update)]
    public async Task<AppResourceDto> UpdateAsync(Guid id, UpdateAppResourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        entity.UpdateDraft(
            input.Name.Trim(),
            input.TitleField,
            input.Filter,
            input.ListView,
            input.Form,
            input.QueryFlowKey,
            input.GetFlowKey,
            input.CreateFlowKey,
            input.UpdateFlowKey,
            input.DeleteFlowKey
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Resources.Delete)]
    public async Task DeleteAsync(Guid id) => await _repository.DeleteAsync(id);

    [Authorize(OrchestrationPermissions.Resources.Publish)]
    public async Task<AppResourceDto> PublishAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        await EnsurePublishedFlowsAsync(entity);
        entity.Publish();
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Resources.Execute)]
    public async Task<AppResourceDto> GetPublishedByCodeAsync(string code)
    {
        var entity = await FindByCodeAsync(code);
        if (entity.Status != AppResourceStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:ResourceNotPublished", code]);
        }

        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Resources.Execute)]
    public Task<ResourceInvokeResultDto> QueryAsync(string code, ResourceRuntimeQueryInput input) =>
        InvokeAsync(code, r => r.QueryFlowKey, input, persistOnSuccess: false);

    [Authorize(OrchestrationPermissions.Resources.Execute)]
    public Task<ResourceInvokeResultDto> GetRecordAsync(string code, ResourceRuntimeGetInput input) =>
        InvokeAsync(code, r => r.GetFlowKey, input, persistOnSuccess: false);

    [Authorize(OrchestrationPermissions.Resources.Execute)]
    public Task<ResourceInvokeResultDto> CreateRecordAsync(string code, ResourceRuntimeSaveInput input) =>
        InvokeAsync(code, r => r.CreateFlowKey, BuildSaveBody(input, includeId: false), persistOnSuccess: true);

    [Authorize(OrchestrationPermissions.Resources.Execute)]
    public Task<ResourceInvokeResultDto> UpdateRecordAsync(string code, ResourceRuntimeSaveInput input) =>
        InvokeAsync(code, r => r.UpdateFlowKey, BuildSaveBody(input, includeId: true), persistOnSuccess: true);

    [Authorize(OrchestrationPermissions.Resources.Execute)]
    public Task<ResourceInvokeResultDto> DeleteRecordAsync(string code, ResourceRuntimeGetInput input) =>
        InvokeAsync(code, r => r.DeleteFlowKey, input, persistOnSuccess: true);

    private async Task<ResourceInvokeResultDto> InvokeAsync(
        string code,
        Func<AppResource, string> flowKeySelector,
        object payload,
        bool persistOnSuccess
    )
    {
        var entity = await FindByCodeAsync(code);
        if (entity.Status != AppResourceStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:ResourceNotPublished", code]);
        }

        var body = JsonSerializer.SerializeToNode(payload, JsonOptions)?.AsObject() ?? [];
        body["resourceCode"] = entity.Code;
        if (payload is ResourceRuntimeQueryInput query)
        {
            var columns = entity.ListView.Columns
                .Where(c => c.Visible)
                .Select(c => c.Field)
                .ToArray();
            body["columns"] = JsonSerializer.SerializeToNode(columns, JsonOptions);
            body["page"] = query.Page;
            body["pageSize"] = query.PageSize;
            if (!string.IsNullOrWhiteSpace(query.Sorting))
            {
                body["sorting"] = query.Sorting;
            }

            if (query.Filter != null)
            {
                body["filter"] = JsonSerializer.SerializeToNode(query.Filter, JsonOptions);
            }

            if (query.Filters != null)
            {
                body["filters"] = JsonSerializer.SerializeToNode(query.Filters, JsonOptions);
            }

            if (query.SummaryFields.Count > 0)
            {
                body["summaryFields"] = JsonSerializer.SerializeToNode(query.SummaryFields, JsonOptions);
            }
        }

        var result = await _invoker.InvokeAsync(
            flowKeySelector(entity),
            body.ToJsonString(),
            triggerSource: "resource:" + entity.Code,
            tenantId: CurrentTenant.Id,
            filterOutputsByRole: true,
            persistOnSuccess: persistOnSuccess
        );

        object? data = null;
        if (!string.IsNullOrWhiteSpace(result.DataJson))
        {
            data = JsonSerializer.Deserialize<object>(result.DataJson, JsonOptions);
        }

        return new ResourceInvokeResultDto
        {
            Success = result.Success,
            InstanceId = result.InstanceId,
            Data = data,
            Error = result.Error
        };
    }

    private static object BuildSaveBody(ResourceRuntimeSaveInput input, bool includeId)
    {
        return new
        {
            id = includeId ? input.Id : null,
            concurrencyStamp = input.ConcurrencyStamp,
            record = input.Record
        };
    }

    private async Task EnsurePublishedFlowsAsync(AppResource entity)
    {
        string[] keys =
        [
            entity.QueryFlowKey,
            entity.GetFlowKey,
            entity.CreateFlowKey,
            entity.UpdateFlowKey,
            entity.DeleteFlowKey
        ];
        foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var def = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == key);
            if (def == null || def.Status != FlowDefinitionStatus.Published)
            {
                throw new UserFriendlyException(L["Orchestration:ResourceFlowNotPublished", key]);
            }
        }
    }

    private async Task<AppResource> FindByCodeAsync(string code)
    {
        var key = Check.NotNullOrWhiteSpace(code, nameof(code)).Trim();
        return await _repository.FirstOrDefaultAsync(x => x.Code == key)
               ?? throw new UserFriendlyException(L["Orchestration:ResourceNotFound", key]);
    }

    private static FilterDef MapFilter(FilterDef filter)
    {
        filter.EnsureSearchForm();
        return filter;
    }

    internal static AppResourceDto Map(AppResource entity)
    {
        return new AppResourceDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            DataSourceCode = entity.DataSourceCode,
            TableName = entity.TableName,
            TitleField = entity.TitleField,
            Status = entity.Status,
            QueryFlowKey = entity.QueryFlowKey,
            GetFlowKey = entity.GetFlowKey,
            CreateFlowKey = entity.CreateFlowKey,
            UpdateFlowKey = entity.UpdateFlowKey,
            DeleteFlowKey = entity.DeleteFlowKey,
            Filter = MapFilter(entity.Filter),
            ListView = entity.ListView,
            Form = entity.Form,
            CreationTime = entity.CreationTime
        };
    }
}
