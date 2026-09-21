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

public class SearchFormDef
{
    public int Columns { get; set; } = 3;

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

    public int? Width { get; set; }

    public bool Sortable { get; set; }

    public bool Visible { get; set; } = true;

    public string? FormatPreset { get; set; }

    public string Align { get; set; } = "left";

    public string Render { get; set; } = "text";

    public Dictionary<string, string> DictMap { get; set; } = [];

    public List<ColumnStyleRule> StyleRules { get; set; } = [];

    public string? SummaryFn { get; set; }

    public string SummaryScope { get; set; } = "filtered";
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

    public string? FormMode { get; set; }

    public string FieldsMode { get; set; } = "full";

    public List<string> Fields { get; set; } = [];

    public bool Confirm { get; set; }

    public string? ConfirmText { get; set; }

    public int BatchLimit { get; set; } = 100;
}

public class ListViewDef
{
    public List<ListColumnDef> Columns { get; set; } = [];

    public string? DefaultSorting { get; set; } = "CreationTime desc";

    public int PageSize { get; set; } = 20;

    public List<ActionDef> Actions { get; set; } = [];
}

public class FormFieldDef
{
    public string Field { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Control { get; set; } = "input";

    public bool VisibleOnCreate { get; set; } = true;

    public bool VisibleOnUpdate { get; set; } = true;

    public bool VisibleOnDetail { get; set; } = true;

    public bool ReadonlyOnCreate { get; set; }

    public bool ReadonlyOnUpdate { get; set; }

    public bool RequiredOnCreate { get; set; }

    public bool RequiredOnUpdate { get; set; }
}

public class FormDef
{
    public List<FormFieldDef> Fields { get; set; } = [];
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
    public const string Flow = "flow";
}
