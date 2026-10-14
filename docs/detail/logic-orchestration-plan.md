> **功能描述**见 [../logic-orchestration.md](../logic-orchestration.md)。本文件是该功能的证据附录（契约 / 评审 / 方案），不单独作为阅读入口。

# Meta.Dow 逻辑编排：差距补齐实施方案

> 编制日期：2026-10-13
> 依据文档：《逻辑编排能力盘点与主流实现对比》2026-10-12、《中台对标与系统目标评估》v2、《项目综合评估与估值》
> 证据基准：`demo-microservice` @ `23d8e06`（规划编制时）→ 实施后 `267d375`（R1–R4 落地）、`588ff03`（文档基线重构）、`91d75bd`（P0 止血批次）；`vue-demo` @ `5d6e69a`。后文「落地状态」章节以实施后的提交为准。
> **本文档只谈逻辑编排这一块，且只写"怎么落地"。** 结论性评价见上列三份报告，本文不重复论证。

---

## 0. 前置：先守住优势，再补短板

补齐的前提是**不破坏已经站住的东西**。以下五条是本次方案的设计约束，任何一项改动如果与它们冲突，方案作废：

| # | 要守住的优势 | 具体约束（写进代码审查清单） |
|---|---|---|
| 1 | **出参角色级可见性**（引擎级数据管控，真正的差异化） | 新节点的输出**必须**经 `FinalOutputAssembler` 装配，不得绕过 `visibleTo` 直接写 `results` |
| 2 | **发布前图级静态校验**（错误拦在发布前，不是运行时） | 新增 DSL 字段/节点类型**必须**同步加 `ValidateDsl` 校验项，禁止只加执行不加校验 |
| 3 | **统一调度器**（主链/循环体/分支共用一个 stepper） | 禁止引入第二套执行循环；控制体一律走 `RunControlBodyAsync` → `ExecuteAsync` |
| 4 | **图态与执行态分离**（引擎不信任前端） | 前端新增字段只落 `graphJson`，引擎侧校验一律以 `dslJson` 为准 |
| 5 | **资产寄存器模型**（不把资产内联进应用） | 本方案涉及的导出/打包，**只导出引用清单 + 被引用资产的快照，不导出一份"内联副本"** |

> 这五条不是形式主义。第 1、2 条一旦破，本系统就从"引擎级管控"退回"业务代码里手写 if"；第 5 条一旦破，寄存器模型退化成容器模型，与宜搭无差别。

---

## 1. 差距总览与实施排序

把三份报告的缺口合并去重后，按「**是否已对外承诺 + 是否有明确产品落点 + 修复成本**」三维排序。**先修"承诺了没兑现"的，再修"能力欠账"，最后才是"增强"** —— 顺序本身就是方案的一部分。

| 编号 | 差距 | 来源 | 对外承诺 | 产品落点 | 成本 | 优先级 |
|---|---|---|:---:|---|:---:|:---:|
| **R1** | HTTP 节点重试语义错位（该关的没关） | 本轮修订 | ✅ 已发布 REST | 所有 HTTP 写节点 | **极低** | **P0** |
| **R2** | `TimeoutMs` / `BranchTimeoutMs` 声明即弃用 | 盘点 P0-1 | ✅ 方案 §10.4 | 属性面板已暴露该字段 | 低 | **P0** |
| **R3** | 非 HTTP 写节点无弹性（DB/Redis/Mongo） | 本轮新发现 | ✅ `flowKey` 可发布 | ForEach 批量写 | 中 | **P0** |
| **R4** | `ResourceCrudExecutor` 无批量入口 + 无事务 | 盘点 P0-4 | ✅ 前端已在用 | `app/runtime.vue:511` 逐条删 | 中 | **P0** |
| **R5** | `async` 节点 fire-and-forget，结果与异常进黑洞 | 盘点 P0-3 | ✅ 方案 §10.4 承诺 Wait | 异步 HTTP 节点 | 中高 | **P1** |
| **R6** | 无幂等键 | 盘点 P1-1 | ✅ REST + 三 MQ 触发 | 所有对外触发口 | 低 | **P1** |
| **R7** | 无断点续跑（`FlowInstanceStatus` 仅 4 态） | 盘点 P1-2 | ⚠️ 试运行已暗示 | 长流程 / 大批量 ForEach | 中 | **P1** |
| **R8** | 可观测性是"记录"不是"观测" | 盘点 P1-3 | ❌ | 运维 | 低 | **P1** |
| **R9** | `SWITCH` 缺 DSL 互译与画布命名 | 综合评估 #3 | ✅ 主方案对标物 | 设计器 | 低 | **P2** |
| **R10** | `DefaultMaxSteps=200` 硬编码、语义含糊 | 盘点 P1-4 | ❌ | 报错体验 | 极低 | **P2** |
| **R11** | `txMode=sameDataSource` 未实现 | 综合评估 #5 | ✅ 文档主推 | MongoDB 写链 | 中高 | **P2** |
| **R12** | 前端设计器/渲染器零测试 | 综合评估 #6 | ❌ | 回归防护 | 中 | **P2** |

**不建议做**（守住边界，理由见 §8）：连接器市场、拖拽渲染引擎、审批流合并、分布式 worker 集群、OLAP 引擎。

> **排序逻辑说明**：R1/R2 之所以压过 R3/R4，是因为它们属于**同一类缺陷——"静默失效"**：用户配置了、系统接受了、但行为与配置不符，且**全程无任何提示**。这类缺陷对平台信任度的伤害远大于"功能缺失"，因为使用者无法通过试错发现它。本系统刚在 `23d8e06` 用 `ValidateOutEdgeCardinality` 清掉了同一类问题，应趁热打铁把这一类彻底扫干净。

---

## 2. P0 实施项

### R1　修正 HTTP 重试语义（最高性价比）

**问题（本轮实测修订）**：`SaaS.HttpApi.Host/Program.cs:21` → `AddServiceDefaults()` → `ConfigureHttpClientDefaults` 注册 `AddStandardResilienceHandler()`，**对全部 HttpClient 生效**。该处理器默认**对所有 HTTP 方法重试 3 次**（含 POST/PUT/DELETE），而全仓**没有任何 `DisableForUnsafeHttpMethods()` 调用**。编排 HTTP 节点会发 POST（`FlowExecutor.cs:887`）→ **非幂等写操作被静默重试，可能产生重复数据**。

> 注：上一轮报告写的"无重试"是错的（关键词搜索未命中 `AddStandardResilienceHandler` 所在的 `ServiceDefaults` 工程）。真实风险更隐蔽：**不是没有重试，是重试装错了位置**。

**改法**（`src/shared/Meta.Dow.ServiceDefaults/Microsoft/Extensions/Hosting/Extensions.cs:33-40`）：

```csharp
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler(options =>
    {
        // 关键：默认不对非安全方法重试。编排的 HTTP 写节点不应被隐式重放。
        options.Retry.DisableForUnsafeHttpMethods();

        // 与编排语义对齐：节点级超时缺省 30s（见 R2），此处总预算给足重试空间
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(500);
        options.Retry.UseJitter = true;   // 防重试风暴
    });
    http.AddServiceDiscovery();
});
```

**配套**：`SaaSApplicationModule.cs:23-29` 里 `OrchestrationHttpCall` 的 `client.Timeout = 30s` 与外层总超时冲突（30s 外层会截断 60s 总预算），**改为不设或设为 90s**，把超时决策权交给弹性管道 + 节点级配置。

**验收标准**：
1. 新增单测：对 `POST` 请求注入一次 503，断言**未发生重试**（可用 `HttpMessageHandler` 计数桩）；
2. 对 `GET` 请求注入两次 503，断言**发生了重试且最终成功**；
3. 属性面板 HTTP 节点上，若正在编辑的方法为非幂等（POST/PUT/DELETE），显示提示："写操作不会自动重试，如需重试请显式配置 `retry` 并确认目标接口幂等。"

---

### R2　让 `TimeoutMs` 真正生效（或拒绝接受）

**问题**：`FlowDslModels.cs:223,376,492` 定义了 `TimeoutMs` / `BranchTimeoutMs`，**全 saas 服务零消费**。用户在属性面板配了超时，系统静默忽略；循环体/并行分支内的超时更是完全不存在。

**改法**（`FlowExecutor.cs`，在节点 dispatch 外层包裹）：

```csharp
// 统一在 ExecuteAsync 的节点调用点包裹，而非每个 case 内部
private async Task<T> WithNodeTimeoutAsync<T>(
    FlowDslNode node, CancellationToken outerCt, Func<CancellationToken, Task<T>> body)
{
    var ms = node.TimeoutMs ?? OrchestrationConsts.NodeDefaultTimeoutMs; // 新增常量 30000
    if (ms <= 0) return await body(outerCt);                            // <=0 表示不限，显式约定

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
    cts.CancelAfter(ms);
    try
    {
        return await body(cts.Token);
    }
    catch (OperationCanceledException) when (!outerCt.IsCancellationRequested)
    {
        throw new UserFriendlyException(
            $"节点「{node.Id}」({node.Type}) 执行超时（{ms} ms）。可通过 timeoutMs 调整，或置 0 关闭。");
    }
}
```

**Parallel 分支**：`BranchTimeoutMs` 已在 DSL 中（`FlowExecutor.cs` 的 `ExecuteParallelAsync` 内），
按 `分支.TimeoutMs ?? node.BranchTimeoutMs ?? node.TimeoutMs ?? 默认 30s` 逐级继承。

**新增常量**（`OrchestrationConsts.cs`）：
```csharp
/// <summary>节点级默认超时（ms）。0 表示不限。</summary>
public const int NodeDefaultTimeoutMs = 30_000;
/// <summary>节点级超时上限，防止误配超大值使护栏失效。</summary>
public const int NodeMaxTimeoutMs = 600_000;
```

**校验**（`ValidateDsl` 新增，与优势 2 保持一致）：
- `timeoutMs < 0` → 报错；`timeoutMs > NodeMaxTimeoutMs` → 报错；
- `branchTimeoutMs` 仅在 `Parallel` 节点上允许（其他节点配了报错，防止"配了不生效"重演）。

**验收标准**：① HTTP 节点 `timeoutMs: 100` + mock 延迟端点 → 断言抛出含 `执行超时（100 ms）` 的 `UserFriendlyException`；② Parallel 某分支超时 → 断言 `failed[].ref` 为该分支且 `joinMode: all` 时整体失败；③ 配 `timeoutMs: -1` → 断言发布前校验拒绝。

---

### R3　非 HTTP 写节点的弹性与超时

**问题**：DB / Redis / Mongo 写节点**不经过 HttpClient**，因此 `AddStandardResilienceHandler` 对它们完全无效（这是 R1 修订后才能看清的盲区）。`ResourceCrudExecutor` / `ParameterizedSqlExecutor` / `MongoDataExecutor` / `RedisDataExecutor` 各自裸调驱动，无重试、无超时（仅有 code 沙箱的 15s 命令超时，且有上限 500/1000 行）。

**改法**：**不要**给这些执行器各写一套重试（会造成四套不一致策略）。改用统一的**执行器级弹性包装**：

```csharp
// 新增：Orchestration/NodeResiliencePolicy.cs
public interface INodeResiliencePolicy
{
    Task<T> ExecuteAsync<T>(
        string nodeId, string operation, bool isWrite,
        Func<CancellationToken, Task<T>> action, CancellationToken ct);
}
```

实现要点：
1. **读操作**（`query` / `get`）：重试 3 次、指数退避 500ms 基 + jitter、仅重试瞬态异常（连接类）。
2. **写操作**（`create` / `update` / `delete`）：**默认不重试**（与 R1 保持一致的原则），
   但**允许节点显式声明** `"retryOnWrite": true`，此时必须同时提供幂等键（见 R6）否则 `ValidateDsl` 报错。
3. 超时一律走 R2 的 `WithNodeTimeoutAsync`，不在此层重复。

**验收标准**：① 读节点遇瞬态连接异常 → 重试并成功；② 写节点遇同样异常 → **立即失败不重试**；③ 写节点声明 `retryOnWrite: true` 但无幂等键 → 发布前校验报错。

---

### R4　`ResourceCrudExecutor` 批量入口 + 单事务（唯一有明确产品落点的修复）

**问题**：`IResourceCrudExecutor` 只有单一入口 `ExecuteAsync(op, nodeInput, isDryRun, ct)`（`ResourceCrudExecutor.cs:16-24`），内部 `CreateAsync`/`UpdateAsync`/`DeleteAsync` 各自提交。前端为此只能写 `for` 循环逐条删（`app/runtime.vue:511`、`reports/runtime.vue:540`）→ **中途失败产生半成品数据**，且 `concurrency > 1` 时更明显。

**改法**：给现有入口**增加两个 op**（不加新接口方法，避免破坏既有调用方）：

```csharp
// ExecuteAsync 的 switch 增加：
"batchcreate" => await BatchCreateAsync(provider, dataSource, resource!, schema, nodeInput, isDryRun, ct),
"batchupdate" => await BatchUpdateAsync(...),
"batchdelete" => await BatchDeleteAsync(...),
```

**事务边界**：
- PostgreSQL / MySQL / SQL Server / Oracle：`IDbTransaction` 包住整批，一次 `CommitAsync`；
- MongoDB：**单机无跨文档事务**（已知 P1-3 风险）→ 走 `txMode=sameDataSource` 的既有限制，
  文档与属性面板**必须标注**"MongoDB 批量写非原子"；
- 批量上限沿用 `CodeDbBatchMaxRows = 500`，超出报错。

**输入契约**（与既有 `payload` 习惯一致）：
```json
{ "resourceCode": "customer", "op": "batchcreate",
  "items": [ { ... }, { ... } ], "stopOnError": true }
```
**输出契约**：`{ "succeeded": 2, "failed": [{ "index": 2, "error": "..." }], "affected": 2 }`

**前端配套**（`app/runtime.vue`）：把逐条 `for` 删改为单次 `batchdelete` 调用；
**这是"顺手解掉一个已有 workaround"**，属本方案里投入产出比最高的一处。

**验收标准**：① 批量插 3 条其中第 2 条违反唯一约束 → `stopOnError: true` 时断言**全部回滚**（表内 0 条）；② `stopOnError: false` 时断言 2 条成功、`failed` 含 index 1；③ 前端批量删 100 条 → 断言**只发一次请求**。

---

## 3. P1 实施项

### R5　`async` 节点改为持久化执行

**问题**：`FlowExecutor.cs:872-903` 用 `Task.Run(..., CancellationToken.None)`，catch 只 `LogWarning`。结果不回收、异常不上抛、无死信、进程重启即消失；且 DryRun 下异步被改成同步，**掩盖了生产差异**。

**改法**：分两步，**先做"可观测 + 不丢"，后做"可等待"**。

- **步骤 1（本次做）**：异步任务落 **`FlowInstance.Nodes` 的独立子记录**（复用 `NodeExecutionRecord`，
  新增 `FlowInstanceStatus.AsyncPending` 语义字段），失败写 `Error` 并触发**失败回调流程**
  （新增 DSL 字段 `onAsyncFailureFlowKey`，为空则记 `LogError` + 指标）。同时补 **`Wait` 语义的最低版**：
  节点可声明 `async: true` + `awaitResult: true`，此时退化为同步执行（与 DryRun 行为一致），
  **明确告知用户"等待结果 = 同步执行"**。
- **步骤 2（下一迭代）**：改为持久化出站队列（落库 → 后台消费者执行），彻底解决重启丢失。
  > 不要一步到位上队列：在 R4 的事务边界落地前，异步 + 重试 + 无事务三者叠加会让数据状态更难推理。

**属性面板**：勾选"异步"时显示明确提示：**"异步节点的执行结果不会被后续节点引用；失败不会中断主流程。"** —— 这句话现在只存在于代码注释里，用户看不到。

**验收标准**：① 异步节点失败 → 断言 `FlowInstance` 有可查询的失败记录且指标 +1；② 配 `awaitResult: true` → 断言主流程阻塞至完成。

---

### R6　幂等键（对外触发口统一）

**问题**：`idempot` 全仓 0 命中。REST + RabbitMQ/Kafka/MQTT 触发无去重，上游重投即重复执行。**此项是 R1/R3 允许写重试的前置条件**。

**改法**：
1. `LogicApiAppService.RunAsync` 接受 `Idempotency-Key` 请求头（可选）；
2. `FlowInstance` 增 `IdempotencyKey`（可空）+ **唯一索引（`TenantId` + `IdempotencyKey`）**；
3. 命中已存在记录 → **返回首次执行的结果**（不再执行），HTTP 语义用 `200` + 响应头 `Idempotent-Replay: true`；
4. 三类 MQ 触发：用消息自带的 `MessageId` 作为幂等键（缺失则告警并跳过去重）。

**验收标准**：① 同 key 调两次 → 断言第二次**未产生新 `FlowInstance`** 且返回相同结果；② 不同 key → 各执行一次；③ 并发同 key → 断言唯一索引生效、无重复执行。

---

### R7　`Paused` 状态 + `ResumeFrom(nodeId)`（低成本续跑，不上 Temporal）

**问题**：`FlowInstanceStatus` 仅 `{ Running, Succeeded, Failed, Cancelled }`，失败只能整体重跑。与 R1/R3 的重试叠加时，重跑代价可能高于失败本身。

**改法**：**不引入工作流引擎**，用已有的 `NodeExecutionRecord` 做最小可用续跑：
1. 枚举增 `Paused`；
2. `EnableRetryableFlow` 等价的节点级 `idempotent: true` 声明（**必填**才允许续跑该节点）；
3. `FlowInstance` 增 `LastCompletedNodeId`；
4. 新增 API `ResumeFromAsync(instanceId, nodeId)`：从指定节点重建 `ctx`（用 `VariablesJson` 快照）
   继续执行；**未声明 `idempotent: true` 的前置节点必须重新执行或显式跳过**，二者都要在 UI 上让用户选。

> 关键设计约束：**续跑的安全性依赖幂等声明**。没有 R6 的幂等键，续跑就是在赌副作用不重复。因此 R7 必须排在 R6 之后。

**验收标准**：① 3 节点流程在第 2 个失败 → `ResumeFrom(第2个)` → 断言第 1 个**未重复执行**；② 第 1 个未声明幂等 → 断言 API 拒绝并提示。

---

### R8　OpenTelemetry：每个节点一个 span + 4 个引擎指标

**问题**：全 saas 服务 0 处 `ActivitySource` / `Meter`。有 `FlowInstance` + `NodeExecutionRecord`（含 `DurationMs`）+ 前端时间线，但无分布式追踪、无引擎级指标。

**好消息**：`ServiceDefaults/Extensions.cs:96-101` **已配好 OpenTelemetry**（metrics/tracing + `AddHttpClientInstrumentation`），
只是**没有任何业务侧的 `ActivitySource`**。所以这是**接线工作，不是新建基建** —— 成本比上一轮报告估的低。

**改法**：
```csharp
// Orchestration/OrchestrationDiagnostics.cs
public static class OrchestrationDiagnostics
{
    public const string SourceName = "Meta.Dow.Orchestration";
    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName);
}
```
- `FlowExecutor` 每个节点执行包一个 `Activity`（标签：`flow.key` / `node.id` / `node.type` / `attempt`）；
- 四个指标：`orchestration.executions`（计数，按 Status）/ `orchestration.node.duration`（直方图）/
  `orchestration.retries`（计数）/ `orchestration.async.pending`（可观察 Gauge）。

**验收标准**：① 启动时 `OrchestrationDiagnostics.SourceName` 出现在 tracer 注册列表；② 跑一条流程 → 断言导出的 span 数与节点数一致；③ 指标端点有四个新指标。

---

## 4. P2 实施项

### R9　`SWITCH` 的 DSL 互译 + 画布命名

`SWITCH` 在**执行能力上已经存在**（`Condition` 多出边 + `combine` 组合式 + `isDefault` 兜底 + 默认策略 `firstMatch`，见 `FlowExecutor.ResolveNextNode`），缺的只是：
1. DSL ↔ `SWITCH(x).TO(a, b).DEFAULT(c)` 文本互译（对标 liteflow-editor-client）；
2. 画布上把 `Condition` 的"多路枚举"形态**命名为 Switch**，与"真假二元"形态区分。

**这是"承认已有能力"的工作，不是新建能力。** 优先级低于 P0/P1 是因为它不解决任何正确性问题。

### R10　`DefaultMaxSteps` 解耦与报错区分

`DefaultMaxSteps = 200` 硬编码（`FlowExecutor.cs:248`）。问题不在数值，在**报错无法区分"循环太长 / 真有环 / 迭代超限"**。
改法：控制体内部**单独计数**（不占主链步数预算）；错误信息三分类，并附当前节点链快照。

### R11　`txMode=sameDataSource`

文档 §5.4.7 已有完整设计，是 MongoDB 无跨文档事务的**唯一补丁**。但**必须排在 R4 之后**：
R4 解决"单节点内批量原子"，R11 解决"跨节点同源原子"，顺序颠倒会先做难的。

### R12　前端设计器/渲染器补测试

现有 336 测试全在 `shared/` 工具层；`designer.vue`（1,640 行）与 `app/runtime.vue`（921 行）零覆盖。
**优先级低不代表不重要，而是因为它不阻塞交付**；但 R1/R2/R4 的前端部分落地时，应同步补对应的组件测试。

---

## 5. 落地批次与依赖关系

```
批次一（互不依赖，可并行，全部本周）
  R1 HTTP 重试语义修正   ← 独立
  R2 节点级 TimeoutMs    ← 独立
  R10 MaxSteps 报错区分  ← 独立（顺手做）
        ↓
批次二（依赖批次一的超时/弹性基建）
  R3 非 HTTP 写节点弹性
  R8 OpenTelemetry 接线  ← 依赖 R3 的埋点位置
        ↓
批次三（事务与幂等是续跑的前置，顺序不可颠倒）
  R4 批量入口 + 单事务   ← 前端 workaround 同步改
  R6 幂等键              ← R3 写重试的前置
        ↓
批次四
  R7 Paused + ResumeFrom ← 强依赖 R6
  R11 txMode             ← 依赖 R4
        ↓
批次五（增强，不阻塞）
  R5 异步持久化（步骤 1）→ 步骤 2
  R9 SWITCH 互译
  R12 前端测试
```

**关键依赖（写错顺序会返工）**：
- **R6 必须在 R3 的"写重试"之前** —— 否则允许了写重试却没有幂等保护；
- **R6 必须在 R7 之前** —— 续跑的安全性完全建立在幂等之上；
- **R4 必须在 R11 之前** —— 先做单节点原子，再做跨节点原子。

---

## 6. 每项的统一验收模板

为避免"改完不知道算不算完"，所有实施项一律走同一张表（**这是流程约束，不是建议**）：

| 检查项 | 要求 |
|---|---|
| 引擎单测 | 新增用例进 `tests/Meta.Dow.Orchestration.Tests`，且 `dotnet test` 全绿（**当前基线 91/91**，2026-10-14 P0 批次后；本模板 2026-10-13 编制时基线为 62/62） |
| 发布前校验 | 若新增 DSL 字段，`ValidateDsl` 必须有对应校验项（守优势 2） |
| 契约同步 | `FlowDslModels.cs` + 前端 `dsl-graph.ts` 双向读写必须同步（避免 P1-1 双写漂移加剧） |
| 文档 | `docs/detail/logic-orchestration-contract.md` 对应章节更新（该文档是主方案，不能落后于代码） |
| 种子示例 | 涉及新语义的，在 `ControlFlowDemoSeedContributor` 加一条演示流程 |
| 前端类型 | `pnpm --filter @vben/web-antd typecheck` 0 诊断 |

---

## 7. 一句话总结

**本方案的主体不是"加功能"，而是"把已承诺的可靠性语义兑现"**：
R1~R4 全部属于"系统接受了配置却在行为上打折"这一类，其中 R1/R2 是**成本极低、收益立竿见影**的两刀。

补齐后本系统在编排能力上会呈现一个很干净的结构：
> **设计期（已有，强）+ 治理期（已有，最强）+ 运行保障期（本次补齐）** —— 三层齐平，"库内数据 + 领域组件 + 被代码调用"的生态位才算真正闭环。

---

## 8. 明确不做（守住边界）

| 不做 | 理由 |
|---|---|
| 连接器市场（1000+ SaaS） | iPaaS 的产品中心，做了就是 n8n 的劣化复制；我们的生态位是"库内数据 + 领域组件" |
| 拖拽页面渲染引擎 | 渲染已由表单/报表设计器覆盖；与"聚合层 ≠ 渲染层"边界冲突 |
| 审批流并入编排器 | 已有独立引擎且松耦合（`BeforeStartFlowKey` / `AfterEndFlowKey`）；LiteFlow 官方边界同样不做审批 |
| 分布式 worker 集群 | **在 R4 的事务边界落地前，扩容只会让半成品数据更多** |
| OLAP 引擎 / 数据湖仓 | 独立赛道（对标 ClickHouse / Doris），不是低代码平台该背的成本 |
| **把资产内联进应用** | 架构红线：一旦放弃 `code` 引用，资产寄存器模型退化为容器模型，核心差异化归零 |

---

## 附：本轮证据修订说明

上一轮报告《逻辑编排能力盘点与主流实现对比》的 **P0-2「全链无重试，HTTP 层无弹性策略」有误**，本方案予以更正：

| | 上一轮结论 | 本轮实测 |
|---|---|---|
| HTTP 弹性 | ❌ 无 Polly / 无 `AddStandardResilienceHandler` | ✅ **已有**，且在 `ConfigureHttpClientDefaults` 全局注册（`ServiceDefaults/Extensions.cs:33-40`） |
| 真实问题 | "没有重试" | **"重试装错位置"**：默认对所有 HTTP 方法重试（含 POST），全仓无 `DisableForUnsafeHttpMethods()` |
| 仍未兑现 | — | **节点级**语义：`TimeoutMs` 零消费；DB/Redis/Mongo **不经过 HttpClient**，完全无弹性 |

**误判原因**：与上一轮"用命名代替架构"同源 —— 在 `FlowExecutor.cs` 和 `saas` 目录内搜 `retry`，而策略实际注册在**共享工程 `Meta.Dow.ServiceDefaults`**。教训已并入项目长期纪律：**凡"某能力不存在"的结论，必须跨工程搜索后再下。**

---

# 落地状态（2026-10-13 实施）

> 本节记录实际实施结果，与前文规划逐项对应。**全部改动均通过验证门禁**（后端 62/62 测试、前端 336/336 测试 + vue-tsc + eslint 全绿）。

## 已完成：R1 / R2 / R3 / R4（P0 四项）

| 项 | 状态 | 后端落点 | 前端落点 |
|---|:---:|---|---|
| **R1** HTTP 重试语义 | ✅ | `ServiceDefaults/Extensions.cs`：`DisableForUnsafeHttpMethods()` + 显式超时/重试调参；`SaaSApplicationModule.cs`：HttpClient `Timeout` 改 `InfiniteTimeSpan`（交给弹性管道决策） | 无（纯后端） |
| **R2** 节点级超时 | ✅ | `FlowExecutor.cs`：抽出 `DispatchNodeAsync`，`ExecuteNodeAsync` 统一包裹 `timeoutMs`；`Parallel` 分支三级超时继承（branch→branchTimeoutMs→node）；`OrchestrationConsts` 新增 3 个常量；`ValidateDsl` 加超时范围校验 | `dsl-graph.ts` + `designer.vue`：`timeoutMs` 已可配置并在面板暴露 |
| **R3** 非 HTTP 写节点弹性 | ✅ | **新增 `NodeResiliencePolicy.cs`**（读写分离：读重试 2 次指数退避+jitter、写默认不重试、瞬态异常白名单跨 4 种 DB 驱动）；`ResourceCrudExecutor` 在 op 分派处单点包裹；`CodeSandboxExecutor` 的 `db.query/get/mget/find/findOne` 走同一策略；**新增 DSL `retryOnWrite`**（写重试必须配 `idempotencyKey`，否则 `ValidateDsl` 报错） | `dsl-graph.ts`：`retryOnWrite` 字段双向映射；`designer.vue`：资源写节点的「写操作弹性」开关 + 风险提示 |
| **R4** 批量入口 + 单事务 | ✅ | `ResourceCrudExecutor`：`batchcreate/batchupdate/batchdelete` 三个 op，复用 `ParameterizedSqlExecutor.BatchAsync` 事务原语；`FlowExecutor` 加 3 个节点类型别名 + `items` 必填校验；**新增 `/batch-delete` 端点**（`AppResourceAppService.BatchDeleteRecordsAsync` 逐条走 `DeleteFlowKey` 流程、部分失败继续、返回 affected/skipped/errors）；新增 `ResourceBatchInvokeResultDto` 等 3 个 DTO + 本地化键 | `orchestration.ts`：`batchDeleteAppResourceRecordsApi`；`app/runtime.vue` + `reports/runtime.vue`：逐条 `for` 删 → **单次批量调用** |

## 关键设计守则（实施中贯彻）

1. **单点包裹**：R2 包在唯一的 `ExecuteNodeAsync`，R3 包在唯一的 op 分派处 —— 新增节点/op 不会漏包，杜绝"配了不生效"重演。
2. **复用优先**：R4 的事务、参数化、行数上限、回滚**全部**复用既有 `BatchAsync`，未新建任何事务机制。
3. **批量仍走流程**：`/batch-delete` **没有**绕过编排去打表，仍是逐条调用资源的 `DeleteFlowKey`（删一条 = 跑一次编排），保住"删除逻辑可编排"这一既有优势。
4. **危险开关需配保护**：`retryOnWrite` 是唯一新增的可选 DSL 字段，且强制要求 `idempotencyKey` —— 纪律落进代码。
5. **Mongo 不假装原子**：批量写仅支持 SQL 家族，Mongo 显式拒绝并给出明确错误。

## 验证结果

```
后端：dotnet build Meta.Dow.slnx -c Release   → 0 error
      dotnet test Meta.Dow.Orchestration.Tests → 62 passed / 0 failed
前端：vue-tsc --noEmit                         → 0 error
      vitest run（编排域）                      → 336 passed / 3 skipped
      eslint（5 个改动文件）                    → 0 error
```

## 未做（留待后续，理由见 §8）

R5（async 持久化）、R6（幂等键运行时落地）、R7（Paused/ResumeFrom）、R8（OpenTelemetry）、
R9（SWITCH 互译）、R10（MaxSteps 错误区分）、R11（txMode）、R12（前端设计器测试）。
其中 **R6 是 R3 的 `retryOnWrite` 真正实用的前置** —— 当前校验层已强制要求声明幂等键，但
"键在运行时如何真正做到去重"属 R6 范围，本次仅落地了**声明即校验**这一半。

---

# 落地状态（2026-10-14 补充：P0 止血批次）

> 本节记录 **P0-1 ~ P0-4** 与评审 **C13 / C16** 的落地结果，对应提交 `91d75bd`。
> 与上一节（R1–R4）不同的是：R1–R4 是**能力补齐**，本批是**止血**——修的是「配了不生效 / 静默通过 / 越权可见」这类必须在上生产前堵死的缺口。

## 已完成：P0-1 ~ P0-4（评审 S6 / S7 / S12 / C2）

| 项 | 状态 | 落点 | 语义 |
|---|:---:|---|---|
| **P0-1** 节点类型白名单 | ✅ | `FlowExecutor.cs`：新增 `SupportedNodeKinds`（**32 种**）+ `ValidateDsl` 内白名单循环 | 与 `DispatchNodeAsync` 的 case 列表**一一对应**；`sql` / `switch` / `wait` **故意不在表内**——它们能被发布成功却无分发分支，调用即 500。改为**发布前拒绝**（宁可发布失败，不要静默） |
| **P0-2** 输出契约强制 | ✅ | `FlowExecutor.cs`：新增 `ValidateOutputContract` + `KnownVisibleToModes` + `ValidateVisibleTo`；`FinishOutputsAsync` 无契约分支在非 dryRun 时抛异常 | 流程**必须显式声明输出契约**（根 `outputs` 或每个 `End.finalOutputs`）；dryRun 保留全量 `ctx.Vars` 便于调试。同时把 `visibleTo` 取值在**发布前**收敛为白名单 |
| **P0-3** 非 Condition 多出边失效 | ✅ | `FlowExecutor.cs`：`ValidateOutEdgeCardinality` 的 `multiOutAllowed` **只保留 `Condition`**（移除 `Switch`） | SWITCH 的事实语义是「Condition 多出边 + `combine`」，不存在独立 `switch` 节点；原先允许 `Switch` 多出边等于放行一个**无分发分支**的配置 |
| **P0-4** 子图角色透传 | ✅ | `FlowExecutor.cs`：`filterOutputsByRole` / `currentUser` 贯通 `RunSegmentAsync` → `ExecuteNodeAsync` → `DispatchNodeAsync` → `ExecuteSubFlowAsync` / `ExecuteForEachAsync` / `ExecuteParallelAsync` / `RunControlBodyAsync`；3 处硬编码 `false, null` 调用点改为透传 | 修的是**出参角色可见性在子图旁路**：子流程/循环/并行体内的节点原先一律不走 `OutputVisibilityFilter`，等于把差异化能力在子图上关掉 |

## 已完成：C13 / C16（补丁未含，本次一并实现）

| 项 | 状态 | 落点 | 语义 |
|---|:---:|---|---|
| **C13** PG/Oracle 瞬态分类失效 | ✅ | `NodeResiliencePolicy.cs`：PG 改为**字符串前缀判断** `IsTransientPostgresSqlState`（`08*` + `40001/40P01/53200/53300/53400/55P03/57P01/57P02/57P03`）；补 Oracle 码 `60/8177/3113/3114/12170/12541/12571/12547/12537/1033/1089` | 旧实现 `int.TryParse(pg.SqlState, out s)` —— PG 的 SQLSTATE 是**字母数字**（如 `40001` ok，但 `08P01` 解析失败 → 0），必然误判非瞬态。Oracle 同理。这是「重试策略在非 SQL Server 上静默失效」 |
| **C16** 批量更新的乐观锁与过滤参数 | ✅ | `ResourceCrudExecutor.cs` `BatchOp.Update`：按表列探测 `ConcurrencyStamp`，逐条写 `pNewStamp` / 读旧戳到 `pOldStamp`，`filter.Args` **在列参数之后**合并；SQL 追加 `SET …=@pNewStamp` 与 `WHERE (…IS NULL OR …=@pOldStamp)` | 与单条 `UpdateAsync`（`:459-486`）语义对齐：**写新戳、比旧戳**。旧批量路径既不写戳也不比戳 → 乐观锁在批量入口被绕过，且 `filter.Args` 丢失导致过滤条件不生效 |

## 本批纪律（写进代码审查清单）

1. **白名单必须与分派表一一对应**：`SupportedNodeKinds` 与 `DispatchNodeAsync` 的 case 列表互为镜像，新增节点必须同时改两处 + 加校验测试。
2. **契约与可见性在发布前收敛**：「DSL 声明了但引擎未消费」「无契约即全量外泄」两类都改为**发布期拒绝**，不留运行时惊喜。
3. **子图不得旁路横切语义**：新增承载子图的执行函数时，`filterOutputsByRole` / `currentUser` **必须**作为参数贯通，否则角色可见性在子图失效。
4. **瞬态分类不得依赖数值转换**：跨厂商错误码一律按**字符串**匹配，且策略文件必须对每种驱动各留一条单测。

## 验证结果（`91d75bd`）

```
后端：dotnet build Meta.Dow.slnx -c Release      → 0 error
      dotnet test tests/Meta.Dow.Orchestration.Tests → 91 passed / 0 failed
      （P0 批次新增 29 例：白名单 5 + 契约 5 + visibleTo 6 + PG SQLSTATE 11 + 存量夹具补齐 2）
```

## 未做（P0 批次的范围外，仍在评审 §5 排期）

- 评审 §5.1 的 **P0-5 ~ P0-14** 本批未动（含默认口令 `1q2w3E*`、破坏性 DDL 门禁、Http 节点 SSRF 防护等，见 review §4.1 S1–S5）。
- **G2（测试覆盖实质为零）未改善**：本批 29 例仍全部是纯单元测试（断言 `ValidateDsl` 的拒绝/放行），脚手架 `null!` 注入导致的**运行时路径零覆盖**依然存在。
- `FlowExecutor.cs` 因本批净增约 190 行，§2–§13 的旧行号已整体下移（见 `logic-orchestration-contract.md` 头部时点声明），**替换为符号名检索前不要按旧行号跳转**。

