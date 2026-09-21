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

public class ReportDefinitionAppService : SaaSAppService, IReportDefinitionAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IRepository<ReportDefinition, Guid> _repository;
    private readonly IRepository<AppResource, Guid> _resourceRepository;
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IPublishedFlowInvoker _invoker;

    public ReportDefinitionAppService(
        IRepository<ReportDefinition, Guid> repository,
        IRepository<AppResource, Guid> resourceRepository,
        IRepository<FlowDefinition, Guid> definitionRepository,
        IPublishedFlowInvoker invoker
    )
    {
        _repository = repository;
        _resourceRepository = resourceRepository;
        _definitionRepository = definitionRepository;
        _invoker = invoker;
    }

    [Authorize(OrchestrationPermissions.Reports.Default)]
    public async Task<PagedResultDto<ReportDefinitionDto>> GetListAsync(ReportDefinitionGetListInput input)
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
        return new PagedResultDto<ReportDefinitionDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.Reports.Default)]
    public async Task<ReportDefinitionDto> GetAsync(Guid id) => Map(await _repository.GetAsync(id));

    [Authorize(OrchestrationPermissions.Reports.Default)]
    public async Task<ReportDefinitionDto> GetByCodeAsync(string code) => Map(await FindByCodeAsync(code));

    [Authorize(OrchestrationPermissions.Reports.Execute)]
    public async Task<ReportDefinitionDto> GetPublishedByCodeAsync(string code)
    {
        var entity = await FindByCodeAsync(code);
        if (entity.Status != AppResourceStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:ReportNotPublished", code]);
        }

        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Reports.Create)]
    public async Task<ReportDefinitionDto> CreateAsync(CreateReportDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:ReportCodeAlreadyExists", code]);
        }

        var entity = new ReportDefinition(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            input.Kind,
            CurrentTenant.Id
        );
        if (!string.IsNullOrWhiteSpace(input.ResourceCode))
        {
            var resource = await FindResourceAsync(input.ResourceCode);
            entity.ApplyFromResource(resource);
        }

        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Reports.Create)]
    public async Task<ReportDefinitionDto> CreateFromResourceAsync(CreateReportFromResourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:ReportCodeAlreadyExists", code]);
        }

        var resource = await FindResourceAsync(input.ResourceCode);
        var entity = new ReportDefinition(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            ReportKind.Resource,
            CurrentTenant.Id
        );
        entity.ApplyFromResource(resource);
        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Reports.Update)]
    public async Task<ReportDefinitionDto> UpdateAsync(Guid id, UpdateReportDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        entity.UpdateDraft(
            input.Name.Trim(),
            input.Kind,
            input.ResourceCode,
            input.QueryFlowKey,
            input.DataScope,
            input.SearchForm,
            input.AdvancedFilter,
            input.Presets,
            input.Kpis,
            input.Columns,
            input.Actions,
            input.DefaultSorting,
            input.PageSize,
            input.SelectionEnabled,
            input.Description
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Reports.Delete)]
    public async Task DeleteAsync(Guid id) => await _repository.DeleteAsync(id);

    [Authorize(OrchestrationPermissions.Reports.Publish)]
    public async Task<ReportDefinitionDto> PublishAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        var flow = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == entity.QueryFlowKey);
        if (flow == null || flow.Status != FlowDefinitionStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:ResourceFlowNotPublished", entity.QueryFlowKey]);
        }

        if (entity.Kind == ReportKind.Resource)
        {
            var resource = await FindResourceAsync(entity.ResourceCode ?? string.Empty);
            if (resource.Status != AppResourceStatus.Published)
            {
                throw new UserFriendlyException(L["Orchestration:ResourceNotPublished", resource.Code]);
            }
        }

        entity.Publish();
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Reports.Execute)]
    public async Task<ResourceInvokeResultDto> QueryAsync(string code, ResourceRuntimeQueryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await FindByCodeAsync(code);
        if (entity.Status != AppResourceStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:ReportNotPublished", code]);
        }

        string resourceCode = entity.ResourceCode ?? string.Empty;
        if (entity.Kind == ReportKind.Resource)
        {
            var resource = await FindResourceAsync(entity.ResourceCode ?? string.Empty);
            if (resource.Status != AppResourceStatus.Published)
            {
                throw new UserFriendlyException(L["Orchestration:ResourceNotPublished", resource.Code]);
            }

            resourceCode = resource.Code;
        }

        var body = JsonSerializer.SerializeToNode(input, JsonOptions)?.AsObject() ?? [];
        if (!string.IsNullOrWhiteSpace(resourceCode))
        {
            body["resourceCode"] = resourceCode;
        }

        body["reportCode"] = entity.Code;
        body["page"] = Math.Max(1, input.Page);
        body["pageSize"] = Math.Clamp(input.PageSize, 1, OrchestrationConsts.ResourceQueryMaxPageSize);
        if (!string.IsNullOrWhiteSpace(input.Sorting))
        {
            body["sorting"] = input.Sorting;
        }
        else if (!string.IsNullOrWhiteSpace(entity.DefaultSorting))
        {
            body["sorting"] = entity.DefaultSorting;
        }

        var columns = entity.Columns.Where(c => c.Visible).Select(c => c.Field).ToArray();
        body["columns"] = JsonSerializer.SerializeToNode(columns, JsonOptions);
        var mergedFilter = CombineFilters(entity.DataScope, input.Filter);
        if (mergedFilter != null)
        {
            body["filter"] = mergedFilter;
        }

        if (input.Filters != null)
        {
            body["filters"] = JsonSerializer.SerializeToNode(input.Filters, JsonOptions);
        }

        var summary = input.SummaryFields.Count > 0
            ? input.SummaryFields
            : entity.Columns
                .Where(c => !string.IsNullOrWhiteSpace(c.SummaryFn))
                .Select(c => new SummaryFieldInput { Field = c.Field, Fn = c.SummaryFn! })
                .ToList();
        if (summary.Count > 0)
        {
            body["summaryFields"] = JsonSerializer.SerializeToNode(summary, JsonOptions);
        }

        var result = await _invoker.InvokeAsync(
            entity.QueryFlowKey,
            body.ToJsonString(),
            triggerSource: "report:" + entity.Code,
            tenantId: CurrentTenant.Id,
            filterOutputsByRole: true,
            persistOnSuccess: false
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

    private async Task<ReportDefinition> FindByCodeAsync(string code)
    {
        var key = Check.NotNullOrWhiteSpace(code, nameof(code)).Trim();
        return await _repository.FirstOrDefaultAsync(x => x.Code == key)
               ?? throw new UserFriendlyException(L["Orchestration:ReportNotFound", key]);
    }

    private async Task<AppResource> FindResourceAsync(string code)
    {
        var key = Check.NotNullOrWhiteSpace(code, nameof(code)).Trim();
        return await _resourceRepository.FirstOrDefaultAsync(x => x.Code == key)
               ?? throw new UserFriendlyException(L["Orchestration:ResourceNotFound", key]);
    }

    internal static ReportDefinitionDto Map(ReportDefinition entity)
    {
        return new ReportDefinitionDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Kind = entity.Kind,
            ResourceCode = entity.ResourceCode,
            QueryFlowKey = entity.QueryFlowKey,
            Status = entity.Status,
            DataScope = entity.DataScope,
            SearchForm = entity.SearchForm,
            AdvancedFilter = entity.AdvancedFilter,
            Presets = entity.Presets,
            Kpis = entity.Kpis,
            Columns = entity.Columns,
            Actions = entity.Actions,
            DefaultSorting = entity.DefaultSorting,
            PageSize = entity.PageSize,
            SelectionEnabled = entity.SelectionEnabled,
            Description = entity.Description,
            CreationTime = entity.CreationTime
        };
    }

    private static JsonNode? CombineFilters(FilterNode? dataScope, object? runtimeFilter)
    {
        var parts = new JsonArray();
        var scope = JsonSerializer.SerializeToNode(dataScope, JsonOptions);
        if (HasFilterContent(scope))
        {
            parts.Add(scope!);
        }

        if (runtimeFilter != null)
        {
            var runtime = JsonSerializer.SerializeToNode(runtimeFilter, JsonOptions);
            if (HasFilterContent(runtime))
            {
                parts.Add(runtime!);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        if (parts.Count == 1)
        {
            return parts[0];
        }

        return new JsonObject
        {
            ["kind"] = "group",
            ["op"] = "and",
            ["children"] = parts
        };
    }

    private static bool HasFilterContent(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return false;
        }

        var kind = obj["kind"]?.GetValue<string>();
        if (kind is "rule" || (obj["left"] != null && obj["children"] is not JsonArray))
        {
            return !string.IsNullOrWhiteSpace(obj["left"]?.GetValue<string>());
        }

        return obj["children"] is JsonArray children && children.Any(HasFilterContent);
    }
}
