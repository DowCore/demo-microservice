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
    private readonly IRepository<FormDefinition, Guid> _forms;
    private readonly IPublishedFlowInvoker _flows;
    private readonly IWorkflowOrgResolver _orgResolver;
    private readonly IWorkflowNotifier _notifier;

    public WorkflowRuntimeAppService(
        IRepository<WorkflowDefinition, Guid> definitions,
        IRepository<WorkflowInstance, Guid> instances,
        IRepository<WorkflowTask, Guid> tasks,
        IRepository<FormDefinition, Guid> forms,
        IPublishedFlowInvoker flows,
        IWorkflowOrgResolver orgResolver,
        IWorkflowNotifier notifier
    )
    {
        _definitions = definitions;
        _instances = instances;
        _tasks = tasks;
        _forms = forms;
        _flows = flows;
        _orgResolver = orgResolver;
        _notifier = notifier;
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

        // W9/F4：记录启动时对应的定义版本与表单版本，使「这一单跑的是第几版」可回答。
        var formSchemaVersion = 0;
        if (!string.IsNullOrWhiteSpace(definition.FormRef))
        {
            var form = await _forms.FirstOrDefaultAsync(x => x.Code == definition.FormRef);
            formSchemaVersion = form?.SchemaVersion ?? 0;
        }

        var instance = new WorkflowInstance(
            GuidGenerator.Create(),
            definition.Code,
            definition.FormRef,
            recordJson,
            user,
            CurrentTenant.Id,
            definition.ProcessJson,
            definition.Version,
            formSchemaVersion
        );
        AppendHistoryRecord(instance, "start", "开始", user, "start", "发起申请");
        await _instances.InsertAsync(instance, autoSave: true);

        var graph = LoadGraph(instance.ProcessSnapshotJson);
        var start = graph.Nodes.Values.FirstOrDefault(n =>
            string.Equals(n.Type, "start", StringComparison.OrdinalIgnoreCase))
            ?? throw new UserFriendlyException("流程里没有开始节点");
        await EnterAsync(instance, graph, start);
        if (instance.Status != "running")
        {
            await FinishAsync(instance, definition);
        }

        return await MapInstanceAsync(instance, graph);
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
            mine.Select(task =>
            {
                instances.TryGetValue(task.InstanceId, out var instance);
                return MapTask(task, instance, GraphOf(instance));
            }).ToList()
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
        var graphJson = !string.IsNullOrWhiteSpace(instance.ProcessSnapshotJson) && instance.ProcessSnapshotJson != "{}"
            ? instance.ProcessSnapshotJson
            : definition.ProcessJson;
        var graph = LoadGraph(graphJson);
        var node = graph.Nodes.GetValueOrDefault(task.NodeId)
            ?? throw new UserFriendlyException("流程定义里已经没有这个节点");

        var action = (input.Action ?? (input.Pass ? "approve" : "reject_terminate")).Trim().ToLowerInvariant();

        // 1. 如果提交了表单补丁数据，增量合并到流程实例表单中
        if (input.RecordPatch != null && input.RecordPatch.Count > 0)
        {
            var patch = FilterRecordPatch(node.FieldPermissions, input.RecordPatch);
            if (patch.Count > 0)
            {
                instance.UpdateRecordJson(MergeRecordPatch(instance.RecordJson, patch));
            }
        }

        // 2. 转办
        if (action == "transfer")
        {
            if (string.IsNullOrWhiteSpace(input.TransferUserName))
            {
                throw new UserFriendlyException("转办必须指定接手人");
            }

            task.Transfer(input.TransferUserName.Trim(), input.Opinion);
            await _tasks.UpdateAsync(task, autoSave: true);

            var newTask = new WorkflowTask(
                GuidGenerator.Create(),
                instance.Id,
                task.NodeId,
                task.NodeName,
                "approve",
                "pending",
                input.TransferUserName.Trim(),
                null,
                task.Sequence,
                CurrentTenant.Id
            );
            await _tasks.InsertAsync(newTask, autoSave: true);
            AppendHistoryRecord(instance, task.NodeId, task.NodeName, user, "transfer", $"转办给 {input.TransferUserName}: {input.Opinion}");
            await _instances.UpdateAsync(instance, autoSave: true);
            return await MapInstanceAsync(instance, graph);
        }

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

        // 3. 驳回给发起人修改
        if (action == "reject_to_starter")
        {
            task.Reject(input.Opinion);
            await _tasks.UpdateAsync(task, autoSave: true);
            await CancelOpenAsync(siblings);
            AppendHistoryRecord(instance, task.NodeId, task.NodeName, user, "reject_to_starter", input.Opinion);

            instance.Reopen("start");
            var starterTask = new WorkflowTask(
                GuidGenerator.Create(),
                instance.Id,
                "start",
                "发起人重新提交",
                "approve",
                "pending",
                instance.StarterUserName,
                null,
                0,
                CurrentTenant.Id
            );
            await _tasks.InsertAsync(starterTask, autoSave: true);
            await _instances.UpdateAsync(instance, autoSave: true);
            return await MapInstanceAsync(instance, graph);
        }

        // 4. 驳回到上一审批节点
        if (action == "reject_to_prev")
        {
            task.Reject(input.Opinion);
            await _tasks.UpdateAsync(task, autoSave: true);
            await CancelOpenAsync(siblings);

            var targetNodeId = input.TargetNodeId;
            if (string.IsNullOrWhiteSpace(targetNodeId))
            {
                targetNodeId = FindPreviousNodeFromHistory(instance.HistoryJson, task.NodeId);
            }

            AppendHistoryRecord(instance, task.NodeId, task.NodeName, user, "reject_to_prev", input.Opinion);

            if (!string.IsNullOrWhiteSpace(targetNodeId) && graph.Nodes.TryGetValue(targetNodeId, out var prevNode))
            {
                instance.Reopen(prevNode.NodeId);
                await CreateTasksAsync(instance, prevNode, notifyOnly: false);
                await _instances.UpdateAsync(instance, autoSave: true);
                return await MapInstanceAsync(instance, graph);
            }

            // 若找不到前置审批节点，兜底退回给发起人
            instance.Reopen("start");
            var fallbackStarterTask = new WorkflowTask(
                GuidGenerator.Create(),
                instance.Id,
                "start",
                "发起人重新提交",
                "approve",
                "pending",
                instance.StarterUserName,
                null,
                0,
                CurrentTenant.Id
            );
            await _tasks.InsertAsync(fallbackStarterTask, autoSave: true);
            await _instances.UpdateAsync(instance, autoSave: true);
            return await MapInstanceAsync(instance, graph);
        }

        // 5. 彻底作废终止
        if (action == "reject_terminate" || !input.Pass)
        {
            if (ShouldRejectNode(node, siblings))
            {
                task.Reject(input.Opinion);
                await _tasks.UpdateAsync(task, autoSave: true);
                await CancelOpenAsync(siblings);
                await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, input.Opinion, false);
                AppendHistoryRecord(instance, node.NodeId, node.Name, user, "reject_terminate", input.Opinion);
                instance.Reject("rejected");
                await FinishAsync(instance, definition);
                return await MapInstanceAsync(instance, graph);
            }
        }

        // 6. 正常同意审批
        if (string.Equals(node.Opinion, "required", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(input.Opinion))
        {
            throw new UserFriendlyException("这个节点必须填写处理意见");
        }

        // W8：本节点标记为 required 的字段，在通过前必须已有值。
        EnforceRequiredFields(node.FieldPermissions, instance.RecordJson);

        task.Approve(input.Opinion);
        await _tasks.UpdateAsync(task, autoSave: true);
        AppendHistoryRecord(instance, node.NodeId, node.Name, user, "approve", input.Opinion);

        if (string.Equals(node.Multi, "sequential", StringComparison.OrdinalIgnoreCase))
        {
            var next = siblings
                .Where(x => x.Status == "waiting")
                .OrderBy(x => x.Sequence)
                .FirstOrDefault();
            if (next != null && !NodePassed(node, siblings))
            {
                next.Activate();
                await _tasks.UpdateAsync(next, autoSave: true);
                return await MapInstanceAsync(instance, graph);
            }
        }

        if (!NodePassed(node, siblings))
        {
            return await MapInstanceAsync(instance, graph);
        }

        await CancelOpenAsync(siblings);
        await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, input.Opinion, true);
        if (!await ConditionPasses(node, instance))
        {
            instance.Complete("condition-rejected");
            await FinishAsync(instance, definition);
            return await MapInstanceAsync(instance, graph);
        }

        var (following, missed) = await ChooseNext(graph, node, instance);
        if (missed)
        {
            instance.Complete("condition-rejected");
            await FinishAsync(instance, definition);
            return await MapInstanceAsync(instance, graph);
        }

        await EnterAsync(instance, graph, following);
        if (instance.Status != "running")
        {
            await FinishAsync(instance, definition);
        }

        return await MapInstanceAsync(instance, graph);
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
                || string.Equals(node.Type, "condition", StringComparison.OrdinalIgnoreCase))
            {
                await InvokeFlowAsync(node.BeforeFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
                await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);

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

            if (string.Equals(node.Type, "cc", StringComparison.OrdinalIgnoreCase))
            {
                await InvokeFlowAsync(node.BeforeFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
                var recipients = await CreateTasksAsync(instance, node, notifyOnly: true);
                // 抄送不再是"只记不送"：经消息通道广播通知，失败只告警、不阻断流程。
                await _notifier.NotifyCcAsync(
                    instance.Id,
                    instance.WorkflowCode,
                    instance.FormRef,
                    node.NodeId,
                    string.IsNullOrWhiteSpace(node.Name) ? node.NodeId : node.Name,
                    instance.RecordJson,
                    recipients.Where(p => !string.IsNullOrWhiteSpace(p.UserName)).Select(p => p.UserName!).ToList(),
                    recipients.Where(p => !string.IsNullOrWhiteSpace(p.Role)).Select(p => p.Role!).ToList(),
                    CurrentTenant.Id
                );
                await InvokeFlowAsync(node.AfterFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);

                var (ccNext, ccMissed) = await ChooseNext(graph, node, instance);
                if (ccMissed)
                {
                    instance.Complete("condition-rejected");
                    await _instances.UpdateAsync(instance, autoSave: true);
                    return;
                }

                node = ccNext;
                continue;
            }

            if (string.Equals(node.Type, "approver", StringComparison.OrdinalIgnoreCase))
            {
                await InvokeFlowAsync(node.BeforeFlowKey, instance.FormRef, instance.RecordJson, node.NodeId, null, true);
                await CreateTasksAsync(instance, node, notifyOnly: false);
                instance.MoveTo(node.NodeId);
                await _instances.UpdateAsync(instance, autoSave: true);
                return;
            }

            // 未知节点类型：不再按审批节点兜底执行，显式报错，避免配置错误被静默吞掉。
            throw new UserFriendlyException($"流程节点「{node.NodeId}」的类型「{node.Type}」不支持");
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
                return (RequireTargetNode(graph, node, only.TargetId), false);
            }

            return (null, true);
        }

        // W6：多条件出边时按显式优先级（数字小者优先）取第一个命中；同优先级按画布连线顺序兜底。
        // 不再靠「数组顺序」这一隐式规则——拖动连线顺序会静默改变审批走向。
        foreach (var edge in edges.Where(EdgeHasCondition).OrderBy(e => e.Priority))
        {
            if (await EdgeMatchesAsync(edge, instance, node.NodeId, cache))
            {
                return (RequireTargetNode(graph, node, edge.TargetId), false);
            }
        }

        var fallback = edges.FirstOrDefault(edge => edge.IsDefault)
            ?? edges.FirstOrDefault(edge => !EdgeHasCondition(edge));
        if (fallback != null)
        {
            return (RequireTargetNode(graph, node, fallback.TargetId), false);
        }

        return (null, true);
    }

    /// <summary>
    /// 取连线目标节点；目标不存在时显式报错（失败关闭）。
    /// 早前实现返回 null，会让 EnterAsync 直接 Complete("approved")——即打错目标的连线导致单据被静默通过。
    /// </summary>
    private static WfProcessNode RequireTargetNode(WfGraph graph, WfProcessNode node, string targetId)
    {
        if (graph.Nodes.TryGetValue(targetId, out var target) && target != null)
        {
            return target;
        }

        throw new UserFriendlyException($"节点「{node.NodeId}」的连线指向不存在的节点「{targetId}」");
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

    private async Task<List<AssigneePick>> CreateTasksAsync(WorkflowInstance instance, WfProcessNode node, bool notifyOnly)
    {
        var people = await ResolveAssigneesAsync(node, instance);

        // §6.6 审批人去重：同一节点同一人（或同一角色）只保留一条待办。
        people = people
            .DistinctBy(p => (p.UserName?.ToLowerInvariant(), p.Role?.ToLowerInvariant()))
            .ToList();

        if (people.Count == 0)
        {
            // 抄送不挡流程：没有抄送对象就直接跳过，不套用 EmptyFallback。
            if (notifyOnly)
            {
                return [];
            }

            // W7 失败关闭：默认不再静默派给 admin，宁可阻断并要求配置者修流程。
            var fallback = (node.EmptyFallback ?? "error").Trim().ToLowerInvariant();
            if (fallback == "skip")
            {
                return [];
            }

            if (fallback == "admin")
            {
                people = [new AssigneePick("admin", null)];
            }
            else
            {
                throw new UserFriendlyException($"节点「{(string.IsNullOrWhiteSpace(node.Name) ? node.NodeId : node.Name)}」没有办理人");
            }
        }

        if (!notifyOnly)
        {
            // 驳回重办等场景：同一节点上已有「未办结」待办的人不再重复建单（已办结的允许重新建）。
            var existingUsers = (await _tasks.GetQueryableAsync())
                .Where(x => x.InstanceId == instance.Id && x.NodeId == node.NodeId && x.Kind == "approve" && (x.Status == "pending" || x.Status == "waiting"))
                .ToList()
                .Select(x => x.AssigneeUserName ?? "")
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            people = people
                .Where(p => string.IsNullOrWhiteSpace(p.UserName) || !existingUsers.Contains(p.UserName!))
                .ToList();
            if (people.Count == 0)
            {
                return [];
            }
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

        return people;
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
            case "role":
                // 一个名额：角色内任一人可办。角色码由 Matches(user, roles) 在取待办时判定。
                return string.IsNullOrWhiteSpace(value) ? [] : [new AssigneePick(null, value)];
            case "manager":
                // §6.1 接组织树：解析发起人直属部门主管（角色码可留空，默认 manager）。
                var manager = await _orgResolver.ResolveDirectManagerAsync(instance.StarterUserName, value);
                return string.IsNullOrWhiteSpace(manager) ? [] : [new AssigneePick(manager, null)];
            default:
                throw new UserFriendlyException($"节点「{(string.IsNullOrWhiteSpace(node.Name) ? node.NodeId : node.Name)}」的办理人规则「{type}」不支持");
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

    private async Task<WorkflowInstanceDto> MapInstanceAsync(WorkflowInstance instance, WfGraph? graph = null)
    {
        graph ??= LoadGraph(
            !string.IsNullOrWhiteSpace(instance.ProcessSnapshotJson) && instance.ProcessSnapshotJson != "{}"
                ? instance.ProcessSnapshotJson
                : (await _definitions.FirstOrDefaultAsync(x => x.Code == instance.WorkflowCode))?.ProcessJson ?? "{}",
            strict: false
        );
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
            DefinitionVersion = instance.DefinitionVersion,
            FormSchemaVersion = instance.FormSchemaVersion,
            ProcessSnapshotJson = instance.ProcessSnapshotJson,
            HistoryJson = instance.HistoryJson,
            Tasks = tasks.Select(task => MapTask(task, instance, graph)).ToList(),
        };
    }

    private static WorkflowTaskDto MapTask(WorkflowTask task, WorkflowInstance? instance, WfGraph? graph = null)
    {
        var fieldPerms = new Dictionary<string, string>();
        if (graph != null && graph.Nodes.TryGetValue(task.NodeId, out var node) && node.FieldPermissions != null)
        {
            fieldPerms = node.FieldPermissions;
        }

        return new WorkflowTaskDto
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
            FieldPermissions = fieldPerms,
            RecordJson = instance?.RecordJson ?? "{}",
            HistoryJson = instance?.HistoryJson ?? "[]",
        };
    }

    private static WfGraph? GraphOf(WorkflowInstance? instance)
    {
        var snapshot = instance?.ProcessSnapshotJson;
        if (string.IsNullOrWhiteSpace(snapshot) || snapshot == "{}")
        {
            return null;
        }

        try
        {
            return LoadGraph(snapshot, strict: false);
        }
        catch (UserFriendlyException)
        {
            return null;
        }
    }

    /// <summary>
    /// 按节点字段权限过滤办理人提交的补丁。
    /// read/hide 拒绝写入；write/required 放行；未知权限值显式报错（失败关闭，而不是静默放行）。
    /// 未配置权限的字段按表单本身的可写性，由前端决定是否提交。
    /// </summary>
    internal static Dictionary<string, object?> FilterRecordPatch(
        Dictionary<string, string>? permissions,
        Dictionary<string, object?> patch
    )
    {
        var allowed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in patch)
        {
            if (permissions != null && permissions.TryGetValue(pair.Key, out var raw))
            {
                var permission = (raw ?? string.Empty).Trim().ToLowerInvariant();
                switch (permission)
                {
                    case "":
                    case "write":
                    case "required":
                        allowed[pair.Key] = pair.Value;
                        break;
                    case "read":
                    case "hide":
                        // 只读 / 隐藏：忽略该字段的补丁。
                        break;
                    default:
                        throw new UserFriendlyException($"表单字段「{pair.Key}」的权限值「{raw}」无效（仅支持 read/write/hide/required）");
                }
            }
            else
            {
                allowed[pair.Key] = pair.Value;
            }
        }

        return allowed;
    }

    /// <summary>本节点标记为 required 的字段，提交时必须已有非空值。</summary>
    private static void EnforceRequiredFields(Dictionary<string, string>? permissions, string recordJson)
    {
        if (permissions is not { Count: > 0 })
        {
            return;
        }

        foreach (var pair in permissions)
        {
            if (!string.Equals(pair.Value?.Trim(), "required", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(ReadPath(recordJson, pair.Key)))
            {
                throw new UserFriendlyException($"表单字段「{pair.Key}」在本节点必填，请填写后再提交");
            }
        }
    }

    private static void AppendHistoryRecord(
        WorkflowInstance instance,
        string nodeId,
        string nodeName,
        string operatorUserName,
        string action,
        string? opinion
    )
    {
        var entry = JsonSerializer.Serialize(new
        {
            nodeId,
            nodeName,
            operatorUserName,
            action,
            opinion,
            time = DateTime.UtcNow
        });
        instance.AppendHistory(entry);
    }

    private static string? FindPreviousNodeFromHistory(string historyJson, string currentNodeId)
    {
        if (string.IsNullOrWhiteSpace(historyJson) || historyJson == "[]") return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<JsonElement>>(historyJson);
            if (list == null || list.Count == 0) return null;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].TryGetProperty("nodeId", out var nid))
                {
                    var id = nid.GetString();
                    if (!string.IsNullOrWhiteSpace(id)
                        && !string.Equals(id, currentNodeId, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(id, "start", StringComparison.OrdinalIgnoreCase))
                    {
                        return id;
                    }
                }
            }
        }
        catch
        {
            /* 忽略解析异常 */
        }
        return null;
    }

    private static string MergeRecordPatch(string recordJson, Dictionary<string, object?> patch)
    {
        if (patch == null || patch.Count == 0) return recordJson;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                string.IsNullOrWhiteSpace(recordJson) ? "{}" : recordJson
            ) ?? [];
            foreach (var kvp in patch)
            {
                dict[kvp.Key] = kvp.Value;
            }
            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return recordJson;
        }
    }

    /// <summary>
    /// 解析流程图。strict=true 时对坏数据（空 id / 重复 id / 端点缺失 / 环）显式报错（W5），
    /// 而不是静默覆盖或跳过——否则一个写坏的流程会「解析成功」但节点少了一半。
    /// 仅用于历史快照的展示场景才传 strict:false。
    /// </summary>
    private static WfGraph LoadGraph(string json, bool strict = true)
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
                if (string.IsNullOrWhiteSpace(id))
                {
                    if (strict)
                    {
                        throw new UserFriendlyException("流程定义里存在缺少 id 的节点");
                    }

                    continue;
                }

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

                if (graph.Nodes.ContainsKey(id))
                {
                    if (strict)
                    {
                        throw new UserFriendlyException($"流程定义里存在重复的节点 id「{id}」");
                    }

                    graph.Nodes[id] = node;
                    continue;
                }

                graph.Nodes[id] = node;
            }

            if (root.TryGetProperty("edges", out var edgesEl) && edgesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in edgesEl.EnumerateArray())
                {
                    var source = ReadJsonString(edge, "source");
                    var target = ReadJsonString(edge, "target");
                    if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                    {
                        if (strict)
                        {
                            throw new UserFriendlyException("流程定义里存在缺少起点或终点的连线");
                        }

                        continue;
                    }

                    string? field = null;
                    string? op = null;
                    string? value = null;
                    List<WfConditionItem>? items = null;
                    string? combine = null;
                    var priority = 0;
                    var isDefault = false;
                    if (edge.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                    {
                        field = ReadJsonString(data, "conditionField");
                        op = ReadJsonString(data, "conditionOp");
                        value = ReadJsonString(data, "conditionValue");
                        combine = ReadJsonString(data, "combine");
                        priority = ReadJsonInt(data, "priority");
                        isDefault = ReadJsonBool(data, "isDefault");
                        if (data.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                        {
                            items = JsonSerializer.Deserialize<List<WfConditionItem>>(itemsEl.GetRawText(), JsonOpts);
                        }
                    }

                    graph.Link(source, target, field, op, value, items, combine, priority, isDefault);
                }
            }

            return graph;
        }

        var tree = JsonSerializer.Deserialize<WfProcessNode>(json, JsonOpts)
            ?? throw new UserFriendlyException("流程定义无法解析");
        FlattenTree(graph, tree, strict);
        return graph;
    }

    private static void FlattenTree(WfGraph graph, WfProcessNode? node, bool strict = true)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (node != null)
        {
            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                if (strict)
                {
                    throw new UserFriendlyException("流程定义里存在缺少 nodeId 的节点");
                }

                break;
            }

            if (!visited.Add(node.NodeId))
            {
                if (strict)
                {
                    throw new UserFriendlyException($"流程定义里存在环：节点「{node.NodeId}」被重复经过");
                }

                break;
            }

            graph.Nodes[node.NodeId] = node;
            if (node.ChildNode != null && !string.IsNullOrWhiteSpace(node.ChildNode.NodeId))
            {
                graph.Link(node.NodeId, node.ChildNode.NodeId, null, null, null);
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

    private static int ReadJsonInt(JsonElement element, string name)
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

    private static bool ReadJsonBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var b) && b,
            _ => false,
        };
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

        /// <summary>W6：条件分支优先级，数字小者优先。同一出节点的条件边应配置互不相同的优先级。</summary>
        public int Priority { get; set; }

        /// <summary>W6：显式默认分支（无条件兜底）。</summary>
        public bool IsDefault { get; set; }
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
            string? combine = null,
            int priority = 0,
            bool isDefault = false
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
                Priority = priority,
                IsDefault = isDefault,
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
        public Dictionary<string, string> FieldPermissions { get; set; } = [];

        /// <summary>候选人为空时的策略：admin（转管理员）/ skip（跳过）/ error（报错阻断）。默认 error，失败关闭。</summary>
        public string EmptyFallback { get; set; } = "error";
    }
}
