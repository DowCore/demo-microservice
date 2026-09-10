using Meta.Dow.Administration;
using Meta.Dow.Administration.MongoDB;
using Meta.Dow.IdentityService;
using Meta.Dow.IdentityService.MongoDB;
using Meta.Dow.Projects;
using Meta.Dow.Projects.MongoDB;
using Meta.Dow.SaaS;
using Meta.Dow.SaaS.MongoDB;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict.Tokens;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;

namespace Meta.Dow.DbMigrator;

[DependsOn(typeof(AbpAutofacModule))]
[DependsOn(typeof(AbpBackgroundJobsAbstractionsModule))]
[DependsOn(typeof(AdministrationMongoDbModule))]
[DependsOn(typeof(AdministrationApplicationContractsModule))]
[DependsOn(typeof(IdentityServiceMongoDbModule))]
[DependsOn(typeof(IdentityServiceApplicationContractsModule))]
[DependsOn(typeof(ProjectsMongoDbModule))]
[DependsOn(typeof(ProjectsApplicationContractsModule))]
[DependsOn(typeof(SaaSMongoDbModule))]
[DependsOn(typeof(SaaSApplicationContractsModule))]
// [DependsOn(typeof(WebAppEntityFrameworkCoreModule))]
// [DependsOn(typeof(WebAppApplicationContractsModule))]
public class MetaDowDbMigratorModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpBackgroundJobOptions>(options => options.IsJobExecutionEnabled = false);
        Configure<TokenCleanupOptions>(options => options.IsCleanupEnabled = false);
    }

    public override void PostConfigureServices(ServiceConfigurationContext context)
    {
        // PostConfigure runs AFTER all other Configure calls, ensuring our settings override everything
        context.Services.PostConfigure<PermissionManagementOptions>(options =>
            options.IsDynamicPermissionStoreEnabled = false
        );

        context.Services.PostConfigure<FeatureManagementOptions>(options =>
            options.IsDynamicFeatureStoreEnabled = false
        );

        context.Services.PostConfigure<SettingManagementOptions>(options =>
            options.IsDynamicSettingStoreEnabled = false
        );
    }
}
