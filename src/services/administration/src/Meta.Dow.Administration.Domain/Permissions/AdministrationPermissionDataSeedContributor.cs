using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;

namespace Meta.Dow.Administration.Permissions;

/// <summary>
/// 为 admin 角色授予当前侧（Host/Tenant）下全部可授予权限，
/// 与 ABP 默认 PermissionDataSeedContributor / 租户创建逻辑对齐。
/// </summary>
public class AdministrationPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IPermissionDataSeeder _permissionDataSeeder;
    private readonly ICurrentTenant _currentTenant;

    public AdministrationPermissionDataSeedContributor(
        IPermissionDefinitionManager permissionDefinitionManager,
        IPermissionDataSeeder permissionDataSeeder,
        ICurrentTenant currentTenant
    )
    {
        _permissionDefinitionManager = permissionDefinitionManager;
        _permissionDataSeeder = permissionDataSeeder;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context?.TenantId))
        {
            var multiTenancySide = context?.TenantId is null
                ? MultiTenancySides.Host
                : MultiTenancySides.Tenant;

            var permissionNames = (await _permissionDefinitionManager.GetPermissionsAsync())
                .Where(p => p.MultiTenancySide.HasFlag(multiTenancySide))
                .Where(p =>
                    p.Providers.Count == 0
                    || p.Providers.Contains(RolePermissionValueProvider.ProviderName)
                )
                .Select(p => p.Name)
                .ToArray();

            await _permissionDataSeeder.SeedAsync(
                RolePermissionValueProvider.ProviderName,
                "admin",
                permissionNames,
                context?.TenantId
            );
        }
    }
}
