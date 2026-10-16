using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 定时触发的唯一执行点。Cron 水位在接纳时推进；重试挂在已有的 <see cref="FlowScheduleFire"/> 上。
/// </summary>
public class FlowScheduleRunner : ITransientDependency
{
    private readonly IRepository<FlowSchedule, Guid> _schedules;
    private readonly IRepository<FlowScheduleFire, Guid> _fires;
    private readonly IPublishedFlowInvoker _invoker;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<FlowScheduleRunner> _logger;

    public FlowScheduleRunner(
        IRepository<FlowSchedule, Guid> schedules,
        IRepository<FlowScheduleFire, Guid> fires,
        IPublishedFlowInvoker invoker,
        IAbpDistributedLock distributedLock,
        IDataFilter dataFilter,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        ILogger<FlowScheduleRunner> logger
    )
    {
        _schedules = schedules;
        _fires = fires;
        _invoker = invoker;
        _distributedLock = distributedLock;
        _dataFilter = dataFilter;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task TickAsync(Guid scheduleId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var schedule = await _schedules.FindAsync(scheduleId, cancellationToken: cancellationToken);
            if (schedule == null)
            {
                return;
            }

            await using var handle = await _distributedLock.TryAcquireAsync(
                LockName(schedule.Code),
                TimeSpan.FromSeconds(30),
                cancellationToken
            );
            if (handle == null)
            {
                _logger.LogDebug("Schedule {Code} 已被其它实例接管，跳过本轮。", schedule.Code);
                return;
            }

            var open = await _fires.GetListAsync(
                x => x.ScheduleId == schedule.Id &&
                     (x.Status == FlowScheduleFireStatus.Running || x.Status == FlowScheduleFireStatus.RetryWait),
                cancellationToken: cancellationToken
            );
            var (cronDue, nominal) = CronDue(schedule, utcNow);
            var actions = FlowScheduleTickPlanner.Plan(new FlowScheduleTickRequest
            {
                UtcNow = utcNow,
                IsEnabled = schedule.IsEnabled,
                CronDue = cronDue,
                MaxAttempts = schedule.EffectiveMaxAttempts,
                RetryDelaysSeconds = schedule.RetryDelaysSeconds,
                OpenFires = open.Select(x => new FlowScheduleOpenFire
                {
                    Id = x.Id,
                    Status = x.Status,
                    Attempt = x.Attempt,
                    MaxAttempts = x.MaxAttempts,
                    NextRetryAt = x.NextRetryAt,
                    StartedAt = x.StartedAt
                }).ToList()
            });

            foreach (var action in actions)
            {
                switch (action.Kind)
                {
                    case FlowScheduleTickActionKind.FailStale:
                        await FailStaleAsync(schedule.Id, action, utcNow, cancellationToken);
                        break;
                    case FlowScheduleTickActionKind.StartRetry:
                        await StartRetryAsync(schedule.Id, action.FireId!.Value, utcNow, cancellationToken);
                        break;
                    case FlowScheduleTickActionKind.StartCron:
                        await AcceptCronAsync(schedule.Id, nominal, utcNow, skip: false, cancellationToken);
                        break;
                    case FlowScheduleTickActionKind.SkipCron:
                        await AcceptCronAsync(schedule.Id, nominal, utcNow, skip: true, cancellationToken);
                        break;
                }
            }
        }
    }

    public async Task<LogicRunResultDto> RunNowAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
        await using var handle = await _distributedLock.TryAcquireAsync(
            LockName(schedule.Code),
            TimeSpan.FromSeconds(30),
            cancellationToken
        );
        if (handle == null)
        {
            throw new UserFriendlyException("该任务正在执行，请稍后再试。");
        }

        var utcNow = DateTime.UtcNow;
        var running = await _fires.AnyAsync(
            x => x.ScheduleId == schedule.Id && x.Status == FlowScheduleFireStatus.Running,
            cancellationToken
        );
        if (running)
        {
            throw new UserFriendlyException("该任务正在执行，请稍后再试。");
        }

        var fire = await InsertFireAsync(
            schedule,
            FlowScheduleTriggerKind.Manual,
            utcNow,
            utcNow,
            skipped: false,
            advanceCron: false,
            cancellationToken
        );
        return await InvokeAndFinishAsync(schedule.Id, fire.Id, cancellationToken);
    }

    public async Task<FlowScheduleFire> QueueRetryAsync(
        Guid scheduleId,
        Guid fireId,
        CancellationToken cancellationToken = default
    )
    {
        var schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
        await using var handle = await _distributedLock.TryAcquireAsync(
            LockName(schedule.Code),
            TimeSpan.FromSeconds(30),
            cancellationToken
        );
        if (handle == null)
        {
            throw new UserFriendlyException("该任务正在执行，请稍后再试。");
        }

        var fire = await _fires.GetAsync(fireId, cancellationToken: cancellationToken);
        if (fire.ScheduleId != schedule.Id)
        {
            throw new UserFriendlyException("触发记录不存在。");
        }

        if (fire.Status != FlowScheduleFireStatus.Dead)
        {
            throw new UserFriendlyException("只有已放弃的记录可以再试。");
        }

        var utcNow = DateTime.UtcNow;
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        fire = await _fires.GetAsync(fireId, cancellationToken: cancellationToken);
        fire.QueueAnotherTry(utcNow);
        await _fires.UpdateAsync(fire, autoSave: true, cancellationToken: cancellationToken);
        var current = await _schedules.GetAsync(schedule.Id, cancellationToken: cancellationToken);
        current.NoteFireProgress(FlowScheduleFireStatus.RetryWait, error: null, dead: false);
        await _schedules.UpdateAsync(current, autoSave: true, cancellationToken: cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return fire;
    }

    private async Task FailStaleAsync(
        Guid scheduleId,
        FlowScheduleTickAction action,
        DateTime utcNow,
        CancellationToken cancellationToken
    )
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var fire = await _fires.FindAsync(action.FireId!.Value, cancellationToken: cancellationToken);
        if (fire == null || fire.Status != FlowScheduleFireStatus.Running)
        {
            return;
        }

        var status = action.ResultStatus ?? FlowScheduleFireStatus.Dead;
        fire.CompleteAttempt(
            success: false,
            instanceId: null,
            error: "执行超过 15 分钟仍未结束，按失败处理。",
            nextStatus: status,
            nextRetryAt: action.NextRetryAt,
            utcNow: utcNow
        );
        await _fires.UpdateAsync(fire, autoSave: true, cancellationToken: cancellationToken);
        var schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
        schedule.NoteFireProgress(status, fire.Error, dead: status == FlowScheduleFireStatus.Dead);
        await _schedules.UpdateAsync(schedule, autoSave: true, cancellationToken: cancellationToken);
        await uow.CompleteAsync(cancellationToken);
    }

    private async Task StartRetryAsync(
        Guid scheduleId,
        Guid fireId,
        DateTime utcNow,
        CancellationToken cancellationToken
    )
    {
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var fire = await _fires.FindAsync(fireId, cancellationToken: cancellationToken);
            if (fire == null || fire.Status != FlowScheduleFireStatus.RetryWait)
            {
                return;
            }

            fire.OpenRetryAttempt(utcNow);
            await _fires.UpdateAsync(fire, autoSave: true, cancellationToken: cancellationToken);
            var schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
            schedule.NoteFireProgress(FlowScheduleFireStatus.Running, error: null, dead: false);
            await _schedules.UpdateAsync(schedule, autoSave: true, cancellationToken: cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        await InvokeAndFinishAsync(scheduleId, fireId, cancellationToken);
    }

    private async Task AcceptCronAsync(
        Guid scheduleId,
        DateTime nominal,
        DateTime utcNow,
        bool skip,
        CancellationToken cancellationToken
    )
    {
        var schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
        var existed = await _fires.FirstOrDefaultAsync(
            x => x.ScheduleId == schedule.Id &&
                 x.TriggerKind == FlowScheduleTriggerKind.Cron &&
                 x.NominalTimeUtc == nominal,
            cancellationToken
        );
        if (existed != null)
        {
            await AdvanceOnlyAsync(schedule.Id, utcNow, cancellationToken);
            return;
        }

        var fire = await InsertFireAsync(
            schedule,
            FlowScheduleTriggerKind.Cron,
            nominal,
            utcNow,
            skipped: skip,
            advanceCron: true,
            cancellationToken
        );
        if (!skip)
        {
            await InvokeAndFinishAsync(schedule.Id, fire.Id, cancellationToken);
        }
    }

    private async Task<FlowScheduleFire> InsertFireAsync(
        FlowSchedule schedule,
        FlowScheduleTriggerKind kind,
        DateTime nominal,
        DateTime utcNow,
        bool skipped,
        bool advanceCron,
        CancellationToken cancellationToken
    )
    {
        using var tenant = _currentTenant.Change(schedule.TenantId);
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var current = await _schedules.GetAsync(schedule.Id, cancellationToken: cancellationToken);
        var fire = new FlowScheduleFire(
            _guidGenerator.Create(),
            current.Id,
            current.Code,
            current.FlowKey,
            kind,
            nominal,
            current.EffectiveMaxAttempts,
            utcNow,
            current.TenantId,
            skipped
        );
        await _fires.InsertAsync(fire, autoSave: true, cancellationToken: cancellationToken);
        if (advanceCron)
        {
            current.NoteAcceptedFire(
                skipped ? FlowScheduleFireStatus.Skipped : FlowScheduleFireStatus.Running,
                utcNow,
                NextAfter(current, utcNow)
            );
        }
        else
        {
            current.NoteFireProgress(
                skipped ? FlowScheduleFireStatus.Skipped : FlowScheduleFireStatus.Running,
                error: null,
                dead: false
            );
        }

        await _schedules.UpdateAsync(current, autoSave: true, cancellationToken: cancellationToken);
        await TrimAsync(current.Id, cancellationToken);
        await uow.CompleteAsync(cancellationToken);
        return fire;
    }

    private async Task<LogicRunResultDto> InvokeAndFinishAsync(
        Guid scheduleId,
        Guid fireId,
        CancellationToken cancellationToken
    )
    {
        FlowSchedule schedule;
        FlowScheduleFire fire;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
            fire = await _fires.GetAsync(fireId, cancellationToken: cancellationToken);
        }

        PublishedFlowInvokeResult? result = null;
        Exception? thrown = null;
        try
        {
            result = await _invoker.InvokeAsync(
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
            thrown = ex;
            _logger.LogError(ex, "Schedule invoke failed. Code={Code} FlowKey={FlowKey}", schedule.Code, schedule.FlowKey);
        }

        var success = thrown == null && result is { Success: true };
        var error = thrown?.Message ?? result?.Error;
        var instanceId = result?.InstanceId ?? Guid.Empty;
        using (_dataFilter.Disable<IMultiTenant>())
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            fire = await _fires.GetAsync(fireId, cancellationToken: cancellationToken);
            schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
            var (status, nextRetry) = success
                ? (FlowScheduleFireStatus.Succeeded, (DateTime?)null)
                : FlowScheduleTickPlanner.DecideFailure(
                    fire.Attempt,
                    fire.MaxAttempts,
                    schedule.RetryDelaysSeconds,
                    DateTime.UtcNow
                );
            fire.CompleteAttempt(success, instanceId, error, status, nextRetry, DateTime.UtcNow);
            await _fires.UpdateAsync(fire, autoSave: true, cancellationToken: cancellationToken);
            schedule.NoteFireProgress(status, fire.Error, dead: status == FlowScheduleFireStatus.Dead);
            await _schedules.UpdateAsync(schedule, autoSave: true, cancellationToken: cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        return new LogicRunResultDto
        {
            Success = success,
            InstanceId = instanceId,
            Error = error,
            DataJson = result?.DataJson,
            Meta = new LogicRunMetaDto
            {
                FlowKey = result?.FlowKey ?? schedule.FlowKey,
                Version = result?.Version ?? 0,
                ExecuteMs = result?.ExecuteMs ?? 0,
                TotalMs = result?.ExecuteMs ?? 0
            }
        };
    }

    private async Task AdvanceOnlyAsync(Guid scheduleId, DateTime utcNow, CancellationToken cancellationToken)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var schedule = await _schedules.GetAsync(scheduleId, cancellationToken: cancellationToken);
        schedule.SetNextFireAt(NextAfter(schedule, utcNow));
        schedule.MarkFired(utcNow, schedule.NextFireAt);
        await _schedules.UpdateAsync(schedule, autoSave: true, cancellationToken: cancellationToken);
        await uow.CompleteAsync(cancellationToken);
    }

    private async Task TrimAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var rows = await _fires.GetListAsync(x => x.ScheduleId == scheduleId, cancellationToken: cancellationToken);
        var extra = rows.Count - OrchestrationConsts.ScheduleFireRetention;
        if (extra <= 0)
        {
            return;
        }

        var drop = rows
            .Where(x => x.Status is FlowScheduleFireStatus.Succeeded or FlowScheduleFireStatus.Dead or FlowScheduleFireStatus.Skipped)
            .OrderBy(x => x.StartedAt)
            .Take(extra)
            .ToList();
        foreach (var row in drop)
        {
            await _fires.DeleteAsync(row, autoSave: true, cancellationToken: cancellationToken);
        }
    }

    private static (bool Due, DateTime Nominal) CronDue(FlowSchedule schedule, DateTime utcNow)
    {
        if (!schedule.IsEnabled)
        {
            return (false, utcNow);
        }

        if (schedule.NextFireAt.HasValue)
        {
            var nominal = DateTime.SpecifyKind(schedule.NextFireAt.Value, DateTimeKind.Utc);
            return (nominal <= utcNow, nominal);
        }

        var next = FlowScheduleAppService.ComputeNext(schedule.Cron, schedule.TimeZone, utcNow.AddMinutes(-1));
        var due = next.HasValue && next.Value <= utcNow;
        return (due, utcNow);
    }

    private static DateTime? NextAfter(FlowSchedule schedule, DateTime utcNow)
    {
        var next = FlowScheduleAppService.ComputeNext(schedule.Cron, schedule.TimeZone, utcNow);
        if (next.HasValue && next.Value <= utcNow)
        {
            next = FlowScheduleAppService.ComputeNext(schedule.Cron, schedule.TimeZone, utcNow.AddSeconds(1));
        }

        return next;
    }

    private static string LockName(string code) => $"meta-dow:flow-schedule:{code}";
}
