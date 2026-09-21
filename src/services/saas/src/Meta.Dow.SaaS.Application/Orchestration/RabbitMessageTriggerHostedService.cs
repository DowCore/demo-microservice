using System.Collections.Concurrent;
using System.Text;
using Meta.Dow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 按已启用的 FlowTrigger 订阅 Rabbit 队列，消息体 JSON 直接作为已发布 flowKey 的入参。
/// </summary>
public class RabbitMessageTriggerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMessageTriggerHostedService> _logger;
    private readonly ConcurrentDictionary<string, ConsumerSlot> _slots = new(StringComparer.OrdinalIgnoreCase);

    public RabbitMessageTriggerHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RabbitMessageTriggerHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rabbit message trigger host started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncConsumersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync Rabbit message triggers.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        foreach (var key in _slots.Keys.ToList())
        {
            await StopSlotAsync(key);
        }
    }

    private async Task SyncConsumersAsync(CancellationToken cancellationToken)
    {
        List<TriggerBind> desired;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();
            var triggerRepo = scope.ServiceProvider.GetRequiredService<IRepository<FlowTrigger, Guid>>();
            var sourceRepo = scope.ServiceProvider.GetRequiredService<IRepository<MessageSource, Guid>>();

            using (dataFilter.Disable<IMultiTenant>())
            {
                var triggers = (await triggerRepo.GetListAsync(x => x.IsEnabled, cancellationToken: cancellationToken))
                    .Where(x => x.TriggerType == FlowTriggerType.Message)
                    .ToList();
                var sources = (await sourceRepo.GetListAsync(x => x.IsEnabled, cancellationToken: cancellationToken))
                    .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

                desired = [];
                foreach (var t in triggers)
                {
                    if (!sources.TryGetValue(t.MessageSourceCode, out var src))
                    {
                        continue;
                    }

                    if (!MessageSourceProvider.IsSupportedForConsume(src.Provider))
                    {
                        continue;
                    }

                    desired.Add(new TriggerBind(t, src));
                }
            }
        }

        var desiredKeys = desired.Select(SlotKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in _slots.Keys.Where(k => !desiredKeys.Contains(k)).ToList())
        {
            await StopSlotAsync(existing);
        }

        foreach (var bind in desired)
        {
            var key = SlotKey(bind);
            if (_slots.ContainsKey(key))
            {
                continue;
            }

            try
            {
                await StartSlotAsync(bind, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to start consumer for trigger {Code} queue {Queue}",
                    bind.Trigger.Code,
                    bind.Trigger.Queue
                );
            }
        }
    }

    private async Task StartSlotAsync(TriggerBind bind, CancellationToken cancellationToken)
    {
        var cstr = ResolveConnection(bind.Source);
        if (string.IsNullOrWhiteSpace(cstr))
        {
            throw new InvalidOperationException("RabbitMQ connection string is empty.");
        }

        var exchange = string.IsNullOrWhiteSpace(bind.Trigger.Exchange)
            ? OrchestrationRabbitConsts.BroadcastExchange
            : bind.Trigger.Exchange!.Trim();
        var exchangeType = NormalizeExchangeType(bind.Trigger.ExchangeType);
        var routingKey = exchangeType == ExchangeType.Fanout
            ? string.Empty
            : (bind.Trigger.RoutingKey ?? string.Empty);
        var queue = bind.Trigger.Queue.Trim();

        var factory = new ConnectionFactory { Uri = new Uri(cstr) };
        var connection = await factory.CreateConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange,
            exchangeType,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.QueueBindAsync(
            queue,
            exchange,
            routingKey,
            arguments: null,
            cancellationToken: cancellationToken
        );
        await channel.BasicQosAsync(0, 5, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        var triggerCode = bind.Trigger.Code;
        var flowKey = bind.Trigger.FlowKey;
        var tenantId = bind.Trigger.TenantId;

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                if (string.IsNullOrWhiteSpace(body))
                {
                    body = "{}";
                }

                using var scope = _scopeFactory.CreateScope();
                var invoker = scope.ServiceProvider.GetRequiredService<IPublishedFlowInvoker>();
                await invoker.InvokeAsync(
                    flowKey,
                    body,
                    triggerSource: $"Message:{triggerCode}",
                    tenantId: tenantId,
                    filterOutputsByRole: false
                );
                await channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trigger {Code} failed processing message for {FlowKey}", triggerCode, flowKey);
                try
                {
                    await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: false);
                }
                catch
                {
                    /* ignore */
                }
            }
        };

        var tag = await channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken);
        _slots[SlotKey(bind)] = new ConsumerSlot(connection, channel, tag);
        _logger.LogInformation(
            "Rabbit trigger listening. Trigger={Code} Queue={Queue} Exchange={Exchange} FlowKey={FlowKey}",
            triggerCode,
            queue,
            exchange,
            flowKey
        );
    }

    private async Task StopSlotAsync(string key)
    {
        if (!_slots.TryRemove(key, out var slot))
        {
            return;
        }

        try
        {
            await slot.Channel.BasicCancelAsync(slot.ConsumerTag);
        }
        catch
        {
            /* ignore */
        }

        try
        {
            await slot.Channel.DisposeAsync();
        }
        catch
        {
            /* ignore */
        }

        try
        {
            await slot.Connection.DisposeAsync();
        }
        catch
        {
            /* ignore */
        }

        _logger.LogInformation("Stopped Rabbit trigger consumer {Key}", key);
    }

    private string? ResolveConnection(MessageSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.ConnectionString))
        {
            return source.ConnectionString.Trim();
        }

        return _configuration.GetConnectionString(MetaDowNames.RabbitMq);
    }

    private static string SlotKey(TriggerBind bind) =>
        $"{bind.Trigger.TenantId:N}|{bind.Trigger.Code}|{bind.Trigger.Queue}";

    private static string NormalizeExchangeType(string? exchangeType)
    {
        var t = (exchangeType ?? "fanout").Trim().ToLowerInvariant();
        return t switch
        {
            "topic" => ExchangeType.Topic,
            "direct" => ExchangeType.Direct,
            "headers" => ExchangeType.Headers,
            _ => ExchangeType.Fanout
        };
    }

    private sealed record TriggerBind(FlowTrigger Trigger, MessageSource Source);

    private sealed record ConsumerSlot(IConnection Connection, IChannel Channel, string ConsumerTag);
}
