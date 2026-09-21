using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 订阅外部 Kafka Topic：消息 value（UTF-8）作为已发布 flowKey 入参 JSON。
/// Trigger.Queue = Topic；Trigger.RoutingKey = ConsumerGroup（空则 orch-{code}）。
/// </summary>
public class KafkaMessageTriggerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaMessageTriggerHostedService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _slots = new(
        StringComparer.OrdinalIgnoreCase
    );

    public KafkaMessageTriggerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaMessageTriggerHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kafka message trigger host started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync Kafka message triggers.");
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
            StopSlot(key);
        }
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        List<(FlowTrigger Trigger, MessageSource Source)> desired;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dataFilter = scope.ServiceProvider.GetRequiredService<IDataFilter>();
            var triggerRepo = scope.ServiceProvider.GetRequiredService<IRepository<FlowTrigger, Guid>>();
            var sourceRepo = scope.ServiceProvider.GetRequiredService<IRepository<MessageSource, Guid>>();

            using (dataFilter.Disable<IMultiTenant>())
            {
                var triggers = await triggerRepo.GetListAsync(
                    x => x.IsEnabled && x.TriggerType == FlowTriggerType.Message,
                    cancellationToken: cancellationToken
                );
                var sources = (await sourceRepo.GetListAsync(
                        x => x.IsEnabled,
                        cancellationToken: cancellationToken
                    ))
                    .Where(x =>
                        string.Equals(x.Provider, MessageSourceProvider.Kafka, StringComparison.OrdinalIgnoreCase)
                    )
                    .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

                desired = triggers
                    .Where(t => sources.ContainsKey(t.MessageSourceCode))
                    .Select(t => (t, sources[t.MessageSourceCode]))
                    .ToList();
            }
        }

        var keys = desired.Select(SlotKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in _slots.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            StopSlot(existing);
        }

        foreach (var bind in desired)
        {
            var key = SlotKey(bind);
            if (_slots.ContainsKey(key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(bind.Source.ConnectionString))
            {
                _logger.LogWarning(
                    "Kafka source {Code} has empty connection string; skip trigger {Trigger}",
                    bind.Source.Code,
                    bind.Trigger.Code
                );
                continue;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!_slots.TryAdd(key, cts))
            {
                cts.Dispose();
                continue;
            }

            _ = Task.Run(() => RunConsumerAsync(bind.Trigger, bind.Source, cts.Token), cts.Token);
            _logger.LogInformation(
                "Kafka trigger listening. Trigger={Code} Topic={Topic} FlowKey={FlowKey}",
                bind.Trigger.Code,
                bind.Trigger.Queue,
                bind.Trigger.FlowKey
            );
        }
    }

    private async Task RunConsumerAsync(
        FlowTrigger trigger,
        MessageSource source,
        CancellationToken cancellationToken
    )
    {
        var topic = trigger.Queue.Trim();
        var group = string.IsNullOrWhiteSpace(trigger.RoutingKey)
            ? $"orch-{trigger.Code}"
            : trigger.RoutingKey!.Trim();

        try
        {
            var config = BuildConsumerConfig(source.ConnectionString!, group);
            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe(topic);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var cr = consumer.Consume(cancellationToken);
                    if (cr?.Message?.Value == null)
                    {
                        continue;
                    }

                    var body = string.IsNullOrWhiteSpace(cr.Message.Value) ? "{}" : cr.Message.Value;
                    using var scope = _scopeFactory.CreateScope();
                    var invoker = scope.ServiceProvider.GetRequiredService<IPublishedFlowInvoker>();
                    await invoker.InvokeAsync(
                        trigger.FlowKey,
                        body,
                        triggerSource: $"Message:{trigger.Code}",
                        tenantId: trigger.TenantId,
                        filterOutputsByRole: false,
                        cancellationToken: cancellationToken
                    );
                    consumer.Commit(cr);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error. Trigger={Code}", trigger.Code);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kafka trigger {Code} failed processing message", trigger.Code);
                }
            }

            consumer.Close();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Kafka consumer stopped. Trigger={Code}", trigger.Code);
        }
    }

    private static ConsumerConfig BuildConsumerConfig(string connectionString, string groupId)
    {
        var config = new ConsumerConfig
        {
            GroupId = groupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = false
        };

        var raw = connectionString.Trim();
        if (!raw.Contains('=', StringComparison.Ordinal))
        {
            config.BootstrapServers = raw;
            return config;
        }

        foreach (
            var part in raw.Split(
                [';', ' ', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = part[..idx].Trim();
            var value = part[(idx + 1)..].Trim();
            config.Set(key, value);
        }

        if (string.IsNullOrWhiteSpace(config.BootstrapServers))
        {
            throw new InvalidOperationException("Kafka connection requires bootstrap.servers.");
        }

        return config;
    }

    private void StopSlot(string key)
    {
        if (!_slots.TryRemove(key, out var cts))
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            /* ignore */
        }

        cts.Dispose();
        _logger.LogInformation("Stopped Kafka trigger consumer {Key}", key);
    }

    private static string SlotKey((FlowTrigger Trigger, MessageSource Source) bind) =>
        $"{bind.Trigger.TenantId:N}|{bind.Trigger.Code}|kafka|{bind.Trigger.Queue}";
}
