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

        // A7：出站安全开关（Http 白名单、重定向策略）。
        context.Services.Configure<OrchestrationOptions>(
            context.Services.GetConfiguration().GetSection(OrchestrationOptions.SectionName)
        );

        context.Services.AddHttpClient(
            "OrchestrationHttpCall",
            client =>
            {
                // 超时决策交给弹性管道（见 Meta.Dow.ServiceDefaults）：总预算 60s（含重试）、
                // 单次尝试 10s。此处不设 client.Timeout，避免外层截断重试空间。
                // 节点级超时由 FlowExecutor 的 timeoutMs 控制（默认 30s）。
                client.Timeout = Timeout.InfiniteTimeSpan;
            }
        )
        // A7：默认禁止跟随重定向。出站白闸只判定首跳，
        // 跟随 3xx 等于把判定权交给对端——公网地址 302 跳 169.254.169.254 是经典绕过。
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

            if (sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrchestrationOptions>>().Value
                .HttpAllowRedirects)
            {
                handler.AllowAutoRedirect = true;
                handler.MaxAutomaticRedirections = 5;
            }

            return handler;
        });

        context.Services.AddAutoMapperObjectMapper<SaaSApplicationModule>();
        Configure<AbpAutoMapperOptions>(options => options.AddMaps<SaaSApplicationModule>(true));

        context.Services.AddHostedService<RabbitMessageTriggerHostedService>();
        context.Services.AddHostedService<KafkaMessageTriggerHostedService>();
        context.Services.AddHostedService<MqttMessageTriggerHostedService>();
        context.Services.AddHostedService<FlowScheduleHostedService>();

        // C1 修复：异步作业的持久化执行器。作业落库后由它轮询执行，
        // 因此进程重启不会让排队中的异步外呼静默消失。
        context.Services.AddHostedService<FlowAsyncJobWorker>();
    }
}
