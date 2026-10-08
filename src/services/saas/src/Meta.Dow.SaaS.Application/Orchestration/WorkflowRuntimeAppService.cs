using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class WorkflowRuntimeAppService : SaaSAppService, IWorkflowRuntimeAppService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IRepository<WorkflowDefinition, Guid> _definitions;
    private readonly IRepository<WorkflowInstance, Guid> _instances;
    private readonly IRepository<WorkflowTask, Guid> _tasks;
    private readonly IPublishedFlowInvoker _flows;

    public WorkflowRuntimeAppService(
        IRepository<WorkflowDefinition, Guid> definitions,
        IRepository<WorkflowInstance, Guid> instances,
        IRepository<WorkflowTask, Guid> tasks,
        IPublishedFlowInvoker flows
    )
    {
        _definitions = definitions;
        _instances = instances;
        _tasks = tasks;
        _flows = flows;
    }

    [Authorize(OrchestrationPermissions.Workflows.Default)]
    public async Task<WorkflowInstanceDto> StartAsync(StartWorkflowDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var definition = await FindPublishedAsync(input.Code);
        var user = CurrentUserName();
        var recordJson = string.IsNullOrWhiteSpace(input.RecordJson) ? "{}" : input.RecordJson;
        await InvokeFlowAsync(
            definition.BeforeStartFlowKey,
            definition.FormRef,
            recordJson,
            "start",
            null,
            true
        );

        var instance = new WorkflowInstance(
            GuidGenerator.Create(),
            definition.Code,
            definition.FormRef,
            recordJson,
            user,
            CurrentTenant.Id
        );
        await _instances.InsertAsync(instance, autoSave: true);

        var graph = LoadGraph(definition.ProcessJson);
        var start = graph.Nodes.Values.FirstOrDefault(n =>
            string.Equals(n.Type, "start", StringComparison.OrdinalIgnoreCase))
            ?? throw new UserFriendlyException("流程里没有开始节点");
        await EnterAsync(instance, graph, start);
        if (instance.Status != "running")
        {
            await FinishAsync(instance, definition);
        }

        return await MapInstanceAsync(instance);
    }

    [Authorize(OrchestrationPermissions.Workflows.Default)]
    public async Task<ListResultDto<WorkflowTaskDto>> GetMyTasksAsync()
    {
        var user = CurrentUserName();
        var roles = CurrentUser.Roles ?? [];
        var query = await _tasks.GetQueryableAsync();
        var pending = query.Where(x => x.Status == "pending").ToList();
        var mine = pending.Where(x => x.Kind == "approve" && x.Matches(user, roles)).Take(100).ToList();
        if (mine.Count == 0)
        {
            return new ListResultDto<WorkflowTaskDto>([]);
        }

        var instanceIds = mine.Select(x => x.InstanceId).Distinct().ToList();
        var instances = (await _instances.GetQueryableAsync())
            .Where(x => instanceIds.Contains(x.Id))
            .ToList()
            .ToDictionary(x => x.Id);

        return new ListResultDto<WorkflowTaskDto>(
            mine.Select(task => MapTask(task, instances.GetValueOrDefault(task.InstanceId))).ToList()
        );
    }

    [Authorize(OrchestrationPermissions.Workflows.Default)]
    public async Task<WorkflowInstanceDto> CompleteAsync(Guid taskId, CompleteWorkflowTaskDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var task = await _tasks.GetAsync(taskId);
        var user = CurrentUserName();
        if (task.Status != "pending" || task.Kind != "approve" || !task.Matches(user, CurrentUser.Roles ?? []))
        {
            throw new UserFriendlyException("这条待办不是你的，或已经办理过");
        }

        var instance = await _instances.GetAsync(task.InstanceId);
        if (instance.Status != "running")
        {
            throw new UserFriendlyException("流程已经结束");
        }

        var definition = await FindPublishedAsync(instance.WorkflowCode);
        var graph = LoadGraph(definition.ProcessJson);
        var node = graph.Nodes.GetValueOrDefault(task.NodeId)
            ?? throw new UserFriendlyException("流程定义里已经没有这个节点");

        if (input.Pass && string.Equals(node.Opinion, "required", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(input.Opinion))
        {
            throw new UserFriendlyException("这个节点必须填写处理意见");
        }

        if (input.Pass)
        {
            task.Approve(input.Opinion);
        }
        else
        {
            task.Reject(input.Opinion);
        }

        await _tasks.UpdateAsync(task, autoSave: true);
        var siblings = (await _tasks.GetQueryableAsync())
            .Where(x => x.InstanceId == instance.Id && x.NodeId == task.NodeId && x.Kind == "approve")
            .ToList();
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].Id == task.Id)
            {
                siblings[i] = task;
            }
        }

        if (!input.Pass && ShouldRejectNode(node, siblings))
        {
            await CancelOpenAsync(siblings);
            await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, input.Opinion, false);
            instance.Reject("rejected");
            await FinishAsync(instance, definition);
            return await MapInstanceAsync(instance);
        }

        if (string.Equals(node.Multi, "sequential", StringComparison.OrdinalIgnoreCase))
        {
            var next = siblings
                .Where(x => x.Status == "waiting")
                .OrderBy(x => x.Sequence)
                .FirstOrDefault();
            if (input.Pass && next != null && !NodePassed(node, siblings))
            {
                next.Activate();
                await _tasks.UpdateAsync(next, autoSave: true);
                return await MapInstanceAsync(instance);
            }
        }

        if (!NodePassed(node, siblings))
        {
            return await MapInstanceAsync(instance);
        }

        await CancelOpenAsync(siblings);
        await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, input.Opinion, true);
        if (!await ConditionPasses(node, instance))
        {
            instance.Complete("condition-rejected");
            await FinishAsync(instance, definition);
            return await MapInstanceAsync(instance);
        }

        var (following, missed) = await ChooseNext(graph, node, instance);
        if (missed)
        {
            instance.Complete("condition-rejected");
            await FinishAsync(instance, definition);
            return await MapInstanceAsync(instance);
        }

        await EnterAsync(instance, graph, following);
        if (instance.Status != "running")
        {
            await FinishAsync(instance, definition);
        }

        return await MapInstanceAsync(instance);
    }

    private async Task FinishAsync(WorkflowInstance instance, WorkflowDefinition definition)
    {
        await InvokeFlowAsync(
            definition.AfterEndFlowKey,
            instance.FormRef,
            instance.RecordJson,
            "end",
            instance.Result,
            string.Equals(instance.Result, "approved", StringComparison.OrdinalIgnoreCase)
        );
        await _instances.UpdateAsync(instance, autoSave: true);
    }

    private async Task EnterAsync(WorkflowInstance instance, WfGraph graph, WfProcessNode? node)
    {
        while (node != null)
        {
            if (string.Equals(node.Type, "end", StringComparison.OrdinalIgnoreCase))
            {
                instance.Complete("approved");
                await _instances.UpdateAsync(instance, autoSave: true);
                return;
            }

            if (string.Equals(node.Type, "start", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Type, "condition", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Type, "cc", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(node.Type, "cc", StringComparison.OrdinalIgnoreCase))
                {
                    await InvokeFlowAsync(node.BeforeFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
                    await CreateTasksAsync(instance, node, notifyOnly: true);
                    await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
                }
                else
                {
                    await InvokeFlowAsync(node.BeforeFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
                    await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
                }

                var (next, missed) = await ChooseNext(graph, node, instance);
                if (missed)
                {
                    instance.Complete("condition-rejected");
                    await _instances.UpdateAsync(instance, autoSave: true);
                    return;
                }

                node = next;
                continue;
            }

            await InvokeFlowAsync(node.BeforeFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
            await CreateTasksAsync(instance, node, notifyOnly: false);
            instance.MoveTo(node.NodeId);
            await _instances.UpdateAsync(instance, autoSave: true);
            return;
        }

        instance.Complete("approved");
        await _instances.UpdateAsync(instance, autoSave: true);
    }

    private async Task<(WfProcessNode? Next, bool Missed)> ChooseNext(
        WfGraph graph,
        WfProcessNode node,
        WorkflowInstance instance
    )
    {
        if (!graph.Outgoing.TryGetValue(node.NodeId, out var edges) || edges.Count == 0)
        {
            return (null, false);
        }

        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (edges.Count == 1)
        {
            var only = edges[0];
            if (!EdgeHasCondition(only) || await EdgeMatchesAsync(only, instance, node.NodeId, cache))
            {
                return (graph.Nodes.GetValueOrDefault(only.TargetId), false);
            }

            return (null, true);
        }

        foreach (var edge in edges.Where(EdgeHasCondition))
        {
            if (await EdgeMatchesAsync(edge, instance, node.NodeId, cache))
            {
                return (graph.Nodes.GetValueOrDefault(edge.TargetId), false);
            }
        }

        var fallback = edges.FirstOrDefault(edge => !EdgeHasCondition(edge));
        if (fallback != null)
        {
            return (graph.Nodes.GetValueOrDefault(fallback.TargetId), false);
        }

        return (null, true);
    }

    private static bool EdgeHasCondition(WfEdge edge) =>
        edge.Items is { Count: > 0 } || !string.IsNullOrWhiteSpace(edge.ConditionField);

    private async Task<bool> EdgeMatchesAsync(
        WfEdge edge,
        WorkflowInstance instance,
        string nodeId,
        Dictionary<string, string?> flowCache
    )
    {
        if (edge.Items is { Count: > 0 })
        {
            return await BlockPassesAsync(instance, nodeId, edge.Items, edge.Combine, flowCache);
        }

        if (string.IsNullOrWhiteSpace(edge.ConditionField))
        {
            return true;
        }

        return CompareValues(
            ReadPath(instance.RecordJson, edge.ConditionField),
            edge.ConditionValue ?? "",
            edge.ConditionOp
        );
    }

    private async Task CreateTasksAsync(WorkflowInstance instance, WfProcessNode node, bool notifyOnly)
    {
        var people = await ResolveAssigneesAsync(node, instance);
        if (people.Count == 0)
        {
            throw new UserFriendlyException($"节点「{node.Name}」没有办理人");
        }

        var sequential = !notifyOnly && string.Equals(node.Multi, "sequential", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < people.Count; i++)
        {
            var person = people[i];
            var status = notifyOnly
                ? "notified"
                : sequential && i > 0
                    ? "waiting"
                    : "pending";
            var task = new WorkflowTask(
                GuidGenerator.Create(),
                instance.Id,
                node.NodeId,
                node.Name,
                notifyOnly ? "cc" : "approve",
                status,
                person.UserName,
                person.Role,
                i,
                CurrentTenant.Id
            );
            await _tasks.InsertAsync(task, autoSave: true);
        }
    }

    private async Task<List<AssigneePick>> ResolveAssigneesAsync(WfProcessNode node, WorkflowInstance instance)
    {
        var type = (node.AssigneeType ?? "role").Trim().ToLowerInvariant();
        var value = node.AssigneeValue?.Trim() ?? "";
        switch (type)
        {
            case "user":
                return string.IsNullOrWhiteSpace(value) ? [] : [new AssigneePick(value, null)];
            case "starter":
                return [new AssigneePick(instance.StarterUserName, null)];
            case "formfield":
                return ReadUsers(instance.RecordJson, value).Select(name => new AssigneePick(name, null)).ToList();
            case "flow":
                var data = await InvokeFlowAsync(
                    node.AssigneeFlowKey,
                    instance.FormRef,
                    instance.RecordJson,
                    node.NodeId,
                    null,
                    true
                );
                return ReadUsersFromFlow(data).Select(name => new AssigneePick(name, null)).ToList();
            case "manager":
                return [new AssigneePick(null, string.IsNullOrWhiteSpace(value) ? "manager" : value)];
            default:
                return string.IsNullOrWhiteSpace(value) ? [] : [new AssigneePick(null, value)];
        }
    }

    private static bool ShouldRejectNode(WfProcessNode node, List<WorkflowTask> siblings)
    {
        var multi = (node.Multi ?? "single").ToLowerInvariant();
        var active = siblings.Where(x => x.Status != "cancelled").ToList();
        var approved = active.Count(x => x.Status == "approved");
        var open = active.Count(x => x.Status is "pending" or "waiting");
        if (multi == "any")
        {
            return open == 0 && approved == 0;
        }

        if (multi == "ratio")
        {
            var need = active.Count * Math.Clamp(node.MultiRatio ?? 100, 1, 100);
            return (approved + open) * 100 < need;
        }

        return true;
    }

    private static bool NodePassed(WfProcessNode node, List<WorkflowTask> siblings)
    {
        var active = siblings.Where(x => x.Status != "cancelled").ToList();
        var approved = active.Count(x => x.Status == "approved");
        var pending = active.Count(x => x.Status is "pending" or "waiting");
        var multi = (node.Multi ?? "single").ToLowerInvariant();
        return multi switch
        {
            "all" => active.Count > 0 && approved == active.Count,
            "any" => approved >= 1,
            "sequential" => pending == 0 && approved == active.Count,
            "ratio" => active.Count > 0 && approved * 100 >= active.Count * Math.Clamp(node.MultiRatio ?? 100, 1, 100),
            _ => approved >= 1,
        };
    }

    private async Task CancelOpenAsync(List<WorkflowTask> siblings)
    {
        foreach (var task in siblings.Where(x => x.Status is "pending" or "waiting"))
        {
            task.Cancel();
            await _tasks.UpdateAsync(task, autoSave: true);
        }
    }

    private async Task<bool> ConditionPasses(WfProcessNode node, WorkflowInstance instance)
    {
        if (node.LeaveCondition?.Items is { Count: > 0 })
        {
            return await BlockPassesAsync(
                instance,
                node.NodeId,
                node.LeaveCondition.Items,
                node.LeaveCondition.Combine,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            );
        }

        if (string.IsNullOrWhiteSpace(node.ConditionField))
        {
            return true;
        }

        return CompareValues(
            ReadPath(instance.RecordJson, node.ConditionField),
            node.ConditionValue ?? "",
            node.ConditionOp
        );
    }

    private async Task<bool> BlockPassesAsync(
        WorkflowInstance instance,
        string nodeId,
        IReadOnlyList<WfConditionItem> items,
        string? combine,
        Dictionary<string, string?> flowCache
    )
    {
        var results = new Dictionary<int, bool>();
        foreach (var item in items)
        {
            var no = item.No > 0 ? item.No : results.Count + 1;
            var left = await ResolveValueAsync(item.Left, instance, nodeId, flowCache);
            var op = item.Op ?? "eq";
            var right = op is "isEmpty" or "isNotEmpty"
                ? ""
                : await ResolveValueAsync(item.Right, instance, nodeId, flowCache);
            results[no] = CompareValues(left, right, op);
        }

        if (results.Count == 0)
        {
            return true;
        }

        var expression = string.IsNullOrWhiteSpace(combine)
            ? string.Join(" and ", results.Keys.OrderBy(no => no))
            : combine;
        return ConditionCombineEvaluator.Evaluate(expression, results);
    }

    private async Task<string> ResolveValueAsync(
        WfValueRef? source,
        WorkflowInstance instance,
        string nodeId,
        Dictionary<string, string?> flowCache
    )
    {
        var kind = (source?.Source ?? "const").Trim().ToLowerInvariant();
        var path = source?.Path?.Trim() ?? "";
        switch (kind)
        {
            case "record":
                return ReadPath(instance.RecordJson, path);
            case "system":
                var key = path.StartsWith("sys.", StringComparison.OrdinalIgnoreCase) ? path[4..] : path;
                return key.ToLowerInvariant() switch
                {
                    "starter" => instance.StarterUserName,
                    "currentuser" => CurrentUserName(),
                    "now" => DateTime.Now.ToString("yyyy-MM-dd"),
                    "formref" => instance.FormRef ?? "",
                    "workflowcode" => instance.WorkflowCode,
                    _ => "",
                };
            case "flow":
                var flowKey = source?.FlowKey?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(flowKey))
                {
                    return "";
                }

                if (!flowCache.TryGetValue(flowKey, out var dataJson))
                {
                    dataJson = await InvokeFlowAsync(flowKey, instance.FormRef, instance.RecordJson, nodeId, null, true);
                    flowCache[flowKey] = dataJson;
                }

                return ReadPath(dataJson ?? "", path);
            default:
                return source?.Value ?? "";
        }
    }

    private static bool CompareValues(string left, string right, string? opName)
    {
        var op = (opName ?? "eq").ToLowerInvariant();
        if (op == "isempty")
        {
            return string.IsNullOrWhiteSpace(left);
        }

        if (op == "isnotempty")
        {
            return !string.IsNullOrWhiteSpace(left);
        }

        if (double.TryParse(left, out var ln) && double.TryParse(right, out var rn))
        {
            return op switch
            {
                "gt" => ln > rn,
                "gte" => ln >= rn,
                "lt" => ln < rn,
                "lte" => ln <= rn,
                "ne" => Math.Abs(ln - rn) > 0.0000001,
                "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                "notcontains" => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
                _ => Math.Abs(ln - rn) < 0.0000001,
            };
        }

        return op switch
        {
            "gt" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) > 0,
            "gte" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) >= 0,
            "lt" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) < 0,
            "lte" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0,
            "ne" => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            "contains" => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            "notcontains" => !left.Contains(right, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
        };
    }

    private async Task<string?> InvokeFlowAsync(
        string? flowKey,
        string? formRef,
        string recordJson,
        string nodeId,
        string? opinion,
        bool pass
    )
    {
        if (string.IsNullOrWhiteSpace(flowKey))
        {
            return null;
        }

        JsonElement record;
        try
        {
            record = JsonSerializer.Deserialize<JsonElement>(recordJson);
        }
        catch
        {
            record = JsonSerializer.Deserialize<JsonElement>("{}");
        }

        var body = JsonSerializer.Serialize(new
        {
            formRef,
            record,
            nodeId,
            opinion,
            pass,
        });
        var result = await _flows.InvokeAsync(flowKey.Trim(), body, "workflow", CurrentTenant.Id);
        if (!result.Success)
        {
            throw new UserFriendlyException(result.Error ?? $"逻辑编排 {flowKey} 执行失败");
        }

        return result.DataJson;
    }

    private async Task<WorkflowDefinition> FindPublishedAsync(string code)
    {
        var query = await _definitions.GetQueryableAsync();
        var definition = query.FirstOrDefault(x => x.Code == code.Trim() && x.Status == AppResourceStatus.Published);
        if (definition == null)
        {
            throw new UserFriendlyException("审批流不存在或未发布");
        }

        return definition;
    }

    private string CurrentUserName() =>
        CurrentUser.UserName
        ?? CurrentUser.Email
        ?? CurrentUser.Id?.ToString()
        ?? "anonymous";

    private async Task<WorkflowInstanceDto> MapInstanceAsync(WorkflowInstance instance)
    {
        var tasks = (await _tasks.GetQueryableAsync())
            .Where(x => x.InstanceId == instance.Id)
            .OrderBy(x => x.Sequence)
            .ToList();
        return new WorkflowInstanceDto
        {
            Id = instance.Id,
            WorkflowCode = instance.WorkflowCode,
            FormRef = instance.FormRef,
            Status = instance.Status,
            Result = instance.Result,
            CurrentNodeId = instance.CurrentNodeId,
            StarterUserName = instance.StarterUserName,
            RecordJson = instance.RecordJson,
            Tasks = tasks.Select(task => MapTask(task, instance)).ToList(),
        };
    }

    private static WorkflowTaskDto MapTask(WorkflowTask task, WorkflowInstance? instance) =>
        new()
        {
            Id = task.Id,
            InstanceId = task.InstanceId,
            WorkflowCode = instance?.WorkflowCode ?? "",
            FormRef = instance?.FormRef,
            NodeId = task.NodeId,
            NodeName = task.NodeName,
            Status = task.Status,
            AssigneeUserName = task.AssigneeUserName,
            CandidateRole = task.CandidateRole,
            Opinion = task.Opinion,
        };

    private static WfGraph LoadGraph(string json)
    {
        var graph = new WfGraph();
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        }
        catch
        {
            throw new UserFriendlyException("流程定义无法解析");
        }

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("nodes", out var nodesEl)
            && nodesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in nodesEl.EnumerateArray())
            {
                var id = ReadJsonString(item, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                WfProcessNode node;
                if (item.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    node = JsonSerializer.Deserialize<WfProcessNode>(data.GetRawText(), JsonOpts) ?? new WfProcessNode();
                }
                else
                {
                    node = new WfProcessNode();
                }

                node.NodeId = id;
                if (string.IsNullOrWhiteSpace(node.Type))
                {
                    node.Type = ReadJsonString(item, "type");
                }

                graph.Nodes[id] = node;
            }

            if (root.TryGetProperty("edges", out var edgesEl) && edgesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in edgesEl.EnumerateArray())
                {
                    var source = ReadJsonString(edge, "source");
                    var target = ReadJsonString(edge, "target");
                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) continue;
                    string? field = null;
                    string? op = null;
                    string? value = null;
                    List<WfConditionItem>? items = null;
                    string? combine = null;
                    if (edge.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        field = ReadJsonString(data, "conditionField");
                        op = ReadJsonString(data, "conditionOp");
                        value = ReadJsonString(data, "conditionValue");
                        combine = ReadJsonString(data, "combine");
                        if (data.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                        {
                            items = JsonSerializer.Deserialize<List<WfConditionItem>>(itemsEl.GetRawText(), JsonOpts);
                        }
                    }

                    graph.Link(source, target, field, op, value, items, combine);
                }
            }

            return graph;
        }

        var tree = JsonSerializer.Deserialize<WfProcessNode>(json, JsonOpts)
            ?? throw new UserFriendlyException("流程定义无法解析");
        FlattenTree(graph, tree);
        return graph;
    }

    private static void FlattenTree(WfGraph graph, WfProcessNode? node)
    {
        while (node != null)
        {
            if (!string.IsNullOrWhiteSpace(node.NodeId))
            {
                graph.Nodes[node.NodeId] = node;
                if (node.ChildNode != null && !string.IsNullOrWhiteSpace(node.ChildNode.NodeId))
                {
                    graph.Link(node.NodeId, node.ChildNode.NodeId, null, null, null);
                }
            }

            node = node.ChildNode;
        }
    }

    private static string ReadJsonString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return "";
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static string ReadField(string recordJson, string field) => ReadPath(recordJson, field);

    private static string ReadPath(string json, string? path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        JsonElement current;
        try
        {
            current = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch
        {
            return string.IsNullOrWhiteSpace(path) ? json : "";
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : current.ToString();
        }

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                var found = false;
                foreach (var prop in current.EnumerateObject())
                {
                    if (string.Equals(prop.Name, part, StringComparison.OrdinalIgnoreCase))
                    {
                        current = prop.Value;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return "";
                }

                continue;
            }

            if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index) && index >= 0)
            {
                var cursor = 0;
                var found = false;
                foreach (var item in current.EnumerateArray())
                {
                    if (cursor == index)
                    {
                        current = item;
                        found = true;
                        break;
                    }

                    cursor++;
                }

                if (!found)
                {
                    return "";
                }

                continue;
            }

            return "";
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : current.ToString();
    }

    private static List<string> ReadUsers(string recordJson, string field)
    {
        var text = ReadField(recordJson, field);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (text.TrimStart().StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(text) ?? [];
            }
            catch
            {
                /* 当作单个名字 */
            }
        }

        return [text];
    }

    private static List<string> ReadUsersFromFlow(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return [];
        }

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(dataJson);
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.EnumerateArray().Select(item => item.ToString().Trim('"')).Where(x => x.Length > 0).ToList();
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
            {
                return users.EnumerateArray().Select(item => item.GetString() ?? item.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }
        }
        catch
        {
            /* 没有候选人 */
        }

        return [];
    }

    private sealed record AssigneePick(string? UserName, string? Role);

    public sealed class WfValueRef
    {
        public string? Source { get; set; }
        public string? Path { get; set; }
        public string? FlowKey { get; set; }
        public string? Value { get; set; }
    }

    public sealed class WfConditionItem
    {
        public int No { get; set; }
        public WfValueRef? Left { get; set; }
        public string? Op { get; set; }
        public WfValueRef? Right { get; set; }
    }

    public sealed class WfConditionBlock
    {
        public List<WfConditionItem> Items { get; set; } = [];
        public string? Combine { get; set; }
    }

    private sealed class WfEdge
    {
        public string TargetId { get; set; } = "";
        public string? ConditionField { get; set; }
        public string? ConditionOp { get; set; }
        public string? ConditionValue { get; set; }
        public List<WfConditionItem> Items { get; set; } = [];
        public string? Combine { get; set; }
    }

    private sealed class WfGraph
    {
        public Dictionary<string, WfProcessNode> Nodes { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<WfEdge>> Outgoing { get; } = new(StringComparer.Ordinal);

        public void Link(
            string source,
            string target,
            string? field,
            string? op,
            string? value,
            List<WfConditionItem>? items = null,
            string? combine = null
        )
        {
            if (!Outgoing.TryGetValue(source, out var list))
            {
                list = [];
                Outgoing[source] = list;
            }

            list.Add(new WfEdge
            {
                TargetId = target,
                ConditionField = field,
                ConditionOp = op,
                ConditionValue = value,
                Items = items ?? [],
                Combine = combine,
            });
        }
    }

    public sealed class WfProcessNode
    {
        public string NodeId { get; set; } = "";
        public string Type { get; set; } = "";
        public string Name { get; set; } = "";
        public string? BeforeFlowKey { get; set; }
        public string? AfterFlowKey { get; set; }
        public string? Opinion { get; set; }
        public string? AssigneeType { get; set; }
        public string? AssigneeValue { get; set; }
        public string? AssigneeFlowKey { get; set; }
        public string? Multi { get; set; }
        public int? MultiRatio { get; set; }
        public string? ConditionField { get; set; }
        public string? ConditionOp { get; set; }
        public string? ConditionValue { get; set; }
        public WfConditionBlock? LeaveCondition { get; set; }
        public WfProcessNode? ChildNode { get; set; }
    }
}
