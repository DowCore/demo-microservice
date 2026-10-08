using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 接收节点上的一条待办。抄送不阻挡流程。
/// </summary>
public class WorkflowTask : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid InstanceId { get; protected set; }

    public string NodeId { get; protected set; } = null!;

    public string NodeName { get; protected set; } = null!;

    /// <summary>approve / cc</summary>
    public string Kind { get; protected set; } = "approve";

    /// <summary>pending / waiting / approved / rejected / cancelled / notified</summary>
    public string Status { get; protected set; } = "pending";

    public string? AssigneeUserName { get; protected set; }

    public string? CandidateRole { get; protected set; }

    public int Sequence { get; protected set; }

    public string? Opinion { get; protected set; }

    protected WorkflowTask()
    {
    }

    public WorkflowTask(
        Guid id,
        Guid instanceId,
        string nodeId,
        string nodeName,
        string kind,
        string status,
        string? assigneeUserName,
        string? candidateRole,
        int sequence,
        Guid? tenantId
    )
        : base(id)
    {
        InstanceId = instanceId;
        NodeId = nodeId;
        NodeName = nodeName;
        Kind = kind;
        Status = status;
        AssigneeUserName = assigneeUserName;
        CandidateRole = candidateRole;
        Sequence = sequence;
        TenantId = tenantId;
    }

    public void Approve(string? opinion)
    {
        Status = "approved";
        Opinion = opinion;
    }

    public void Reject(string? opinion)
    {
        Status = "rejected";
        Opinion = opinion;
    }

    public void Cancel() => Status = "cancelled";

    public void Transfer(string targetUserName, string? opinion)
    {
        Status = "transferred";
        Opinion = opinion;
    }

    public void Activate() => Status = "pending";

    public bool Matches(string userName, string[] roles)
    {
        if (!string.IsNullOrWhiteSpace(AssigneeUserName))
        {
            return string.Equals(AssigneeUserName, userName, StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(CandidateRole))
        {
            return false;
        }

        foreach (var role in roles)
        {
            if (string.Equals(role, CandidateRole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
