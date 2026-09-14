namespace Meta.Dow.DataPermission;

/// <summary>
/// 数据权限范围（角色级 / 资源级）。
/// </summary>
public enum DataScope
{
    /// <summary>仅本人创建的数据。</summary>
    Self = 0,

    /// <summary>本部门。</summary>
    Department = 1,

    /// <summary>本部门及下级。</summary>
    DepartmentAndChildren = 2,

    /// <summary>指定组织集合。</summary>
    Custom = 3,

    /// <summary>全部数据。</summary>
    All = 4,
}
