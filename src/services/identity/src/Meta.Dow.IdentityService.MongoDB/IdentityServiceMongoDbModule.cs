using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.Identity.MongoDB;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.OpenIddict.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.IdentityService.MongoDB;

[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(AbpIdentityMongoDbModule))]
[DependsOn(typeof(AbpOpenIddictMongoDbModule))]
[DependsOn(typeof(IdentityServiceDomainModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class IdentityServiceMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options =>
        {
            options.Databases.Configure(
                MetaDowNames.IdentityServiceDb,
                db =>
                {
                    db.MappedConnections.Add("AbpIdentity");
                    db.MappedConnections.Add("AbpOpenIddict");
                }
            );
        });
    }
}
