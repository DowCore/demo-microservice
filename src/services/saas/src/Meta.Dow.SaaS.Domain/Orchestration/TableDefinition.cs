using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

public class TableColumn
{
    public string Name { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string PlatformType { get; set; } = TablePlatformType.String;

    public int? Length { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public bool Nullable { get; set; }

    public string? Default { get; set; }

    public bool Unique { get; set; }

    public string? Comment { get; set; }

    public string Origin { get; set; } = TableOrigin.User;

    /// <summary>
    /// Last physical column name applied to the database. Used to emit RENAME COLUMN.
    /// </summary>
    public string? AppliedName { get; set; }
}

public class TableIndexColumn
{
    public string Name { get; set; } = null!;

    public bool Descending { get; set; }
}

public class TableIndexDef
{
    public string Name { get; set; } = null!;

    public bool Unique { get; set; }

    public bool IsPrimary { get; set; }

    public string Origin { get; set; } = TableOrigin.User;

    public List<TableIndexColumn> Columns { get; set; } = [];
}

/// <summary>某 DataSource 下的托管表设计时结构。</summary>
public class TableDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string DataSourceCode { get; protected set; } = null!;

    public string TableName { get; protected set; } = null!;

    public string DisplayName { get; protected set; } = null!;

    public string Origin { get; protected set; } = TableOrigin.User;

    public string SyncState { get; protected set; } = TableSyncState.Draft;

    public DateTime? LastAppliedAt { get; protected set; }

    public string? Comment { get; protected set; }

    public List<TableColumn> Columns { get; protected set; } = [];

    public List<TableIndexDef> Indexes { get; protected set; } = [];

    protected TableDefinition()
    {
    }

    public TableDefinition(
        Guid id,
        string dataSourceCode,
        string tableName,
        string displayName,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetDataSourceCode(dataSourceCode);
        SetTableName(tableName);
        SetDisplayName(displayName);
        TenantId = tenantId;
        Origin = "managed";
        SyncState = TableSyncState.Draft;
        Columns = AbpConventionColumns.Create();
        Indexes = AbpConventionColumns.CreateDefaultIndexes(tableName);
    }

    public void UpdateDraft(
        string displayName,
        string? comment,
        IEnumerable<TableColumn> columns,
        IEnumerable<TableIndexDef> indexes
    )
    {
        SetDisplayName(displayName);
        Comment = comment?.Trim();
        ReplaceColumns(columns);
        ReplaceIndexes(indexes);
        if (SyncState == TableSyncState.InSync)
        {
            SyncState = TableSyncState.LocalAhead;
        }
    }

    public void MarkApplied()
    {
        SyncState = TableSyncState.InSync;
        LastAppliedAt = DateTime.UtcNow;
        foreach (var col in Columns)
        {
            col.AppliedName = col.Name;
        }
    }

    public void MarkLocalAhead()
    {
        if (SyncState == TableSyncState.InSync)
        {
            SyncState = TableSyncState.LocalAhead;
        }
    }

    private void SetDataSourceCode(string code)
    {
        DataSourceCode = Check.NotNullOrWhiteSpace(
            code,
            nameof(code),
            OrchestrationConsts.MaxDataSourceCodeLength
        ).Trim();
    }

    private void SetTableName(string tableName)
    {
        var name = Check.NotNullOrWhiteSpace(
            tableName,
            nameof(tableName),
            OrchestrationConsts.MaxTableNameLength
        ).Trim();
        if (!SqlIdentifier.IsValid(name))
        {
            throw new BusinessException("Orchestration:InvalidIdentifier").WithData("Name", name);
        }

        TableName = name;
    }

    private void SetDisplayName(string displayName)
    {
        DisplayName = Check.NotNullOrWhiteSpace(
            displayName,
            nameof(displayName),
            OrchestrationConsts.MaxNameLength
        ).Trim();
    }

    private void ReplaceColumns(IEnumerable<TableColumn> columns)
    {
        var list = columns?.ToList() ?? [];
        if (list.Count == 0)
        {
            throw new BusinessException("Orchestration:TableColumnsRequired");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in list)
        {
            if (!SqlIdentifier.IsValid(col.Name))
            {
                throw new BusinessException("Orchestration:InvalidIdentifier").WithData("Name", col.Name);
            }

            if (!TablePlatformType.IsSupported(col.PlatformType))
            {
                throw new BusinessException("Orchestration:UnsupportedColumnType")
                    .WithData("Type", col.PlatformType);
            }

            col.Name = col.Name.Trim();
            col.PlatformType = TablePlatformType.Normalize(col.PlatformType);
            col.DisplayName = string.IsNullOrWhiteSpace(col.DisplayName) ? col.Name : col.DisplayName.Trim();
            col.Origin = string.IsNullOrWhiteSpace(col.Origin) ? TableOrigin.User : col.Origin.Trim();
            if (string.IsNullOrWhiteSpace(col.AppliedName))
            {
                col.AppliedName = null;
            }
            else
            {
                col.AppliedName = col.AppliedName.Trim();
                if (!SqlIdentifier.IsValid(col.AppliedName))
                {
                    throw new BusinessException("Orchestration:InvalidIdentifier")
                        .WithData("Name", col.AppliedName);
                }
            }

            if (!names.Add(col.Name))
            {
                throw new BusinessException("Orchestration:DuplicateColumn").WithData("Name", col.Name);
            }
        }

        foreach (var required in AbpConventionColumns.RequiredNames)
        {
            if (!names.Contains(required))
            {
                throw new BusinessException("Orchestration:ConventionColumnRequired")
                    .WithData("Name", required);
            }
        }

        Columns = list;
    }

    private void ReplaceIndexes(IEnumerable<TableIndexDef> indexes)
    {
        var list = indexes?.ToList() ?? [];
        var colNames = Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ix in list)
        {
            if (!SqlIdentifier.IsValid(ix.Name))
            {
                throw new BusinessException("Orchestration:InvalidIdentifier").WithData("Name", ix.Name);
            }

            if (ix.Columns.Count == 0)
            {
                throw new BusinessException("Orchestration:IndexColumnsRequired").WithData("Name", ix.Name);
            }

            foreach (var c in ix.Columns)
            {
                if (!colNames.Contains(c.Name))
                {
                    throw new BusinessException("Orchestration:IndexColumnMissing")
                        .WithData("Index", ix.Name)
                        .WithData("Column", c.Name);
                }
            }
        }

        Indexes = list;
    }
}

public static class AbpConventionColumns
{
    public static readonly string[] RequiredNames =
    [
        "Id",
        "TenantId",
        "ConcurrencyStamp",
        "CreationTime",
        "CreatorId",
        "LastModificationTime",
        "LastModifierId",
        "IsDeleted",
        "DeleterId",
        "DeletionTime"
    ];

    public static List<TableColumn> Create()
    {
        return
        [
            Col("Id", "主键", TablePlatformType.Guid, nullable: false),
            Col("TenantId", "租户", TablePlatformType.Guid, nullable: true),
            Col("ConcurrencyStamp", "并发戳", TablePlatformType.String, nullable: false, length: 40),
            Col("CreationTime", "创建时间", TablePlatformType.DateTime, nullable: false),
            Col("CreatorId", "创建人", TablePlatformType.Guid, nullable: true),
            Col("LastModificationTime", "修改时间", TablePlatformType.DateTime, nullable: true),
            Col("LastModifierId", "修改人", TablePlatformType.Guid, nullable: true),
            Col("IsDeleted", "已删除", TablePlatformType.Boolean, nullable: false, defaultValue: "false"),
            Col("DeleterId", "删除人", TablePlatformType.Guid, nullable: true),
            Col("DeletionTime", "删除时间", TablePlatformType.DateTime, nullable: true)
        ];
    }

    public static List<TableIndexDef> CreateDefaultIndexes(string tableName)
    {
        var safe = Regex.Replace(tableName, @"[^A-Za-z0-9_]", "_");
        return
        [
            new TableIndexDef
            {
                Name = $"PK_{safe}",
                IsPrimary = true,
                Unique = true,
                Origin = TableOrigin.Convention,
                Columns = [new TableIndexColumn { Name = "Id" }]
            },
            new TableIndexDef
            {
                Name = $"IX_{safe}_Tenant_Deleted",
                Origin = TableOrigin.Convention,
                Columns =
                [
                    new TableIndexColumn { Name = "TenantId" },
                    new TableIndexColumn { Name = "IsDeleted" }
                ]
            },
            new TableIndexDef
            {
                Name = $"IX_{safe}_Tenant_Creation",
                Origin = TableOrigin.Convention,
                Columns =
                [
                    new TableIndexColumn { Name = "TenantId" },
                    new TableIndexColumn { Name = "CreationTime", Descending = true }
                ]
            }
        ];
    }

    public static bool IsConvention(string name) =>
        RequiredNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static TableColumn Col(
        string name,
        string display,
        string type,
        bool nullable,
        int? length = null,
        string? defaultValue = null
    )
    {
        return new TableColumn
        {
            Name = name,
            DisplayName = display,
            PlatformType = type,
            Nullable = nullable,
            Length = length,
            Default = defaultValue,
            Origin = TableOrigin.Convention
        };
    }
}
