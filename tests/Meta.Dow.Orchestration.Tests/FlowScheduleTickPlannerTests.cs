using Meta.Dow.SaaS.Orchestration;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

public class FlowScheduleTickPlannerTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Due_and_idle_starts_one_cron()
    {
        var actions = FlowScheduleTickPlanner.Plan(Request(cronDue: true));

        Assert.Equal([FlowScheduleTickActionKind.StartCron], actions.Select(x => x.Kind).ToArray());
    }

    [Fact]
    public void Fresh_running_skips_the_due_cron()
    {
        var actions = FlowScheduleTickPlanner.Plan(Request(
            cronDue: true,
            open: [Fire(FlowScheduleFireStatus.Running, startedAt: Now.AddMinutes(-1))]
        ));

        Assert.Equal([FlowScheduleTickActionKind.SkipCron], actions.Select(x => x.Kind).ToArray());
    }

    [Fact]
    public void Due_retry_runs_and_the_cron_is_skipped()
    {
        var actions = FlowScheduleTickPlanner.Plan(Request(
            cronDue: true,
            open: [Fire(FlowScheduleFireStatus.RetryWait, nextRetryAt: Now.AddSeconds(-1))]
        ));

        Assert.Equal(
            [FlowScheduleTickActionKind.StartRetry, FlowScheduleTickActionKind.SkipCron],
            actions.Select(x => x.Kind).ToArray()
        );
    }

    [Fact]
    public void Future_retry_only_skips_the_cron()
    {
        var actions = FlowScheduleTickPlanner.Plan(Request(
            cronDue: true,
            open: [Fire(FlowScheduleFireStatus.RetryWait, nextRetryAt: Now.AddMinutes(2))]
        ));

        Assert.Equal([FlowScheduleTickActionKind.SkipCron], actions.Select(x => x.Kind).ToArray());
    }

    [Fact]
    public void Stale_running_with_attempts_left_waits_out_the_delay()
    {
        var actions = FlowScheduleTickPlanner.Plan(Request(
            cronDue: true,
            maxAttempts: 3,
            delays: [60],
            open: [Fire(FlowScheduleFireStatus.Running, attempt: 1, maxAttempts: 3, startedAt: Now.AddMinutes(-20))]
        ));

        Assert.Equal(FlowScheduleTickActionKind.FailStale, actions[0].Kind);
        Assert.Equal(FlowScheduleFireStatus.RetryWait, actions[0].ResultStatus);
        Assert.Equal(Now.AddSeconds(60), actions[0].NextRetryAt);
        Assert.Equal(FlowScheduleTickActionKind.SkipCron, actions[1].Kind);
        Assert.DoesNotContain(actions, x => x.Kind == FlowScheduleTickActionKind.StartRetry);
    }

    [Fact]
    public void Stale_running_at_max_attempts_dies_and_a_new_cron_starts()
    {
        var actions = FlowScheduleTickPlanner.Plan(Request(
            cronDue: true,
            open: [Fire(FlowScheduleFireStatus.Running, attempt: 1, maxAttempts: 1, startedAt: Now.AddMinutes(-20))]
        ));

        Assert.Equal(FlowScheduleFireStatus.Dead, actions[0].ResultStatus);
        Assert.Equal(FlowScheduleTickActionKind.StartCron, actions[1].Kind);
    }

    [Fact]
    public void Disabled_schedule_only_closes_a_stale_run()
    {
        var actions = FlowScheduleTickPlanner.Plan(Request(
            enabled: false,
            cronDue: true,
            open:
            [
                Fire(FlowScheduleFireStatus.Running, startedAt: Now.AddMinutes(-20)),
                Fire(FlowScheduleFireStatus.RetryWait, nextRetryAt: Now.AddSeconds(-5))
            ]
        ));

        Assert.Equal([FlowScheduleTickActionKind.FailStale], actions.Select(x => x.Kind).ToArray());
    }

    [Fact]
    public void Failure_under_the_cap_waits_and_reuses_the_last_delay()
    {
        var (status, next) = FlowScheduleTickPlanner.DecideFailure(2, 4, [30, 90], Now);

        Assert.Equal(FlowScheduleFireStatus.RetryWait, status);
        Assert.Equal(Now.AddSeconds(90), next);
    }

    [Fact]
    public void Failure_at_the_cap_is_dead()
    {
        var (status, next) = FlowScheduleTickPlanner.DecideFailure(1, 1, null, Now);

        Assert.Equal(FlowScheduleFireStatus.Dead, status);
        Assert.Null(next);
    }

    private static FlowScheduleTickRequest Request(
        bool cronDue = false,
        bool enabled = true,
        int maxAttempts = 1,
        int[]? delays = null,
        FlowScheduleOpenFire[]? open = null
    ) => new()
    {
        UtcNow = Now,
        IsEnabled = enabled,
        CronDue = cronDue,
        MaxAttempts = maxAttempts,
        RetryDelaysSeconds = delays,
        OpenFires = open ?? []
    };

    private static FlowScheduleOpenFire Fire(
        FlowScheduleFireStatus status,
        int attempt = 1,
        int maxAttempts = 3,
        DateTime? startedAt = null,
        DateTime? nextRetryAt = null
    ) => new()
    {
        Id = Guid.NewGuid(),
        Status = status,
        Attempt = attempt,
        MaxAttempts = maxAttempts,
        StartedAt = startedAt ?? Now,
        NextRetryAt = nextRetryAt
    };
}
