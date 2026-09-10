using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.TenantManagement.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.MongoDB;

[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(AbpTenantManagementMongoDbModule))]
[DependsOn(typeof(SaaSDomainModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class SaaSMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options =>
            options.Databases.Configure(MetaDowNames.SaaSDb, db => db.MappedConnections.Add("AbpTenantManagement")));
    }
}
