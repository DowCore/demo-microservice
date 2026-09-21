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

    public string QueryFlowKey { get; protected set; } = SystemResourceFlowKeys.Query;

    public int Status { get; protected set; } = AppResourceStatus.Draft;

    public FilterNode DataScope { get; protected set; } = new();

    public SearchFormDef SearchForm { get; protected set; } = new();

    public bool AdvancedFilter { get; protected set; } = true;

    public List<NamedFilterPreset> Presets { get; protected set; } = [];

    public List<KpiDef> Kpis { get; protected set; } = [];

    public List<ListColumnDef> Columns { get; protected set; } = [];

    public List<ActionDef> Actions { get; protected set; } = [];

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
        Kind = string.IsNullOrWhiteSpace(kind) ? ReportKind.Resource : kind.Trim().ToLowerInvariant();
        TenantId = tenantId;
        Status = AppResourceStatus.Draft;
        DataScope = new FilterNode { Kind = "group", Op = "and" };
        SearchForm = new SearchFormDef();
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
        string? queryFlowKey,
        FilterNode? dataScope,
        SearchFormDef? searchForm,
        bool advancedFilter,
        List<NamedFilterPreset>? presets,
        List<KpiDef>? kpis,
        List<ListColumnDef>? columns,
        List<ActionDef>? actions,
        string? defaultSorting,
        int? pageSize,
        bool selectionEnabled,
        string? description
    )
    {
        SetName(name);
        Kind = string.IsNullOrWhiteSpace(kind) ? Kind : kind.Trim().ToLowerInvariant();
        ResourceCode = string.IsNullOrWhiteSpace(resourceCode) ? null : resourceCode.Trim();
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
        QueryFlowKey = resource.QueryFlowKey;
        resource.Filter.EnsureSearchForm();
        SearchForm = resource.Filter.SearchForm ?? new SearchFormDef();
        AdvancedFilter = resource.Filter.AdvancedFilter;
        DataScope = resource.Filter.DataScope ?? new FilterNode { Kind = "group", Op = "and" };
        Presets = resource.Filter.Presets ?? [];
        Columns = resource.ListView.Columns.Select(CloneColumn).ToList();
        Actions = resource.ListView.Actions.Select(CloneAction).ToList();
        DefaultSorting = resource.ListView.DefaultSorting;
        PageSize = resource.ListView.PageSize;
        SelectionEnabled = Actions.Any(a =>
            string.Equals(a.Scene, "batch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Position, "batch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.Scope, "selection", StringComparison.OrdinalIgnoreCase));
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
            Render = c.Render,
            DictMap = new Dictionary<string, string>(c.DictMap),
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
            Open = a.Open,
            FormMode = a.FormMode,
            FieldsMode = a.FieldsMode,
            Fields = [.. a.Fields],
            Confirm = a.Confirm,
            ConfirmText = a.ConfirmText,
            BatchLimit = a.BatchLimit
        };
}
