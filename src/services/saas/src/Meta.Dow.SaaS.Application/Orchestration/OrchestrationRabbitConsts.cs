namespace Meta.Dow.SaaS.Orchestration;

/// <summary>编排对外消息（与内部 EventBus 交换机 Meta.Dow 隔离）。</summary>
public static class OrchestrationRabbitConsts
{
    /// <summary>广播（fanout）：所有绑定队列均收到同一份 JSON。</summary>
    public const string BroadcastExchange = "Meta.Dow.Orchestration.Broadcast";

    /// <summary>主题路由（topic）：按 routingKey 匹配。</summary>
    public const string TopicExchange = "Meta.Dow.Orchestration";

    public const string DefaultExchangeType = "fanout";
}
