using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 消息触发器：订阅 MessageSource → 调用已发布 flowKey（不扩展编排画布）。
/// </summary>
public class FlowTrigger : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    public FlowTriggerType TriggerType { get; protected set; }

    /// <summary>目标已发布逻辑编码</summary>
    public string FlowKey { get; protected set; } = null!;

    public string MessageSourceCode { get; protected set; } = null!;

    /// <summary>消费队列名（Rabbit 必填）</summary>
    public string Queue { get; protected set; } = null!;

    /// <summary>绑定交换机；空则用平台广播交换机</summary>
    public string? Exchange { get; protected set; }

    public string? ExchangeType { get; protected set; }

    public string? RoutingKey { get; protected set; }

    public bool IsEnabled { get; protected set; }

    public string? Description { get; protected set; }

    protected FlowTrigger()
    {
    }

    public FlowTrigger(
        Guid id,
        string code,
        string name,
        string flowKey,
        string messageSourceCode,
        string queue,
        string? exchange = null,
        string? exchangeType = null,
        string? routingKey = null,
        string? description = null,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetCode(code);
        SetName(name);
        SetFlowKey(flowKey);
        SetMessageSourceCode(messageSourceCode);
        SetQueue(queue);
        Exchange = string.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim();
        ExchangeType = string.IsNullOrWhiteSpace(exchangeType) ? "fanout" : exchangeType.Trim();
        RoutingKey = string.IsNullOrWhiteSpace(routingKey) ? null : routingKey.Trim();
        Description = description?.Trim();
        TriggerType = FlowTriggerType.Message;
        TenantId = tenantId;
        IsEnabled = true;
    }

    public void Update(
        string name,
        string flowKey,
        string messageSourceCode,
        string queue,
        bool isEnabled,
        string? exchange,
        string? exchangeType,
        string? routingKey,
        string? description
    )
    {
        SetName(name);
        SetFlowKey(flowKey);
        SetMessageSourceCode(messageSourceCode);
        SetQueue(queue);
        IsEnabled = isEnabled;
        Exchange = string.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim();
        ExchangeType = string.IsNullOrWhiteSpace(exchangeType) ? "fanout" : exchangeType.Trim();
        RoutingKey = string.IsNullOrWhiteSpace(routingKey) ? null : routingKey.Trim();
        Description = description?.Trim();
    }

    private void SetCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), OrchestrationConsts.MaxTriggerCodeLength);
    }

    private void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrchestrationConsts.MaxNameLength);
    }

    private void SetFlowKey(string flowKey)
    {
        FlowKey = Check.NotNullOrWhiteSpace(flowKey, nameof(flowKey), OrchestrationConsts.MaxCodeLength);
    }

    private void SetMessageSourceCode(string code)
    {
        MessageSourceCode = Check.NotNullOrWhiteSpace(
            code,
            nameof(code),
            OrchestrationConsts.MaxMessageSourceCodeLength
        );
    }

    private void SetQueue(string queue)
    {
        Queue = Check.NotNullOrWhiteSpace(queue, nameof(queue), 128);
    }
}
