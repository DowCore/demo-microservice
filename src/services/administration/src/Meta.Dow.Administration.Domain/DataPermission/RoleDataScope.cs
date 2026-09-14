using Meta.Dow.Administration.DataPermission;
using Meta.Dow.DataPermission;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.Administration.DataPermission;

/// <summary>
/// 角色在某资源上的数据范围策略。
/// </summary>
public class RoleDataScope : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid RoleId { get; protected set; }

    /// <summary>
    /// 资源键，如 Projects.Issues；* 表示角色默认范围。
    /// </summary>
    public string Resource { get; protected set; } = DataPermissionConsts.DefaultResource;

    public DataScope Scope { get; protected set; }

    /// <summary>
    /// Custom 范围下勾选的组织 ID。
    /// </summary>
    public List<Guid> OrganizationIds { get; protected set; } = [];

    protected RoleDataScope() { }

    public RoleDataScope(
        Guid id,
        Guid roleId,
        string resource,
        DataScope scope,
        List<Guid>? organizationIds = null,
        Guid? tenantId = null
    )
        : base(id)
    {
        TenantId = tenantId;
        RoleId = roleId;
        SetResource(resource);
        SetScope(scope, organizationIds);
    }

    public void SetResource(string resource)
    {
        Resource = Check.NotNullOrWhiteSpace(resource, nameof(resource), DataPermissionConsts.MaxResourceLength);
    }

    public void SetScope(DataScope scope, List<Guid>? organizationIds = null)
    {
        Scope = scope;
        OrganizationIds = scope == DataScope.Custom ? organizationIds?.Distinct().ToList() ?? [] : [];
    }
}
