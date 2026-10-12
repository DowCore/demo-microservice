using System;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace Meta.Dow.Gateway;

internal static class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        builder.Services.AddOpenApi(options => options.UseJwtBearerAuthentication());

        // 令牌校验：原先 AddJwtBearer() 未配置 Authority/Audience，网关实际上不校验令牌。
        var configuration = builder.Configuration;
        var authority = configuration["AuthServer:Authority"] ?? "https://localhost:7600/";

        builder
            .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = configuration.GetValue("AuthServer:RequireHttpsMetadata", false);
                options.TokenValidationParameters.ValidateIssuer = true;

                // ⚠ 不要在此写死单一 Audience。
                // 本网关同时前置多个 API，各 API 的受众互不相同：OpenIddict 把「作用域关联的资源名」
                // 写进令牌的 aud，本仓库即 MetaDowAdministration / MetaDowAuthServer /
                // MetaDowIdentityService / MetaDowSaaS（见 DbMigrator 的 ApiResource 与
                // OpenIddictScopes.Resources）。这些名字里**没有**解决方案名 "Meta.Dow"。
                // 若写死 Audience="Meta.Dow" 并开启校验，则**每一个有效令牌都会被判无效**，
                // 表现为网关对所有需要认证的请求返回 401（曾实际发生）。
                // 因此默认只校验签名/签发者/有效期，受众交由下游各服务按自身受众把关
                // （各服务 ConfigureAuthentication(configuration, MetaDowNames.XxxApi)）。
                // 如需在网关强校验受众，在 AuthServer:ValidAudiences 显式列出允许值。
                var validAudiences = configuration.GetSection("AuthServer:ValidAudiences").Get<string[]>();
                if (validAudiences is { Length: > 0 })
                {
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidAudiences = validAudiences;
                }
                else
                {
                    options.TokenValidationParameters.ValidateAudience = false;
                }
                // 统一错误体：与 ABP 的 RemoteServiceErrorResponse 结构一致，前端可统一处理。
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        await context.Response.WriteAsync(
                            JsonSerializer.Serialize(
                                new { error = new { code = "Unauthorized", message = "未认证或令牌无效" } }
                            )
                        );
                    },
                };
            });

        // 默认策略要求已认证：兜底路由不再对匿名请求放行。
        builder.Services.AddAuthorization(options =>
            options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
        );

        // 网关限流：按用户（未登录按来源 IP）固定窗口限流，防止单点打爆后端。
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(
                        new { error = new { code = "TooManyRequests", message = "请求过于频繁，请稍后再试" } }
                    ),
                    cancellationToken
                );
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var key =
                    context.User.Identity?.Name
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(
                    key,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }
                );
            });
        });

        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

        var app = builder.Build();

        app.MapDefaultEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.UseHttpsRedirection();

        app.UseRateLimiter();

        app.UseAuthentication();

        app.UseAuthorization();

        app.MapReverseProxy();

        app.Run();
    }
}
