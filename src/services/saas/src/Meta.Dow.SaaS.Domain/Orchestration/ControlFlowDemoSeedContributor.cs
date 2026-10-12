using System;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 种子：控制流原语演示流程（已发布，可直接 POST /api/logic/{key}）。
///
/// 覆盖 v1.2 新增的两个原语：
/// 1. <c>cf-foreach-demo</c>        —— ForEach + 内联循环体 + continueOnError
/// 2. <c>cf-foreach-subflow-demo</c> —— ForEach + subFlowKey（复用已发布的 order-http-demo）
/// 3. <c>cf-parallel-demo</c>       —— Parallel 三分支并发聚合（分支内含 Condition）
///
/// 与 <see cref="FlowDefinitionDataSeedContributor"/> 同样遵循"开发期刷新演示 DSL"的口径：
/// 已存在则 draft 覆盖 + published 版本号自增，便于随代码演进持续修正演示。
/// </summary>
public class ControlFlowDemoSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string ForEachDemoCode = "cf-foreach-demo";

    public const string ForEachSubFlowDemoCode = "cf-foreach-subflow-demo";

    public const string ParallelDemoCode = "cf-parallel-demo";

    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IRepository<FlowVersion, Guid> _versionRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public ControlFlowDemoSeedContributor(
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
            await SeedOneAsync(
                context?.TenantId,
                ForEachDemoCode,
                "控制流演示：批量迭代（ForEach）",
                BuildForEachDsl()
            );
            await SeedOneAsync(
                context?.TenantId,
                ForEachSubFlowDemoCode,
                "控制流演示：按项调用子流程（ForEach + SubFlow）",
                BuildForEachSubFlowDsl()
            );
            await SeedOneAsync(
                context?.TenantId,
                ParallelDemoCode,
                "控制流演示：并发聚合（Parallel）",
                BuildParallelDsl()
            );
        }
    }

    private async Task SeedOneAsync(Guid? tenantId, string code, string name, string dslJson)
    {
        var existing = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == code);
        if (existing != null)
        {
            existing.MarkSystem();
            existing.SetReusable(true);
            existing.UpdateDraft(name, "演示", graphJson: "{}", dslJson: dslJson, isReusable: true);
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
                    tenantId: tenantId
                ),
                autoSave: true
            );
            return;
        }

        var definitionId = _guidGenerator.Create();
        var definition = new FlowDefinition(
            definitionId,
            name,
            code,
            "演示",
            tenantId,
            isReusable: true,
            isSystem: true
        );
        definition.UpdateDraft(name, "演示", graphJson: "{}", dslJson: dslJson, isReusable: true);
        definition.MarkPublished(1);

        await _definitionRepository.InsertAsync(definition, autoSave: true);
        await _versionRepository.InsertAsync(
            new FlowVersion(
                _guidGenerator.Create(),
                definitionId,
                version: 1,
                graphJson: "{}",
                dslJson: dslJson,
                tenantId: tenantId
            ),
            autoSave: true
        );
    }

    /// <summary>
    /// ForEach + 内联循环体：对 input.lines 逐项执行，单项失败不中断整轮。
    /// 演示要点：itemsPath / itemVar / indexVar / concurrency / maxIterations / itemErrorMode。
    /// </summary>
    public static string BuildForEachDsl()
    {
        return """
{
  "version": "1.1",
  "key": "cf-foreach-demo",
  "trigger": { "type": "Manual" },
  "inputs": [
    {
      "name": "lines",
      "type": "array",
      "required": true,
      "source": "input",
      "description": "待处理明细，元素形如 { sku, qty }"
    }
  ],
  "outputs": [
    { "name": "items", "type": "array", "from": "items", "visibleTo": { "mode": "all" } },
    { "name": "count", "type": "number", "from": "count", "visibleTo": { "mode": "all" } },
    { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
    { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "each",
      "type": "ForEach",
      "ref": "each",
      "itemsPath": "input.lines",
      "itemVar": "item",
      "indexVar": "index",
      "concurrency": 1,
      "maxIterations": 50,
      "itemErrorMode": "continueOnError",
      "inputs": [
        { "name": "trace", "from": { "literal": "batch" } }
      ],
      "loopBody": {
        "inputs": [
          { "name": "item", "type": "object", "source": "input" },
          { "name": "index", "type": "number", "source": "input" }
        ],
        "nodes": [
          { "id": "body_start", "type": "Start" },
          {
            "id": "pick",
            "type": "Assign",
            "ref": "pick",
            "inputs": [
              { "name": "sku", "from": "input.item.sku" },
              { "name": "qty", "from": "input.item.qty" },
              { "name": "seq", "from": "input.index" }
            ],
            "outputs": [
              { "name": "sku", "from": "sku" },
              { "name": "qty", "from": "qty" },
              { "name": "seq", "from": "seq" }
            ]
          },
          {
            "id": "trace",
            "type": "Log",
            "ref": "trace",
            "message": "第 {{input.index}} 行：{{input.item.sku}} × {{input.item.qty}}",
            "outputs": [{ "name": "message", "from": "message" }]
          },
          {
            "id": "body_end",
            "type": "End",
            "finalOutputs": [
              { "name": "sku", "type": "string", "from": "sku", "visibleTo": { "mode": "all" } },
              { "name": "qty", "type": "number", "from": "qty", "visibleTo": { "mode": "all" } },
              { "name": "seq", "type": "number", "from": "seq", "visibleTo": { "mode": "all" } }
            ]
          }
        ],
        "edges": [
          { "source": "body_start", "target": "pick" },
          { "source": "pick", "target": "trace" },
          { "source": "trace", "target": "body_end" }
        ]
      },
      "outputs": [
        { "name": "items", "type": "array", "from": "items" },
        { "name": "count", "type": "number", "from": "count" },
        { "name": "succeeded", "type": "number", "from": "succeeded" },
        { "name": "failed", "type": "array", "from": "failed" }
      ]
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "items", "type": "array", "from": "items", "visibleTo": { "mode": "all" } },
        { "name": "count", "type": "number", "from": "count", "visibleTo": { "mode": "all" } },
        { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
        { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "each" },
    { "source": "each", "target": "end" }
  ]
}
""";
    }

    /// <summary>
    /// ForEach + subFlowKey：循环体直接复用**已存在的种子流程** order-http-demo。
    /// 演示要点：循环体不必内联，可直接按项调用任一已发布流程（此处走 mock://echo，无需外网）。
    /// </summary>
    public static string BuildForEachSubFlowDsl()
    {
        return """
{
  "version": "1.1",
  "key": "cf-foreach-subflow-demo",
  "trigger": { "type": "Manual" },
  "inputs": [
    {
      "name": "orders",
      "type": "array",
      "required": true,
      "source": "input",
      "description": "待风控订单，元素形如 { id, qty }"
    },
    {
      "name": "token",
      "type": "string",
      "source": "input",
      "default": "demo-token"
    }
  ],
  "outputs": [
    { "name": "items", "type": "array", "from": "items", "visibleTo": { "mode": "all" } },
    { "name": "count", "type": "number", "from": "count", "visibleTo": { "mode": "all" } },
    { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
    { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "each_order",
      "type": "ForEach",
      "ref": "eachOrder",
      "itemsPath": "input.orders",
      "itemVar": "order",
      "indexVar": "seq",
      "concurrency": 1,
      "maxIterations": 20,
      "itemErrorMode": "continueOnError",
      "subFlowKey": "order-http-demo",
      "inputs": [
        { "name": "amount", "from": "input.order.qty" },
        { "name": "token", "from": "input.token" },
        { "name": "order", "from": "input.order" }
      ],
      "outputs": [
        { "name": "items", "type": "array", "from": "items" },
        { "name": "count", "type": "number", "from": "count" },
        { "name": "succeeded", "type": "number", "from": "succeeded" },
        { "name": "failed", "type": "array", "from": "failed" }
      ]
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "items", "type": "array", "from": "items", "visibleTo": { "mode": "all" } },
        { "name": "count", "type": "number", "from": "count", "visibleTo": { "mode": "all" } },
        { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
        { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "each_order" },
    { "source": "each_order", "target": "end" }
  ]
}
""";
    }

    /// <summary>
    /// Parallel（WHEN）+ ALL：三个分支并发执行后汇合。
    /// 其中 risk 分支内部含 Condition 多出边（高/低风险），用于演示"子图内同样支持分支"。
    /// </summary>
    public static string BuildParallelDsl()
    {
        return """
{
  "version": "1.1",
  "key": "cf-parallel-demo",
  "trigger": { "type": "Manual" },
  "inputs": [
    { "name": "orderId", "type": "string", "required": true, "source": "input" },
    { "name": "amount", "type": "number", "source": "input", "default": 0 }
  ],
  "outputs": [
    { "name": "basic", "type": "object", "from": "basic", "visibleTo": { "mode": "all" } },
    { "name": "risk", "type": "object", "from": "risk", "visibleTo": { "mode": "all" } },
    { "name": "audit", "type": "object", "from": "audit", "visibleTo": { "mode": "all" } },
    { "name": "ok", "type": "boolean", "from": "ok", "visibleTo": { "mode": "all" } },
    { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
    { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "fan",
      "type": "Parallel",
      "ref": "fan",
      "joinMode": "all",
      "inputs": [
        { "name": "orderId", "from": "input.orderId" },
        { "name": "amount", "from": "input.amount" }
      ],
      "branches": [
        {
          "ref": "basic",
          "inputs": [
            { "name": "orderId", "from": "input.orderId" }
          ],
          "body": {
            "inputs": [
              { "name": "orderId", "type": "string", "source": "input" }
            ],
            "nodes": [
              { "id": "b_s", "type": "Start" },
              {
                "id": "b_a",
                "type": "Assign",
                "ref": "bA",
                "inputs": [
                  { "name": "orderId", "from": "input.orderId" },
                  { "name": "status", "from": { "literal": "verified" } }
                ],
                "outputs": [
                  { "name": "orderId", "from": "orderId" },
                  { "name": "status", "from": "status" }
                ]
              },
              {
                "id": "b_e",
                "type": "End",
                "finalOutputs": [
                  { "name": "orderId", "type": "string", "from": "orderId", "visibleTo": { "mode": "all" } },
                  { "name": "status", "type": "string", "from": "status", "visibleTo": { "mode": "all" } }
                ]
              }
            ],
            "edges": [
              { "source": "b_s", "target": "b_a" },
              { "source": "b_a", "target": "b_e" }
            ]
          }
        },
        {
          "ref": "risk",
          "inputs": [
            { "name": "amount", "from": "input.amount" }
          ],
          "body": {
            "inputs": [
              { "name": "amount", "type": "number", "source": "input" }
            ],
            "nodes": [
              { "id": "r_s", "type": "Start" },
              {
                "id": "r_c",
                "type": "Condition",
                "ref": "rC",
                "items": [
                  { "no": 1, "left": "input.amount", "op": "gt", "right": 1000 }
                ]
              },
              {
                "id": "r_hi",
                "type": "Assign",
                "ref": "rHi",
                "inputs": [
                  { "name": "riskLevel", "from": { "literal": "high" } },
                  { "name": "amount", "from": "input.amount" }
                ],
                "outputs": [
                  { "name": "riskLevel", "from": "riskLevel" },
                  { "name": "amount", "from": "amount" }
                ]
              },
              {
                "id": "r_lo",
                "type": "Assign",
                "ref": "rLo",
                "inputs": [
                  { "name": "riskLevel", "from": { "literal": "normal" } },
                  { "name": "amount", "from": "input.amount" }
                ],
                "outputs": [
                  { "name": "riskLevel", "from": "riskLevel" },
                  { "name": "amount", "from": "amount" }
                ]
              },
              {
                "id": "r_e",
                "type": "End",
                "finalOutputs": [
                  { "name": "riskLevel", "type": "string", "from": "riskLevel", "visibleTo": { "mode": "all" } },
                  { "name": "amount", "type": "number", "from": "amount", "visibleTo": { "mode": "all" } }
                ]
              }
            ],
            "edges": [
              { "source": "r_s", "target": "r_c" },
              { "source": "r_c", "target": "r_hi", "combine": "1" },
              { "source": "r_c", "target": "r_lo", "isDefault": true },
              { "source": "r_hi", "target": "r_e" },
              { "source": "r_lo", "target": "r_e" }
            ]
          }
        },
        {
          "ref": "audit",
          "optional": true,
          "inputs": [
            { "name": "orderId", "from": "input.orderId" }
          ],
          "body": {
            "inputs": [
              { "name": "orderId", "type": "string", "source": "input" }
            ],
            "nodes": [
              { "id": "a_s", "type": "Start" },
              {
                "id": "a_l",
                "type": "Log",
                "ref": "aL",
                "message": "审计：订单 {{input.orderId}} 由 {{sys.userName}} 提交",
                "outputs": [{ "name": "message", "from": "message" }]
              },
              {
                "id": "a_a",
                "type": "Assign",
                "ref": "aA",
                "inputs": [
                  { "name": "orderId", "from": "input.orderId" },
                  { "name": "operator", "from": "sys.userName" },
                  { "name": "audited", "from": { "literal": true } }
                ],
                "outputs": [
                  { "name": "orderId", "from": "orderId" },
                  { "name": "operator", "from": "operator" },
                  { "name": "audited", "from": "audited" }
                ]
              },
              {
                "id": "a_e",
                "type": "End",
                "finalOutputs": [
                  { "name": "orderId", "type": "string", "from": "orderId", "visibleTo": { "mode": "all" } },
                  { "name": "operator", "type": "string", "from": "operator", "visibleTo": { "mode": "all" } },
                  { "name": "audited", "type": "boolean", "from": "audited", "visibleTo": { "mode": "all" } }
                ]
              }
            ],
            "edges": [
              { "source": "a_s", "target": "a_l" },
              { "source": "a_l", "target": "a_a" },
              { "source": "a_a", "target": "a_e" }
            ]
          }
        }
      ],
      "outputs": [
        { "name": "basic", "type": "object", "from": "basic" },
        { "name": "risk", "type": "object", "from": "risk" },
        { "name": "audit", "type": "object", "from": "audit" },
        { "name": "ok", "type": "boolean", "from": "ok" },
        { "name": "succeeded", "type": "number", "from": "succeeded" },
        { "name": "failed", "type": "array", "from": "failed" }
      ]
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "basic", "type": "object", "from": "basic", "visibleTo": { "mode": "all" } },
        { "name": "risk", "type": "object", "from": "risk", "visibleTo": { "mode": "all" } },
        { "name": "audit", "type": "object", "from": "audit", "visibleTo": { "mode": "all" } },
        { "name": "ok", "type": "boolean", "from": "ok", "visibleTo": { "mode": "all" } },
        { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
        { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "fan" },
    { "source": "fan", "target": "end" }
  ]
}
""";
    }
}
