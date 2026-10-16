using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 一次正点或一次手工执行。重试记在同一条的 <see cref="Attempts"/> 里，不另开名义时刻。
/// </summary>
public class FlowScheduleFire : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid ScheduleId { get; protected set; }

    public string ScheduleCode { get; protected set; } = null!;

    public string FlowKey { get; protected set; } = null!;

    public FlowScheduleTriggerKind TriggerKind { get; protected set; }

    public DateTime NominalTimeUtc { get; protected set; }

    public FlowScheduleFireStatus Status { get; protected set; }

    public int Attempt { get; protected set; }

    public int MaxAttempts { get; protected set; }

    public DateTime? NextRetryAt { get; protected set; }

    public DateTime StartedAt { get; protected set; }

    public DateTime? FinishedAt { get; protected set; }

    public string? Error { get; protected set; }

    public string? SkipReason { get; protected set; }

    public List<FlowScheduleFireAttempt> Attempts { get; protected set; } = [];

    protected FlowScheduleFire()
    {
    }

    public FlowScheduleFire(
        Guid id,
        Guid scheduleId,
        string scheduleCode,
        string flowKey,
        FlowScheduleTriggerKind triggerKind,
        DateTime nominalTimeUtc,
        int maxAttempts,
        DateTime startedAtUtc,
        Guid? tenantId,
        bool skipped
    )
        : base(id)
    {
        ScheduleId = scheduleId;
        ScheduleCode = scheduleCode;
        FlowKey = flowKey;
        TriggerKind = triggerKind;
        NominalTimeUtc = AsUtc(nominalTimeUtc);
        MaxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
        StartedAt = AsUtc(startedAtUtc);
        TenantId = tenantId;
        if (skipped)
        {
            Status = FlowScheduleFireStatus.Skipped;
            SkipReason = "Overlap";
            FinishedAt = StartedAt;
            return;
        }

        Status = FlowScheduleFireStatus.Running;
        Attempt = 1;
        Attempts.Add(new FlowScheduleFireAttempt { N = 1, StartedAt = StartedAt });
    }

    public void OpenRetryAttempt(DateTime utcNow)
    {
        Attempt++;
        Status = FlowScheduleFireStatus.Running;
        NextRetryAt = null;
        Attempts.Add(new FlowScheduleFireAttempt { N = Attempt, StartedAt = AsUtc(utcNow) });
    }

    public void CompleteAttempt(
        bool success,
        Guid? instanceId,
        string? error,
        FlowScheduleFireStatus nextStatus,
        DateTime? nextRetryAt,
        DateTime utcNow
    )
    {
        var finished = AsUtc(utcNow);
        var attempt = Attempts.LastOrDefault(x => x.N == Attempt);
        if (attempt != null)
        {
            attempt.Success = success;
            attempt.InstanceId = instanceId is { } id && id != Guid.Empty ? id : null;
            attempt.FinishedAt = finished;
            attempt.Error = Trim(error);
        }

        Status = nextStatus;
        Error = success ? null : Trim(error);
        NextRetryAt = nextStatus == FlowScheduleFireStatus.RetryWait ? AsUtc(nextRetryAt ?? finished) : null;
        if (nextStatus is FlowScheduleFireStatus.Succeeded or FlowScheduleFireStatus.Dead or FlowScheduleFireStatus.Skipped)
        {
            FinishedAt = finished;
        }
    }

    public void QueueAnotherTry(DateTime utcNow)
    {
        MaxAttempts = Math.Max(MaxAttempts, Attempt) + 1;
        Status = FlowScheduleFireStatus.RetryWait;
        NextRetryAt = AsUtc(utcNow);
        FinishedAt = null;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? Trim(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var text = error.Trim();
        return text.Length <= OrchestrationConsts.ScheduleFireErrorMaxLength
            ? text
            : text[..OrchestrationConsts.ScheduleFireErrorMaxLength];
    }
}

public class FlowScheduleFireAttempt
{
    public int N { get; set; }

    public Guid? InstanceId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public bool Success { get; set; }

    public string? Error { get; set; }
}
