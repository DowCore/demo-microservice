using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Meta.Dow.DbMigrator;

public class MetaDowDbMigrationService(
    ILogger<MetaDowDbMigrationService> logger,
    ITenantRepository tenantRepository,
    IDataSeeder dataSeeder,
    ICurrentTenant currentTenant
) : ITransientDependency
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding MongoDB host data ...");
        await SeedDataAsync(null).ConfigureAwait(false);

        var tenants = await tenantRepository
            .GetListAsync(includeDetails: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var tenant in tenants)
        {
            using (currentTenant.Change(tenant.Id))
            {
                await SeedDataAsync(tenant).ConfigureAwait(false);
            }
        }

        logger.LogInformation("MongoDB seed completed.");
    }

    private Task SeedDataAsync(Tenant? tenant)
    {
        if (tenant is null)
        {
            logger.LogInformation("Seeding host data ...");
        }
        else
        {
            logger.LogInformation("Seeding tenant data: {Name} ({Id})", tenant.Name, tenant.Id);
        }

        return dataSeeder.SeedAsync(
            new DataSeedContext(tenant?.Id)
                .WithProperty(
                    IdentityDataSeedContributor.AdminEmailPropertyName,
                    IdentityDataSeedContributor.AdminEmailDefaultValue
                )
                .WithProperty(
                    IdentityDataSeedContributor.AdminPasswordPropertyName,
                    IdentityDataSeedContributor.AdminPasswordDefaultValue
                )
        );
    }
}
