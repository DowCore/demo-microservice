> **功能描述**见 [../logic-orchestration.md](../logic-orchestration.md)。本文件是该功能的证据附录（契约 / 评审 / 方案），不单独作为阅读入口。

# Meta.Dow 低代码逻辑编排 — 实现契约

> **文档性质**：**事实契约**。本文描述代码的**真实行为**，不是规划书。
> **代码基线**：`Meta.Dow.slnx` @ commit `91d75bd`（2026-10-14，**P0 止血批次**：S6/S7/S12/C2 已修，C13/C16 已修）+ **轨道A 整改批次**（A1/A3/A4/A5/A7/A8 + 控制流语义）+ **校验覆盖范围补齐批次**（§14.0 末四行）。`tests/Meta.Dow.Orchestration.Tests` = **297/297 通过**（基线 91）。
> **警告数说明**：`267d375`（2026-10-13）实测记162 警告；165 与 162 的差值来自 NU1902/NU1903 审计类警告随NuGet 漏洞库波动（restore 实测存在该类警告），与代码无关。
> **核对方式**：文中每条结论都可在**符号名**处复核。
> ⚠️ **行号时点**：整改批次在 `FlowExecutor.cs` 等文件**净增数百行**，且多轮改动持续移动位置，因此
> **全文行号一律按符号名检索**（`EvaluateConditionItem` / `ExecuteWaitAsync` / `ValidateDsl` 等）。本轮已把 §14 与 §2 中的主要落点改为符号名，彻底清理需一次独立的文档治理 pass。
> **状态图例**：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划，见 §15） ｜ 🐞 已知缺陷（见 §14）｜ ✅已修（缺陷已修复，§14 中就地标注）
> **前端影响**：前端在 `D:\Project\vue-demo`（不在本仓），本仓文档无法回答「界面上是否可用」。前端需跟进的清单见[评估文档 §8.8](./logic-orchestration-assessment.md)。
> **重构说明**：本文由 2026-10-13 的代码核对重构而成，取代此前把「目标态规格」与「现状事实」混写的版本。旧版中大量重复四遍的节点规格已合并为 §3–§9 的单一契约；规划内容统一收进 §15，与契约严格隔离。旧版全文见 git 历史。

## 目录

1. [定位与边界](#1-定位与边界)
2. [现状能力总表（唯一权威表）](#2-现状能力总表唯一权威表)
3. [DSL 契约](#3-dsl-契约)
4. [节点目录](#4-节点目录)
5. [节点 I/O 与结果信封](#5-节点-io-与结果信封)
6. [条件与分支](#6-条件与分支)
7. [控制流原语：ForEach / Parallel / SubFlow](#7-控制流原语foreach--parallel--subflow)
8. [Code 沙箱与数据访问](#8-code-沙箱与数据访问)
9. [最终输出与角色可见性](#9-最终输出与角色可见性)
10. [发布校验](#10-发布校验)
11. [执行语义与运行保障](#11-执行语义与运行保障)
12. [API 契约](#12-api-契约)
13. [权限目录](#13-权限目录)
14. [行为边界与已知缺陷](#14-行为边界与已知缺陷)
15. [规划（未实现）](#15-规划未实现)
16. [操作说明与设计器规格](#16-操作说明与设计器规格)
17. [对标与明确不做](#17-对标与明确不做)
18. [参考链接](#18-参考链接)
19. [修订记录](#19-修订记录)

---

## 1. 定位与边界

### 系统目标（本系统的北极星）

**构造通用、稳定的逻辑编排底座 —— 其他低代码能力都在这个逻辑编排上实现。**

- 逻辑编排（D 类 / LiteFlow 语系）= **主方案**；
- 表单、报表、OA 审批、资源 CRUD、表设计器、触发层 = **功能性实现**（编排的载体，不是独立产品）；
- 改动判定标准：任何改动先回答「它让底座更通用、更稳定，还是让某个功能绕开底座？」—— 后者默认拒绝，确有必要须显式登记理由。

该目标已固化为 Cursor 规则：后端 `.cursor/rules/logic-orchestration.mdc`（架构红线、稳定性优先级、文档纪律），
前端设计器侧 `vue-demo/.cursor/rules/logic-orchestration-designer.mdc`（图态/执行态分离、校验对齐、沙箱两层模型）。

### 1.1 是什么

**代码逻辑编排**：把代码里的 `if / 顺序 / 并行 / 循环 / 调内部服务` 从代码中抽出来，可视化拼装成一条**可发布为系统 API** 的逻辑。

产物是 `POST /api/logic/{flowKey}`，响应按调用方角色裁剪字段。

主方案位于 `Meta.Dow.SaaS`，数据在 `MetaDowSaaSDb`（MongoDB）。运行相关代码集中在
`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/`。

### 1.2 不是哪几类东西（这四类不要混为一谈）

| 类别 | 代表 | 关注点 | 本仓是否有 |
|------|------|--------|-----------|
| A. 审批流 / BPM | Flowable、钉钉审批 | 人工待办、会签、Bookmark | 有，但**是另一套引擎**：见 `./oa-contract.md` |
| B. 自动化 / iPaaS | Zapier、n8n | 触发器 + 海量 SaaS 连接器 | ❌ 刻意不做 |
| C. 通用低代码页面搭建 | 宜搭表单页 | 页面/组件/数据绑定 | 有表单库，见 `./data-collection-contract.md` |
| **D. 代码逻辑编排（本文）** | LiteFlow、宜搭「逻辑编排」、微搭「逻辑流」 | **if / 顺序 / 并行 / 循环 / 调内部服务** | ✅ 本文主题 |

**边界一句话**：编排引擎只负责「业务怎么跑」；**何时启动**由触发层负责（§7.3）。

### 1.3 引擎五条设计原则（原文保留，作为取舍依据）

1. **契约优先**：一个逻辑应当有入参/出参契约。⚠️ 代码**未强制**（§10）。
2. **数据显式流**：节点之间靠出参/入参映射，少魔法全局变量（`vars` 仅兼容旧 DSL）。
3. **条件可组合**：分支靠 Condition 节点，而不是一整段不可维护的脚本（Code 节点除外，且受沙箱约束）。
4. **最小可见**：响应默认只含调用方有权字段。🐞 子图与无契约路径**违反**此原则（§14）。
5. **安全默认**：Sql/Code 默认受限；写库需显式授权。

### 1.4 目标架构中的位置

```
Vue Vben（web-antd，前端在 D:\Project\vue-demo，不在本仓）
        │ YARP Gateway：/api/orchestration/* 与 /api/logic/* → SaaS
        ▼
Meta.Dow.SaaS ★
  · 定义 / 版本 / 发布 → MongoDB
  · DSL 执行器（本主题）
  · 系统 API：POST /api/logic/{flowKey}
        │
        ├── Identity：角色列表（出参可见性配置）、运行时 JWT 角色
        ├── 外部 HTTP / 内部 API（Http 节点）
        ├── 外部 SQL / Redis / Mongo（Code 节点 db.*，白名单 DataSource）
        └── RabbitMQ（RabbitMqPublish + 触发层）
```

---

## 2. 现状能力总表（唯一权威表）

> 本表是**唯一**的能力状态来源。旧版散落在 §0/§11.3/§14/§15 的四张互相矛盾的状态表已废弃。

| 能力 | 状态 | 证据 | 备注 |
|---|---|---|---|
| 入参契约 InputSchema（name/type/required/default/source/systemKey/systemExpr/rules/properties/items） | ⚠️ | `FlowDslModels.cs:21-50` | `source` 仅 `input`/`system` 被消费（`FlowExecutor.cs:388-403`）；`expr` ❌ 未实现；`type` 仅在规则层校验，`datetime` 不校验 |
| 系统参数 `sys.*` + 日期表达式 | ✅ | `LogicApiAppService.cs:106-148`、`SystemContextBuilder.cs`、`DateExpressionEvaluator.cs` | 12 个固定键 + `sys.Now ± 3d`、`\| monthEnd` 等后缀；有 `/system-parameters` 与 `/system-parameters/preview-date` |
| 入参声明式校验 `rules[]` | ✅ | `InputRuleValidator.cs` | required/min/max/minLength/maxLength/minItems/maxItems/pattern/enum/email/guid/url/expr/assert（另有未文档化别名 `js`/`when`） |
| 条件节点：多条件 + 组合式 + 多出边 | ✅ | `FlowExecutor.cs` `EvaluateConditionItem` / `ResolveNextNode` / `SupportedConditionOps` | 算子已补齐至 23 个；`strategy` 取值白名单已在发布期校验 ✅ |
| 执行节点 I/O（inputs / outputs / resultRoot / map / to） | ✅ | `FlowExecutor.cs` `ApplyExecutableOutputs`、`FlowContextResolver.cs` `ApplyNodeOutputs` | 契约与实现一致 |
| Http 节点 | ✅ | `FlowExecutor.cs` `ExecuteHttpAsync` / `QueueHttpCallAsync` | 同步/异步见 §11.4；出站 SSRF 防护见 `HttpEgressGuard` ✅ |
| Code 节点（Jint 沙箱） | ✅ | `CodeSandboxExecutor.cs` | 本引擎最扎实的部分，见 §8 |
| **独立 Sql 节点** | ❌ | `SupportedNodeKinds` 无 `sql` | SQL 只能经 Code 节点 `db.*` 或资源 CRUD 节点；**发布期已拦** ✅ |
| Assign / `setvariable` | ✅ | `FlowExecutor.cs` `DispatchNodeAsync` | |
| Log（`{{path}}` 模板） | ✅ | `FlowExecutor.cs` `ResolveTemplateMessage` | `[FlowLog]` 打日志 |
| Mask / Throw / `assert` | ✅ | `FlowExecutor.cs` dispatch、`MaskValueHelper.cs` | phone/idCard/bankCard 预设的 keep 参数被硬编码 🐞 |
| RabbitMqPublish（fanout 广播） | ✅ | `FlowExecutor.cs` `BuildRabbitPayload` | `payloadMode` 支持 `object`（默认）/ `raw`，`payload.item` 字段投影已实现；`array` **已在发布期拒绝** ✅ |
| SubFlow（逻辑组件调用） | ✅ | `FlowExecutor.cs` `ExecuteSubFlowAsync` | 深度上限 5、调用栈环检测 |
| **ForEach / Loop** | ✅ | `FlowExecutor.cs` `ExecuteForEachAsync` | 并发窗口、`maxIterations`、`itemErrorMode` |
| **Parallel / WHEN** | ✅ | `FlowExecutor.cs` `ExecuteParallelAsync` | `joinMode=all/any/allSettled`、`branchTimeoutMs` |
| **Switch（多路枚举分支）** | ❌ | 无 dispatch 分支 | **发布期已被 `SupportedNodeKinds` 拒绝** ✅（不再出现「发布成功、调用即 500」） |
| Wait | ✅ | `FlowExecutor.cs` `ExecuteWaitAsync` / `WaitForJobAsync` | 异步出参的唯一消费入口；字段与失败语义见 §6.4 |
| `txMode=sameDataSource`（同库统一事务） | ❌ | DSL 无该字段 | 跨节点多次 `db.execute` 逐句自动提交；配 `txMode` 会被当未知字段拒 🐞 |
| 资源 CRUD 节点（9 个别名） | ✅ | `FlowExecutor.cs` dispatch、`ResourceCrudExecutor.cs` | 文档旧版未收录 |
| 出参角色可见性（visibleTo） | ✅ | `OutputVisibilityFilter`、`FinalOutputAssembler`、`FlowDefinitionAppService.ValidateOutputContracts` | `mode` 白名单 + `roleIds` 拒绝已覆盖**根级/End/嵌套（map.item 可无限层级、group.header、group.children）**；未知 mode 的运行期兜底仍是 `return true`，但发布期已先拒 🐞 |
| 出参整形 map / group / aggregate / promote / wrap | ✅ | `FinalOutputAssembler`、`FlowDefinitionAppService.ValidateOutputContract` | 见 §9；`aggregate` 与 `map` 同配、`wrap` 非 `array` 均**已在发布期拒绝**（根级与 End 均覆盖）✅ |
| 发布为系统 API `POST /api/logic/{flowKey}` | ✅ | `LogicApiController` | |
| 定义版本与版本历史 | ✅ | `FlowDefinitionController` | |
| 发布校验（结构） | ✅ | `FlowExecutor.ValidateDsl`、`FlowDefinitionAppService.ValidateOutputContracts`、`DslSchemaValidator.ValidateJson` / `ValidateSemantics` | 含节点类型白名单、出边基数、可达性、输出契约（含 End 与嵌套）、未知字段、条件算子与 `strategy` 白名单、`payloadMode` 取值白名单；仍**无 Warning 通道、无环检测** |
| 实例与节点执行日志 | ⚠️ | `LogicApiAppService`、`FlowInstance` | 子图内节点明细丢弃 |
| 试运行 DryRun | ⚠️ | `FlowInstanceAppService`、`FlowExecutor` | Http 节点**直接跳过**（非「异步改同步」）；无 `previewAsRole` |
| 发布缓存 | ⚠️ | `PublishedFlowCache`、`FlowDefinitionInvalidationBroadcaster` | 本地快照 + 共享失效代数比对；**最终一致**，非强一致推送 |
| 触发层（Rabbit / Kafka / MQTT / Cron） | ✅ | `RabbitMessageTriggerHostedService` 等、`TriggerControllers` | 与画布解耦；MQTT 多副本重复执行 🐞 |
| 单条逻辑独立调用权限码 | ❌ | `LogicApiAppService` | 只有 `Orchestration.Instances.Run` |
| 流程停用 / 启用接口 | ✅ | `FlowDefinitionAppService.DisableAsync` / `EnableAsync` | 此前实体有方法但**全仓无调用方**，流程无法通过 API 停用 |
| 异步作业消费 | ⚠️ | `FlowAsyncJobWorker`、`FlowAsyncJobDispatcher` | 单副本正确；**多副本无claim/乐观锁，会重复消费同一条 `Queued` 作业** 🐞（见 §14.3 末两条） |

---

## 3. DSL 契约

DSL 反序列化：**camelCase + 大小写不敏感**（`FlowExecutor.cs:40-44`）。

### 3.1 文档根 `FlowDslDocument`

| 字段 | 类型 | 默认 | 消费点 |
|---|---|---|---|
| `version` | string? | null | **不校验**（常量 `OrchestrationConsts.DslVersion = "1.1"`） |
| `key` | string? | null | 仅用于 SubFlow 环检测（`FlowExecutor.cs:159`） |
| `inputs[]` | `FlowInputParameterDsl[]` | `[]` | §3.2 |
| `outputs[]` | `FlowOutputParameterDsl[]` | `[]` | End 无 `finalOutputs` 时的回退契约（`:487-495`） |
| `nodes[]` | `FlowDslNode[]` | `[]` | §3.3 |
| `edges[]` | `FlowDslEdge[]` | `[]` | §3.4 |

### 3.2 入参 `FlowInputParameterDsl`

| 字段 | 类型 | 默认 | 状态 |
|---|---|---|---|
| `name` | string | **必填** | ✅ 发布校验 `:2437-2440` |
| `displayName` | string? | null | ❌ 落库不消费 |
| `type` | string | `"string"` | ⚠️ 只有 `number/boolean/object/array/guid` 在规则层校验（`InputRuleValidator.cs:161-201`），`datetime` 不校验 |
| `required` | bool | false | ✅ `:405-410` |
| `default` | any | null | ✅ `:392-395` |
| `source` | `input`\|`system`\|`expr` | `input` | ⚠️ `expr` ❌ 未实现 |
| `systemKey` | string? | null | ✅ |
| `systemExpr` | string? | null | ✅ 发布校验 `LooksLikeDateExpression` `:2449-2453` |
| `description` | string? | null | ❌ 不消费 |
| `rules[]` | `FlowInputRuleDsl[]` | null | ✅ `InputRuleValidator` |
| `properties[]` / `items` | 嵌套 | null | ✅ |

**调用方不可覆盖 `system` 入参**（`FlowExecutor.cs:388-391`）。⚠️ body 中多余的键被**静默丢弃**（`:420` 附近），无告警。

### 3.3 节点 `FlowDslNode` 字段总表

| 字段 | 类型 | 默认 | 状态 |
|---|---|---|---|
| `id` | string | 必填 | ✅ |
| `type` | string | 必填 | ⚠️ **无类型白名单**（§10） |
| `ref` | string? | 回退 `id` | ✅ 用于 `results.{ref}` 与短名 |
| `kind` | `Http`\|`Code`\|`Sql` | `Http` | ⚠️ `Sql` 无 dispatch |
| `async` | bool | false | ⚠️ **仅 Http 读取**（`:723`）；其它节点忽略且不报错 🐞 |
| `timeoutMs` | int? | `30000` | ✅ 合法区间 50..600000，`0`=不限时；**默认值未在旧文档声明** |
| `retryOnWrite` | bool | false | ⚠️ 仅资源写节点；要求同时提供幂等键，由 `ValidateDsl` 强制（`:2790-2806`） |
| `resultRoot` | string? | Http/Code 无；SubFlow 默认 `"data"` | ✅ 仅作用于**节点出参投影**，不参与最终出参（§5.2） |
| `inputs[]` / `outputs[]` | `FlowNodeBindingDsl[]` | null | ✅ §5 |
| `finalOutputs[]` | `FlowOutputParameterDsl[]` | null | ✅ 仅 End 节点，优先于根级 `outputs` |
| `headers[]` | `FlowNodeBindingDsl[]` | null | ✅ Http |
| `maskStrategy` / `itemField` / `maskRules[]` | | | ✅ Mask |
| `bodyMode` | `auto`\|`query`\|`json`\|`form`\|`raw` | auto | ✅ Http |
| `failWhen` / `failMessage` / `failCode` | | | ✅ 兼容写法 |
| `failItems[]` / `failCombine` | | | ✅ 异常条件表（与 Condition 同模型） |
| `entry` | `FlowConditionBlockDsl` | null | ⚠️ **`items` 为空时恒真**，只配 `combine` 无效 🐞 |
| `items[]` / `strategy` | | `firstMatch` | ⚠️ 仅 Condition；`strategy` 不校验 🐞 |
| 兼容旧字段 | `expression`/`message`/`name`/`variable`/`value`/`method`/`url`/`body`/`responseVariable`/`script` | | ⚠️ `responseVariable` **直写 `vars`，绕过 outputs 与可见性** 🐞 |
| `dataSourceId` / `dataSource` | string? | null | ✅ Code 用；`ResolveDataSourceId()` 优先前者 |
| `sql` | string? | null | ❌ 独立 Sql 节点不存在，该字段悬空 |
| Rabbit 组 | `exchange`/`exchangeType`/`routingKey`/`persistent`/`onError`/`payload`/`payloadMode`/`payloadFrom` | | ⚠️ `payloadMode=array` 无分支 🐞 |
| `subFlowKey` | string? | null | ✅ SubFlow |
| 控制流组 | `itemsPath`/`itemVar`/`indexVar`/`maxIterations`/`itemErrorMode`/`concurrency`/`loopBody`/`joinMode`/`branchTimeoutMs`/`branches[]`（含 `outputs`） | | ✅ §7 |

### 3.4 边 `FlowDslEdge`

| 字段 | 说明 | 状态 |
|---|---|---|
| `source` / `target` | 端点必须存在 | ✅ 发布校验 `:2420-2426` |
| `combine` | 组合式，如 `1 and (2 or 3)` | ✅ 发布校验引用编号 |
| `isDefault` | 显式默认分支 | ✅ 运行时兜底；⚠️ 发布期**不校验**多条默认边 |
| `items[]` | 边自带条件表（与节点共享 `items` 二选一） | ✅ |
| `when` | 旧模型 `true`/`false` | ✅ 兼容保留 |

### 3.5 出参 `FlowOutputParameterDsl`

见 §9.1 完整字段表。

---

## 4. 节点目录

### 4.1 分发表（唯一分发点 `DispatchNodeAsync`，`FlowExecutor.cs:640-793`）

`type` 先 `ToLowerInvariant()`；当 `type=logic` 时折算为其 `kind`（`:595-599`）。

| `type`（含别名） | 行号 | 类别 |
|---|---|---|
| `start` / `end` | 642-645 | 结构 |
| `log` | 647-666 | 动作 |
| `assign` / `setvariable` | 668-674 | 动作 |
| `mask` | 676-681 | 动作 |
| `throw` / `assert` | 683-688 | 动作 |
| `condition` | 690-707（选边 `497-554`） | 结构（多出边） |
| `http` / `httpcall` | 709-732 | 动作 |
| `code` | 734-739 | 动作 |
| `rabbitmqpublish` / `rabbitpublish` / `broadcast` | 741-748 | 动作 |
| `subflow` / `logiccomponent` / `component` | 750-757 | 控制流 |
| `foreach` / `loop` | 759-765 | 控制流 |
| `parallel` / `when` | 767-773 | 控制流 |
| `resourcequery` / `resourceget` / `resourcecreate` / `resourceupdate` / `resourcedelete` / `resourcecrud` / `resourcebatchcreate` / `resourcebatchupdate` / `resourcebatchdelete` / `resourcebatch` | 775-789 | 动作 |
| **`default`** | **791-792** | 抛 `Unsupported node type '{node.Type}'` |

**注意**：`type=Logic, kind=Sql` 会折算成 `sql` → 落到 `default` 抛错。校验层不拦（§10、§14-2）。

### 4.2 发布期允许「多出边」的节点

仅 `Condition` 与 `Switch`（`FlowExecutor.cs:2599-2603`）。`End` 不允许出边；其它节点出边 >1 直接发布报错并提示改用 Condition/Parallel（`:2636-2641`）。

⚠️ 但 `Switch` **没有 dispatch 分支** ⇒ 该校验豁免是错的（§14-2）。

---

## 5. 节点 I/O 与结果信封

### 5.1 入参映射 `inputs[]`

| 字段 | 状态 |
|---|---|
| `name` | ✅ |
| `type` | 🐞 **不做类型强制**（`FlowContextResolver.cs:358-378`） |
| `required` | ✅ 缺失即抛（`:369-372`）——但**发布期不校验**（§10） |
| `from` | ✅ 支持 `input.` / `sys.` / `vars.` / `results.` / 短名 / `{"literal":…}` / 日期表达式；🐞 **不支持 `{{}}` 模板** |
| `to` | ✅ 默认 `results.{ref}.{name}`；`vars.` 前缀写全局变量 |
| `transform` | 🐞 **声明了但全仓零消费点** |
| `map.item[]` | ✅ 与 End 同模型，且会按源形态推断 array/object（`:579-581`） |

### 5.2 结果信封与 `resultRoot`

每个执行节点产出一份 **raw 信封**，再按 `outputs[]` 投影：

| kind | raw 形状 | 推荐 `resultRoot` |
|---|---|---|
| Http | `{ statusCode, status, body, response{status,body} }` | `body` / `body.data` |
| Code | `{ return: …, …顶层展开 }` | `return` |
| SubFlow | `{ success, subFlowKey, subFlowVersion, executeMs, data }` | `data`（默认） |
| Assign | 即 nodeInput 投影 | 空 |

`outputs[].from` **优先相对 `resultRoot`**，找不到再回退 raw 根；`statusCode`、`body.xxx` 等信封路径始终相对 raw（`FlowContextResolver.cs:549-602`）。

**重要**：`resultRoot` **只作用于节点出参投影**。最终出参（`End.finalOutputs`）的 `from` **不受任何 `resultRoot` 影响**，它只解析 `input.`/`sys.`/`vars.`/`results.`/`Pubs`/`FlatVarsLegacy`（`FinalOutputAssembler.cs:682-686`）。

### 5.3 出参投影与引用方式

- 无 `outputs` 时，raw 顶层字段**自动成为出参名**（`:1398-1412`）。
- 每个可执行节点有引用名 `ref`（缺省用 `id`）；结果同时写入：
  - `results.{ref}.{name}` 与 `results.{id}.{name}`（`FlowContextResolver.cs:428-432`）
  - **短名 `Pubs[name]`**（`:434-438`）——同名短名后写覆盖，冲突时请用 `ref.name`。
- 下游引用优先级（`ResolvePathNode`，`FlowContextResolver.cs:41-163`）：`input.` → `sys.` → `vars.` → `results.` → 路径糖 → `Results[首段]` → `Pubs[首段]` → `Pubs[精确]` → `Pubs[忽略大小写]` → `FlatVarsLegacy` → `Vars`。
  裸名 `level` 查的是 `Pubs`/`Vars`，**不是**某个节点的相对字段。

### 5.4 路径糖（列表聚合）

仅当 token 含 `.` 且不以 `input./sys./vars.` 开头时生效（`FlowContextResolver.cs:168-215`）：

| 写法 | 含义 |
|---|---|
| `x.count` | 末段为 `count` → 列表元素个数 |
| `x.sum.amount` | 倒数第二段 ∈ `count/first/last/sum/avg/min/max` → 对 `amount` 做该聚合 |
| `x.first.y` / `x.max.date` | 同上 |

基座不是数组时糖判定失败并继续常规解析；`min/max` 无匹配值返回 **null**。

❌ **旧文档没有记录这个能力**，见 §19。

### 5.5 `map.item` 语义

- 子字段：`name` / `type` / `from` / `visibleTo` / `sensitive` / `map`。
- **嵌套无层级上限**（`FinalOutputAssembler.cs:454-561` 递归）。`name` 为空的子项直接跳过。
- `from` 支持：`{"literal": …}`、`{"template": "ORD-{{id}}"}`、全局路径（`input./sys./vars./results.` 或 `Results`/`Pubs` 的键）、或**相对当前行**（`.`/`$`/空=整行；自动剥离 `item.`/`row.` 前缀）。
- `type=array` → 对源数组逐行投影；`type=object` → 把源当单行投影。
- 顶层出参与节点绑定的**语义不同**：顶层出参只有显式 `type: "object"` 才走单对象投影，**其余（包括默认 `"string"`）一律按数组处理**；对象源 + `map` 但没写 `type:object`/`wrap:array` 会**抛异常**（`AsArray` `:722-724`）。节点绑定则按源形态自动推断。

### 5.6 `wrap` / `promote` / `aggregate` / `group` 的优先级

`group` → `aggregate` → `map` → `wrap` → 透传（`FinalOutputAssembler.cs:235/240/247/275/282`）。

🐞 **`aggregate` 与 `map` 同配时 `aggregate` 立即 return，`map` 被完全跳过且无告警**。

---

## 6. 条件与分支

### 6.1 条件表与组合式

| 字段 | 说明 |
|---|---|
| `no` | 编号，从 1 起；同节点内唯一（发布校验 `:2460-2464`） |
| `left` | `input`/`sys`/`vars`/`results.*`/短名/日期表达式 |
| `op` | 见下表 |
| `right` | 字面量或同样来源 |

**实际支持的算子**（`FlowExecutor.cs` `EvaluateConditionItem`，白名单见 `SupportedConditionOps`，共 **23** 个）：

| 类别 | 取值 |
|---|---|
| 比较 | `eq`/`==`、`ne`/`!=`、`gt`/`>`、`gte`/`>=`、`lt`/`<`、`lte`/`<=` |
| 包含 | `contains`、`notContains`、`startswith`、`endswith` |
| 集合 | `in`、`notin`、`between` |
| 空值 | `isEmpty`、`isNotEmpty`、`isNull`、`isNotNull` |

算子名**大小写不敏感、首尾空白会被`Trim`**，故 `"IN"` 与 `" in "` 合法。

**语义细节（前端实现时必须对齐，否则会产出错误 DSL）**：

- `in` / `notin`：右值可以是**数组**，也可以是**逗号分隔字符串**（`MatchesIn`）。
- `between`：右值必须是**含两个元素的数组**，语义为**闭区间** `[min, max]`。
  ⚠️ 与资源/报表筛选器里的 `between` **同名不同层** —— 后者的右值是 `{min, max}`，由 `FilterSqlBuilder` 生成半开区间 SQL。两者不可混用。
  ⚠️ 闭区间与 `FilterSqlBuilder` 的半开区间不一致，是已知缺陷C20，未统一。
- `isNull` / `isNotEmpty` 等**不需要右值**；`isEmpty` / `isNotEmpty` 对空白字符串也判真。

算子白名单在**发布期**校验（`ValidateConditionOp`），`failItems` 同样受检。拼错不再拖到运行期才报。

组合式只允许编号与 `and` / `or` / `not` / 括号，发布期校验引用的编号存在（`ConditionCombineEvaluator.Validate`）。

### 6.2 出边策略

- `strategy` **只接受 `firstMatch` / `allMatch`**，取值在**发布期**校验（`ValidateDsl`）。拼错会阻止发布，不再静默改变走向。
  - `firstMatch`（默认）：按**出边顺序**取第一条组合式为真的边，命中即停。
  - `allMatch`：所有条件都成立才走该边。
- 皆假 → 取 `isDefault` 边，其次 `when=false` 边；**都没有则报错**。
- ⚠️ 若节点**没有声明 `items`** 而只在边上写了 `combine`，会在**发布期**被拒（原先掉进旧 `expression`/`when` 分支，`combine` 被忽略并静默走第一条边）。

### 6.3 节点级进入条件 `entry`

- 为假 → 节点标记 `Skipped`，不执行、不写 outputs（`:574-584`）。
- ⚠️ 只配 `combine` 不配 `items` 时恒为真（`EvaluateConditionBlock` 直接 `return true`）—— 现已在**发布期**被拒。

### 6.4 Wait 节点

等待异步外呼作业落终态，是**异步出参唯一的消费入口**（`FlowExecutor.ExecuteWaitAsync`）。

**DSL 字段**

| 字段 | 类型 | 说明 |
|---|---|---|
| `jobId` | string | 要等的作业 Id。支持 `{{results.xxx.jobId}}` 模板；缺省时取名为 `jobId` 的入参 |
| `waitTimeoutMs` | int? | 最长等待毫秒数。缺省取节点级 `timeoutMs`，都缺省用 `30000` |
| `waitFailOnTimeout` | bool | 默认 `true` |

**发布期校验**（`ValidateDsl`）

- 必须有 `jobId` 或名为 `jobId` 的入参，否则拒绝。
- `waitTimeoutMs` 必须在 `1..600000`（`FlowExecutor.MaxWaitTimeoutMs`，10 分钟）之间。**0 或负数会立即假超时，极大值会把线程占死到进程回收。**
- `waitFailOnTimeout=false` 却**没有声明 `outputs`** ⇒ 拒绝：放行等待失败却不给下游判断依据，该节点没有意义。

**出参**：`jobId`、`status`、`timedOut`、`failed`、`statusCode`、`error`、`body`（`body` 会解析为 JSON，非 JSON 时降级为字符串）。

**失败语义**（`waitFailOnTimeout=true` 时）

| 情形 | 结果 |
|---|---|
| 作业 `Succeeded` | 节点 `Succeeded` |
| 作业 `Failed` / `Canceled` | 节点 `WaitJobFailed`，抛`UserFriendlyException`，流程失败 |
| 超过 `waitTimeoutMs` | 节点 `WaitTimedOut`，抛 `UserFriendlyException`，流程失败 |
| 作业 Id 不存在 | 抛 `UserFriendlyException` |

⚠️ **超时用专用异常 `FlowAsyncJobTimeoutException` 表达**，不用通用 `UserFriendlyException` —— 上层需区分「作业失败」与「等不到结果」，靠匹配文案不可维护。

设 `waitFailOnTimeout=false` 时上述三种情形**均放行**，并留下 `timedOut` / `failed` 出参供下游判断。

---

## 7. 控制流原语：ForEach / Parallel / SubFlow

三个原语共用**同一个调度原语** `RunSegmentAsync`（单游标分段执行，`FlowExecutor.cs:272-353`），避免出现第二套调度器。

### 7.1 ForEach / Loop（`:1597-1720`）

| 字段 | 默认 | 语义 |
|---|---|---|
| `itemsPath` | — | 待迭代数组来源；解析结果必须是 array，空数组 = 0 次迭代且**不算失败** |
| `itemVar` | `item` | 循环项变量名（`item` / `{{item.sku}}`） |
| `indexVar` | `index` | 下标（从 0 起） |
| `maxIterations` | `1000` | 硬上限 `10000`（`OrchestrationConsts.cs:31-34`） |
| `itemErrorMode` | `failFast` | `failFast` 任一项失败即终止；`continueOnError` 跳过并汇总 `failed[]` |
| `concurrency` | `1` | >1 时按**窗口并发**（`Task.WhenAll`），上限 16；要求循环体幂等 |
| `loopBody` / `subFlowKey` | — | 二选一，`loopBody` 优先 |

产出契约 `{ items[], count, succeeded, failed[] }`，其中单项产出 = 循环体 End 的 `finalOutputs`。

循环体可以是一段脚本：Start → Code → End。Code 把 `return` 写在原始结果上；End 用 `finalOutputs` 的 `from: return` 且 `promote: true` 时，返回的对象成为 `items[i]`。标量 `promote` 在运行时拒绝。种子 `cf-line-check` 即此形态。没有 `break` / `continue`；过滤一行靠脚本返回 `keep: false`。

🐞 `failFast` 在**整窗跑完后**才抛，不取消同窗其它迭代。

### 7.2 Parallel / WHEN（`:1726-1900`）

| 字段 | 默认 | 语义 |
|---|---|---|
| `branches[]` | 至少 2 条，至多 16 条，`ref` 唯一且非空 | 每条含 `ref`（结果键）、`subFlowKey` 或 `body`、`inputs`、`outputs`、`timeoutMs`、`optional` |
| `joinMode` | `all` | `all` 全部成功才继续（非 optional 分支失败即抛）；`any` 首个成功即用；`allSettled` 等全部结束 |
| `branchTimeoutMs` | 继承 `timeoutMs` | 单分支超时。实际取值 `branch.TimeoutMs ?? node.BranchTimeoutMs ?? node.TimeoutMs ?? 30s` |
| `branch.outputs` | 空 | 空则保留被调流程的完整结果。非空时 `ProjectBranchOutputs` 用 `FinalOutputAssembler.ResolveRelative` 投影；无点号的短名相对被调结果，`input.` / `sys.` / `results.` / `vars.` 相对父上下文 |

分支并发为**异步 I/O 并发**（一次性启动全部 Task），非线程并行。汇合结果写 `results.{parallelRef}.{branchRef}`。`body` 与 `subFlowKey` 同时存在时执行 `body`。设计器在选中已发布流程时清掉 `body`。种子 `cf-parallel-demo` 的三条分支只写 `subFlowKey` 与入参出参，分别调用 `cf-branch-basic`、`cf-branch-risk`、`cf-branch-audit`。被调流程多返回的字段（如 basic 的 `note`）不在调用方 `outputs` 里时，投影后为空。

🐞 `joinMode=any` **不取消其余分支**（`FlowDslModels.cs:378` 注释称会取消），副作用在流程继续后仍在发生。

### 7.3 SubFlow / 逻辑组件（`:1478-1579`）

- 逻辑组件 = 勾选 `isReusable` 且**已发布**的流程定义（`code` = `flowKey`）。
- 同步调用：本节点 `inputs` → 子流程请求体；子流程 End 出参装入信封 `data`；`resultRoot` 默认 `data`。
- **嵌套上限 5 层**（`MaxSubFlowDepth = 5`），**禁止循环**（调用栈含同一 `flowKey` 即抛）。
- `onError`：`fail`（默认）中断父流程；`ignore` 继续并写出 `success=false`。
- **不单独落子实例**：子流程节点明细不写入父 `FlowInstance.Nodes`（仅本 SubFlow 节点一条记录）。
- 子图透传父调用的 `filterOutputsByRole` 与 `currentUser`（`ExecuteSubFlowAsync` / `ExecuteForEachAsync` / `ExecuteParallelAsync` / `RunControlBodyAsync`）。入口本身传 `false` 或 `null` 时，子图同样不裁剪。
- 引用索引 `FlowUsage`：`GET /api/orchestration/definitions/reusable-lookup`、`/usages/by-callee`、`/{id}/usages`。

### 7.4 触发层（不扩展画布）

| 概念 | 类比 | 说明 |
|---|---|---|
| MessageSource | DataSource | 登记 Rabbit / Kafka / MQTT 连接；Rabbit 连接串留空 = 平台 `rabbitmq` |
| FlowTrigger | — | 队列 + 绑定 → `flowKey`；消息 JSON = 流程入参 |
| FlowSchedule | — | Cron + 固定 VariablesJson → `flowKey`。`maxAttempts` / `retryDelaysSeconds` 在计划上；每次触发落 `FlowScheduleFire`（`FlowScheduleRunner`） |

已落地 Rabbit / Kafka / MQTT 消费 HostedService（约 20s 热更新监听）+ Cronos 进程内轮询。
🐞 `FlowScheduleHostedService` 用了 `IAbpDistributedLock`，但 Rabbit/Kafka/MQTT 三个触发器**都没有**；MQTT 的 client id 含随机 Guid ⇒ **多副本必然重复触发**。

---

## 8. Code 沙箱与数据访问

### 8.1 隔离强度（本项目最扎实的部分）

| 项 | 值 | 证据 |
|---|---|---|
| 引擎 | Jint 4.4.1 | `Directory.Packages.props:20` |
| 脚本超时 | **8s（硬顶）** | `OrchestrationConsts.cs:20`、`CodeSandboxExecutor.cs:74-86` |
| 最大语句数 | 20,000 | 同上 |
| 递归深度 | 64 | 同上 |
| 屏蔽全局 | `fetch` / `XMLHttpRequest` / `eval` / `Function` = `undefined` | `CodeSandboxExecutor.cs:83-86` |
| CLR 访问 | **未开 `AllowClr`** ⇒ 脚本碰不到文件/网络/反射 | 同上 |
| 可见变量 | `nodeInput` / `input` / `sys` / `results`（可选）/ `db`（配了数据源时） | `:88-108` |
| 取消 | 传 `CancellationToken` | `:74-86` |

⚠️ `timeoutMs` 可配到 600,000ms，但沙箱硬顶 8s ⇒ **配 >8s 完全无效且无提示**。

### 8.2 `db` 宿主 API

- `db.query`（读）/ `db.execute`（单条写）/ `db.batch`（同一 SQL + 参数数组），另有 PascalCase 别名 `Query/Execute/Batch`（`:235-237`）。
- 宿主构造函数还注入了 **Redis / Mongo** 执行器（`:196-216`）⇒ 实际 API 面**比旧文档声称的"仅三种"更宽**。
- 参数化：`@name` 对应 `inputs[].name`；值全部走参数绑定，不做字符串拼接。
  🐞 命名参数正则**误匹配字符串字面量与 `::` 转型** ⇒ `email='a@b.com'`、`x::int` 会抛 "Missing SQL parameter"。
- 上线：`db.batch` 单次上限 **500 行**；`db.query` 上限 **1000 行**；命令超时 **15s**（`OrchestrationConsts.cs:22-24`）。
- 🐞 `db.*` 是同步 `GetAwaiter().GetResult()`（`:223-232`），**不受 Jint 超时覆盖**，只受节点 token 与 15s 命令超时约束。

### 8.3 写权限与静态检查

| 机制 | 实现 | 状态 |
|---|---|---|
| 写权限 | `execute`/`batch` 调 `EnsureSqlWrite`，要求 `Orchestration.Sql.Write` | 🐞 `select` **和 `with`** 都视为读 ⇒ `WITH x AS (…) DELETE FROM t` **绕过写权限**；`db.query` 完全不检查 |
| 数据源访问模式 | `ParameterizedSqlExecutor` 校验 DataSource 的 `accessMode`/`allowedOps` | ✅ 兜底 |
| 脚本静态检查 | `CodeScriptStaticAnalyzer`：`db.` 三点式调用 + 第一参数须为无插值字面量 | 🐞 **正则词法检查，不是 AST**（旧文档称"脚本 AST"）；`db["execute"](…)` 不被匹配；`const sql="…"; db.execute(sql)` 反被拒绝 |
| 检查时机 | `CodeSandboxExecutor.cs:70`（**运行期**） | 🐞 旧文档要求发布期 Error |

---

## 9. 最终输出与角色可见性

### 9.1 `FlowOutputParameterDsl` 字段全表

| 字段 | 默认 | 消费点 / 状态 |
|---|---|---|
| `name` | **无默认（必填）** | 写入 `data[name]`，原样使用，不经命名策略 |
| `type` | `"string"` | 影响 `promote` 空值形态、object/array 判定 |
| `from` | null | 为空则回退 `name`；解析见 §9.2 |
| `visibleTo` | null（视为 `all`） | §9.4 |
| `sensitive` | false | 🐞 **仅打一条日志**，不脱敏不裁剪（§9.6） |
| `description` | null | 🐞 无任何消费点 |
| `map.item[]` | null | §9.3 |
| `aggregate{op,path}` | null | 支持 `count/sum/avg/min/max/first/last` |
| `wrap` | null | 🐞 **只识别 `"array"`**，其它取值无效果 |
| `promote` | false | `data` 直接等于该字段值；契约只能有这一项，否则抛异常 |
| `group{by,header[],children{},promote}` | null | §9.3 |

### 9.2 取值解析

`path = from ?? name` → `ResolvePathNode`。找不到时返回 **null**（仅模板插值里变空串）。路径为空/空白时返回**根本身**。键查找"精确优先 + 忽略大小写回退"；数组支持 `[n]` 与裸数字。

### 9.3 map / group / aggregate / promote

**`map.item`**：嵌套无上限；`from` 支持 `{"literal"}`、`{"template"}`、全局路径、相对行路径；`type=array|object` 决定按行还是按对象。

**`group`**（`by` 非 null 才触发）：
- `by` 接受字符串或字符串数组；**键数为 0 抛异常**。
- 分组键缺失/null → 归入空串桶；桶字典**大小写敏感**（`StringComparer.Ordinal`）。
- `header[]`：`aggregate` 优先，否则 `take`（`last` = 最后一行，其它一律 first）+ `from ?? name`；未配置 `header` 时把 `by` 各键从每组第一行取值。
- `children{}`：逐行按 `children.map.item` 投影，无 map 则原样克隆；结果恒为数组。
- `group.promote`：空数组 → 静默不写任何字段；>1 组 → 抛异常；恰好 1 组 → 把其字段 clone 到 `data` 根级；**值是 null/标量时静默丢弃**。

**`aggregate`**：`sum/avg/min/max` 必须给 `path`，否则抛异常；**没有任何数值时 `sum/avg` 返回 0，`min/max` 返回 null**；未知 op 抛异常。

**`promote`（出参级）**：只能有一项；不可见时 `type=array` 给 `[]`、否则给 `{}`；**结果是标量则抛异常**。约束只在**运行时**校验，定义期无校验。

### 9.4 `visibleTo` 判定矩阵

`bypass = isDryRun || !filterOutputsByRole || currentUser == null`（`FlowExecutor.cs:455`）。
`filterOutputsByRole: true` 来自系统 API 与资源/报表执行入口。SubFlow、ForEach、Parallel 把父调用传入的 `filterOutputsByRole` 与 `currentUser` 继续往下传，不再在控制体里写死 `false` / `null`。

| `mode` | 判定 | 行号 |
|---|---|---|
| null / 空 / `all` | **true** | `OutputVisibilityFilter.cs:63-67` |
| `none` | false | `:69-72` |
| `roles` | `roleNames` 与 `currentUser.Roles` 有交集 → true | `:74-80` |
| `roles`（`roleIds` 回退） | 🐞 `roleIds` 拿去和**角色名**集合比较 ⇒ 只填 `roleIds` 的字段**恒不可见** | `:81-84` |
| `permissions` | `permissions` 集合有交集（集合 = `CollectPermissionNames` → `IPermissionChecker.IsGrantedAsync`） | `:89-93`、`FlowExecutor.cs:457-469` |
| **其它未知值** | 🐞 **true（兜底放行）** | `:95` |

🐞 **权限收集缺口**：`CollectPermissionNames`（`FinalOutputAssembler.cs:870-898`）只收集 `output.visibleTo.permissions`、`output.map`（递归）、`group.children.map` —— **从不遍历 `group.header`** ⇒ `header[].visibleTo.permissions` 永不生效，非 bypass 时该表头**必然被丢弃**。

判定点共 5 处：出参、promote 出参、`group.header`、`group.children`、`map.item`。

### 9.5 无契约回退

当 `End.finalOutputs` 与根级 `outputs` **都为空**时（`FlowExecutor.cs:446-453`）：

- `data` = **整个 `ctx.Vars`**（仅 vars，不含 input/sys/results）；
- `meta.visibleFields` = vars 的全部键名；
- `omittedFieldCount` 恒为 0；
- 🐞 **不做任何可见性过滤**，也不走 map/aggregate/promote/group ⇒ 内部变量整包外泄，而计数为 0 反而掩盖了它。

### 9.6 `sensitive` 的真实作用

声明处有三处（出参、`map.item`、`group.header`），全仓**唯一读取点是 `FlowExecutor.cs:476`**：只取**出参级**的 `sensitive` 字段名，非空时打一条 Information 日志。

⇒ **不脱敏、不裁剪、不影响 `data` 与 `visibleFields`**；`map.item`/`group.header` 上的 `sensitive` 连日志都不会出现。与 Mask 节点无关（Mask 走 `node.maskRules` → `MaskValueHelper`）。

---

## 10. 发布校验

### 10.1 已实现（`ValidateDsl` `FlowExecutor.cs:2388-2545` + `ValidateOutEdgeCardinality` `:2597-2644`）

| 规则 | 行号 |
|---|---|
| `nodes` 非空 | 2390-2393 |
| Start **恰好 1 个** | 2395-2398 |
| 至少 1 个 End | 2400-2403 |
| 节点 id 唯一 | 2405-2408 |
| 节点 `ref` 唯一 | 2410-2416 |
| 边的 source/target 必须存在 | 2420-2426 |
| End 无出边；非 `Condition`/`Switch` 出边 >1 报错；可达节点必须有出边 | 2597-2644 |
| 入参 `name` 必填；`source=system` 须有 `systemKey`/`systemExpr`；`systemExpr` 必须是合法日期表达式 | 2435-2453 |
| Condition 编号唯一、必须有出边、`combine` 引用编号必须存在 | 2456-2477 |
| `entry.combine` 引用编号必须存在（仅当 `items` 非空） | 2481-2484 |
| SubFlow 必须有 `subFlowKey` | 2487-2494 |
| ResourceQuery 必须有 `resourceCode` 或 `dataSourceCode+tableName` | 2496-2507 |
| `retryOnWrite` 只允许资源写节点，且必须提供幂等键 | 2790-2806 |

### 10.2 缺失或错位

| 旧文档声称的规则 | 实际 |
|---|---|
| 存在环 → Error | ❌ **发布不查环**；只在运行期用 `visited`（`:313-317`） |
| 无 InputSchema 不可发布 | ❌ 不校验；`inputs` 为空时走 legacy 平铺路径（`:371-381`） |
| 必填入参缺 `type`/`name` | ⚠️ 只查 `name` |
| 执行节点 required 映射缺失 → Error | ❌ 只在运行期抛（`FlowContextResolver.cs:369-372`） |
| Code 未过沙箱静态检查 → Error | ❌ 运行期才查（`CodeSandboxExecutor.cs:70`） |
| Sql 未选白名单 / 疑似拼接 → Error | ❌ 无独立 Sql 节点；未登记/停用的 `dataSourceId` 也**不校验** |
| 同步节点读异步出参且无 Wait → Error | ❌无此校验（Wait 已实现，但未做「跨异步边界」的静态检查） |
| `outputs[].from` 指向未声明路径 | ❌ 不校验 |
| **Warning 级**：无 `visibleTo` / 不可达节点 /敏感未标 `sensitive` | ❌ **完全没有 Warning 通道**，`ValidateDsl` 只 throw |
| 未知 / 未实现的 DSL 字段 | ✅ `DslSchemaValidator.ValidateJson` 在发布期拒绝（反射生成白名单，模型加字段自动跟随） |
| 已知字段的**取值**是否有消费点 | ✅ `DslSchemaValidator.ValidateSemantics`（如 `payloadMode=array` 拒绝） |
| 节点 `type`/`kind` 白名单 | ✅ `SupportedNodeKinds`，与 dispatch case 逐一对应 |
| `condition.strategy` 取值 | ✅ 发布期白名单 `firstMatch` / `allMatch` |
| 条件算子（含 `failItems`） | ✅ 发布期白名单 `SupportedConditionOps` |
| Wait 的 `jobId` / `waitTimeoutMs` / `outputs` | ✅ 见 §6.4 |
| 出参 `aggregate` + `map` 冲突、`wrap` 取值 | ✅ 发布期拒绝，**根级 `outputs` 与 `End.finalOutputs` 均覆盖** |
| 嵌套 `visibleTo`（`map.item[]` / `group.header[]` / `group.children`） | ✅ 发布期递归校验（此前只查根级，嵌套拼错走运行期 `return true` 兜底） |
| `payloadMode` 取值 | ✅ 仅 `object`（省略即此值）/ `raw`；`array` 拒绝 |

### 10.3 「校验放行 → 运行期抛错」的组合（最危险）

以下组合**已在发布期消除**：

1. ~~`type=Logic, kind=Sql` → 发布通过 → 运行期抛 `Unsupported node type 'sql'`~~ ⇒ 现由节点类型白名单在发布期拒绝。
2. ~~`type=Switch` 多出边 → 校验显式豁免 → 无 dispatch → 运行期抛错~~ ⇒ 同上。
3. ~~任意拼错的 `type`（如 `Foo`）→ 运行期抛错~~ ⇒ 现发布期拒绝并给出「是否想写 X」提示。
4. ~~`templates`/`transform` 等被忽略的配置 → 静默不生效~~ ⇒ 现由 `DslSchemaValidator.ValidateJson` 在发布期拒绝（`transform`、`asyncJobId`、`previewAsRole`、`sql` 等「模型有但无消费点」的字段一律拒绝并给替代方案）。
5. ~~`payloadMode=array` → 静默退化为整包 inputs~~ ⇒ 现由 `ValidateSemantics` 按取值拒绝。
6. ~~只配 `End.finalOutputs` 的流程绕过 `aggregate`+`map` 冲突拦截~~ ⇒ 遍历范围已覆盖 End 节点。

**仍然存在的**（未在本轮整改范围）：

- 环检测：仍只在运行期用 `visited`，发布期不查环。
- 多副本下`FlowAsyncJobWorker` 会重复消费同一条 `Queued` 作业（无 claim/乐观锁）——但这属于运行期幂等性问题，不是校验放行。

---

## 11. 执行语义与运行保障

### 11.1 主循环（`RunSegmentAsync` `FlowExecutor.cs:272-353`）

单游标从 `startId` 沿边推进，直到命中 `stopIds`、命中 End、或出错。以下情况**一律失败关闭**：

| 情况 | 结果 |
|---|---|
| 节点无出边 | `Node '{id}' has no outgoing edge.` |
| 无匹配分支且无 default | `No matching branch from node '{id}'.` |
| 检测到环（重复经过非 End 节点） | `Cycle detected at node '{id}'.` |
| 超过 200 步 | `Exceeded maximum execution steps.` |

**这是本引擎最值得保留的性质**：配置错误不会静默变成「成功」。（对比：OA 审批引擎仍存在连线指向不存在节点时静默 `Complete("approved")` 的问题。）

### 11.2 节点超时

- 默认 `30000ms`；合法区间 `50..600000`；`0` = 不限时（`OrchestrationConsts`）。
- 超时包在**唯一分发行**上（`FlowExecutor.ExecuteNodeAsync`），新增节点不会漏包。
- ⚠️ **对 Code 节点只生效到 8s**：`timeoutMs ≤ 8000` 时按配置正常生效；`> 8000` 会被 Jint 沙箱的硬顶（`OrchestrationConsts.CodeScriptTimeoutSeconds = 8`）**静默截断**，配 600s 与配 8s 效果相同。这是"配了看起来生效、实际没生效"的典型残留 🐞。
- 🐞 对异步 Http 作业无效：作业已脱离节点生命周期，须用 Wait 的 `waitTimeoutMs`（见 §11.4）。

### 11.3 弹性与重试

| 层 | 行为 | 证据 |
|---|---|---|
| 节点级策略 | 读操作瞬态故障**自动重试 2 次**（指数退避 + 全抖动）；写操作**默认 0 次**，须 `retryOnWrite` + 幂等键 | `NodeResiliencePolicy.cs:59/93/128-134` |
| 不重试 | `UserFriendlyException`、`OperationCanceledException` | `:141-164` |
| 覆盖范围 | ⚠️ 只挂在资源 CRUD 与 Code 的 `db` 读上；**`db.execute`/`db.batch` 无策略** | `ResourceCrudExecutor.cs:154-159`、`CodeSandboxExecutor.cs:242+` vs `:251-264` |
| Http 全局客户端 | **POST/PUT/DELETE 明确禁用重试**（`DisableForUnsafeHttpMethods`）；安全方法最多重试 2 次（退避 500ms + 抖动）；单次尝试 10s、总预算 60s | `src/shared/Meta.Dow.ServiceDefaults/Microsoft/Extensions/Hosting/Extensions.cs:41-53` |
| 瞬态分类 | 🐞 PostgreSQL `SqlState` 被 `int.TryParse`：`40P01`(死锁)、`08xxx`(连接异常)、`57P01`(关停) 全部解析失败 → 0 → **不重试**；🐞 Oracle 取了 `Number` 但错误码表里**没有任何 Oracle 码** → 恒不重试 | `NodeResiliencePolicy.cs:173/175/182-190` |

⇒ 「节点级重试」的实际生效面比旧文档承诺的窄得多：**POST 类 Http 集成不重试**，`timeoutMs` > 60s 会被 HttpClient 总预算截断。

### 11.4 异步（`async`）

⚠️ **只有 Http/HttpCall 读取 `node.Async`**；`code`/`rabbitmqpublish`/`subflow`/`foreach`/`parallel`/`resource*`/`assign`/`log`/`mask` **静默忽略该字段**。

Http 异步的实际行为（`FlowExecutor.QueueHttpCallAsync`）：

- 校验入参并过出站闸后，**登记一个持久化作业**（`FlowAsyncJob`）并把 `jobId` 写入 `NodeExecutionRecord.AsyncJobId` 与节点出参，主链立即继续；
- 作业由 `FlowAsyncJobWorker`（`BackgroundService`，2s 轮询）交给 `FlowAsyncJobDispatcher` 执行——**不再是进程内 `Task.Run`**；
- **成功与失败都写回实体**：状态、响应码/响应体、失败原因、重试次数、终态时间全部落库；
- 连落库都失败时记 `LogError`，绝不静默；
- 宿主回收/重启后作业仍在库里（`Queued` 状态可被下一轮轮询捡起）；
- 出参含 `jobId`，配合 **Wait 节点**（§6.4）即可让异步出参真正参与下游编排。

**仍然存在的限制**：

- ⚠️ 异步分支**不调用 `CheckFailWhen`** ⇒ `failWhen`/`failItems` 在异步节点上永不被求值。
- ⚠️ 节点 `timeoutMs` 对**已登记的作业本身**无约束（作业是独立生命周期）；需要约束等待时长请用 Wait 节点的 `waitTimeoutMs`。

### 11.5 DryRun（试运行）

| 节点 | DryRun 行为 |
|---|---|
| Http | 🐞 **直接 `Skipped`**（`reason=dry-run`），不真实外呼（`:712-721`）⇒ 并非"异步改同步"，而是"不执行" |
| RabbitMqPublish | 回执 `published=false, dryRun=true`，不真实发送（`:2062-2100`）✅ |
| SubFlow | 递归执行子流程（Http 仍按 DryRun 规则跳过） |
| 出参过滤 | `filterOutputsByRole: false` + `isDryRun` ⇒ `bypass=true`，**恒返回完整 data** |

❌ `previewAsRole` 参数不存在（DTO 无该字段）⇒ 旧文档的"按角色预览"无接口支撑。

### 11.6 出站闸门（Http SSRF 防护）

`HttpEgressGuard.EnsureAllowed` 在**发起请求之前**判定（不是靠 `HttpClient` 事后补救——后者防不住首次直连，也防不住 DNS 重绑定）：

| 判定 | 行为 |
|---|---|
| 协议 | 仅 `http` / `https`；`mock://`（设计器预览伪协议）直接放行 |
| 主机白名单 | `Orchestration:Http:AllowedHosts` 命中则放行（平台自身回调内部编排服务需要） |
| `localhost` | 直接拒绝（字面量判定，不经 DNS） |
| 解析后地址 | `Dns.GetHostAddresses` 逐个判`IsForbiddenAddress`，任一命中即拒绝 |

`IsForbiddenAddress` 覆盖：回环、`0.0.0.0/8`、`10/8`、`172.16/12`、`192.168/16`、`169.254/16`（链路本地，含云元数据）、`100.64/10`（CGNAT）、`192.0.0/24`（IETF 保留）、`198.18/15`（基准测试）、`>= 224`（组播/保留）；IPv6 覆盖 `IPv4-mapped`（映射到内网 IPv4 的经典绕过手法）、`fe80::/10`、`fc00::/7`、`::` / `::1`。云元数据端点 `169.254.169.254`、`100.100.100.200`、`192.0.0.192` 显式列出。

**重定向**：命名客户端 `OrchestrationHttpCall` 默认 `AllowAutoRedirect = false`。允许 3xx 跟随会让「公网 302 跳内网元数据」绕过首跳判定。仅当 `Orchestration:Http:AllowRedirects=true` 时开启，并限制 `MaxAutomaticRedirections = 5`。

⚠️ **配置缺省即最严**：`HttpAllowedHosts` 默认 `[]`，即默认部署下**不允许任何内网出站**。若 Http 节点需要访问内部服务，必须在 SaaS Host 的 appsettings 里显式配置 `Orchestration:Http:AllowedHosts`（当前各 `appsettings*.json` **均无该节**）。

### 11.7 发布缓存与实例

- 本地快照：**进程内 `IMemoryCache`**（`PublishedFlowCache`），绝对 10 分钟 + 滑动 3 分钟，键 `orch:pub:{tenantId|host}:{flowKey}`。
- 跨副本失效：**共享单调递增的「失效代数」**（`FlowDefinitionInvalidationBroadcaster`，存`IDistributedCache`）。读取本地快照前先比对代数，不一致则丢弃重载。发布 / **停用** / **删除**三个入口都会自增代数并广播。
- 广播失败（Redis 不可用）**只降级不阻断**：读取退回 0（等价于「没人广播过」），主操作照常完成。
- ⚠️ **最终一致**：失效在副本**下一次读取时**生效，不是即时推送。若业务要求「停用后零窗口」，需在调用入口二次校验状态。
- 实例：执行器本身不建实例，由 `LogicApiAppService`（系统 API）与 `FlowInstanceAppService`（DryRun）各自建；SubFlow 子流程**不落任何实例**。
- 🐞 子图内节点执行明细被丢弃（只回传 `Ok/Output/Error`），`FlowInstance.Nodes` 看不到循环第几轮、哪一步失败。

---

## 12. API 契约

### 12.1 端点（以 controller 为准）

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | `/api/logic/{flowKey}` | **已发布逻辑系统 API**（`LogicApiController.cs:22-24`） |
| GET/POST | `/api/orchestration/definitions` | 列表 / 创建 |
| GET/PUT/DELETE | `/api/orchestration/definitions/{id}` | 详情 / 更新草稿 / 删除 |
| POST | `/api/orchestration/definitions/{id}/publish` | 发布 |
| POST | `/api/orchestration/definitions/{id}/disable` | **停用** 🆕（此前实体有 `Disable()` 但无 API 调用方） |
| POST | `/api/orchestration/definitions/{id}/enable` | **启用**（回到草稿态） 🆕 |
| GET | `/api/orchestration/definitions/{id}/versions` | 版本历史 |
| GET | `/api/orchestration/definitions/reusable-lookup` | SubFlow 选用列表 |
| GET | `/api/orchestration/definitions/usages/by-callee?flowKey=` | 谁在调用 |
| GET | `/api/orchestration/definitions/{id}/usages` | 该定义调用了谁 |
| GET | `/api/orchestration/instances` / `/{id}` | 实例列表 / 详情（含节点日志） |
| POST | `/api/orchestration/instances/**dry-run**` | 试运行 ⚠️ 旧文档写作 `instances:dry-run` |
| POST | `/api/orchestration/instances/{id}/cancel` | 取消 |
| GET | `/api/orchestration/system-parameters` | `sys.*` 目录 |
| POST | `/api/orchestration/system-parameters/preview-date` | 日期表达式试算 |
| GET/POST/PUT/DELETE | `/api/orchestration/data-sources…` | 数据源白名单（含 `/lookup`、`/providers`、`/test`、`/{id}/test`） |
| — | `/api/orchestration/roles` | ❌ **不存在**（旧文档列了） |

### 12.2 `POST /api/logic/{flowKey}` 响应

```json
{
  "success": true,
  "instanceId": "…",
  "data": { "approved": true, "message": "ok" },
  "error": null,
  "meta": {
    "visibleFields": ["approved", "message"],
    "omittedFieldCount": 1,
    "flowKey": "order.risk-check",
    "version": 3
  }
}
```

- `omittedFieldCount = max(0, ContractFieldCount - VisibleFields.Count)`（`LogicApiAppService.cs:83`）。
- ⚠️ `LogicRunMetaDto` **含** `lookupMs`/`executeMs`/`persistMs`/`totalMs`（`:98-101`），但**手写的 `LogicApiController` 不返回这四项**（`LogicApiController.cs:38-51`）⇒ 走该端点的调用方拿不到分段耗时。
- ⚠️ 入参校验失败抛 `UserFriendlyException`，controller 无 try/catch ⇒ 实际是 **200 + ABP 包装体**，不是旧文档承诺的 400。
- 系统参数目录（`LogicApiAppService.cs:106-135`）：`sys.userId`、`sys.userName`、`sys.userEmail`、`sys.tenantId`、`sys.tenantName`、`sys.culture`、`sys.timeZone`、`sys.correlationId`、`sys.Now`、`sys.Today`、`sys.MonthStart`、`sys.MonthEnd`；日期表达式示例 `sys.Now + 3d`、`sys.MonthStart + 1M | monthEnd`、`sys.Today - 1w | weekStart`。

网关：`/api/orchestration/{*any}` 与 `/api/logic/{*any}` → SaaS（`src/gateway/Meta.Dow.Gateway/appsettings.json:35-46`）。

---

## 13. 权限目录

定义在 `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Permissions/`（`OrchestrationPermissions.cs` + `SaaSPermissionDefinitionProvider.cs:21-131`），组名 `Orchestration`：

`Definitions`(`.Create/.Update/.Delete/.Publish`)、`Instances`(`.Run/.Cancel/.Delete`)、`DataSources`(`.Create/.Update/.Delete/.Ddl`)、`Tables`(`.Create/.Update/.Delete/.Ddl`)、`QueryObjects`(`.Create/.Update/.Delete`)、`Resources`(`.Create/.Update/.Delete/.Publish/.Execute`)、`Reports`(`.Create/.Update/.Delete/.Publish/.Execute`)、`Forms`(`.Create/.Update/.Delete/.Publish`)、`Workflows`(`.Create/.Update/.Delete/.Publish`)、`MessageSources`(`.Create/.Update/.Delete`)、`Triggers`(`.Create/.Update/.Delete`)、`Schedules`(`.Create/.Update/.Delete/.Run`)、`Sql.Write`。

要点：

1. **调用权与字段可见分离**：`/api/logic/{flowKey}` 只要求 `Orchestration.Instances.Run`（`LogicApiAppService.cs:35`）；字段可见靠 `visibleTo`。
2. 🐞 **没有单条逻辑的调用权限码**：旧文档的 `Orchestration.Logic.{flowKey}`（标"可选"）不存在 ⇒ 持 `Instances.Run` 者可调用**全部**已发布流程。
3. 字段级 `visibleTo.roles/permissions` **不做成 PermissionDefinition**，运行时过滤。
4. 权限挂在**应用服务**上（`[Authorize(...)]`），controller 本身不挂 —— 新加 controller 端点时必须走应用服务，否则无保护。

---

## 14. 行为边界与已知缺陷

> 按后果排序。每条给出机理与代码位置，便于修复与写回归测试。**本节只列当前代码仍存在的真实行为**。
> ⚠️ **编号与 review §4 一一对应，空缺编号即已修复**——已修项统一收进 §14.0（保留编号仅为对照，不再视为缺陷）。

### 14.0 已修复（基线 `91d75bd`，2026-10-14 P0 止血批次 + 覆盖范围补齐批次）

| 原编号 | 缺陷 | 修复方式 | 落点 |
|---|---|---|---|
| 1 | 无契约时外泄整个 `ctx.Vars`（S7） | 新增 `ValidateOutputContract`：根级 `outputs` 或**每个** End 的 `finalOutputs` 至少其一，否则发布被拒；运行期 `FinishOutputsAsync` 仍有兜底（`isDryRun` 试运行保留全量视图供设计期调试） | `FlowExecutor.cs` `ValidateOutputContract` / `FinishOutputsAsync` |
| 2 | 子图旁路 `visibleTo`（S6） | `filterOutputsByRole` / `currentUser` 由 `ExecuteNodeAsync` 经 `DispatchNodeAsync` 下沉到 SubFlow / ForEach / Parallel 控制体，替换原先硬编码的 `false` / `null` | `FlowExecutor.cs`（`ExecuteSubFlowAsync` / `ExecuteForEachAsync` / `ExecuteParallelAsync` / `RunControlBodyAsync` 签名 + 3 处 `ExecuteAsync` 调用点） |
| 3 | `visibleTo.mode` 拼错即全可见（S12） | 未知 mode 改为发布期报错（只接受 `all` / `none` / `roles` / `permissions`），**且覆盖到嵌套字段** | `FlowExecutor.cs` `ValidateVisibleTo`（internal） |
| 4 | `roleIds` 永不匹配（S12） | `visibleTo.roleIds` 显式拒绝并提示改用 `roleNames`（`ICurrentUser` 只暴露角色名），同样覆盖嵌套字段 | `FlowExecutor.cs` `ValidateVisibleTo` |
| 5 | `group.header[].visibleTo.permissions` 永不生效 | `CollectPermissionNames` 补上 `group.header[]` 与 `group.children.visibleTo` 两个入口 | `FinalOutputAssembler.cs` `CollectPermissionNames` |
| 12 | 发布成功、调用即 500（C2） | 新增 `SupportedNodeKinds` 白名单，与 dispatch case 逐一对应；`Sql` / `Switch` 及拼错类型发布期拒绝；移除 `Switch` 的出边豁免。**`Wait` 已于本轮补齐 dispatch 分支，故在白名单内** | `FlowExecutor.cs` `SupportedNodeKinds` / `ValidateDsl` / `ValidateOutEdgeCardinality` |
| 20 | PG 死锁/断连与 Oracle 全部错误码不重试（C13） | PG 改按 `SqlState` **字符串**分类（`08` 前缀 + `40001`/`40P01`/`53xxx`/`55P03`/`57P01~03`）；补 Oracle 错误码分支 | `NodeResiliencePolicy.cs` `IsTransientPostgresSqlState` / `IsTransientDbError` |
| — | `batchupdate` 抛 Missing SQL parameter + 戳比对恒不匹配（C16） | 合并 `filter.Args`；戳语义对齐单条 `UpdateAsync`（SET 写新戳 / WHERE 比旧戳，旧戳缺省 NULL 时跳过并发校验） | `ResourceCrudExecutor.cs` `BatchWriteAsync` |
| — | 未知 / 未实现 DSL 字段静默忽略（C4/E4） | `DslSchemaValidator` 在发布期拒绝；反射生成白名单故模型加字段自动跟随；拼写接近时给建议 | `DslSchemaValidator.cs` `ValidateJson` |
| — | 🆕 `payload` 被误列为「未实现字段」 | `payload.item` 是 `BuildRabbitPayload` 的实际消费点（消息体字段投影），此前被列入黑名单导致该形态**根本无法发布**，而提示语却写着"仅支持 raw 与 payload.item"。移除该条目，改由取值白名单接管 | `DslSchemaValidator.cs` `UnimplementedFields` |
| — | 🆕 `payloadMode=array` 静默退化为整包 inputs | `BuildRabbitPayload` 无 array 分支，配了等于没配。新增 `ValidateSemantics` 按取值拦截（`object` / `raw`，省略即 `object`） | `DslSchemaValidator.cs` `ValidateSemantics` |
| — | 🆕 出参契约校验只遍历根级 `outputs` | `ResolveFinalSchema` 是 `FinalOutputs` 优先，故只配 End 的流程可完整绕过 `aggregate`+`map` 冲突与 `wrap` 取值拦截。遍历范围扩到 `End.finalOutputs` | `FlowDefinitionAppService.cs` `ValidateOutputContracts` |
| — | 🆕 嵌套 `visibleTo` 未经发布期校验 | 拼错的嵌套 `visibleTo` 在运行期走 `OutputVisibilityFilter.IsVisible` 的 `return true` 兜底 = "配了可见性却全可见"，比不配更危险。新增递归校验 `map.item[]`（可无限层级）/ `group.header[]` / `group.children` | `FlowDefinitionAppService.cs` `ValidateNestedVisibleTo` / `ValidateGroup` |

> **回归测试**：`tests/Meta.Dow.Orchestration.Tests`（共 **297 例**，基线 91 例）。
> 新增 `PublishValidationCoverageTests`（钉住"校验覆盖范围"）与 `OutputVisibilityTests`（钉住权限名收集范围）。

> ⚠️ **这批修复揭示了一个可复用的失效模式**，文档读者需留意：
> **校验逻辑本身写对了，只遍历了"最显眼的那一层"，于是嵌套分支与 End 节点成为后门。**
> 本批 5 处修复中有4 处是这个模式（`payload` 黑名单、`aggregate`/`map` 冲突、`visibleTo` 嵌套、权限名收集）。
> 因此新增的测试**刻意针对覆盖范围而非具体取值**——防止再次退化成"只查根级"。

### 14.1 数据暴露类

| # | 严重度 | 现象 | 机理 | 位置 | 后果 / 规避 |
|---|---|---|---|---|---|
| 6 | **中** | 旧字段 `responseVariable` 绕过可见性 | 直写 `vars` / `FlatVarsLegacy`，不经 `outputs` / `visibleTo` | `FlowExecutor.cs` `ExecuteHttpNodeAsync`（`if (!string.IsNullOrWhiteSpace(node.ResponseVariable))`） | 旧 DSL 流程可把受限值直接放进输出。⚠️ 它**既不在 `UnimplementedFields`（有消费点）、也不被发布期拦截**，是当前唯一"无白名单 + 无拦截"的数据暴露路径 |

### 14.2 安全类

| # | 严重度 | 现象 | 机理 | 位置 |
|---|---|---|---|---|
| 7 | **高** | ✅ 已修 | 只读权限可被 `WITH` 前缀绕过 → 判定已抽为 `SqlWriteClassifier`，覆盖 `WITH` 与 `db.query`（§11.6 相关、§6.1） |
| 8 | **高** | ✅ 已修 | Http 节点 SSRF 无防护 → 新增 `HttpEgressGuard`，含私网/回环/链路本地/元数据/重定向（§11.6） |
| 9 | **中** | 无单条逻辑调用权限 | 只有 `Instances.Run` | `LogicApiAppService` |
| 10 | **中** | 静态检查过松又过严 | 正则词法：`db["execute"](…)` 不匹配；`const sql="…"` 被拒 | `CodeScriptStaticAnalyzer.cs` |

### 14.3 正确性与可靠性类

| # | 严重度 | 现象 | 机理 | 位置 |
|---|---|---|---|---|
| 11 | **高** | ✅ 已修 | 异步节点结果与失败全部丢失 → 改为持久化作业 `FlowAsyncJob` + worker 轮询，成功/失败均回写并带 `jobId`（§11.4） |
| 13 | **高** | ⚠️ 部分已修 | 异步不受节点超时约束、`failWhen` 不生效 → 作业已持久化、失败回写（§11.4）；但`failWhen` 仍不求值、节点 `timeoutMs` 仍不约束作业本身（用 Wait 的 `waitTimeoutMs`） |
| 14 | **高** | ✅ 已修 | 已下线流程仍可被调用 → 三入口（发布/停用/删除）均失效缓存，并经共享失效代数跨副本生效；**代价是最终一致**（§11.7） |
| 15 | **中** | ✅ 已修 | 条件算子缺失 → 现有 23 个算子并有发布期白名单（§6.1） |
| 16 | **中** | ✅ 已修 | `entry` 只配 `combine` 恒真 → 发布期拒绝 |
| 17 | **中** | ✅ 已修 | 条件节点可静默走错边 → 出边 `combine` 无 `items` 在发布期被拒 |
| 18 | **中** | ✅ 已修 | `strategy` 语义与命名不符 → 现明确 `firstMatch` / `allMatch` 且发布期校验取值 |
| 19 | **中** | 跨节点写库非原子 | 无 `txMode`；`db.execute` 每次新建连接自动提交，仅 `db.batch` 有事务。**注意 `txMode` 本身现会被 `DslSchemaValidator` 当未知字段拒绝**（模型无该属性），所以不存在"配了不生效"，而是"根本配不了" | 无字段；`ParameterizedSqlExecutor.cs` |
| 21 | **中** | ✅ 已修 | `aggregate` 与 `map` 同配时 map 静默失效 → 发布期拒绝，**根级 `outputs` 与 `End.finalOutputs` 均覆盖** | `FlowDefinitionAppService.cs` `ValidateOutputContract` |
| 22 | **中** | ⚠️ 部分不成立 | 原文称 `from` 模板与 `outputs` 字面量"无消费点"——**这是文档错误**：两者均有实现（`FlowContextResolver.ResolveTemplate` 处理 `"ORD-{{id}}"` 模板；`FlowContextResolver.ResolvePath` 处理 `{"literal": ...}`）。真正不生效的只有 **`inputs[].type` 不参与必填校验** | `FlowContextResolver.cs` `ResolveTemplate` / `ResolvePath`；`FlowExecutor.cs` `AreInputsSatisfied` |
| 23 | **中** | `timeoutMs` 对 Code 节点只生效到 8s | 沙箱经 Jint 硬顶 `CodeScriptTimeoutSeconds = 8`，而发布期允许配到 600s：≤8s 正常生效，**>8s 会被静默截断到 8s**，配 600s 与配 8s 效果相同 | `CodeSandboxExecutor.cs` `TimeoutInterval`；`OrchestrationConsts.cs` `CodeScriptTimeoutSeconds` |
| 24 | **中** | MQTT 触发器多副本重复执行 | client id 含随机 Guid，且无分布式锁 | `MqttMessageTriggerHostedService.cs` |
| 25 | **中** | 发布校验无 Warning 通道 | `ValidateDsl` 只 throw，返回 `void` | `FlowExecutor.cs` `ValidateDsl` |
| 26 | **低** | SQL 命名参数正则误匹配 | 字符串字面量、`::` 转型会抛 "Missing SQL parameter" | `ParameterizedSqlExecutor.cs` |
| 27 | **低** | ✅ 已修 | `payloadMode=array` 退化 → 发布期按取值拒绝（`object` / `raw`） | `DslSchemaValidator.cs` `ValidateSemantics` |
| 28 | **低** | Mask 预设的 keep 参数被硬编码 | phone/idCard/bankCard 固定 3/4、4/4、0/4 | `MaskValueHelper.cs` |
| 29 | **低** | 子图节点审计明细丢失 | 控制流只回传 `Ok/Output/Error` | `FlowExecutor.cs` `RunControlBodyAsync` |
| 30 | **低** | `joinMode=any` 不取消其余分支 | 只移除已完成 Task，其余继续执行 | `FlowExecutor.cs` `ExecuteParallelAsync` |
| — | **中** | 🆕 `FlowAsyncJobWorker` 多副本重复消费 | `GetQueuedJobsAsync` 无 claim/乐观锁语义，`MarkRunning` 也没有 CAS 条件（只判终态）。两个副本可同时取到同一条 `Queued` 作业并各自外呼一次 | `FlowAsyncJobDispatcher.cs` `GetQueuedJobsAsync`；`FlowAsyncJob.cs` `MarkRunning` |
| — | **低** | 🆕 `AttemptCount` 是只增不减的死字段 | `MarkRunning` 自增，但失败后无任何路径把它退回 `Queued` 重排队，故该字段永远只反映"被 worker 捞起过几次"，不反映重试次数 | `FlowAsyncJob.cs` `MarkRunning`；`FlowAsyncJobDispatcher.cs` `MarkFailedAsync` |

---

## 15. 规划（未实现）

> **以下能力当前不可用。** 与 §3–§13 的契约严格隔离；旧版把它们混写在契约章节，是文档失真的主要来源。

| 能力 | 说明 | 优先级 |
|---|---|---|
| **独立 `Sql` 节点** | 白名单库 + `@param` 参数化 + `Orchestration.Sql.Write`；目前只能经 Code `db.*` 或资源 CRUD | P1 |
| **`Switch`** | 多路枚举分支（缺执行分支，发布期被白名单拒绝） | P1 |
| **`txMode=sameDataSource`** | 按 DataSource 懒开连接+事务，同 code 的多次 `db.execute` 共享事务，成功 commit / 失败 rollback；需 `FlowDbSessionScope` 挂 `FlowRuntimeContext` | P1 |
| `Orchestration.Logic.{flowKey}` | 单条逻辑的调用权限码 | P1 |
| 发布校验 Warning 通道 | 不可达节点、无 `visibleTo`、敏感字段未标记、异步出参未 Wait 的引用 | P1 |
| `previewAsRole` | DryRun 按角色预览响应字段 | P2 |
| Outbox | 事务性发信到 Rabbit（库成功 ⟺ 消息必达） | P2 |
| MqttPublish 节点 | 画布内发 MQTT（目前只有 MQTT 触发器） | P2 |
| 领域组件目录 | `pricing.calc` 等注册式组件，替代堆 HTTP | P2 |
| EL 文本对照 | 侧栏显示 THEN/IF… 文本 | P3 |
| 字段级审计 | 出参字段级访问审计 | P3 |

---

## 16. 操作说明与设计器规格

> ⚠️ **本节涉及的前端均在 `D:\Project\vue-demo`（Vben Admin + AntV X6），不在本仓库，无法核验**。以下为产品与交互约定，后端 API 部分已按 §12 校正。

### 16.1 端到端主路径

```
① 新建逻辑，定义请求参数（可绑系统参数 / sys.Now - 3d）
→ ② 拖 Condition：多条件 + 1 and (2 or 3)，拉出多条分支边（配 isDefault）
→ ③ 拖执行节点（Http / Code）：配同步/异步、入参映射、resultRoot、出参投影
→ ④ 在 End 配置最终输出字段 + 哪些角色可见（或 promote 直接作为 data）
→ ⑤ 保存 → ⑥ 试运行（注意：Http 节点在试运行中不会真实外呼）
→ ⑦ 发布为系统 API
→ ⑧ 调用；按角色拿到可见字段
→ ⑨ 看实例日志（含条件命中、节点 Skipped、出参）
```

### 16.2 设计器三栏

| 区域 | 作用 |
|------|------|
| 左：节点库 | 结构 Start/End/Condition；执行 Http/Code/资源 CRUD；控制流 ForEach/Parallel/SubFlow |
| 中：画布 | 顺序与分支（X6） |
| 右：属性面板 | 条件表 + 组合式；节点 inputs/outputs；`async`；`timeoutMs`；最终可见角色 |

### 16.3 属性面板要点

- **Condition**：条件表（编号 / 左值 / 运算符 / 右值）+ 组合式输入框 + 模板按钮；出边列表每边绑组合式或"默认分支"。
  - **运算符下拉现应提供 23 个**（§6.1），含此前不可用的 `startswith`/`endswith`/`in`/`notin`/`between`/`isNull`/`isNotNull`。
    ⚠️ 旧版本文档曾写「不应提供 `in`/`notIn`/`between`（未实现）」——**该指引已作废**，它们现已实现。
  - ⚠️ **`between` 的右值必须是两个输入组成的数组**（闭区间 `[min, max]`），单输入控件会产出非法 DSL。
  - ⚠️ `isNull`/`isNotEmpty` 不需要右值，右值输入框应隐藏。
  - 建议补`strategy` 下拉（`firstMatch` / `allMatch`）—— 后端已支持并严格校验，但前端目前恒为 `firstMatch`，`allMatch` 不可达。
- **执行节点通用**：`kind`、`async`（⚠️ 目前仅 Http 生效）、`timeoutMs`、inputs 表、outputs 表。
- **Http**：`method`/`url`/`headers`/`bodyMode`/`resultRoot`；`mock://` 前缀可跳过真实外呼。
  ⚠️ 内网 / 回环 / 链路本地 / 云元数据地址会被**服务端出站闸门拒绝**（§11.6），面板宜提示，避免用户反复排查。
  ⚠️ 异步模式下应说明：出参含 `jobId`，需接 **Wait** 节点才能消费结果——旧文案「下游不可引用本节点出参（需 Wait，规划中）」已过时且语义相反。
- **Wait** 🆕：`jobId`（支持 `{{results.xxx.jobId}}` 模板）、`waitTimeoutMs`（1..600000，默认 30000）、`waitFailOnTimeout`（默认开）。字段与校验见 §6.4。
- **Code**：`dataSourceId` + 脚本；面板应提示 `db.query/execute/batch` 三 API 与静态检查规则。
  ⚠️ `db.execute` / `db.query` 均受 `Orchestration.Sql.Write` 写闸门约束，判定已覆盖 `WITH` 前缀。
- **End / 最终输出**：字段名、类型、`from`、`visibleTo`、`sensitive`（⚠️ `sensitive` 目前仅打日志）、`map`、`group`、`aggregate`、`promote`、`wrap`。
- ⚠️ **不要提供**：`transform`、`from` 模板、`outputs` 字面量、`asyncJobId`、`previewAsRole`。
  这些字段服务端**已显式拒绝**（`DslSchemaValidator` 的「模型有但无消费点」清单）——不是静默忽略，而是发布时报错。

### 16.4 画布与快捷键（沿用原文）

撤销重做 `Ctrl+Z`/`Ctrl+Y`、复制粘贴 `Ctrl+C`/`Ctrl+V`、删除 `Delete`、缩放 `Ctrl+滚轮`、双击节点展开属性面板、右键上下文菜单。节点外观：Start/End 圆形、Condition 菱形（角标显示组合式摘要）、执行节点卡片（显示入参/出参个数）；异步节点应有角标。

### 16.5 流程停用 / 启用🆕

`definitions/index.vue` 的操作列需按状态互斥显示「停用」/「启用」（`POST .../definitions/{id}/disable` / `.../enable`）。
状态渲染层（`flow-meta.ts` 的 `definitionStatusMeta` 含 `2: 已禁用`、筛选项、列表 Tag）**前端已就绪**，只缺API 函数与两个按钮。

---

## 17. 对标与明确不做

### 17.1 主线对标

| 对象 | 定位 | 借鉴 | 不照搬 |
|---|---|---|---|
| **LiteFlow** | Java 规则引擎，EL 表达 THEN/IF/SWITCH/WHEN/FOR | "代码逻辑编排"这个命题与控制流原语划分 | Java 生态、组件注册机制 |
| 宜搭「逻辑编排」 | 表单提交后的逻辑增强 | 判式可视化、节点 I/O 映射 | 连接器市场 |
| 微搭「逻辑流」 | 开始入参 → 条件/并行 → 结束出参 | 服务端表达式、入参/出参契约 | 云绑定 |
| X6 flowchart / liteflow-editor-client | 画布实现参考 | 画布交互、EL 文本预览 | 直接嵌入编辑器 |

### 17.2 控制流原语对标（现状）

| 代码结构 | 本仓对应 | 状态 |
|---|---|---|
| 顺序 | 节点 + 边 | ✅ |
| `if / else if / else` | Condition（多出边 + `combine` + `isDefault`） | ✅（算子已补齐 23 个） |
| `switch` | Switch | ❌ |
| `for` / `foreach` | ForEach / Loop（内联子图或 SubFlow） | ✅ |
| `Task.WhenAll` | Parallel / WHEN（`joinMode=all`） | ✅ |
| `Task.WhenAny` | Parallel（`joinMode=any`） | ✅（不取消其余分支） |
| `await` 汇合 | Wait | ✅ |
| `try / catch / finally` | `onError`、`failItems`+`failCombine`、`throw` | ✅ 部分 |
| 事务 | `txMode=sameDataSource` | ❌ |
| 方法调用 | SubFlow（逻辑组件） | ✅ |

### 17.3 明确不做（控风险）

| 不做 | 原因 |
|---|---|
| 审批流 / 会签 / 待办 | 不是本主题（另有 OA 引擎） |
| n8n 式连接器市场 | 偏离「逻辑 API」定位 |
| 任意用户脚本无沙箱 | 多租户安全 |
| 无白名单、可拼接的生产库 SQL | 审计失控 |
| 调用方覆盖 `sys.userId` | 防伪造身份 |
| 能调 API 就返回全部字段 | 违反最小可见 |
| 页面可视化搭建 | 正交能力（另有表单库） |
| 跨库 XA / 两阶段提交 | 运维重，Redis/Mongo 难支持 |

---

## 18. 参考链接

| 资源 | URL |
|---|---|
| LiteFlow | https://github.com/dromara/liteflow |
| LiteFlow 构造 EL | https://liteflow.cc/pages/a3cb4b/ |
| X6 可视化 LiteFlow | https://github.com/Cooooooler/liteflow-editor-client |
| 宜搭逻辑编排 | https://docs.aliwork.com/docs/yida_qalist/lmvsctyt7rxtgsrq/lbvx0y |
| 微搭逻辑流（条件/并行） | https://cloud.tencent.com/document/product/1301/77287 |
| AntV X6 | https://github.com/antvis/X6 |
| LogicFlow | https://github.com/didi/LogicFlow |
| n8n（仅借鉴调试，非产品形态） | https://github.com/n8n-io/n8n |
| Elsa（.NET 工作流，非主线） | https://docs.elsaworkflows.io/getting-started/concepts |
| 本仓 IAM | [identity-contract.md](./identity-contract.md) |
| 本仓表单/OA/报表 | [form-contract.md](./form-contract.md)、[data-collection-contract.md](./data-collection-contract.md)、[report-contract.md](./report-contract.md) |
| 本仓编排配套 | [完善意见稿](./logic-orchestration-review.md)、[落地方案](./logic-orchestration-plan.md)、[取舍对比](./logic-orchestration-tradeoffs.md)、[主流对标分析](./logic-orchestration-benchmark.md) |

---

## 19. 修订记录

### 2026-10-13 结构性重构（本文）

**为什么改**：旧版把「目标态规格」与「现状事实」混写在同一章，同一能力在文档中一处写"已落地"、另一处写"规划中"；且大量"已实现"断言对应的字段在代码里不存在（例如旧版 §9.3 给出的可执行 DSL 示例包含 `kind: "Sql"`，而执行器没有该分支）。这直接导致过按文档配置、发布成功、调用即失败。

**结构变化**

| 旧版 | 新版 |
|---|---|
| §5.4 / §9.3 / §11 / §14 四处重复描述同一批节点 | 合并为 §3–§9 的单一契约 |
| §0 / §11.3 核对表 / §14 差距表 / §15 分期清单（四张互相矛盾的状态表） | 合并为 §2 唯一权威表 |
| "已落地/规划中"混写在契约章节 | 契约（§3–§13）与规划（§15）严格隔离 |
| 无缺陷章节 | 新增 §14「行为边界与已知缺陷」（按后果排序，30 条，附机理与行号） |
| `file:///d:/Project/...` 绝对盘符链接 | 仓库相对路径（跨文档用 `./xxx.md` 相对链接） |

**删除的与代码矛盾的断言（择要）**

| 旧版断言 | 代码真相 |
|---|---|
| §9.3 示例含 `kind: "Sql"` 节点，§5.4.5 把 Sql 列为三大 kind 之一 | 执行器无 `case "sql"`（`FlowExecutor.cs:791-792`），发布期也不拦 |
| §5.5 列出条件算子 `in`/`notIn`/`between` | 未实现，落到 `Unsupported condition op`（`:2297-2310`） |
| §5.4.2 `transform` 支持 trim/toString/toNumber/JSONPath | `Transform` 属性零消费点（`FlowDslModels.Transform`）；🆕 现已在发布期直接拒绝 |
| §5.4.2 `from` 来源可为"模板"；§5.4.4 `outputs.from` 支持字面量 | ❗**这条"旧版断言"本身是错的**——两者**均已实现**（`FlowContextResolver.ResolveTemplate` 处理 `"ORD-{{id}}"`；`ResolvePath` 处理 `{"literal": ...}`）。是后续的 §14-22 条目误抄了本行，才把正确实现写成了缺陷 |
| §5.4/§11.4 "每个执行节点都有同步/异步" | 只有 Http 读取 `async`，其余静默忽略（`:723-727`） |
| §5.4.1 "异步任务完成后写 outputs / 记实例；失败记节点错误" | 全部未落地（`:911-942`） |
| §5.4.1/§10.4 "DryRun 异步一律改同步" | DryRun 下 Http **被跳过**（`:712-721`） |
| §5.4.7 `txMode=sameDataSource` 为"已采纳模型" | DSL 无该字段，跨节点 `db.execute` 逐句提交 |
| §9.4 Code 静态检查是"脚本 AST"、发布期 Error | 正则词法检查、运行期执行 |
| §9.4 "无 InputSchema 不可发布"（列为不可破原则） | 不校验，`inputs` 为空时走 legacy 平铺路径（`:371-381`） |
| §9.4 三条约 Warning 规则 | 无 Warning 通道，`ValidateDsl` 只 throw |
| §10.3/§13 "试运行可按角色预览（`previewAsRole`）" | DTO 无该字段；DryRun 恒 bypass 过滤 |
| §13 `POST /api/orchestration/instances:dry-run` | 实际为 `/api/orchestration/instances/dry-run` |
| §13 `GET /api/orchestration/roles` | 路由不存在 |
| §13 响应含 `meta.lookupMs/executeMs/persistMs/totalMs` | DTO 有，但手写 controller 不返回 |
| §11.3 状态表"Switch/**Loop/ForEach**/Sql 未实现"、"Parallel 未实现" | ForEach/Loop 与 Parallel/WHEN **已实现**并有 39 个测试 |
| §14 差距表把"系统参数/条件节点/节点 I/O/最终出参可见性/发布为 API/取值选择器"列为 P0 未做 | 均已实现 |
| §15 阶段 3 `- [ ] Wait、SWITCH、WHEN` | WHEN 已实现 |
| §5.9 "执行器尚未真正投递后台（规划 P1）" | 已投递（`Task.Run`），实现质量见 §14-11 |
| §11.1 未列 SubFlow / 资源 CRUD 节点 | 均已实现（`:750-757`、`:775-789`） |

**补入的、旧版漏记的既有能力**

- 列表路径糖：`x.count`、`x.sum.amount`、`x.first.y`、`x.max.date`（`FlowContextResolver.cs:168-215`）。
- 控制流原语 ForEach / Parallel / WHEN 的完整参数（`itemsPath`、`concurrency`、`itemErrorMode`、`maxIterations`、`joinMode`、`branchTimeoutMs`、`branch.optional`）。
- 节点级超时的默认值与合法区间（30s / 50..600000ms / 0=不限）。
- 弹性策略的读写分离与 `retryOnWrite` + 幂等键门禁。
- 资源 CRUD 九个别名节点。

**已知未收敛项**（本次只改文档，未改代码）：§14 全部 30 条缺陷保持现状；建议按其中 1、2、7、 11、12、14 六条优先修复。

---

### 2026-10-14 整改后同步（对应[评估文档](./logic-orchestration-assessment.md)§八）

**为什么改**：§2能力表自称「唯一状态来源」，但本轮整改落地后，其中若干条已失真——包括一条**会主动误导前端**的指令（§16.3 原写「运算符下拉不应提供 `in`/`notIn`/`between`」，而这三个算子现已实现）。文档失真会让前端按错误的指引实现，因此本次同步逐条核对。

**已由失真转为准确的能力表条目**

| 条目 | 原状态 | 现状态 |
|---|---|---|
| 条件节点算子 / `strategy` | ⚠️ 算子缺 3 个、`strategy` 从不校验 | ✅ 23 个算子 + 发布期白名单（§6.1） |
| Wait 节点 | ❌ 无 | ✅ 已实现（§6.4新增规格） |
| Http 节点 SSRF | （备注）无防护🐞 | ✅ `HttpEgressGuard`（§11.6 新增章节） |
| 发布校验（结构） | ⚠️ 无节点类型白名单 | ✅ 含白名单、未知字段、算子与 `strategy 校验（§10.2） |
| 发布缓存 | ⚠️ 失效只挂发布 | ⚠️ 三入口失效 + 跨副本代数，**最终一致**（§11.7） |
| 流程停用 / 启用 | 未收录 | ✅ 新增两个端点（§12.1） |

**新增章节**：§6.4 Wait 节点规格、§11.6 出站闸门。

**§14 缺陷表中已修复的条目**（原编号）：11（异步 fire-and-forget）、15（条件算子缺失）、16（`strategy` 不校验）、22（出站无白名单）、26→ 见 §11.6、28（SQL 写判定绕过 `WITH`）已在 §11.6 / §6.1 反映。**其余条目保持现状。**

**⚠️ 本次同步仍未做的**：§14 中大量条目引用了 `FlowExecutor.cs` 的**旧行号**。本轮整改使该文件净增数百行，**这些行号已全部不可信**。请一律按符号名检索（与[评估文档](./logic-orchestration-assessment.md)同一条引用纪律）。彻底清理行号需要一次独立的文档治理 pass。

---

### 2026-10-14 校验覆盖范围补齐批次（§14.0 末四行）

**为什么改**：上一批同步是"按代码核对文档"，本轮反过来——**按文档逐条回读代码**，专门找文档声称已修但实际只修了一半的地方。结果发现 5 处，其中**一处是上一批自己引入的回归**。

**发现的可复用失效模式**

> **校验逻辑本身写对了，只遍历了"最显眼的那一层"，于是嵌套分支与 End 节点成为后门。**

本批 5 处修复中有 4 处命中这个模式。典型症状：代码看起来有校验、测试也覆盖了，但测试同样只测了根级路径，于是"覆盖范围"从未被验证。新增的两个测试文件因此**刻意针对覆盖范围而非具体取值**。

**逐条修正**

| 条目 | 核对发现 | 处理 |
|---|---|---|
| `payload` | ❗**上一批引入的回归**：`payload.item` 是 `BuildRabbitPayload` 的实际消费点，却被列入 `UnimplementedFields` 黑名单，导致该形态**根本无法发布**——而提示语恰好写着"仅支持 raw 与 payload.item"，自相矛盾 | 移除黑名单条目；改由 `ValidateSemantics` 按**取值**接管 |
| `payloadMode=array` | 模型注释写了 `object \| array \| raw`，但代码无 array 分支，配了等于没配 | 新增取值白名单（`object` / `raw`），发布期拒绝 |
| `aggregate`+`map` 冲突 / `wrap` 取值 | `ValidateOutputContracts` 只遍历 `dsl.Outputs`；而 `ResolveFinalSchema` 是 `FinalOutputs` 优先，故**只配 End 的流程可完整绕过** | 遍历范围扩到 `End.finalOutputs`（条目 21 转✅） |
| 嵌套 `visibleTo` | 校验方法本身正确，但只在顶层被调用；嵌套拼错仍走 `OutputVisibilityFilter.IsVisible` 的 `return true` 兜底 = "配了可见性却全可见" | 新增递归校验 `map.item[]`（无限层级）/ `group.header[]` / `group.children` |
| `group.header[].visibleTo.permissions` | `CollectPermissionNames` 漏收 `group.header` 与 `group.children.visibleTo` → 权限模式下分组表头对所有人消失（条目 5 转 ✅） | 补全收集范围 |

**同时修正的文档错误**（代码是对的、文档写错了）

- §14-22 原称"`from` 模板与字面量无消费点" → **错**，两者均有实现（`ResolveTemplate` / `ResolvePath`）；已改为「仅 `inputs[].type` 不参与必填校验」。§19 中误抄该错误断言的那一行也已标注。
- §14-23 原称"`timeoutMs` 对 Code 无效" → 改为「≤8s 正常生效，**>8s 静默截断**」，并同步 §11.2。
- §14-19 补注：`txMode` 现会被 `DslSchemaValidator` 当未知字段拒绝，故不是"配了不生效"而是"根本配不了"。
- §14-6 补注：`responseVariable` **有**消费点故不在黑名单内，也未被发布期拦截，是当前唯一"无白名单 + 无拦截"的数据暴露路径。

**新记录的残留缺陷**（文档此前完全没有的）

- `FlowAsyncJobWorker` **多副本重复消费**：`GetQueuedJobsAsync` 无 claim/乐观锁，`MarkRunning` 也无 CAS 条件，两个副本可同时取到同一条 `Queued` 作业并各自外呼一次。
- `AttemptCount` 是**只增不减的死字段**：失败后无路径退回 `Queued` 重排队，故它反映的是"被捞起过几次"而非"重试了几次"。

**测试**：269 → **297**，新增 `PublishValidationCoverageTests`、`OutputVisibilityTests`。受影响工程（Application / MongoDB）编译 0 错误。
