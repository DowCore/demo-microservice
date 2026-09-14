using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.Administration.Localization;
using Meta.Dow.Administration.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Meta.Dow.Administration.Permissions;

public class AdministrationPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var administrationGroup = context.AddGroup(AdministrationPermissions.GroupName, L("Permission:Administration"));

        var settingsPermissions = administrationGroup.AddPermission(
            AdministrationPermissions.Settings.Default,
            L("Permission:Administration:Settings")
        );
        settingsPermissions.AddChild(
            AdministrationPermissions.Settings.Update,
            L("Permission:Administration:Settings.Update")
        );

        var menus = administrationGroup.AddPermission(
            AdministrationPermissions.Menus.Default,
            L("Permission:Administration:Menus")
        );
        menus.AddChild(AdministrationPermissions.Menus.Create, L("Permission:Administration:Menus.Create"));
        menus.AddChild(AdministrationPermissions.Menus.Update, L("Permission:Administration:Menus.Update"));
        menus.AddChild(AdministrationPermissions.Menus.Delete, L("Permission:Administration:Menus.Delete"));

        var dataScopes = administrationGroup.AddPermission(
            AdministrationPermissions.DataScopes.Default,
            L("Permission:Administration:DataScopes")
        );
        dataScopes.AddChild(
            AdministrationPermissions.DataScopes.Manage,
            L("Permission:Administration:DataScopes.Manage")
        );
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AdministrationResource>(name);
    }
}
