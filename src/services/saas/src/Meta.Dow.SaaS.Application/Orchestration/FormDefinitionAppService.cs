using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class FormDefinitionAppService : SaaSAppService, IFormDefinitionAppService
{
    private readonly IRepository<FormDefinition, Guid> _repository;
    private readonly IRepository<AppResource, Guid> _resourceRepository;

    public FormDefinitionAppService(
        IRepository<FormDefinition, Guid> repository,
        IRepository<AppResource, Guid> resourceRepository
    )
    {
        _repository = repository;
        _resourceRepository = resourceRepository;
    }

    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<PagedResultDto<FormDefinitionDto>> GetListAsync(FormDefinitionGetListInput input)
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
        return new PagedResultDto<FormDefinitionDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<FormDefinitionDto> GetAsync(Guid id) => Map(await _repository.GetAsync(id));

    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<FormDefinitionDto> GetByCodeAsync(string code) =>
        Map(await FindByCodeAsync(code));

    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<FormDefinitionDto> GetPublishedByCodeAsync(string code)
    {
        var entity = await FindByCodeAsync(code);
        if (entity.Status != AppResourceStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:FormNotPublished", code]);
        }

        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Forms.Create)]
    public async Task<FormDefinitionDto> CreateAsync(CreateFormDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:FormCodeAlreadyExists", code]);
        }

        var entity = new FormDefinition(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            CurrentTenant.Id
        );
        entity.UpdateDraft(
            input.Name.Trim(),
            input.Description,
            input.SourceResourceCode,
            new FormDef(),
            []
        );

        if (!string.IsNullOrWhiteSpace(input.SourceResourceCode))
        {
            await SyncCatalogFromResourceAsync(entity, input.SourceResourceCode.Trim());
        }

        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Forms.Update)]
    public async Task<FormDefinitionDto> UpdateAsync(Guid id, UpdateFormDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        var schema = input.Schema ?? new FormDef();
        EnsureFlattenedFields(schema);
        entity.UpdateDraft(
            input.Name.Trim(),
            input.Description,
            input.SourceResourceCode,
            schema,
            input.FieldCatalog ?? []
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Forms.Delete)]
    public async Task DeleteAsync(Guid id) => await _repository.DeleteAsync(id, autoSave: true);

    [Authorize(OrchestrationPermissions.Forms.Publish)]
    public async Task<FormDefinitionDto> PublishAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        EnsureFlattenedFields(entity.Schema);
        if (entity.Schema.Fields == null || entity.Schema.Fields.Count == 0)
        {
            throw new UserFriendlyException(L["Orchestration:FormFieldsRequired"]);
        }

        entity.Publish();
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    private static void EnsureFlattenedFields(FormDef schema)
    {
        if (schema == null) return;
        schema.Fields ??= [];
        if (schema.Layout != null && schema.Layout.Count > 0)
        {
            var extracted = new System.Collections.Generic.List<FormFieldDef>();
            ExtractFieldsFromWidgets(schema.Layout, extracted);
            if (extracted.Count > 0)
            {
                var existingMap = schema.Fields.ToDictionary(f => f.Field, StringComparer.OrdinalIgnoreCase);
                foreach (var field in extracted)
                {
                    if (!existingMap.ContainsKey(field.Field))
                    {
                        schema.Fields.Add(field);
                    }
                }
            }
        }
    }

    private static void ExtractFieldsFromWidgets(System.Collections.Generic.List<FormWidgetDef> widgets, System.Collections.Generic.List<FormFieldDef> result)
    {
        if (widgets == null) return;
        foreach (var widget in widgets)
        {
            if (string.Equals(widget.Kind, "field", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(widget.Field))
            {
                result.Add(new FormFieldDef
                {
                    Field = widget.Field,
                    Title = widget.Title ?? widget.Field,
                    Control = widget.Control,
                    Span = widget.Span,
                    Placeholder = widget.Placeholder,
                    VisibleOnCreate = widget.VisibleOnCreate,
                    VisibleOnUpdate = widget.VisibleOnUpdate,
                    VisibleOnDetail = widget.VisibleOnDetail,
                    ReadonlyOnCreate = widget.ReadonlyOnCreate,
                    ReadonlyOnUpdate = widget.ReadonlyOnUpdate,
                    RequiredOnCreate = widget.RequiredOnCreate,
                    RequiredOnUpdate = widget.RequiredOnUpdate,
                    Dependencies = widget.Dependencies ?? [],
                    ControlProps = widget.ControlProps ?? []
                });
            }

            if (widget.Children != null && widget.Children.Count > 0)
            {
                ExtractFieldsFromWidgets(widget.Children, result);
            }
        }
    }

    private async Task SyncCatalogFromResourceAsync(FormDefinition entity, string resourceCode)
    {
        var resource = await _resourceRepository.FirstOrDefaultAsync(x => x.Code == resourceCode);
        if (resource == null)
        {
            return;
        }

        entity.UpdateDraft(
            entity.Name,
            entity.Description,
            resourceCode,
            entity.Schema.Fields.Count > 0 ? entity.Schema : CloneForm(resource.Form),
            resource.ListView.Columns.ToList()
        );
    }

    private async Task<FormDefinition> FindByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new UserFriendlyException(L["Orchestration:FormKeyRequired"]);
        }

        var key = code.Trim();
        var entity = await _repository.FirstOrDefaultAsync(x => x.Code == key);
        if (entity == null)
        {
            throw new UserFriendlyException(L["Orchestration:FormNotFound", key]);
        }

        return entity;
    }

    private static FormDef CloneForm(FormDef form) =>
        new()
        {
            SubmitFlowKey = form.SubmitFlowKey,
            Fields = form.Fields.Select(CloneField).ToList(),
            Layout = form.Layout?.Select(CloneWidget).ToList() ?? [],
            VformJson = form.VformJson
        };

    private static FormWidgetDef CloneWidget(FormWidgetDef w) =>
        new()
        {
            Id = w.Id,
            Kind = w.Kind,
            Container = w.Container,
            Columns = w.Columns,
            Title = w.Title,
            Hidden = w.Hidden,
            Children = w.Children?.Select(CloneWidget).ToList() ?? [],
            Field = w.Field,
            Control = w.Control,
            Span = w.Span,
            Placeholder = w.Placeholder,
            VisibleOnCreate = w.VisibleOnCreate,
            VisibleOnUpdate = w.VisibleOnUpdate,
            VisibleOnDetail = w.VisibleOnDetail,
            ReadonlyOnCreate = w.ReadonlyOnCreate,
            ReadonlyOnUpdate = w.ReadonlyOnUpdate,
            RequiredOnCreate = w.RequiredOnCreate,
            RequiredOnUpdate = w.RequiredOnUpdate
        };

    private static FormFieldDef CloneField(FormFieldDef f) =>
        new()
        {
            Field = f.Field,
            Title = f.Title,
            Control = f.Control,
            Span = f.Span,
            Placeholder = f.Placeholder,
            VisibleOnCreate = f.VisibleOnCreate,
            VisibleOnUpdate = f.VisibleOnUpdate,
            VisibleOnDetail = f.VisibleOnDetail,
            ReadonlyOnCreate = f.ReadonlyOnCreate,
            ReadonlyOnUpdate = f.ReadonlyOnUpdate,
            RequiredOnCreate = f.RequiredOnCreate,
            RequiredOnUpdate = f.RequiredOnUpdate,
            Rules = f.Rules,
            Children = f.Children?.Select(CloneField).ToList() ?? [],
            MinItems = f.MinItems,
            MaxItems = f.MaxItems,
            TreeChildrenField = f.TreeChildrenField
        };

    private static FormDefinitionDto Map(FormDefinition e) =>
        new()
        {
            Id = e.Id,
            Code = e.Code,
            Name = e.Name,
            Description = e.Description,
            Status = e.Status,
            SourceResourceCode = e.SourceResourceCode,
            Schema = e.Schema,
            FieldCatalog = e.FieldCatalog,
            CreationTime = e.CreationTime
        };

    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<ListResultDto<FormDefinitionLookupDto>> GetPublishedLookupAsync(
        string? filter = null
    )
    {
        var query = await _repository.GetQueryableAsync();
        query = query.Where(x => x.Status == AppResourceStatus.Published);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            query = query.Where(x => x.Name.Contains(f) || x.Code.Contains(f));
        }

        var list = query
            .OrderBy(x => x.Name)
            .Take(200)
            .ToList()
            .Select(x => new FormDefinitionLookupDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                FieldCount = x.Schema?.Fields?.Count ?? 0
            })
            .ToList();

        return new ListResultDto<FormDefinitionLookupDto>(list);
    }
}
