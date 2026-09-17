using System;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 种子：复杂 HTTP 演示流程（已发布，可直接 POST /api/logic/order-http-demo）。
/// </summary>
public class FlowDefinitionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string DemoCode = "order-http-demo";

    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IRepository<FlowVersion, Guid> _versionRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public FlowDefinitionDataSeedContributor(
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant
    )
    {
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        using (_currentTenant.Change(context?.TenantId))
        {
            var dslJson = BuildDemoDslJson();
            var existing = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == DemoCode);
            if (existing != null)
            {
                // 开发期：刷新演示 DSL（End.finalOutputs / Mask / map / aggregate）
                existing.UpdateDraft(existing.Name, existing.Category, graphJson: "{}", dslJson: dslJson);
                if (existing.Status == FlowDefinitionStatus.Published)
                {
                    var next = (existing.PublishedVersion ?? 0) + 1;
                    existing.MarkPublished(next);
                    await _definitionRepository.UpdateAsync(existing, autoSave: true);
                    await _versionRepository.InsertAsync(
                        new FlowVersion(
                            _guidGenerator.Create(),
                            existing.Id,
                            version: next,
                            graphJson: "{}",
                            dslJson: dslJson,
                            tenantId: context?.TenantId
                        ),
                        autoSave: true
                    );
                }
                else
                {
                    await _definitionRepository.UpdateAsync(existing, autoSave: true);
                }

                return;
            }

            var definitionId = _guidGenerator.Create();
            var definition = new FlowDefinition(
                definitionId,
                "订单 HTTP 风控演示",
                DemoCode,
                "演示",
                context?.TenantId
            );
            definition.UpdateDraft(
                "订单 HTTP 风控演示",
                "演示",
                graphJson: "{}",
                dslJson: dslJson
            );
            definition.MarkPublished(1);

            await _definitionRepository.InsertAsync(definition, autoSave: true);
            await _versionRepository.InsertAsync(
                new FlowVersion(
                    _guidGenerator.Create(),
                    definitionId,
                    version: 1,
                    graphJson: "{}",
                    dslJson: dslJson,
                    tenantId: context?.TenantId
                ),
                autoSave: true
            );
        }
    }

    private static string BuildDemoDslJson()
    {
        return """
{
  "version": "1.1",
  "key": "order-http-demo",
  "trigger": { "type": "Manual" },
  "inputs": [
    {
      "name": "amount",
      "type": "number",
      "required": true,
      "source": "input",
      "rules": [
        { "type": "min", "value": 0, "message": "金额不能为负" }
      ]
    },
    {
      "name": "token",
      "type": "string",
      "required": true,
      "source": "input",
      "description": "下游 HTTP Authorization"
    },
    {
      "name": "order",
      "type": "object",
      "source": "input",
      "properties": [
        { "name": "id", "type": "string", "required": true, "source": "input" },
        { "name": "qty", "type": "number", "source": "input" }
      ]
    },
    {
      "name": "queryFrom",
      "type": "datetime",
      "source": "system",
      "systemExpr": "sys.Now - 3d"
    }
  ],
  "outputs": [
    { "name": "message", "type": "string", "from": "message", "visibleTo": { "mode": "all" } },
    { "name": "level", "type": "string", "from": "level", "visibleTo": { "mode": "all" } },
    { "name": "httpStatus", "type": "number", "from": "statusCode", "visibleTo": { "mode": "all" } },
    { "name": "remoteUrl", "type": "string", "from": "remoteUrl", "visibleTo": { "mode": "all" } },
    { "name": "tokenMasked", "type": "string", "from": "tokenMasked", "sensitive": true, "visibleTo": { "mode": "all" } },
    { "name": "lineItems", "type": "array", "from": "lineItems", "visibleTo": { "mode": "all" },
      "map": {
        "item": [
          { "name": "sku", "from": "id" },
          { "name": "quantity", "from": "qty" },
          { "name": "label", "from": { "template": "ORD-{{id}}" } }
        ]
      }
    },
    { "name": "lineCount", "type": "number", "from": "lineItems", "aggregate": { "op": "count" }, "visibleTo": { "mode": "all" } }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "guard",
      "type": "Throw",
      "ref": "guard",
      "failItems": [
        { "no": 1, "left": "input.amount", "op": "lt", "right": 0 }
      ],
      "failCombine": "1",
      "failCode": "Biz:InvalidAmount",
      "failMessage": "金额非法：{{input.amount}}",
      "outputs": [{ "name": "thrown", "from": "thrown" }]
    },
    {
      "id": "http_post",
      "type": "HttpCall",
      "ref": "httpPost",
      "async": false,
      "method": "POST",
      "url": "mock://echo",
      "bodyMode": "json",
      "resultRoot": "body",
      "headers": [
        { "name": "Authorization", "from": "input.token" },
        { "name": "X-Client", "from": { "literal": "Meta.Dow.Orchestration" } },
        { "name": "Accept", "from": { "literal": "application/json" } }
      ],
      "inputs": [
        { "name": "orderId", "from": "input.order.id" },
        { "name": "amount", "from": "input.amount" },
        { "name": "qty", "from": "input.order.qty" },
        { "name": "queryFrom", "from": "input.queryFrom" },
        { "name": "operator", "from": "sys.userName" }
      ],
      "outputs": [
        { "name": "statusCode", "from": "statusCode" },
        { "name": "body", "from": "body" },
        { "name": "remoteUrl", "from": "url" }
      ],
      "failItems": [
        { "no": 1, "left": "statusCode", "op": "ne", "right": 200 },
        { "no": 2, "left": "remoteUrl", "op": "isEmpty", "right": "" }
      ],
      "failCombine": "1 or 2",
      "failCode": "Http:UpstreamFailed",
      "failMessage": "上游调用失败 status={{statusCode}} url={{remoteUrl}}"
    },
    {
      "id": "branch",
      "type": "Condition",
      "items": [
        { "no": 1, "left": "input.amount", "op": "gt", "right": 1000 },
        { "no": 2, "left": "statusCode", "op": "eq", "right": 200 }
      ]
    },
    {
      "id": "set_high",
      "type": "Assign",
      "ref": "setHigh",
      "inputs": [{ "name": "level", "from": { "literal": "high" } }],
      "outputs": [{ "name": "level", "from": "level" }]
    },
    {
      "id": "set_low",
      "type": "Assign",
      "ref": "setLow",
      "inputs": [{ "name": "level", "from": { "literal": "normal" } }],
      "outputs": [{ "name": "level", "from": "level" }]
    },
    {
      "id": "log_high",
      "type": "Log",
      "ref": "logHigh",
      "message": "高额订单 {{input.order.id}} amount={{input.amount}} level={{level}} http={{statusCode}} echo={{remoteUrl}}",
      "outputs": [{ "name": "message", "from": "message" }]
    },
    {
      "id": "log_low",
      "type": "Log",
      "ref": "logLow",
      "message": "普通订单 {{input.order.id}} amount={{input.amount}} level={{level}} http={{statusCode}}",
      "outputs": [{ "name": "message", "from": "message" }]
    },
    {
      "id": "prep_lines",
      "type": "Assign",
      "ref": "prepLines",
      "inputs": [
        {
          "name": "lineItems",
          "type": "array",
          "from": {
            "literal": [
              { "id": "ORD-1001", "qty": 2 },
              { "id": "ORD-1001-B", "qty": 1 }
            ]
          }
        }
      ],
      "outputs": [
        { "name": "lineItems", "type": "array", "from": "lineItems" }
      ]
    },
    {
      "id": "mask_token",
      "type": "Mask",
      "ref": "maskToken",
      "maskStrategy": "rules",
      "inputs": [
        { "name": "token", "from": "input.token" }
      ],
      "maskRules": [
        { "field": "token", "op": "mask", "keepStart": 7, "keepEnd": 0, "maskChar": "*" }
      ],
      "outputs": [
        { "name": "tokenMasked", "from": "token" }
      ]
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "message", "type": "string", "from": "message", "visibleTo": { "mode": "all" } },
        { "name": "level", "type": "string", "from": "level", "visibleTo": { "mode": "all" } },
        { "name": "httpStatus", "type": "number", "from": "statusCode", "visibleTo": { "mode": "all" } },
        { "name": "remoteUrl", "type": "string", "from": "remoteUrl", "visibleTo": { "mode": "all" } },
        { "name": "tokenMasked", "type": "string", "from": "tokenMasked", "sensitive": true, "visibleTo": { "mode": "all" } },
        {
          "name": "lineItems",
          "type": "array",
          "from": "lineItems",
          "visibleTo": { "mode": "all" },
          "map": {
            "item": [
              { "name": "sku", "from": "id" },
              { "name": "quantity", "from": "qty" },
              { "name": "label", "from": { "template": "Line {{id}}" } }
            ]
          }
        },
        {
          "name": "lineCount",
          "type": "number",
          "from": "lineItems",
          "aggregate": { "op": "count" },
          "visibleTo": { "mode": "all" }
        }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "guard" },
    { "source": "guard", "target": "http_post" },
    { "source": "http_post", "target": "branch" },
    { "source": "branch", "target": "set_high", "combine": "1 and 2" },
    { "source": "branch", "target": "set_low", "isDefault": true },
    { "source": "set_high", "target": "log_high" },
    { "source": "set_low", "target": "log_low" },
    { "source": "log_high", "target": "prep_lines" },
    { "source": "log_low", "target": "prep_lines" },
    { "source": "prep_lines", "target": "mask_token" },
    { "source": "mask_token", "target": "end" }
  ]
}
""";
    }
}
