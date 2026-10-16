using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 定时触发：Cron 到点后按 flowKey 调用已发布逻辑。
/// </summary>
public class FlowSchedule : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    public string FlowKey { get; protected set; } = null!;

    /// <summary>标准 5/6 段 Cron（Cronos）</summary>
    public string Cron { get; protected set; } = null!;

    public string TimeZone { get; protected set; } = "UTC";

    /// <summary>固定请求体 JSON（映射到流程 InputSchema）</summary>
    public string? VariablesJson { get; protected set; }

    public bool IsEnabled { get; protected set; }

    public string? Description { get; protected set; }

    public DateTime? LastFiredAt { get; protected set; }

    public DateTime? NextFireAt { get; protected set; }

    /// <summary>一次触发最多调用几次，含第一次。缺省或 0 按 1。</summary>
    public int MaxAttempts { get; protected set; } = 1;

    /// <summary>第 2 次及以后之前的等待秒数。空则运行时用 60/300/900。</summary>
    public List<int>? RetryDelaysSeconds { get; protected set; }

    public FlowScheduleFireStatus? LastFireStatus { get; protected set; }

    public string? LastError { get; protected set; }

    public int ConsecutiveFailures { get; protected set; }

    protected FlowSchedule()
    {
    }

    public FlowSchedule(
        Guid id,
        string code,
        string name,
        string flowKey,
        string cron,
        string? timeZone = null,
        string? variablesJson = null,
        string? description = null,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetCode(code);
        SetName(name);
        SetFlowKey(flowKey);
        SetCron(cron);
        SetTimeZone(timeZone);
        VariablesJson = string.IsNullOrWhiteSpace(variablesJson) ? null : variablesJson.Trim();
        Description = description?.Trim();
        TenantId = tenantId;
        IsEnabled = true;
    }

    public void Update(
        string name,
        string flowKey,
        string cron,
        string? timeZone,
        string? variablesJson,
        bool isEnabled,
        string? description,
        int maxAttempts,
        IReadOnlyList<int>? retryDelaysSeconds
    )
    {
        SetName(name);
        SetFlowKey(flowKey);
        SetCron(cron);
        SetTimeZone(timeZone);
        VariablesJson = string.IsNullOrWhiteSpace(variablesJson) ? null : variablesJson.Trim();
        IsEnabled = isEnabled;
        Description = description?.Trim();
        SetRetryPolicy(maxAttempts, retryDelaysSeconds);
    }

    public void SetRetryPolicy(int maxAttempts, IReadOnlyList<int>? retryDelaysSeconds)
    {
        MaxAttempts = maxAttempts is >= OrchestrationConsts.ScheduleMinAttempts and <= OrchestrationConsts.ScheduleMaxAttempts
            ? maxAttempts
            : OrchestrationConsts.ScheduleMinAttempts;
        RetryDelaysSeconds = retryDelaysSeconds is { Count: > 0 }
            ? retryDelaysSeconds.ToList()
            : null;
    }

    public int EffectiveMaxAttempts =>
        MaxAttempts is >= OrchestrationConsts.ScheduleMinAttempts and <= OrchestrationConsts.ScheduleMaxAttempts
            ? MaxAttempts
            : OrchestrationConsts.ScheduleMinAttempts;

    /// <summary>接纳一次正点：推进水位，并记下进行中或跳过。不改连续失败次数。</summary>
    public void NoteAcceptedFire(FlowScheduleFireStatus status, DateTime firedAtUtc, DateTime? nextFireAtUtc)
    {
        MarkFired(firedAtUtc, nextFireAtUtc);
        LastFireStatus = status;
    }

    public void NoteFireProgress(FlowScheduleFireStatus status, string? error, bool dead)
    {
        LastFireStatus = status;
        if (status == FlowScheduleFireStatus.Succeeded)
        {
            LastError = null;
            ConsecutiveFailures = 0;
            return;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            var text = error.Trim();
            LastError = text.Length <= OrchestrationConsts.ScheduleFireErrorMaxLength
                ? text
                : text[..OrchestrationConsts.ScheduleFireErrorMaxLength];
        }

        if (dead)
        {
            ConsecutiveFailures++;
        }
    }

    public void MarkFired(DateTime firedAtUtc, DateTime? nextFireAtUtc)
    {
        LastFiredAt = firedAtUtc;
        NextFireAt = nextFireAtUtc;
    }

    public void SetNextFireAt(DateTime? nextFireAtUtc)
    {
        NextFireAt = nextFireAtUtc;
    }

    private void SetCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), OrchestrationConsts.MaxScheduleCodeLength);
    }

    private void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrchestrationConsts.MaxNameLength);
    }

    private void SetFlowKey(string flowKey)
    {
        FlowKey = Check.NotNullOrWhiteSpace(flowKey, nameof(flowKey), OrchestrationConsts.MaxCodeLength);
    }

    private void SetCron(string cron)
    {
        Cron = Check.NotNullOrWhiteSpace(cron, nameof(cron), OrchestrationConsts.MaxCronLength);
    }

    private void SetTimeZone(string? timeZone)
    {
        TimeZone = string.IsNullOrWhiteSpace(timeZone)
            ? "UTC"
            : Check.NotNullOrWhiteSpace(timeZone, nameof(timeZone), OrchestrationConsts.MaxTimeZoneLength);
    }
}
