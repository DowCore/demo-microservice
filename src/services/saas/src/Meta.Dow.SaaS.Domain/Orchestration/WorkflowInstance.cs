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

    protected WorkflowInstance()
    {
    }

    public WorkflowInstance(
        Guid id,
        string workflowCode,
        string? formRef,
        string recordJson,
        string starterUserName,
        Guid? tenantId
    )
        : base(id)
    {
        WorkflowCode = workflowCode;
        FormRef = formRef;
        RecordJson = string.IsNullOrWhiteSpace(recordJson) ? "{}" : recordJson;
        StarterUserName = starterUserName;
        TenantId = tenantId;
        Status = "running";
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
