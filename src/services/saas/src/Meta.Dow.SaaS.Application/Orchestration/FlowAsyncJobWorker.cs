using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 异步作业的落盘执行器：把排队状态的作业真正跑掉。
///
/// <para><b>为什么需要它</b>：C1 修复把作业<b>持久化</b>了，但作业本身仍需有人执行。
/// 若只依赖创建它的那次请求内的后台任务，进程重启后队列里的作业就再无人问津——
/// 这正是原实现（进程内 <c>Task.Run</c>）的根本缺陷。
/// 「持久化 + 独立轮询 worker」组合起来，作业才真正跨重启不丢。</para>
///
/// <para><b>为什么不用构造函数注入仓储</b>：worker 是单例，而 ABP 的仓储是作用域限定的。
/// 这里每次迭代开一个 scope 解析 <see cref="FlowAsyncJobDispatcher"/>，
/// 由它持有仓储——职责边界与作用域都正确。</para>
///
/// <para><b>存活纪律</b>：轮询循环必须活下去。worker 崩溃等于所有异步作业永久滞留，
/// 因此任何异常都只记录并退避，绝不退出循环。</para>
/// </summary>
public class FlowAsyncJobWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FlowAsyncJobWorker> _logger;

    public FlowAsyncJobWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FlowAsyncJobWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Flow async job worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainOnceAsync(stoppingToken);

                // 没有待办时退避，避免空转打满数据库。
                if (processed == 0)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Flow async job worker iteration failed; retrying.");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }

        _logger.LogInformation("Flow async job worker stopped.");
    }

    /// <summary>取一批排队作业并执行，返回本轮处理条数。</summary>
    private async Task<int> DrainOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<FlowAsyncJobDispatcher>();

        var pending = await dispatcher.GetQueuedJobsAsync();

        foreach (var job in pending)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                await dispatcher.ExecuteAsync(job.Id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // dispatcher 内部已落库失败原因；这里只兜住它自身崩溃的情况。
                _logger.LogError(ex, "Async job {JobId} could not be dispatched.", job.Id);
            }
        }

        return pending.Count;
    }
}
