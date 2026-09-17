using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 租户级外部库白名单；Code/Sql 节点通过 Code 引用。
/// </summary>
public class DataSource : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    /// <summary>postgres / mysql / sqlserver</summary>
    public string Provider { get; protected set; } = null!;

    public string ConnectionString { get; protected set; } = null!;

    public DataSourceAccessMode AccessMode { get; protected set; }

    /// <summary>select / insert / update / delete；空表示按 AccessMode 推导</summary>
    public List<string> AllowedOps { get; protected set; } = [];

    public bool IsEnabled { get; protected set; }

    public string? Description { get; protected set; }

    protected DataSource()
    {
    }

    public DataSource(
        Guid id,
        string code,
        string name,
        string provider,
        string connectionString,
        DataSourceAccessMode accessMode,
        IEnumerable<string>? allowedOps = null,
        string? description = null,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetCode(code);
        SetName(name);
        SetProvider(provider);
        SetConnectionString(connectionString);
        AccessMode = accessMode;
        AllowedOps = NormalizeOps(allowedOps);
        Description = description?.Trim();
        TenantId = tenantId;
        IsEnabled = true;
    }

    public void Update(
        string name,
        string provider,
        DataSourceAccessMode accessMode,
        IEnumerable<string>? allowedOps,
        string? description,
        bool isEnabled,
        string? connectionString = null
    )
    {
        SetName(name);
        SetProvider(provider);
        AccessMode = accessMode;
        AllowedOps = NormalizeOps(allowedOps);
        Description = description?.Trim();
        IsEnabled = isEnabled;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            SetConnectionString(connectionString);
        }
    }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;

    public bool AllowsRead()
    {
        return AccessMode is DataSourceAccessMode.Read or DataSourceAccessMode.ReadWrite;
    }

    public bool AllowsWrite()
    {
        return AccessMode is DataSourceAccessMode.Write or DataSourceAccessMode.ReadWrite;
    }

    public bool AllowsOp(string op)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(op);
        var normalized = op.Trim().ToLowerInvariant();
        if (AllowedOps.Count > 0)
        {
            return AllowedOps.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        }

        return normalized switch
        {
            "select" or "get" or "mget" or "hget" or "hgetall" or "find" or "findone" => AllowsRead(),
            "insert" or "update" or "delete" or "set" or "mset" or "del" or "hset"
                or "insertone" or "insertmany" or "updateone" or "updatemany"
                or "deleteone" or "deletemany" => AllowsWrite(),
            _ => false
        };
    }

    private void SetCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), OrchestrationConsts.MaxDataSourceCodeLength);
    }

    private void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrchestrationConsts.MaxNameLength);
    }

    private void SetProvider(string provider)
    {
        var p = DataSourceProvider.Normalize(
            Check.NotNullOrWhiteSpace(provider, nameof(provider), 32)
        );
        if (!DataSourceProvider.IsSupported(p))
        {
            throw new BusinessException("Orchestration:UnsupportedDataSourceProvider")
                .WithData("Provider", p);
        }

        Provider = p;
    }

    private void SetConnectionString(string connectionString)
    {
        ConnectionString = Check.NotNullOrWhiteSpace(
            connectionString,
            nameof(connectionString),
            OrchestrationConsts.MaxConnectionStringLength
        );
    }

    private static List<string> NormalizeOps(IEnumerable<string>? allowedOps)
    {
        if (allowedOps == null)
        {
            return [];
        }

        return allowedOps
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
