using Microsoft.Extensions.DependencyInjection;
using Meta.Dow.Administration;
using Meta.Dow.SaaS.Orchestration;
using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;

namespace Meta.Dow.SaaS;

[DependsOn(typeof(AbpAutoMapperModule))]
[DependsOn(typeof(AbpDddApplicationModule))]
[DependsOn(typeof(AbpTenantManagementApplicationModule))]
[DependsOn(typeof(AdministrationApplicationModule))]
[DependsOn(typeof(SaaSApplicationContractsModule))]
[DependsOn(typeof(SaaSDomainModule))]
public class SaaSApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMemoryCache();
        context.Services.AddHttpClient(
            "OrchestrationHttpCall",
            client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            }
        );

        context.Services.AddAutoMapperObjectMapper<SaaSApplicationModule>();
        Configure<AbpAutoMapperOptions>(options => options.AddMaps<SaaSApplicationModule>(true));

        context.Services.AddHostedService<RabbitMessageTriggerHostedService>();
        context.Services.AddHostedService<KafkaMessageTriggerHostedService>();
        context.Services.AddHostedService<MqttMessageTriggerHostedService>();
        context.Services.AddHostedService<FlowScheduleHostedService>();
    }
}
