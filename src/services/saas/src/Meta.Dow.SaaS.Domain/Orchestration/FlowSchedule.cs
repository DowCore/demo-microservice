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
        string? description
    )
    {
        SetName(name);
        SetFlowKey(flowKey);
        SetCron(cron);
        SetTimeZone(timeZone);
        VariablesJson = string.IsNullOrWhiteSpace(variablesJson) ? null : variablesJson.Trim();
        IsEnabled = isEnabled;
        Description = description?.Trim();
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
