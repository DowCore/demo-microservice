> **功能描述**见 [../logic-orchestration.md](../logic-orchestration.md)。本文件是该功能的证据附录（契约 / 评审 / 方案），不单独作为阅读入口。

# Meta.Dow 逻辑编排：能力盘点与主流实现对比（2026-10-14 刷新版）

> **文档性质**：对标分析（评审层文档）。与 `docs/detail/logic-orchestration-review.md`（缺陷清单）、`docs/detail/logic-orchestration-plan.md`（落地方案）配套阅读；**缺陷以 review 的 S/C/E/G 编号为单一真相源**，本文只做映射与归因，不重复定义缺陷。
> **证据基线**：`Meta.Dow.slnx` @ `91d75bd`（2026-10-14，P0 止血批次）。`dotnet build -c Release` **0 错误**；`tests/Meta.Dow.Orchestration.Tests` **91/91 通过**。每条结论附 `相对路径:行号`，行号对应上述 commit。
> **P0 止血批次已落地（2026-10-14）**：C2 节点类型白名单、S7 输出契约强制、S12 `visibleTo` 取值收敛（含 `roleIds` 拒绝）、S6 子图角色透传、C13 PG/Oracle 瞬态分类、C16 `batchupdate` 双缺陷。§4.2 的存量清单已随之缩减。
> **系统目标**：构造通用、稳定的逻辑编排底座 —— 其他低代码能力都在这个逻辑编排上实现（见契约文档 §1「系统目标」与 `.cursor/rules/logic-orchestration.mdc`）。
> **与 2026-10-12 版的关系**：取代工作区旧报告 `Meta.Dow-逻辑编排能力盘点与主流实现对比-2026-10-12.md`；旧版基于 `23d8e06`（R1–R4 实施前），其「运行保障期全部为零」的结论已过时，本文按当前代码重测。

---

## 0. 结论摘要

> **设计期与治理期强于多数同类；运行保障期经 R1–R4 从"白纸"补到"有骨架"，但骨架上仍有三根承重梁没搭：异步结果回收（R5）、幂等运行时去重（R6）、持久化执行（R7）。**

| 判断 | 内容 |
|---|---|
| **最强的三条** | ① 出参契约带**角色可见性**（接口层按 roles+permissions 过滤，`FinalOutputAssembler` × `OutputVisibilityFilter`）② **发布前图级校验 + 可达性 BFS + `FlowUsage` 调用影响索引**三者闭环 ③ **统一调度器** `RunSegmentAsync`（主链/循环体/并行分支同一个 stepper，控制体继承完整 End 语义） |
| **最危险的三个空洞** | ① `async` 节点仍是 `Task.Run` fire-and-forget（C1：结果与失败全丢，实例却判 Succeeded）② 无节点类型白名单（C2：`sql`/`switch` 发布成功、调用即 500）③ 无最终契约时整包外泄 `ctx.Vars`（S7）＋ 子图整体旁路可见性（S6） |
| **结构性天花板** | 单次 burst + 200 步游标；`FlowInstanceStatus` 仅 4 态，无断点续跑（E7）—— 与 Temporal/Airflow 类「事件溯源 + 重放」是两种物种 |
| **不该补的** | 连接器生态（1000+ SaaS 连接器）、拖拽渲染引擎、审批流（已有独立引擎）、分布式 worker 集群（在事务/补偿解决前扩容只会放大半成品数据） |

---

## 1. 已实现能力盘点（按层，全部带证据）

### 1.1 设计期

| 能力 | 实现物 | 证据 | 状态 |
|---|---|---|---|
| X6 画布 | 节点面板 / 属性面板 / 端口连接 | `vue-demo` `shared/create-flow-x6.ts`、`definitions/designer.vue` | ✅ |
| 图态与执行态**分离** | 画布存 `GraphJson`，引擎只读 `DslJson` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/FlowDefinition.cs` | ✅ |
| DSL 版本号 | `DslVersion = "1.1"` | `.../Meta.Dow.SaaS.Domain.Shared/Orchestration/OrchestrationConsts.cs` | ✅ |
| 共享编辑器 | 条件组 / 过滤组 / 出参契约 / 掩码 / 输出映射 / Cron 等 | `vue-demo` `apps/web-antd/src/views/orchestration/shared/*-editor.vue` 共 12 个 | ✅ |

### 1.2 发布前校验（相对多数引擎做得深）

`FlowExecutor.ValidateDsl`（`.../Orchestration/FlowExecutor.cs:2388` 起）静态校验项：

| 校验 | 说明 |
|---|---|
| 出边基数 | `ValidateOutEdgeCardinality`（`FlowExecutor.cs:2597`）：仅 Condition 允许多出边；End 必须 0 出边；可达节点需 ≥1 出边 —— 把「看起来正常却只跑一条边」的静默失效提前变成显式错误 |
| 可达性 | `CollectReachable` BFS（`FlowExecutor.cs:2712`）：不可达节点显式跳过（区分死节点与故意留白） |
| 控制流结构 | ForEach 必配 `itemsPath` + 循环体或 `subFlowKey`；Parallel ≥2 分支、ref 唯一、≤16；嵌套深度 ≤5 |
| 超时取值 | `timeoutMs` 必须 0（不限）或 `NodeMinTimeoutMs..NodeMaxTimeoutMs` 区间（R2，防「配了等于没配」） |
| 写重试声明 | `retryOnWrite` 必须配 `idempotencyKey`（R3 声明层校验；运行时去重 R6 未做） |

> ⚠️ 仍缺：**节点类型白名单**（C2，未知类型发布通过、运行期 500，评审 P0-1）、**输出契约强制**（S7，P0-2）、**未知 DSL 字段拒绝**（静默忽略，评审 P1-2）。

### 1.3 执行期

| 能力 | 实现物 | 证据 | 状态 |
|---|---|---|---|
| 统一调度原语 | `RunSegmentAsync` 返回 `SegmentOutcome`，主链 / ForEach 体 / Parallel 分支共用 | `FlowExecutor.cs:278` | ✅ |
| 控制流原语 | Condition（多出边 + combine/isDefault/firstMatch ≈ switch-case-default）、ForEach/Loop（failFast/continueOnError + concurrency）、Parallel/When（all/any/allSettled） | `FlowExecutor.cs:690,759,767` | ✅ |
| 节点库 | dispatch 共 32 个 case 标签（含别名） | `FlowExecutor.cs:642-792` | ✅ |
| 节点级超时 | `timeoutMs` 单点包裹 `DispatchNodeAsync`；Parallel 分支三级继承 | `FlowExecutor.cs:586-611` | ✅ R2 |
| 嵌套与环保护 | `callStack` 共享命名空间 + `MaxSubFlowDepth=5` + `MaxControlNestingDepth=5` | `FlowExecutor.cs` | ✅ |
| 并发安全 | 每调用复制 `callStack`；内联子图 JSON 深拷贝 | `BuildInlineBodyDsl`（`FlowExecutor.cs:1987`） | ✅ |
| 步数护栏 | `DefaultMaxSteps = 200` | `FlowExecutor.cs:248` | ⚠️ 硬编码（R10 未做） |

### 1.4 运行保障期（R1–R4 后的新现状）

2026-10-12 版写「retry/timeout/idempot 全 0 命中」——已不成立。当前实测：

| 能力 | 实现 | 证据 | 剩余缺口 |
|---|---|---|---|
| HTTP 重试语义 | 非安全方法（POST/PUT/DELETE）**禁重试**；总预算 60s / 单次 10s / 重试 2 次 + 抖动 | `src/shared/Meta.Dow.ServiceDefaults/.../Extensions.cs:43` `DisableForUnsafeHttpMethods()` | 属全局默认，非节点级策略 |
| 节点级超时 | 见 §1.3 | `FlowExecutor.cs:586-611` | `async` 分支不受包裹（C1 同类） |
| 非 HTTP 写弹性 | `NodeResiliencePolicy`：读重试 2 次、写默认不重试；`retryOnWrite` 须配幂等键 | `.../Orchestration/NodeResiliencePolicy.cs` | PG/Oracle 瞬态分类失效（C13）；幂等键运行时去重未做（R6） |
| 批量写事务 | batchcreate/batchupdate/batchdelete 复用 `BatchAsync` 单事务；`/batch-delete` 逐条走 `DeleteFlowKey` | `.../Orchestration/ResourceCrudExecutor.cs:602-760` | `batchupdate` 双缺陷（C16：未合并 `filter.Args`、新戳既当新值又当旧值） |

**仍是零的能力**（2026-10-14 全 Orchestration 目录实测）：

| 能力 | 命中 | 对应缺陷 |
|---|---|---|
| **引擎自定义** span / 指标（`ActivitySource` / `Meter`） | 0 文件 —— 注意：宿主层 OTel 已注册（`src/shared/Meta.Dow.ServiceDefaults/Microsoft/Extensions/Hosting/Extensions.cs:114,120,131`），有 HTTP/EF/日志自动采集，缺的是**编排节点级**埋点 | R8 |
| checkpoint / resume / replay | 0 文件 | E7 / R7 |
| 死信 / 熔断 | 0 | R5 配套 |
| 跨进程缓存失效 | 仅进程内 IMemoryCache + 发布点失效 | C3 / E8 |

### 1.5 治理期（最强的一层）

| 能力 | 实现物 | 证据 |
|---|---|---|
| 入参契约 | `InputRuleValidator`：类型/形状校验，不通过即拒 | `.../Orchestration/InputRuleValidator.cs` |
| 出参契约 | `finalOutputs` + `map` + `aggregate` + `visibleTo` | `.../Orchestration/FinalOutputAssembler.cs` |
| **角色可见性** | 运行时按 roles+permissions 过滤出参字段 | `.../Orchestration/OutputVisibilityFilter.cs` |
| 日志脱敏 | `MaskValueHelper.RedactForLog` 日志专用脱敏 | `.../Orchestration/MaskValueHelper.cs` |
| 写权限闸门 | `Orchestration.Sql.Write` 统一覆盖 SQL / Mongo / Redis / 资源 CRUD 写 | `CodeSandboxExecutor.cs:410`、`MongoDataExecutor.cs:298`、`RedisDataExecutor.cs:256`、`ResourceCrudExecutor.cs:539` |
| 代码沙箱 | Roslyn `CSharpScript` + 8s 硬超时 + 静态分析 + 行数上限 | `.../Orchestration/CodeSandboxExecutor.cs` |
| 版本治理 | Draft/Published + `FlowVersion` 不可变快照 + `FlowUsage` 影响面索引 | `.../Domain/Orchestration/FlowDefinition.cs`、`FlowUsage.cs` |

### 1.6 接入期

REST 发布（`LogicApiAppService.RunAsync`，含按角色过滤）｜ RabbitMQ / Kafka / MQTT 三个真实消费者｜ Cron + 时区 + `NextFireAt` 预计算｜ 与 OA 审批引擎经 `BeforeStartFlowKey`/`AfterEndFlowKey` 松耦合。

### 1.7 「底座被消费」的实证（2026-10-14 核查，回应系统目标）

系统目标要求「其他低代码能力都在这个逻辑编排上实现」。核查结论：**这不是口号，当前代码已是这个形状** —— 但实现方式是「**低代码能力整体住在编排模块内**」，而非「各自独立、调用编排」。

| 消费方 | 是否经编排 | 证据 |
|---|---|---|
| 资源 CRUD（AppResource） | ✅ 五个 op **全部**经 `flowKey` 发起 | `AppResourceAppService.cs:120,124,128,132,136`（query/get/create/update/delete → `InvokeAsync(code, r => r.XxxFlowKey, …)`）；批量删除亦逐条走 `DeleteFlowKey`（`:176`） |
| 报表查询（ReportDefinition） | ✅ 查询经流程 | `ReportDefinitionAppService.cs:305-306` `_invoker.InvokeAsync(entity.QueryFlowKey, …)`；未发布时显式报错（`:195`） |
| OA 审批 | ✅ 前后置钩子 | `WorkflowRuntimeAppService.cs:57`（`BeforeStartFlowKey`）、`:351`（`AfterEndFlowKey`） |
| 表单 / 表设计 / 数据源 / 触发层 | ✅ 与编排同模块 | 应用服务**物理位于** `.../Meta.Dow.SaaS.Application/Orchestration/`（`FormDefinitionAppService` / `TableDefinitionAppService` / `DataSourceAppService` / `FlowTriggerAppService` / `MessageSourceAppService` 等 18 个） |

> **含义**：目标态与现状**结构一致**，因此「其他低代码在编排之上实现」是**已有事实**，可作为后续改动的既有约束而非待办。
> **代价**：所有负载都在 `Orchestration/` 一个目录（也正是 P2-1「拆分 SaaS」要处理的对象）；且 `AppResource.CreateFlowKey` 等默认指向 `sys.*` 系统流程（`SystemResourceFlowSeedContributor`），意味着**平台自带的 CRUD 也从同一条编排路径走**，编排层的缺陷会被所有低代码能力同步放大 —— 这是「P0 止血」优先于「补新节点」的又一个理由。

---

## 2. 主流实现对比

### 2.1 五个对照阵营

| 阵营 | 代表 | 与我们的关系 |
|---|---|---|
| A. 代码逻辑编排 | LiteFlow | **对标主线**（契约文档 §17 已定） |
| B. iPaaS 自动化 | n8n、Zapier、Make | 借体验（错误工作流、执行可重放），不借形态 |
| C. AI 工作流 | Dify、Coze | 借「发布为 API / 调试」，不借产品中心 |
| D. 持久化执行引擎 | Temporal、Airflow、Camunda、Elsa | **运行保障层的真正参照物** |
| E. 国内低代码 | 宜搭、简道云、微搭 | 同类 D 类产品；我们赢在资产模型，输在交付闭环 |

### 2.2 十维对比矩阵（2026-10-14 重评）

图例：**强** = 开箱即有且成熟 / **中** = 有但弱或需自行补齐 / **弱** = 基本没有

| 维度 | 我方 | iPaaS | 持久化引擎 | AI 工作流 | 国内低代码 |
|---|---|---|---|---|---|
| 入参/出参契约化 | **强** | 中 | 中 | 弱 | 中 |
| 出参角色级可见性 | **强** | 弱 | 弱 | 弱 | 中 |
| 控制流原语完整度 | **强**（缺 while/range/break，E5） | 强 | 强 | 中 | 中 |
| 数据接入与库内计算 | **强**（6 类数据源） | 中 | 弱 | 弱 | 中 |
| 连接器生态 | 弱 | **强** | 弱 | 中 | **强** |
| 失败重试与错误策略 | **中偏下**（R1/R3 后：HTTP 读与非 HTTP 读有重试；写需显式 retryOnWrite；异步/节点级策略仍缺） | **强** | **强** | 中 | 中 |
| 补偿 / 事务一致性 | 弱（R4 后有**单节点批量事务**；跨节点无 txMode（E4）、无补偿） | 中 | **强** | 弱 | 中 |
| 持久化执行（续跑/重放） | **弱（无）**（E7） | **强** | **强** | 中 | 中 |
| 可观测与调试 | 中（有时间线与节点记录；无 OTel/指标，R8） | **强** | **强** | 中 | 中 |
| 版本与影响面治理 | **强** | 中 | 中 | 中 | 中 |
| 嵌入宿主（作为库被调） | **强** | 中 | **强** | 中 | 弱 |

**读法**：优势集中在左列上半部（契约、控制流、数据接入、治理、嵌入）；劣势集中在运行保障四格。R1–R4 把「失败重试」从"弱"抬到"中偏下"，「事务」从"无"抬到"单节点级"——但「持久化执行」和「异步结果回收」仍是硬缺口。

### 2.3 逐阵营的得失

#### vs LiteFlow（对标主线）
- **我们赢**：X6 画布 + 属性面板 + DSL 规范化（LiteFlow 本体是规则文件）；发布前图级校验；出参角色可见性；`FlowUsage` 影响面索引。
- **我们输**：EL 表达式生态与热更新成熟度；`WHEN` 有 `maxWaitSeconds`——我方 `BranchTimeoutMs` 已实现（R2）但 `async` 分支超时不受包裹。
- **同构**：都主张「控制流 + 已注册组件」，都明确「不做审批」。

#### vs n8n / Zapier（iPaaS）
- **我们赢**：`RunAsync(flowKey, ctx)` 可被业务代码当库调用；出参契约 + 角色过滤 + 日志脱敏；发布前校验；库内计算与元数据编目。
- **我们输（差距最大）**：节点级重试策略 + **错误工作流** + 部分重跑 + 执行数据 pin；连接器数量；queue mode 分布式执行。
- **结论**：n8n 的「执行可重放、失败可续跑」是产品级基础设施，仍是我们最该抄的一块（对应 R5/R6/R7）。

#### vs Temporal / Airflow / Camunda（持久化引擎）
- **我们赢**：零额外基础设施；DSL 由设计器产出；领域组件 vs 通用 Activity。
- **我们输（物种级差距）**：事件溯源 + 确定性重放；Activity 至少一次 + 幂等；Saga/补偿；Timers/Signals；历史可回放调试。
- **关键判断**：不需要变成它们，但必须借「给每个外部副作用的下一步加护栏」这条纪律 —— R6 幂等键运行时落地是这条纪律的第一个兑现点。

#### vs 宜搭 / 简道云 / 微搭（国内低代码）
- **我们赢**：`flowKey` 是跨应用**共享资产**（它们多为应用内私有）；代码调用入口；审计与脱敏粒度。
- **我们输**：生态与开箱体验；它们把低代码做成了完整交付闭环，我们的应用外壳层仍在补（见《中台对标》v2）。

---

## 3. 优势（逐条给理由）

1. **出参契约做成了权限系统，不只是数据结构。** 接口层过滤（`FinalOutputAssembler` × `OutputVisibilityFilter.IsVisible`）让「同一份数据对不同角色露出不同字段」成为引擎能力。对照：n8n/Temporal 无此概念；宜搭类多在页面层遮字段 —— 接口层过滤才是「数据不出后端」的强保证。⚠️ 当前被子图旁路（S6）与未知 mode 兜底放行（S12）削弱，是 P0-4/P0-3 的修复对象。
2. **发布前图级校验 + 可达性 BFS。** 出边基数、End 零出边、子图结构、不可达节点显式豁免 —— 把一整类「永远不生效的连线」拦在发布前。多数引擎留到运行时才炸。
3. **统一调度器，控制体继承完整 End 语义。** 循环体/分支体天然拥有 `finalOutputs`/`map`/`aggregate`/`visibleTo`。「控制体内外语义一致」是很少见的干净设计（n8n 的 Loop 子节点与主流程语义有差异）。
4. **图态与执行态分离，引擎不信任前端。** 前端改坏不污染执行；DSL 可被外部程序生成。
5. **版本快照 + 缓存失效 + `FlowUsage` 影响面索引三者闭环。** n8n 有版本但不告诉你谁在调用你 —— 这是「改一处炸一片」的直接对策。⚠️ 缓存失效只挂 `PublishAsync`（C3），三者闭环目前缺「跨进程失效」一角（E8）。
6. **代码沙箱是认真的。** Roslyn + 8s 硬超时 + 静态分析 + 行数上限 + `Sql.Write` 权限闸门 + 日志独立脱敏。⚠️ 静态分析是正则词法（S19）、`WITH` 前缀可绕过只读（S4），边界已知。
7. **触发入口是真实消费者。** RabbitMQ/Kafka/MQTT 三个 `BackgroundService` + Cron + REST + 表单/领域事件。
8. **「不做审批」的边界纪律守住了。** OA 是独立引擎，经 `BeforeStartFlowKey`/`AfterEndFlowKey` 松耦合，与 LiteFlow 官方边界一致。

---

## 4. 不足与可完善项（全量对照 review 编号）

> 编号说明：S/C/E/G 为 `logic-orchestration-review.md` §4 的缺陷编号；R 为 `remediation-plan.md` 的整改项；P0/P1/P2 为 review §5 的批次。全部缺陷均已对照当前代码实测（2026-10-13/14 复核）。

### 4.1 已修复项回顾（R1–R4 @ 2026-10-13；P0 止血批次 @ `91d75bd` 2026-10-14，勿再当缺陷报）

| 项 | 修复内容 | 残留关联缺陷 |
|---|---|---|
| R1 | HTTP 非安全方法禁重试（`DisableForUnsafeHttpMethods`）+ 弹性参数显式化 | 节点级 HTTP 重试策略仍无（属 R8/运行保障 P1） |
| R2 | `timeoutMs` 单点包裹 + Parallel 三级继承 + 取值校验 | `async` 分支不受包裹（并入 C1）；MaxSteps 硬编码（R10） |
| R3 | `NodeResiliencePolicy` 读写分离 + `retryOnWrite` 声明校验 | R6（幂等运行时去重）；**C13 已在本批次修掉** |
| R4 | 批量三入口 + 单事务 + `/batch-delete` 走 DeleteFlowKey | 跨节点事务仍无（E4/R11）；**C16 已在本批次修掉** |
| **P0 止血批次** | **C2** 节点类型白名单 ｜ **S7** 输出契约强制（`ValidateOutputContract`）｜ **S12** `visibleTo` mode 收敛 + `roleIds` 拒绝 ｜ **S6** 子图角色透传；同批带走 **C13**（PG/Oracle 分类）、**C16**（`batchupdate` 双缺陷） | 均 ✅（回归测试 `FlowExecutorDslValidationTests` 29 例）；残留：**C1**（异步结果回收，根治属 R5）、P0-5～P0-14 未做 |

### 4.2 存量 P0 级（止血类）

> **2026-10-14 更新**：原表中的 C2 / S7 / S6 / S12 / C16 / C13 已随 P0 止血批次修复，移入 §4.1。以下为**仍未修**项。

| 编号 | 缺陷 | 代码证据（2026-10-14 复核） |
|---|---|---|
| C1 | `async` 节点 `Task.Run(..., CancellationToken.None)`：不写 outputs/JobId、失败仅日志、节点永停 `AsyncQueued` 而实例判 Succeeded、不受 timeoutMs 包裹、不调 `failWhen` | `FlowExecutor.cs:901-930, 723-731`（0c4f7eb 时点） |
| S1–S5 | 表列 `Origin` 客户端可控；破坏性 DDL 门禁缺失；默认口令 `1q2w3E*` 硬编码；`WITH` 绕过只读；Http 节点无 SSRF 防护 | 见 review §4.1，2026-10-13 全部实测确认存活 |

### 4.3 存量 P1 级（运行保障欠账）

| 编号 | 缺陷 | 说明 |
|---|---|---|
| R6 | 幂等键运行时去重未做 | R3 只完成「声明即校验」；是写重试与续跑（R7）的共同前置 |
| R5 | 异步节点持久化 + Wait 语义未做 | C1 的根治方案；方案 §10.4 承诺过 Wait |
| C3/E8 | 发布缓存：失效只挂 `PublishAsync`；进程内缓存跨副本不共享（TTL 10 分钟）→ 已下线流程仍可被调用 | `PublishedFlowCache.cs:38, 104-112`；`FlowDefinitionAppService.cs:290` |
| S14/E10 | 无单流程调用权限码 | `/api/logic/{flowKey}` 仅需 `Instances.Run`（`LogicApiAppService.cs:35`） |
| S8/P1-8 | `skipValidation:true` + 自带 resourceCode/filter → 报表/资源级授权与 DataScope 可绕过 | `LogicApiAppService.cs:61` |
| S9/P1-7 | `DataPermissionQueryableExtensions` 全仓零调用点 → 数据范围是配置界面而非执行约束 | 2026-10-13 实测 |
| E4/R11 | `txMode=sameDataSource` 不存在，且未知 DSL 字段静默忽略 | 跨节点写链非原子 |
| C4/C6/C7/C8 | OA 侧：驳回语义、原地改写、补丁丢失、无并发戳 | 见 review §4.2，全部实测存活 |
| G 系列 | `null!` 脚手架（HTTP/SQL/Code/资源 CRUD 路径测试必炸）、7/7 Dockerfile 不可构建、分析器无门禁、`src/CLAUDE.md` 失真 | review §4.4 |

### 4.4 结构性 P2 级（需排期决策）

| 编号 | 项 | 判断 |
|---|---|---|
| E7/P2-4 | 持久化执行 / 断点续跑 | 引擎适用边界的根本问题；低成本版 = `Paused` + `ResumeFrom(nodeId)`（R7），强依赖 R6 |
| E5/P2-5 | while / range-for / break / continue、独立 Sql 节点、Wait | 控制流补齐；SWITCH 的执行能力已存在（Condition 多出边），缺的只是 DSL 互译与画布命名（R9，最便宜的补齐项） |
| R8 | OpenTelemetry：每节点一个 span + 4 个引擎指标 | 当前全 Orchestration 目录 0 文件命中 |
| P2-1 | 拆分 SaaS：编排执行器 + 数据源/表设计抽为独立服务 | SaaS 占全仓约 78% 代码，README 自列的头号风险 |

### 4.5 明确不做（与目标一致的负向约束）

连接器市场（iPaaS 的产品中心，做了就变 n8n 劣化复制）｜ 拖拽页面渲染引擎（与「资产寄存器 ≠ 渲染层」边界冲突）｜ 审批流并入编排器（已有独立引擎）｜ 分布式 worker 集群（事务/补偿解决前扩容只放大半成品数据）。

---

## 5. 根因：一次 burst 型执行器，承担了联网级调用面

```
单次 burst 执行（.NET 内存中 for 循环推进游标）
   ├─ 无跨节点事务边界 → 因为"一次调用 = 一个请求"，事务本应由调用方负责
   ├─ 重试/补偿曾经为零 → 因为失败了整体重来，不需要局部恢复
   ├─ 无断点续跑       → 因为一次调用要么跑完要么失败，没有"中间态"
   └─ async 无回收     → 因为"不阻塞返回"就够了，没人消费结果
```

这套语义在原始设计场景下是**正确**的：方案 §2.6 写明「逻辑被代码调用，而不是替代整个系统」—— 此时**调用方的 IUnitOfWork 就是事务边界**。

**问题出在：系统已经越过这条安全线，护栏还没补齐。**

| 越过安全线的动作 | 越线后必须补的 | 当前状态 |
|---|---|---|
| 发布为 REST API（`LogicApiAppService`） | 幂等键、重试策略、错误语义 | 重试语义已修（R1/R3）；幂等键运行时未做（R6） |
| 接入 Rabbit/Kafka/MQTT 触发 | 消费幂等、死信、重试退避 | 均未做 |
| 提供 `async: true` 节点 | 结果回收、失败告警、持久化任务 | 全缺（C1/R5） |
| 提供批量写 | 批量事务或补偿链 | 单节点批量事务已有（R4）；跨节点补偿无 |
| 多副本部署 | 发布缓存跨进程失效 | 无（C3/E8） |

> **一句话根因**（与 2026-10-12 版一致，仍然成立）：这套引擎按「被代码内嵌调用」设计，却被当「对外服务」在用。R1–R4 补上了最先漏水的四块板；剩下的 P0/P1 项是同一笔身份升级欠账的其余部分。

---

## 6. 与缺陷清单 / 落地方案的执行顺序对照

```
止血（review §5.1 P0，全部小改动）
  C2 节点白名单 → S7 无契约报错 → S12 mode 报错 → S6 子图透传角色
  （配套：C16 batchupdate、C13 PG/Oracle 分类 —— P1-10/P1-11 可同批带走）

运行保障（review §5.2 P1 / plan R 序列，顺序不可颠倒）
  R6 幂等键运行时落地        ← R3 写重试的前置、R7 续跑的前置
  R5 异步持久化 + Wait       ← 根治 C1
  R7 Paused + ResumeFrom     ← 强依赖 R6
  C3/E8 缓存失效广播          ← 多副本正确性
  R11 txMode                  ← 依赖 R4（已完成）
  R8 OpenTelemetry            ← 依赖 R3 埋点位置

结构性（review §5.3 P2）
  P2-4 持久化执行评估 → R9 SWITCH 互译 → P2-5 控制流补齐
```

**验收纪律**：每项的验收标准以 review §5 的表为准；实现完成后必须同步更新三份文档（契约 §14 标记已修、review 标注落地 commit、plan 落地状态），并实测构建/测试基线。

---

## 7. 一页总结

| | 评价 |
|---|---|
| **目标是否成立** | ✅ 成立。「通用稳定型编排底座 + 其他低代码作为载体」与资产寄存器模型、统一调度器、契约化治理完全自洽；已固化为 `.cursor/rules/logic-orchestration.mdc`。 |
| **设计期** | ✅ 强于多数主流实现（发布前图级校验 + 影响面索引 + 契约化）。 |
| **执行期** | ✅ 干净（统一调度器、控制体语义继承、并发安全）。 |
| **治理期** | ✅ 最强项（角色级出参可见性 + 日志脱敏 + 写权限闸门 + 沙箱）。 |
| **运行保障期** | ⚠️ R1–R4 后「有骨架无承重梁」：重试语义/超时/写弹性/批量事务已落地；异步结果回收（R5）、幂等运行时（R6）、持久化执行（R7）、跨进程缓存（E8）、可观测（R8）仍缺。 |
| **最大风险** | 已对外提供 REST / MQ / async 节点，可靠性语义未全部兑现 —— 承诺先于能力。 |
| **最该抄的** | n8n 的「节点级重试 + 错误工作流 + 执行可重放」；Temporal 的「每个外部副作用都有护栏」这条纪律。 |
| **最该守的** | 不做连接器市场；不做审批；不把资产内联进应用；不做第二个引擎。 |
