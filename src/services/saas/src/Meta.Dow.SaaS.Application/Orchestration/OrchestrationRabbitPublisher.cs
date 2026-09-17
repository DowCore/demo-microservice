using System.Text;
using System.Text.Json;
using Meta.Dow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public interface IOrchestrationRabbitPublisher
{
    Task PublishAsync(
        string exchange,
        string exchangeType,
        string routingKey,
        string jsonBody,
        bool persistent,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 使用 Aspire/宿主已配置的 RabbitMQ 连接串发布编排消息（AMQP）。
/// 广播请用 fanout 交换机 <see cref="OrchestrationRabbitConsts.BroadcastExchange"/>。
/// </summary>
public class OrchestrationRabbitPublisher : IOrchestrationRabbitPublisher, ITransientDependency
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrchestrationRabbitPublisher> _logger;

    public OrchestrationRabbitPublisher(
        IConfiguration configuration,
        ILogger<OrchestrationRabbitPublisher> logger
    )
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task PublishAsync(
        string exchange,
        string exchangeType,
        string routingKey,
        string jsonBody,
        bool persistent,
        CancellationToken cancellationToken = default
    )
    {
        var cstr = _configuration.GetConnectionString(MetaDowNames.RabbitMq);
        if (string.IsNullOrWhiteSpace(cstr))
        {
            throw new UserFriendlyException(
                "RabbitMQ 连接串未配置（ConnectionStrings:rabbitmq）。请通过 Aspire AppHost 启动。"
            );
        }

        var ex = string.IsNullOrWhiteSpace(exchange)
            ? OrchestrationRabbitConsts.BroadcastExchange
            : exchange.Trim();
        var type = NormalizeExchangeType(exchangeType);
        var key = type == ExchangeType.Fanout
            ? string.Empty
            : (routingKey ?? string.Empty);

        var factory = new ConnectionFactory { Uri = new Uri(cstr) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: ex,
            type: type,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );

        var body = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
        var props = new BasicProperties
        {
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            DeliveryMode = persistent ? DeliveryModes.Persistent : DeliveryModes.Transient,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            MessageId = Guid.NewGuid().ToString("N"),
            Headers = new Dictionary<string, object?>
            {
                ["x-meta-dow-source"] = "orchestration"
            }
        };

        await channel.BasicPublishAsync(
            exchange: ex,
            routingKey: key,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken
        );

        _logger.LogInformation(
            "Orchestration Rabbit published. Exchange={Exchange} Type={Type} RoutingKey={RoutingKey} Bytes={Bytes}",
            ex,
            type,
            key,
            body.Length
        );
    }

    private static string NormalizeExchangeType(string? exchangeType)
    {
        var t = (exchangeType ?? OrchestrationRabbitConsts.DefaultExchangeType).Trim().ToLowerInvariant();
        return t switch
        {
            "topic" => ExchangeType.Topic,
            "direct" => ExchangeType.Direct,
            "headers" => ExchangeType.Headers,
            _ => ExchangeType.Fanout
        };
    }
}
