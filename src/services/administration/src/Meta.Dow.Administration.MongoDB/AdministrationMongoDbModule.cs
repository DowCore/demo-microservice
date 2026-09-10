using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.AuditLogging.MongoDB;
using Volo.Abp.Data;
using Volo.Abp.FeatureManagement.MongoDB;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.PermissionManagement.MongoDB;
using Volo.Abp.SettingManagement.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.Administration.MongoDB;

[DependsOn(typeof(AbpAuditLoggingMongoDbModule))]
[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(AbpFeatureManagementMongoDbModule))]
[DependsOn(typeof(AbpPermissionManagementMongoDbModule))]
[DependsOn(typeof(AbpSettingManagementMongoDbModule))]
[DependsOn(typeof(AdministrationDomainModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class AdministrationMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options =>
        {
            options.Databases.Configure(
                MetaDowNames.AdministrationDb,
                db =>
                {
                    db.MappedConnections.Add("AbpAuditLogging");
                    db.MappedConnections.Add("AbpFeatureManagement");
                    db.MappedConnections.Add("AbpPermissionManagement");
                    db.MappedConnections.Add("AbpSettingManagement");
                }
            );
        });
    }
}
