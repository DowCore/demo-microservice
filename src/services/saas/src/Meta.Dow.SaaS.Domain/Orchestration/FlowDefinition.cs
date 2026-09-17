using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Name { get; protected set; } = null!;

    public string Code { get; protected set; } = null!;

    public string? Category { get; protected set; }

    public FlowDefinitionStatus Status { get; protected set; }

    public string? GraphJson { get; protected set; }

    public string? DslJson { get; protected set; }

    public int? PublishedVersion { get; protected set; }

    protected FlowDefinition()
    {
    }

    public FlowDefinition(
        Guid id,
        string name,
        string code,
        string? category = null,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetName(name);
        SetCode(code);
        Category = category;
        TenantId = tenantId;
        Status = FlowDefinitionStatus.Draft;
    }

    public void UpdateDraft(string name, string? category, string? graphJson, string? dslJson)
    {
        SetName(name);
        Category = category;
        GraphJson = graphJson;
        DslJson = dslJson;
    }

    public void MarkPublished(int version)
    {
        PublishedVersion = version;
        Status = FlowDefinitionStatus.Published;
    }

    public void Disable()
    {
        Status = FlowDefinitionStatus.Disabled;
    }

    public void EnableAsDraft()
    {
        if (Status == FlowDefinitionStatus.Disabled)
        {
            Status = PublishedVersion.HasValue
                ? FlowDefinitionStatus.Published
                : FlowDefinitionStatus.Draft;
        }
    }

    private void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrchestrationConsts.MaxNameLength);
    }

    private void SetCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), OrchestrationConsts.MaxCodeLength);
    }
}
