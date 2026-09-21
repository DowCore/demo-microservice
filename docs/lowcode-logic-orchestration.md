# Meta.Dow 低代码逻辑编排方案

> 目标：在现有 **ABP 微服务 + Vue Vben（web-antd）** 上，落地一套 **代码逻辑编排** 能力。  
> 前端：`D:\Project\vue-demo`（AntV X6 设计器）  
> 后端：`demo-microservice`（MVP 落在 `Meta.Dow.SaaS`）  
> 本文同时是：**产品定位校准 + 如何编排代码逻辑 + 技术总纲**。

**一句话定位（请以此为准）**：先定义逻辑请求参数（可引用系统参数与日期表达式）；画布上用 **条件节点**（多条件 + 逻辑运算）分支，用 **执行节点**（HTTP / 代码块 / SQL，各配入参/出参，可异步）；最终结果字段可配置 **角色可见性**；发布后成为系统 API。目标是做成强大的逻辑编排引擎，而不是连接器市场或审批流。

---

## 目录

1. [范围界定](#1-先界定范围四种能力不要混为一谈)
2. [主题校准：代码逻辑编排](#2-主题校准代码逻辑编排不是-ipaas)
3. [开源与产品对照](#3-开源与产品对照github--国内低代码)
4. [编排心智模型](#4-本仓库的编排心智模型)
5. [产品规格：入参 / 系统 API / 节点 / 条件](#5-产品规格入参--系统-api--节点--条件)（含 [引擎能力全景](#58-强大逻辑编排引擎能力全景)、[同库事务](#547-同库统一事务per-datasource)、[Rabbit 发布](#548-消息发布节点rabbitmqpublish优先本地-rabbitmq)）
6. [如何进行编排](#6-如何进行编排产品操作说明)
7. [设计器 UX](#7-设计器交互规格ux)
8. [目标架构](#8-目标架构对齐现有微服务)
9. [领域模型与 DSL](#9-领域模型与-dsl-规范)
10. [执行语义](#10-数据流与执行语义)
11. [节点类型规划](#11-节点类型规划)
12. [IAM / 菜单](#12-与现有-iam--菜单的对接)
13. [API](#13-api)
14. [MVP 差距](#14-当前-mvp-与目标差距)
15. [分期任务](#15-分期落地任务清单)
16. [不做什么](#16-明确不做什么控风险)
17. [选型结论](#17-技术选型结论)
18. [参考链接](#18-参考链接)
19. [下一步](#19-建议的下一步)

---

## 1. 先界定范围：四种能力不要混为一谈

| 类型 | 典型产品 | 解决什么 | 本方案 |
|------|----------|----------|--------|
| **A. 页面/表单低代码** | LowCodeEngine、Amis、NocoBase | 拖拽页面、CRUD、看板视图 | **另文规划**，见 [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md)（本文不实现） |
| **B. 审批/BPM** | 钉钉审批、Flowable、Camunda | 人审、会签、待办 | **另文规划**（同上）；逻辑编排画布内仍明确不做 |
| **C. 自动化 / iPaaS** | n8n、Zapier、阿里云逻辑编排（连接器） | 把 Slack/钉钉/HTTP 等 SaaS 串起来 | **易偏离主题，仅借鉴调试/版本，不做成连接器市场** |
| **D. 代码逻辑编排** | **LiteFlow**、宜搭「逻辑编排」、微搭逻辑流、LogicFlow 逻辑图 | 把 **if/顺序/并行/循环/调内部服务** 从代码里抽出来可视化拼装 | **是（主方案）** |

上一版文档对标 n8n/Dify 过多，会把产品做成「集成自动化」。本仓库要做的是 **D**：对业务代码的可视化编排。

**正确的产品一句话**：开发者把领域逻辑拆成组件（校验、计价、写库、调内部 API）；编排者在画布上用 **THEN / IF / SWITCH / WHEN** 拼出可热更的执行图；运行时按图执行，共享一份上下文 `vars`。

---

## 2. 主题校准：代码逻辑编排（不是 iPaaS）

### 2.1 要解决的代码问题（LiteFlow 原话场景）

复杂业务（下单、计价、开通租户）常见形态：

```csharp
// 瀑布流 + 硬编码分支，改顺序就要改代码、全量回归
ValidateOrder();
if (order.Amount > 1000) { NotifyRisk(); }
else { MarkNormal(); }
SaveOrder();
PublishEvent();
```

痛点：步骤耦合、复用差、改流程等于改代码、无法热变更。  
**代码逻辑编排** 的做法：每个步骤变成独立组件，**流转只由规则（图 / EL）驱动**。

LiteFlow 称之为「工作台模式」：组件（工人）只读写上下文（工作台），互不调用；换顺序、插入、撤掉组件，工人代码不变。

### 2.2 和「自动化编排」差在哪

| | 代码逻辑编排（本方案） | iPaaS 自动化（n8n 等） |
|--|------------------------|------------------------|
| 用户 | 研发 / 懂业务的实施 | 运营 / 集成工程师 |
| 节点是什么 | **领域组件**：校验、算价、写单、调本系统 AppService | **连接器**：Gmail、Slack、HTTP 通用 |
| 控制流 | THEN / IF / SWITCH / WHEN / FOR，对标代码结构 | Trigger → Action 流水线 |
| 数据 | 一份业务上下文 `vars` / LiteFlow Context | 每节点 `$json` 管道 |
| 触发 | 业务代码里 `Run(flowKey, ctx)`；表单提交、领域事件 | Webhook / Cron / 第三方事件 |
| 成功标准 | 能替换一段 C# 业务方法 | 能打通两个 SaaS |
| 官方边界 | LiteFlow：**只做逻辑流转，不做角色审批** | n8n：集成优先 |

宜搭把「逻辑编排」定义为：用 IF / 分支 / 创建数据 / 异常结束等 **连接器拼业务逻辑**，用于表单提交后的增强，而不是审批人流转。微搭「逻辑流」则是：开始节点收参 → 条件/并行 → 结束节点出参，表达式操作 `#变量`。这些才是 D 类产品。

### 2.3 业界怎么实现「代码逻辑」的可视化

成熟路径几乎都是 **两段式**：

```
① 开发者注册组件（C# / 脚本，单一职责）
② 画布拼控制流  →  生成 EL / DSL  →  引擎按图执行
```

| 实现 | 仓库 / 产品 | 画布 | 执行 |
|------|-------------|------|------|
| LiteFlow 本体 | [dromara/liteflow](https://github.com/dromara/liteflow) | 规则文件 EL，可无 UI | Java 组件 + 脚本热更 |
| X6 可视化 LiteFlow | [Cooooooler/liteflow-editor-client](https://github.com/Cooooooler/liteflow-editor-client) | **AntV X6**：EL 树 → 节点/边 | 后端跑 LiteFlow EL |
| LogicFlow → EL | [logicflow-liteflow](https://gitee.com/xdewx/logicflow-liteflow)、[liteflow-logicflow-vue](https://gitee.com/ganzhirong/liteflow-logicflow-vue) | LogicFlow 图 | 图 JSON → `THEN/IF/WHEN…` |
| 图转 EL（后端） | [iflytek-liteflow-el-builder](https://github.com/356110537/iflytek-liteflow-el-builder) | 任意前端 nodes/edges | `LiteFlowUtil.createEL` |
| 宜搭逻辑编排 | 钉钉宜搭文档 | 开始/IF/分支/异常结束/写数据 | 平台执行器 + 连接器 |
| 微搭逻辑流 | 腾讯云微搭 | 开始(入参)/结束(出参)/条件/并行 | 服务端表达式 `#var` |

**本仓库应对齐这条路径**：X6 画控制流 → 规范化 DSL（可演进为 LiteFlow 风格 EL）→ .NET 解释器执行 **已注册的领域组件**。HttpCall / Log 只是组件的两种实现，不是产品中心。

### 2.4 控制流原语（对标代码，而不是对标 Zapier）

LiteFlow EL 是「代码逻辑」的标准抽象，画布节点应直接对应这些关键字：

| 代码结构 | LiteFlow EL | 画布怎么画 | Meta.Dow 现状 |
|----------|-------------|------------|----------------|
| `A(); B(); C();` | `THEN(a, b, c)` | 串行连线 | 有（边无 when） |
| `if (x) A(); else B();` | `IF(x, a, b)` | Condition + true/false 边 | 有 Condition |
| `switch (x) { case … }` | `SWITCH(x).TO(a, b)` | 多出口边，边带 tag | **缺 SWITCH** |
| `Task.WhenAll` | `WHEN(a, b, c)` | 并行网关或分组 | **缺** |
| `for / while` | `FOR/WHILE.DO(…)` | 循环分组 + break 边 | **缺（一期可禁环）** |
| `try/finally` | `THEN(…).PRE(…).FINALLY(…)` / `CATCH` | 分组 | 后置 |
| `a && b` | `AND/OR/NOT` | **Condition 多条件 + 组合式** `1 and (2 or 3)` | 目标能力；现仅边 true/false |

示例（把开头那段 C# 编成 EL）：

```text
THEN(
  validateOrder,
  IF(amountOverLimit, notifyRisk, markNormal),
  saveOrder,
  publishEvent
);
```

画布上应能看出 **同一棵控制流树**，保存时生成等价 DSL/EL，而不是一串「HTTP 连接器」。

### 2.5 组件 vs 控制流（双角色）

| 角色 | 做什么 | 类比 |
|------|--------|------|
| **组件开发者** | 写 `ILogicComponent`：读 `vars`，写 `vars`，调 AppService；注册 `code`（如 `pricing.calc`） | LiteFlow `@LiteFlowComponent("a")` |
| **逻辑编排者** | 不写 C#；从节点库拖 **已注册组件 + IF/THEN 结构**，配表达式与入参映射 | LiteFlow 规则文件 / 宜搭画布 |

节点库应分成两栏（宜搭/LiteFlow 编辑器都是这样）：

1. **结构节点**：开始、结束、IF、SWITCH、并行、循环（控制流）  
2. **逻辑组件**：本系统已注册的领域步骤（才是「代码」）

当前 MVP 把 HttpCall/Log 当成主节点，容易让人以为在做 n8n。正确演进：**HttpCall 只是一种通用组件**；主菜单应出现「校验订单」「计算价格」这类业务组件。

### 2.6 调用方式：逻辑被代码调用，而不是替代整个系统

代码逻辑编排的入口通常是 **函数调用**，而不是运维去点「运行」：

```csharp
// 领域服务里触发已发布的逻辑图（对标 LiteFlow flowExecutor.execute2Resp）
await _logicOrchestrator.RunAsync("order.submit", ctx: new { order });
```

也可挂在：表单提交后（宜搭）、领域事件、管理端试运行。  
列表页「手动运行」只是调试手段，不是主触发模型。

### 2.7 本方案落地映射

| LiteFlow / 宜搭概念 | Meta.Dow |
|---------------------|----------|
| Component | 注册表中的逻辑节点（C# handler + 元数据） |
| Context / 工作台 | `vars`（P1 加 `results[nodeId]`） |
| EL | `dslJson`（可增加 `el` 字段或从 DAG 生成 THEN/IF） |
| Chain / 规则 | `FlowDefinition` + `FlowVersion` |
| 热刷新 | 发布新版本后下次 `RunAsync` 即用新图 |
| 脚本组件 | P2 再考虑受限表达式；禁止任意 C# |

**.NET 注意**：LiteFlow 是 Java。首期继续自研解释器对齐 EL 语义即可；不要为了「用 LiteFlow」引入 JVM。若未来要 1:1 兼容，可评估移植 EL 解析或把规则交给独立 Java sidecar（不作为默认路径）。

---

## 3. 开源与产品对照（GitHub / 国内低代码）

下列对照分两档：**主线（代码逻辑）** 与 **容易跑偏（仅借鉴体验）**。

### 3.1 主线对照

| 项目 | 定位 | 值得学 | 不宜照搬 |
|------|------|--------|----------|
| [dromara/liteflow](https://github.com/dromara/liteflow) | 编排式规则引擎，**组件=代码片段** | THEN/IF/WHEN…、上下文解耦、热更、明确不做审批 | Java 实现；脚本语言过多不安全 |
| [liteflow-editor-client](https://github.com/Cooooooler/liteflow-editor-client) | **X6 画 EL 树** | 结构节点与业务组件分层、EL↔图互转 | React 栈 |
| logicflow-liteflow / liteflow-logicflow-vue | 图画 EL | IF 边 true/false、FOR 用分组、并行 GROUP | 前端框架不同 |
| 宜搭逻辑编排 | 表单后业务逻辑 | 开始连接器=触发、IF/分支/异常结束、动态取值 | 钉钉连接器生态 |
| 微搭逻辑流 | 服务编排 | 开始入参/结束出参、标准条件 vs 公式、并行汇聚 | 腾讯云绑定 |

### 3.2 辅助对照（借鉴体验，不要做成产品形态）

| 项目 | Stars 量级（约） | 定位 | 可借鉴 | 易偏离主题之处 |
|------|------------------|------|--------|----------------|
| [n8n-io/n8n](https://github.com/n8n-io/n8n) | 20万+ | iPaaS 自动化 | 试运行、执行日志、表达式 | 1500+ 连接器；产品中心是集成不是代码解耦 |
| [langgenius/dify](https://github.com/langgenius/dify) | 10万+ | AI 工作流 | 发布为 API、调试 | 强绑 LLM |
| [windmill-labs/windmill](https://github.com/windmill-labs/windmill) | 1万+ | 脚本 Flow | `results.{id}` 引用 | 以脚本为中心 |
| [elsa-workflows/elsa-core](https://github.com/elsa-workflows/elsa-core) | 7千+ | .NET 工作流 | Activity/Outcome；同栈 | 偏工作流/阻塞，不等于 LiteFlow 式组件编排 |
| [antvis/X6](https://github.com/antvis/X6) | 6千+ | 图编辑引擎 | Stencil、端口、插件 | 只画不跑 |
| [didi/LogicFlow](https://github.com/didi/LogicFlow) | 1万+ | 逻辑图画布 | 逻辑编排场景、图转引擎 | 本仓库已选 X6，可作对照 |
| [gfYoboo/workflowEditor](https://github.com/gfYoboo/workflowEditor) | 数百 | X6 审批设计器 | 属性面板 | **审批 BPM，与主题相反** |

### 3.3 概念对齐（以代码逻辑为主）

```
┌──────────────┐   ┌────────────────┐   ┌────────────┐   ┌────────────┐
│ 控制流结构     │ → │ 逻辑组件 Node   │ → │ 上下文 vars │ → │ 执行实例    │
│ THEN/IF/WHEN  │   │ 领域代码片段    │   │ 工作台      │   │ 逐步日志    │
└──────────────┘   └────────────────┘   └────────────┘   └────────────┘
        ↑                    ↑                  ↑               ↑
  LiteFlow EL          @Component           Context        FlowInstance
  宜搭 IF/分支         微搭活动节点         #变量           试运行结果
```

| 概念 | LiteFlow | 宜搭/微搭 | Meta.Dow |
|------|----------|-----------|----------|
| 控制流 | THEN/IF/SWITCH/WHEN | IF、分支、并行 | 结构节点 + `edges.when` |
| 一步逻辑 | Component | 连接器/活动 | **注册组件**（HttpCall/Log 只是通用组件） |
| 上下文 | Slot / Context | 动态内容 / `#var` | `vars` |
| 入口 | `execute(chainId)` | 表单生命周期 / 开始连接器 | **`RunAsync(flowKey, ctx)`** + 调试运行 |
| 产物 | EL 规则 | 逻辑流定义 | `dslJson`（可生成 EL 风格文本） |

### 3.4 对 Meta.Dow 的直接启示（校准后）

1. **主线是组件编排，不是连接器编排**（LiteFlow / 宜搭逻辑编排）。  
2. **画布画的是控制流树**，应对齐 THEN/IF/WHEN，而不仅是「节点+任意边」。  
3. **节点库分两栏**：结构节点 + 已注册领域组件（见 liteflow-editor-client）。  
4. **主触发是代码调用** `RunAsync`；管理端运行只做调试。  
5. **明确不做审批**（LiteFlow 官方边界；审批用 Flowable/flowlong）。  
6. **n8n/Dify 只借调试与版本体验**，不借产品形态。  
7. X6 只负责「画」；执行、组件注册、版本在后端。

---

## 4. 本仓库的编排心智模型

把一次 **代码逻辑** 编排理解成四层：

| 层 | 名称 | 谁在用 | 存什么 |
|----|------|--------|--------|
| L1 画布 | Graph | 设计器 | 控制流结构 + 组件落点（AntV X6 JSON） |
| L2 语义图 | DSL / EL | 执行器 | THEN/IF… 或等价 nodes/edges |
| L3 定义 | FlowDefinition + FlowVersion | 管理端 | 草稿、已发布不可变版本 |
| L4 运行 | FlowInstance | 调试 / 审计 | 每次执行的 vars 与节点日志 |

**编排 = 在 L1 上拼控制流与组件 → 生成 L2 → 发布 L3 → 业务代码 `RunAsync` 产生 L4。**

```
组件开发者注册 Handler ──┐
                         ▼
编排者拖拽画布 (L1) ──graphToDsl/EL──► DSL (L2) ──发布──► FlowVersion (L3)
                                                              │
                              业务代码 RunAsync(flowKey, ctx) ──┘
                                                              ▼
                                                         FlowInstance (L4)
```

---

## 5. 产品规格：入参 / 系统 API / 节点 / 条件

本章按产品口径定义「一条逻辑」必须具备的能力。后续设计器、DSL、执行器都按此实现。

### 5.1 一条逻辑长什么样

```
定义请求参数（可绑系统参数 / 日期表达式）
        ↓
画条件节点：多条件 + 逻辑运算分支（1 and (2 or 3)）
        ↓
画执行节点：HTTP / 代码块 / SQL
  · 配置本节点输入参数映射
  · 配置本节点输出参数映射
  · 可选异步
        ↓
配置最终输出：字段 ← 节点出参；并可指定哪些角色可见
        ↓
试运行 → 发布为系统 API
        ↓
调用方按角色拿到「可见字段子集」
```

和「只在画布上拖 if-else」的差别：

| 能力 | 说明 |
|------|------|
| **先定契约** | 逻辑先有入参 schema，再画实现 |
| **发布即 API** | 发布后成为系统接口 |
| **系统参数 + 日期公式** | `sys.userId`、`sys.Now - 3d` 等 |
| **条件节点多条件逻辑** | 独立 Condition 节点：条件列表 + `and/or/not` |
| **执行节点入参/出参** | 每个 Http/Code/Sql 显式声明输入映射与输出契约 |
| **最终结果角色可见** | 出参字段可绑角色，调用方只看到有权字段 |
| **节点可异步** | 不阻塞主链返回 |

### 5.2 请求参数（Input Schema）

每条逻辑**必须先定义请求参数**，相当于方法签名。发布为 API 后，调用方按此传 JSON；试运行也按此填表。

#### 参数定义

| 字段 | 说明 |
|------|------|
| `name` | 参数名，如 `orderId`、`amount`、`startDate` |
| `displayName` | 中文名 |
| `type` | `string` / `number` / `boolean` / `datetime` / `guid` / `object` / `array` |
| `required` | 是否必填 |
| `default` | 默认值；可写字面量，也可绑系统参数 |
| `source` | `input`（调用方传入）/ `system`（系统参数或日期表达式）/ `expr`（其它表达式） |
| `systemKey` | 固定系统键，如 `sys.userId`、`sys.MonthStart` |
| `systemExpr` | 日期表达式，如 `sys.Now - 3d`、`sys.Today + 1M \| startOfDay`（与 systemKey 二选一） |
| `rules` | **声明式校验规则**（非 JS），见下表 |
| `properties` | `type=object` 时的下行字段（同构递归） |
| `items` | `type=array` 时的元素 schema |
| `description` | 备注 |

#### 声明式校验规则（rules）

不在入参上执行任意 JS。规则由引擎解释，发布与 `POST /api/logic/{flowKey}` 入口统一校验；失败返回友好错误（可用 `message` 自定义）。

| type | value | 适用 | 说明 |
|------|-------|------|------|
| `required` | — | 任意 | 可与字段级 `required` 并存 |
| `min` / `max` | 数字 | number | 数值上下界 |
| `minLength` / `maxLength` | 整数 | string（也可用在 array 长度语义外） | 字符串长度 |
| `minItems` / `maxItems` | 整数 | array | 数组项数 |
| `pattern` | 正则字符串 | string | 匹配失败则拒绝 |
| `enum` | 字符串数组 | 任意标量 | 必须落在集合内 |
| `email` / `guid` / `url` | — | string | 格式校验 |
| **`expr`** | 布尔表达式字符串 | 任意 | **条件为真则失败**（等价 if cond return message） |
| **`assert`** | 布尔表达式字符串 | 任意 | **条件为假则失败**（必须成立） |

受控表达式（非任意 JS）支持：`input.xxx` / `sys.xxx`、比较、`&&` `||` `!` `()`，以及 `and`/`or`/`not`。

```json
{
  "name": "age",
  "type": "number",
  "required": true,
  "source": "input",
  "rules": [
    {
      "type": "expr",
      "value": "input.age < 0 || input.age > 100",
      "message": "年龄应该大于0，小于100"
    }
  ]
}
```

等价写法（assert）：`"value": "input.age >= 0 && input.age <= 100"`。

> 不开放任意 JS/`return` 语句。复杂过程式校验放 Code 沙箱节点。

示例：

```json
{
  "name": "amount",
  "type": "number",
  "required": true,
  "source": "input",
  "rules": [
    { "type": "min", "value": 0, "message": "金额不能为负" },
    { "type": "max", "value": 1000000 }
  ]
}
```

跨字段也可写在任一字段的 `expr` 里（如 `input.qty > 0 && input.amount > 0`）。复杂过程式逻辑用 Code 沙箱节点，不写在 InputSchema。

运行时合并顺序：**系统参数 / 日期表达式解析 → 默认值 → 调用方传入覆盖 input 型参数**。  
`source=system` 的参数（含日期表达式）**不允许被调用方覆盖**（防伪造当前用户、防篡改时间窗）。

#### 系统参数怎么用

取值选择器分两类：

| 用法 | 配置 | 示例 |
|------|------|------|
| **固定键** | `systemKey` | `sys.userId`、`sys.MonthStart` |
| **日期表达式** | `systemExpr`（或在选择器里选「日期公式」） | `sys.Now - 3d`、`sys.Today + 1M` |

入参示例：

```json
{
  "name": "fromDate",
  "type": "datetime",
  "source": "system",
  "systemExpr": "sys.Now - 3d",
  "description": "默认取当前时间往前 3 天"
}
```

设计器里所有「取值」控件统一提供五类来源：

1. **字面量**  
2. **请求参数**（本逻辑 `inputs`）  
3. **系统参数（固定键）**  
4. **日期表达式**（基于 `sys.Now` / `sys.Today` 等做加减，**锚点必须带 `sys.` 前缀**）  
5. **上游节点输出**（`results.nodeId.xxx` / `vars.xxx`）

#### 系统参数目录：身份与环境

| 键 | 含义 | 示例 |
|----|------|------|
| `sys.userId` | 当前登录用户 Id | |
| `sys.userName` | 当前用户名 | `admin` |
| `sys.userEmail` | 当前用户邮箱 | |
| `sys.tenantId` | 当前租户 Id（Host 为空） | |
| `sys.tenantName` | 当前租户名 | |
| `sys.culture` | 当前语言 | `zh-Hans` |
| `sys.timeZone` | 租户/用户时区 | `Asia/Shanghai` |
| `sys.correlationId` | 本次请求追踪 Id | |

身份类参数只读、不可表达式覆盖。

#### 系统参数目录：时间锚点（固定键）

全部按 **`sys.timeZone`（默认租户时区）** 计算；同一次运行内快照固定，避免跑到一半 `sys.Now` 跳变。

时间类键与身份类同属 `sys.*`；时间锚点统一 **PascalCase**（`sys.Now`），与 `sys.userId` 等 camelCase 身份键并列，表达式里一律写完整 `sys.Xxx`。

| 键 | 含义 | 示例（假设现在是 2026-09-14 17:05） |
|----|------|------|
| `sys.Now` | 当前时刻（含时分秒） | `2026-09-14T17:05:00+08:00` |
| `sys.Today` | 当天 00:00:00 | `2026-09-14T00:00:00+08:00` |
| `sys.Yesterday` | 昨天 00:00:00 | `2026-09-13T00:00:00+08:00` |
| `sys.Tomorrow` | 明天 00:00:00 | `2026-09-15T00:00:00+08:00` |
| `sys.MonthStart` | 本月 1 日 00:00:00 | `2026-09-01T00:00:00+08:00` |
| `sys.MonthEnd` | **下月 1 日 00:00:00**（半开区间右端，查询用 `<`） | `2026-10-01T00:00:00+08:00` |
| `sys.WeekStart` | 本周一 00:00:00（可配置周起始，默认周一） | `2026-09-14T00:00:00+08:00` |
| `sys.WeekEnd` | 下周一 00:00:00（半开） | `2026-09-21T00:00:00+08:00` |
| `sys.YearStart` | 本年 1 月 1 日 00:00:00 | `2026-01-01T00:00:00+08:00` |
| `sys.YearEnd` | 下年 1 月 1 日 00:00:00（半开） | `2027-01-01T00:00:00+08:00` |

> 区间约定：开始用「含」，结束用「不含」的半开区间 `[start, end)`，避免月底 23:59:59 漏秒。  
> 兼容：旧稿 `sys.now` / `sys.monthStart` 等小写键视为别名，发布校验可 Warning，运行时映射到上表正式键。

#### 日期表达式（相对时间，推荐）

在 `sys.*` 时间锚点上做加减，语法：

```text
sys.<锚点> <+/-> <整数><单位> [ | <边界函数> ]
```

**约定：锚点必须写完整 `sys.Xxx`，禁止裸写 `now` / `today`。** 与 `sys.userId` 同一命名空间，选择器与求值器只认一套前缀。

**锚点**（仅下列正式写法）：

| 写法 | 含义 |
|------|------|
| `sys.Now` | 当前时刻 |
| `sys.Today` | 当天 00:00 |
| `sys.MonthStart` | 本月开始 |
| `sys.YearStart` | 本年开始 |
| `sys.WeekStart` | 本周开始 |
| `sys.Yesterday` / `sys.Tomorrow` | 昨/明 00:00 |

**单位**（大小写不敏感）：

| 单位 | 含义 | 示例 |
|------|------|------|
| `s` | 秒 | `sys.Now - 30s` |
| `m` | 分钟 | `sys.Now - 15m` |
| `h` | 小时 | `sys.Now - 2h` |
| `d` | 天 | **`sys.Now - 3d`**（当前时刻往前 3 天） |
| `w` | 周（7 天） | `sys.Today - 1w` |
| `M` | 月（日历月） | `sys.Today - 1M` |
| `y` | 年（日历年） | `sys.Today - 1y` |

注意：`m` = 分钟，`M` = 月，避免混淆；设计器下拉选单位，减少手误。

**边界函数**（可选，管道写法）：

| 写法 | 含义 |
|------|------|
| `\| startOfDay` | 落到当天 00:00:00 |
| `\| endOfDay` | 落到次日 00:00:00（半开右端） |
| `\| startOfMonth` | 落到当月 1 日 00:00 |
| `\| endOfMonth` | 落到下月 1 日 00:00 |
| `\| startOfYear` | 落到当年 1 月 1 日 |
| `\| endOfYear` | 落到下年 1 月 1 日 |

**常用例子**

| 需求 | 表达式 | 说明 |
|------|--------|------|
| 当前时间前 3 天 | `sys.Now - 3d` | 保留时分秒 |
| 今天往前 3 天的 0 点 | `sys.Today - 3d` | 或 `sys.Now - 3d \| startOfDay` |
| 最近 7 天起始 | `sys.Today - 7d` | 配合 `sys.Today` 作结束 |
| 一小时前 | `sys.Now - 1h` | |
| 下月同一天 | `sys.Today + 1M` | 月末对齐按日历（如 1/31 +1M → 2/28） |
| 上月月初 | `sys.MonthStart - 1M` | 或 `sys.Today - 1M \| startOfMonth` |
| 上月结束（半开） | `sys.MonthStart` | 查询：`>= sys.MonthStart - 1M AND < sys.MonthStart` |
| 本季度初（简化） | `sys.YearStart + N*3M` | N 由实现按月份推算；也可后续加 `sys.QuarterStart` |
| 明年今天 | `sys.Today + 1y` | |

**求值规则**

1. 先取 `sys.*` 锚点瞬时值（同一次运行内不变）。  
2. 再按单位加减；`M`/`y` 用日历语义，不是固定 30/365 天。  
3. 若有 `| startOfXxx`，在加减之后再截断。  
4. 非法语法（缺 `sys.` 前缀、未知单位、非整数）→ 发布校验 Error；运行时不应出现。  
5. 结果类型一律 `datetime`（ISO-8601 + 偏移）。

**设计器交互建议**

```
[ 锚点: sys.Now ▼ ] [ - ▼ ] [ 3 ] [ 天(d) ▼ ]  [ 截断: 无 ▼ ]
预览：2026-09-11T17:05:00+08:00
生成表达式：sys.Now - 3d
```

也可高级模式手写 `systemExpr`，与可视化双向同步；手写时必须带 `sys.`。

#### 在条件 / Sql / Http 中引用

| 场景 | 写法 |
|------|------|
| 进入条件左/右值 | 选择「日期表达式」或填 `sys.Now - 3d` |
| Sql 参数 | `@from` 绑 `sys.Now - 3d`、`@to` 绑 `sys.Today` |
| Http / 节点 input.from | `"from": "sys.Now - 3d"` 或选择器选日期公式 |
| Code 沙箱 | 只读：`sys.Now`、`sys.eval('sys.Now - 3d')`（禁止自己拼时区） |

条件示例：

```
1. input.createdAt  gte  sys.Now - 3d
2. input.createdAt  lt   sys.Today
组合：1 and 2
```

表示「创建时间在最近三天内，且早于今天 0 点」。

### 5.3 发布为系统 API

逻辑发布后，不只是「可被代码 `RunAsync`」，而是成为一条 **系统 API**：

| 项 | 约定 |
|----|------|
| 路径 | `POST /api/logic/{flowKey}`（建议；或 `/api/orchestration/run/{flowKey}`） |
| 方法 | 默认 POST；可在定义上声明 GET（仅查询类、无副作用） |
| 请求体 | 按 Input Schema + **rules** 校验；失败 400 |
| 鉴权 | 沿用 JWT；可额外绑 `Orchestration.Logic.{flowKey}` 或复用现有权限码 |
| 响应 | `{ success, data, error, instanceId }`；`data` 来自 End 节点声明的出参 |
| 文档 | 发布后出现在 Gateway / Scalar 文档（按租户可见性另定） |
| 版本 | 调用始终打到 **当前已发布版本**；旧版本仅实例回放 |

出参（Output Schema）在 End 或逻辑级声明，**每个字段可配角色可见性**（详见 5.6），例如：

```json
{
  "outputs": [
    { "name": "approved", "type": "boolean", "from": "vars.approved", "visibleTo": { "mode": "all" } },
    {
      "name": "riskScore",
      "type": "number",
      "from": "results.code1.score",
      "visibleTo": { "mode": "roles", "roleNames": ["admin", "risk_officer"] },
      "sensitive": true
    }
  ]
}
```

响应中的 `data` **已按调用方角色过滤**；不可见字段不会出现在 JSON 里。

调用示例：

```http
POST /api/logic/order.submit
Authorization: Bearer …
Content-Type: application/json

{ "orderId": "ORD-001", "amount": 1500 }
```

`operatorId`、`queryFrom` 若绑了系统参数，无需调用方传递。

### 5.3.1 复杂 HTTP 演示流程（数据库种子）

演示数据由 **DbMigrator / SaaS `FlowDefinitionDataSeedContributor`** 写入，不靠改前端默认模板。

- 编码 / flowKey：`order-http-demo`
- 状态：已发布（种子会刷新 DSL）
- 链路：Throw → Http POST(`mock://echo` 本地回声，免外网) → Condition → Assign/Log → Assign(lineItems) → **Mask(token)** → **End(finalOutputs)**
- HttpCall 支持 `mock://echo`（即时回声，兼容 httpbin 的 `body.url`）、`mock://status/500`；真外网仍写 `https://...`（国内访问 httpbin 常需 2s+）
- Logic API `meta` 含分段耗时：`lookupMs`（发布定义缓存）/ `executeMs`（解释器）/ `persistMs`（写实例）/ `totalMs`。若总耗时 ~1s 而 `executeMs` 仅数毫秒，瓶颈在鉴权/网关/Mongo 落库，而非 DSL 解析。
- End 演示：`map.item`（list 字段转换）、`aggregate.count`、`tokenMasked`（脱敏 + sensitive）

迁移或重置库后即可调用（Host 租户）。需已登录用户具备权限 **`Orchestration.Instances.Run`**（admin 角色在权限种子后默认拥有）。

**契约约定：**

- 普通节点 `outputs`：仅供下游引用（短名 / `ref.name`）
- **End.`finalOutputs`**：唯一决定 API `data`；根级 `outputs` 为兼容摘要（设计器保存时从 End 同步）
- **Mask**：声明式脱敏，不负责权限；权限用 `visibleTo`

#### List 产出与字段转换（完整示例）

**1）节点产出 list（Assign / Sql / Http）**

```json
{
  "id": "prep_lines",
  "type": "Assign",
  "ref": "prepLines",
  "inputs": [{
    "name": "lineItems",
    "type": "array",
    "from": {
      "literal": [
        { "id": "ORD-1001", "qty": 2 },
        { "id": "ORD-1001-B", "qty": 1 }
      ]
    }
  }],
  "outputs": [
    { "name": "lineItems", "type": "array", "from": "lineItems" }
  ]
}
```

设计器：Assign →「填入 list 示例」；字面量用 **JSON 文本框**（不要用 `String(array)`，否则会显示 `[object Object]`）。

**2）End 上做元素字段转换 + 合计**

```json
{
  "type": "End",
  "finalOutputs": [
    {
      "name": "lineItems",
      "type": "array",
      "from": "lineItems",
      "map": {
        "item": [
          { "name": "sku", "from": "id" },
          { "name": "quantity", "from": "qty" },
          { "name": "label", "from": { "template": "Line {{id}}" } }
        ]
      },
      "visibleTo": { "mode": "all" }
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
```

设计器 End：类型选 `array` / `object` → **map.item** 可视化映射（可无限嵌套）；或点「+ group 示例」/「改为分组」，用表单配置 `by` / header 主字段 / children 明细列（无需手写 JSON）。

#### 嵌套 map（无限层级）

`map.item[]` 中每个字段本身也可再声明 `type` + `map`，形成树状投影，**节点出参与 End.finalOutputs 同一模型**：

| 子字段 type | 有嵌套 `map.item` 时 | 无嵌套 map |
|-------------|----------------------|------------|
| `array` | 对 `from` 解析出的数组**逐行**按子 `map.item` 投影 | 原样透传数组/值 |
| `object` | 对 `from` 解析出的对象按子 `map.item` 投影字段 | 原样透传对象/值 |
| 标量 | 忽略误配的 `map`，透传标量 | 透传 |

设计器：选 `object` / `array` 后展开下一层映射表；每层 `from` **相对当前层**（数组相对元素、对象相对该对象）；仍可用 `input.` / `sys.` / 模板。UI 上限约 12 层（防误配）；引擎侧递归无硬上限。

```json
{
  "name": "orders",
  "type": "array",
  "from": "rows",
  "map": {
    "item": [
      { "name": "id", "type": "string", "from": "orderId" },
      {
        "name": "customer",
        "type": "object",
        "from": "buyer",
        "map": {
          "item": [
            { "name": "name", "from": "name" },
            { "name": "phone", "from": "mobile" }
          ]
        }
      },
      {
        "name": "lines",
        "type": "array",
        "from": "items",
        "map": {
          "item": [
            { "name": "sku", "from": "sku" },
            {
              "name": "attrs",
              "type": "object",
              "from": "meta",
              "map": {
                "item": [
                  { "name": "color", "from": "color" },
                  { "name": "size", "from": "size" }
                ]
              }
            }
          ]
        }
      }
    ]
  }
}
```

语义：`orders[]` → 每行投影 `id` / `customer{name,phone}` / `lines[]` → 每行再投影 `sku` / `attrs{color,size}`。

#### List 路径糖（P1）

对已发布的 list 短名可直接写：

| 路径 | 含义 |
|------|------|
| `rows.count` | 元素个数 |
| `rows.first.order` / `rows.last.order` | 首/末行字段 |
| `rows.0.order` | 下标（与 first 类似） |
| `rows.sum.amount` / `avg` / `min` / `max` | 对字段聚合 |

```json
{ "name": "orderCount", "from": "rows.count" }
{ "name": "maxDate", "from": "rows.max.date" }
```

#### 分组 group（P2）：多主字段 + 子表

扁平行：

```json
[
  { "orderId": "123", "customerName": "张三", "orderDate": "2026-09-01", "amount": 10, "item": "A", "qty": 1 },
  { "orderId": "123", "customerName": "张三", "orderDate": "2026-09-01", "amount": 20, "item": "B", "qty": 2 },
  { "orderId": "456", "customerName": "李四", "orderDate": "2026-09-02", "amount": 5, "item": "C", "qty": 1 }
]
```

End 定义：

```json
{
  "name": "orders",
  "type": "array",
  "from": "rows",
  "group": {
    "by": "orderId",
    "header": [
      { "name": "orderId", "from": "orderId", "take": "first" },
      { "name": "customerName", "from": "customerName", "take": "first" },
      { "name": "orderDate", "from": "orderDate", "take": "first" },
      { "name": "totalAmount", "aggregate": { "op": "sum", "path": "amount" } }
    ],
    "children": {
      "name": "items",
      "map": {
        "item": [
          { "name": "code", "from": "item" },
          { "name": "qty", "from": "qty" }
        ]
      }
    }
  },
  "visibleTo": { "mode": "all" }
}
```

多分组键：`"by": ["orderId", "warehouseId"]`。

结果：

```json
{
  "orders": [
    {
      "orderId": "123",
      "customerName": "张三",
      "orderDate": "2026-09-01",
      "totalAmount": 30,
      "items": [{ "code": "A", "qty": 1 }, { "code": "B", "qty": 2 }]
    },
    {
      "orderId": "456",
      "customerName": "李四",
      "orderDate": "2026-09-02",
      "totalAmount": 5,
      "items": [{ "code": "C", "qty": 1 }]
    }
  ]
}
```

**设计要点：** 订单主属性全部放进 `header`（同组取 `first`，金额类用组内 `aggregate`）；明细只放 `children.map`。不要把主属性塞进 path 糖。

- **默认（列表）**：`data.orders = [{ orderId, customerName, items }]` —— header 已是元素内顶层字段，不是 `order:{}` 再包一层。
- **提升为顶层**（`group.promote: true`，且恰好 1 组）：`data.orderId` / `data.customerName` / `data.items` 直接落在 API 根级。

```json
{
  "name": "orders",
  "type": "array",
  "from": "rows",
  "group": {
    "by": "orderId",
    "promote": true,
    "header": [
      { "name": "orderId", "from": "orderId", "take": "first" },
      { "name": "customerName", "from": "customerName", "take": "first" }
    ],
    "children": {
      "name": "items",
      "map": { "item": [{ "name": "code", "from": "item" }] }
    }
  }
}
```

设计器 End 分组面板勾选「提升为顶层」即可。

运行结果示例（map）：

```json
{
  "lineItems": [
    { "sku": "ORD-1001", "quantity": 2, "label": "Line ORD-1001" },
    { "sku": "ORD-1001-B", "quantity": 1, "label": "Line ORD-1001-B" }
  ],
  "lineCount": 2
}
```

**Postman / RestClient 注意：**

1. `Authorization: Bearer …` 必须是 AuthServer 返回的 **`access_token`**（正常为以 `eyJ` 开头的 JWT）。
2. **不要**把浏览器 Cookie、Data Protection 密文（常以 `CfDJ8` 开头）、refresh_token、或页面里其它字符串当成 Bearer。
3. 网关地址：`https://localhost:7500`（经 Gateway 转发到 SaaS）；也可直连 `https://localhost:7003`。
4. 若仍报 `AbpAuthorizationException`：先确认 token 有效，再在 Identity「角色权限」给 admin 勾选 `Orchestration.Instances.Run`，或重新跑一次 DbMigrator 权限种子。

```bash
# 1) 取 access_token（scope 须含 MetaDowSaaS）
TOKEN=$(curl -sk -X POST "https://localhost:7600/connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=MetaDow_Vue&username=admin&password=1q2w3E*&scope=openid profile email roles MetaDowSaaS MetaDowAdministration MetaDowIdentityService" \
  | jq -r .access_token)

# 2) 经 Gateway 调用
curl -sk -X POST "https://localhost:7500/api/logic/order-http-demo" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "amount": 1500,
    "token": "Bearer demo-token",
    "order": { "id": "ORD-1001", "qty": 2 }
  }'
```

PowerShell（Windows）：

```powershell
$tokenBody = "grant_type=password&client_id=MetaDow_Vue&username=admin&password=1q2w3E*&scope=openid profile email roles MetaDowSaaS MetaDowAdministration MetaDowIdentityService"
$token = (Invoke-RestMethod -Uri "https://localhost:7600/connect/token" -Method Post `
  -ContentType "application/x-www-form-urlencoded" -Body $tokenBody -SkipCertificateCheck).access_token

Invoke-RestMethod -Uri "https://localhost:7500/api/logic/order-http-demo" -Method Post `
  -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -SkipCertificateCheck `
  -Body '{"amount":1500,"token":"Bearer demo-token","order":{"id":"ORD-1001","qty":2}}'
```

若库中尚无该定义：执行 `make migrate` 或 Aspire 的 DbMigrator / Reset Databases。

### 5.4 执行节点：实现类型 + 入参/出参 + 同步/异步

节点分三类：

| 类别 | 例子 | 职责 |
|------|------|------|
| **结构** | Start / End | 入口、最终输出 |
| **条件节点** | Condition | 多条件 + 逻辑运算后选分支（见 5.5） |
| **执行节点** | Http / Code / Sql / Assign / Log / Mask | 真正干活；**显式配置输入与输出** |

执行节点公共属性：

| 属性 | 说明 |
|------|------|
| `kind` / `type` | `Http`（HttpCall）/ `Code` / `Sql` / Assign / Log / Mask … |
| **`async`** | **同步或异步**（见下节；设计器必须可见，不可只藏在 JSON） |
| `timeoutMs` / `retry` | 超时与重试（异步任务同样生效） |
| `inputs` | 本节点输入参数表 |
| `outputs` | 本节点输出参数表（从「原始结果信封」投影） |
| `resultRoot` | （Http/Sql 推荐）原始结果中的业务数据根路径，见 5.4.3 |
| `entry` | 可选进入条件（节点级；与条件节点互补） |

#### 5.4.1 同步 vs 异步（必须在节点上体现）

每个执行节点都有明确的执行模式；**默认同步**。产品与设计器都应把该选项摆在属性面板显眼位置（开关 + 角标），而不是「DSL 有字段、画布上看不出来」。

| `async` | 名称 | 行为 | 下游能否立刻用出参 |
|---------|------|------|-------------------|
| `false`（默认） | **同步** | 跑完本节点 → 写出 outputs → 再走后续边 | **可以** |
| `true` | **异步** | 校验入参后投递后台任务 → 主链立刻继续；任务完成后写 outputs / 记实例 | **不可以**（除非先 Wait） |

```
同步：  … → [Http 同步] ──outputs 就绪──► [Condition] → …
异步：  … → [Http 异步] ──主链不等──► [End 先返回]
                    │
                    └─(后台)→ 写 results / 更新实例节点状态
```

**规则**

1. 主链上若某节点 `async=true`，其后续节点**不得**在未 Wait 时引用该节点出参（发布校验：Warning → 后期 Error）。  
2. 接口返回时机：主链上同步节点都结束后即可返回；异步节点可能仍在跑（实例状态可显示 `Running` / 节点 `Pending`）。  
3. DryRun / 试运行：异步**一律改同步**，保证调试可复现。  
4. 失败策略：同步失败默认可中断主链；异步失败记节点错误与告警，不默认回滚已返回的 API（需业务自行补偿）。  
5. 超时：`timeoutMs` 对同步=阻塞上限；对异步=后台任务上限。

**设计器要求（相对现状）**

| 项 | 要求 |
|----|------|
| 属性面板 | 执行节点顶部提供「执行方式：同步 / 异步」单选或开关 |
| 画布 | 异步节点角标 `异步`（或闪电图标） |
| 校验 | 引用异步出参且无 Wait → 保存/发布提示 |
| 现状 | DSL 已有 `async` 字段；**设计器尚未暴露**；执行器尚未真正投递后台（规划 P1） |

#### 5.4.2 本节点输入参数（Node Inputs）

每个执行节点在跑之前，先把上下文 **映射进本节点局部 `nodeInput`**，再交给 Http/Code/Sql。禁止在实现里到处硬编码 `vars.xxx` 字符串（可选兼容，但面板以映射表为准）。

| 字段 | 说明 |
|------|------|
| `name` | 本节点内参数名，如 `orderId`、`amount` |
| `type` | 类型，与逻辑入参类型集相同 |
| `required` | 映射缺失是否报错 |
| `from` | 来源：`input.xxx` / `sys.xxx` / `vars.xxx` / `results.ref.field` / 短名 / 字面量 / 模板 |
| `transform` | 可选：`trim`、`toString`、`toNumber`、JSONPath 等 |

Http 示例（method/url 建议放节点配置，body 字段走 inputs）：

```json
{
  "type": "HttpCall",
  "ref": "riskApi",
  "async": false,
  "method": "POST",
  "url": "https://api.example.com/risk/check",
  "bodyMode": "json",
  "inputs": [
    { "name": "orderId", "from": "input.order.id" },
    { "name": "amount", "from": "input.amount" },
    { "name": "operatorId", "from": "sys.userId" }
  ]
}
```

#### 5.4.3 原始结果信封 → 业务根 → 出参投影（核心）

执行节点跑完后，引擎手里先有一份 **原始结果信封（raw）**，再按 `outputs` **投影**成下游可用的短名。  
对 Http 尤其重要：上游常见统一包装，不能把整包 JSON 直接当业务出参。

**Http 原始信封（引擎固定形状）**

```json
{
  "statusCode": 200,
  "status": 200,
  "body": { },
  "response": { "status": 200, "body": { } }
}
```

其中 `body` = 响应 JSON（或文本包一层）。若业务返回：

```json
{ "status": 1, "message": "ok", "data": { "level": "high", "score": 90, "items": [1, 2] } }
```

则完整路径是：

| 路径 | 含义 |
|------|------|
| `statusCode` | HTTP 层状态（200/500…） |
| `body.status` | **业务**状态码（1/0…） |
| `body.message` | 业务消息 |
| `body.data` | **业务数据根** |
| `body.data.level` | 业务字段 |
| `body.data.items` | 业务数组 |

**两步配置（设计器应引导，而不是让用户只挖整包）**

```
① 指定「业务数据根」resultRoot（相对 raw，路径任意）
     例：body.data / body.result / body.payload / body（整包业务 JSON）
② 在根之下声明 outputs[]（相对 resultRoot，或仍写信封绝对路径）
     标量：level ← level
     list：lines ← lines，type=array，并可配 map.item 做行投影
③ 信封层字段仍可声明
     例：httpStatus ← statusCode；bizStatus ← body.status
```

**通用性说明（不要写死 data/result）**

Http 业务包装形态各异：`data` / `result` / `payload` / `content` / 甚至 `body` 本身就是业务对象。`resultRoot` 是**任意 JSON 路径**（相对信封），设计器提供常见提示，但允许手写任意嵌套，例如 `body.data.order`、`body.items`。

**list 在 data 里如何 map**

假设响应：

```json
{
  "status": 1,
  "data": {
    "orderId": "ORD-1",
    "buyer": { "name": "张三", "mobile": "13800000000" },
    "lines": [
      { "sku": "A", "qty": 2, "meta": { "color": "red", "size": "M" } },
      { "sku": "B", "qty": 1, "meta": { "color": "blue", "size": "L" } }
    ]
  }
}
```

推荐配置（嵌套已支持；`from` 用点路径或再套一层 `map`）：

```json
{
  "resultRoot": "body.data",
  "outputs": [
    { "name": "orderId", "type": "string", "from": "orderId" },
    { "name": "buyerName", "type": "string", "from": "buyer.name" },
    {
      "name": "buyer",
      "type": "object",
      "from": "buyer",
      "map": {
        "item": [
          { "name": "name", "from": "name" },
          { "name": "phone", "from": "mobile" }
        ]
      }
    },
    {
      "name": "lineItems",
      "type": "array",
      "from": "lines",
      "map": {
        "item": [
          { "name": "code", "from": "sku" },
          { "name": "quantity", "from": "qty" },
          {
            "name": "attrs",
            "type": "object",
            "from": "meta",
            "map": {
              "item": [
                { "name": "color", "from": "color" },
                { "name": "size", "from": "size" }
              ]
            }
          },
          { "name": "label", "from": { "template": "SKU {{sku}}" } }
        ]
      }
    },
    { "name": "bizStatus", "type": "number", "from": "body.status" }
  ]
}
```

语义：

1. `resultRoot=body.data` → 业务根是 data 对象。  
2. 标量嵌套：`from=buyer.name` 点路径直接取。  
3. 对象投影：`type=object` + `map.item` 重命名/挑字段。  
4. `lineItems`：`from=lines` 取数组后按 `map.item` 投影；子字段 `attrs` 再嵌套 object map。  
5. 若不想在 Http 节点 map：只声明 `{ "name": "lines", "type": "array", "from": "lines" }`，再到 **End.finalOutputs** 用 `map` / `group` / **`promote`** 整形。

#### 场景 A：业务根是对象，API `data` 仍是对象（默认）

```json
// End.finalOutputs — 多项 → data 为对象
[
  { "name": "orderId", "from": "orderId" },
  { "name": "lineItems", "type": "array", "from": "lineItems", "map": { "item": [...] } }
]
// → { "success": true, "data": { "orderId": "...", "lineItems": [...] } }
```

#### 场景 B：业务根本身就是数组（`body.data = [...]`）

```json
{
  "resultRoot": "body.data",
  "outputs": [
    {
      "name": "rows",
      "type": "array",
      "from": ".",
      "map": {
        "item": [
          { "name": "id", "from": "id" },
          { "name": "name", "from": "name" }
        ]
      }
    }
  ]
}
```

`from: "."` = 取 `resultRoot` 自身（整段数组）。下游短名 `rows` 已是投影后的 list。

#### 场景 C：希望 API 的 `data` **直接是数组**（不是 `{ lineItems: [...] }`）

End 只配 **一项**，并设 `promote: true`：

```json
{
  "type": "End",
  "finalOutputs": [
    {
      "name": "rows",
      "type": "array",
      "from": "rows",
      "promote": true,
      "map": {
        "item": [
          { "name": "id", "from": "id" },
          { "name": "name", "from": "name" }
        ]
      },
      "visibleTo": { "mode": "all" }
    }
  ]
}
```

响应：

```json
{
  "success": true,
  "instanceId": "...",
  "data": [
    { "id": "A", "name": ".." },
    { "id": "B", "name": ".." }
  ]
}
```

对比：

| 配置 | `data` 形状 |
|------|-------------|
| 多项 / 未 promote | `{ "lineItems": [...], "lineCount": 2 }`（对象） |
| 单项 + `promote: true` + type=array | `[...]`（**直接数组**） |
| 单项 + `promote: true` + type=object | `{ ... }`（直接对象，无字段名外壳） |

设计器：End 出参类型选 array/object → 勾选 **「data 直接为此值」**；此时请只保留这一项。

DSL 完整节点示例：

```json
{
  "type": "HttpCall",
  "ref": "riskApi",
  "async": false,
  "resultRoot": "body.data",
  "outputs": [
    { "name": "httpStatus", "type": "number", "from": "statusCode" },
    { "name": "bizStatus", "type": "number", "from": "body.status" },
    { "name": "level", "type": "string", "from": "level" },
    {
      "name": "items",
      "type": "array",
      "from": "items",
      "map": {
        "item": [
          { "name": "sku", "from": "sku" },
          { "name": "qty", "from": "qty" }
        ]
      }
    }
  ]
}
```

语义（解析顺序）：

1. `from` **先相对 `resultRoot`**；找不到再相对 **raw**；`statusCode` / `body.*` 等信封路径始终相对 raw。  
2. 若出参带 `map.item`：`type=array` 逐行投影，`type=object` 按字段投影；子字段还可再嵌套 `map`（无限层级）。  
3. 写出后下游可用：`level` / `items` / `riskApi.items`。  
4. **未声明 outputs**：整包 raw 仍进 `results[nodeId]`（调试）。  
5. End 仍可用 `map`/`group` 做最终 API 整形；节点 map 与 End map 可二选一或叠加（End 再加工节点已投影的 list）。

**设计器面板建议（Http）**

```
┌─ Http 节点 ─────────────────────────────┐
│ 执行方式  (•) 同步  ( ) 异步              │
│ Method / URL / Headers / Body…           │
│                                          │
│ 响应数据根 resultRoot（可手写任意路径）   │
│   [ body.data          ⌄ ]               │
│                                          │
│ 输出字段 outputs                         │
│   orderId   string  orderId              │
│   lineItems array   lines  + map.item…   │
│   httpStatus number statusCode           │
└──────────────────────────────────────────┘
```

试运行成功后，可用「从上次响应推断」：解析 `body`，列出候选路径，一键填 `resultRoot` 并勾选字段生成 `outputs`（规划中）。

#### 5.4.4 本节点输出参数（Node Outputs）字段表

| 字段 | 说明 |
|------|------|
| `name` | 出参短名，如 `level`、`cnt` |
| `type` | 类型；`array`/`object` 时可配嵌套 `map` |
| `from` | 相对 `resultRoot` 或 raw 的路径；也支持字面量（少用） |
| `map.item[]` | 与 End 同模型；子字段可再选 `object`/`array` 继续展开（无限层级） |
| `to` | 可选；默认写入 `results.{ref}.{name}` 与短名；可兼写 `vars.{name}` |
| `description` | 备注 |

Sql / Code 同理——**不同节点 kind 的 raw 信封不同，`resultRoot` 含义跟着变**：

| kind | raw 信封（示意） | 常用 `resultRoot` | 含义 |
|------|------------------|-------------------|------|
| **Http** | `{ statusCode, body }` | `body` / `body.data` / `body.xxx` | 相对 HTTP 响应 JSON；**不是** Code 的 return |
| **Code** | `{ return: {…}, …顶层展开 }` | **`return`** | 相对脚本**返回值对象**；见下方专节 |
| **Sql** | `{ rows, rowCount }` | `rows` | 相对查询行集 |
| **Assign** | 即 nodeInput 投影 | （空） | 一般不需要 resultRoot |

#### `resultRoot = return` 是什么？（仅 Code 节点）

Code 节点执行后，引擎把脚本结果放进固定信封：

```json
{
  "return": { "approved": true, "score": 90, "message": "ok" },
  "approved": true,
  "score": 90,
  "message": "ok"
}
```

- 键 **`return`**：完整返回值（对象或标量）。  
- 若返回值是对象，字段会**再展开一份到 raw 顶层**（兼容旧写法直接 `from: approved`）。

因此：

| 配置 | 效果 |
|------|------|
| `resultRoot: "return"`，`from: "score"` | 从返回对象取 `score`（推荐、语义清晰） |
| 不设 resultRoot，`from: "score"` | 也能取到（靠顶层展开） |
| `from: "return"` | 取出整个返回值 |
| `from: "return.score"` | 信封绝对路径，不依赖 resultRoot |

**注意：** Http 节点的 `resultRoot` 提示里**不应再混入**「return — Code 返回」。Http 请用 `body` / `body.data` 等；只有 **Code** 节点才应选 `return`。

Code 示例：

```json
{
  "type": "Code",
  "ref": "calc",
  "resultRoot": "return",
  "script": "{ \"approved\": true, \"score\": {{input.amount}} }",
  "outputs": [
    { "name": "approved", "type": "boolean", "from": "approved" },
    { "name": "score", "type": "number", "from": "score" }
  ]
}
```

#### 各 kind 出参 from 速查

| kind | raw 形状（示意） | 推荐 `resultRoot` | `from` 示例 |
|------|------------------|-------------------|-------------|
| Http | `{ statusCode, body }` | `body.data`（或业务实际路径） | `level`、`statusCode`、`body.message` |
| Code | `{ return, … }` | `return` | `approved`、`score` |
| Sql | `{ rows, rowCount }` | `rows` | `0.Cnt`、`rowCount`（或绝对 `rows.0.Cnt`） |
| Assign | nodeInput | （空） | 字段名即 from |

约定：

1. 声明 `outputs` 后，下游选择器**优先展示已声明出参**，降低「挖 JSON」成本。  
2. 发布校验：下游引用的短名 / `ref.field` 必须能被某同步节点（或 Wait 之后的异步节点）出参满足。  
3. **End.finalOutputs** 只引用这些短名做 API 整形；不要把 `body.data.xxx` 直接写进 End（除非调试）。

#### 5.4.5 kind 要点（Http / Code / Sql）

- **Http**：请求用 `method`/`url`/`headers`/`inputs`（body）；响应先定 `resultRoot`，再 `outputs` 投影；可用 `mock://echo` 本地回声。  
- **Code**：沙箱只见 `nodeInput`、`sys`；推荐 `resultRoot=return`，再 `outputs` 投影返回字段。禁止任意 IO。  
- **Sql**：参数化；`inputs`→`@param`；`resultRoot` 常为 `rows`；写库要 `Orchestration.Sql.Write`。

#### 5.4.6 Code 进阶：遍历 / 脚本逻辑 + 选库执行参数化 SQL（规划规格）

当前 MVP 的 Code 多为简单插值。产品目标是：在**受控沙箱**里写循环、拼业务结构，并**可选绑定外部库**，用**参数化 API** 执行 SQL（含批量插入），而不是把用户拼好的整段 SQL 字符串直接丢给数据库。

```
全局登记 DataSource（连接串、只读/可写、租户可见）
        ↓
Code 节点：选 dataSourceId（可空=纯计算）
        ↓
脚本可读：nodeInput / input / sys / 上游短名（只读快照）
脚本可调用：db.query / db.execute / db.batch（仅参数化）
        ↓
return { … } → resultRoot=return → outputs 投影
```

##### 1）全局外部数据库连接（DataSource）

编排服务维护租户级 **数据源白名单**（不是连接器市场，是管理员登记的受控库）：

| 字段 | 说明 |
|------|------|
| `id` / `code` | 稳定标识；Code/Sql 节点引用 |
| `name` | 显示名 |
| `provider` | `postgres` / `mysql` / `sqlserver` / `oracle` / `redis` / `mongodb` |
| `family` | 引擎族：`sql` / `redis` / `mongo`（决定沙箱 API） |
| `connectionSecret` | 密文存库；设计器只见掩码 hint |
| `accessMode` | `read` / `write` / `readWrite` |
| `allowedOps` | 可选细粒度；空则按 accessMode 推导 |
| `enabled` | 停用后节点执行失败 |

管理端：**逻辑编排 → 数据连接**（`/orchestration/data-sources`）；API：`GET/POST /api/orchestration/data-sources`，`GET .../providers`、`.../lookup`。写库另需 `Orchestration.Sql.Write`。

按 `family` 注入的 Code 宿主 API：

| family | 查询 | 单写 | 批量 |
|--------|------|------|------|
| **sql** | `db.query(sql, args)` | `db.execute(sql, args)` | `db.batch(sql, paramList)` |
| **redis** | `db.get(key)` / `db.mget(keys)` | `db.set(key, value, {ttl?})` | `db.mset({k:v,…})`；`db.del(keys)` |
| **mongo** | `db.find(coll, filter, opts?)` / `db.findOne` | `db.insertOne` / `db.updateOne` | `db.insertMany(coll, docs)`；`db.deleteMany` |

**Redis 要点**：key 可用变量拼接；对象值自动 JSON 序列化；`mset` 适合 list→多 key 缓存。

**Mongo 要点**：集合名必须字面量；filter/文档可用变量；批量插入用 `insertMany`，外层字段写入每条 doc。

节点上：

```json
{
  "type": "Code",
  "ref": "persistLines",
  "dataSourceId": "ds_order_pg",
  "resultRoot": "return",
  "inputs": [
    { "name": "orderId", "from": "input.order.id" },
    { "name": "lines", "type": "array", "from": "lineItems" }
  ]
}
```

未选 `dataSourceId`：禁止调用 `db.*`（纯计算 / 转换）。

##### 2）沙箱里能做什么、不能做什么

| 允许 | 禁止 |
|------|------|
| 读 `nodeInput`、只读 `input`/`sys` 快照、上游已声明出参短名 | `fetch` / 任意 Http、读文件系统、启进程 |
| `for` / `map` / `filter` 遍历 list，组装对象 | 动态 `eval`、加载外部 npm、访问未声明全局 |
| 调用宿主注入的 **`db.query` / `db.execute` / `db.batch`** | 字符串拼接后 `db.raw(sql)`、ADO 任意 CommandText |
| `return { inserted, ids }` | 改写 `sys` / 伪造 `userId` |

超时、内存、语句条数、影响行数设上限；DryRun 默认可「不落库 / 事务回滚」。

##### 3）防注入：只允许参数化宿主 API（核心）

**原则：SQL 文本与参数值分离；值永远进 Parameters，不进 SQL 字符串。**

宿主提供（示意，语言可为 JS 沙箱或 .NET 脚本宿主）：

```js
// 单条：sql 仅允许常量字符串字面量（静态分析禁止变量当 sql）
const r = await db.execute(
  `INSERT INTO order_line (order_id, sku, qty) VALUES (@orderId, @sku, @qty)`,
  { orderId: nodeInput.orderId, sku: row.sku, qty: row.qty }
);

// 查询
const rows = await db.query(
  `SELECT id, sku FROM order_line WHERE order_id = @orderId`,
  { orderId: nodeInput.orderId }
);

// 批量：同一 SQL 模板 + 参数对象数组 → 服务端循环/真正 batch，值全部参数化
const batch = await db.batch(
  `INSERT INTO order_line (order_id, sku, qty) VALUES (@orderId, @sku, @qty)`,
  nodeInput.lines.map((x) => ({
    orderId: nodeInput.orderId,
    sku: x.sku,
    qty: x.qty
  }))
);

return { inserted: batch.affectedRows, rows: batch.rows };
```

引擎侧强制：

1. **SQL 必须是静态字符串**（或白名单模板 ID）；拒绝 `db.execute(userSql, …)`、`db.execute("…"+sku, …)`。  
2. 命名参数 `@name` / `:name` 与第二参数对象的 key **一一对应**；多余/缺失参数报错。  
3. **禁止**把 list 序列化进 SQL 文本；批量只能走 `db.batch` 或「表值/UNNEST」类受控扩展。  
4. 标识符（表名/列名）**不允许**来自用户输入；若需动态表，走管理员登记的 **SqlTemplate**（固定文本 + 仅值参数）。  
5. `accessMode=read` 的数据源调用 `execute`/`batch` 写语句 → 拒绝。  
6. 审计：记录 dataSourceId、SQL 指纹（规范化文本 hash）、参数 **形状**（非明文敏感值）、影响行数。

##### 4）list 变量如何变成执行参数

编排里 list 来自 Assign / Http 出参 map / 入参，经 `inputs` 进入 `nodeInput.lines`（JSON 数组）。**不要**写成：

```js
// ❌ 禁止：拼接导致注入与语法灾难
db.execute("INSERT … VALUES ('" + lines.map(x => x.sku).join("','") + "')");
```

**推荐三种模式：**

| 模式 | 适用 | 写法要点 |
|------|------|----------|
| **A. 脚本循环 + `db.execute`** | 行数少、逻辑分支多 | `for (const row of nodeInput.lines) { await db.execute(SQL, { …row 映射 }) }` |
| **B. `db.batch(sql, paramList)`** | 批量插入/更新 | `paramList = lines.map(x => ({ orderId, sku: x.sku, qty: x.qty }))`；服务端按条绑定参数 |
| **C. 纯 Sql 节点** | SQL 固定、无复杂 JS | 不用 Code；`inputs` 绑标量；list 场景仍建议 Code+batch 或专用「表值参数」模板 |

`db.batch` 语义（实现约定）：

```
输入：
  sql   = "INSERT INTO t (a,b) VALUES (@a,@b)"   // 静态
  args  = [ { a:1, b:"x" }, { a:2, b:"y" } ]     // 与 list 等长

执行：
  foreach item in args:
    command.Parameters 清空后按 item 绑定
    ExecuteNonQuery（或驱动支持的真正 batch）

输出：
  { affectedRows, results?: [...] }
```

**循环与外层变量**：沙箱是完整 JS 作用域。`for` / `map` 内可声明临时变量；外层的 `orderId`、`sys.tenantId` 等可直接闭包使用。落库时必须把外层字段**显式写入**每一行参数对象（例如 `{ orderId, sku: row.sku }`），SQL 不会自动带上外层上下文。

数据库宿主 API **仅三种**：`db.query`（读）、`db.execute`（单条写）、`db.batch`（同一 SQL + 参数数组）。没有 `db.raw` / 动态 SQL 字符串。

PostgreSQL 可选扩展（仍参数化）：

```js
await db.execute(
  `INSERT INTO order_line (order_id, sku, qty)
   SELECT @orderId, * FROM UNNEST(@skus::text[], @qtys::int[]) AS u(sku, qty)`,
  {
    orderId: nodeInput.orderId,
    skus: nodeInput.lines.map((x) => x.sku),
    qtys: nodeInput.lines.map((x) => x.qty)
  }
);
```

数组本身作为**一个参数值**绑定，不把元素拼进 SQL。

##### 5）完整示例：遍历 lineItems 写入库

入参 / 上游已有：

```json
{
  "orderId": "ORD-1",
  "lines": [
    { "sku": "A", "qty": 2 },
    { "sku": "B", "qty": 1 }
  ]
}
```

Code 节点（示意脚本）：

```js
const orderId = nodeInput.orderId;
const lines = nodeInput.lines || [];

const paramList = [];
for (const row of lines) {
  if (!row.sku || row.qty <= 0) continue;
  paramList.push({
    orderId: orderId,
    sku: String(row.sku),
    qty: Number(row.qty)
  });
}

const batch = await db.batch(
  `INSERT INTO order_line (order_id, sku, qty) VALUES (@orderId, @sku, @qty)`,
  paramList
);

return {
  inserted: batch.affectedRows,
  skipped: lines.length - paramList.length
};
```

出参：

```json
{
  "resultRoot": "return",
  "outputs": [
    { "name": "inserted", "type": "number", "from": "inserted" },
    { "name": "skipped", "type": "number", "from": "skipped" }
  ]
}
```

##### 6）Code+db 与独立 Sql 节点如何分工

| | **Sql 节点** | **Code + dataSource** |
|--|--------------|------------------------|
| SQL | 面板里写死一条模板 | 脚本里多条 / 分支 / 循环 batch |
| 参数 | `inputs` → `@name` | `db.*` 第二参数对象 / 对象数组 |
| list | 弱（单行或需模板扩展） | **强：遍历 + batch** |
| 适用 | 简单查询、固定写入 | 「算完再写库」、批量落明细 |

两者共用同一套 **DataSource 白名单** 与 `Orchestration.Sql.Write`。

##### 7）权限与发布校验

| 规则 | 说明 |
|------|------|
| 读库 | 节点绑定的 DataSource 对当前租户启用且允许 select |
| 写库 | 调用方或定义发布者具备 `Orchestration.Sql.Write`；DataSource `accessMode` 含 write |
| 静态分析 | 脚本 AST：禁止字符串拼接进 `db.*` 第一参数；禁止未绑定数据源调用 `db` |
| 发布 | 未登记 / 已停用的 `dataSourceId` → Error |

##### 8）与当前实现差距

| 能力 | 现状 | 目标 |
|------|------|------|
| Code | **已落地**：Jint 沙箱（循环/`return`/`db.*`） | 持续收紧超时/内存/静态分析 |
| DataSource | **已落地**：`/api/orchestration/data-sources` CRUD + lookup | 密钥进配置中心（当前 Mongo 存串+API 掩码） |
| Code 选库 | **已落地**：`dataSourceId` + 设计器下拉 | — |
| `db.batch` / 参数化 | **已落地**：`db.query` / `execute` / `batch` + 审计日志 | PG UNNEST 等扩展可选 |
| Sql 节点 | 规划中 | 与 DataSource 共用 |

落地顺序（已完成 ①③④ 核心）：① DataSource 登记与权限 → ② Sql 节点参数化（待） → ③ Code 沙箱只读计算 → ④ Code 注入 `db.query/execute/batch`。

#### 5.4.7 同库统一事务（per DataSource）

##### 现状（问题）

今日 Code/`db.*` 是**每次语句独立开连、自动提交**：

```
CodeA: db.execute #1 → commit
CodeA: db.execute #2 → commit
CodeB: db.execute #3 → commit
Throw / 节点失败 → 流程 Failed，但 #1/#2/#3 已落库，无法回滚
```

仅单次 `db.batch([...])` 内部有显式事务。跨节点、跨多次 `db.execute` **没有**统一事务。

##### 目标语义

| 场景 | 行为 |
|------|------|
| 编排内**同一** SQL DataSource（同一 code）多次写库 | 共用**一条连接 + 一个事务** |
| 业务 Throw / FailWhen / 节点 Failed / 未捕获异常 | **Rollback** 该 DataSource 上本 run 全部未提交写入 |
| 成功到达 End 且流程 Succeeded | **Commit** |
| 编排内使用**多个不同** DataSource | **不要求**分布式事务；各库独立（见下） |
| Redis / Mongo DataSource | **不参与** SQL 事务（无 XA） |
| DryRun | 可开事务，结束时**一律 Rollback**（或完全不连库） |

##### 推荐模型（采纳）

流程定义级开关（默认关闭，兼容现有）：

```json
{
  "key": "order.submit",
  "txMode": "sameDataSource"
}
```

| `txMode` | 含义 |
|----------|------|
| `none`（默认） | 现状：每语句 auto-commit；`db.batch` 仍自带短事务 |
| `sameDataSource` | 按 DataSource **code** 懒开启 `DbConnection`+`DbTransaction`；同 code 的所有 `db.query/execute/batch` 挂到该事务；**不同 code 各自独立事务**（多库不强求一致） |

实现要点：

1. **`FlowDbSessionScope`** 挂在 `FlowRuntimeContext`（按 DS code → session）。  
2. `ParameterizedSqlExecutor`：有 session 则复用 conn/tx；无则保持今日 per-call。  
3. `FlowExecutor`：`Succeeded` → 对各 session `Commit`；`Failed`/异常 → `Rollback`；`finally` Dispose。  
4. 发布校验：`txMode=sameDataSource` 且节点引用了 ≥2 个 SQL DS → **Warning**（多库各自提交，非原子）。  
5. 持锁风险：事务开启后若再跑长时间 **HttpCall**，设计器 **Warning**；可选策略 `txPolicy.forbidExternalCallWhileOpen=true` → 发布 Error。  
6. 只读 `db.query`：默认也走同一连接（可见未提交写入）；可用 `txPolicy.includeSelect=false` 让 SELECT 走短连。

##### 生命周期

```
Start flow (txMode=sameDataSource)
  ├─ Code(ds=orderDb) db.execute → lazy BEGIN
  ├─ Code(ds=orderDb) db.execute → 同一 tx
  ├─ Code(ds=logDb)  db.execute → 另一 tx（独立）
  ├─ Throw / Fail     → orderDb ROLLBACK；logDb ROLLBACK
  └─ End success      → orderDb COMMIT；logDb COMMIT
```

##### 其它方案对比（为何不选）

| 方案 | 优点 | 缺点 | 结论 |
|------|------|------|------|
| **A. 同 DS 统一事务（上）** | 语义清晰、实现可控 | 不能跨库；长事务忌 Http | **主推** |
| B. 仅加强 `db.batch` | 改动小 | 无法跨 Code 节点；编排者易漏 | 保留作局部手段 |
| C. XA / 两阶段提交 | 真·跨库原子 | 运维重、Redis/Mongo 难、Aspire 不匹配 | **不做** |
| D. Saga / 补偿节点 | 适合跨服务 | 要写补偿逻辑，编排复杂 | P2 可选 |
| E. 事务性 Outbox | 消息与库一致 | 需本地 outbox 表 + 投递器 | 与 **RabbitMQ** 组合时推荐 |

**产品口径**：同库要原子 → 开 `txMode=sameDataSource` 且节点绑同一 DataSource；多库 → 接受最终一致，或拆流程 / 用补偿。

#### 5.4.8 消息发布节点（RabbitMqPublish，优先本地 RabbitMQ）

##### 产品口径（已定 / 已落地）

**优先使用 Aspire 已集成的本地 RabbitMQ**。节点类型：`RabbitMqPublish`。  
默认 **fanout 广播**：交换机 `Meta.Dow.Orchestration.Broadcast`，所有绑定该交换机的队列都会收到同一份 JSON。与内部 EventBus 交换机 `Meta.Dow` **隔离**。

##### 外部如何订阅广播

1. 连本机 Rabbit（Aspire 映射的 AMQP 端口，管理台通常 `15672`）  
2. 声明自己的 Queue（任意名）  
3. Bind：`exchange=Meta.Dow.Orchestration.Broadcast`，`routingKey` 可空（fanout 忽略）  
4. Consume：消息 body 为 UTF-8 JSON  

也可用管理台 Exchanges → `Meta.Dow.Orchestration.Broadcast` → Bindings 绑定测试队列。

##### 需求

- 经**本地 RabbitMQ** 发布消息（复用 AppHost 连接串 `rabbitmq`）  
- **默认广播（fanout）**；也可切 `topic` / `direct` 并配置路由键模板  
- 将本节点 `payload.fields` 拼成 **JSON** 发出（不进全局 vars）  
- 下游仅回执：`published` / `exchange` / `routingKey`  

##### 节点契约示例

```json
{
  "type": "RabbitMqPublish",
  "ref": "mqBroadcast",
  "exchange": "Meta.Dow.Orchestration.Broadcast",
  "exchangeType": "fanout",
  "persistent": true,
  "onError": "fail",
  "payloadMode": "object",
  "payload": {
    "item": [
      { "name": "orderId", "from": "orderId" },
      { "name": "amount", "from": "amount" }
    ]
  },
  "inputs": [
    { "name": "orderId", "from": "input.order.id" },
    { "name": "amount", "from": "input.amount" }
  ],
  "outputs": [
    { "name": "published", "from": "published" },
    { "name": "exchange", "from": "exchange" }
  ]
}
```

试运行（DryRun）不真实发消息，回执 `published=false, dryRun=true`。

##### 推荐节点契约（设计说明）

原规划保留：`payload.fields` 局部组装；`onError=fail|ignore`。实现方式为 **RabbitMQ.Client 直发**（非 ABP ETO）。

##### 为何载荷不是「全局输出变量」

| 做法 | 问题 |
|------|------|
| 先 Assign 一堆 `vars.msg.*` 再发 | 污染上下文 |
| **`payload.fields` 局部组装（已采用）** | 契约在节点内闭合 |

##### 与统一事务的顺序

仍见 §5.4.7；消息节点失败且 `onError=fail` 时流程 Failed（后续同库事务落地后可联动 Rollback）。

##### 方案对比（当前优先级）

| 方案 | 优先级 |
|------|--------|
| **RabbitMqPublish fanout 广播** | **已落地 P0** |
| topic / direct | 同节点可切换 |
| Outbox | P2 |
| MqttPublish | P2 非默认 |

##### 设计器

左侧拖「Rabbit广播」→ 配交换机 / fanout / 消息体 map / 失败策略。

#### 5.4.9 SubFlow（逻辑组件调用）

##### 产品口径（已落地 MVP）

- **逻辑组件** = 勾选 `isReusable` 且 **已发布** 的流程定义（`code` = flowKey）。
- 画布节点类型：`SubFlow`（兼容别名 `LogicComponent` / `Component`）。
- 同步调用：本节点 `inputs` → 子流程请求体；子流程跑完 End 出参装入信封 `data`。
- 默认 `resultRoot=data`；出参 `from` 相对子流程业务结果；信封字段 `success` / `subFlowKey` / `subFlowVersion` / `executeMs` 仍可从 raw 根取。
- **嵌套上限** 5 层；**禁止循环**（调用栈含同一 flowKey）。
- `onError`：`fail`（默认）中断父流程；`ignore` 继续并把 `success=false` 写出。
- 试运行（DryRun）会递归执行子流程（Http 等仍按 DryRun 规则跳过）。
- **不单独落子实例**：子节点执行明细不写入父 `FlowInstance.Nodes`（仅本 SubFlow 节点一条记录）。

##### 引用索引（FlowUsage）

保存草稿 / 发布时扫描 DSL 中的 SubFlow，写入 `FlowUsage`：

| 字段 | 说明 |
|------|------|
| CallerDefinitionId / CallerCode | 谁在调用 |
| CalleeFlowKey | 被调 flowKey |
| NodeId / NodeRef | 画布节点 |

API：

- `GET /api/orchestration/definitions/reusable-lookup` — 设计器选用列表  
- `GET /api/orchestration/definitions/usages/by-callee?flowKey=` — 谁在用该组件  
- `GET /api/orchestration/definitions/{id}/usages` — 该定义调用了谁  

前端：「逻辑组件」菜单页 + 设计器 SubFlow 下拉。

##### 节点契约示例

```json
{
  "type": "SubFlow",
  "ref": "sub1",
  "subFlowKey": "pricing-calc",
  "onError": "fail",
  "resultRoot": "data",
  "inputs": [
    { "name": "amount", "from": "input.amount" }
  ],
  "outputs": [
    { "name": "level", "from": "level" },
    { "name": "ok", "from": "success" }
  ]
}
```

#### 5.4.10 触发层：消息订阅 / 定时（不扩展画布）

编排只负责「业务怎么跑」。**何时启动**由触发层完成：订阅器 / 定时任务直接按 **已发布 `flowKey`（编码）** 调用，与 `POST /api/logic/{flowKey}` 同源（`PublishedFlowInvoker`）。

| 概念 | 类比 | 说明 |
|------|------|------|
| MessageSource | DataSource | 登记 Rabbit/Kafka/MQTT 连接；Rabbit 连接串留空 = 平台 `rabbitmq` |
| FlowTrigger | — | 队列 + 交换机绑定 → `flowKey`；消息 JSON = 流程入参 |
| FlowSchedule | — | Cron + 固定 VariablesJson → `flowKey` |

**已落地：** Rabbit / **Kafka** / **MQTT** 消费 HostedService（约 20s 热更新监听）；Cronos 进程内轮询定时；管理页「消息连接 / 消息触发 / 定时任务」。

字段约定（`FlowTrigger`）：

| 协议 | Queue 字段 | RoutingKey 字段 | 连接串 |
|------|------------|-----------------|--------|
| Rabbit | 队列名 | topic/direct 路由键 | 可空→平台 rabbitmq |
| Kafka | Topic | Consumer Group（空=`orch-{code}`） | **必填** bootstrap |
| MQTT | Topic（支持 +/#） | QoS 0/1/2（默认 1） | **必填** tcp:// 或 Host= |

**刻意不做：** 画布 Kafka/MQTT/Cron 节点；触发器内嵌业务 DSL。

### 5.5 条件节点：多条件 + 逻辑运算

**条件节点（Condition）** 是独立结构节点，专门做分支；与「执行节点上的进入条件」是两层能力：

| | 条件节点 Condition | 执行节点 entry |
|--|-------------------|----------------|
| 位置 | 画布上的菱形/分支节点 | 挂在 Http/Code/Sql 上 |
| 作用 | 求值后走 **多条出边之一** | 决定本节点 **执行还是跳过** |
| 条件模型 | 相同：编号列表 + 组合式 | 相同 |
| 典型用法 | `if/else if/else`、多路分流 | 「金额为空则不调通知」 |

#### 条件列表

| 字段 | 说明 |
|------|------|
| `no` | 编号，从 1 开始 |
| `left` | `input` / `sys` / `vars` / `results.*.*` / 日期表达式 |
| `op` | `eq` `ne` `gt` `gte` `lt` `lte` `contains` `notContains` `in` `notIn` `isEmpty` `isNotEmpty` `between` |
| `right` | 字面量或同样来源；`between` 时为 `[min,max]` |

```
1. input.amount      gt     1000
2. sys.userName       eq     admin
3. vars.orderCount   gt     0
4. input.createdAt   gte    sys.Now - 3d
```

#### 组合式（逻辑运算）

只允许编号与 `and` / `or` / `not` / 括号：

```
1 and (2 or 3)
not 4 or (1 and 2)
```

设计器：条件表 + 组合输入框 + 模板按钮（全 and / 全 or / `1 and (2 or 3)`）+ 非法编号标红。

#### 出边策略

条件节点可有 **N 条出边**，每条边自带一套 `items + combine`（或共享条件表、边只写组合式）。

| 策略 | 说明 |
|------|------|
| `firstMatch`（固定默认） | 按出边顺序取第一条组合式为真的边；皆假走 else |

```
Condition
  ├─ 边A: 1 and 2        → 管理员超限分支
  ├─ 边B: 1 and not 2    → 普通用户超限分支
  └─ 边C: default        → 未超限 / End
```

#### 执行节点上的进入条件

仍可用同一套模型；组合为空 = 恒真。条件为假时标记 `Skipped`，不执行、不写 outputs。

### 5.6 最终输出与角色可见性

逻辑级最终出参在 **End.`finalOutputs`**（兼容根级 `outputs`）决定 API 响应里的 `data`。

#### 出参字段定义

| 字段 | 说明 |
|------|------|
| `name` | 响应字段名；`promote=true` 时仅作契约/日志标识，不包进 data |
| `type` | 类型；`array`/`object` 时可配 `map` / `promote` |
| `from` | 来源短名 / `ref.field` / `input.x`；嵌套可用点路径如 `buyer.name` |
| `visibleTo` | 字段可见性：`all` / `roles` / `permissions` / `none`（无权限则**省略键**） |
| `sensitive` | 敏感标记（审计日志提示；不代替权限） |
| `map.item[]` | list/object 字段映射；`from` 相对当前层，或 `input.`/`sys.` 全局，或 `{ "template": "…" }`；**子项可再带 `type`+`map` 无限嵌套**（见上文「嵌套 map」） |
| `promote` | `true` 时 **API `data` 直接等于本字段值**（须为唯一出参）；得到 `data: [...]` / `data: {...}` |
| `aggregate` | `{ op: count\|sum\|avg\|min\|max\|first\|last, path? }`，对 `from` 指向的 array 聚合 |
| `wrap` | `array`：单对象包成单元素数组 |

#### Mask 脱敏节点

```json
{
  "type": "Mask",
  "ref": "maskToken",
  "maskStrategy": "rules",
  "inputs": [{ "name": "token", "from": "input.token" }],
  "maskRules": [
    { "field": "token", "op": "mask", "keepStart": 7, "keepEnd": 0 }
  ],
  "outputs": [{ "name": "tokenMasked", "from": "token" }]
}
```

`op`：`fixed` / `keepEmpty` / `phone` / `idCard` / `email` / `bankCard` / `name` / `mask` / `hash` / `redact` / `drop`。  
`maskStrategy: items` + `itemField` 可对 list 逐行打码。Mask **不管授权**；谁能看字段仍由 End.`visibleTo` 决定。

#### `visibleTo` 模式

| mode | 含义 |
|------|------|
| `all` | 凡能调用该逻辑 API 的人都能看到该字段 |
| `roles` | 仅列出的角色可见 |
| `permissions` | 持有列出权限码者可见（运行时 `IPermissionChecker`） |
| `none` | 永不进响应 |

#### 运行时过滤

```
到达 End → 读 finalOutputs（否则根 outputs）
  → map / aggregate 组装完整候选 data
  → 按 visibleTo（含 list 内字段）省略不可见键
  → 返回 { success, data, meta.visibleFields, meta.omittedFieldCount }
```

规则：

1. **调用权限**与**字段可见性**分离。  
2. 无权限字段整键省略（不用 `null` 占位）。  
3. 试运行可 bypass 角色过滤以便预览；正式 `/api/logic` 按调用方过滤。  
4. 复杂派生用中间 Code 节点；**禁止**在 End 挂自由 JS。  

#### 设计器交互

- **End**：最终出参表（可见性 / 敏感 / aggregate / **递归 map.item** / **promote→data 直接为数组**）  
- **节点 outputs**：`array`/`object` 同样用递归 map 编辑器  
- **Mask**：策略 + 规则表 + I/O  

角色选择器对接 Identity；权限码手填或后续接权限目录。

### 5.7 上下文分层（执行时）

| 命名空间 | 内容 | 谁能改 |
|----------|------|--------|
| `input` | 逻辑请求参数 | 调用方；system 型只读 |
| `sys` | 系统参数 / 日期表达式快照 | 只读 |
| `nodeInput` | 当前执行节点映射后的局部入参 | 执行器写入，节点只读 |
| `vars` | 跨节点共享 | 节点 outputs / Code 写入 |
| `results[nodeId]` | 节点原始或声明出参 | 该节点执行后写入 |

选择器展示：`input.amount`、`sys.userId`、`sys.Now - 3d`、`vars.approved`、`results.sql1.cnt`。

### 5.8 强大逻辑编排引擎：能力全景

在以上规格之上，引擎长期目标如下（实现按阶段推进，但产品叙事以此为准）。

```
┌─────────────────────────────────────────────────────────────┐
│                     Meta.Dow Logic Engine                    │
├──────────────┬──────────────┬──────────────┬────────────────┤
│ 契约层        │ 编排层        │ 执行层        │ 安全与可见性    │
│ InputSchema  │ X6 画布       │ Http/Code/Sql│ 调用鉴权        │
│ OutputSchema │ 条件节点      │ 同步/异步     │ 字段角色可见    │
│ 系统参数/日期 │ 执行节点I/O  │ 重试/超时     │ Sql白名单/沙箱  │
│ 发布为 API   │ 多条件逻辑式  │ 实例时间线    │ 敏感字段脱敏    │
└──────────────┴──────────────┴──────────────┴────────────────┘
```

| 层级 | 能力 | 价值 |
|------|------|------|
| 契约 | 入参/出参强类型、默认值、系统参数 | 逻辑即 API，契约清晰 |
| 分支 | 条件节点多条件 + `1 and (2 or 3)` | 业务规则可视化、可维护 |
| 节点 I/O | 每节点显式入参/出参映射 | 可组合、可校验、可文档化 |
| 执行 | Http/Code/Sql + 异步 | 覆盖集成、计算、查库 |
| 可见性 | 最终字段按角色过滤 | 同一 API，不同角色不同视图 |
| 运维 | 版本、试运行、实例日志、跳过原因 | 可观测、可回放 |
| 扩展 | Wait、WHEN 并行、SubFlow、组件目录 | 向「强大引擎」演进 |

**引擎原则（不可破）**

1. **契约优先**：无 InputSchema 不可发布。  
2. **数据显式流**：节点之间靠出参/入参映射，少魔法全局变量。  
3. **条件可组合**：禁止只靠一整段不可维护脚本做分支（Code 节点除外且沙箱）。  
4. **最小可见**：响应默认只含调用方有权字段。  
5. **安全默认**：Sql/Code 默认受限；写库、敏感出参显式授权。

### 5.9 和现有 MVP 的对应关系

| 产品规格 | 当前实现 | 差距 |
|----------|----------|------|
| Input Schema | 定义级入参表 + 试运行 JSON | 规则校验已有；表单化持续打磨 |
| 系统参数 / 日期公式 | `sys.*` + 日期表达式 | 已有 |
| 条件节点多条件逻辑 | Condition + 编号组合式 | 已有 |
| 执行节点入参/出参表 | Http/Assign/Log/Mask 等已映射 | 面板持续打磨 |
| **节点同步/异步** | DSL + 设计器开关/角标；Http 异步主链排队（`AsyncQueued`） | Wait / 实例回写出参仍为 P2 |
| **Http `resultRoot` + 出参投影** | 已实现：先相对 `resultRoot`，信封路径回退 raw | 试运行「从响应推断」可后续补 |
| 最终出参角色可见 | End.finalOutputs + visibleTo | 已有；group/promote 已有 |
| 发布为系统 API | `/api/logic/{flowKey}` + 字段过滤 | 已有 |
| Http / Code / Sql | HttpCall + Code；Sql 规划中 | Sql P1 |

---

## 6. 如何进行编排（产品操作说明）

本章说明如何用本工具 **编排一条可发布为 API 的逻辑**。

### 6.1 端到端主路径（必会）

```
① 新建逻辑，定义请求参数（可绑系统参数 / `sys.Now - 3d`）
→ ② 拖条件节点：多条件 + 1 and (2 or 3)，拉出多条分支边
→ ③ 拖执行节点（Http/Code/Sql）：配 **同步/异步**、入参映射、**响应数据根 resultRoot**、出参投影
→ ④ 在 End 配置最终输出字段 + 哪些角色可见
→ ⑤ 保存 → ⑥ 试运行（可切换模拟角色看字段差异）
→ ⑦ 发布为系统 API
→ ⑧ 调用；按角色拿到可见字段
→ ⑨ 看实例日志（含条件命中、节点跳过、出参）
```

#### ① 新建并定义请求参数（先于画布）

1. 进入 **逻辑编排 → 流程定义 → 新建**。  
2. 填写名称、编码 `flowKey`。  
3. **先维护请求参数表**（传入 / 系统参数 / 日期表达式）。  
4. 可先草拟最终出参与角色可见性，画完节点后再精确绑 `from`。  
5. 进入设计器。

#### ② 认识设计器三栏

| 区域 | 作用 |
|------|------|
| 左：节点库 | **结构** Start/End；**条件** Condition；**执行** Http/Code/Sql |
| 中：画布 | 顺序与分支 |
| 右：属性面板 | 条件表/组合式；节点入参/出参；异步；最终可见角色 |
| 顶：工具条 | 保存、试运行、发布为 API |

#### ③ 配置要点

1. **条件节点**：条件 1、2、3… + 组合式；每条出边绑定组合或默认 else。  
2. **执行节点**：选 **同步/异步** → 填输入映射 →（Http）指定 **`resultRoot`（如 `body.data`）** → 声明相对根的 **outputs**。  
3. **End**：最终字段 ← 节点出参短名；`visibleTo` / `map` / `group`。  

#### ④～⑨ 保存到观测

试运行支持「以角色 admin / user 预览响应字段」。发布后 `/api/logic/{flowKey}` 自动按调用方角色过滤 `data`。

### 6.2 典型场景

#### 场景 A：条件分支 + 节点 I/O + 角色可见

1. 入参：`amount`、`queryFrom=sys.Now - 3d`、`operatorId=sys.userId`  
2. Http/Sql 节点：**同步**；`resultRoot=body.data`（或 Sql 的 `rows`）；outputs 写出 `cnt` / `level`  
3. Condition：`1` cnt>0；`2` amount>1000；组合 `1 and 2` → 超限边 / 默认边  
4. Code 节点：算 `riskScore` 写出  
5. Http 通知（仅超限边，可标 **异步**，End 不依赖其出参）  
6. End 出参：`approved` 全员可见；`riskScore` 仅 `admin`、`risk_officer` 可见  

#### 场景 B：异步写库，接口先返回

Start → Code（校验，**同步**，出参 ok）→ Sql（写审计，**异步**）→ End（`accepted` 全员可见；不读 Sql 出参）

#### 场景 C：Http 统一包装取 data

上游返回 `{ "status": 1, "data": { "orderId": "…", "lines": [] } }`：

1. Http **同步**，`resultRoot = body.data`  
2. outputs：`orderId ← orderId`，`lines ← lines`，另可 `bizStatus ← body.status`（信封层）  
3. End：`lines` 用 `map`/`group` 整形；主属性用短名 `orderId`

### 6.3 连线与进入规则

| 规则 | 说明 |
|------|------|
| 单 Start | 必须 |
| 条件节点 | 多出边 + firstMatch/default |
| 执行节点 entry | 可选；假则 Skipped |
| 节点 I/O | 下游只引用上游已声明 outputs（推荐） |
| 异步依赖 | 禁止未 Wait 就读异步出参 |

### 6.4 角色分工

| 角色 | 典型操作 |
|------|----------|
| 逻辑编排者 | 入参、条件、节点 I/O、出参可见性、发布 |
| 业务研发 | 调用 API；按角色消费字段 |
| 安全/管理员 | 配置角色、Sql 写权限、敏感字段 |
| 运维 | 实例日志、试运行 |

---

## 7. 设计器交互规格（UX）

对照 [liteflow-editor-client](https://github.com/Cooooooler/liteflow-editor-client)（X6 画 EL）、宜搭逻辑编排、微搭逻辑流、X6 官方 flowchart，设计器应满足下列规格。

### 7.1 布局

```
┌──────────────────────────────────────────────────────────────┐
│ 返回 │ 流程名 │ 状态Tag │ vN │ 未保存 │  保存 试运行 发布为API │
├──────────────────────────────────────────────────────────────┤
│ 撤销 重做 │ 缩放% │ 适应 │ 框选 │ 复制 删除 │ 导出 │ 小地图 │ 快捷键 │
├──────────┬───────────────────────────────────┬───────────────┤
│ 节点库    │            X6 画布                 │ 属性面板      │
│ · 条件    │  网格 · 对齐线                     │ · 条件多条件  │
│ · HTTP    │  小地图                            │   +逻辑组合  │
│ · 代码块  │  右键菜单                          │ · 节点入参表  │
│ · SQL     │                                    │ · 节点出参表  │
│           │                                    │ · 异步       │
│           │                                    │ · 最终可见角色│
└──────────┴───────────────────────────────────┴───────────────┘
```

### 7.2 节点外观（产品态，不是默认几何形）

| 类型 | 视觉 | 端口 |
|------|------|------|
| Start / End | 圆形，语义色（绿/灰） | 四向；推荐 Start 只出、End 只入 |
| Condition | 菱形；角标显示组合式摘要 | 一入多出 |
| 执行节点（Http / Code / Sql） | 卡片；显示入参/出参个数；异步角标 | 四向 |

### 7.3 画布交互清单

| 交互 | 规格 | 参考 |
|------|------|------|
| 拖入节点 | 结构节点与领域组件从左侧拖入 | liteflow-editor / X6 Dnd |
| 连线 | 输出磁铁 → 输入磁铁；禁止自环 | X6 connecting |
| 选中 | 单击；框选可多选 | Selection |
| 属性同步 | 右侧改参即时回写 | 宜搭属性区 |
| 右键菜单 | 节点编辑/复制/删除；边 true/false | 自研 overlay |
| 撤销重做 | Ctrl+Z / Y | X6 History |
| EL 预览（P1） | 侧栏显示 THEN/IF… 文本 | liteflow-editor |
| 校验反馈 | 发布前标红非法结构 | LiteFlow 图归并约束 |

### 7.4 属性面板字段规格

逻辑定义页：**请求参数表**、**最终出参表（含角色可见）**。

**条件节点 Condition**

- 条件表格：编号、左值、运算符、右值  
- 组合式：`1 and (2 or 3)`  
- 出边列表：每边绑定组合式或「默认 else」；策略 firstMatch  

**执行节点通用**

- kind、async、timeout  
- **输入参数表**：name / type / from 选择器  
- **输出参数表**：name / type / from(原始) / to(vars|results)  
- 可选 entry 进入条件（同条件模型）  

**Http / Code / Sql**：在通用 I/O 之上的实现细节（URL、脚本、数据源+SQL）。

**End / 最终输出**

- 字段名、类型、from、**visibleTo（全部 / 角色多选）**、sensitive  

### 7.5 快捷键

| 键位 | 行为 |
|------|------|
| Ctrl+Z / Ctrl+Y | 撤销 / 重做 |
| Ctrl+C / Ctrl+V | 复制 / 粘贴节点 |
| Delete / Backspace | 删除选中 |
| Ctrl+滚轮 | 缩放 |
| 双击节点 | 展开属性面板 |
| 右键 | 上下文菜单 |

---

## 8. 目标架构（对齐现有微服务）

```
┌─────────────────────────────────────────────────────────────┐
│  Vue Vben（web-antd）                                        │
│  · 逻辑编排设计器（AntV X6）                                │
│  · 流程定义列表 / 版本 / 发布                                │
│  · 执行实例 / 日志 / 试运行                                  │
│  · 权限：Orchestration.*（菜单 Permission 绑定）             │
└───────────────────────────┬─────────────────────────────────┘
                            │ Gateway YARP
        ┌───────────────────┼───────────────────┐
        ▼                   ▼                   ▼
┌──────────────┐  ┌─────────────────┐  ┌──────────────────┐
│ Identity     │  │ Administration  │  │ SaaS ★ MVP       │
│ 用户/角色/OU │  │ 菜单/权限/设置  │  │ /api/orchestration│
└──────────────┘  └─────────────────┘  │ 定义·版本·执行   │
                                       └────────┬─────────┘
                                                │ 调用
                         ┌──────────────────────┼──────────────┐
                         ▼                      ▼              ▼
                   Identity API            Projects API     外部 HTTP
                   / 发邮件等              / 业务动作       / Webhook
```

| 能力 | 服务 | 说明 |
|------|------|------|
| 流程定义 / 版本 / 发布 | **`Meta.Dow.SaaS`（MVP）** | MongoDB；含 Input/Output Schema、节点 I/O |
| 执行实例 / 节点日志 | 同上 | DSL 执行器：Condition 组合、映射、异步 |
| 系统逻辑 API | 同上 | `POST /api/logic/{flowKey}`；出参按角色过滤 |
| 角色列表（可见性配置） | Identity | 编排器拉取角色；运行时用 JWT 角色过滤 |
| 节点「调用内部 API」 | SaaS 经 HttpClient 出站 | DryRun 可跳过 Http |
| 菜单与授权 | 沿用 IAM | `Orchestration.*`；调用权 ≠ 字段可见 |
| Gateway | YARP | `/api/orchestration/*`、`/api/logic/*` → SaaS |

> MVP 先放在 SaaS；执行/重试/定时膨胀后再拆独立 Orchestration 服务（可对照 Elsa Server 独立部署）。

---

## 9. 领域模型与 DSL 规范

### 9.1 定义侧（Design-time）

| 实体 | 要点 |
|------|------|
| `FlowDefinition` | 名称、`flowKey`、分类、租户、状态；**InputSchema / OutputSchema（含 visibleTo）** |
| `FlowVersion` | 不可变版本号、graphJson、dslJson、发布人/时间；对外 API 指向当前版本 |
| `InputParameter` | name、type、required、default、source、systemKey/systemExpr、**rules[]**、properties/items |
| `OutputParameter` | name、type、from、**visibleTo**、sensitive |
| `SystemParameter` | 内置 `sys.*` + **日期表达式求值器**（s/m/h/d/w/M/y，可选 startOf/endOf） |
| `DataSource` | Sql 节点连接白名单（只读/写） |
| `ConditionNode` | 条件表 `items[]` + 出边各自 `combine` / `default`；策略 firstMatch |
| `LogicNode` | kind=Http/Code/Sql、**async（同步/异步）**、timeout、**resultRoot**、**inputs[] / outputs[]**、可选 entry |
| `NodeBinding` | 单条入参/出参映射：name、type、from、to、transform |
| `FlowEdge` | source/target；条件边带 `combine` 或 `isDefault` |

### 9.2 运行侧（Run-time）

| 实体 | 要点 |
|------|------|
| `FlowInstance` | 版本、调用方、角色快照、`input`/`sys`、完整 `data`（未过滤）、状态 |
| `NodeExecution` | entry/分支求值、Skipped/Executed、async JobId、nodeInput 快照、声明出参、错误 |
| 上下文 | `input` + `sys` + `nodeInput` + `vars` + `results` |
| 响应过滤 | 按调用方角色/权限裁剪 `data`；`meta.visibleFields` 记录实际返回字段 |

### 9.3 Executable DSL（后端只信这份）

存储：

1. **`graphJson`**：X6 回显  
2. **`dslJson`**：执行图（契约、条件节点、节点 I/O、出参可见性）  
3. **`elText`（可选）**：LiteFlow 风格对照文本  

```json
{
  "version": "1.1",
  "key": "order.risk-check",
  "inputs": [
    { "name": "amount", "type": "number", "required": true, "source": "input" },
    { "name": "orderId", "type": "string", "required": true, "source": "input" },
    { "name": "queryFrom", "type": "datetime", "source": "system", "systemExpr": "sys.Now - 3d" },
    { "name": "operatorId", "type": "guid", "source": "system", "systemKey": "sys.userId" }
  ],
  "outputs": [
    {
      "name": "approved",
      "type": "boolean",
      "from": "vars.approved",
      "visibleTo": { "mode": "all" }
    },
    {
      "name": "riskScore",
      "type": "number",
      "from": "results.code1.score",
      "visibleTo": { "mode": "roles", "roleNames": ["admin", "risk_officer"] },
      "sensitive": true
    },
    {
      "name": "message",
      "type": "string",
      "from": "vars.message",
      "visibleTo": { "mode": "all" }
    }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "sql1",
      "type": "Logic",
      "kind": "Sql",
      "async": false,
      "dataSource": "MetaDowProjectsDb",
      "sql": "SELECT COUNT(1) AS Cnt FROM Orders WHERE TenantId=@TenantId AND CreationTime>=@queryFrom AND Amount>@amount",
      "inputs": [
        { "name": "queryFrom", "type": "datetime", "from": "input.queryFrom" },
        { "name": "amount", "type": "number", "from": "input.amount" }
      ],
      "outputs": [
        { "name": "cnt", "type": "number", "from": "rows[0].Cnt", "to": "vars.orderCount" }
      ],
      "entry": {
        "items": [{ "no": 1, "left": "input.amount", "op": "gt", "right": 0 }],
        "combine": "1"
      }
    },
    {
      "id": "code1",
      "type": "Logic",
      "kind": "Code",
      "async": false,
      "script": "return { approved: nodeInput.cnt > 0, score: nodeInput.cnt * 10, message: 'ok' };",
      "inputs": [
        { "name": "cnt", "type": "number", "from": "vars.orderCount" }
      ],
      "outputs": [
        { "name": "approved", "type": "boolean", "from": "return.approved", "to": "vars.approved" },
        { "name": "score", "type": "number", "from": "return.score", "to": "results.code1.score" },
        { "name": "message", "type": "string", "from": "return.message", "to": "vars.message" }
      ]
    },
    {
      "id": "cond1",
      "type": "Condition",
      "strategy": "firstMatch",
      "items": [
        { "no": 1, "left": "vars.approved", "op": "eq", "right": true },
        { "no": 2, "left": "sys.userName", "op": "eq", "right": "admin" },
        { "no": 3, "left": "input.amount", "op": "gt", "right": 10000 }
      ]
    },
    {
      "id": "http1",
      "type": "Logic",
      "kind": "Http",
      "async": true,
      "method": "POST",
      "url": "/api/notify/risk",
      "resultRoot": "body.data",
      "inputs": [
        { "name": "orderId", "from": "input.orderId" },
        { "name": "operatorId", "from": "sys.userId" },
        { "name": "amount", "from": "input.amount" }
      ],
      "outputs": [
        { "name": "httpStatus", "type": "number", "from": "statusCode" },
        { "name": "bizStatus", "type": "number", "from": "body.status" },
        { "name": "ticketId", "type": "string", "from": "ticketId" }
      ]
    },
    { "id": "end", "type": "End" }
  ],
  "edges": [
    { "source": "start", "target": "sql1" },
    { "source": "sql1", "target": "code1" },
    { "source": "code1", "target": "cond1" },
    {
      "source": "cond1",
      "target": "http1",
      "combine": "1 and (2 or 3)"
    },
    {
      "source": "cond1",
      "target": "end",
      "isDefault": true
    },
    { "source": "http1", "target": "end" }
  ]
}
```

**字段约定**

| 字段 | 约束 |
|------|------|
| `inputs[]` | 逻辑 API 请求契约；`source=system` 不可被调用方覆盖 |
| `outputs[]` | 最终响应字段；**必须**带 `visibleTo`（缺省视为 `all`） |
| `nodes[].type=Condition` | 共享 `items`；出边写 `combine` 或 `isDefault` |
| `nodes[].type=Logic` | `kind` ∈ Http/Code/Sql；**显式 `inputs` / `outputs`** |
| `nodes[].async` | 默认 false=同步；true=异步（下游不得读其出参，除非 Wait） |
| `nodes[].resultRoot` | Http/Sql 业务数据根，如 `body.data`；`outputs.from` 优先相对该根（绝对路径如 `statusCode` / `rows[0].Cnt` 仍可用） |
| `entry` / 边 `combine` | 仅编号与 `and/or/not/()` |
| `sql` | 参数化；`@name` 对应 `inputs[].name`；数据源白名单 |

### 9.4 发布前校验规则

| 级别 | 规则 |
|------|------|
| Error | 无 Start / 多个 Start / 存在环 |
| Error | 必填入参缺少 type/name；无 InputSchema 不可发布 |
| Error | 条件组合式引用不存在的编号；Condition 无出边 |
| Error | 执行节点必填 `inputs` 映射缺失且 required |
| Error | Code 未过沙箱静态检查；Sql 未选白名单或疑似拼接 |
| Error | 同步节点读取异步节点出参且无 Wait |
| Error | `outputs[].from` 指向未声明路径（后期收紧） |
| Warning | 最终出参无 `visibleTo`（将按 all） |
| Warning | 存在不可达节点；出参从未被写入 |
| Warning | 敏感字段未标 `sensitive` |

---

## 10. 数据流与执行语义

### 10.1 执行器主循环

```
API POST /api/logic/{flowKey} (body)
  → 鉴权（调用权限）
  → 解析 sys.* 与日期表达式快照
  → 按 InputSchema 合并：system 默认 ← body（不可覆盖 system）
  → 校验必填 / 类型
  → 创建 FlowInstance（记录调用方角色快照）
  → 从 Start 沿边推进：
        · Logic 节点:
            求值 optional entry → 假则 Skipped，不写 outputs
            真 → 映射 inputs → nodeInput
            → async? 投递后台 : 执行 Http/Code/Sql
            → 按 outputs[] 写 vars/results
        · Condition 节点:
            按边序求值 combine；firstMatch 选一条；皆假走 default
            无匹配且无 default → Error
        · End:
            按 OutputSchema.from 组装完整 data
            → 按 visibleTo + 调用方角色/权限过滤
            → 返回 { success, data, instanceId, meta }
  → 主链结束即返回（异步节点可能仍在跑）
```

### 10.2 节点 I/O 语义（方法调用模型）

每个**可执行节点**视为一次方法调用：`outputs = Node.Execute(inputs)`。

| 节点 | 读 | 写 |
|------|----|----|
| Start | — | 初始化 `input` / `sys` |
| Assign / Http / Code / Log | **nodeInput**（由 `inputs[]` 映射） | **`results[nodeId].*`**（由 `outputs[]` 投影） |
| Condition | input / sys / results | 不写业务数据；只选边 |
| End | input / sys / results | API `data`（再经角色过滤） |

规则：

1. 可执行节点声明 `inputs[]` / `outputs[]`；无 `outputs` 时，结果对象顶层字段自动成为出参名。  
2. 每个动作节点有 **引用名 `ref`**（如 `setLevel`），结果挂在 `results.{ref}`。  
3. **推荐引用方式（由简到繁）**：  
   - 短名：`level` / `{{level}}`（出参名不冲突时）  
   - 引用名：`setLevel.level` / `{{setLevel.level}}`  
   - 完整：`results.setLevel.level`（仍兼容）  
4. 同名短名后写覆盖；冲突时请用 `引用名.出参`。  
5. `vars.*` / 内部 nodeId 仅兼容旧 DSL。  
6. 映射失败且 required → 节点 Failed。  
7. 最终 `data` 过滤在出站完成。

### 10.3 角色可见性过滤语义

```
fullData = map(OutputSchema)
callerRoles / callerPermissions = from JWT / Identity
for each field in OutputSchema:
  if visibleTo.mode == all → keep
  if roles → keep iff 交集非空
  if permissions → keep iff 持有任一权限
  if none → drop from API response
return filtered data + meta.visibleFields
```

试运行（DryRun）默认返回**完整 data**（编排者调试），但 UI 可切换「按角色预览」。

### 10.4 错误、超时、重试、异步

| 主题 | 建议 |
|------|------|
| 同步 | 默认；失败可中断主链 |
| 异步 | 不阻塞返回；失败记日志；需结果则 Wait |
| 超时 | 节点级 timeoutMs |
| Sql | 参数化 + 租户注入；写需 `Orchestration.Sql.Write` |
| Code | 沙箱超时/内存；只见 nodeInput/sys |
| DryRun | 异步改同步；可「按角色预览」过滤出参 |

### 10.5 与阻塞式工作流的关系

主链一次 burst。异步是后台任务，不是审批 Bookmark。人工待办不做。

---

## 11. 节点类型规划

### 11.1 当前设计器可用

| 类别 | 节点 | 说明 |
|------|------|------|
| 结构 | Start / End | 入口 / 出口；Start 维护 InputSchema |
| 结构 | Condition | 条件表 + 出边 combine / else |
| 动作 | **Assign** | 入参映射 → `results[nodeId]`（取代写变量） |
| 动作 | HttpCall | HTTP + inputs/outputs |
| 动作 | Code | 沙箱脚本 + 可选 DataSource/`db.*` |
| 动作 | Log | `{{path}}` 模板；出参 `message` |
| 动作 | Mask / Throw | 脱敏 / 业务失败 |
| 动作 | **RabbitMqPublish** | 本地 Rabbit **fanout 广播**；`payload` 局部 JSON |
| 横切（规划） | **`txMode=sameDataSource`** | 同 SQL 库跨节点统一事务；失败回滚 |

> 心智模型：动作节点 = C# 方法调用；数据只通过 `results.*` 向下游流动。副作用节点（Rabbit）载荷用局部 `payload`，回执才进 outputs。

### 11.2 日志模板用法（Log）

运行时用 `FlowContextResolver.ResolveTemplate`，匹配 `{{ path }}`（花括号内可有空格）。

| 写法 | 含义 | 示例 |
|------|------|------|
| `{{input.x}}` | 请求入参 | `金额={{input.amount}}` |
| `{{input.a.b}}` | 嵌套入参 | `订单={{input.order.id}}` |
| `{{vars.x}}` | 流程变量 | `标记={{vars.flag}}` |
| `{{sys.userName}}` / `{{sys.Now}}` | 系统上下文 | `操作人={{sys.userName}}` |
| `{{results.nodeId.x}}` | 某节点结果 | `状态={{results.http1.statusCode}}` |
| 无前缀 `{{flag}}` | 兼容读 `vars.flag` | 尽量写全 `vars.` |

规则：

1. 整段文本可混写多处插值；未解析到的路径替换为空字符串。  
2. 模板**不是**任意 JS；只做路径替换，不做运算（运算用 Condition / SetVariable / Code）。  
3. 试运行与正式执行都会把解析后的文案写入节点日志（`InputJson.message`），并打 `[FlowLog]`。  
4. 敏感字段若在契约标了 `sensitive`，后续应对日志做脱敏（规划中）。

推荐示例：

```text
High amount: {{input.amount}}, user={{sys.userName}}, at={{sys.Now}}
订单 {{input.order.id}} 数量={{input.order.qty}} → flag={{vars.flag}}
```

### 11.3 动作节点扩展路线（按优先级）

| 优先级 | 节点 | 用途 | 说明 |
|--------|------|------|------|
| **P0 已有** | Assign / Log / HttpCall / Code / Mask / Throw / **RabbitMqPublish** / **SubFlow** | 统一 I/O + fanout + 逻辑组件 | 见 §5.4.8 / §5.4.9 |
| **P1** | **txMode=sameDataSource** | 同 SQL 库跨节点统一事务 | 失败 Rollback；多库不强求；见 §5.4.7 |
| **P1** | Switch | 多路枚举分支 | 与 Condition 互补 |
| **P1** | Loop / ForEach | 数组批处理 | 代码逻辑编排刚需 |
| **P1** | Sql（白名单） | 受控读写 | 与 DataSource 共用 |
| **P2** | Outbox | 事务性发信到 Rabbit | 库成功 ⟺ 消息必达 |
| **P2** | MqttPublish | IoT / 设备 MQTT | 非默认；确有需求再做 |
| **P2** | Parallel / Wait | 并行与汇合 | 非审批 bookmark |
| **P2** | 领域组件目录 | `pricing.calc` 等 | 产品主路径：注册组件而非堆 HTTP |

刻意不做：UserTask / 会签 / 待办、开放任意脚本沙箱、海量 SaaS 连接器、跨库 XA。

### 11.4 产品口径补齐（横切能力）

| 类别 | 能力 |
|------|------|
| 契约 | InputSchema / OutputSchema；`sys.*`；日期式 `sys.Now - 3d` |
| 发布 | `POST /api/logic/{flowKey}` + 出参角色过滤 |
| 条件 | 编号条件 + `1 and (2 or 3)` + 多出边 / else |
| 执行 | 每节点 inputs / outputs；可选 `async`；**同库 `txMode`** |
| 消息 | **RabbitMqPublish** 局部 payload（优先本地 Rabbit）；可选 Outbox；MQTT 非默认 |
| 选择器 | literal / input / sys / vars / results / 日期式 |
| IAM | 调用权限 ≠ 字段可见 |

### 11.5 P2 增强

| 能力 | 说明 |
|------|------|
| Wait | 等待异步出参可用 |
| WHEN 并行 | 多分支并行后汇合 |
| 组件市场 | 可复用领域组件（不止 Http/Code/Sql） |
| 数据源管理 | Sql/Code 共用登记、只读/写、审计 |
| Code+db | 沙箱循环 + `db.batch` 参数化；见 §5.4.6 |

> Sql / Code 写库必须白名单 + 参数化，不是开放查询台，禁止字符串拼 SQL。

---

## 12. 与现有 IAM / 菜单的对接

完全沿用 `docs/iam-rbac-menu.md`，并扩展字段可见性：

| 权限名 | 用途 |
|--------|------|
| `Orchestration.Definitions` | 进入编排、查看定义 |
| `Orchestration.Definitions.Create/Update/Delete` | 改稿、维护入参/出参/节点 I/O |
| `Orchestration.Definitions.Publish` | 发布为系统 API |
| `Orchestration.Instances` | 查看执行实例（含完整 data 快照） |
| `Orchestration.Instances.Run` | 试运行 / 调试；可按角色预览 |
| `Orchestration.Instances.Cancel` | 取消 |
| `Orchestration.Sql.Write` | 允许 Sql 节点写库 |
| `Orchestration.Logic.{flowKey}`（可选） | **调用**某条已发布逻辑 API |
| （字段级）`visibleTo.roles / permissions` | **不**做成 PermissionDefinition；存在 OutputSchema，运行时过滤 |

要点：

1. **调用权**与**字段可见**分离。  
2. 角色名/Id 与 Identity 对齐；编排器提供角色多选。  
3. 业务 API 建议不对 Host 自动 bypass 全部字段。  
4. 多租户：定义、实例、角色列表均隔离。

落地：PermissionDefinitionProvider → 种子 → SysMenu → `AccessControl`。

---

## 13. API

| 方法 | 路径 | 说明 |
|------|------|------|
| GET/POST | `/api/orchestration/definitions` | 定义列表/创建（含 inputs/outputs） |
| GET/PUT | `/api/orchestration/definitions/{id}` | 详情/更新草稿（含节点 I/O） |
| POST | `/api/orchestration/definitions/{id}/publish` | 发布；生成系统 API |
| GET | `/api/orchestration/definitions/{id}/versions` | 版本历史 |
| POST | `/api/orchestration/instances:dry-run` | 试运行；可选 `previewAsRole` |
| GET | `/api/orchestration/instances/{id}` | 实例 + 节点日志（entry/分支/Skipped） |
| POST | `/api/orchestration/instances/{id}/cancel` | 取消 |
| GET | `/api/orchestration/system-parameters` | `sys.*` 目录 + 日期表达式试算 |
| GET | `/api/orchestration/data-sources` | Sql 数据源白名单 |
| GET | `/api/orchestration/roles` | 编排器用：当前租户角色列表（出参可见配置） |
| **POST** | **`/api/logic/{flowKey}`** | **已发布逻辑系统 API；响应已按角色过滤** |

**系统 API 响应形状**

```json
{
  "success": true,
  "instanceId": "...",
  "data": { "approved": true, "message": "ok" },
  "meta": {
    "visibleFields": ["approved", "message"],
    "omittedFieldCount": 1
  }
}
```

Gateway：`/api/orchestration/{*any}` 与 `/api/logic/{*any}` → SaaS。

---

## 14. 当前 MVP 与目标差距

| 能力 | 产品口径 | 当前 | 优先级 |
|------|----------|------|--------|
| 请求参数定义 | Input Schema + 默认值 / system | 试运行手写 JSON | **P0** |
| 系统参数 | `sys.*` + 日期表达式 | 无 | **P0** |
| 条件节点 | 多条件 + 逻辑组合 + 多出边 | 边 when true/false | **P0** |
| 执行节点 I/O | 每节点 inputs / outputs | 无显式映射 | **P0** |
| 最终出参可见性 | OutputSchema.visibleTo | 无 | **P0** |
| 发布为系统 API | `/api/logic/{flowKey}` + 字段过滤 | 仅内部 instances | **P0** |
| 节点种类 | Http / Code / Sql | 仅 HttpCall 等 | **P0** |
| 节点异步 | `async` + 面板开关/角标 | DSL 有字段，UI/执行未齐 | **P1** |
| Http 结果根 | `resultRoot` + 相对投影 | 仅绝对 `from`（如 `body.url`） | **P1** |
| 取值选择器 | input/sys/vars/results | 手写 | **P0** |
| Sql 安全 | 白名单 + 参数化 + 租户 | 无 | 与 Sql 同步 |

**结论**：目标引擎 = **契约 → 条件分支 → 节点显式 I/O → 执行 → 角色可见响应**。实现优先补齐契约、Condition 组合、节点 I/O、发布与字段过滤，而不是堆连接器。

---

## 15. 分期落地任务清单

### 阶段 0：口径冻结

- [x] 代码逻辑编排，不是 iPaaS / BPM  
- [x] 入参 / 系统参数 / 日期表达式 / 发布为 API  
- [x] 条件节点多条件 + 逻辑运算；执行节点入参/出参；最终结果角色可见  
- [x] 引擎能力全景（契约 / 编排 / 执行 / 安全可见性）

### 阶段 1（引擎骨架，先做）

- [x] 定义级 **请求参数 / 最终出参（含 visibleTo）** 表单  
- [x] **系统参数** + 日期表达式求值与选择器（`sys.Now - 3d`）  
- [x] **Condition**：条件表 + `1 and (2 or 3)` + 多出边 / default  
- [x] 执行节点 **inputs / outputs** 映射（Http）  
- [x] 发布 **`POST /api/logic/{flowKey}`**；按 schema 校验；**响应按角色过滤**  
- [x] 发布校验（编号、环、Start、systemExpr）

### 阶段 2（执行能力补齐）

- [ ] **DataSource** 全局外部库登记（密钥、读写模式、租户隔离）  
- [ ] **Sql** 节点：白名单库 + `@param` 参数化 + `Orchestration.Sql.Write`  
- [x] **Code** 沙箱：循环/遍历；可选 `dataSourceId`；仅 `db.query/execute/batch`（禁拼接 SQL）
- [x] Code+db：**list → paramList → batch** 文档见 §5.4.6
- [ ] **Sql** 节点：与 DataSource 共用参数化执行
- [x] 节点 **同步/异步**：设计器开关 + 画布角标；Http 异步排队（主链不写出参）  
- [x] Http/Sql **`resultRoot`**：先定业务根再投影出参  
- [ ] Wait：等待异步出参可用；实例回写异步结果  
- [ ] DryRun：入参表单 + **按角色预览**完善；写库试运行默认回滚  

### 阶段 3（强大引擎）

- [ ] Wait、SWITCH、WHEN  
- [x] **SubFlow** + 逻辑组件目录 / FlowUsage 引用索引（§5.4.9）  
- [ ] 领域组件目录（领域服务注册）；EL 文本对照  
- [ ] 单条逻辑独立调用权限码；字段级审计  

---

## 16. 明确不做什么（控风险）

| 不做 | 原因 |
|------|------|
| 审批流 / 会签 / 待办 | 不是本主题 |
| n8n 式连接器市场 | 偏离「逻辑 API」 |
| 任意用户脚本无沙箱 | 多租户安全 |
| 无白名单、可拼接的生产库 SQL | 审计失控 |
| 调用方覆盖 `sys.userId` | 防伪造身份 |
| 能调 API 就返回全部字段 | 违反最小可见；必须走 visibleTo |
| 页面可视化搭建 | 正交能力 |

---

## 17. 技术选型结论

| 决策点 | 结论 |
|--------|------|
| 产品形态 | **契约 + 条件分支 + 节点 I/O + 发布 API + 角色可见** |
| 条件 | **独立 Condition 节点**：多条件编号 + `and/or/not`；多出边 |
| 执行节点 | **Http / Code / Sql**；每节点配置输入/输出参数；可异步 |
| 最终结果 | OutputSchema + **visibleTo（角色/权限）** 出站过滤 |
| 参数 | 请求参数 + 系统参数 + 日期相对表达式（`sys.Now - 3d`） |
| 前端 | AntV X6；属性面板以映射表与条件表为核心 |
| 后端 | 自研 .NET 解释器；Sql/Code 沙箱；Identity 角色对接 |
| 触发 | 已发布 `/api/logic/{flowKey}` 为主；试运行为辅 |
| 部署 | MVP 在 SaaS；目标演进为强大逻辑编排引擎 |

---

## 18. 参考链接

### 代码逻辑编排（主线）

| 资源 | URL |
|------|-----|
| LiteFlow | https://github.com/dromara/liteflow |
| LiteFlow 简介（工作台模式） | https://liteflow.cc/pages/5816c5/ |
| LiteFlow 构造 EL | https://liteflow.cc/pages/a3cb4b/ |
| X6 可视化 LiteFlow | https://github.com/Cooooooler/liteflow-editor-client |
| LogicFlow → LiteFlow EL | https://gitee.com/xdewx/logicflow-liteflow |
| liteflow-logicflow-vue | https://gitee.com/ganzhirong/liteflow-logicflow-vue |
| 图转 EL（Java） | https://github.com/356110537/iflytek-liteflow-el-builder |
| 宜搭逻辑编排 | https://docs.aliwork.com/docs/yida_qalist/lmvsctyt7rxtgsrq/lbvx0y |
| 微搭逻辑流（条件/并行） | https://cloud.tencent.com/document/product/1301/77287 |

### 画布与辅助

| 资源 | URL |
|------|-----|
| AntV X6 | https://github.com/antvis/X6 |
| X6 flowchart 示例 | https://github.com/antvis/X6/blob/master/site/examples/showcase/practices/demo/flowchart.ts |
| LogicFlow | https://github.com/didi/LogicFlow |
| n8n（仅借鉴调试，非产品形态） | https://github.com/n8n-io/n8n |
| Elsa（.NET 工作流，非主线） | https://docs.elsaworkflows.io/getting-started/concepts |
| 本仓库 IAM | `docs/iam-rbac-menu.md` |

---

## 19. 建议的下一步

1. **设计器 / 执行器**：巩固 Http `resultRoot` + 出参 `map`；Code/Sql 按 §5.4.6 演进。  
2. **DataSource + 参数化 SQL**：先管理端登记与 Sql 节点，再给 Code 注入 `db.batch`。  
3. 随后：Wait / 并行 / 领域组件目录。  
4. 对外文案：「逻辑编排引擎 / 系统 API」，不用「自动化」「审批」。

下一轮实现优先：**DataSource 白名单** → **Sql 参数化** → **Code 沙箱 + db.batch（list 安全落库）**。
