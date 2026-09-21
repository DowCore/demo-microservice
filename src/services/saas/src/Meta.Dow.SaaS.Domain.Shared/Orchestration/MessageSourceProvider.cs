using System;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>消息连接协议（入站订阅用）。</summary>
public static class MessageSourceProvider
{
    public const string Rabbit = "rabbit";
    public const string Kafka = "kafka";
    public const string Mqtt = "mqtt";

    public static readonly string[] All = [Rabbit, Kafka, Mqtt];

    public static string DisplayName(string provider) =>
        (provider ?? "").Trim().ToLowerInvariant() switch
        {
            Rabbit => "RabbitMQ",
            Kafka => "Kafka",
            Mqtt => "MQTT",
            _ => provider ?? ""
        };

    public static string ConnectionHint(string provider) =>
        (provider ?? "").Trim().ToLowerInvariant() switch
        {
            Rabbit => "留空则使用平台 ConnectionStrings:rabbitmq；或 amqp://user:pass@host:5672",
            Kafka => "必填。例：localhost:9092 或 bootstrap.servers=host:9092;security.protocol=PLAINTEXT",
            Mqtt => "必填。例：tcp://host:1883 或 Host=host;Port=1883;Username=u;Password=p;ClientId=orch",
            _ => ""
        };

    /// <summary>三种协议均已支持消费。</summary>
    public static bool IsSupportedForConsume(string provider)
    {
        var p = (provider ?? "").Trim().ToLowerInvariant();
        return p is Rabbit or Kafka or Mqtt;
    }

    public static bool RequiresConnectionString(string provider) =>
        !string.Equals(provider, Rabbit, StringComparison.OrdinalIgnoreCase);
}
