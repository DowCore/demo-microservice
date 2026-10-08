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

    public void Publish() => Status = AppResourceStatus.Published;

    public static string DefaultProcessJson() =>
        """
        {
          "nodeId": "start",
          "type": "start",
          "name": "开始",
          "childNode": {
            "nodeId": "approver_1",
            "type": "approver",
            "name": "接收",
            "assigneeType": "role",
            "assigneeValue": "",
            "opinion": "optional",
            "multi": "single",
            "childNode": {
              "nodeId": "end",
              "type": "end",
              "name": "结束"
            }
          }
        }
        """;
}
