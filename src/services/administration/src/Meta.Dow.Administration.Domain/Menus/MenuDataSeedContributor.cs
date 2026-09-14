using System;
using System.Threading.Tasks;
using Meta.Dow.Administration.Menus;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.Administration.Menus;

public class MenuDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<SysMenu, Guid> _menuRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public MenuDataSeedContributor(
        IRepository<SysMenu, Guid> menuRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant
    )
    {
        _menuRepository = menuRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context?.TenantId))
        {
            if (await _menuRepository.GetCountAsync() == 0)
            {
                await SeedInitialMenusAsync();
            }

            await EnsureEmailSettingsMenuAsync();
            await EnsureMenuKeepAliveAsync();
            await RemoveDemoMenusAsync();
        }
    }

    private async Task SeedInitialMenusAsync()
    {
        var dashboardId = _guidGenerator.Create();
        var abpId = _guidGenerator.Create();
        var iamId = _guidGenerator.Create();

        await InsertAsync(
            dashboardId,
            null,
            "Dashboard",
            "仪表盘",
            MenuType.Directory,
            "/dashboard",
            null,
            null,
            "lucide:layout-dashboard",
            -1
        );
        await InsertAsync(
            _guidGenerator.Create(),
            dashboardId,
            "Workspace",
            "工作台",
            MenuType.Menu,
            "/dashboard/workspace",
            "/dashboard/workspace/index",
            null,
            "carbon:workspace",
            0,
            affixTab: true
        );

        await InsertAsync(
            abpId,
            null,
            "AbpAdmin",
            "ABP 管理",
            MenuType.Directory,
            "/abp",
            null,
            null,
            "lucide:shield",
            10
        );
        await InsertAsync(
            _guidGenerator.Create(),
            abpId,
            "AbpIdentityUsers",
            "用户",
            MenuType.Menu,
            "/abp/identity/users",
            "/abp/identity/users",
            "AbpIdentity.Users",
            "lucide:users",
            0
        );
        await InsertAsync(
            _guidGenerator.Create(),
            abpId,
            "AbpIdentityRoles",
            "角色",
            MenuType.Menu,
            "/abp/identity/roles",
            "/abp/identity/roles",
            "AbpIdentity.Roles",
            "lucide:shield-check",
            1
        );
        await InsertAsync(
            _guidGenerator.Create(),
            abpId,
            "AbpIdentityOrganizationUnits",
            "组织机构",
            MenuType.Menu,
            "/abp/identity/organization-units",
            "/abp/identity/organization-units",
            "AbpIdentity.OrganizationUnits",
            "lucide:network",
            2
        );
        await InsertAsync(
            _guidGenerator.Create(),
            abpId,
            "AbpSaasTenants",
            "租户",
            MenuType.Menu,
            "/abp/saas/tenants",
            "/abp/saas/tenants",
            "AbpTenantManagement.Tenants",
            "lucide:building-2",
            3
        );
        await InsertAsync(
            _guidGenerator.Create(),
            abpId,
            "AbpEmailSettings",
            "邮件设置",
            MenuType.Menu,
            "/abp/settings/emailing",
            "/abp/settings/emailing",
            "SettingManagement.Emailing",
            "lucide:mail",
            4
        );

        await InsertAsync(
            iamId,
            null,
            "IamAdmin",
            "权限中心",
            MenuType.Directory,
            "/iam",
            null,
            null,
            "lucide:key-round",
            20
        );
        await InsertAsync(
            _guidGenerator.Create(),
            iamId,
            "IamMenus",
            "菜单管理",
            MenuType.Menu,
            "/iam/menus",
            "/iam/menus/index",
            "Administration.Menus",
            "lucide:menu",
            0
        );
        await InsertAsync(
            _guidGenerator.Create(),
            iamId,
            "IamDataScopes",
            "数据权限",
            MenuType.Menu,
            "/iam/data-scopes",
            "/iam/data-scopes/index",
            "Administration.DataScopes",
            "lucide:database",
            1
        );
    }

    private async Task EnsureEmailSettingsMenuAsync()
    {
        var existing = await _menuRepository.FirstOrDefaultAsync(x => x.Name == "AbpEmailSettings");
        if (existing != null)
        {
            if (!existing.KeepAlive)
            {
                existing.Update(
                    existing.Name,
                    existing.Title,
                    existing.Type,
                    existing.ParentId,
                    existing.Path,
                    existing.Component,
                    existing.Redirect,
                    existing.Permission,
                    existing.Icon,
                    existing.SystemCode,
                    existing.Order,
                    existing.IsVisible,
                    existing.IsEnabled,
                    existing.AffixTab,
                    keepAlive: true
                );
                await _menuRepository.UpdateAsync(existing, autoSave: true);
            }
            return;
        }

        var abpAdmin = await _menuRepository.FirstOrDefaultAsync(x => x.Name == "AbpAdmin");
        if (abpAdmin == null)
        {
            return;
        }

        await InsertAsync(
            _guidGenerator.Create(),
            abpAdmin.Id,
            "AbpEmailSettings",
            "邮件设置",
            MenuType.Menu,
            "/abp/settings/emailing",
            "/abp/settings/emailing",
            "SettingManagement.Emailing",
            "lucide:mail",
            4
        );
    }

    private async Task EnsureMenuKeepAliveAsync()
    {
        var menus = await _menuRepository.GetListAsync(x => x.Type == MenuType.Menu && !x.KeepAlive);
        foreach (var menu in menus)
        {
            menu.Update(
                menu.Name,
                menu.Title,
                menu.Type,
                menu.ParentId,
                menu.Path,
                menu.Component,
                menu.Redirect,
                menu.Permission,
                menu.Icon,
                menu.SystemCode,
                menu.Order,
                menu.IsVisible,
                menu.IsEnabled,
                menu.AffixTab,
                keepAlive: true
            );
            await _menuRepository.UpdateAsync(menu, autoSave: true);
        }
    }

    private async Task RemoveDemoMenusAsync()
    {
        string[] demoNames =
        [
            "Analytics",
            "Demos",
            "AntDesignDemos",
            "VbenProject",
            "VbenDocument",
            "VbenGithub",
            "VbenAntdVNext",
            "VbenNaive",
            "VbenTDesign",
            "VbenElementPlus",
            "VbenAbout",
        ];

        foreach (var name in demoNames)
        {
            var menu = await _menuRepository.FirstOrDefaultAsync(x => x.Name == name);
            if (menu != null)
            {
                await _menuRepository.DeleteAsync(menu, autoSave: true);
            }
        }
    }

    private async Task InsertAsync(
        Guid id,
        Guid? parentId,
        string name,
        string title,
        MenuType type,
        string? path,
        string? component,
        string? permission,
        string? icon,
        int order,
        bool affixTab = false
    )
    {
        var menu = new SysMenu(
            id,
            name,
            title,
            type,
            parentId,
            path,
            component,
            permission,
            icon,
            MenuConsts.DefaultSystemCode,
            order,
            _currentTenant.Id
        );
        menu.Update(
            name,
            title,
            type,
            parentId,
            path,
            component,
            null,
            permission,
            icon,
            MenuConsts.DefaultSystemCode,
            order,
            isVisible: true,
            isEnabled: true,
            affixTab,
            keepAlive: type == MenuType.Menu
        );
        await _menuRepository.InsertAsync(menu, autoSave: true);
    }
}
