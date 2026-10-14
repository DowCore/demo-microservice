using System;
using System.Linq;
using Meta.Dow.SaaS.Orchestration;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// 出参可见性相关的<b>纯逻辑</b>测试：权限名收集与可见性判定。
///
/// <para>这组测试的动机是一个容易漏掉的因果链：<c>visibleTo.mode=permissions</c> 的字段
/// 能否可见，取决于两件事——① 声明的权限名是否被 <c>CollectPermissionNames</c> 收进
/// 待校验集合；② 该权限当前用户是否真有。<b>漏收不会报错</b>，只会让字段静默消失，
/// 表现是"权限模式下的分组表头不见了"，极难定位。</para>
/// </summary>
public class OutputVisibilityTests
{
    private static FlowOutputParameterDsl Output(
        string name,
        string type = "array",
        FlowOutputMapDsl? map = null,
        FlowOutputGroupDsl? group = null,
        FlowVisibleToDsl? visibleTo = null) =>
        new()
        {
            Name = name,
            Type = type,
            From = "results.q.items",
            Map = map,
            Group = group,
            VisibleTo = visibleTo
        };

    private static FlowOutputGroupDsl Group(
        string[] headerPerms,
        string[]? childrenPerms = null,
        FlowOutputMapDsl? childrenMap = null) =>
        new()
        {
            By = System.Text.Json.Nodes.JsonValue.Create("orderId"),
            Header =
            [
                new FlowOutputGroupHeaderDsl
                {
                    Name = "orderId",
                    From = "orderId",
                    VisibleTo = headerPerms.Length == 0
                        ? null
                        : new FlowVisibleToDsl { Mode = "permissions", Permissions = [.. headerPerms] }
                }
            ],
            Children = new FlowOutputGroupChildrenDsl
            {
                Name = "items",
                Map = childrenMap,
                VisibleTo = childrenPerms == null || childrenPerms.Length == 0
                    ? null
                    : new FlowVisibleToDsl { Mode = "permissions", Permissions = [.. childrenPerms] }
            }
        };

    // ==================================================================
    // CollectPermissionNames：收集范围必须覆盖全部 visibleTo 载体
    // ==================================================================

    [Fact]
    public void Top_level_output_permissions_are_collected()
    {
        var names = FinalOutputAssembler.CollectPermissionNames(
            [Output("x", "object", visibleTo: Perms("p.top"))]).ToList();

        Assert.Contains("p.top", names);
    }

    [Fact]
    public void Map_item_permissions_are_collected()
    {
        var output = Output("rows", map: new FlowOutputMapDsl
        {
            Item = [new FlowOutputMapItemDsl { Name = "id", From = "id", VisibleTo = Perms("p.map") }]
        });

        Assert.Contains("p.map", FinalOutputAssembler.CollectPermissionNames([output]));
    }

    [Fact]
    public void Nested_map_item_permissions_are_collected()
    {
        var output = Output("rows", map: new FlowOutputMapDsl
        {
            Item =
            [
                new FlowOutputMapItemDsl
                {
                    Name = "detail",
                    From = "detail",
                    Map = new FlowOutputMapDsl
                    {
                        Item = [new FlowOutputMapItemDsl { Name = "s", From = "s", VisibleTo = Perms("p.deep") }]
                    }
                }
            ]
        });

        Assert.Contains("p.deep", FinalOutputAssembler.CollectPermissionNames([output]));
    }

    [Fact]
    public void Group_header_permissions_are_collected()
    {
        // 曾经的缺口：header 的 visibleTo.permissions 从不被收集，于是 FlowExecutor
        // 拿去IsGrantedAsync 的集合里没有它，权限模式下分组表头对所有人被丢弃。
        var names = FinalOutputAssembler.CollectPermissionNames(
            [Output("orders", group: Group(["p.header"]))]).ToList();

        Assert.Contains("p.header", names);
    }

    [Fact]
    public void Group_children_own_visible_to_permissions_are_collected()
    {
        // 第二条独立入口：children 自己的 visibleTo 决定整个子表是否可见。
        var names = FinalOutputAssembler.CollectPermissionNames(
            [Output("orders", group: Group([], childrenPerms: ["p.children"]))]).ToList();

        Assert.Contains("p.children", names);
    }

    [Fact]
    public void Group_children_map_permissions_are_collected()
    {
        var output = Output("orders", group: Group([], childrenMap: new FlowOutputMapDsl
        {
            Item = [new FlowOutputMapItemDsl { Name = "sku", From = "sku", VisibleTo = Perms("p.childmap") }]
        }));

        Assert.Contains("p.childmap", FinalOutputAssembler.CollectPermissionNames([output]));
    }

    [Fact]
    public void Empty_and_blank_permission_names_are_not_collected()
    {
        // 空串会被送去IsGrantedAsync("")，结果无意义且可能被误判为已授予。
        var output = Output("x", "object", visibleTo: new FlowVisibleToDsl { Mode = "permissions", Permissions = ["", "  ", "p.ok"] });

        var names = FinalOutputAssembler.CollectPermissionNames([output]).ToList();

        Assert.Equal(["p.ok"], names);
    }

    [Fact]
    public void Outputs_without_visible_to_collect_nothing()
    {
        var output = Output("x", "object", map: new FlowOutputMapDsl
        {
            Item = [new FlowOutputMapItemDsl { Name = "id", From = "id" }]
        });

        Assert.Empty(FinalOutputAssembler.CollectPermissionNames([output]));
    }

    // ==================================================================
    // OutputVisibilityFilter：兜底方向必须是"拒绝"而不是"放行"
    // ==================================================================

    [Fact]
    public void Unknown_mode_falls_back_to_visible()
    {
        // 这是 §14 记录过的关键取舍：运行期不再对未知 mode 兜底放行，
        // 因为发布期已经会拒（ValidateVisibleTo）；此处保持放行仅为兼容
        // 直接构造模型对象的路径。两层防线不能同时失效。
        Assert.True(OutputVisibilityFilter.IsVisible(
            new FlowVisibleToDsl { Mode = "谁都没听过的模式" }, new HashSet<string> { "admin" }, new HashSet<string> { "perm" }));
    }

    [Fact]
    public void Null_visible_to_is_visible_to_everyone()
    {
        Assert.True(OutputVisibilityFilter.IsVisible(null, new HashSet<string>(), new HashSet<string>()));
    }

    private static FlowVisibleToDsl Perms(string p) =>
        new() { Mode = "permissions", Permissions = [p] };
}