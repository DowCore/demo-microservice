using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>OA 抄送通知派发。</summary>
public interface IWorkflowNotifier
{
    Task NotifyCcAsync(
        Guid instanceId,
        string workflowCode,
        string? formRef,
        string nodeId,
        string nodeName,
        string recordJson,
        IReadOnlyList<string> users,
        IReadOnlyList<string> roles,
        Guid? tenantId
    );
}

/// <summary>
/// 抄送节点通知。经编排广播交换机（fanout）派发 <c>workflow.cc</c> 消息，
/// 由下游通知消费者转成站内信 / 邮件等。发送失败只告警，不阻断流程。
/// </summary>
public class WorkflowNotifier : IWorkflowNotifier, ITransientDependency
{
    private readonly IOrchestrationRabbitPublisher _publisher;
    private readonly ILogger<WorkflowNotifier> _logger;

    public WorkflowNotifier(IOrchestrationRabbitPublisher publisher, ILogger<WorkflowNotifier> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task NotifyCcAsync(
        Guid instanceId,
        string workflowCode,
        string? formRef,
        string nodeId,
        string nodeName,
        string recordJson,
        IReadOnlyList<string> users,
        IReadOnlyList<string> roles,
        Guid? tenantId
    )
    {
        if ((users == null || users.Count == 0) && (roles == null || roles.Count == 0))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(
            new
            {
                type = "workflow.cc",
                instanceId,
                workflowCode,
                formRef,
                nodeId,
                nodeName,
                recordJson,
                users,
                roles,
                tenantId,
                time = DateTime.UtcNow,
            }
        );

        try
        {
            await _publisher.PublishAsync(
                OrchestrationRabbitConsts.BroadcastExchange,
                OrchestrationRabbitConsts.DefaultExchangeType,
                "workflow.cc",
                payload,
                persistent: true
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "抄送通知发布失败（不阻断流程）。Instance={InstanceId} Node={NodeId}",
                instanceId,
                nodeId
            );
        }
    }
}
