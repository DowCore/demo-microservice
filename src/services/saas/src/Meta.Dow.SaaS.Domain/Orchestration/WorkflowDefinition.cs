using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// OA 人审工作流定义（钉钉式节点树）。与逻辑编排 FlowDefinition 分离，禁止混画。
/// </summary>
public class WorkflowDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string Code { get; protected set; } = null!;

    public string Name { get; protected set; } = null!;

    public string? Description { get; protected set; }

    public int Status { get; protected set; } = AppResourceStatus.Draft;

    /// <summary>已发布版本号。0 = 从未发布；每次发布 +1，用于回答「在途实例跑的是第几版」。</summary>
    public int Version { get; protected set; }

    /// <summary>绑定的独立表单编码（FormDefinition.Code）</summary>
    public string? FormRef { get; protected set; }

    /// <summary>钉钉式流程树 JSON（start → approver/cc/condition → end）</summary>
    public string ProcessJson { get; protected set; } = "{}";

    /// <summary>启动前可选挂逻辑编排 FlowKey</summary>
    public string? BeforeStartFlowKey { get; protected set; }

    /// <summary>流程结束后可选挂逻辑编排 FlowKey</summary>
    public string? AfterEndFlowKey { get; protected set; }

    protected WorkflowDefinition()
    {
    }

    public WorkflowDefinition(Guid id, string code, string name, Guid? tenantId = null)
        : base(id)
    {
        SetCode(code);
        SetName(name);
        TenantId = tenantId;
        Status = AppResourceStatus.Draft;
        ProcessJson = DefaultProcessJson();
    }

    public void SetCode(string code) =>
        Code = Check.NotNullOrWhiteSpace(code, nameof(code)).Trim();

    public void SetName(string name) =>
        Name = Check.NotNullOrWhiteSpace(name, nameof(name)).Trim();

    public void UpdateDraft(
        string name,
        string? description,
        string? formRef,
        string processJson,
        string? beforeStartFlowKey,
        string? afterEndFlowKey
    )
    {
        SetName(name);
        Description = description?.Trim();
        FormRef = string.IsNullOrWhiteSpace(formRef) ? null : formRef.Trim();
        ProcessJson = string.IsNullOrWhiteSpace(processJson) ? DefaultProcessJson() : processJson;
        BeforeStartFlowKey = string.IsNullOrWhiteSpace(beforeStartFlowKey)
            ? null
            : beforeStartFlowKey.Trim();
        AfterEndFlowKey = string.IsNullOrWhiteSpace(afterEndFlowKey)
            ? null
            : afterEndFlowKey.Trim();
    }

    /// <summary>发布：状态置为已发布并递增版本号（版本用于冻结「在途实例对应的定义版本」）。</summary>
    public void Publish()
    {
        Status = AppResourceStatus.Published;
        Version++;
    }

    /// <summary>
    /// 默认流程（W4）：直接产出画布所用的 nodes/edges 格式。
    /// 旧实现产出 childNode 链，该格式结构上无法表达多条带条件的出边，新建流程会落在一个「不能分支」的残废格式上。
    /// 运行时仍兼容解析旧格式（向后兼容），但新建一律使用本格式。
    /// </summary>
    public static string DefaultProcessJson() =>
        """
        {
          "nodes": [
            { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start", "name": "开始" } },
            { "id": "approver_1", "type": "approver", "data": { "nodeId": "approver_1", "type": "approver", "name": "接收", "assigneeType": "role", "assigneeValue": "", "opinion": "optional", "multi": "single" } },
            { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end", "name": "结束" } }
          ],
          "edges": [
            { "source": "start", "target": "approver_1", "data": { "priority": 0 } },
            { "source": "approver_1", "target": "end", "data": { "priority": 0 } }
          ]
        }
        """;
}
