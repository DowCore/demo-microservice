using Microsoft.Extensions.DependencyInjection;
using Meta.Dow.SaaS.Orchestration;
using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;

namespace Meta.Dow.SaaS;

[DependsOn(typeof(AbpAutoMapperModule))]
[DependsOn(typeof(AbpDddApplicationModule))]
[DependsOn(typeof(AbpTenantManagementApplicationModule))]
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
                // 超时决策交给弹性管道（见 Meta.Dow.ServiceDefaults）：总预算 60s（含重试）、
                // 单次尝试 10s。此处不设 client.Timeout，避免外层截断重试空间。
                // 节点级超时由 FlowExecutor 的 timeoutMs 控制（默认 30s）。
                client.Timeout = Timeout.InfiniteTimeSpan;
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
