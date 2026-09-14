namespace Meta.Dow.DataPermission;

public class DataScopeResult
{
    public DataScope Scope { get; set; } = DataScope.Self;

    public Guid? UserId { get; set; }

    /// <summary>
    /// 部门 / 自定义组织 ID 集合（含下级展开后的结果）。
    /// </summary>
    public List<Guid> OrganizationIds { get; set; } = [];

    public static DataScopeResult Deny(Guid? userId) =>
        new()
        {
            Scope = DataScope.Self,
            UserId = userId,
            OrganizationIds = [],
        };

    public static DataScopeResult AllowAll(Guid? userId) =>
        new()
        {
            Scope = DataScope.All,
            UserId = userId,
            OrganizationIds = [],
        };
}
