using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 订阅外部 MQTT Topic：payload UTF-8 作为已发布 flowKey 入参 JSON。
/// Trigger.Queue = Topic（可用 +/# 通配）；Trigger.RoutingKey = QoS（0/1/2，默认 1）。
/// </summary>
public class MqttMessageTriggerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MqttMessageTriggerHostedService> _logger;
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);

    public MqttMessageTriggerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<MqttMessageTriggerHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MQTT message trigger host started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync MQTT message triggers.");
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
                        string.Equals(x.Provider, MessageSourceProvider.Mqtt, StringComparison.OrdinalIgnoreCase)
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
            await StopSlotAsync(existing);
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
                    "MQTT source {Code} has empty connection string; skip trigger {Trigger}",
                    bind.Source.Code,
                    bind.Trigger.Code
                );
                continue;
            }

            try
            {
                await StartSlotAsync(bind.Trigger, bind.Source, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to start MQTT consumer for trigger {Code}",
                    bind.Trigger.Code
                );
            }
        }
    }

    private async Task StartSlotAsync(
        FlowTrigger trigger,
        MessageSource source,
        CancellationToken cancellationToken
    )
    {
        var opts = ParseMqttOptions(source.ConnectionString!, trigger.Code);
        var factory = new MqttFactory();
        var client = factory.CreateMqttClient();
        var triggerCode = trigger.Code;
        var flowKey = trigger.FlowKey;
        var tenantId = trigger.TenantId;
        var topic = trigger.Queue.Trim();
        var qos = ParseQos(trigger.RoutingKey);

        client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var body = e.ApplicationMessage.ConvertPayloadToString();
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT trigger {Code} failed processing message", triggerCode);
            }
        };

        client.DisconnectedAsync += async e =>
        {
            if (e.ClientWasConnected)
            {
                _logger.LogWarning("MQTT disconnected. Trigger={Code} Reason={Reason}", triggerCode, e.Reason);
            }

            await Task.CompletedTask;
        };

        await client.ConnectAsync(opts, cancellationToken);
        await client.SubscribeAsync(
            new MqttTopicFilterBuilder().WithTopic(topic).WithQualityOfServiceLevel(qos).Build(),
            cancellationToken
        );

        _slots[SlotKey((trigger, source))] = new Slot(client);
        _logger.LogInformation(
            "MQTT trigger listening. Trigger={Code} Topic={Topic} FlowKey={FlowKey}",
            triggerCode,
            topic,
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
            if (slot.Client.IsConnected)
            {
                await slot.Client.DisconnectAsync();
            }
        }
        catch
        {
            /* ignore */
        }

        slot.Client.Dispose();
        _logger.LogInformation("Stopped MQTT trigger consumer {Key}", key);
    }

    private static MqttClientOptions ParseMqttOptions(string connectionString, string triggerCode)
    {
        var raw = connectionString.Trim();
        var builder = new MqttClientOptionsBuilder().WithClientId($"orch-{triggerCode}-{Guid.NewGuid():N}"[..22]);

        if (raw.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("mqtt://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("ssl://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("mqtts://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw.Replace("mqtt://", "tcp://", StringComparison.OrdinalIgnoreCase)
                .Replace("mqtts://", "ssl://", StringComparison.OrdinalIgnoreCase));
            var useTls = uri.Scheme.Equals("ssl", StringComparison.OrdinalIgnoreCase);
            var port = uri.Port > 0 ? uri.Port : (useTls ? 8883 : 1883);
            builder.WithTcpServer(uri.Host, port);
            if (useTls)
            {
                builder.WithTlsOptions(o => o.UseTls());
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                builder.WithCredentials(
                    Uri.UnescapeDataString(parts[0]),
                    parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null
                );
            }

            return builder.Build();
        }

        string? host = null;
        var portNum = 1883;
        string? user = null;
        string? pass = null;
        string? clientId = null;
        var tls = false;

        foreach (
            var part in raw.Split(
                [';', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var idx = part.IndexOf('=');
            if (idx <= 0)
            {
                if (host == null)
                {
                    host = part;
                }

                continue;
            }

            var k = part[..idx].Trim();
            var v = part[(idx + 1)..].Trim();
            switch (k.ToLowerInvariant())
            {
                case "host":
                case "server":
                    host = v;
                    break;
                case "port":
                    _ = int.TryParse(v, out portNum);
                    break;
                case "username":
                case "user":
                    user = v;
                    break;
                case "password":
                case "pass":
                    pass = v;
                    break;
                case "clientid":
                    clientId = v;
                    break;
                case "tls":
                case "ssl":
                    tls = v is "1" or "true" or "yes";
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("MQTT connection requires Host or tcp:// URI.");
        }

        builder.WithTcpServer(host, portNum);
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            builder.WithClientId(clientId);
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            builder.WithCredentials(user, pass);
        }

        if (tls)
        {
            builder.WithTlsOptions(o => o.UseTls());
        }

        return builder.Build();
    }

    private static MqttQualityOfServiceLevel ParseQos(string? routingKey)
    {
        return (routingKey ?? "1").Trim() switch
        {
            "0" => MqttQualityOfServiceLevel.AtMostOnce,
            "2" => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtLeastOnce
        };
    }

    private static string SlotKey((FlowTrigger Trigger, MessageSource Source) bind) =>
        $"{bind.Trigger.TenantId:N}|{bind.Trigger.Code}|mqtt|{bind.Trigger.Queue}";

    private sealed record Slot(IMqttClient Client);
}
