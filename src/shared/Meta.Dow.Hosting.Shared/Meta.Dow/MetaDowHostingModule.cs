using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using StackExchange.Redis;
using Meta.Dow.MultiTenancy;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs.RabbitMQ;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.Data;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus.RabbitMq;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.RabbitMQ;
using Volo.Abp.SettingManagement;
using Volo.Abp.Swashbuckle;

namespace Meta.Dow;

[DependsOn(typeof(AbpAspNetCoreMultiTenancyModule))]
[DependsOn(typeof(AbpAspNetCoreSerilogModule))]
[DependsOn(typeof(AbpAutofacModule))]
[DependsOn(typeof(AbpBackgroundJobsRabbitMqModule))]
[DependsOn(typeof(AbpCachingStackExchangeRedisModule))]
[DependsOn(typeof(AbpDataModule))]
[DependsOn(typeof(AbpDistributedLockingModule))]
[DependsOn(typeof(AbpEventBusRabbitMqModule))]
[DependsOn(typeof(AbpSwashbuckleModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class MetaDowHostingModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var configuration = context.Services.GetConfiguration();

        context.ConfigureDistributedLocking(configuration);

        Configure<AbpMultiTenancyOptions>(options => options.IsEnabled = MultiTenancyConsts.IsEnabled);

        // Disable dynamic initializers to prevent race conditions when multiple services share the same database
        Configure<PermissionManagementOptions>(options => options.IsDynamicPermissionStoreEnabled = false);

        Configure<FeatureManagementOptions>(options => options.IsDynamicFeatureStoreEnabled = false);

        Configure<SettingManagementOptions>(options => options.IsDynamicSettingStoreEnabled = false);

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Languages.Add(new LanguageInfo("ar", "ar", "العربية"));
            options.Languages.Add(new LanguageInfo("cs", "cs", "Čeština"));
            options.Languages.Add(new LanguageInfo("en", "en", "English"));
            options.Languages.Add(new LanguageInfo("en-GB", "en-GB", "English (UK)"));
            options.Languages.Add(new LanguageInfo("fi", "fi", "Finnish"));
            options.Languages.Add(new LanguageInfo("fr", "fr", "Français"));
            options.Languages.Add(new LanguageInfo("hi", "hi", "Hindi"));
            options.Languages.Add(new LanguageInfo("is", "is", "Icelandic"));
            options.Languages.Add(new LanguageInfo("it", "it", "Italiano"));
            options.Languages.Add(new LanguageInfo("hu", "hu", "Magyar"));
            options.Languages.Add(new LanguageInfo("pt-BR", "pt-BR", "Português"));
            options.Languages.Add(new LanguageInfo("ro-RO", "ro-RO", "Română"));
            options.Languages.Add(new LanguageInfo("ru", "ru", "Русский"));
            options.Languages.Add(new LanguageInfo("sk", "sk", "Slovak"));
            options.Languages.Add(new LanguageInfo("tr", "tr", "Türkçe"));
            options.Languages.Add(new LanguageInfo("zh-Hans", "zh-Hans", "简体中文"));
            options.Languages.Add(new LanguageInfo("zh-Hant", "zh-Hant", "繁體中文"));
            options.Languages.Add(new LanguageInfo("de-DE", "de-DE", "Deutsch"));
            options.Languages.Add(new LanguageInfo("es", "es", "Español"));
        });

        Configure<AbpRabbitMqOptions>(options =>
        {
            var cstr = configuration.GetConnectionString(MetaDowNames.RabbitMq);
            options.Connections.Default = new ConnectionFactory() { Uri = new Uri(cstr!) };
        });

        Configure<AbpRabbitMqEventBusOptions>(options =>
        {
            options.ClientName = configuration["RabbitMQ:EventBus:ClientName"]!;
            options.ExchangeName = configuration["RabbitMQ:EventBus:ExchangeName"]!;
        });
    }

}

public static class HostingExtensions
{
    /// <summary>
    /// 注册分布式锁提供者（Redis/Medallion）。未配置 Redis 连接串时直接跳过，
    /// 由 ABP 回退到进程内锁，避免在无 Redis 的环境下启动失败。
    /// 复用入口：各服务 Host 模块可单独调用本扩展方法获得跨实例锁（如低代码调度器）。
    /// </summary>
    public static ServiceConfigurationContext ConfigureDistributedLocking(
        this ServiceConfigurationContext context,
        IConfiguration configuration
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(MetaDowNames.Redis);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return context;
        }

        context.Services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            var connection = ConnectionMultiplexer.Connect(connectionString);

            return new RedisDistributedSynchronizationProvider(connection.GetDatabase());
        });

        return context;
    }

    public static ServiceConfigurationContext ConfigureDataProtection(
        this ServiceConfigurationContext context,
        IWebHostEnvironment hostingEnvironment,
        IConfiguration configuration,
        string name
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        var dataProtectionBuilder = context.Services.AddDataProtection().SetApplicationName(MetaDowNames.MetaDow);
        if (!hostingEnvironment.IsDevelopment())
        {
            var redis = ConnectionMultiplexer.Connect(configuration.GetConnectionString(MetaDowNames.Redis)!);
            dataProtectionBuilder.PersistKeysToStackExchangeRedis(redis, $"{name}-Keys");
        }

        return context;
    }
}
