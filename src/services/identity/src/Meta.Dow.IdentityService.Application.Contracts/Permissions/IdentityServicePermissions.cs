using Volo.Abp.Reflection;

namespace Meta.Dow.IdentityService.Permissions;

/// <summary>
/// 对齐 ABP Commercial Identity Pro 的组织机构权限名，供菜单与按钮使用。
/// </summary>
public static class IdentityServicePermissions
{
    public static class OrganizationUnits
    {
        public const string Default = "AbpIdentity.OrganizationUnits";
        public const string ManageOU = Default + ".ManageOU";
        public const string ManageRoles = Default + ".ManageRoles";
        public const string ManageMembers = Default + ".ManageMembers";
    }

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(IdentityServicePermissions));
    }
}
