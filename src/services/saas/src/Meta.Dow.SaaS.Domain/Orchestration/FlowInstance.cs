using System;
using System.Collections.Generic;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowInstance : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid DefinitionId { get; protected set; }

    public string DefinitionName { get; protected set; } = null!;

    public int Version { get; protected set; }

    public FlowInstanceStatus Status { get; protected set; }

    public string? VariablesJson { get; protected set; }

    public string? Error { get; protected set; }

    public bool IsDryRun { get; protected set; }

    /// <summary>触发来源：Http / Message:{code} / Schedule:{code}</summary>
    public string? TriggerSource { get; protected set; }

    public List<NodeExecutionRecord> Nodes { get; protected set; } = [];

    protected FlowInstance()
    {
    }

    public FlowInstance(
        Guid id,
        Guid definitionId,
        string definitionName,
        int version,
        bool isDryRun,
        string? variablesJson,
        Guid? tenantId = null,
        string? triggerSource = null
    )
        : base(id)
    {
        DefinitionId = definitionId;
        DefinitionName = definitionName;
        Version = version;
        IsDryRun = isDryRun;
        VariablesJson = variablesJson;
        TenantId = tenantId;
        TriggerSource = triggerSource;
        Status = FlowInstanceStatus.Running;
    }

    public void AddNode(NodeExecutionRecord record)
    {
        Nodes.Add(record);
    }

    public void Succeed(string? variablesJson)
    {
        Status = FlowInstanceStatus.Succeeded;
        VariablesJson = variablesJson;
        Error = null;
    }

    public void Fail(string error, string? variablesJson = null)
    {
        Status = FlowInstanceStatus.Failed;
        Error = error;
        if (variablesJson != null)
        {
            VariablesJson = variablesJson;
        }
    }

    public void Cancel()
    {
        if (Status == FlowInstanceStatus.Running)
        {
            Status = FlowInstanceStatus.Cancelled;
        }
    }
}
