using Localization.Resources.AbpUi;
using Microsoft.Extensions.DependencyInjection;
using Meta.Dow.IdentityService.Localization;
using Volo.Abp.Account;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Meta.Dow.IdentityService;

[DependsOn(typeof(IdentityServiceApplicationContractsModule))]
[DependsOn(typeof(AbpAspNetCoreMvcModule))]
[DependsOn(typeof(AbpIdentityHttpApiModule))]
[DependsOn(typeof(AbpAccountHttpApiModule))]
public class IdentityServiceHttpApiModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<IMvcBuilder>(mvcBuilder =>
            mvcBuilder.AddApplicationPartIfNotExists(typeof(IdentityServiceHttpApiModule).Assembly)
        );
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpLocalizationOptions>(options =>
            options.Resources.Get<IdentityServiceResource>().AddBaseTypes(typeof(AbpUiResource))
        );
    }
}
