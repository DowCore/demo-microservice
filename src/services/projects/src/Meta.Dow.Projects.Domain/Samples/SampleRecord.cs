using Meta.Dow.DataPermission;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.Projects.Samples;

/// <summary>
/// 示例：业务实体同时实现组织与创建人接口，列表查询时调用
/// <c>query.ApplyDataPermission(await dataScopeResolver.ResolveAsync("Projects.Samples"))</c>。
/// </summary>
public class SampleRecord : FullAuditedAggregateRoot<Guid>, IMultiTenant, IHasOrganizationId, IHasCreatorId
{
    public Guid? TenantId { get; protected set; }

    public Guid? OrganizationId { get; protected set; }

    public string Title { get; protected set; } = null!;

    protected SampleRecord() { }

    public SampleRecord(Guid id, string title, Guid? organizationId = null, Guid? tenantId = null)
        : base(id)
    {
        Title = title;
        OrganizationId = organizationId;
        TenantId = tenantId;
    }
}
