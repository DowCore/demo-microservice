using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>业务资源：绑定托管表 + 列表/表单 + 五个 flowKey。</summary>
public class AppResource : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    public string DataSourceCode { get; protected set; } = null!;

    public string TableName { get; protected set; } = null!;

    public string TitleField { get; protected set; } = "Id";

    public string PrimaryKey { get; protected set; } = "Id";

    public string SoftDeleteField { get; protected set; } = "IsDeleted";

    public string TenantField { get; protected set; } = "TenantId";

    public string ConcurrencyField { get; protected set; } = "ConcurrencyStamp";

    public string QueryFlowKey { get; protected set; } = SystemResourceFlowKeys.Query;

    public string GetFlowKey { get; protected set; } = SystemResourceFlowKeys.Get;

    public string CreateFlowKey { get; protected set; } = SystemResourceFlowKeys.Create;

    public string UpdateFlowKey { get; protected set; } = SystemResourceFlowKeys.Update;

    public string DeleteFlowKey { get; protected set; } = SystemResourceFlowKeys.Delete;

    public int Status { get; protected set; } = AppResourceStatus.Draft;

    public FilterDef Filter { get; protected set; } = new();

    public ListViewDef ListView { get; protected set; } = new();

    public FormDef Form { get; protected set; } = new();

    protected AppResource()
    {
    }

    public AppResource(
        Guid id,
        string code,
        string name,
        string dataSourceCode,
        string tableName,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetCode(code);
        SetName(name);
        DataSourceCode = Check.NotNullOrWhiteSpace(dataSourceCode, nameof(dataSourceCode)).Trim();
        TableName = Check.NotNullOrWhiteSpace(tableName, nameof(tableName)).Trim();
        TenantId = tenantId;
        Status = AppResourceStatus.Draft;
        Filter = new FilterDef();
        ListView = new ListViewDef();
        Form = new FormDef();
    }

    public void UpdateDraft(
        string name,
        string? titleField,
        FilterDef? filter,
        ListViewDef? listView,
        FormDef? form,
        string? queryFlowKey,
        string? getFlowKey,
        string? createFlowKey,
        string? updateFlowKey,
        string? deleteFlowKey
    )
    {
        SetName(name);
        if (!string.IsNullOrWhiteSpace(titleField))
        {
            TitleField = titleField.Trim();
        }

        Filter = filter ?? Filter;
        ListView = listView ?? ListView;
        Form = form ?? Form;
        QueryFlowKey = NormalizeFlowKey(queryFlowKey, QueryFlowKey);
        GetFlowKey = NormalizeFlowKey(getFlowKey, GetFlowKey);
        CreateFlowKey = NormalizeFlowKey(createFlowKey, CreateFlowKey);
        UpdateFlowKey = NormalizeFlowKey(updateFlowKey, UpdateFlowKey);
        DeleteFlowKey = NormalizeFlowKey(deleteFlowKey, DeleteFlowKey);
    }

    public void ApplyDefaultsFromTable(TableDefinition table)
    {
        var userCols = table.Columns.Where(c => c.Origin != TableOrigin.Convention).ToList();
        TitleField = userCols.FirstOrDefault()?.Name ?? "Id";

        ListView.Columns = table.Columns
            .Where(c => c.Origin != TableOrigin.Convention || c.Name == "CreationTime")
            .Select(c => new ListColumnDef
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
            })
            .ToList();

        ListView.Actions =
        [
            new ActionDef
            {
                Key = "create", Label = "新增", Position = "toolbar", Scene = "toolbar",
                Scope = "none", Kind = "create", FlowKey = CreateFlowKey, Open = "drawer",
                FormMode = "create"
            },
            new ActionDef
            {
                Key = "update", Label = "编辑", Position = "row", Scene = "row",
                Scope = "row", Kind = "update", FlowKey = UpdateFlowKey, Open = "drawer",
                FormMode = "update"
            },
            new ActionDef
            {
                Key = "detail", Label = "详情", Position = "row", Scene = "row",
                Scope = "row", Kind = "detail", FlowKey = GetFlowKey, Open = "drawer",
                FormMode = "detail"
            },
            new ActionDef
            {
                Key = "delete", Label = "删除", Position = "row", Scene = "row",
                Scope = "row", Kind = "delete", FlowKey = DeleteFlowKey, Open = "none",
                Confirm = true, ConfirmText = "确认删除该记录？"
            },
            new ActionDef
            {
                Key = "batchDelete", Label = "批量删除", Position = "batch", Scene = "batch",
                Scope = "selection", Kind = "delete", FlowKey = DeleteFlowKey, Open = "none",
                Confirm = true, ConfirmText = "删除选中的记录？", BatchLimit = 100
            }
        ];

        Form.Fields = table.Columns.Select(c =>
        {
            var convention = c.Origin == TableOrigin.Convention;
            return new FormFieldDef
            {
                Field = c.Name,
                Title = c.DisplayName,
                Control = c.PlatformType switch
                {
                    TablePlatformType.Boolean => "switch",
                    TablePlatformType.Int or TablePlatformType.Long or TablePlatformType.Decimal => "number",
                    TablePlatformType.Date => "date",
                    TablePlatformType.DateTime => "datetime",
                    TablePlatformType.Text => "textarea",
                    _ => "input"
                },
                VisibleOnCreate = !convention,
                VisibleOnUpdate = !convention,
                VisibleOnDetail = !convention || c.Name is "CreationTime" or "LastModificationTime",
                ReadonlyOnCreate = convention,
                ReadonlyOnUpdate = convention || c.Name == "Id",
                RequiredOnCreate = !convention && !c.Nullable,
                RequiredOnUpdate = !convention && !c.Nullable
            };
        }).ToList();

        Filter.Items = userCols.Take(5).Select((c, i) => new FilterItemDef
        {
            No = i + 1,
            Left = c.Name,
            Op = c.PlatformType is TablePlatformType.String or TablePlatformType.Text ? "contains" : "eq",
            ValueSource = "user",
            Exposed = true
        }).ToList();
        Filter.Combine = Filter.Items.Count == 0
            ? null
            : string.Join(" and ", Filter.Items.Select(x => x.No.ToString()));
        Filter.AdvancedFilter = true;
        Filter.DataScope ??= new FilterNode { Kind = "group", Op = "and" };
        Filter.SearchForm = new SearchFormDef
        {
            Columns = 3,
            Fields = userCols.Take(5).Select((c, i) =>
            {
                var (op, control) = c.PlatformType switch
                {
                    TablePlatformType.String or TablePlatformType.Text or TablePlatformType.Enum
                        => ("contains", "input"),
                    TablePlatformType.Int or TablePlatformType.Long or TablePlatformType.Decimal
                        => ("between", "numberRange"),
                    TablePlatformType.Date => ("between", "dateRange"),
                    TablePlatformType.DateTime => ("between", "dateRange"),
                    TablePlatformType.Boolean => ("eq", "switch"),
                    _ => ("eq", "input")
                };
                return new SearchFormFieldDef
                {
                    Key = "f" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Field = c.Name,
                    Title = c.DisplayName,
                    Op = op,
                    Control = control,
                    Span = 1
                };
            }).ToList()
        };
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

    private static string NormalizeFlowKey(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
