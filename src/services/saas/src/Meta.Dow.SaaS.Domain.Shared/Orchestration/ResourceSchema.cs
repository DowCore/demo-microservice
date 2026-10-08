using System;
using System.Collections.Generic;
using System.Linq;

namespace Meta.Dow.SaaS.Orchestration;

public class FilterItemDef
{
    public int No { get; set; }

    public string Left { get; set; } = null!;

    public string Op { get; set; } = "eq";

    public string ValueSource { get; set; } = "user";

    public string? Literal { get; set; }

    public string? SystemKey { get; set; }

    public bool Exposed { get; set; } = true;
}

public class FilterNode
{
    public string Kind { get; set; } = "group";

    public string Op { get; set; } = "and";

    public string? Left { get; set; }

    public object? Right { get; set; }

    public List<FilterNode> Children { get; set; } = [];
}

public class SearchFormFieldDef
{
    public string Key { get; set; } = null!;

    public string Field { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Op { get; set; } = "eq";

    public string Control { get; set; } = "input";

    public int Span { get; set; } = 1;

    public string? Placeholder { get; set; }

    public string? OrGroup { get; set; }
}

public class DictOption
{
    public string Value { get; set; } = "";

    public string Label { get; set; } = "";

    public string? Tone { get; set; }
}

/// <summary>可复用字典：多列共用同一套码→文案→颜色。</summary>
public class NamedDict
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public List<DictOption> Options { get; set; } = [];
}

public class SearchFormDef
{
    public int Columns { get; set; } = 3;

    /// <summary>and=全部满足（默认，NocoBase Filter Form）；or=任一满足。</summary>
    public string Combine { get; set; } = "and";

    public List<SearchFormFieldDef> Fields { get; set; } = [];
}

public class NamedFilterPreset
{
    public string Key { get; set; } = null!;

    public string Label { get; set; } = null!;

    public FilterNode Filter { get; set; } = new();
}

public class FilterDef
{
    public List<FilterItemDef> Items { get; set; } = [];

    public string? Combine { get; set; }

    public FilterNode? DataScope { get; set; }

    public SearchFormDef SearchForm { get; set; } = new();

    public bool AdvancedFilter { get; set; } = true;

    public List<NamedFilterPreset> Presets { get; set; } = [];

    /// <summary>
    /// 旧版 items（暴露给用户的编号条件）升格为查询表单，避免运行时仍露出公式框。
    /// </summary>
    public void EnsureSearchForm()
    {
        SearchForm = SearchForm ?? new SearchFormDef();
        if (SearchForm.Fields.Count > 0 || Items.Count == 0)
        {
            return;
        }

        SearchForm.Columns = SearchForm.Columns <= 0 ? 3 : SearchForm.Columns;
        SearchForm.Fields = Items
            .Where(i => i.Exposed && !string.IsNullOrWhiteSpace(i.Left))
            .Select((item, index) => new SearchFormFieldDef
            {
                Key = "f" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Field = item.Left,
                Title = item.Left,
                Op = string.IsNullOrWhiteSpace(item.Op) ? "eq" : item.Op,
                Control = ControlFromOp(item.Op)
            })
            .ToList();
    }

    private static string ControlFromOp(string? op)
    {
        var normalized = (op ?? "eq").Trim().ToLowerInvariant();
        return normalized switch
        {
            "between" => "dateRange",
            "in" or "notin" => "input",
            "contains" or "notcontains" or "startswith" => "input",
            _ => "input"
        };
    }
}

public class ColumnStyleRule
{
    public string Op { get; set; } = "eq";

    public string? Value { get; set; }

    public string Tone { get; set; } = "default";

    public string Target { get; set; } = "cell";
}

public class ListColumnDef
{
    public string Field { get; set; } = null!;

    public string Title { get; set; } = null!;

    /// <summary>列宽（px）。空则自适应，表格横向滚动时按默认宽度估算。</summary>
    public int? Width { get; set; }

    public bool Sortable { get; set; }

    public bool Visible { get; set; } = true;

    public string? FormatPreset { get; set; }

    /// <summary>left / center / right</summary>
    public string Align { get; set; } = "left";

    /// <summary>超长省略并悬浮展示全文（Ant Table ellipsis）。</summary>
    public bool Ellipsis { get; set; }

    /// <summary>空 / left / right，列冻结。</summary>
    public string Fixed { get; set; } = "";

    public string Render { get; set; } = "text";

    public Dictionary<string, string> DictMap { get; set; } = [];

    public List<DictOption> Options { get; set; } = [];

    /// <summary>引用通用字典 Code；空则用本列 Options。</summary>
    public string? DictCode { get; set; }

    /// <summary>JS：参数 value / row / raw，返回展示文案。</summary>
    public string? FormatFn { get; set; }

    /// <summary>JS：参数 value / row / raw，返回 HTML。</summary>
    public string? RenderFn { get; set; }

    public List<ColumnStyleRule> StyleRules { get; set; } = [];

    public string? SummaryFn { get; set; }

    public string SummaryScope { get; set; } = "filtered";

    /// <summary>按格式预设给出对齐 / 默认列宽 / 是否省略（空宽表示自适应）。</summary>
    public static void ApplyLayoutDefaults(ListColumnDef col, string? formatPreset = null)
    {
        var preset = (formatPreset ?? col.FormatPreset ?? "text").Trim().ToLowerInvariant();
        col.Align = preset switch
        {
            "currency" or "number" or "percent" => "right",
            "boolean" => "center",
            _ => "left"
        };
        col.Width ??= preset switch
        {
            "boolean" => 80,
            "date" => 120,
            "datetime" => 170,
            "currency" or "number" or "percent" => 120,
            _ => null
        };
        if (preset is "text" or "ellipsis" || string.IsNullOrEmpty(preset))
        {
            col.Ellipsis = true;
        }
    }
}

public class ActionDef
{
    public string Key { get; set; } = null!;

    public string Label { get; set; } = null!;

    public string Position { get; set; } = "toolbar";

    public string Scene { get; set; } = "";

    public string Scope { get; set; } = "";

    public string Kind { get; set; } = "custom";

    public string? FlowKey { get; set; }

    public string Open { get; set; } = "drawer";

    public string Fixed { get; set; } = "";

    public string? FormKey { get; set; }

    public string? FormMode { get; set; }

    public string FieldsMode { get; set; } = "full";

    public List<string> Fields { get; set; } = [];

    /// <summary>调用逻辑编排时从当前行/勾选行抽取的字段；空则传整行。</summary>
    public List<string> PayloadFields { get; set; } = [];

    /// <summary>执行成功后：refresh / close / none</summary>
    public string After { get; set; } = "refresh";

    public bool Confirm { get; set; }

    public string? ConfirmText { get; set; }

    public int BatchLimit { get; set; } = 100;

    /// <summary>按钮说明，配置列表里展示。</summary>
    public string? Description { get; set; }

    /// <summary>是否对勾选行生效（工具栏批量，或行按钮同时出批量入口）。</summary>
    public bool MultiSelect { get; set; }
}

public class ListViewDef
{
    public List<ListColumnDef> Columns { get; set; } = [];

    public string? DefaultSorting { get; set; } = "CreationTime desc";

    public int PageSize { get; set; } = 20;

    public List<ActionDef> Actions { get; set; } = [];

    public List<NamedDict> Dictionaries { get; set; } = [];
}

public class FormFieldRuleDef
{
    public int? MinLength { get; set; }

    public int? MaxLength { get; set; }

    public string? Pattern { get; set; }

    public string? PatternMessage { get; set; }

    public decimal? Min { get; set; }

    public decimal? Max { get; set; }

    public string? Message { get; set; }
}

public class FormFieldDependencyDef
{
    public string SourceField { get; set; } = null!;

    public string Op { get; set; } = "eq";

    public string? Value { get; set; }

    public string Action { get; set; } = "show";

    public string? SetValue { get; set; }
}

public class FormFieldDef
{
    public string Field { get; set; } = null!;

    public string Title { get; set; } = null!;

    /// <summary>
    /// input/textarea/number/date/datetime/switch；
    /// userPicker/deptPicker/upload/radio/checkbox/select/cascader；
    /// table=明细表格；list=可重复块；tree=树形明细（对标 form-create 表格表单/嵌套/无限级）。
    /// </summary>
    public string Control { get; set; } = "input";

    /// <summary>24 栅格，12=半行，24=整行。明细容器通常 24。</summary>
    public int Span { get; set; } = 12;

    public string? Placeholder { get; set; }

    public bool VisibleOnCreate { get; set; } = true;

    public bool VisibleOnUpdate { get; set; } = true;

    public bool VisibleOnDetail { get; set; } = true;

    public bool ReadonlyOnCreate { get; set; }

    public bool ReadonlyOnUpdate { get; set; }

    public bool RequiredOnCreate { get; set; }

    public bool RequiredOnUpdate { get; set; }

    /// <summary>前端校验规则（必填外的长度/正则/数值范围）。</summary>
    public FormFieldRuleDef? Rules { get; set; }

    /// <summary>字段动态联动规则（显隐/禁用/必填/自动赋值）。</summary>
    public List<FormFieldDependencyDef> Dependencies { get; set; } = [];

    /// <summary>控件特有配置（如 userPicker 范围、upload 格式限制等）。</summary>
    public Dictionary<string, object?> ControlProps { get; set; } = [];

    /// <summary>明细列 / 子字段（table、list、tree）。</summary>
    public List<FormFieldDef> Children { get; set; } = [];

    /// <summary>明细最少行数。</summary>
    public int? MinItems { get; set; }

    /// <summary>明细最多行数。</summary>
    public int? MaxItems { get; set; }

    /// <summary>tree 子节点数组字段名，默认 children。</summary>
    public string? TreeChildrenField { get; set; }
}

/// <summary>
/// 表单节点：先容器、后基础字段（对标 VForm widgetList）。
/// kind=container：grid / card / data-table / subform；kind=field：基础控件。
/// </summary>
public class FormWidgetDef
{
    public string Id { get; set; } = "";

    /// <summary>container 或 field。</summary>
    public string Kind { get; set; } = "field";

    /// <summary>grid | card | data-table | subform。仅容器。</summary>
    public string? Container { get; set; }

    /// <summary>栅格列数 1–4。</summary>
    public int Columns { get; set; } = 2;

    public string? Title { get; set; }

    public bool Hidden { get; set; }

    public List<FormWidgetDef> Children { get; set; } = [];

    public string? Field { get; set; }

    public string Control { get; set; } = "input";

    public int Span { get; set; } = 24;

    public string? Placeholder { get; set; }

    public bool VisibleOnCreate { get; set; } = true;

    public bool VisibleOnUpdate { get; set; } = true;

    public bool VisibleOnDetail { get; set; } = true;

    public bool ReadonlyOnCreate { get; set; }

    public bool ReadonlyOnUpdate { get; set; }

    public bool RequiredOnCreate { get; set; }

    public bool RequiredOnUpdate { get; set; }

    /// <summary>字段动态联动规则（显隐/禁用/必填/自动赋值）。</summary>
    public List<FormFieldDependencyDef> Dependencies { get; set; } = [];

    /// <summary>控件特有配置。</summary>
    public Dictionary<string, object?> ControlProps { get; set; } = [];
}

public class FormDef
{
    /// <summary>扁平字段（由 layout 派生，兼容旧运行时与校验）。</summary>
    public List<FormFieldDef> Fields { get; set; } = [];

    /// <summary>容器优先的布局树。有内容时渲染以它为准。</summary>
    public List<FormWidgetDef> Layout { get; set; } = [];

    /// <summary>
    /// VForm3 描述 JSON：widgetList + formConfig（栅格列 / 表格单元格 / 标签页 / 基础与高级字段）。
    /// </summary>
    public string? VformJson { get; set; }

    /// <summary>可选：表单提交走逻辑编排，空则走资源 create/update API。</summary>
    public string? SubmitFlowKey { get; set; }
}

public class KpiDef
{
    public string Title { get; set; } = null!;

    public string Field { get; set; } = null!;

    public string Fn { get; set; } = "count";
}

public static class ReportKind
{
    public const string Resource = "resource";

    public const string Table = "table";

    public const string Flow = "flow";

    public static string Normalize(string? kind)
    {
        var value = (kind ?? Resource).Trim().ToLowerInvariant();
        return value is Table or Flow ? value : Resource;
    }
}
