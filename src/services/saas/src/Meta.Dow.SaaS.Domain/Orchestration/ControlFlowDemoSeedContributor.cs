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
/// 3. <c>cf-parallel-demo</c>       —— Parallel 三分支，各自调用已发布流程并声明入参/出参
/// 4. <c>cf-line-check</c>          —— ForEach 循环体是一段脚本：逐行清洗、计价、打风险标
///
/// 与 <see cref="FlowDefinitionDataSeedContributor"/> 同样遵循"开发期刷新演示 DSL"的口径：
/// 已存在则 draft 覆盖 + published 版本号自增，便于随代码演进持续修正演示。
/// </summary>
public class ControlFlowDemoSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string ForEachDemoCode = "cf-foreach-demo";

    public const string ForEachSubFlowDemoCode = "cf-foreach-subflow-demo";

    public const string ParallelDemoCode = "cf-parallel-demo";

    public const string LineCheckDemoCode = "cf-line-check";

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
                "cf-branch-basic",
                "控制流演示：分支流程·订单核对",
                BuildBranchBasicDsl()
            );
            await SeedOneAsync(
                context?.TenantId,
                "cf-branch-risk",
                "控制流演示：分支流程·金额风险",
                BuildBranchRiskDsl()
            );
            await SeedOneAsync(
                context?.TenantId,
                "cf-branch-audit",
                "控制流演示：分支流程·提交审计",
                BuildBranchAuditDsl()
            );
            await SeedOneAsync(
                context?.TenantId,
                ParallelDemoCode,
                "控制流演示：并发聚合（Parallel）",
                BuildParallelDsl()
            );
            await SeedOneAsync(
                context?.TenantId,
                LineCheckDemoCode,
                "控制流演示：逐行处理（ForEach 脚本）",
                BuildLineCheckDsl()
            );
        }
    }

    private async Task SeedOneAsync(Guid? tenantId, string code, string name, string dslJson)
    {
        var existing = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == code);
        if (existing != null)
        {
            existing.ClearSystemMark();
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
            isSystem: false
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

    /// <summary>被并行分支调用：核对订单，返回 orderId、status，另带一个调用点不取的 note。</summary>
    public static string BuildBranchBasicDsl()
    {
        return """
{
  "version": "1.1",
  "key": "cf-branch-basic",
  "trigger": { "type": "Manual" },
  "inputs": [
    { "name": "orderId", "type": "string", "required": true, "source": "input" }
  ],
  "outputs": [
    { "name": "orderId", "type": "string", "from": "orderId", "visibleTo": { "mode": "all" } },
    { "name": "status", "type": "string", "from": "status", "visibleTo": { "mode": "all" } },
    { "name": "note", "type": "string", "from": "note", "visibleTo": { "mode": "all" } }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "pick",
      "type": "Assign",
      "ref": "pick",
      "inputs": [
        { "name": "orderId", "from": "input.orderId" },
        { "name": "status", "from": { "literal": "verified" } },
        { "name": "note", "from": { "literal": "internal" } }
      ],
      "outputs": [
        { "name": "orderId", "from": "orderId" },
        { "name": "status", "from": "status" },
        { "name": "note", "from": "note" }
      ]
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "orderId", "type": "string", "from": "orderId", "visibleTo": { "mode": "all" } },
        { "name": "status", "type": "string", "from": "status", "visibleTo": { "mode": "all" } },
        { "name": "note", "type": "string", "from": "note", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "pick" },
    { "source": "pick", "target": "end" }
  ]
}
""";
    }

    /// <summary>被并行分支调用：金额大于 1000 标 high，否则 normal。</summary>
    public static string BuildBranchRiskDsl()
    {
        return """
{
  "version": "1.1",
  "key": "cf-branch-risk",
  "trigger": { "type": "Manual" },
  "inputs": [
    { "name": "amount", "type": "number", "source": "input", "default": 0 }
  ],
  "outputs": [
    { "name": "riskLevel", "type": "string", "from": "riskLevel", "visibleTo": { "mode": "all" } },
    { "name": "amount", "type": "number", "from": "amount", "visibleTo": { "mode": "all" } }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "gate",
      "type": "Condition",
      "ref": "gate",
      "items": [ { "no": 1, "left": "input.amount", "op": "gt", "right": 1000 } ]
    },
    {
      "id": "high",
      "type": "Assign",
      "ref": "high",
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
      "id": "normal",
      "type": "Assign",
      "ref": "normal",
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
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "riskLevel", "type": "string", "from": "riskLevel", "visibleTo": { "mode": "all" } },
        { "name": "amount", "type": "number", "from": "amount", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "gate" },
    { "source": "gate", "target": "high", "combine": "1" },
    { "source": "gate", "target": "normal", "isDefault": true },
    { "source": "high", "target": "end" },
    { "source": "normal", "target": "end" }
  ]
}
""";
    }

    /// <summary>被并行分支调用：记下订单、操作人和已审计标记。</summary>
    public static string BuildBranchAuditDsl()
    {
        return """
{
  "version": "1.1",
  "key": "cf-branch-audit",
  "trigger": { "type": "Manual" },
  "inputs": [
    { "name": "orderId", "type": "string", "required": true, "source": "input" }
  ],
  "outputs": [
    { "name": "orderId", "type": "string", "from": "orderId", "visibleTo": { "mode": "all" } },
    { "name": "operator", "type": "string", "from": "operator", "visibleTo": { "mode": "all" } },
    { "name": "audited", "type": "boolean", "from": "audited", "visibleTo": { "mode": "all" } }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "log",
      "type": "Log",
      "ref": "log",
      "message": "审计：订单 {{input.orderId}} 由 {{sys.userName}} 提交"
    },
    {
      "id": "pick",
      "type": "Assign",
      "ref": "pick",
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
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "orderId", "type": "string", "from": "orderId", "visibleTo": { "mode": "all" } },
        { "name": "operator", "type": "string", "from": "operator", "visibleTo": { "mode": "all" } },
        { "name": "audited", "type": "boolean", "from": "audited", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "log" },
    { "source": "log", "target": "pick" },
    { "source": "pick", "target": "end" }
  ]
}
""";
    }

    /// <summary>
    /// Parallel：三条分支分别调用已发布流程。调用点声明入参和出参，
    /// basic 的出参不含被调流程里的 note，用于确认出参投影生效。
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
          "subFlowKey": "cf-branch-basic",
          "inputs": [
            { "name": "orderId", "from": "input.orderId" }
          ],
          "outputs": [
            { "name": "orderId", "from": "orderId" },
            { "name": "status", "from": "status" }
          ]
        },
        {
          "ref": "risk",
          "subFlowKey": "cf-branch-risk",
          "inputs": [
            { "name": "amount", "from": "input.amount" }
          ],
          "outputs": [
            { "name": "riskLevel", "from": "riskLevel" },
            { "name": "amount", "from": "amount" }
          ]
        },
        {
          "ref": "audit",
          "optional": true,
          "subFlowKey": "cf-branch-audit",
          "inputs": [
            { "name": "orderId", "from": "input.orderId" }
          ],
          "outputs": [
            { "name": "orderId", "from": "orderId" },
            { "name": "operator", "from": "operator" },
            { "name": "audited", "from": "audited" }
          ]
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

    /// <summary>
    /// ForEach 的循环体是一段脚本：对当前行清洗 sku、计算 amount、按数量打 riskLevel。
    /// qty &gt; 100 为 high，否则 normal。return 的对象就是 items 的一项。
    /// </summary>
    public static string BuildLineCheckDsl()
    {
        return """
{
  "version": "1.1",
  "key": "cf-line-check",
  "trigger": { "type": "Manual" },
  "inputs": [
    {
      "name": "lines",
      "type": "array",
      "required": true,
      "source": "input",
      "description": "明细，元素形如 { sku, qty }"
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
      "itemVar": "line",
      "indexVar": "index",
      "concurrency": 2,
      "maxIterations": 50,
      "itemErrorMode": "continueOnError",
      "loopBody": {
        "inputs": [],
        "nodes": [
          { "id": "body_start", "type": "Start" },
          {
            "id": "body_code",
            "type": "Code",
            "ref": "eachRow",
            "script": "const line = input[\"line\"];\nconst index = Number(input[\"index\"] || 0);\nconst row = line || {};\nconst sku = String(row.sku || '').trim();\nconst qty = Number(row.qty || 0);\nconst price = Number(row.price || 0);\nreturn {\n  sku: sku,\n  qty: qty,\n  price: price,\n  amount: qty * price,\n  riskLevel: qty > 100 ? 'high' : 'normal',\n  keep: Boolean(sku) && qty > 0,\n  index: index\n};"
          },
          {
            "id": "body_end",
            "type": "End",
            "finalOutputs": [
              { "name": "row", "type": "object", "from": "return", "promote": true, "visibleTo": { "mode": "all" } }
            ]
          }
        ],
        "edges": [
          { "source": "body_start", "target": "body_code" },
          { "source": "body_code", "target": "body_end" }
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
}
