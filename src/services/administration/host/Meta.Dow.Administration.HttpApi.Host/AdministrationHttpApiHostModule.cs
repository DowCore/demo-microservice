using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Logging;
using Meta.Dow.Administration.MongoDB;
using Meta.Dow.IdentityService;
using Meta.Dow.IdentityService.MongoDB;
using Meta.Dow.MultiTenancy;
using Meta.Dow.SaaS;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc.UI.MultiTenancy;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Meta.Dow.Administration;

[DependsOn(typeof(AbpAspNetCoreMvcUiMultiTenancyModule))]
[DependsOn(typeof(AbpIdentityDomainModule))]
[DependsOn(typeof(AdministrationApplicationModule))]
[DependsOn(typeof(AdministrationMongoDbModule))]
[DependsOn(typeof(AdministrationHttpApiModule))]
[DependsOn(typeof(IdentityServiceApplicationContractsModule))]
[DependsOn(typeof(IdentityServiceMongoDbModule))]
[DependsOn(typeof(SaaSApplicationContractsModule))]
[DependsOn(typeof(MetaDowMicroserviceModule))]
[DependsOn(typeof(MetaDowServiceDefaultsModule))]
public class AdministrationHttpApiHostModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        context.ConfigureMicroservice(MetaDowNames.AdministrationApi);

        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<AdministrationDomainSharedModule>(
                    Path.Combine(
                        hostingEnvironment.ContentRootPath,
                        string.Format("..{0}..{0}src{0}Meta.Dow.Administration.Domain.Shared", Path.DirectorySeparatorChar)
                    )
                );
                options.FileSets.ReplaceEmbeddedByPhysical<AdministrationDomainModule>(
                    Path.Combine(
                        hostingEnvironment.ContentRootPath,
                        string.Format("..{0}..{0}src{0}Meta.Dow.Administration.Domain", Path.DirectorySeparatorChar)
                    )
                );
                options.FileSets.ReplaceEmbeddedByPhysical<AdministrationApplicationContractsModule>(
                    Path.Combine(
                        hostingEnvironment.ContentRootPath,
                        string.Format(
                            "..{0}..{0}src{0}Meta.Dow.Administration.Application.Contracts",
                            Path.DirectorySeparatorChar
                        )
                    )
                );
                options.FileSets.ReplaceEmbeddedByPhysical<AdministrationApplicationModule>(
                    Path.Combine(
                        hostingEnvironment.ContentRootPath,
                        string.Format("..{0}..{0}src{0}Meta.Dow.Administration.Application", Path.DirectorySeparatorChar)
                    )
                );
            });
        }
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        IdentityModelEventSource.ShowPII = true;
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseHttpsRedirection();
        app.UseCorrelationId();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }

        app.UseAbpRequestLocalization();
        app.UseAuthorization();
        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Administration API");
            var configuration = context.GetConfiguration();
            options.OAuthClientId(configuration["AuthServer:SwaggerClientId"]);
            options.OAuthClientSecret(configuration["AuthServer:SwaggerClientSecret"]);
        });
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}
