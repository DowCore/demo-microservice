using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureMongoConnectionStrings();
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default.
            // 安全默认值：标准处理器默认会重试所有 HTTP 方法（含 POST/PUT/DELETE），
            // 这会让**非幂等写操作被静默重放**（编排的 HTTP 节点会发 POST）。
            // 因此显式禁止对非安全方法重试 —— 与 Temporal/n8n 的一致立场：
            // 写操作的重试必须由使用方显式声明，不能由基础设施默认开启。
            http.AddStandardResilienceHandler(options =>
            {
                options.Retry.DisableForUnsafeHttpMethods();

                // 与编排的节点级超时（OrchestrationConsts.NodeDefaultTimeoutMs = 30s）对齐：
                // 单次尝试 10s、总预算 60s（含重试），避免外层 HttpClient.Timeout 截断重试空间。
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);

                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.Retry.UseJitter = true; // 防重试风暴
            });

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Aspire 单节点 MongoDB 不是 replica set，ABP/驱动默认 retryable writes 会失败。
    /// </summary>
    private static IHostApplicationBuilder ConfigureMongoConnectionStrings(this IHostApplicationBuilder builder)
    {
        var keys = new[]
        {
            MetaDowNames.AdministrationDb,
            MetaDowNames.IdentityServiceDb,
            MetaDowNames.ProjectsDb,
            MetaDowNames.SaaSDb,
            MetaDowNames.AuthServerDb,
        };

        var overrides = new Dictionary<string, string?>();
        foreach (var key in keys)
        {
            var cs = builder.Configuration.GetConnectionString(key);
            if (string.IsNullOrWhiteSpace(cs))
            {
                continue;
            }

            if (cs.Contains("retryWrites=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            overrides[$"ConnectionStrings:{key}"] = cs.Contains('?', StringComparison.Ordinal)
                ? cs + "&retryWrites=false"
                : cs + "?retryWrites=false";
        }

        if (overrides.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder
            .Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation()
            )
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// 映射健康检查端点。原先只在 Development 且从未被调用，导致 Aspire / 容器编排的就绪探针拿不到信号。
    /// 现在无论环境都映射：/health（全部检查）、/alive（liveness）。仅在开发环境返回逐项明细，
    /// 生产只返回聚合状态，避免泄露内部依赖信息。
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var detailed = app.Environment.IsDevelopment();

        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks(
            "/health",
            new HealthCheckOptions { ResponseWriter = (context, report) => WriteReportAsync(context, report, detailed) }
        );

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(
            "/alive",
            new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
                ResponseWriter = (context, report) => WriteReportAsync(context, report, detailed),
            }
        );

        return app;
    }

    private static Task WriteReportAsync(HttpContext context, HealthReport report, bool detailed)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        if (!detailed)
        {
            return context.Response.WriteAsync(report.Status.ToString());
        }

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,
                description = entry.Value.Description,
                error = entry.Value.Exception?.Message,
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
