using System;
using Meta.Dow.IdentityService.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.Localization;

namespace Meta.Dow.IdentityService.Permissions;

public class IdentityServicePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.GetPermissionOrNull(IdentityServicePermissions.OrganizationUnits.Default) != null)
        {
            return;
        }

        var identityGroup = context.GetGroup(IdentityPermissions.GroupName);

        var ou = identityGroup.AddPermission(
            IdentityServicePermissions.OrganizationUnits.Default,
            L("Permission:OrganizationUnits")
        );
        ou.AddChild(
            IdentityServicePermissions.OrganizationUnits.ManageOU,
            L("Permission:OrganizationUnits.ManageOU")
        );
        ou.AddChild(
            IdentityServicePermissions.OrganizationUnits.ManageRoles,
            L("Permission:OrganizationUnits.ManageRoles")
        );
        ou.AddChild(
            IdentityServicePermissions.OrganizationUnits.ManageMembers,
            L("Permission:OrganizationUnits.ManageMembers")
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<IdentityServiceResource>(name);
    }
}
