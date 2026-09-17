using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowVersion : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid DefinitionId { get; protected set; }

    public int Version { get; protected set; }

    public string GraphJson { get; protected set; } = null!;

    public string DslJson { get; protected set; } = null!;

    protected FlowVersion()
    {
    }

    public FlowVersion(
        Guid id,
        Guid definitionId,
        int version,
        string graphJson,
        string dslJson,
        Guid? tenantId = null
    )
        : base(id)
    {
        DefinitionId = definitionId;
        Version = version;
        GraphJson = Check.NotNull(graphJson, nameof(graphJson));
        DslJson = Check.NotNullOrWhiteSpace(dslJson, nameof(dslJson));
        TenantId = tenantId;
    }
}
