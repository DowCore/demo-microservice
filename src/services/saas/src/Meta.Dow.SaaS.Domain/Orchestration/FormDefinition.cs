using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 独立可复用表单库。资源/报表/OA 节点通过 FormRef(code) 引用，避免把 Schema 锁死在报表 Tab。
/// </summary>
public class FormDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    public string? Description { get; protected set; }

    public int Status { get; protected set; } = AppResourceStatus.Draft;

    /// <summary>可选：从某资源列目录取字段库</summary>
    public string? SourceResourceCode { get; protected set; }

    public FormDef Schema { get; protected set; } = new();

    /// <summary>设计器字段库（无资源时手工维护；有资源时可从 ListView 同步）</summary>
    public List<ListColumnDef> FieldCatalog { get; protected set; } = [];

    protected FormDefinition()
    {
    }

    public FormDefinition(Guid id, string code, string name, Guid? tenantId = null)
        : base(id)
    {
        SetCode(code);
        SetName(name);
        TenantId = tenantId;
        Status = AppResourceStatus.Draft;
        Schema = new FormDef();
        FieldCatalog = [];
    }

    public void SetCode(string code) =>
        Code = Check.NotNullOrWhiteSpace(code, nameof(code)).Trim();

    public void SetName(string name) =>
        Name = Check.NotNullOrWhiteSpace(name, nameof(name)).Trim();

    public void UpdateDraft(
        string name,
        string? description,
        string? sourceResourceCode,
        FormDef schema,
        List<ListColumnDef>? fieldCatalog
    )
    {
        SetName(name);
        Description = description?.Trim();
        SourceResourceCode = string.IsNullOrWhiteSpace(sourceResourceCode)
            ? null
            : sourceResourceCode.Trim();
        Schema = schema ?? new FormDef();
        FieldCatalog = fieldCatalog ?? [];
    }

    public void Publish() => Status = AppResourceStatus.Published;
}
