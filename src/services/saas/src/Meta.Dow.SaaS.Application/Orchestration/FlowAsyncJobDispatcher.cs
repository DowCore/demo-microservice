using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 异步外呼作业调度：登记 → 执行 → 回写结果与失败。
///
/// <para><b>取代什么</b>：原 <c>FlowExecutor</c> 里的
/// <c>_ = Task.Run(async () => { … }, CancellationToken.None)</c>。
/// 那段代码有四个不可接受的性质：结果丢失、失败仅日志、进程重启即丢、
/// 不受节点超时约束。本类把作业登记为持久化实体，再由受控执行器处理。</para>
///
/// <para><b>为什么不在 FlowExecutor 里直接写库</b>：FlowExecutor 是瞬时依赖，
/// 执行期不一定处于可写 UoW 中；且异步作业的生命周期必须长于创建它的那次请求。
/// 拆成独立服务后，作业状态对运维可见、可查询、可补偿。</para>
/// </summary>
public class FlowAsyncJobDispatcher : ITransientDependency
{
    private readonly IRepository<FlowAsyncJob, Guid> _jobRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FlowAsyncJobDispatcher> _logger;
    private readonly IGuidGenerator _guidGenerator;

    public FlowAsyncJobDispatcher(
        IRepository<FlowAsyncJob, Guid> jobRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<FlowAsyncJobDispatcher> logger,
        IGuidGenerator guidGenerator
    )
    {
        _jobRepository = jobRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _guidGenerator = guidGenerator;
    }

    /// <summary>
    /// 登记一个异步作业。调用方拿到 JobId 写进节点执行记录，
    /// 下游的 Wait 节点据此等待。
    /// </summary>
    public async Task<Guid> EnqueueAsync(
        Guid instanceId,
        string nodeId,
        string flowKey,
        string method,
        string url,
        string? requestBody,
        Guid? tenantId,
        CancellationToken cancellationToken = default
    )
    {
        var job = new FlowAsyncJob(
            _guidGenerator.Create(),
            instanceId,
            nodeId,
            flowKey,
            method,
            url,
            requestBody,
            tenantId
        );

        await _jobRepository.InsertAsync(job, autoSave: true, cancellationToken);
        return job.Id;
    }

    /// <summary>
    /// 取出当前排队中的作业。按创建顺序排序，保证先来先执行。
    /// 由 <c>FlowAsyncJobWorker</c> 轮询调用。
    ///
    /// <para>排序键用 Id 而不是 <c>CreationTime</c>：本实体继承 <c>AggregateRoot&lt;Guid&gt;</c>
    /// 而非 <c>FullAuditedAggregateRoot</c>，没有 CreationTime 字段。
    /// Guid v7 本身单调递增，因此按 Id 排序等价于按创建时间排序。</para>
    /// </summary>
    public async Task<List<FlowAsyncJob>> GetQueuedJobsAsync()
    {
        var query = await _jobRepository.GetQueryableAsync();

        return query
            .Where(x => x.Status == FlowAsyncJobStatus.Queued)
            .OrderBy(x => x.Id)
            .ToList();
    }

    /// <summary>
    /// 执行已登记的作业，并把成功或失败<b>写回实体</b>。
    ///
    /// <para>失败不再被吞掉：<c>MarkFailedAsync</c> 会落库，
    /// 因此作业状态、失败原因、重试次数都可事后审计。</para>
    /// </summary>
    public async Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetAsync(jobId);
        job.MarkRunning();
        await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken);

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(job.Method.ToUpperInvariant()), job.Url);
            if (job.RequestBody != null && !request.Method.Equals(HttpMethod.Get))
            {
                request.Content = new StringContent(
                    job.RequestBody,
                    System.Text.Encoding.UTF8,
                    System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json")
                );
            }

            var client = _httpClientFactory.CreateClient("OrchestrationHttpCall");
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            job.MarkSucceeded((int)response.StatusCode, body);
            await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken);

            _logger.LogInformation(
                "Async job {JobId} for node {NodeId} completed with status {Status}.",
                job.Id,
                job.NodeId,
                (int)response.StatusCode
            );
        }
        catch (Exception ex)
        {
            // C1 的核心修复：失败必须落库。原实现在这里只 LogWarning，
            // 于是"外呼失败"与"实例成功"并存，排障时完全无从判断。
            await MarkFailedAsync(jobId, ex, cancellationToken);
        }
    }

    private async Task MarkFailedAsync(Guid jobId, Exception ex, CancellationToken cancellationToken)
    {
        try
        {
            var job = await _jobRepository.GetAsync(jobId);
            job.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
            await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken);

            _logger.LogError(
                ex,
                "Async job {JobId} for node {NodeId} failed and was recorded.",
                jobId,
                job.NodeId
            );
        }
        catch (Exception persistEx)
        {
            // 连失败都写不进去时，必须留下 Error 级日志：
            // 静默吞掉会让作业既无结果也无原因，彻底不可观测。
            _logger.LogError(
                persistEx,
                "Async job {JobId} failed AND the failure could not be persisted.",
                jobId
            );
        }
    }

    /// <summary>
    /// 等待作业进入终态，超时抛 <see cref="FlowAsyncJobTimeoutException"/>。
    ///
    /// <para>超时用<b>专用异常类型</b>而不是通用 <c>UserFriendlyException</c>：
    /// 上层需要区分「作业失败了」和「等不到结果」这两种语义，
    /// 用通用异常只能靠字符串匹配来分辨，这是不可维护的。</para>
    /// </summary>
    public async Task<FlowAsyncJob> WaitForCompletionAsync(
        Guid jobId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            var job = await TryGetAsync(jobId, cancellationToken)
                       ?? throw new UserFriendlyException(
                           $"Wait 节点引用的异步作业 {jobId} 不存在，可能已被清理或从未成功登记。"
                       );

            if (job.Status is FlowAsyncJobStatus.Succeeded or FlowAsyncJobStatus.Failed or FlowAsyncJobStatus.Canceled)
            {
                return job;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new FlowAsyncJobTimeoutException(jobId, timeout, job.Status);
            }

            // 剩余时间不足一个轮询周期时按剩余时间睡，避免超时点被轮询间隔顶过去。
            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(200) ? remaining : TimeSpan.FromMilliseconds(200),
                cancellationToken
            );
        }
    }

    /// <summary>
    /// 取作业快照；不存在时返回 <c>null</c> 而不是抛异常。
    /// 超时路径需要「查不到就降级」而不是中断。
    /// </summary>
    public async Task<FlowAsyncJob?> TryGetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _jobRepository.FindAsync(jobId, cancellationToken: cancellationToken);
    }
}

/// <summary>等待异步作业超时。专用类型，便于上层区分「失败」与「等不到」。</summary>
public class FlowAsyncJobTimeoutException : Exception
{
    public Guid JobId { get; }
    public TimeSpan Timeout { get; }
    public FlowAsyncJobStatus LastStatus { get; }

    public FlowAsyncJobTimeoutException(Guid jobId, TimeSpan timeout, FlowAsyncJobStatus lastStatus)
        : base($"Async job {jobId} did not complete within {timeout.TotalSeconds:0.###}s (last status: {lastStatus}).")
    {
        JobId = jobId;
        Timeout = timeout;
        LastStatus = lastStatus;
    }
}
