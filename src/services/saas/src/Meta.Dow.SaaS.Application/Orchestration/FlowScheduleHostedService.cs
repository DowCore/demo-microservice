using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 轮询已启用 FlowSchedule，按 Cron 调用已发布 flowKey（轻量调度，产品表自建）。
/// 多实例部署时通过分布式锁保证同一计划同一时刻只触发一次。
/// </summary>
public class FlowScheduleHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FlowScheduleHostedService> _logger;
    private readonly IAbpDistributedLock _distributedLock;

    public FlowScheduleHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<FlowScheduleHostedService> logger,
        IAbpDistributedLock distributedLock
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _distributedLock = distributedLock;
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
        List<FlowSchedule> due;

        using (var scope = _scopeFactory.CreateScope())
        {
            var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<FlowSchedule, Guid>>();
            using (dataFilter.Disable<IMultiTenant>())
            {
                var all = await repo.GetListAsync(x => x.IsEnabled, cancellationToken: cancellationToken);
                due = all
                    .Where(s =>
                    {
                        if (s.NextFireAt.HasValue)
                        {
                            return s.NextFireAt.Value <= utcNow;
                        }

                        // 首次：若从未算过 Next，补算；到点则触发
                        var next = FlowScheduleAppService.ComputeNext(s.Cron, s.TimeZone, utcNow.AddMinutes(-1));
                        return next.HasValue && next.Value <= utcNow;
                    })
                    .ToList();
            }
        }

        foreach (var schedule in due)
        {
            try
            {
                await FireAsync(schedule, utcNow, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Schedule {Code} fire failed for FlowKey={FlowKey}",
                    schedule.Code,
                    schedule.FlowKey
                );
            }
        }
    }

    private async Task FireAsync(FlowSchedule schedule, DateTime utcNow, CancellationToken cancellationToken)
    {
        // 多实例防重：同一计划同一时刻只允许一个实例触发（Redis 锁；未配置 Redis 时退化为进程内锁）。
        await using var handle = await _distributedLock.TryAcquireAsync(
            $"meta-dow:flow-schedule:{schedule.Code}",
            TimeSpan.FromSeconds(30),
            cancellationToken
        );
        if (handle == null)
        {
            _logger.LogDebug("Schedule {Code} 已被其它实例接管，跳过本次触发。", schedule.Code);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<IPublishedFlowInvoker>();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<FlowSchedule, Guid>>();
        var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();

        Exception? invokeError = null;
        try
        {
            await invoker.InvokeAsync(
                schedule.FlowKey,
                schedule.VariablesJson ?? "{}",
                triggerSource: $"Schedule:{schedule.Code}",
                tenantId: schedule.TenantId,
                filterOutputsByRole: false,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            invokeError = ex;
            _logger.LogError(
                ex,
                "Schedule invoke failed. Code={Code} FlowKey={FlowKey}",
                schedule.Code,
                schedule.FlowKey
            );
        }

        // 无论调用成败都推进下次时间，避免 NextFireAt 卡在过去反复空转
        var next = FlowScheduleAppService.ComputeNext(schedule.Cron, schedule.TimeZone, utcNow);
        if (next.HasValue && next.Value <= utcNow)
        {
            next = FlowScheduleAppService.ComputeNext(
                schedule.Cron,
                schedule.TimeZone,
                utcNow.AddSeconds(1)
            );
        }

        using (dataFilter.Disable<IMultiTenant>())
        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
        {
            var entity = await repo.GetAsync(schedule.Id, cancellationToken: cancellationToken);
            entity.MarkFired(utcNow, next);
            await repo.UpdateAsync(entity, autoSave: true, cancellationToken: cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        if (invokeError != null)
        {
            throw invokeError;
        }

        _logger.LogInformation(
            "Schedule fired. Code={Code} FlowKey={FlowKey} Next={Next}",
            schedule.Code,
            schedule.FlowKey,
            next
        );
    }
}
