using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 一次 OA 审批。record 是绑定表单的那一份数据。
/// </summary>
public class WorkflowInstance : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string WorkflowCode { get; protected set; } = null!;

    public string? FormRef { get; protected set; }

    public string RecordJson { get; protected set; } = "{}";

    /// <summary>running / completed / rejected</summary>
    public string Status { get; protected set; } = "running";

    public string? CurrentNodeId { get; protected set; }

    public string? Result { get; protected set; }

    public string StarterUserName { get; protected set; } = null!;

    /// <summary>启动时的流程定义快照，隔离设计态变更</summary>
    public string ProcessSnapshotJson { get; protected set; } = "{}";

    /// <summary>审批流转轨迹记录数组 JSON</summary>
    public string HistoryJson { get; protected set; } = "[]";

    protected WorkflowInstance()
    {
    }

    public WorkflowInstance(
        Guid id,
        string workflowCode,
        string? formRef,
        string recordJson,
        string starterUserName,
        Guid? tenantId,
        string? processSnapshotJson = null
    )
        : base(id)
    {
        WorkflowCode = workflowCode;
        FormRef = formRef;
        RecordJson = string.IsNullOrWhiteSpace(recordJson) ? "{}" : recordJson;
        StarterUserName = starterUserName;
        TenantId = tenantId;
        Status = "running";
        ProcessSnapshotJson = string.IsNullOrWhiteSpace(processSnapshotJson) ? "{}" : processSnapshotJson;
        HistoryJson = "[]";
    }

    public void SetProcessSnapshot(string snapshotJson) =>
        ProcessSnapshotJson = string.IsNullOrWhiteSpace(snapshotJson) ? "{}" : snapshotJson;

    public void UpdateRecordJson(string recordJson) =>
        RecordJson = string.IsNullOrWhiteSpace(recordJson) ? "{}" : recordJson;

    public void AppendHistory(string entryJson)
    {
        if (string.IsNullOrWhiteSpace(entryJson)) return;
        var list = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<System.Text.Json.JsonElement>>(
            string.IsNullOrWhiteSpace(HistoryJson) ? "[]" : HistoryJson
        ) ?? [];
        using var doc = System.Text.Json.JsonDocument.Parse(entryJson);
        list.Add(doc.RootElement.Clone());
        HistoryJson = System.Text.Json.JsonSerializer.Serialize(list);
    }

    public void Reopen(string? nodeId)
    {
        Status = "running";
        Result = null;
        CurrentNodeId = nodeId;
    }

    public void MoveTo(string? nodeId) => CurrentNodeId = nodeId;

    public void Complete(string result)
    {
        Status = "completed";
        Result = result;
        CurrentNodeId = null;
    }

    public void Reject(string result)
    {
        Status = "rejected";
        Result = result;
        CurrentNodeId = null;
    }
}
