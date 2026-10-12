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

    /// <summary>已发布版本号。0 = 从未发布；每次发布 +1。</summary>
    public int SchemaVersion { get; protected set; }

    /// <summary>可选：从某资源列目录取字段库</summary>
    public string? SourceResourceCode { get; protected set; }

    /// <summary>草稿态 Schema：设计器可自由编辑。</summary>
    public FormDef Schema { get; protected set; } = new();

    /// <summary>
    /// 已发布的不可变快照（F4）。发布时深拷贝生成，运行时消费方（OA 表单渲染 / 记录写入）只读此快照，
    /// 因此「继续改草稿」不会让在途审批单的字段结构漂移。为空表示从未发布。
    /// </summary>
    public FormDef? PublishedSchema { get; protected set; }

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

    /// <summary>发布：写入不可变快照并递增版本号。快照由调用方深拷贝后传入。</summary>
    public void Publish(FormDef snapshot)
    {
        PublishedSchema = snapshot ?? new FormDef();
        SchemaVersion++;
        Status = AppResourceStatus.Published;
    }

    /// <summary>运行时/消费方应使用的有效 Schema：优先已发布快照，无快照时退回草稿。</summary>
    public FormDef GetEffectiveSchema() => PublishedSchema ?? Schema;
}
