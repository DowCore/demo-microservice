using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.Orchestration;

public class PublishedFlowInvokeResult
{
    public bool Success { get; set; }
    public Guid InstanceId { get; set; }
    public string? DataJson { get; set; }
    public string? Error { get; set; }
    public string FlowKey { get; set; } = null!;
    public int Version { get; set; }
    public int ExecuteMs { get; set; }
}

/// <summary>
/// 按已发布 flowKey 执行并落库实例（Http / 消息 / 定时共用）。
/// </summary>
public interface IPublishedFlowInvoker
{
    Task<PublishedFlowInvokeResult> InvokeAsync(
        string flowKey,
        string? bodyJson,
        string? triggerSource = null,
        Guid? tenantId = null,
        bool filterOutputsByRole = false,
        CancellationToken cancellationToken = default
    );
}

public class PublishedFlowInvoker : IPublishedFlowInvoker, ITransientDependency
{
    private readonly PublishedFlowCache _publishedFlowCache;
    private readonly FlowExecutor _flowExecutor;
    private readonly IRepository<FlowInstance, Guid> _instanceRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<PublishedFlowInvoker> _logger;

    public PublishedFlowInvoker(
        PublishedFlowCache publishedFlowCache,
        FlowExecutor flowExecutor,
        IRepository<FlowInstance, Guid> instanceRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<PublishedFlowInvoker> logger
    )
    {
        _publishedFlowCache = publishedFlowCache;
        _flowExecutor = flowExecutor;
        _instanceRepository = instanceRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public async Task<PublishedFlowInvokeResult> InvokeAsync(
        string flowKey,
        string? bodyJson,
        string? triggerSource = null,
        Guid? tenantId = null,
        bool filterOutputsByRole = false,
        CancellationToken cancellationToken = default
    )
    {
        using (_currentTenant.Change(tenantId))
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var published = await _publishedFlowCache.GetByKeyAsync(flowKey);

            var instance = new FlowInstance(
                _guidGenerator.Create(),
                published.DefinitionId,
                published.DefinitionName,
                published.Version,
                isDryRun: false,
                bodyJson,
                tenantId ?? _currentTenant.Id,
                triggerSource
            );

            var sw = Stopwatch.StartNew();
            var execution = await _flowExecutor.ExecuteAsync(
                published.DslJson,
                bodyJson,
                isDryRun: false,
                filterOutputsByRole: filterOutputsByRole,
                currentUser: null,
                cancellationToken,
                skipValidation: true
            );
            sw.Stop();

            foreach (var node in execution.Nodes)
            {
                instance.AddNode(node);
            }

            if (execution.Succeeded)
            {
                instance.Succeed(execution.VariablesJson);
            }
            else
            {
                instance.Fail(execution.Error ?? "Failed", execution.VariablesJson);
            }

            await _instanceRepository.InsertAsync(instance, autoSave: true);
            await uow.CompleteAsync(cancellationToken);

            if (!execution.Succeeded)
            {
                _logger.LogWarning(
                    "Published flow invoke failed. FlowKey={FlowKey} Trigger={Trigger} Error={Error}",
                    flowKey,
                    triggerSource,
                    execution.Error
                );
            }

            return new PublishedFlowInvokeResult
            {
                Success = execution.Succeeded,
                InstanceId = instance.Id,
                DataJson = execution.OutputDataJson,
                Error = execution.Error,
                FlowKey = published.FlowKey,
                Version = published.Version,
                ExecuteMs = execution.ExecuteMs > 0 ? execution.ExecuteMs : (int)sw.ElapsedMilliseconds
            };
        }
    }
}
