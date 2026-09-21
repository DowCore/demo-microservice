using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 流程引用索引：谁在 DSL 里用 SubFlow 调用了哪个 flowKey。
/// 在保存草稿 / 发布时重建。
/// </summary>
public class FlowUsage : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid CallerDefinitionId { get; protected set; }

    public string CallerCode { get; protected set; } = null!;

    public string CallerName { get; protected set; } = null!;

    /// <summary>被调用的已发布逻辑编码（flowKey）</summary>
    public string CalleeFlowKey { get; protected set; } = null!;

    public string? NodeId { get; protected set; }

    public string? NodeRef { get; protected set; }

    protected FlowUsage()
    {
    }

    public FlowUsage(
        Guid id,
        Guid callerDefinitionId,
        string callerCode,
        string callerName,
        string calleeFlowKey,
        string? nodeId,
        string? nodeRef,
        Guid? tenantId = null
    )
        : base(id)
    {
        CallerDefinitionId = callerDefinitionId;
        CallerCode = Check.NotNullOrWhiteSpace(callerCode, nameof(callerCode), OrchestrationConsts.MaxCodeLength);
        CallerName = Check.NotNullOrWhiteSpace(callerName, nameof(callerName), OrchestrationConsts.MaxNameLength);
        CalleeFlowKey = Check.NotNullOrWhiteSpace(calleeFlowKey, nameof(calleeFlowKey), OrchestrationConsts.MaxCodeLength);
        NodeId = nodeId;
        NodeRef = nodeRef;
        TenantId = tenantId;
    }
}
