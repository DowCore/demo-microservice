using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 轮询已启用 FlowSchedule，按 Cron 调用已发布 flowKey（轻量调度，产品表自建）。
/// 多实例部署时通过分布式锁保证同一计划同一时刻只触发一次。
/// </summary>
public class FlowScheduleHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FlowScheduleHostedService> _logger;

    public FlowScheduleHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<FlowScheduleHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Flow schedule host started.");
        // 错开启动，避免与其它 host 抢同一秒
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flow schedule tick failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        List<Guid> dueIds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<FlowSchedule, Guid>>();
            var fires = scope.ServiceProvider.GetRequiredService<IRepository<FlowScheduleFire, Guid>>();
            using (dataFilter.Disable<IMultiTenant>())
            {
                var enabled = await repo.GetListAsync(x => x.IsEnabled, cancellationToken: cancellationToken);
                var due = enabled.Where(s => IsDue(s, utcNow)).Select(s => s.Id);
                var open = await fires.GetListAsync(
                    x => x.Status == FlowScheduleFireStatus.Running || x.Status == FlowScheduleFireStatus.RetryWait,
                    cancellationToken: cancellationToken
                );
                dueIds = due.Concat(open.Select(x => x.ScheduleId)).Distinct().ToList();
            }
        }

        foreach (var id in dueIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<FlowScheduleRunner>();
                await runner.TickAsync(id, utcNow, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schedule {ScheduleId} tick failed.", id);
            }
        }
    }

    private static bool IsDue(FlowSchedule schedule, DateTime utcNow)
    {
        if (schedule.NextFireAt.HasValue)
        {
            return schedule.NextFireAt.Value <= utcNow;
        }

        var next = FlowScheduleAppService.ComputeNext(schedule.Cron, schedule.TimeZone, utcNow.AddMinutes(-1));
        return next.HasValue && next.Value <= utcNow;
    }
}
