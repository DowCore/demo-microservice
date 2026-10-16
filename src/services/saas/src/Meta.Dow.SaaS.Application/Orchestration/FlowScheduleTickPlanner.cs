using System;
using System.Collections.Generic;
using System.Linq;

namespace Meta.Dow.SaaS.Orchestration;

internal enum FlowScheduleTickActionKind
{
    FailStale,
    StartRetry,
    StartCron,
    SkipCron
}

internal sealed class FlowScheduleOpenFire
{
    public Guid Id { get; init; }

    public FlowScheduleFireStatus Status { get; set; }

    public int Attempt { get; init; }

    public int MaxAttempts { get; init; }

    public DateTime? NextRetryAt { get; set; }

    public DateTime StartedAt { get; init; }
}

internal sealed class FlowScheduleTickRequest
{
    public DateTime UtcNow { get; init; }

    public bool IsEnabled { get; init; }

    public bool CronDue { get; init; }

    public int MaxAttempts { get; init; }

    public IReadOnlyList<int>? RetryDelaysSeconds { get; init; }

    public IReadOnlyList<FlowScheduleOpenFire> OpenFires { get; init; } = [];
}

internal sealed class FlowScheduleTickAction
{
    public FlowScheduleTickActionKind Kind { get; init; }

    public Guid? FireId { get; init; }

    public FlowScheduleFireStatus? ResultStatus { get; init; }

    public DateTime? NextRetryAt { get; init; }
}

/// <summary>
/// 给定未完成的触发记录和当前时间，决定这一轮是判僵死、重试、开一次正点，还是因重叠跳过。
/// </summary>
internal static class FlowScheduleTickPlanner
{
    public static readonly int[] DefaultDelays = [60, 300, 900];

    public static IReadOnlyList<int> Delays(IReadOnlyList<int>? configured) =>
        configured is { Count: > 0 } ? configured : DefaultDelays;

    public static int DelaySeconds(IReadOnlyList<int>? configured, int failedAttempt)
    {
        var delays = Delays(configured);
        var index = failedAttempt - 1;
        if (index < 0)
        {
            index = 0;
        }

        if (index >= delays.Count)
        {
            index = delays.Count - 1;
        }

        return delays[index];
    }

    public static (FlowScheduleFireStatus Status, DateTime? NextRetryAt) DecideFailure(
        int attempt,
        int maxAttempts,
        IReadOnlyList<int>? delays,
        DateTime utcNow
    )
    {
        var cap = maxAttempts < 1 ? 1 : maxAttempts;
        if (attempt < cap)
        {
            return (
                FlowScheduleFireStatus.RetryWait,
                utcNow.AddSeconds(DelaySeconds(delays, attempt))
            );
        }

        return (FlowScheduleFireStatus.Dead, null);
    }

    public static IReadOnlyList<FlowScheduleTickAction> Plan(FlowScheduleTickRequest request)
    {
        var now = request.UtcNow;
        var staleBefore = now.AddMinutes(-OrchestrationConsts.ScheduleFireStaleMinutes);
        var fires = request.OpenFires.Select(Clone).ToList();
        var actions = new List<FlowScheduleTickAction>();

        foreach (var fire in fires.Where(x => x.Status == FlowScheduleFireStatus.Running && x.StartedAt <= staleBefore).ToList())
        {
            var (status, nextRetry) = DecideFailure(fire.Attempt, fire.MaxAttempts, request.RetryDelaysSeconds, now);
            actions.Add(new FlowScheduleTickAction
            {
                Kind = FlowScheduleTickActionKind.FailStale,
                FireId = fire.Id,
                ResultStatus = status,
                NextRetryAt = nextRetry
            });
            fire.Status = status;
            fire.NextRetryAt = nextRetry;
        }

        var running = fires.Any(x => x.Status == FlowScheduleFireStatus.Running);
        var dueRetry = fires
            .Where(x => x.Status == FlowScheduleFireStatus.RetryWait && x.NextRetryAt.HasValue && x.NextRetryAt.Value <= now)
            .OrderBy(x => x.NextRetryAt)
            .FirstOrDefault();
        if (request.IsEnabled && !running && dueRetry != null)
        {
            actions.Add(new FlowScheduleTickAction
            {
                Kind = FlowScheduleTickActionKind.StartRetry,
                FireId = dueRetry.Id
            });
            dueRetry.Status = FlowScheduleFireStatus.Running;
        }

        if (request.IsEnabled && request.CronDue)
        {
            var blocked = fires.Any(x =>
                x.Status is FlowScheduleFireStatus.Running or FlowScheduleFireStatus.RetryWait
            );
            actions.Add(new FlowScheduleTickAction
            {
                Kind = blocked ? FlowScheduleTickActionKind.SkipCron : FlowScheduleTickActionKind.StartCron
            });
        }

        return actions;
    }

    private static FlowScheduleOpenFire Clone(FlowScheduleOpenFire fire) => new()
    {
        Id = fire.Id,
        Status = fire.Status,
        Attempt = fire.Attempt,
        MaxAttempts = fire.MaxAttempts,
        NextRetryAt = fire.NextRetryAt,
        StartedAt = fire.StartedAt
    };
}
