using Meta.Dow.IdentityService.Localization;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.Validation;
using Volo.Abp.Validation.Localization;
using Volo.Abp.VirtualFileSystem;

namespace Meta.Dow.IdentityService;

[DependsOn(typeof(AbpValidationModule))]
[DependsOn(typeof(AbpIdentityDomainSharedModule))]
[DependsOn(typeof(AbpOpenIddictDomainSharedModule))]
public class IdentityServiceDomainSharedModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
            options.FileSets.AddEmbedded<IdentityServiceDomainSharedModule>()
        );

        Configure<AbpLocalizationOptions>(options =>
        {
            options
                .Resources.Add<IdentityServiceResource>("en")
                .AddBaseTypes(typeof(AbpValidationResource))
                .AddVirtualJson("/Localization/IdentityService");
        });

        Configure<AbpExceptionLocalizationOptions>(options =>
            options.MapCodeNamespace("IdentityService", typeof(IdentityServiceResource))
        );
    }
}
