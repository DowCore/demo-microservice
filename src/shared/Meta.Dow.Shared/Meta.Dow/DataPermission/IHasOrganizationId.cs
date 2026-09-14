namespace Meta.Dow.DataPermission;

/// <summary>
/// 实体归属组织，用于行级数据权限过滤。
/// </summary>
public interface IHasOrganizationId
{
    Guid? OrganizationId { get; }
}
