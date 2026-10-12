using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// OA 流程定义发布前的结构校验（对标逻辑编排的 <c>FlowExecutor.ValidateDsl</c>）。
/// 覆盖：节点 id 唯一且类型合法、start/end 唯一、连线端点存在、无环、非结束节点必须有出线、
/// 审批/抄送节点办理人完整、条件引用的字段属于绑定表单。
/// 目标是让配置错误在「发布口」就被拦下，而不是在运行时以最坏形态爆发。
/// </summary>
public static class WorkflowDefinitionValidator
{
    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "start",
        "end",
        "approver",
        "cc",
        "condition",
    };

    private static readonly HashSet<string> KnownAssigneeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "role",
        "user",
        "starter",
        "manager",
        "formfield",
        "flow",
    };

    private static readonly HashSet<string> KnownMulti = new(StringComparer.OrdinalIgnoreCase)
    {
        "single",
        "all",
        "any",
        "sequential",
        "ratio",
    };

    /// <summary>
    /// 校验流程定义。失败抛出 <see cref="UserFriendlyException"/>，消息可直接展示给配置者。
    /// </summary>
    /// <param name="processJson">流程定义 JSON（nodes/edges 或旧 childNode 链）。</param>
    /// <param name="formFields">绑定表单的字段名集合；为空时跳过字段引用校验。</param>
    public static void Validate(string? processJson, IReadOnlyCollection<string>? formFields)
    {
        var (nodes, edges) = Parse(processJson);

        if (nodes.Count == 0)
        {
            throw Fail("流程为空：没有解析到任何节点。请先画出开始、接收、结束节点。");
        }

        // 1. 节点 id 唯一且类型合法
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw Fail("存在没有 nodeId 的节点");
            }

            if (!ids.Add(node.Id))
            {
                throw Fail($"节点 id 重复：{node.Id}");
            }

            if (!KnownTypes.Contains(node.Type))
            {
                throw Fail($"节点「{node.Id}」类型「{node.Type}」无效（仅支持 start/end/approver/cc/condition）");
            }
        }

        // 2. start / end
        var starts = nodes.Where(x => Eq(x.Type, "start")).ToList();
        if (starts.Count != 1)
        {
            throw Fail($"必须有且只有一个开始节点，当前 {starts.Count} 个");
        }

        if (!nodes.Any(x => Eq(x.Type, "end")))
        {
            throw Fail("至少需要一个结束节点");
        }

        // 3. 连线端点必须存在
        foreach (var edge in edges)
        {
            if (!ids.Contains(edge.Source))
            {
                throw Fail($"连线起点「{edge.Source}」不存在");
            }

            if (!ids.Contains(edge.Target))
            {
                throw Fail($"连线「{edge.Source} → {edge.Target}」指向不存在的节点「{edge.Target}」");
            }
        }

        // 4. 非结束节点必须有出线（避免静默走完）
        foreach (var node in nodes.Where(x => !Eq(x.Type, "end")))
        {
            if (!edges.Any(e => Eq(e.Source, node.Id)))
            {
                throw Fail($"节点「{node.Id}」没有出线，流程会在此处静默结束");
            }
        }

        // 5. 审批 / 抄送节点办理人完整、枚举合法
        foreach (var node in nodes.Where(x => Eq(x.Type, "approver") || Eq(x.Type, "cc")))
        {
            ValidateAssigneeNode(node);
        }

        // 6. 条件引用的字段属于绑定表单
        if (formFields is { Count: > 0 })
        {
            var fields = new HashSet<string>(formFields, StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.ConditionField))
                {
                    CheckRecordField($"节点「{node.Id}」的离开条件", node.ConditionField!, fields);
                }

                CheckItems($"节点「{node.Id}」的离开条件", node.LeaveItems, fields);
            }

            foreach (var edge in edges)
            {
                if (!string.IsNullOrWhiteSpace(edge.ConditionField))
                {
                    CheckRecordField($"连线「{edge.Source} → {edge.Target}」", edge.ConditionField!, fields);
                }

                CheckItems($"连线「{edge.Source} → {edge.Target}」", edge.Items, fields);
            }
        }

        // 7. W6：同一节点的条件出边若使用了显式优先级，则彼此不得重复（避免走向依赖画布数组顺序）
        CheckEdgePriorities(edges);

        // 8. 从开始节点可达范围内不允许有环
        DetectCycle(starts[0].Id, edges);
    }

    /// <summary>
    /// W6：当同一节点存在多条条件出边且配置了显式优先级时，优先级必须互不相同。
    /// 未配置优先级（全为 0）时保持向后兼容，按画布连线顺序求值。
    /// </summary>
    private static void CheckEdgePriorities(List<VEdge> edges)
    {
        foreach (var group in edges.GroupBy(e => e.Source, StringComparer.OrdinalIgnoreCase))
        {
            var conditional = group.Where(HasCondition).ToList();
            if (conditional.Count < 2)
            {
                continue;
            }

            var duplicate = conditional
                .Where(e => e.Priority > 0)
                .GroupBy(e => e.Priority)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                var targets = string.Join("、", duplicate.Select(e => e.Target));
                throw Fail(
                    $"节点「{group.Key}」有多条同为优先级 {duplicate.Key} 的条件分支（指向 {targets}），走向不明确。请为条件分支设置互不相同的优先级"
                );
            }
        }
    }

    private static bool HasCondition(VEdge edge) =>
        edge.Items is { Count: > 0 } || !string.IsNullOrWhiteSpace(edge.ConditionField);

    private static void ValidateAssigneeNode(VNode node)
    {
        var type = (node.AssigneeType ?? "role").Trim();
        if (!KnownAssigneeTypes.Contains(type))
        {
            throw Fail($"节点「{node.Id}」的办理人规则「{type}」无效（仅支持 role/user/starter/manager/formField/flow）");
        }

        if (!string.IsNullOrWhiteSpace(node.Multi) && !KnownMulti.Contains(node.Multi!.Trim()))
        {
            throw Fail($"节点「{node.Id}」的多人规则「{node.Multi}」无效（仅支持 single/all/any/sequential/ratio）");
        }

        switch (type.ToLowerInvariant())
        {
            case "user" when string.IsNullOrWhiteSpace(node.AssigneeValue):
                throw Fail($"节点「{node.Id}」的办理人规则是「指定用户」，必须填写用户名");
            case "formfield" when string.IsNullOrWhiteSpace(node.AssigneeValue):
                throw Fail($"节点「{node.Id}」的办理人规则是「表单字段里的人」，必须填写字段名");
            case "role" when string.IsNullOrWhiteSpace(node.AssigneeValue):
                throw Fail($"节点「{node.Id}」的办理人规则是「指定角色」，必须填写角色码");
            case "flow" when string.IsNullOrWhiteSpace(node.AssigneeFlowKey):
                throw Fail($"节点「{node.Id}」的办理人规则是「逻辑编排返回候选人」，必须指定选人流程");
        }
    }

    private static void CheckItems(string where, List<VItem>? items, HashSet<string> fields)
    {
        if (items == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item.Source is null || !item.Source.Equals("record", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Path))
            {
                continue;
            }

            CheckRecordField(where, item.Path!, fields);
        }
    }

    private static void CheckRecordField(string where, string path, HashSet<string> fields)
    {
        var first = path
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
        {
            return;
        }

        if (!fields.Contains(first))
        {
            throw Fail($"{where} 引用了绑定表单里不存在的字段「{first}」");
        }
    }

    private static void DetectCycle(string startId, List<VEdge> edges)
    {
        var outgoing = edges
            .GroupBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Target).ToList(),
                StringComparer.OrdinalIgnoreCase
            );

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Dfs(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!visiting.Add(id))
            {
                throw Fail($"流程存在环路：节点「{id}」被重复经过");
            }

            if (outgoing.TryGetValue(id, out var next))
            {
                foreach (var target in next)
                {
                    Dfs(target);
                }
            }

            visiting.Remove(id);
            visited.Add(id);
        }

        Dfs(startId);
    }

    private static (List<VNode> Nodes, List<VEdge> Edges) Parse(string? processJson)
    {
        var nodes = new List<VNode>();
        var edges = new List<VEdge>();
        if (string.IsNullOrWhiteSpace(processJson))
        {
            return (nodes, edges);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(processJson);
        }
        catch
        {
            throw Fail("流程定义不是合法 JSON");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (
                root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("nodes", out var nodesEl)
                && nodesEl.ValueKind == JsonValueKind.Array
            )
            {
                foreach (var item in nodesEl.EnumerateArray())
                {
                    var id = ReadString(item, "id");
                    var hasData =
                        item.TryGetProperty("data", out var data)
                        && data.ValueKind == JsonValueKind.Object;
                    var type = hasData ? ReadString(data, "type") : "";
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        type = ReadString(item, "type");
                    }

                    nodes.Add(
                        new VNode(
                            id,
                            type,
                            hasData ? NullIfEmpty(ReadString(data, "assigneeType")) : null,
                            hasData ? NullIfEmpty(ReadString(data, "assigneeValue")) : null,
                            hasData ? NullIfEmpty(ReadString(data, "assigneeFlowKey")) : null,
                            hasData ? NullIfEmpty(ReadString(data, "multi")) : null,
                            hasData ? NullIfEmpty(ReadString(data, "conditionField")) : null,
                            hasData ? ReadItems(data) : null
                        )
                    );
                }

                if (root.TryGetProperty("edges", out var edgesEl) && edgesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var edge in edgesEl.EnumerateArray())
                    {
                        var hasData =
                            edge.TryGetProperty("data", out var data)
                            && data.ValueKind == JsonValueKind.Object;
                        edges.Add(
                            new VEdge(
                                ReadString(edge, "source"),
                                ReadString(edge, "target"),
                                hasData ? NullIfEmpty(ReadString(data, "conditionField")) : null,
                                hasData ? ReadItems(data) : null,
                                hasData ? ReadInt(data, "priority") : 0
                            )
                        );
                    }
                }

                return (nodes, edges);
            }

            // 旧的 childNode 链格式
            var current = root;
            while (current.ValueKind == JsonValueKind.Object)
            {
                var id = ReadString(current, "nodeId");
                if (string.IsNullOrWhiteSpace(id))
                {
                    break;
                }

                nodes.Add(
                    new VNode(
                        id,
                        ReadString(current, "type"),
                        NullIfEmpty(ReadString(current, "assigneeType")),
                        NullIfEmpty(ReadString(current, "assigneeValue")),
                        NullIfEmpty(ReadString(current, "assigneeFlowKey")),
                        NullIfEmpty(ReadString(current, "multi")),
                        NullIfEmpty(ReadString(current, "conditionField")),
                        ReadItems(current)
                    )
                );

                if (current.TryGetProperty("childNode", out var child) && child.ValueKind == JsonValueKind.Object)
                {
                    var childId = ReadString(child, "nodeId");
                    if (!string.IsNullOrWhiteSpace(childId))
                    {
                        edges.Add(new VEdge(id, childId, null, null));
                    }

                    current = child;
                }
                else
                {
                    break;
                }
            }
        }

        return (nodes, edges);
    }

    private static List<VItem>? ReadItems(JsonElement owner)
    {
        if (owner.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (owner.TryGetProperty("items", out var direct) && direct.ValueKind == JsonValueKind.Array)
        {
            return ParseItems(direct);
        }

        if (
            owner.TryGetProperty("leaveCondition", out var block)
            && block.ValueKind == JsonValueKind.Object
            && block.TryGetProperty("items", out var nested)
            && nested.ValueKind == JsonValueKind.Array
        )
        {
            return ParseItems(nested);
        }

        return null;
    }

    private static List<VItem> ParseItems(JsonElement array)
    {
        var list = new List<VItem>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? source = null;
            string? path = null;
            if (item.TryGetProperty("left", out var left) && left.ValueKind == JsonValueKind.Object)
            {
                source = NullIfEmpty(ReadString(left, "source"));
                path = NullIfEmpty(ReadString(left, "path"));
            }

            list.Add(new VItem(source, path));
        }

        return list;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return "";
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static int ReadInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out var s) => s,
            _ => 0,
        };
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Eq(string? left, string right) =>
        string.Equals(left?.Trim(), right, StringComparison.OrdinalIgnoreCase);

    private static UserFriendlyException Fail(string message) => new(message);

    private sealed record VNode(
        string Id,
        string Type,
        string? AssigneeType,
        string? AssigneeValue,
        string? AssigneeFlowKey,
        string? Multi,
        string? ConditionField,
        List<VItem>? LeaveItems
    );

    private sealed record VItem(string? Source, string? Path);

    private sealed record VEdge(string Source, string Target, string? ConditionField, List<VItem>? Items, int Priority = 0);
}
