using System;
using System.Collections.Generic;
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
    private readonly IRepository<WorkflowDefinition, Guid> _workflowRepository;
    private readonly IRepository<ReportDefinition, Guid> _reportRepository;

    public FormDefinitionAppService(
        IRepository<FormDefinition, Guid> repository,
        IRepository<AppResource, Guid> resourceRepository,
        IRepository<WorkflowDefinition, Guid> workflowRepository,
        IRepository<ReportDefinition, Guid> reportRepository
    )
    {
        _repository = repository;
        _resourceRepository = resourceRepository;
        _workflowRepository = workflowRepository;
        _reportRepository = reportRepository;
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
        return new PagedResultDto<FormDefinitionDto>(total, items.Select(x => Map(x)).ToList());
    }

    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<FormDefinitionDto> GetAsync(Guid id) => Map(await _repository.GetAsync(id));

    /// <summary>按 code 取草稿（设计器编辑态使用）。</summary>
    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<FormDefinitionDto> GetByCodeAsync(string code) => Map(await FindByCodeAsync(code));

    /// <summary>按 code 取已发布（消费方使用）：返回发布时的不可变快照，而非会继续漂移的草稿。</summary>
    [Authorize(OrchestrationPermissions.Forms.Default)]
    public async Task<FormDefinitionDto> GetPublishedByCodeAsync(string code)
    {
        var entity = await FindByCodeAsync(code);
        if (entity.Status != AppResourceStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:FormNotPublished", code]);
        }

        return Map(entity, published: true);
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

    /// <summary>
    /// 删除表单（F6）：先做引用完整性检查，被审批流/报表引用时拒绝删除，
    /// 而不是放任删除后在运行时抛 FormNotFound。
    /// </summary>
    [Authorize(OrchestrationPermissions.Forms.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);

        if (await _workflowRepository.AnyAsync(x => x.FormRef == entity.Code))
        {
            throw new UserFriendlyException($"表单「{entity.Code}」已被审批流引用，请先解除引用再删除");
        }

        var reports = await _reportRepository.GetListAsync();
        if (reports.Any(r => r.Actions.Any(a => string.Equals(a.FormKey, entity.Code, StringComparison.OrdinalIgnoreCase))))
        {
            throw new UserFriendlyException($"表单「{entity.Code}」已被报表动作引用，请先解除引用再删除");
        }

        var resources = await _resourceRepository.GetListAsync();
        if (resources.Any(r => r.ListView.Actions.Any(a => string.Equals(a.FormKey, entity.Code, StringComparison.OrdinalIgnoreCase))))
        {
            throw new UserFriendlyException($"表单「{entity.Code}」已被资源动作引用，请先解除引用再删除");
        }

        await _repository.DeleteAsync(id, autoSave: true);
    }

    /// <summary>
    /// 发布（F4/F5）：拍平 → 字段名校验 → 与绑定资源列对齐校验 → 写入不可变快照并递增版本。
    /// </summary>
    [Authorize(OrchestrationPermissions.Forms.Publish)]
    public async Task<FormDefinitionDto> PublishAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        EnsureFlattenedFields(entity.Schema);
        if (entity.Schema.Fields == null || entity.Schema.Fields.Count == 0)
        {
            throw new UserFriendlyException(L["Orchestration:FormFieldsRequired"]);
        }

        ValidateFieldNames(entity.Schema);
        await ValidateAgainstResourceAsync(entity);

        // 深拷贝为不可变快照：之后继续编辑草稿不会影响在途单据解析到的字段结构。
        entity.Publish(CloneForm(entity.Schema));
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    /// <summary>从绑定资源重新同步字段库（F7）：资源列后续变更时的手动/脚本同步入口。</summary>
    [Authorize(OrchestrationPermissions.Forms.Update)]
    public async Task<FormDefinitionDto> ResyncCatalogFromResourceAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (string.IsNullOrWhiteSpace(entity.SourceResourceCode))
        {
            throw new UserFriendlyException("该表单未绑定资源，无法同步字段库");
        }

        var resource = await _resourceRepository.FirstOrDefaultAsync(x => x.Code == entity.SourceResourceCode);
        if (resource == null)
        {
            throw new UserFriendlyException($"绑定资源「{entity.SourceResourceCode}」不存在，无法同步字段库");
        }

        // 仅刷新字段库；已有编辑过的 Schema 不覆盖，避免抹掉设计器里的手工调整。
        entity.UpdateDraft(
            entity.Name,
            entity.Description,
            entity.SourceResourceCode,
            entity.Schema,
            resource.ListView.Columns.ToList()
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    /// <summary>
    /// F1：以 Layout 为准的覆盖式拍平。
    /// 原实现用「已存在则跳过」，导致改必填/换控件/改标题不会写入、删除字段也不收缩，
    /// 发布出去的是一份陈旧字段定义。这里改为 Layout 有内容时完全由它派生 Fields。
    /// </summary>
    internal static void EnsureFlattenedFields(FormDef schema)
    {
        if (schema == null) return;
        schema.Fields ??= [];
        if (schema.Layout is not { Count: > 0 })
        {
            return;
        }

        var extracted = new List<FormFieldDef>();
        ExtractFieldsFromWidgets(schema.Layout, extracted);
        if (extracted.Count > 0)
        {
            schema.Fields = extracted;
        }
    }

    /// <summary>
    /// F2：从布局树提取字段。补全原先丢失的 Rules / MinItems / MaxItems / TreeChildrenField / ColumnName，
    /// 并把明细容器（data-table/subform/list/tree）的子列聚合为 <see cref="FormFieldDef.Children"/>，
    /// 使明细结构、行数限制、校验规则不再在拍平后失真。
    /// </summary>
    internal static void ExtractFieldsFromWidgets(List<FormWidgetDef> widgets, List<FormFieldDef> result)
    {
        if (widgets == null) return;
        foreach (var widget in widgets)
        {
            if (IsDetailContainer(widget) && !string.IsNullOrWhiteSpace(widget.Field))
            {
                var detail = BuildField(widget);
                if (widget.Children is { Count: > 0 })
                {
                    ExtractFieldsFromWidgets(widget.Children, detail.Children);
                }

                result.Add(detail);
                continue;
            }

            if (string.Equals(widget.Kind, "field", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(widget.Field))
            {
                result.Add(BuildField(widget));
            }

            if (widget.Children is { Count: > 0 })
            {
                ExtractFieldsFromWidgets(widget.Children, result);
            }
        }
    }

    internal static bool IsDetailContainer(FormWidgetDef widget)
    {
        if (!string.Equals(widget.Kind, "container", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var container = (widget.Container ?? "").Trim().ToLowerInvariant();
        return container is "data-table" or "subform" or "list" or "tree";
    }

    internal static FormFieldDef BuildField(FormWidgetDef widget) =>
        new()
        {
            Field = widget.Field!,
            ColumnName = widget.ColumnName,
            Title = widget.Title ?? widget.Field!,
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
            Rules = widget.Rules,
            MinItems = widget.MinItems,
            MaxItems = widget.MaxItems,
            TreeChildrenField = widget.TreeChildrenField,
            Dependencies = CloneDependencies(widget.Dependencies),
            ControlProps = new Dictionary<string, object?>(widget.ControlProps ?? [])
        };

    internal static void ValidateFieldNames(FormDef schema)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Walk(IEnumerable<FormFieldDef> items)
        {
            foreach (var field in items)
            {
                if (string.IsNullOrWhiteSpace(field.Field))
                {
                    throw new UserFriendlyException("存在没有字段名的字段，请补全后再发布");
                }

                if (!seen.Add(field.Field.Trim()))
                {
                    throw new UserFriendlyException($"表单字段名重复：{field.Field}（字段名必须唯一）");
                }

                if (field.Children is { Count: > 0 } children)
                {
                    Walk(children);
                }
            }
        }

        Walk(schema.Fields ?? []);
    }

    /// <summary>F5：绑定资源时，字段应能对应到资源的真实列（顶层字段；明细子列豁免）。</summary>
    private async Task ValidateAgainstResourceAsync(FormDefinition entity)
    {
        if (string.IsNullOrWhiteSpace(entity.SourceResourceCode))
        {
            return;
        }

        var resource = await _resourceRepository.FirstOrDefaultAsync(x => x.Code == entity.SourceResourceCode);
        if (resource == null)
        {
            return;
        }

        var columns = CollectResourceColumns(resource);
        if (columns.Count == 0)
        {
            return;
        }

        var allowed = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        foreach (var field in (entity.Schema.Fields ?? []).Where(f => f.Children is not { Count: > 0 }))
        {
            var actual = string.IsNullOrWhiteSpace(field.ColumnName) ? field.Field : field.ColumnName!;
            if (!allowed.Contains(actual))
            {
                throw new UserFriendlyException(
                    $"字段「{field.Field}」在绑定资源「{entity.SourceResourceCode}」中找不到对应的列「{actual}」，请检查字段映射"
                );
            }
        }
    }

    private static List<string> CollectResourceColumns(AppResource resource)
    {
        var columns = new List<string>();
        foreach (var field in resource.Form?.Fields ?? [])
        {
            if (!string.IsNullOrWhiteSpace(field.Field))
            {
                columns.Add(field.Field);
            }
        }

        foreach (var column in resource.ListView?.Columns ?? [])
        {
            if (!string.IsNullOrWhiteSpace(column.Field))
            {
                columns.Add(column.Field);
            }
        }

        return columns;
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

    internal static FormDef CloneForm(FormDef form) =>
        new()
        {
            SubmitFlowKey = form.SubmitFlowKey,
            Fields = form.Fields.Select(CloneField).ToList(),
            Layout = form.Layout?.Select(CloneWidget).ToList() ?? [],
            VformJson = form.VformJson
        };

    /// <summary>F2/F3：克隆时完整复制全部语义属性，避免再出现「漏拷贝导致字段联动/控件配置被清空」。</summary>
    internal static FormWidgetDef CloneWidget(FormWidgetDef w) =>
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
            ColumnName = w.ColumnName,
            Control = w.Control,
            Span = w.Span,
            Placeholder = w.Placeholder,
            VisibleOnCreate = w.VisibleOnCreate,
            VisibleOnUpdate = w.VisibleOnUpdate,
            VisibleOnDetail = w.VisibleOnDetail,
            ReadonlyOnCreate = w.ReadonlyOnCreate,
            ReadonlyOnUpdate = w.ReadonlyOnUpdate,
            RequiredOnCreate = w.RequiredOnCreate,
            RequiredOnUpdate = w.RequiredOnUpdate,
            Rules = CloneRule(w.Rules),
            MinItems = w.MinItems,
            MaxItems = w.MaxItems,
            TreeChildrenField = w.TreeChildrenField,
            Dependencies = CloneDependencies(w.Dependencies),
            ControlProps = new Dictionary<string, object?>(w.ControlProps ?? [])
        };

    internal static FormFieldDef CloneField(FormFieldDef f) =>
        new()
        {
            Field = f.Field,
            ColumnName = f.ColumnName,
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
            Rules = CloneRule(f.Rules),
            Dependencies = CloneDependencies(f.Dependencies),
            ControlProps = new Dictionary<string, object?>(f.ControlProps ?? []),
            Children = f.Children?.Select(CloneField).ToList() ?? [],
            MinItems = f.MinItems,
            MaxItems = f.MaxItems,
            TreeChildrenField = f.TreeChildrenField
        };

    private static FormFieldRuleDef? CloneRule(FormFieldRuleDef? rule) =>
        rule == null
            ? null
            : new FormFieldRuleDef
            {
                MinLength = rule.MinLength,
                MaxLength = rule.MaxLength,
                Pattern = rule.Pattern,
                PatternMessage = rule.PatternMessage,
                Min = rule.Min,
                Max = rule.Max,
                Message = rule.Message
            };

    private static List<FormFieldDependencyDef> CloneDependencies(List<FormFieldDependencyDef>? source) =>
        source?.Select(d => new FormFieldDependencyDef
        {
            SourceField = d.SourceField,
            Op = d.Op,
            Value = d.Value,
            Action = d.Action,
            SetValue = d.SetValue
        }).ToList() ?? [];

    private static FormDefinitionDto Map(FormDefinition e, bool published = false) =>
        new()
        {
            Id = e.Id,
            Code = e.Code,
            Name = e.Name,
            Description = e.Description,
            Status = e.Status,
            SchemaVersion = e.SchemaVersion,
            SourceResourceCode = e.SourceResourceCode,
            Schema = published ? e.GetEffectiveSchema() : e.Schema,
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
                FieldCount = x.GetEffectiveSchema().Fields?.Count ?? 0
            })
            .ToList();

        return new ListResultDto<FormDefinitionLookupDto>(list);
    }
}
