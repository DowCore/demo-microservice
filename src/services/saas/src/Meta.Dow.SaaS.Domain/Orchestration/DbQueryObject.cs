using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

public class QueryObjectParameter
{
    public string Name { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string PlatformType { get; set; } = TablePlatformType.String;

    public string Direction { get; set; } = QueryParameterDirection.In;

    public int Ordinal { get; set; }

    public bool Nullable { get; set; } = true;

    public bool HasDefault { get; set; }
}

/// <summary>
/// 从 SQL 数据源登记的只读查询对象（视图 / 存储过程 / 表值函数），不做 CREATE/ALTER。
/// </summary>
public class DbQueryObject : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string DataSourceCode { get; protected set; } = null!;

    public string Kind { get; protected set; } = QueryObjectKind.View;

    public string ObjectName { get; protected set; } = null!;

    public string DisplayName { get; protected set; } = null!;

    public string RoutineKind { get; protected set; } = QueryRoutineKind.View;

    public bool CanSelectFrom { get; protected set; } = true;

    public string? Comment { get; protected set; }

    public string? ImportWarning { get; protected set; }

    public DateTime? LastImportedAt { get; protected set; }

    public List<TableColumn> Columns { get; protected set; } = [];

    public List<QueryObjectParameter> Parameters { get; protected set; } = [];

    protected DbQueryObject()
    {
    }

    public DbQueryObject(
        Guid id,
        string dataSourceCode,
        string kind,
        string objectName,
        string displayName,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetDataSourceCode(dataSourceCode);
        SetKind(kind);
        SetObjectName(objectName);
        SetDisplayName(displayName);
        TenantId = tenantId;
        RoutineKind = Kind == QueryObjectKind.View ? QueryRoutineKind.View : QueryRoutineKind.Procedure;
        CanSelectFrom = Kind == QueryObjectKind.View;
    }

    public void ApplyImport(
        string displayName,
        string routineKind,
        bool canSelectFrom,
        string? comment,
        string? importWarning,
        IEnumerable<TableColumn> columns,
        IEnumerable<QueryObjectParameter> parameters
    )
    {
        SetDisplayName(displayName);
        RoutineKind = string.IsNullOrWhiteSpace(routineKind)
            ? (Kind == QueryObjectKind.View ? QueryRoutineKind.View : QueryRoutineKind.Procedure)
            : routineKind.Trim().ToLowerInvariant();
        CanSelectFrom = Kind == QueryObjectKind.View || canSelectFrom;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ImportWarning = string.IsNullOrWhiteSpace(importWarning) ? null : importWarning.Trim();
        ReplaceColumns(columns);
        ReplaceParameters(parameters);
        LastImportedAt = DateTime.UtcNow;
    }

    public void SetDisplayName(string displayName)
    {
        DisplayName = Check.NotNullOrWhiteSpace(
            displayName,
            nameof(displayName),
            OrchestrationConsts.MaxNameLength
        ).Trim();
    }

    private void SetDataSourceCode(string code)
    {
        DataSourceCode = Check.NotNullOrWhiteSpace(
            code,
            nameof(code),
            OrchestrationConsts.MaxDataSourceCodeLength
        ).Trim();
    }

    private void SetKind(string kind)
    {
        var value = QueryObjectKind.Normalize(kind);
        if (!QueryObjectKind.IsSupported(value))
        {
            throw new BusinessException("Orchestration:InvalidQueryObjectKind").WithData("Kind", kind);
        }

        Kind = value;
    }

    private void SetObjectName(string objectName)
    {
        var name = Check.NotNullOrWhiteSpace(
            objectName,
            nameof(objectName),
            OrchestrationConsts.MaxTableNameLength
        ).Trim();
        if (!SqlIdentifier.IsValid(name))
        {
            throw new BusinessException("Orchestration:InvalidIdentifier").WithData("Name", name);
        }

        ObjectName = name;
    }

    private void ReplaceColumns(IEnumerable<TableColumn> columns)
    {
        var list = columns?.ToList() ?? [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in list)
        {
            if (!SqlIdentifier.IsQuotable(col.Name))
            {
                throw new BusinessException("Orchestration:InvalidIdentifier").WithData("Name", col.Name);
            }

            col.Name = col.Name.Trim();
            col.PlatformType = TablePlatformType.IsSupported(col.PlatformType)
                ? TablePlatformType.Normalize(col.PlatformType)
                : TablePlatformType.String;
            col.DisplayName = string.IsNullOrWhiteSpace(col.DisplayName) ? col.Name : col.DisplayName.Trim();
            col.Origin = TableOrigin.User;
            if (!names.Add(col.Name))
            {
                throw new BusinessException("Orchestration:DuplicateColumn").WithData("Name", col.Name);
            }
        }

        Columns = list;
    }

    private void ReplaceParameters(IEnumerable<QueryObjectParameter> parameters)
    {
        var list = parameters?.ToList() ?? [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordinal = 1;
        foreach (var param in list)
        {
            if (!SqlIdentifier.IsValid(param.Name))
            {
                throw new BusinessException("Orchestration:InvalidIdentifier").WithData("Name", param.Name);
            }

            param.Name = param.Name.Trim();
            param.DisplayName = string.IsNullOrWhiteSpace(param.DisplayName) ? param.Name : param.DisplayName.Trim();
            param.PlatformType = TablePlatformType.IsSupported(param.PlatformType)
                ? TablePlatformType.Normalize(param.PlatformType)
                : TablePlatformType.String;
            param.Direction = QueryParameterDirection.Normalize(param.Direction);
            if (param.Ordinal <= 0)
            {
                param.Ordinal = ordinal;
            }

            ordinal++;
            if (!names.Add(param.Name))
            {
                throw new BusinessException("Orchestration:DuplicateColumn").WithData("Name", param.Name);
            }
        }

        Parameters = list.OrderBy(x => x.Ordinal).ToList();
    }
}
