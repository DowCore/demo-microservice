using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 消息连接源（类比 DataSource）：登记 broker，供消息触发器引用。
/// </summary>
public class MessageSource : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    /// <summary>rabbit / kafka / mqtt</summary>
    public string Provider { get; protected set; } = null!;

    /// <summary>留空表示使用平台默认连接（Rabbit → ConnectionStrings:rabbitmq）</summary>
    public string? ConnectionString { get; protected set; }

    public bool IsEnabled { get; protected set; }

    public string? Description { get; protected set; }

    protected MessageSource()
    {
    }

    public MessageSource(
        Guid id,
        string code,
        string name,
        string provider,
        string? connectionString = null,
        string? description = null,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetCode(code);
        SetName(name);
        SetProvider(provider);
        ConnectionString = string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim();
        Description = description?.Trim();
        TenantId = tenantId;
        IsEnabled = true;
    }

    public void Update(
        string name,
        string provider,
        bool isEnabled,
        string? description,
        string? connectionString = null
    )
    {
        SetName(name);
        SetProvider(provider);
        IsEnabled = isEnabled;
        Description = description?.Trim();
        if (connectionString != null)
        {
            ConnectionString = string.IsNullOrWhiteSpace(connectionString)
                ? null
                : connectionString.Trim();
        }
    }

    private void SetCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), OrchestrationConsts.MaxMessageSourceCodeLength);
    }

    private void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrchestrationConsts.MaxNameLength);
    }

    private void SetProvider(string provider)
    {
        var p = Check.NotNullOrWhiteSpace(provider, nameof(provider), 32).Trim().ToLowerInvariant();
        if (!MessageSourceProvider.All.Contains(p, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessException("Orchestration:UnsupportedMessageSourceProvider")
                .WithData("Provider", provider);
        }

        Provider = p;
    }
}
