using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.Projects.MongoDB;

[DependsOn(typeof(ProjectsDomainModule))]
[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class ProjectsMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options => options.Databases.Configure(MetaDowNames.ProjectsDb, db => { }));

        context.Services.AddMongoDbContext<ProjectsDbContext>(options => options.AddDefaultRepositories(true));
    }
}
