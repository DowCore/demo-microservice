using Volo.Abp.ObjectExtending;
using Volo.Abp.Threading;

namespace Meta.Dow.SaaS;

public static class SaaSModuleExtensionConfigurator
{
    private static readonly OneTimeRunner OneTimeRunner = new();

    public static void Configure()
    {
        OneTimeRunner.Run(() =>
        {
            ObjectExtensionManager
                .Instance.Modules()
                .ConfigureTenantManagement(tenantManagement =>
                {
                    tenantManagement.ConfigureTenant(tenant =>
                    {
                        tenant.AddOrUpdateProperty<string>("AdminEmail");
                    });
                });
        });
    }
}
