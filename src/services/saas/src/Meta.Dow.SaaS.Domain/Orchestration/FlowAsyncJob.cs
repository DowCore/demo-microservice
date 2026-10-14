using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 异步外呼作业。取代原先"进程内 Task.Run + 失败仅日志"的 fire-and-forget。
///
/// <para><b>缺陷 C1 的机理</b>：原实现的异步 Http 节点是
/// <c>_ = Task.Run(async () => { … }, CancellationToken.None)</c>，
/// 于是产生四个后果：</para>
/// <list type="number">
/// <item>不写 outputs、不落 JobId ⇒ 结果无人可取；</item>
/// <item>失败只 <c>LogWarning</c> ⇒ 实例却已判 <c>Succeeded</c>，"承诺成功、实际失败"；</item>
/// <item>节点状态永停 <c>AsyncQueued</c>，无法区分"排队中"与"失败"；</item>
/// <item>进程重启即丢 ⇒ 宿主编排重启会让在途作业静默消失。</item>
/// </list>
///
/// <para><b>本实体的作用</b>：把"排队"变成<b>可持久化、可查询、可重试</b>的一等记录。
/// 执行结果与失败都写回本实体；流程实例通过 <see cref="JobId"/> 关联。
/// 这是 <c>Paused</c> / <c>ResumeFrom</c>（断点续跑）的前置条件——没有可寻址的作业记录，
/// 续跑无从谈起。</para>
/// </summary>
public class FlowAsyncJob : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>所属流程实例。实例先落库、作业随后更新，故此处存 InstanceId。</summary>
    public Guid InstanceId { get; private set; }

    /// <summary>所属节点。用于把作业结果对齐到具体节点。</summary>
    public string NodeId { get; private set; } = null!;

    public string FlowKey { get; private set; } = null!;

    public string Method { get; private set; } = null!;

    public string Url { get; private set; } = null!;

    public string? RequestBody { get; private set; }

    public FlowAsyncJobStatus Status { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public string? Error { get; private set; }

    /// <summary>已尝试次数。供重试策略与排障使用。</summary>
    public int AttemptCount { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    protected FlowAsyncJob()
    {
    }

    public FlowAsyncJob(
        Guid id,
        Guid instanceId,
        string nodeId,
        string flowKey,
        string method,
        string url,
        string? requestBody = null,
        Guid? tenantId = null
    )
        : base(id)
    {
        InstanceId = instanceId;
        NodeId = nodeId;
        FlowKey = flowKey;
        Method = method;
        Url = url;
        RequestBody = requestBody;
        TenantId = tenantId;
        Status = FlowAsyncJobStatus.Queued;
    }

    public void MarkRunning()
    {
        if (Status is FlowAsyncJobStatus.Succeeded or FlowAsyncJobStatus.Failed or FlowAsyncJobStatus.Canceled)
        {
            return;
        }

        Status = FlowAsyncJobStatus.Running;
        AttemptCount++;
    }

    public void MarkSucceeded(int responseStatusCode, string? responseBody)
    {
        Status = FlowAsyncJobStatus.Succeeded;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        Error = null;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = FlowAsyncJobStatus.Failed;
        Error = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        if (Status is FlowAsyncJobStatus.Succeeded or FlowAsyncJobStatus.Failed)
        {
            return;
        }

        Status = FlowAsyncJobStatus.Canceled;
        Error = reason ?? "Canceled.";
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public enum FlowAsyncJobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4
}
