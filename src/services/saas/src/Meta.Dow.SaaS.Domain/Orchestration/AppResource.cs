using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>业务资源：绑定托管表或只读查询对象 + 列表/表单 + flowKey。</summary>
public class AppResource : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    public string DataSourceCode { get; protected set; } = null!;

    public string TableName { get; protected set; } = null!;

    public string SourceKind { get; protected set; } = AppResourceSourceKind.Table;

    public Guid? QueryObjectId { get; protected set; }

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
        SourceKind = AppResourceSourceKind.Table;
        Filter = new FilterDef();
        ListView = new ListViewDef();
        Form = new FormDef();
    }

    public bool IsQueryCatalog() => AppResourceSourceKind.IsQueryCatalog(SourceKind);

    public void BindQueryObject(DbQueryObject queryObject)
    {
        ArgumentNullException.ThrowIfNull(queryObject);
        SourceKind = queryObject.Kind == QueryObjectKind.Procedure
            ? AppResourceSourceKind.Procedure
            : AppResourceSourceKind.View;
        QueryObjectId = queryObject.Id;
        DataSourceCode = queryObject.DataSourceCode;
        TableName = queryObject.ObjectName;
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

        ListView.Actions =
        [
            new ActionDef
            {
                Key = "create", Label = "新增", Position = "toolbar", Scene = "toolbar",
                Scope = "none", Kind = "create", FlowKey = CreateFlowKey, Open = "page",
                FormMode = "create", FormKey = "create"
            },
            new ActionDef
            {
                Key = "update", Label = "编辑", Position = "row", Scene = "row",
                Scope = "row", Kind = "update", FlowKey = UpdateFlowKey, Open = "page",
                FormMode = "update", FormKey = "update", Fixed = "right"
            },
            new ActionDef
            {
                Key = "detail", Label = "详情", Position = "row", Scene = "row",
                Scope = "row", Kind = "detail", FlowKey = GetFlowKey, Open = "page",
                FormMode = "detail", FormKey = "detail", Fixed = "right"
            },
            new ActionDef
            {
                Key = "delete", Label = "删除", Position = "row", Scene = "row",
                Scope = "row", Kind = "delete", FlowKey = DeleteFlowKey, Open = "none",
                Confirm = true, ConfirmText = "确认删除该记录？", Fixed = "right"
            },
            new ActionDef
            {
                Key = "batchDelete", Label = "批量删除", Position = "toolbar", Scene = "toolbar",
                Scope = "selection", Kind = "delete", FlowKey = DeleteFlowKey, Open = "none",
                Confirm = true, ConfirmText = "删除选中的记录？", BatchLimit = 100, MultiSelect = true
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
        ApplySearchForm(userCols);
    }

    public void ApplyDefaultsFromQueryObject(DbQueryObject queryObject)
    {
        ArgumentNullException.ThrowIfNull(queryObject);
        BindQueryObject(queryObject);
        var cols = queryObject.Columns.ToList();
        TitleField = cols.FirstOrDefault()?.Name ?? queryObject.Parameters.FirstOrDefault()?.Name ?? "Id";
        if (cols.Any(c => c.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)))
        {
            PrimaryKey = "Id";
        }
        else if (cols.Count > 0)
        {
            PrimaryKey = cols[0].Name;
        }

        ListView.Columns = cols.Select(c =>
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
        }).ToList();

        ListView.Actions = [];
        if (cols.Any(c => c.Name.Equals(PrimaryKey, StringComparison.OrdinalIgnoreCase)))
        {
            ListView.Actions.Add(new ActionDef
            {
                Key = "detail",
                Label = "详情",
                Position = "row",
                Scene = "row",
                Scope = "row",
                Kind = "detail",
                FlowKey = GetFlowKey,
                Open = "page",
                FormMode = "detail",
                FormKey = "detail",
                Fixed = "right"
            });
        }

        Form.Fields = cols.Select(c => new FormFieldDef
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
            VisibleOnCreate = false,
            VisibleOnUpdate = false,
            VisibleOnDetail = true,
            ReadonlyOnCreate = true,
            ReadonlyOnUpdate = true,
            RequiredOnCreate = false,
            RequiredOnUpdate = false
        }).ToList();

        Filter.Items = [];
        Filter.Combine = null;
        Filter.AdvancedFilter = queryObject.Kind == QueryObjectKind.View;
        Filter.DataScope ??= new FilterNode { Kind = "group", Op = "and" };

        if (queryObject.Kind == QueryObjectKind.Procedure)
        {
            var inputs = queryObject.Parameters.Where(p => QueryParameterDirection.IsInput(p.Direction)).ToList();
            Filter.SearchForm = new SearchFormDef
            {
                Columns = 3,
                Fields = inputs.Take(8).Select((p, i) => new SearchFormFieldDef
                {
                    Key = "p" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Field = p.Name,
                    Title = p.DisplayName,
                    Op = "eq",
                    Control = p.PlatformType switch
                    {
                        TablePlatformType.Boolean => "switch",
                        TablePlatformType.Int or TablePlatformType.Long or TablePlatformType.Decimal => "number",
                        TablePlatformType.Date => "date",
                        TablePlatformType.DateTime => "datetime",
                        _ => "input"
                    },
                    Span = 1
                }).ToList()
            };
            return;
        }

        ApplySearchForm(cols.Take(5).ToList());
    }

    private void ApplySearchForm(List<TableColumn> userCols)
    {
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
