using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>已发布的查询页配方：查询表单、列展示、按钮、汇总。</summary>
public class ReportDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    public string Kind { get; protected set; } = ReportKind.Resource;

    public string? ResourceCode { get; protected set; }

    public string? DataSourceCode { get; protected set; }

    public string? TableName { get; protected set; }

    public string QueryFlowKey { get; protected set; } = SystemResourceFlowKeys.Query;

    public int Status { get; protected set; } = AppResourceStatus.Draft;

    public FilterNode DataScope { get; protected set; } = new();

    public SearchFormDef SearchForm { get; protected set; } = new();

    public bool AdvancedFilter { get; protected set; } = true;

    public List<NamedFilterPreset> Presets { get; protected set; } = [];

    public List<KpiDef> Kpis { get; protected set; } = [];

    public List<ListColumnDef> Columns { get; protected set; } = [];

    public List<ActionDef> Actions { get; protected set; } = [];

    public FormDef Form { get; protected set; } = new();

    public List<NamedDict> Dictionaries { get; protected set; } = [];

    public string? DefaultSorting { get; protected set; } = "CreationTime desc";

    public int PageSize { get; protected set; } = 20;

    public bool SelectionEnabled { get; protected set; }

    public string? Description { get; protected set; }

    protected ReportDefinition()
    {
    }

    public ReportDefinition(Guid id, string code, string name, string kind, Guid? tenantId = null)
        : base(id)
    {
        SetCode(code);
        SetName(name);
        Kind = ReportKind.Normalize(kind);
        TenantId = tenantId;
        Status = AppResourceStatus.Draft;
        DataScope = new FilterNode { Kind = "group", Op = "and" };
        SearchForm = new SearchFormDef();
        Form = new FormDef();
        Dictionaries = [];
        AdvancedFilter = true;
        Presets = [];
        Kpis = [];
        Columns = [];
        Actions = [];
        PageSize = 20;
    }

    public void UpdateDraft(
        string name,
        string kind,
        string? resourceCode,
        string? dataSourceCode,
        string? tableName,
        string? queryFlowKey,
        FilterNode? dataScope,
        SearchFormDef? searchForm,
        bool advancedFilter,
        List<NamedFilterPreset>? presets,
        List<KpiDef>? kpis,
        List<ListColumnDef>? columns,
        List<ActionDef>? actions,
        FormDef? form,
        List<NamedDict>? dictionaries,
        string? defaultSorting,
        int? pageSize,
        bool selectionEnabled,
        string? description
    )
    {
        SetName(name);
        Kind = ReportKind.Normalize(kind);
        ResourceCode = string.IsNullOrWhiteSpace(resourceCode) ? null : resourceCode.Trim();
        DataSourceCode = string.IsNullOrWhiteSpace(dataSourceCode) ? null : dataSourceCode.Trim();
        TableName = string.IsNullOrWhiteSpace(tableName) ? null : tableName.Trim();
        if (!string.IsNullOrWhiteSpace(queryFlowKey))
        {
            QueryFlowKey = queryFlowKey.Trim();
        }

        DataScope = dataScope ?? DataScope;
        SearchForm = searchForm ?? SearchForm;
        AdvancedFilter = advancedFilter;
        Presets = presets ?? Presets;
        Kpis = kpis ?? Kpis;
        Columns = columns ?? Columns;
        Actions = actions ?? Actions;
        Form = form ?? Form;
        Dictionaries = dictionaries ?? Dictionaries;
        if (!string.IsNullOrWhiteSpace(defaultSorting))
        {
            DefaultSorting = defaultSorting.Trim();
        }

        if (pageSize is >= 1 and <= OrchestrationConsts.ResourceQueryMaxPageSize)
        {
            PageSize = pageSize.Value;
        }

        SelectionEnabled = selectionEnabled;
        Description = description?.Trim();
    }

    public void ApplyFromResource(AppResource resource)
    {
        Kind = ReportKind.Resource;
        ResourceCode = resource.Code;
        DataSourceCode = resource.DataSourceCode;
        TableName = resource.TableName;
        QueryFlowKey = resource.QueryFlowKey;
        resource.Filter.EnsureSearchForm();
        SearchForm = resource.Filter.SearchForm ?? new SearchFormDef();
        AdvancedFilter = resource.Filter.AdvancedFilter;
        DataScope = resource.Filter.DataScope ?? new FilterNode { Kind = "group", Op = "and" };
        Presets = resource.Filter.Presets ?? [];
        Columns = resource.ListView.Columns.Select(CloneColumn).ToList();
        Actions = resource.ListView.Actions.Select(CloneAction).ToList();
        Form = CloneForm(resource.Form);
        Dictionaries = CloneDicts(resource.ListView.Dictionaries);
        DefaultSorting = resource.ListView.DefaultSorting;
        PageSize = resource.ListView.PageSize;
        SelectionEnabled = Actions.Any(a =>
            a.MultiSelect ||
            string.Equals(a.Scene, "batch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Position, "batch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Scope, "selection", StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyFromTable(TableDefinition table)
    {
        Kind = ReportKind.Table;
        DataSourceCode = table.DataSourceCode;
        TableName = table.TableName;
        QueryFlowKey = SystemResourceFlowKeys.Query;
        SearchForm = new SearchFormDef();
        AdvancedFilter = true;
        DataScope = new FilterNode { Kind = "group", Op = "and" };
        Presets = [];
        Columns = ColumnsFromTable(table);
        Actions = [];
        Form = new FormDef();
        Dictionaries = [];
        DefaultSorting = table.Columns.Any(c =>
            string.Equals(c.Name, "CreationTime", StringComparison.OrdinalIgnoreCase))
            ? "CreationTime desc"
            : null;
        PageSize = 20;
        SelectionEnabled = false;
    }

    public void SetWriteResource(string? resourceCode)
    {
        ResourceCode = string.IsNullOrWhiteSpace(resourceCode) ? null : resourceCode.Trim();
    }

    public void SetQueryFlowKey(string queryFlowKey)
    {
        QueryFlowKey = Check.NotNullOrWhiteSpace(queryFlowKey, nameof(queryFlowKey)).Trim();
    }

    public void SetPhysicalTable(string? dataSourceCode, string? tableName)
    {
        DataSourceCode = string.IsNullOrWhiteSpace(dataSourceCode) ? null : dataSourceCode.Trim();
        TableName = string.IsNullOrWhiteSpace(tableName) ? null : tableName.Trim();
    }

    public static List<ListColumnDef> ColumnsFromTable(TableDefinition table)
    {
        return table.Columns
            .Where(c => c.Origin != TableOrigin.Convention ||
                        string.Equals(c.Name, "CreationTime", StringComparison.OrdinalIgnoreCase))
            .Select(c =>
            {
                var col = new ListColumnDef
                {
                    Field = c.Name,
                    Title = c.DisplayName,
                    Visible = true,
                    Sortable = c.PlatformType is TablePlatformType.DateTime or TablePlatformType.Int
                        or TablePlatformType.Long or TablePlatformType.Decimal or TablePlatformType.String,
                    FormatPreset = c.PlatformType switch
                    {
                        TablePlatformType.DateTime => "datetime",
                        TablePlatformType.Date => "date",
                        TablePlatformType.Decimal => "currency",
                        TablePlatformType.Boolean => "boolean",
                        _ => "text"
                    }
                };
                ListColumnDef.ApplyLayoutDefaults(col);
                return col;
            })
            .ToList();
    }

    public void Publish() => Status = AppResourceStatus.Published;

    public void Unpublish() => Status = AppResourceStatus.Draft;

    private void SetCode(string code)
    {
        var value = Check.NotNullOrWhiteSpace(code, nameof(code), OrchestrationConsts.MaxResourceCodeLength)
            .Trim();
        if (!SqlIdentifier.IsValid(value))
        {
            throw new BusinessException("Orchestration:InvalidIdentifier").WithData("Name", value);
        }

        Code = value;
    }

    private void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrchestrationConsts.MaxNameLength).Trim();
    }

    private static ListColumnDef CloneColumn(ListColumnDef c) =>
        new()
        {
            Field = c.Field,
            Title = c.Title,
            Width = c.Width,
            Sortable = c.Sortable,
            Visible = c.Visible,
            FormatPreset = c.FormatPreset,
            Align = c.Align,
            Ellipsis = c.Ellipsis,
            Fixed = c.Fixed,
            Render = c.Render,
            DictMap = new Dictionary<string, string>(c.DictMap),
            Options = c.Options.Select(CloneOption).ToList(),
            DictCode = c.DictCode,
            FormatFn = c.FormatFn,
            RenderFn = c.RenderFn,
            StyleRules = c.StyleRules.Select(r => new ColumnStyleRule
            {
                Op = r.Op,
                Value = r.Value,
                Tone = r.Tone,
                Target = r.Target
            }).ToList(),
            SummaryFn = c.SummaryFn,
            SummaryScope = c.SummaryScope
        };

    private static ActionDef CloneAction(ActionDef a) =>
        new()
        {
            Key = a.Key,
            Label = a.Label,
            Position = a.Position,
            Scene = string.IsNullOrWhiteSpace(a.Scene) ? a.Position : a.Scene,
            Scope = a.Scope,
            Kind = a.Kind,
            FlowKey = a.FlowKey,
            Open = string.IsNullOrWhiteSpace(a.Open)
                ? (a.Kind is "create" or "update" or "detail" ? "page" : "none")
                : a.Open,
            Fixed = string.Equals(a.Scene, "row", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Position, "row", StringComparison.OrdinalIgnoreCase)
                ? "right"
                : a.Fixed,
            FormKey = string.IsNullOrWhiteSpace(a.FormKey)
                ? (a.Kind is "create" or "update" or "detail" ? a.Kind : a.FormMode)
                : a.FormKey,
            FormMode = a.FormMode,
            FieldsMode = a.FieldsMode,
            Fields = [.. a.Fields],
            Confirm = a.Confirm,
            ConfirmText = a.ConfirmText,
            BatchLimit = a.BatchLimit,
            Description = a.Description,
            MultiSelect = a.MultiSelect ||
                          string.Equals(a.Scope, "selection", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a.Scene, "batch", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a.Position, "batch", StringComparison.OrdinalIgnoreCase)
        };

    private static FormDef CloneForm(FormDef form) =>
        new()
        {
            SubmitFlowKey = form.SubmitFlowKey,
            Fields = form.Fields.Select(f => new FormFieldDef
            {
                Field = f.Field,
                Title = f.Title,
                Control = f.Control,
                Span = f.Span <= 0 ? 12 : f.Span,
                Placeholder = f.Placeholder,
                VisibleOnCreate = f.VisibleOnCreate,
                VisibleOnUpdate = f.VisibleOnUpdate,
                VisibleOnDetail = f.VisibleOnDetail,
                ReadonlyOnCreate = f.ReadonlyOnCreate,
                ReadonlyOnUpdate = f.ReadonlyOnUpdate,
                RequiredOnCreate = f.RequiredOnCreate,
                RequiredOnUpdate = f.RequiredOnUpdate,
                Children = f.Children?.Select(c => new FormFieldDef
                {
                    Field = c.Field,
                    Title = c.Title,
                    Control = c.Control,
                    Span = c.Span
                }).ToList() ?? []
            }).ToList(),
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
            RequiredOnUpdate = w.RequiredOnUpdate,
            Children = w.Children?.Select(CloneWidget).ToList() ?? []
        };

    private static DictOption CloneOption(DictOption o) =>
        new()
        {
            Value = o.Value,
            Label = o.Label,
            Tone = o.Tone
        };

    private static List<NamedDict> CloneDicts(List<NamedDict>? source) =>
        (source ?? []).Select(d => new NamedDict
        {
            Code = d.Code,
            Name = d.Name,
            Options = d.Options.Select(CloneOption).ToList()
        }).ToList();
}
