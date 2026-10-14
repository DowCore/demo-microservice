> **功能描述**见 [../form.md](../form.md)。本文件是该功能的证据附录（契约 / 评审 / 方案），不单独作为阅读入口。

# Meta.Dow 表单与 OA 审批：能力契约、行为边界与增强规划

> **文档性质**：混合，已拆分——§1 定位与边界、§3 契约与实现细节、§6 操作说明为**事实契约**；§5 为**规划**（当前不可用）；§4 为**已知缺陷**。三部分严格隔离，不混写。
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）：Release 构建 0 错误，62/62 单测通过。
> **核对方式**：文中每条"已实现"结论均附 `相对路径:行号`，均经逐行 read 复核；§9 列出本次改写的失真断言全表。
> **状态图例**：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划） ｜ 🐞 已知缺陷 ｜ ⚠️ 未核验（前端在 vue-demo）
> **关联文档**：[`oa-contract.md`](./oa-contract.md)（OA 侧契约、缺陷与规划，与本文**不重复**）、[`data-collection-contract.md`](./data-collection-contract.md)、[`form-preview.md`](./form-preview.md)、[`logic-orchestration-contract.md`](./logic-orchestration-contract.md)、[`report-contract.md`](./report-contract.md)
> **前后端边界**：前端 `vue-demo`（Vben Admin / Ant Design Vue）**不在本仓**，凡涉前端的结论一律标注 `⚠️ 未核验（前端在 vue-demo）`。

---

## 1. 定位与边界

### 1.1 这篇管什么

本仓（`Meta.Dow.SaaS`）的**表单定义链**与**人审工作流定义链**的：

- 领域模型与 JSON 契约（字段、布局树、控件、联动、节点、连线）；
- 定义侧与运行时的**真实行为**：默认值、上限、校验时机、失败开/失败关；
- 定义侧与运行时**已知缺陷**（§4）；
- 对照成熟方案后**仍缺**的能力（§5，明确"当前不可用"）。

### 1.2 这篇不管什么

- **不描述前端渲染**。表单渲染器、设计器交互、审批时间轴的实际渲染，全部在 `vue-demo`，本仓只能核验后端供数契约。
- **不描述逻辑编排引擎的控制流原语**。`FlowExecutor` 的 `ForEach`/`Parallel`/`SubFlow` 等属于编排引擎，见 [`logic-orchestration-contract.md`](./logic-orchestration-contract.md)。**特别注意**：OA 侧与人审侧的"并行/循环"是**两回事**——编排侧的并行与循环**已实现**（见 §2 表），而 **OA 人审侧**的 fork/join、`callActivity` 确实未实现。
- **不描述报表定义**。见 [`report-contract.md`](./report-contract.md)。

### 1.3 初始缺陷诊断与重构目标（保留原文，状态已按代码校正）

| 模块 | 原诊断的致命缺陷 | 业务影响 | 重构标准 | 现状 |
|:---|:---|:---|:---|:---|
| **表单编辑** | 缺少组织协同控件（用户/部门/级联/上传等） | 无法录入请假人、报销部门、凭证附件等 OA 基础信息 | 扩充标准控件协议 | ✅ 模型层已扩（§3.1）；渲染 `⚠️ 未核验` |
| **表单编辑** | 缺少字段动态联动与依赖 | 无法按条件动态展示输入项 | 增加 `dependencies` 协议 | ⚠️ 协议已有，**无任何后端求值点**（§4.3） |
| **表单编辑** | `Fields`、`Layout`、`VformJson` 多轨割裂，发布硬编码阻断 | 设计器用容器树时若未同步 `Fields`，发布报 `FormFieldsRequired` | 统一以容器树为核心，后端自动拍平 | ⚠️ 覆盖式拍平**主体已实现**，但存在幽灵字段缺口（§4.1） |
| **OA 流程** | 缺少"退回/驳回"、"转办"等核心审批动作 | 审批不通过导致整条流程异常终结 | 五动作 | ⚠️ 入口齐备，**驳回语义有严重缺陷**（§4.8） |
| **OA 流程** | 缺少节点级表单字段权限控制 | 权限控制形同虚设 | 节点 `fieldPermissions` | ⚠️ 已实现但 fail-open（§4.7） |
| **OA 流程** | 审批过程无法补充或修改表单数据 | 审批人无法在审核时纠正字段 | `CompleteWorkflowTaskDto.RecordPatch` | 🐞 已实现但异常路径静默丢失（§4.9） |
| **OA 流程** | 流程无版本快照，导致在途审批死锁 | 改流程后在途任务按最新 JSON 寻址报错 | 启动时固化 `ProcessSnapshotJson` | ✅ 流程侧已固化（§3.4） |
| **OA 流程** | 部门主管硬编码为全局 `manager`，空审批人直接崩溃 | 不是发起人直属主管；无人时抛异常卡死 | 组织树解析 + `emptyFallback` | ⚠️ 已实现；`manager` 可解析出**发起人本人**（§4.6） |

> **一句话结论**：表单定义链与 OA 定义链的**入口几乎都建好了**，但「入口存在」不等于「行为可靠」——表单未真正冻结、字段联动后端不求值、驳回语义错、补丁可能丢、并发无保护。§2 是能力清单，§4 是稳定性真相，两者不冲突。

---

## 2. 现状能力总表（本文唯一权威表）

> 本表回答"能力**有没有**落地"；§4 回答"**稳不稳**"。⚠️/❌/🐞 项一律不得理解为可用。

### 2.1 表单侧

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|--:|------|:----:|------------------|------|
| F-1 | 控件类型枚举（`input`/`textarea`/`number`/`select`/`radio`/`checkbox`/`cascader`/`userPicker`/`deptPicker`/`upload`/`table`/`list`/`tree`/…） | ✅ | `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs:334`（`FormFieldDef.Control` 默认 `input`，注释列全枚举） | 模型是**自由字符串**，非 enum：拼错不报错 |
| F-2 | 控件特有配置 `ControlProps` | ✅ | `ResourceSchema.cs:362`（字段）、`:441`（控件节点） | `Dictionary<string, object?>`，键值任意、不校验 |
| F-3 | 字段动态联动协议 `dependencies` | ⚠️ | `ResourceSchema.cs:359`（`List<FormFieldDependencyDef>`）、`:304-315`（`SourceField`/`Op`/`Value`/`Action`/`SetValue`） | **协议存在，后端零消费点**；全部求值在前端（§4.3） |
| F-4 | 栅格与容器树（`grid`/`card`/`data-table`/`subform`） | ✅ | `ResourceSchema.cs:381-442`（`FormWidgetDef`）、`:449-450`（`FormDef.Layout`） | |
| F-5 | 容器树自动拍平 `Fields`（覆盖式） | ⚠️ | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FormDefinitionAppService.cs:213-228`（`EnsureFlattenedFields`）；调用点 `:117`（`UpdateAsync`）、`:165`（`PublishAsync`） | **抽出 0 个字段时不覆盖** → 幽灵字段仍可发布（§4.1） |
| F-6 | 拍平不丢明细结构/规则/联动/控件配置 | ✅ | `FormDefinitionAppService.cs:275-297`（`BuildField`）、`:235-262`（`ExtractFieldsFromWidgets`）、`:264-273`（`IsDetailContainer`） | 明细容器的子列聚合为 `Children`，不再扁平到顶层 |
| F-7 | 克隆不丢属性（深拷贝） | ✅ | `FormDefinitionAppService.cs:426-454`（`CloneWidget`）、`:456-479`（`CloneField`）、`:481-493`（`CloneRule`）、`:495-503`（`CloneDependencies`） | `Rules`/`Dependencies`/`ControlProps` 均新建对象 |
| F-8 | 发布生成不可变快照 + `SchemaVersion` | ⚠️ | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/FormDefinition.cs:37`（`PublishedSchema`）、`:25`（`SchemaVersion`）、`:81-86`（`Publish`）、`:89`（`GetEffectiveSchema`）；调用 `FormDefinitionAppService.cs:174-175` | 快照是**单份**、无历史版本；历史记录 `PublishedSchema=null` 时回落**可变草稿**（§4.2） |
| F-9 | 落库列名映射 `ColumnName` | ⚠️ | `ResourceSchema.cs:325`（字段）、`:403`（控件） | **运行时无任何消费点**（§4.4）；仅用于发布校验 `FormDefinitionAppService.cs:350` |
| F-10 | 发布校验：字段名唯一 | ✅ | `FormDefinitionAppService.cs:299-325`（`ValidateFieldNames`），调用点 `:171` | 大小写不敏感去重；递归校验 `Children` |
| F-11 | 发布校验：与绑定资源列对齐 | ⚠️ | `FormDefinitionAppService.cs:328-358`（`ValidateAgainstResourceAsync`），调用点 `:172` | **fail-open**：资源不存在（`:336-339`）或无列（`:342-345`）时**直接放行**（§4.5） |
| F-12 | 删除引用完整性检查 | ⚠️ | `FormDefinitionAppService.cs:134-156` | 覆盖 OA `FormRef`（`:138`）、报表 `ActionDef.FormKey`（`:143-147`）、资源 `ListView.Actions[].FormKey`（`:149-153`）；**先查后删非原子**，且 `FormKey` 实为模式名（§4.10） |
| F-13 | 资源列重新同步入口 | ⚠️ | `FormDefinitionAppService.cs:182-206`（`ResyncCatalogFromResourceAsync`）；端点 `src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/AppBuilderControllers.cs:273-276` | **只刷新 `FieldCatalog`**，**不重推 `Schema.Fields`**（§4.11） |
| F-14 | 字段校验规则的**后端求值** | ❌ | `ResourceSchema.cs:287-302`（`FormFieldRuleDef` 仅 `MinLength`/`MaxLength`/`Pattern`/`PatternMessage`/`Min`/`Max`/`Message`） | 后端无消费点；§5.1 的 `Kind`/`Len`/`Enum`/`Validator`/`Deps`/`Severity` 全不存在 |
| F-15 | 表单提交走编排 `SubmitFlowKey` | ❌ | `ResourceSchema.cs:458` 定义；仅被 `FormDefinitionAppService.cs:419` 与 `ReportDefinition.cs:314` 克隆 | **无运行时消费点** |

### 2.2 OA 定义链（W1–W9 逐条复核）

> 完整运行时契约见 [`oa-contract.md`](./oa-contract.md) §3，本文不重复。

| # | 原缺陷 | 状态 | 证据 `file:line` | 遗留问题 |
|--:|--------|:----:|------------------|----------|
| W1 | 定义侧零校验 | ⚠️ | `WorkflowDefinitionValidator.cs:50-150`（全量结构校验），调用点 `WorkflowDefinitionAppService.cs:154`（仅 `PublishAsync`） | 已发布定义可被 `PUT` **原地改写且不重校验** → 校验可被整体绕过（§4.12 ①） |
| W2 | 连线指向不存在节点 → 静默通过 | ⚠️ | `WorkflowRuntimeAppService.cs:484-492`（`RequireTargetNode` 抛错），三处调用 `:454`/`:466`/`:474` | **无出线节点仍 fail-open** → `Complete("approved")`（`:443-446` → `:433`），§4.12 ② |
| W3 | 未知节点类型被当审批节点 | ✅ | `WorkflowRuntimeAppService.cs:420-430`（`approver` 显式分支，其余抛错） | — |
| W4 | 默认格式是残废的 `childNode` 链 | ✅ | `WorkflowDefinition.cs:92-105`（`DefaultProcessJson()` 产出 `nodes`/`edges`）；旧树仍可读 `WorkflowRuntimeAppService.cs:1192-1195` | 双格式长期并存（旧格式仍能读入，边不带条件） |
| W5 | 解析容错过度，坏数据静默降级 | ✅ | `WorkflowRuntimeAppService.cs:1108-1116`、`:1134-1143`、`:1154-1162`（`LoadGraph` strict）、`:1213-1221`（环检测） | `strict:false` 仅用于历史快照展示 |
| W6 | 条件分支靠数组顺序 | ⚠️ | `WorkflowRuntimeAppService.cs:1435-1439`（`Priority`/`IsDefault`）、`:462-475`（排序取首个命中）；校验 `WorkflowDefinitionValidator.cs:156-178` | **全 0 时仍按数组顺序**；`IsDefault` 完全不校验；校验只查 `Priority>0` 的重复（§4.12 ③） |
| W7 | 办理人魔法字符串，默认值危险 | ⚠️ | `WorkflowRuntimeAppService.cs:1501`（`EmptyFallback = "error"`）、`:622-630`（显式 `role`、未知类型抛错）、`:600-632`（`manager` 分支） | `WorkflowOrgResolver` **不排除发起人本人 → 可自审批**（§4.6） |
| W8 | 字段权限 fail-open | ⚠️ | `WorkflowRuntimeAppService.cs:957-990`（未知值抛错 `:980`）、`:993-1012`（`EnforceRequiredFields`） | **未声明字段默认放行**（`:983-986`）；**无模式级 baseline**（§4.7） |
| W9 | 无版本、无唯一索引 | ⚠️ | `WorkflowDefinition.cs:25`（`Version`）、`:81-85`（`Publish` 递增）；`WorkflowInstance.cs:30`/`:33`；`src/services/saas/src/Meta.Dow.SaaS.MongoDB/SaaSMongoDbModule.cs:53-63`（唯一索引 `TenantId+Code`） | **建索引失败仅 `LogWarning`**，启动照常（`SaaSMongoDbModule.cs:101-104`）；只存 int 版本号，**无历史快照可回放**（§4.2） |

### 2.3 编排引擎侧（澄清历史误记）

| 能力 | 状态 | 证据 `file:line` |
|------|:----:|------------------|
| 循环 `ForEach` / `Loop` | ✅ | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FlowExecutor.cs:759-765`（`ExecuteForEachAsync`） |
| 并行 / 合流 `Parallel` / `When` | ✅ | `FlowExecutor.cs:767-773`（`ExecuteParallelAsync`） |
| 子流程 `SubFlow` | ✅ | `FlowExecutor.cs:750-757`（`ExecuteSubFlowAsync`） |
| 独立 `Sql` 节点 | ❌ | 节点 dispatch 无 `sql` 分支（`FlowExecutor.cs:791-792` 对未知类型抛错）；SQL 经 Code 节点 `db.*` 间接实现 |
| 独立 `Switch` 节点 | ❌ | 同上 |

> **历史误记更正**：旧版 §8.5.4 把"并行网关/合流、循环"写成"仍为规划"，那是**逻辑编排引擎**的能力，且**已实现**。真正未实现的是 **OA 人审侧**的 fork/join 与 `callActivity`（见 [`oa-contract.md`](./oa-contract.md) §5），以及编排引擎的独立 `Sql`/`Switch` 节点。

### 2.4 测试覆盖真相（实测）

| 测试类 | 测试特性 | 用例数 | 覆盖内容 |
|--------|--------:|-------:|----------|
| `FlowExecutorControlFlowTests` | 33 `[Fact]` + 2 `[Theory]`（6 `[InlineData]`） | **39** | **逻辑编排引擎**的出边基数/可达性校验、`ForEach`、`Parallel`、四条种子演示流程端到端 |
| `WorkflowDefinitionValidatorTests` | 11 `[Fact]` | 11 | 定义侧结构校验（`WorkflowDefinitionValidator.cs`） |
| `FormSchemaFlatteningTests` | 7 `[Fact]` | 7 | 表单拍平/克隆/字段名校验 |
| `WorkflowRecordPatchTests` | 4 `[Fact]` | 4 | `FilterRecordPatch` 的 read/hide/write/required/未知值 |
| `SecretsConfigSafetyTests` | 1 `[Fact]` | 1 | secrets 配置禁止 JSON 数组 |
| **合计** | **58**（56 `[Fact]` + 2 `[Theory]`） | **62** | 无 `MemberData`/`ClassData`；`dotnet test -c Release` 实测 62/62 通过 |

**零覆盖清单（这是 §4 缺陷长期存活的结构性原因）**：

- **全部运行时路径**：`WorkflowRuntimeAppService` 的 `EnterAsync` / `ChooseNext` / `CompleteAsync` / `CreateTasksAsync` / `ResolveAssigneesAsync`；`WorkflowOrgResolver`；`WorkflowNotifier`；
- **驳回语义**（§4.8）、**补丁落库**（§4.9）、**并发行为**（§4.13）；
- **F-8 快照 / F-12 删除引用检查 / F-13 重同步**（表单侧运行时）；
- 表单拍平只覆盖「Layout 抽出 ≥1 字段」的路径，**没有**「Layout 存在但抽不出字段」的用例（即 §4.1 缺口无回归保护）。

---

## 3. 契约与实现细节（按代码写实）

### 3.1 表单 Schema 模型

三层共存的模型（`ResourceSchema.cs`）：

| 类型 | 位置 | 作用 |
|------|------|------|
| `FormDef` | `:444-459` | `Fields`（扁平，由 Layout 派生）+ `Layout`（容器树）+ `VformJson`（VForm3 描述）+ `SubmitFlowKey` |
| `FormWidgetDef`（节点） | `:381-442` | 布局树节点，`Kind` = `field`/`container`；容器 `Container` ∈ `grid`/`card`/`data-table`/`subform` |
| `FormFieldDef`（字段） | `:317-375` | 扁平字段，与上方**几乎重复**（见 §4.14） |

**控件枚举（`FormFieldDef.Control`，`:334` 默认 `input`）**——模型层是自由字符串，**拼错不会报错、不会告警**：

- 基础输入：`input`、`textarea`、`number`、`password`
- 选择与多选：`radio`、`checkbox`、`select`、`cascader`
- 日期时间：`date`、`datetime`、`time`、`dateRange`
- 组织架构：`userPicker`、`deptPicker`
- 文件媒体：`upload`
- 开关与评价：`switch`、`rate`、`slider`
- 布局与容器：`grid`、`card`、`table`、`list`、`tree`、`tabs`

对应的 JSON 形态：

```json
{
  "control": "userPicker",
  "multiple": false,
  "controlProps": {
    "placeholder": "请选择审批人",
    "orgScope": "currentDept"
  }
}
```

> `multiple` 与 `orgScope` 在代码里**没有强类型属性**，只能塞进 `ControlProps`（`ResourceSchema.cs:362`）——键名由前后端约定，后端不校验。

**`FormFieldDef` 全字段（`:317-375`）**：

| 属性 | 类型 | 默认 | 说明 |
|------|------|------|------|
| `Field` | string | `null!`（必填） | 字段名 |
| `ColumnName` | string? | null | 落库列名映射；空则按 `Field` 处理（**无运行时消费点**，§4.4） |
| `Title` | string | `null!` | 标题 |
| `Control` | string | `"input"` | 控件类型（自由字符串） |
| `Span` | int | `12` | 24 栅格占列 |
| `Placeholder` | string? | null | 占位 |
| `VisibleOnCreate`/`VisibleOnUpdate`/`VisibleOnDetail` | bool | `true` | 场景可见性 |
| `ReadonlyOnCreate`/`ReadonlyOnUpdate` | bool | `false` | 场景只读（**无 `ReadonlyOnDetail`**） |
| `RequiredOnCreate`/`RequiredOnUpdate` | bool | `false` | 场景必填（**无 `RequiredOnDetail`**） |
| `Rules` | `FormFieldRuleDef?` | null | **单个可空对象**，不是数组（§4.15） |
| `Dependencies` | `List<FormFieldDependencyDef>` | `[]` | 联动规则（**后端不求值**） |
| `ControlProps` | `Dictionary<string, object?>` | `[]` | 控件特有配置 |
| `Children` | `List<FormFieldDef>` | `[]` | 明细列/子字段 |
| `MinItems`/`MaxItems` | int? | null | 明细行数上下限 |
| `TreeChildrenField` | string? | null | `tree` 子节点数组字段名 |

**`FormFieldRuleDef`（唯一存在的校验规则模型，`:287-302`）**：

```csharp
public class FormFieldRuleDef
{
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? Pattern { get; set; }
    public string? PatternMessage { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public string? Message { get; set; }
}
```

`Rules` 属性**本身是 `FormFieldRuleDef?`**（`:356`），即"一个字段一条规则对象"。旧文档 §5.1/§5.1.1 设计的 `List<FormFieldRuleDef> Rules` + `Kind`/`Len`/`Enum`/`Validator`/`Deps`/`Trigger`/`Severity` + `ValidateFlowKey` **在代码中全部不存在**（全仓 grep 无 `ButtonDef`/`ValidateFlowKey`/`modePermissions`）。这些一律归入 §5。

**`FormFieldDependencyDef`（`:304-315`）**：

```csharp
public class FormFieldDependencyDef
{
    public string SourceField { get; set; } = null!;   // 触发字段
    public string Op { get; set; } = "eq";             // eq | ne | in | notin | contains | isEmpty | isNotEmpty
    public string? Value { get; set; }                 // 比较值
    public string Action { get; set; } = "show";       // show | hide | enable | disable | require | optional | setValue
    public string? SetValue { get; set; }              // Action=setValue 时的赋值
}
```

`Op` / `Action` 均为**自由字符串**，后端无枚举校验、无求值器。文档旧版 §5.2 设计的 `loadOptions`/`fetchValue`/`compute`/`cascade` 四个 Action **不存在**（§5.2）。

### 3.2 拍平（`EnsureFlattenedFields`）的真实语义

`FormDefinitionAppService.cs:213-228`：

```csharp
internal static void EnsureFlattenedFields(FormDef schema)
{
    if (schema == null) return;
    schema.Fields ??= [];
    if (schema.Layout is not { Count: > 0 }) return;      // 无 Layout → 保留原 Fields
    var extracted = new List<FormFieldDef>();
    ExtractFieldsFromWidgets(schema.Layout, extracted);
    if (extracted.Count > 0)                              // ← 缺口：抽出 0 个时不覆盖
    {
        schema.Fields = extracted;
    }
}
```

- **是覆盖式**：Layout 有内容且能抽出至少 1 个字段时，`Fields` 被**整体替换**，因此"改必填/换控件/改标题""删字段收缩"都已生效（旧版 F1 的"只增不覆盖"已不成立）。
- **调用点**：`UpdateAsync`（`:117`）与 `PublishAsync`（`:165`）都会先跑一遍。
- **仍存在的缺口**：见 §4.1。

**字段抽取规则（`:235-262`）**：

- `Kind = "container"` 且 `Container ∈ {data-table, subform, list, tree}` 且 `Field` 非空 → 作为**明细字段**加入，其 `Children` 递归抽成该字段的 `Children`（不会重复出现在顶层）；
- `Kind = "field"` 且 `Field` 非空 → 作为普通字段加入；
- 其它容器（`grid`/`card`）→ 自身不入表，只递归其 `Children`。

`BuildField`（`:275-297`）逐属性拷贝 `ColumnName`/`Title`/`Control`/`Span`/`Placeholder`/`VisibleOn*`/`ReadonlyOn*`/`RequiredOn*`/`Rules`/`MinItems`/`MaxItems`/`TreeChildrenField`，并把 `Dependencies` 与 `ControlProps` **深拷贝**。

> 注意 `Title = widget.Title ?? widget.Field!`（`:280`）——控件没配标题时**用字段名兜底**，不是保留空。

### 3.3 发布链与快照

`FormDefinitionAppService.PublishAsync`（`:162-178`）顺序：

1. `EnsureFlattenedFields(entity.Schema)`（`:165`）；
2. `Fields` 为空 → 抛 `Orchestration:FormFieldsRequired`（`:166-169`）；
3. `ValidateFieldNames(entity.Schema)`（`:171` → `:299-325`）：递归校验字段名非空且**大小写不敏感唯一**；
4. `ValidateAgainstResourceAsync(entity)`（`:172` → `:328-358`）；
5. `entity.Publish(CloneForm(entity.Schema))`（`:175` → `FormDefinition.cs:81-86`）：`PublishedSchema = 深拷贝`、`SchemaVersion++`、`Status = Published`；
6. `UpdateAsync(autoSave: true)`。

`GetEffectiveSchema()`（`FormDefinition.cs:89`）= `PublishedSchema ?? Schema`。消费点：`GetPublishedByCodeAsync`（`FormDefinitionAppService.cs:67-76`，`Map(entity, published: true)` → `:515`）与 `GetPublishedLookupAsync`（`:542`）。

`CloneForm`（`:416-423`）**不深拷贝** `VformJson`（string，不可变，无妨）与 `SubmitFlowKey`（string），`Fields`/`Layout` 逐层克隆。

### 3.4 流程实例快照与版本

- 启动：`StartAsync`（`WorkflowRuntimeAppService.cs:50-98`）在 `beforeStartFlowKey` 成功后才 `new WorkflowInstance(..., definition.ProcessJson, definition.Version, formSchemaVersion)`（`:73-83`），随后 `AppendHistoryRecord(instance, "start", "开始", user, "start", "发起申请")`（`:84`）。
- 流转：`CompleteAsync` 优先读 `instance.ProcessSnapshotJson`，为 `{}` 时才回落当前定义（`:146-148`）；`MapInstanceAsync` 同理（`:880-885`）。
- 实例上的版本号：`DefinitionVersion`（`WorkflowInstance.cs:30`）与 `FormSchemaVersion`（`:33`），暴露在 `WorkflowInstanceDto`（`AppBuilderDtos.cs:771`/`:774`）。
- **但只有 int，没有历史结构**：见 §4.2。

### 3.5 字段权限矩阵与补丁过滤

契约与行为见 [`oa-contract.md`](./oa-contract.md) §3.5（避免重复）。此处只补两条经常被误读的点：

- `FilterRecordPatch` 对 `read`/`hide` 是**静默忽略**（不报错、不提示），调用方无法从响应中看出补丁被丢弃（`WorkflowRuntimeAppService.cs:975-978`）；
- `MergeRecordPatch` 是**顶层键覆盖**（`:1063-1081`），不是深度合并——嵌套对象会被整段替换。

---

## 4. 行为边界与已知缺陷

格式：`【严重度】现象 — 机理 — file:line — 后果/规避`（`file:line` 未加路径前缀者均指 `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/` 下文件）

### F 系列（表单定义链）

#### 4.1 【中】拍平被 `extracted.Count > 0` 门控 → 幽灵字段仍可发布

**现象**：设计器把某个字段节点删掉、同时在画布上保留/新增一个纯容器（`grid`/`card`）时，旧 `Fields` 条目**不会收缩**；发布仍然成功，发布的是一份含已删字段的陈旧定义。

**机理**：`EnsureFlattenedFields`（`FormDefinitionAppService.cs:213-228`）的覆盖只在 `extracted.Count > 0` 时发生（`:224`）。当 `Layout` 非空但抽不出任何字段（只有容器）时，函数提前于覆盖逻辑之后什么也不做，`schema.Fields` 原样保留。`PublishAsync` 只校验 `Fields.Count > 0`（`:166`），非零即通过。

**后果**：幽灵字段进入已发布快照与 `FieldCatalog` 之外的字段集，前端渲染与后端校验的依据不一致；该缺口**无回归测试**（`FormSchemaFlatteningTests` 的 7 个用例全都保证抽出 ≥1 个字段）。

**规避**：删除字段时确保 Layout 里至少还有一个 `kind=field` 节点；根治应把 `schema.Fields = extracted` 移出条件分支（Layout 非空即覆盖，允许结果为空数组）。

#### 4.2 【中】表单并未真正冻结：无历史快照、历史记录回落可变草稿

**现象**：`SchemaVersion` 与 `PublishedSchema` 都在，但**无法回答"那次实例用的是哪张表单结构"**——只有一个 int。

**机理**：

1. `PublishedSchema` 是**单份可变引用**，每次发布整份覆盖（`FormDefinition.cs:81-86`），**不保留历史版本列表**；
2. 历史数据（发布快照功能上线前已发布的表单）`PublishedSchema == null`，`GetEffectiveSchema()` 会 `?? Schema` 回落到**可变草稿**（`FormDefinition.cs:89`）；
3. 实例只存 `FormSchemaVersion`（int，`WorkflowInstance.cs:33`）。

**后果**：文档旧版说"OA 启动时再固化 `FormSnapshotJson`"——**该字段不存在**（全仓 grep 无 `FormSnapshotJson`）。流程冻结了，**表单并没有冻结**。旧版 §8.3 的"最坏组合（F4+W9）"在 `267d375` 上**部分仍然成立**：在途审批单读到的字段结构仍可随草稿漂移。

**规避**：无。发布后不要再改草稿（但这与设计器正常工作流冲突）。

#### 4.3 【中】字段联动 `dependencies` 后端零求值点

**现象**：`Dependencies` 可以被保存、可以随拍平与克隆原样传递，但**后端不会执行它**——显隐/禁用/必填/赋值全部依赖前端。

**机理**：全仓 grep `.Dependencies` 仅命中 `FormDefinitionAppService.cs` 四处**拷贝**（`:295`、`:452`、`:473`、`CloneDependencies` 定义 `:495-503`），**没有任何求值调用**。`Op`/`Action` 也没有枚举校验。

**后果**：跳过前端直接调 API 提交数据时，联动带来的必填/禁用约束**全部不生效**。对 OA 场景尤为相关：`dependencies` 常被用来做"请假类型=病假才要求上传证明"这类合规校验，后端不校验等于没有。**规避**：把真正的硬约束下沉到逻辑编排 Code/Condition 节点。

#### 4.4 【中】`ColumnName` 有属性、无消费点

**现象**：`FormFieldDef.ColumnName` / `FormWidgetDef.ColumnName` 都已存在，也能在拍平/克隆中传递，但**没有任何运行时读取点**。

**机理**：全仓 grep `ColumnName` 仅 8 处：`OrchestrationConsts.cs:11`（长度常量）、`ResourceSchema.cs:325`/`:403`（定义）、`FormDefinitionAppService.cs:279`/`:437`/`:460`（拷贝）、`:350`（**仅发布校验**用于「字段→资源列」映射比对）。`ResourceCrudExecutor` 里**没有**任何按 `ColumnName` 投影 `record` 的代码。

**后果**：旧版 §5.5/§5.8.4 描述的"提交 `record` 时按 `ColumnName` 投影到库"**未实现**。列改名后把 `ColumnName` 改成新列名，表单控件 key 不变——但落库仍按 `Field` 走，改名实际失效。**规避**：不要让 `ColumnName` 与 `Field` 不一致。

#### 4.5 【中】资源列对齐校验 fail-open

**现象**：绑定资源的表单，即使字段完全对不上资源列，发布也可能成功。

**机理**：`ValidateAgainstResourceAsync`（`FormDefinitionAppService.cs:328-358`）有三处**直接 `return`（放行）**：

- `SourceResourceCode` 为空（`:330-333`）→ 不校验（合理）；
- **资源查不到**（`:335-339`）→ 静默放行；
- **资源没有任何列**（`CollectResourceColumns` 返回空，`:341-345`）→ 静默放行。

另外校验**跳过明细字段**（`:348` 的 `Where(f => f.Children is not { Count: > 0 })`），即子表列名映射完全不管。

**后果**：绑定关系写错（资源被删/改名）时，表单仍能发布，错误推迟到运行时的静默丢数据。**规避**：发布后人工核对 `SourceResourceCode` 是否存在。

#### 4.6 【中】`manager` 可解析出**发起人本人** → 可自审批

**现象**：发起人自己恰好是所在组织单元里持 `manager` 角色的成员时，主管解析返回**发起人自己**，节点待办落回发起人。

**机理**：`WorkflowOrgResolver.cs:64-72` 遍历 OU 成员，命中角色即 `return member.UserName`（`:70`），**全程未与入参 `userName` 比对**。

**后果**：审批链失去制衡。**规避**：组织架构上确保发起人不是本部门 `manager` 角色成员。（此条同时记录在 [`oa-contract.md`](./oa-contract.md) §4.6——OA 侧文档为主，此处仅留存根。）

#### 4.7 【中】字段权限矩阵 fail-open 的两处

见 [`oa-contract.md`](./oa-contract.md) §4.5（主文档）。摘要：**未声明权限的字段默认放行**（`WorkflowRuntimeAppService.cs:983-986`）；**模式级 baseline 层不存在**——旧版 §5.8.3 设计的"模式级 baseline ∩ 节点级 ∪ 按钮白名单"三层叠加，代码里只有节点级一层。

#### 4.8 【高】驳回被静默当成通过（会签/比例节点）

见 [`oa-contract.md`](./oa-contract.md) §4.1（主文档，含完整机理与行号）。摘要：`WorkflowRuntimeAppService.cs:274` 的 `!input.Pass` 分支内层 `if (ShouldRejectNode(...))`（`:276`）在 `all`/`sequential`/`single` 下恒为 true、在 `any`/`ratio` 下可能为 false；为 false 时**不 return**，落到 `task.Approve(...)`（`:299`）并记 `approve` 轨迹（`:301`）。

#### 4.9 【高】审批表单补丁在异常路径上静默丢失

见 [`oa-contract.md`](./oa-contract.md) §4.9（主文档）。摘要：补丁先写内存（`:161`），Mongo 无变更跟踪且 `TransactionBehavior=Disabled`（`SaaSMongoDbModule.cs:27-28`），任何在 `_instances.UpdateAsync` 之前的 `throw`（`:293` 意见必填、`:297` `EnforceRequiredFields`、`:551` 无办理人、`:630` 未知办理人类型）都会让本次补丁与轨迹整段丢失。

#### 4.10 【中】删除引用检查的两个盲点

**现象**：`DeleteAsync`（`FormDefinitionAppService.cs:134-156`）确实做了引用检查，但：

1. **先查后删非原子**：`AnyAsync`/`GetListAsync`（`:138`/`:143`/`:149`）与 `DeleteAsync`（`:155`）之间无事务，`SaaSMongoDbModule.cs:27-28` 又显式关闭了 UoW 事务；并发下仍可删掉刚被引用的表单。
2. **`ListView.Actions[].FormKey` 不是表单引用**：`AppResource.cs:159`/`:165`/`:171`/`:280` 生成动作时把 `FormKey` 写成 **`"create"`/`"update"`/`"detail"`**（模式名，配合 `FormMode`），并不是表单库 `Code`。因此 `:150` 那条比对**永远不会命中**——既漏检真实的资源内联表单（`AppResource.Form`，`AppResource.cs:53`），又在语义上过度宣称。
3. **`ReportDefinition.Actions[].FormKey` 才是真引用**（报表动作指向表单库 Code），这条检查有效（`:143-147`）。
4. `GetListAsync()` 把**全表**工作流/报表/资源拉进内存再 `Any`（`:138` 是 `AnyAsync`，`:143`/`:149` 是全量加载），大租户下是明显的性能问题。

**后果**：`FormKey` 那条检查给出虚假的安全感；真实存在的资源内联表单绑定**未被保护**。**规避**：不要依赖该检查保护 `AppResource.Form`。

#### 4.11 【中】`resync-catalog` 只刷 `FieldCatalog`，不重推 `Schema.Fields`

**现象**：`POST /api/orchestration/forms/{id}/resync-catalog`（`AppBuilderControllers.cs:273-276`）的注释与文档旧版称"重新对齐字段目录与拍平字段"，实际**只替换 `FieldCatalog`**。

**机理**：`ResyncCatalogFromResourceAsync`（`FormDefinitionAppService.cs:182-206`）构造 `UpdateDraft(entity.Name, entity.Description, entity.SourceResourceCode, entity.Schema, resource.ListView.Columns.ToList())`（`:197-203`）——第 4 个参数传的是**原样的 `entity.Schema`**，只有第 5 参数 `fieldCatalog` 换了。代码注释亦自认"已有编辑过的 Schema 不覆盖"（`:196`）。

**后果**：资源表结构变更后，`Schema.Fields` **不会**跟着对齐；如果期望的是"重新同步到可发布状态"，这个端点做不到。**规避**：资源列变更后需在表单设计器里手工重配字段，再发布。

#### 4.12 【高/中】已发布定义的三个绕过口

| # | 现象 | 机理 | `file:line` |
|--:|------|------|-------------|
| ① 【高】 | 已发布审批流可被 `PUT` 原地改写，**不重新校验** | `WorkflowDefinitionAppService.UpdateAsync` 只调 `EnsureFormRefAsync` 再 `UpdateDraft`，不判断 `Status`、不调 `WorkflowDefinitionValidator` | `WorkflowDefinitionAppService.cs:108-127` vs 校验唯一入口 `:154` |
| ② 【高】 | **无出线节点仍 fail-open** | `ChooseNext` 在无出线时返回 `(null, false)`；`EnterAsync` 的 `while` 收到 null 退出，落到 `Complete("approved")` | `WorkflowRuntimeAppService.cs:443-446` → `:386` / `:433` |
| ③ 【中】 | 优先级/默认分支语义不完整 | 全 `Priority=0` 时 `OrderBy` 稳定排序＝**数组顺序**；`IsDefault` 在校验器里**完全没解析**；优先级查重只查 `Priority>0` | `WorkflowRuntimeAppService.cs:462-475`；`WorkflowDefinitionValidator.cs:156-178` |

①②组合即为最危险路径：先正常发布，再 `PUT` 删掉某节点全部出线；此后新实例走到该节点即**无人审批「已通过」**。详见 [`oa-contract.md`](./oa-contract.md) §4.2。

#### 4.13 【高】并发无保护

见 [`oa-contract.md`](./oa-contract.md) §4.10。摘要：`WorkflowInstance.cs:10` 与 `WorkflowTask.cs:10` 均**无 `ConcurrencyStamp`**；`AppendHistory`（`WorkflowInstance.cs:76-85`）是 JSON 读改写；`CompleteAsync` 的状态检查（`:134`）与变更（`:299-300`）之间无互斥 → 双重推进、轨迹互相覆盖。

#### 4.14 【低】`FormFieldDef` 与 `FormWidgetDef` 仍是两个近乎重复的类（F3 未真正合并）

**现象**：旧版 §8.5.1 声称 F3 "保留双模型但消除漏拷贝根因：两类的字段集按 F2 对齐为同一超集语义"。**"消除根因"不成立**——两类仍是各自独立声明的类，属性逐条重复书写，只是**本轮把缺失的属性补齐了**（所以漏拷贝暂时不复发）。

**机理**：两类的字段集现状（已逐条比对）：

| 属性组 | `FormFieldDef`（`:317-375`） | `FormWidgetDef`（`:381-442`） | 是否对齐 |
|--------|:---:|:---:|:---:|
| `Field` / `Title` / `Control` / `Span` / `Placeholder` | ✅ `:319`/`:327`/`:334`/`:337`/`:339` | ✅ `:400`/`:394`/`:405`/`:407`/`:409` | ✅ |
| `ColumnName` | ✅ `:325` | ✅ `:403` | ✅ |
| `VisibleOnCreate`/`Update`/`Detail` | ✅ `:341-345` | ✅ `:411-415` | ✅ |
| `ReadonlyOnCreate`/`Update` | ✅ `:347-349` | ✅ `:417-419` | ✅ |
| `RequiredOnCreate`/`Update` | ✅ `:351-353` | ✅ `:421-423` | ✅ |
| `Rules`（单对象） | ✅ `:356` | ✅ `:426` | ✅ |
| `Dependencies` / `ControlProps` | ✅ `:359`/`:362` | ✅ `:438`/`:441` | ✅ |
| `Children` | ✅ `:365`（`List<FormFieldDef>`） | ✅ `:398`（`List<FormWidgetDef>`） | 元素类型不同 |
| `MinItems`/`MaxItems`/`TreeChildrenField` | ✅ `:368`/`:371`/`:374` | ✅ `:429`/`:432`/`:435` | ✅ |
| `Id` / `Kind` / `Container` / `Columns` / `Hidden` | ❌ 无 | ✅ `:383`/`:386`/`:389`/`:392`/`:396` | 仅布局侧需要 |
| `ReadonlyOnDetail` / `RequiredOnDetail` | ❌ 两侧都无 | ❌ | — |

**真正未解决的是"三处同改"**：新增一个字段属性时，仍需同时修改 `BuildField`（`FormDefinitionAppService.cs:275-297`）、`CloneWidget`（`:426-454`）、`CloneField`（`:456-479`）**三处**——F2 的根因（每加属性改三处、必然漏）**并未消除**，只是当前属性集已补齐、暂时不复发。

> **更正**：本轮初稿曾断言"`FormWidgetDef` 少了 `VisibleOnDetail`，因此明细可见性无法在拍平结果中表达"——**该断言错误**。`FormWidgetDef.VisibleOnDetail` 存在于 `ResourceSchema.cs:415`，且 `BuildField` 会透传（`FormDefinitionAppService.cs:286`）。文档不能留下这种"看起来精确"的错误结论，故在此显式更正。

#### 4.15 【低】`Rules` 是单对象而非数组

**现象**：旧版 §5.1/§5.1.1 通篇按 `List<FormFieldRuleDef> Rules` 设计（"规则数组"、"每字段挂 `validate: [{...}]`"），代码里 `FormFieldDef.Rules` 与 `FormWidgetDef.Rules` 都是**单个可空对象**（`ResourceSchema.cs:356`、`:426`）。

**机理**：一个字段只能挂一条"长度+正则+数值范围"组合规则（`FormFieldRuleDef` 内部已有 7 个字段，勉强够用），无法表达"多条独立规则各自带自己的 message/severity"。

**后果**：文档与代码的契约不一致；按文档实现的规则数组会被**静默丢弃**（`FormWidgetDef.Rules` 反序列化时多余数组元素报错或忽略，取决于序列化器行为）。**规避**：按单对象契约对接。

#### 4.16 【低】`Control`/`Op`/`Action`/`Container` 均为自由字符串，无枚举校验

`FormFieldDef.Control`（`:334`）、`FormFieldDependencyDef.Op`/`Action`（`:308`/`:312`）、`FormWidgetDef.Container`（`:389`）都没有枚举或白名单校验。发布校验 `ValidateFieldNames`（`FormDefinitionAppService.cs:299-325`）只查字段名唯一，不查这些。**后果**：拼错控件名不会在任何环节报错，只有前端渲染出空白。对比 `WorkflowDefinitionValidator` 对节点类型/办理人规则/多人规则都做了枚举校验（`WorkflowDefinitionValidator.cs:17-43`、`:183-207`）——**表单侧的校验强度明显弱于 OA 侧**。

---

## 5. 规划（未实现）—— 当前不可用

> 本章与 §3 严格隔离。以下能力在 `267d375` 上**不存在**，不要按此配置或对接。
> **注意**：§5.1–§5.6 是**协议设计稿**（保留原设计论证，供实施时参考），不是契约。§5.7、§5.8 是**交互/按钮设计稿**。两者都未落地。

### 5.1 字段校验规则协议（含双层校验模型）❌

**现状不足**：`FormFieldDef` 有 `RequiredOnCreate/Update/Detail` 与 `Dependencies`，但缺统一的 `rules` 协议；表单侧没有与编排一致的规则 DSL，后端也**不求值**（§4.3、F-14）。对比参考：[form-create](https://www.form-create.com/designer/) 每字段挂 `validate: [{ required, message, pattern, min, max }]`；[Formily](https://github.com/alibaba/formily) 用 `x-validator` 支持内置规则 + 自定义函数名；Ant Design Vue `rules` 也是这套。

**协议设计稿（补入 `FormFieldDef`，与现有单对象 `Rules` 二选一后替换）**：

```csharp
public class FormFieldRuleDef
{
    // required | pattern | min | max | len | enum | validator | asyncValidator
    public string Kind { get; set; } = "required";

    // —— 声明式参数 ——
    public string? Pattern { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public int? Len { get; set; }
    public List<string>? Enum { get; set; }

    /// 前端 JS 灵活校验函数体（Kind=validator/asyncValidator 时用）
    /// 签名：(value, row, ctx) => boolean | string | {valid,message} | Promise<同左>
    public string? Validator { get; set; }

    /// 该规则依赖的其它字段名；任一变化时自动重校验本字段
    public List<string> Deps { get; set; } = [];

    public string Message { get; set; } = "";
    public string Trigger { get; set; } = "blur";    // change | blur | submit
    public string Severity { get; set; } = "error";  // error | warning
}
public List<FormFieldRuleDef> Rules { get; set; } = [];

/// 提交时权威校验流（可选；create/update 流自带校验时可不填）
/// 入参 record，出参 { valid, errors:[{field,message}] }
public string? ValidateFlowKey { get; set; }
```

**双层校验模型（原则：前端灵活 + 后端权威）**：

| 层 | 目的 | 形态 | 信任度 |
|----|------|------|--------|
| **前端层** | 即时反馈、用户体验 | 声明式规则 + JS 函数体（跨字段、异步、联动清空） | 不可信（用户可绕，等于绕自己） |
| **后端层** | 防绕过、权威 | 提交时走逻辑编排 `create/update` 流的 Code/Condition 节点；可选 `validateFlowKey` 试提交 | 唯一可信 |

> 对照：form-create `validate: [{required}, {validator:(rule,val,cb)=>cb()}]`；Formily `x-validator` 支持函数 + 字符串函数名；Ant Design Vue `rules.validator: async (rule,val)=>true|string`；VForm3 `onValidate` 配置扩展。**成熟方案都给前端 JS 灵活度，权威校验在后端。** 本协议对齐此分工。

**前端 `ctx` 校验上下文（只读快照，沙箱隔离）**：

| 字段 | 说明 |
|------|------|
| `ctx.fields` | 当前表单所有字段值（跨字段校验用） |
| `ctx.sys` | `{ userId, tenantId, now, culture }` 只读快照 |
| `ctx.dict(code)` | 取字典项（与列表 `transform.dict` 同源） |
| `ctx.t(key)` | i18n 文案 |
| `ctx.formRef` | 当前表单实例引用（调控件 API，如清空/聚焦） |

**禁止**：直连 DB；`document/window/storage` 沙箱隔离；`eval/new Function` 由运行时统一注入受控（不开放给设计器用户直接写）。

**前端 JS 校验示例（设计稿）**：

```js
// 1. 简单长度+复杂度
(value, row, ctx) => {
  if (!value) return true;                       // 非必填交给 required 规则
  if (value.length < 8) return "密码至少 8 位";
  if (!/[A-Z]/.test(value)) return "需含大写字母";
  return true;
}

// 2. 跨字段：金额不超过预算
(value, row, ctx) => value <= ctx.fields.Budget ? true : `不能超过预算 ${ctx.fields.Budget}`

// 3. 严重级 warning：超 1 万提示但允许提交
(value, row, ctx) => value > 10000 ? "超 1 万，需走审批" : true

// 4. 异步查重（调 validateFlowKey，不直连 DB）
async (value, row, ctx) => {
  const r = await fetch(`/api/logic/user.checkName?name=${encodeURIComponent(value)}`);
  const { valid, message } = await r.json();
  return valid ? true : message;
}
// 注：user.checkName 须 isReusable + visibleTo 受控 + traceMode=errors

// 5. 调 ctx.formRef 联动清空
(value, row, ctx) => {
  if (value === 'reset') { ctx.formRef.setFieldValue('City', null); }
  return true;
}
```

**后端权威校验（提交时必跑，设计稿）**：

```
表单提交
  → 前端 Rules 全过（含 async）        ← 体验层，可被绕过
  → POST /api/logic/{createFlowKey}     默认 sys.resource.create 或自定义 order.create
       流内 Code/Condition 节点做权威校验：
         · 业务规则（订单金额上限、库存是否够）
         · 查库唯一性（订单号不能重复）
         · 跨表一致性（客户状态有效）
         · 权限二次确认（当前用户能否改此单）
       失败 → throw UserFriendlyException → 前端 toast + 字段定位
  → 校验通过 → ResourceCreate 写库
```

**安全边界（关键，保留原论证）**：

| 关注点 | 前端 | 后端 |
|--------|------|------|
| 信任度 | 不可信（浏览器用户可控） | 唯一可信 |
| 绕过后果 | 用户绕自己，无漏洞 | 漏洞，必须防 |
| JS 执行 | 自由写函数体，沙箱可选（防意外 DOM 污染，不防恶意） | 不跑用户 JS，只跑编排 Code 节点（白名单+参数化） |
| 查库 | 只调 `validateFlowKey`（受控流），不直连 DB | 编排节点 `db.*`，白名单 DS |
| 防滥用 | 异步 rule 限流（同字段 change 内只跑一次） | `traceMode=errors` + `visibleTo` 受控 |

### 5.2 高级联动：级联取数 / 异步选项 / 表达式计算 ❌

**现状不足**：`dependencies` 只做 show/hide/enable/disable/require/setValue（且后端不求值），常见的"改省份自动拉城市""改客户自动带出信用额度""金额=单价×数量"都没有协议。

**对照**：Formily `x-reactions` 用 `fulfill.state` + `fulfill.run`（受控表达式）；VForm3 `eventFunc` 在 onChange 触发；form-create `control` 触发其它字段显隐/赋值。钉钉审批有"公式字段"。

**协议设计稿（扩展 `Action` 枚举与 `SetValue` 语义）**：

| 新 Action | 含义 | SetValue 形态 |
|-----------|------|---------------|
| `loadOptions` | 改 A 后异步拉 B 的选项（如城市） | `flowKey` + 输出映射到 B 的 `options` |
| `fetchValue` | 改 A 后查库回填 B（如信用额度） | `flowKey`，入参取 A，出参写 B |
| `compute` | 表达式计算（金额=单价×数量） | 前端 JS 表达式（与 §5.1 校验同沙箱），**写库以 `create/update` 流 Code 节点重算为准** |
| `cascade` | 级联清空依赖项（改省时清空市） | 目标字段名数组 |

**约束**：`loadOptions`/`fetchValue` 必须挂已发布 `flowKey`，不在前端跑任意查询；`compute` 可在前端用 JS 表达式，**但写库值以 `create/update` 流 Code 节点重算为准**——前端 `compute` 仅作展示，后端不信任前端算的值；旧"白名单解析器禁止 eval"方案废弃，改为双层信任（前端灵活 + 后端权威）。

### 5.3 子表 / 明细的行级权限与行联动 ❌

**现状不足**：`table`/`list` 控件已支持，但缺①行级字段权限（与节点 `FieldPermissions` 类似但按行）、②行级联动（改某行单价时该行金额重算）、③行数量上下限的**运行时强制**（模型有 `MinItems`/`MaxItems`，见 `ResourceSchema.cs:368`/`:371`，但后端不校验）、④行必填校验。

**对照**：钉钉明细表支持行级公式、行级必填、行数量限制；VForm3 子表 `subForm` 支持行级联动。

**协议设计稿（`table` 控件的 `ControlProps` 扩展）**：

```json
{
  "control": "table",
  "controlProps": {
    "minRows": 0, "maxRows": 20,
    "rowRules": [ { "field": "Amount", "kind": "required", "when": "Qty > 0" } ],
    "rowCompute": [ { "field": "Amount", "expr": "Price * Qty" } ],
    "addColumnFlowKey": "order.addLine"
  }
}
```

行级联动复用 §5.2 的 `compute`/`fetchValue`，作用域限当前行。

### 5.4 表单版本与发布快照的**完整**方案 ⚠️ 半成品

**已落地的一半**：`PublishedSchema` + `SchemaVersion` + `GetEffectiveSchema()`（§3.3）。
**未落地的一半**：`WorkflowInstance.FormSnapshotJson` —— 该字段**不存在**（`WorkflowInstance.cs` 只有 `ProcessSnapshotJson` 与 `FormSchemaVersion`）；历史版本列表不存在。对照参考：Flowable 表单有 `FormVersion`；飞书审批模板有版本号；Formily Schema 带 `version`。

**协议设计稿（补齐后半）**：

- OA `WorkflowInstance` 启动时除 `ProcessSnapshotJson` 外，再固化 `FormSnapshotJson = form.GetEffectiveSchema()`（与流程快照同生命周期）；
- 运行时渲染/校验**只读** `FormSnapshotJson`，草稿编辑不影响在途单；
- `FormDefinition` 保留历史发布版本列表（而非单份覆盖），使 `FormSchemaVersion` 可回放；
- 与 [`data-collection-contract.md`](./data-collection-contract.md) §5.13"资源发布运行时只读已发布 Schema"对齐。

### 5.5 表单与资源的字段映射契约（`ColumnName` 的**运行时**部分）⚠️ 半成品

**已落地的一半**：`FormFieldDef.ColumnName` / `FormWidgetDef.ColumnName` 属性 + 发布时的对齐校验（§3.1、F-9）。
**未落地的一半**：**提交 `record` 时按 `ColumnName` 投影到库**。`ResourceCrudExecutor` 没有任何按 `ColumnName` 投影的代码（§4.4）。

**协议设计稿**：提交 `record` 时按 `ColumnName`（空则 `Field`）投影到库列；列改名后只需改 `ColumnName`，不用改表单控件 key。

### 5.6 多语言与布局预设 ❌

**现状不足**：`Title`/`Placeholder` 是单语言 `string`（`ResourceSchema.cs:327`/`:339`），无 i18n 结构；布局无紧凑/宽松预设。

**协议设计稿**：`title`/`placeholder`/`description` 支持 `i18n: { "zh-CN": "...", "en-US": "..." }`，运行时按 `sys.culture` 取；`FormDef.layoutPreset: compact | default | spacious`。

### 5.7 表单设计器交互与动态按钮配置 ❌

> 本节是**前端交互设计稿**（`vue-demo`），本仓完全无法核验。落地优先级见 §5.7.8。

#### 5.7.1 表单设计器交互不足清单

| 不足 | 现状 | 对照参考 | 补法 |
|------|------|----------|------|
| 撤销/重做 | 缺 | VForm3/form-create 均支持 | 画布操作栈，Ctrl+Z/Y，存 50 步 |
| 组件复制/粘贴/批量编辑 | 缺 | VForm3 支持多选+批量改 props | Ctrl+C/V；多选后右键"批量改 required/宽度" |
| 模板字段库 | 缺 | 钉钉"常用字段"预设 | 左组件库增"常用"分组：手机号/身份证/金额/地址/日期范围，一键插入带预设校验 |
| 结构大纲树 | 缺 | VForm3 大纲视图 | 左侧增"结构"Tab，树形显示容器/字段，点击定位+拖拽排序 |
| 真机预览 | 缺 | 钉钉手机预览 | 顶部"预览"按钮，弹窗选 PC/手机宽度，实时渲染已发布 Schema |
| 撤回发布 | 缺 | — | 草稿可"另存为新版本"，发布后可"回滚到上一版本"（不动在途实例快照） |

#### 5.7.2 字段配置面板协议（右栏，分层）

设计器右栏配置面板分两层：**通用项**（所有控件共享）+ **特有项**（按 `control` 类型）。

**通用项（`FormCommonProps`，所有控件）**：

| 分组 | 字段 | 说明 |
|------|------|------|
| 基本 | `key` | 字段名（提交 key，唯一） |
| 基本 | `columnName` | 库列映射（§5.5；**属性存在但运行时无消费**） |
| 基本 | `label`/`labelI18n` | 标题/多语言 |
| 基本 | `placeholder`/`help`/`tooltip` | 占位/帮助/提示气泡 |
| 校验 | `rules` | §5.1 规则（**当前是单对象，非数组**） |
| 校验 | `requiredOnCreate/Update/Detail` | 节点级必填快捷（**注意：无 `RequiredOnDetail` 属性**） |
| 布局 | `span` | 栅格占列（1-24），默认 12 |
| 布局 | `labelWidth`/`labelPosition` | 标签宽/位置（top/left） |
| 布局 | `offset`/`push`/`pull` | 栅格偏移 |
| 联动 | `dependencies` | §5.2 显隐/赋值/级联 |
| 联动 | `defaultValue`/`defaultValueExpr` | 创建默认值（字面量或 JS 表达式，如 `ctx.sys.now`） |
| 权限 | `fieldPermissions` | 按节点 read/write/hide/required（§3.5） |
| 高级 | `customClass`/`style` | 自定义类名/样式（白名单） |

**特有项（按 control，示例）**：

| control | 特有项 |
|---------|--------|
| `select`/`cascader`/`radio` | `options`(静态) / `dictCode`(字典) / `optionsFlowKey`(远程) / `labelKey`/`valueKey`/`multiple`/`filterable` |
| `date`/`datetime` | `format`/`valueFormat`/`disabledDate`/`shortcuts`(今天/本周/近30天) |
| `number` | `min`/`max`/`step`/`precision`/`unit`(元/个/%) |
| `upload` | `accept`/`maxSize`/`maxCount`/`multiple`/`uploadFlowKey`(自定义上传流) |
| `userPicker`/`deptPicker` | `multiple`/`range`(本部门/全租户) / `valueType`(id/name) |
| `table`(子表) | `minRows`/`maxRows`/`rowRules`/`rowCompute`/`addColumnFlowKey`(§5.3) |

#### 5.7.3 布局与容器配置细项

| 容器 | 配置项 |
|------|--------|
| `grid` 栅格 | `gutter`(间距) / `columns: [{span,offset}]` |
| `card` 卡片 | `title`/`bordered`/`collapsible`/`defaultCollapsed` |
| `tabs` 标签页 | `tabs:[{name,fields}]` / `type`(line/card) / `closable` |
| `collapse` 折叠面板 | `panels:[{name,fields,defaultActive}]` |
| `divider` 分割线 | `title`/`dashed`/`position` |

**嵌套规则**：容器可嵌套容器（card→grid→table），最多 3 层防性能问题；`table` 子表内只允许字段不允许容器；设计器拖拽时校验嵌套合法性并提示。

> 后端现状：`FormWidgetDef` 只有 `Columns`（栅格列数，`:392`），**没有** `gutter`/`panels`/`collapse`/`divider` 的结构化属性——这些只能塞 `ControlProps` 或留在 `VformJson`。

#### 5.7.4 数据源与字典统一管理

**现状不足**：`select`/`cascader` 选项来源零散，有的硬编码、有的调 `flowKey`、有的查字典，无统一协议。

**协议**：选项来源三选一，优先级 `dictCode` > `optionsFlowKey` > `options`：

- `dictCode`：挂字典码，前端缓存 5 分钟，后端 `SettingManagement` 维护（与报表 `transform.dict` 同源）；
- `optionsFlowKey`：挂已发布流，入参 `{ parentValue? }`（级联用），出参 `[{label,value,disabled?}]`，`isReusable`+`visibleTo` 受控，前端按 `parentValue` 缓存；
- `options`：静态数组，适合性别这类固定枚举。

**字典管理**：管理端增"字典管理"菜单（`sys_dict` + `sys_dict_item`），支持树形字典（省市区）；表单设计器选项面板可"从字典选"下拉。

> 后端现状：`ResourceSchema.cs` 里有可复用字典模型 `NamedDict`/`DictOption`（`:56-73`），但**挂在 `ListViewDef.Dictionaries`（`:284`）上，是列表/报表用的**，与表单字段**没有绑定关系**。

#### 5.7.5 动态化按钮配置协议（`ButtonDef`，核心）❌

**现状不足**：表单底部按钮（提交/保存/暂存）、列表 action 按钮、审批节点按钮各自硬编码，无统一动态配置协议。钉钉/飞书审批的按钮是按节点 + 权限 + 流程状态动态渲染的。

> **关键事实**：全仓 grep **无 `ButtonDef`**。表单侧没有按钮模型；列表/报表侧现有 `ActionDef`（`ResourceSchema.cs:227-272`），字段集与下方设计稿**不对齐**（无 `VisibleExpr`/`DisabledExpr`/`BeforeExpr`/`AfterExpr`/`FlowKey` 之外的动态条件）。

**统一协议设计稿**（补入 `FormDef` / `AppResource.ListView` / `WfProcessNode`）：

```csharp
public class ButtonDef
{
    public string Key { get; set; } = "";          // 唯一，如 submit/save/approve/reject
    public string Label { get; set; } = "";        // 文案（支持 i18n）
    public string? LabelI18n { get; set; }
    public string? Icon { get; set; }
    public string Type { get; set; } = "default";  // primary|default|dashed|danger|link
    public string Scene { get; set; } = "form";    // form|toolbar|row|node
    public string? Permission { get; set; }        // 权限码，如 Order.Approve
    public string? FlowKey { get; set; }           // 点击调用的已发布流
    public string? ValidateFlowKey { get; set; }   // 点击前试校验流（可选，§5.1）
    public string? BeforeExpr { get; set; }        // 前端 JS：点击前钩子，返回 false 取消
    public string? AfterExpr { get; set; }         // 前端 JS：成功后（跳转/刷新/发消息）
    public string? VisibleExpr { get; set; }       // 前端 JS：可见条件
    public string? DisabledExpr { get; set; }      // 前端 JS：禁用条件
    public string? Confirm { get; set; }           // 二次确认文案
    public int? BatchLimit { get; set; }           // 单批上限
    public string? Redirect { get; set; }          // 成功后跳转路由
    public bool RefreshAfter { get; set; } = true; // 成功后刷新
}
public List<ButtonDef> Buttons { get; set; } = [];
```

**按钮三类来源的统一（设计稿）**：

| 场景 | 挂载点 | 示例 |
|------|--------|------|
| 表单底部 | `FormDef.Buttons` | 提交(save→create流)/暂存(draft)/取消 |
| 列表工具栏/行内 | `AppResource.ListView.Buttons`（与报表 `ActionDef` 合流） | 新增/导出/批量删除/行内编辑 |
| 审批节点 | `WfProcessNode.Buttons`（覆盖默认 approve/reject） | 自定义"加签""委办""退回上级" |

**关键：审批按钮也走 `ButtonDef`**。当前 `approve/reject_*` 是硬编码动作，改为节点配 `Buttons: [{Key:"approve",FlowKey:"order.afterApprove",...},{Key:"reject_to_prev",...}]`；缺省时节点按 `multi`+`assigneeType` 自动生成默认按钮。

#### 5.7.6 按钮事件流（before → flow → after）

```
用户点按钮
  → BeforeExpr（前端 JS，可选）
       · 二次确认（Confirm 非空则弹框）
       · 数据预处理（如组装 recordPatch）
       · 调 ValidateFlowKey 试校验（可选）
       · 返回 false 取消点击
  → POST /api/logic/{FlowKey}            后端权威，必跑
       · 入参：表单 record + 按钮 payload + ctx（instanceId/nodeId 若审批）
       · 流内 Code/Condition 节点：权限二次确认 + 业务校验 + 写库/审批推进
       · 失败 → throw → 前端 toast
       · 成功 → 返回 { success, data, redirect? }
  → AfterExpr（前端 JS，可选）
       · 跳转 Redirect / 刷新 RefreshAfter / 发消息 / 关闭弹窗
```

**与 §5.1 双层校验对齐**：`BeforeExpr` 是体验层（可绕），`FlowKey` 内 Code 节点是权威层（不可绕）；`ValidateFlowKey` 是按钮级的"试提交"。

#### 5.7.7 按钮与权限/状态/节点的动态联动

| 联动维度 | 实现（设计稿） |
|----------|------|
| 按角色显隐 | `Permission` 权限码 + `VisibleExpr`（`ctx.sys.roles.includes('admin')`） |
| 按表单状态显隐 | `VisibleExpr` 读 `ctx.record.Status` |
| 按审批节点显隐 | 审批按钮挂在 `WfProcessNode.Buttons`，仅当前节点渲染 |
| 按数据状态禁用 | `DisabledExpr`（库存为 0 时禁用"出库"按钮） |
| 按选择数动态 | 列表 `BatchLimit` + `DisabledExpr`（未选行时禁用批量按钮） |
| 按钮顺序 | `Buttons` 数组顺序即渲染顺序 |

**对照钉钉/飞书**：钉钉审批按钮按节点 + 角色动态渲染（审批人见"同意/拒绝/转办"，发起人见"撤回"，抄送人无按钮）；飞书列表 action 按状态显隐。本协议用 `VisibleExpr`/`DisabledExpr`/`Permission` 三件套覆盖这些场景，权威判断在后端 `FlowKey` 内 Code 节点。

#### 5.7.8 落地优先级

| 能力 | 优先级 |
|------|--------|
| §5.7.2 字段配置面板通用项/特有项分层 | P1（设计器可用性基础） |
| §5.7.5 ButtonDef 协议 + 表单底部/审批按钮统一 | P1（动态化核心） |
| §5.7.6 按钮 before→flow→after 事件流 | P1 |
| §5.7.4 字典统一管理（dictCode/optionsFlowKey/options） | P1 |
| §5.7.7 按钮权限/状态/节点联动 | P1 |
| §5.7.1 撤销重做/模板字段库/大纲树 | P2（体验增强） |
| §5.7.3 布局容器配置细项 | P2 |

### 5.8 报表按钮与表单关联的缺陷与补充（`ActionDef` 与 `ButtonDef` 割裂）❌

> §5.7.5 把 `ButtonDef` 统一到 `FormDef.Buttons` / `ListView.Buttons` / `WfProcessNode.Buttons`。但 [`report-contract.md`](./report-contract.md) §6 的 `ActionDef` 字段（`scene`/`scope`/`open`/`fieldsMode`/`payload`/`after`/`batchLimit`）与 `ButtonDef` 字段**并未真正对齐**，且"按钮打开表单"这条主链路有多处缺陷。本节保留为设计稿。

#### 5.8.1 缺陷清单（12 项，设计侧）

| # | 缺陷 | 影响 |
|---|------|------|
| 1 | `ActionDef` 无 `formKey`/`formMode` 的**多表单**语义 | 一个资源多表单（紧凑列表/完整编辑/只读详情）无法按场景选 |
| 2 | 表单模式（create/edit/view/audit）未协议化 | 非 OA 场景缺"模式级字段权限"，纯 CRUD 无法按模式控制只读/必填 |
| 3 | 行内"编辑"取了行 record，但如何注入表单 `initialValues` 未定义 | 数据回填靠隐式约定，改名即断 |
| 4 | `ActionDef.after` 与 `ButtonDef.AfterExpr`/`RefreshAfter` 语义重复且不一致 | 两套"提交后行为"协议，实现易分叉 |
| 5 | 报表按钮 payload（如"基于当前行新建"继承父值）无法注入表单默认值 | 缺 `payloadToFormFields` 映射 |
| 6 | 报表 `ActionDef.Permission` 与表单 `ButtonDef.Permission` 权限链未定义顺序 | 打开表单后按钮权限可能与报表按钮冲突 |
| 7 | 行内编辑（cell scope + 编辑控件）与弹窗表单校验割裂 | 行内编辑是否走表单 `rules` 未定，校验双标 |
| 8 | 批量编辑（选 N 行弹表单填公共字段批量更新）缺协议 | `batchLimit` 有限流无"批量编辑表单"形态 |
| 9 | "编辑"前预校验（如已审批不能改）缺 `canOpenExpr`/`beforeExpr` | 不可编辑的行点了才报错，体验差 |
| 10 | 主从表单（订单+明细）详情打开时子表数据回填未协议化 | 缺 `detailFlowKey` 取完整 record（含子表） |
| 11 | 报表列 `fieldPermissions` 与表单字段 `fieldPermissions` 继承关系未定 | OA 场景打开表单后字段权限是否继承当前节点不清 |
| 12 | `ActionDef` 与 `ButtonDef` 字段不对齐，前端两套渲染逻辑 | 维护成本高，行为不一致 |

> **注**：`ActionDef` 现状（`ResourceSchema.cs:227-272`）已有 `FormKey`/`FormMode`/`FieldsMode`/`Fields`/`PayloadFields`/`After`/`Confirm`/`BatchLimit`/`MultiSelect`——第 1、2 项已**部分**存在（`FormKey`+`FormMode` 属性在，见 `:247`/`:249`），但**运行时是否消费未在本仓核验**（资源运行时的表单打开逻辑在 `ResourceCrudExecutor`/前端）。

#### 5.8.2 统一协议：`ActionDef` ⊂ `ButtonDef`（合流，设计稿）

**决策**：`ActionDef` 不再独立，改为 `ButtonDef` 的"列表/报表场景特化"。`ButtonDef` 增报表相关字段，`ActionDef` 保留为别名兼容：

```csharp
// —— 报表/列表场景补充（原 ActionDef 字段合流）——
public string? Scope { get; set; }          // none|row|selection|cell|column
public string? Open { get; set; }           // none|modal|drawer|page|link|inline
public string? FormKey { get; set; }        // 打开的表单（缺省=资源默认 form）
public string? FormMode { get; set; }       // create|edit|view|audit
public string? FieldsMode { get; set; }     // full|whitelist|none
public List<string>? FieldsWhitelist { get; set; }
public string? PayloadToFields { get; set; } // payload → 表单 defaultValue 映射
public string? DetailFlowKey { get; set; }   // 打开详情时取完整 record（含子表）的流
public string? CanOpenExpr { get; set; }     // 打开前预校验，前端 JS
public string? RefreshScene { get; set; }    // list|detail|none
public bool CloseAfterSubmit { get; set; } = true;
```

**对应关系**：原 `ActionDef.after` → `ButtonDef.AfterExpr` + `RefreshScene` + `CloseAfterSubmit`；原 `ActionDef.fieldsMode` → `ButtonDef.FieldsMode` + `FieldsWhitelist`；原 `ActionDef.batchLimit` → `ButtonDef.BatchLimit`。废弃 `ActionDef` 独立类，保留别名兼容期。

#### 5.8.3 表单模式与模式级字段权限（设计稿）

**协议**：`FormMode: create | edit | view | audit`（audit = OA 审批节点办理）。表单字段权限三层叠加：

```
最终字段权限 = 模式级 baseline(FieldPermissions by mode)
              ∩ OA 节点级(若 audit 模式，WfProcessNode.FieldPermissions)
              ∪ 显式覆盖(按钮 FieldsWhitelist)
```

| 模式 | baseline 默认（设计） | 按钮场景 |
|------|--------------|----------|
| `create` | 所有字段可写（除系统列） | 表单底部"提交/暂存" |
| `edit` | 主键+系统列只读，余可写 | 行内/弹窗"编辑" |
| `view` | 全部只读 | "查看详情"，无提交按钮 |
| `audit` | 按 OA 节点 `FieldPermissions` | 审批节点按钮（§5.7.5） |

`FormFieldDef` 增 `modePermissions: { create:{...}, edit:{...}, view:{...} }`，非 OA 场景用此；OA 场景 `audit` 模式读 `WfProcessNode.FieldPermissions` 覆盖。

> **现状**：`modePermissions` **不存在**（全仓 grep 无）；三层叠加**只有节点级一层**（§4.7）。

#### 5.8.4 数据回填与 payload 映射（设计稿）

**行 record → 表单**（`Open=modal/drawer/page` + `FormMode=edit/view`）：

- 默认：报表当前行 record 整体作为表单 `initialValues`，按 `FormFieldDef.ColumnName` 投影（§5.5——**该投影未实现**，§4.4）。
- 子表回填：若表单含 `table` 子表且行 record 不含子表数据，调 `DetailFlowKey` 取完整 record，入参 `{ id: row.Id }`，出参 `{ record, subTables: {lines:[...]} }`。

**payload → 表单默认值**（`PayloadToFields`，"基于当前行新建"场景）：

```json
{
  "PayloadToFields": {
    "CustomerId": "row.CustomerId",
    "OrderDate": "ctx.sys.now",
    "Source": "'referral'"
  }
}
```

映射值支持：`row.{field}`、`ctx.sys.*`、`'{literal}'`。与 §5.7.2 `defaultValueExpr` 同沙箱，前端 JS 求值后作为 `create` 模式初始值。

#### 5.8.5 提交后刷新策略（统一 after 协议，设计稿）

废弃 `ActionDef.after`，统一用 `ButtonDef` 三字段：

| 字段 | 取值 | 行为 |
|------|------|------|
| `CloseAfterSubmit` | true/false | 提交后关弹窗（false=留在表单继续录） |
| `RefreshScene` | list/detail/none | 刷新目标 |
| `AfterExpr` | JS | 自定义（跳转 `Redirect`/发消息/调其它流），最后执行 |

默认：`CloseAfterSubmit=true` + `RefreshScene=list` + `RefreshAfter=true`。

#### 5.8.6 权限解析链（打开表单全链路，设计稿）

```
报表按钮点击
  → ① 报表访问权限：App.Report.{code}（已落地，见 [identity-contract.md](./identity-contract.md) §7）
  → ② 按钮权限：ButtonDef.Permission（前端 VisibleExpr 先过滤，后端 flowKey 内 [Authorize] 二次确认）
  → ③ 打开表单：按 FormMode 取 modePermissions baseline
  → ④ 若 audit 模式：叠加 WfProcessNode.FieldPermissions
  → ⑤ 表单内按钮权限：FormDef.Buttons[*].Permission
  → ⑥ 提交流 FlowKey 内 Code 节点：权限最终确认（防前端绕过 ① ② ⑤）
```

关键：前端 `VisibleExpr`/`DisabledExpr`/`Permission` 是体验过滤（可绕），后端 `FlowKey` 内 Code 节点 `[Authorize]` + 权限码校验是权威（不可绕）。

#### 5.8.7 批量编辑表单（设计稿）

**协议**：`ButtonDef` 增 `BatchEdit: { enabled: true, fields: [...] }`（仅 `Scope=selection` 时生效）。

- 用户选 N 行 → 点"批量编辑" → 弹精简表单（只含 `BatchEdit.fields`）→ 填公共值 → 提交。
- 提交流入参 `{ ids: [...], patch: {字段:值} }`，流内 Code 节点循环 `UpdateAsync` 或批量 SQL（参数化），受 `BatchLimit` 上限。
- 校验：每个字段的 `rules` 跑一次（不按行跑）；行级业务校验在流内按 id 循环。

#### 5.8.8 行内编辑与表单校验统一（设计稿）

**协议**：`Open=inline` 时：

- 单元格用对应字段的 `control` + `controlProps` + `rules` 渲染（与表单同协议）；
- 失焦时跑该字段 `rules`（含异步），失败标红 + tooltip；
- 提交时调 `FlowKey`（或资源默认 `updateFlowKey`），入参 `{ id, patch: {field:value} }`，流内 Code 节点做权威校验；
- `FieldsMode=whitelist` + `FieldsWhitelist` 控制哪些列可行内编辑。

**关键**：行内编辑与弹窗表单**共用** `FormFieldDef.Rules` + `FlowKey` 校验，不再两套校验逻辑。

#### 5.8.9 落地优先级

| 能力 | 优先级 |
|------|--------|
| §5.8.2 ActionDef ⊂ ButtonDef 合流（字段对齐） | P1（消除双标基础） |
| §5.8.3 表单模式 + modePermissions | P1 |
| §5.8.4 数据回填 + PayloadToFields + DetailFlowKey | P1 |
| §5.8.5 提交后刷新三字段统一 | P1 |
| §5.8.6 权限解析链 | P1 |
| §5.8.8 行内编辑与表单校验统一 | P1 |
| §5.8.7 批量编辑表单 | P2 |
| §5.8.1 #10 主从子表回填（DetailFlowKey） | P1（含在 §5.8.4） |

---

## 6. 操作说明 / 交互规格

> §6.6–§6.9 是**历史事故复盘与真机验证记录**（原文 §8.6/§8.7/§8.8/§8.9）。均为真实记录，**按原样保留**并仅做路径与表述校正。它们不属于"当前能力契约"，是运维知识。

### 6.1 接口与权限

| 侧 | 端点 | 权限码 |
|----|------|--------|
| 表单库 | `api/orchestration/forms`（`AppBuilderControllers.cs:256-276`） | `Orchestration.Forms.*`（`FormDefinitionAppService.cs:33`/`:58`/`:78`/`:111`/`:133`/`:161`/`:181`） |
| 审批流定义 | `api/orchestration/workflows`（`AppBuilderControllers.cs:281-317`） | `Orchestration.Workflows.*` |
| 审批运行时 | `api/orchestration/workflow-runtime`（`:321-339`） | `Orchestration.Workflows.Default` |

表单侧新增的两个提交后端点：`POST /api/orchestration/forms/{id}/publish`（`:269-271`）、`POST /api/orchestration/forms/{id}/resync-catalog`（`:273-276`）。

### 6.2 表单设计器「容器删不掉」（2026-10-10，前端 `vue-demo`）

> ⚠️ 未核验（前端在 vue-demo）：以下全部在 `vue-demo`，本仓无法复核。保留原文记录。

**现象**：在设计器里选中一个容器（如「栅格列」）后，**找不到任何删除入口**；误拖进来的栅格/表格/标签页删不掉，最后只能用「清空」把整张表单一起清掉。

**根因（四处，均在 `vue-demo`）**

| # | 缺陷 | 位置 |
|:--|:--|:--|
| 1 | 操作条（上移/下移/复制/删除）条件为 `selected && !widget.internal`。而 `grid-col`/`table-cell`/`tab-pane` 在模型里都标了 `internal: true` ⇒ 这三类容器**永远没有操作条** | `shared/vform-node.vue` |
| 2 | 右侧属性面板的「移除组件」同样被 `v-if="!selected.internal"` 挡掉 | `shared/vform-designer.vue` |
| 3 | **容器本身选不中**（更根本）：`.vf-col` / `.vf-cell` / `.vf-pane` 的 `@click` 都 `stopPropagation()` 后选中自己；`<Tabs>` 更是整块 `@click.stop` ⇒ 栅格/表格/标签页的**父容器**几乎没有可点区域 | `shared/vform-node.vue` |
| 4 | 结构隐患：`removeWidget` 是纯递归删除，直接删内部单元格会留下 **0 列栅格 / 0 页签标签页 / 缺格表格** —— 这些空壳渲染不出任何可点面，画布上再也选不中 | `shared/vform-model.ts` |

**修复**

| 层 | 措施 | 位置 |
|:--|:--|:--|
| 容器抓手 | 容器节点左上角渲染「栅格 / 表格 / 标签页 / 卡片 / 子表单 / 树形」角标，点它即选中容器本身 | `shared/vform-node.vue` |
| 内部单元格删除 | 选中的栅格列 / 表格单元格 / 标签页内联显示红色「删除」按钮 | `shared/vform-node.vue` |
| 属性面板 | 去掉 `v-if="!selected.internal"`，容器也能从右侧一键移除 | `shared/vform-designer.vue` |
| 安全删除 | 新增 `findParentWidget()` 与 `removeWidgetSafe()`：删栅格最后一列 / 标签页最后一个页签 / 表格最后一列时，**连整个容器一并删** | `shared/vform-model.ts`（`removeSelected` 改用它） |

**验证（真机，Chrome + `playwright-core`，只读交互、未点「保存」）**

| 用例 | 结果 |
|:--|:--|
| A1 拖入「双列栅格」 | 栅格数 1 → 2 ✅ |
| A2 新栅格列数 | 2 ✅ |
| A3 选中栅格列 | 「删除」按钮可见 = true ✅ |
| A4 删除一列 | 该栅格列数 2 → 1 ✅ |
| A5 再删最后一列 | 栅格数回到 1（**整个栅格被一并移除**）✅ |
| B1 拖入「三列栅格」 | 栅格数 1 → 2 ✅ |
| B2 容器抓手文案 | 「栅格」✅ |
| B3 点抓手 | 容器操作条（含「删除」）可见 = true ✅ |
| B4 删除容器 | 栅格数回到 1 ✅ |
| C1 选中栅格列 | 属性面板「移除组件」可见 = true ✅ |
| C2 面板移除 | 栅格数回到 1（单列栅格整体移除）✅ |
| 控制台 | **0 错误** ✅ |

前端类型检查：`pnpm -F @vben/web-antd run typecheck` → **exit 0**。

> 这条缺陷与后端 §4.1 有结构性呼应：**前端删容器留下"0 列空壳"，后端拍平又可能抽不出字段**。两侧都应以"空结构必须显式表达"为不变量。

### 6.3 工程化加固与守卫（2026-10，本仓可核验部分）

| 方向 | 内容 |
|:---|:---|
| **密钥治理** | 各 Host `appsettings.json` 移除明文 `SwaggerClientSecret` / `DefaultPassPhrase`，改由 git-ignored 的 `appsettings.secrets.json` 提供；提交 `*.example` 模板；`.gitignore` 覆盖 `**/appsettings.secrets.json`、`tempkey.jwk`、`tempkey.rsa` |
| **证书** | AuthServer 从 `OpenIddict:Certificate:Path` 加载 OpenIddict **签名与加密证书**，不再依赖开发默认证书 |
| **健康检查** | `MapDefaultEndpoints()` 在所有环境映射 `/health` 与 `/alive`（JSON 输出）；5 个 Host 的 `Program.cs` 均调用 |
| **网关加固** | JWT Bearer 校验 + 默认「已认证」策略；限流 ~300 req/min（按客户端分区）；统一 401/429 错误体；`{**catch-all}` 要求认证；集群超时 30s |
| **服务边界** | 移除 SaaS `Application` / `Application.Contracts` 对 Administration 的**编译期 `ProjectReference`**，改用 `Volo.Abp.Identity.Domain` 包 |
| **分布式锁** | `ConfigureDistributedLocking` 提为公共扩展方法（无 Redis 时跳过）；`FlowScheduleHostedService` 触发前获取 `IAbpDistributedLock` |
| **构建基建** | `global.json`（SDK 10.0.401）、`.editorconfig`、`Directory.Packages.props`（CPM）、`.dockerignore`；Dockerfile 重写；`Meta.Dow.slnx` 纳入 tests 目录 |
| **包源治理** | 删除 4 个模板残留的服务级 `NuGet.Config`；根 `NuGet.config` 补 `<packageSourceMapping>` |
| **CI** | `.github/workflows/ci.yml`：restore → build（Release）→ test，上传 trx |
| **测试** | `tests/Meta.Dow.Orchestration.Tests`（xUnit，**62 个用例**，明细见 §2.4）；应用服务成员 `private` → `internal` + `InternalsVisibleTo` |
| **文档** | `src/CLAUDE.md` 与代码对齐（Aspire 13.5.3 / MongoDB / `Meta.Dow.slnx` / CPM / CI） |
| **前端对齐** | ⚠️ 未核验（前端在 vue-demo）：`orchestration.ts` 类型补齐、`resyncCatalogFromResourceApi`、版本列、连线优先级/默认分支面板、`form-schema.ts` 属性透传 |

### 6.4 登录回归事故：OpenIddict 客户端被「数组按索引合并」抹除（2026-10-10）

**现象**：前端跳到 AuthServer 后不再出现登录页，直接返回 OpenIddict 错误响应：

```
error: invalid_request
error_description: The specified 'client_id' is invalid.
error_uri: https://documentation.openiddict.com/errors/ID2052
```

**根因（配置合并语义 × 破坏性清理）**

`.NET` 配置对 **JSON 数组是按索引合并**的，而不是整段替换、也不是拼接。密钥治理把 `ClientSecret` 从 `appsettings.json` 抽到 `appsettings.secrets.json` 时，secrets 文件里用**同一个 `"Clients"` 键**再写了一份数组，于是两份配置按索引逐字段"混血"：

| 索引 | `appsettings.json` 原值 | `secrets.json` 覆盖 | 合并结果 |
|:--|:--|:--|:--|
| 0 | `MetaDow_Vue` + Vue 的 GrantTypes/Scopes/RedirectUris | `{ClientId: Administration_API, ClientSecret}` | **`Administration_API`**，但带着 Vue 的授权码/密码/刷新令牌、Vue 的 scope 与回调地址 |
| 1 | `Administration_API` | `IdentityService_API` | `IdentityService_API` 带着 Administration_API 的重定向 |
| 2 | `IdentityService_API` | `Projects_API` | `Projects_API` 带着 IdentityService_API 的重定向 |
| 3 | `Projects_API` | `SaaS_API` | `SaaS_API` 带着 Projects_API 的重定向 |
| 4 | `SaaS_API` | —（secrets 只有 4 项） | `SaaS_API` 保持原值 |

结果：**`MetaDow_Vue` 不再是任何一个条目的 `ClientId`**，随后 `RemoveObsoleteClientsAsync`（"删掉配置里没登记的客户端"）把它**软删除**（MongoDB 实测 `IsDeleted: true`，`DeletionTime: 2026-10-10T13:33:09.713Z`），前端再带 `client_id=MetaDow_Vue` 请求 `/connect/authorize` 即被拒。

**修复**

| 层 | 措施 | 位置 |
|:--|:--|:--|
| 配置 | 密钥改放**独立的 `ClientSecrets` 对象键**（`{clientId: secret}`）。对象是**按 key** 合并的，不会错位；`Clients` 数组只保留在 `appsettings.json` 一处，成为唯一事实来源 | `DbMigrator/appsettings.secrets.json`(+`.example`) |
| 种子器 | 新增 `LoadClients()` 把 `ClientSecrets:{clientId}` 合并进客户端定义 | `OpenIddictDataSeeder.cs` |
| 种子器 | 新增 `ValidateClients()`：**先在内存校验完再碰数据库**（ClientId 非空/不重复、GrantTypes/Scopes 非空），不合法直接抛错中止 | 同上 |
| 种子器 | 破坏性清理**显式开关** `PruneObsoleteClients`（默认 `false`），并逐个 ClientId 打 **Warning** 日志 | 同上 |
| 种子器 | `ApplyClientSettings` 改为**配置即事实**：先 `Clear()` 掉 `Permissions`/`Requirements`/`RedirectUris`/`PostLogoutRedirectUris` 再按配置重建。原实现在旧值上累加，混血数据一旦写入就永远清不掉；改后重跑迁移可**自愈收敛** | 同上 |
| 数据 | 一次性修复脚本：恢复被软删的 `MetaDow_Vue`，并把 4 个被污染 API 客户端的 `ClientUri`/`RedirectUris`/`PostLogoutRedirectUris`/`Permissions` 收敛回配置 | MongoDB `MetaDowIdentityServiceDb.OpenIddictApplications`（OpenIddict 数据落在 Identity 库，因 `AbpDbConnectionOptions` 把 `AbpOpenIddict` 映射到 `MetaDowIdentityServiceDb`） |
| 护栏 | 新增 `SecretsConfigSafetyTests`：扫描全仓 `appsettings*.secrets.json`（含 `.example` 模板），**拒绝任何 JSON 数组**，并打印具体 JSON 路径 | `tests/Meta.Dow.Orchestration.Tests/SecretsConfigSafetyTests.cs` |

**验证**：`curl -k https://localhost:7600/connect/authorize?client_id=MetaDow_Vue&...` 由错误响应恢复为 `302 → /Account/Login`；后端 `dotnet build -c Release` 0 错误；**单元测试 62/62 通过**。

**长期规则**：secrets 覆盖文件里**永远不要出现 JSON 数组**。需要覆盖数组语义时改用对象键。

### 6.5 登录后全线 401：网关把 JWT 受众写死成解决方案名（2026-10-10）

**现象**：登录页能打开，登录也能成功取到令牌（库中可见），但随后控制台刷屏 401；页面弹出 `未认证或令牌无效` 与 `页面加载失败：Request failed with status code 401`。

**定位（先证来源，再谈原因）**

`未认证或令牌无效` 这个文案**全仓只出现在一个地方**——网关 `Meta.Dow.Gateway/Program.cs` 的 `JwtBearerEvents.OnChallenge`。所以这组 401 必然由**网关**产生，而不是被代理的服务。

**根因：受众（audience）写死错误**

上一批"网关加固"里写了：

```csharp
options.Audience = configuration["AuthServer:Audience"] ?? "Meta.Dow";
options.TokenValidationParameters.ValidateAudience = true;
```

但 OpenIddict 是把**「作用域所关联的资源名」**写进令牌的 `aud`。本仓库 `DbMigrator/appsettings.json` 与库中 `OpenIddictScopes` 一致地声明：

| 作用域 | `Resources` |
|:--|:--|
| `MetaDowAdministration` | `["MetaDowAdministration"]` |
| `MetaDowAuthServer` | `["MetaDowAuthServer"]` |
| `MetaDowIdentityService` | `["MetaDowIdentityService"]` |
| `MetaDowSaaS` | `["MetaDowSaaS"]` |

而前端（`MetaDow_Vue`）实际申请的 scope 是 `openid profile email offline_access MetaDowIdentityService MetaDowAdministration MetaDowSaaS`（见 `vue-demo/apps/web-antd/.env` 与库中 `OpenIddictAuthorizations.Scopes`）。

⇒ 令牌的 `aud` **只可能**是上述服务名，**永远不含解决方案名 `Meta.Dow`**。开启 `ValidateAudience` 且要求 `Meta.Dow` ⇒ **每一个有效令牌都被判无效** ⇒ 所有需要认证的请求 401。

> 注意：`MetaDowAuthServerModule` 里的 `options.AddAudiences("Meta.Dow")` 是给 **AuthServer 自身做令牌校验**用的"可接受受众"声明，它**不会**把 `Meta.Dow` 塞进签发的令牌里——两者极易混淆。

**为什么现在才暴露**：前端 `/api/menu/*`、`/api/permission-management/*` 原先**没有显式路由**，会落进 catch-all（`{**catch-all}` + `AuthorizationPolicy: Default`）。登录成功后前端第一件事就是拉 `/api/menu/all`，于是必然撞上这条兜底路由，缺陷立刻显形。

**修复**

| 层 | 措施 | 位置 |
|:--|:--|:--|
| 网关 | **不再写死单一受众**。默认 `ValidateAudience = false`（仍校验签名/签发者/有效期），受众由下游各服务按自身受众把关；如需在网关强校验，用 `AuthServer:ValidAudiences` 显式列出允许值 | `Meta.Dow.Gateway/Program.cs` |
| 网关配置 | `AuthServer.Audience` → `AuthServer.ValidAudiences: []`；补齐前端实际在用却缺路由的 `menu`、`permission-management` 两条路由（→ administration 集群） | `Meta.Dow.Gateway/appsettings.json` |

#### 6.5.1 验证记录（真机，2026-10-10 14:1x）

`AppHost` 全栈重启后，用**真实令牌**经网关实测（只读探测，未改任何数据）：

**令牌事实**（`POST https://localhost:7600/connect/token`，`client_id=MetaDow_Vue`，密码模式）：

```
aud = ['MetaDowAdministration', 'MetaDowIdentityService', 'MetaDowSaaS']
iss = https://localhost:7600/
```

**网关实测**（`https://localhost:7500`，即前端 Vite `/api` 代理目标）：

| 请求 | 无令牌 | 带令牌 |
|:--|:--|:--|
| `GET /api/menu/all` | 401 | **200** |
| `GET /api/abp/application-configuration` | — | **200** |
| `GET /api/permission-management/permissions?...` | — | **200** |
| `GET /api/account/my-profile` | — | **200** |

即：有效令牌放行、无令牌仍拦截 401，符合预期。

**长期规则**：**同时前置多个 API 的网关，不要在网关写死单一 JWT 受众。** 受众是"每个 API 自己的事"，网关只负责签名/签发者/有效期的边缘把关。若要校验，必须列出**全部**允许受众。

#### 6.5.2 全站路由渲染巡检（真机 29 路由，2026-10-10 14:2x）

> ⚠️ 未核验（前端在 vue-demo）：本次巡检针对前端，本仓无法复核。保留原文记录。

用 `playwright-core`（复用 vue-demo 的 pnpm 仓库，不下载 Chromium）驱动本机 Chrome，登录后对**全部 29 个菜单路由**做逐页渲染巡检（含 3 个带真实 id 的设计器、资源编辑器、报表编辑器、资源运行时）。

**结论：0 个功能错误。** 逐页判定维度：`console.error` / `pageerror` / `requestfailed` / HTTP ≥ 400，全部为 0；唯一被标记的 `/dashboard/workspace` 也 issues=0，只是 dev 首次硬刷新的启动遮罩超过巡检阈值（页面正常渲染）。

**唯一的控制台输出是两类警告，均与批次改动无关：**

| 警告 | 出处 | 性质 |
|:--|:--|:--|
| `[Vben Form] BREAKING CHANGE ... slotProps.componentProps` | 上游模板自带 Vite 插件 `internal/vite-config/src/plugins/form-field-slot-migration-warning.ts`（`apply:'serve'` 无条件注入脚本 + 启动 `logger.warn`） | 提示，**不扫代码**；本仓 app 零 `v-bind="slotProps"`，**无需整改** |
| `Runtime directive used on component with non-element root node`<br>`Component inside <Transition> renders non-element root node that cannot be animated` | 项目**路由错误边界包装器** `packages/effects/layouts/src/hooks/index.ts:115`：render 返回 `slots.default?.()`（vnode 数组 ⇒ **Fragment 根**），且 `Wrapper.name` 被设为路由组件名 | 真实（轻微）缺陷，**每个路由页都触发** |

**Fragment 根的实际影响**：`content.vue:57` 的 `v-show="!route.meta.iframeSrc"` 作用在该包装组件上，因根不是单一元素而**静默失效**（iframe 路由不会被隐藏）；`<Transition>` 的进出场动画对该页也不生效。功能不受影响。

**修复方向（未实施，待确认）**：让包装器返回单一元素根，例如 `return h('div', { class: 'h-full contents' }, slots.default?.());` —— 会为每个路由页增加一层包裹元素，需回归布局样式，故先记录不动。

### 6.6 Aspire Dashboard 启动告警：浏览器 UI 状态解密失败（非缺陷，2026-10-10）

**现象**（仅启动 AppHost 时出现，业务服务日志里没有）：

```
warn: Aspire.Hosting.Dashboard.Model.BrowserStorage.LocalBrowserStorage[0]
Error when reading 'Aspire_Resources_CollapsedResourceNames_MetaDow' as List`1.
System.Security.Cryptography.CryptographicException: The payload was invalid.
   at ...ProtectedBrowserStorage.GetAsync[TValue](String purpose, String key)
```

**来源**：`Aspire.Hosting.Dashboard` —— 即**内嵌在 AppHost 进程里的 Aspire 控制台**，不是任何一个业务 Host。`LocalBrowserStorage` 读写的是**浏览器 localStorage**（用途仅为记住"资源列表里哪些分组被折叠"这类 UI 偏好）。

**机制（.NET Data Protection 语义，确定性）**

`ProtectedBrowserStorage` 走的是 ASP.NET Core **Data Protection**：写入 localStorage 前 `Protect(purpose, json)`，读取时 `Unprotect`。而 Data Protection 的密文是 **AES-CBC + HMAC** 的**认证加密**——HMAC 校验不过就直接抛 `CryptographicException: The payload was invalid`（这正是栈里的 `CbcAuthenticatedEncryptor.DecryptImpl`）。

验证失败只可能来自两件事：

1. **密钥材料不同**——加密时的那个 DP 密钥已不在当前密钥环里；
2. **ApplicationDiscriminator 不同**——官方文档原文：

   > *"An identifier that uniquely discriminates this application from all other applications on the machine. **The discriminator value is implicitly included in all protected payloads** ... to isolate multiple logical applications that all happen to be using the same key material."*

   即 discriminator 会被隐式混入**每一个**受保护负载。它一变，旧负载一律解不开。默认 discriminator = 应用的 **ContentRootPath**；本项目业务侧则显式设为 `"Meta.Dow"`（`MetaDowHostingModule.ConfigureDataProtection` 的 `SetApplicationName(MetaDowNames.MetaDow)`）。

**本机实测取证**

| 项 | 实测 |
|:--|:--|
| 密钥环目录 | `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`（**默认文件密钥环**，7 个 key） |
| 最新 key | `key-a71cba2b…` 创建于 **2026-09-09**，有效至 2026-12-08（90 天周期内，**尚未过期、不会被轮换**） |
| 历史 key | 2024-03 / 2024-06 / 2024-11 / 2025-08 / 2026-01 / 2026-04 —— 连 2 年前的都**仍在**，说明密钥环**未被清理** |
| 结果 | 密钥环稳定 ⇒ 失败原因**不是**"密钥被删"，而是**加密时的 discriminator/用途与当前实例不同** |

**影响**：**零功能影响**。它只是控制台自己的一条 UI 偏好读不出来，Aspire 会 catch 掉并回落到默认值；业务服务的 DP 是**独立**的（同一文件环但 discriminator = `Meta.Dow`）。

**处理**

| 层 | 措施 |
|:--|:--|
| 真正修复（浏览器侧，一次性） | 打开控制台页面 → F12 Console 执行 `Object.keys(localStorage).filter(k=>k.startsWith('Aspire_')).forEach(k=>localStorage.removeItem(k))`，或"清除站点数据" |
| 噪声抑制（仓库侧，已实施） | AppHost 两个 `appsettings*.json` 的 `Logging:LogLevel` 增加 `"Aspire.Hosting.Dashboard.Model.BrowserStorage": "Error"`：只压掉这一类**已在 Aspire 内部被 catch、仅剩日志输出**的 warning，**Error 级仍会打** |

**注意**：不要为了"消除告警"去动 Data Protection 配置或删除 `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`——该密钥环是**机器级共享**的（业务服务的开发态 DP 也用它），删掉会让所有服务在开发态重新生成密钥并丢失既有保护负载。

---

## 7. 参考链接

- [`oa-contract.md`](./oa-contract.md) —— OA 审批流：表单绑定、节点规则与运行时契约、W 系列缺陷、OA 侧规划
- [`data-collection-contract.md`](./data-collection-contract.md) —— 人审与逻辑编排的分工（§6）、资源发布运行时只读已发布 Schema（§5.13）
- [`form-preview.md`](./form-preview.md) —— 表单预览
- [`logic-orchestration-contract.md`](./logic-orchestration-contract.md) —— 逻辑编排引擎（`ForEach`/`Parallel`/`SubFlow` 已实现；独立 `Sql`/`Switch` 未实现）
- [`report-contract.md`](./report-contract.md) —— 报表与 `ActionDef`（§6）
- [`identity-contract.md`](./identity-contract.md) —— 组织机构、数据权限、报表访问权限（§7）
- 外部对标：[form-create](https://www.form-create.com/designer/)、[VForm3](https://www.vform666.com/vform3/)、[Formily](https://github.com/alibaba/formily)、[Workflow-Vue3](https://stavinli.github.io/Workflow-Vue3/dist/index.html#/)、[lowflow-design](https://tsai996.github.io/lowflow-design/)
- 钉钉 / 飞书审批（字段权限矩阵、按钮动态化、明细表行级公式）

---

## 8. 落地优先级总览（规划项）

| 能力 | 优先级 | 关联代码 | 章节 |
|------|--------|----------|------|
| 字段校验 rules 协议（含双层校验） | P1 | `FormFieldDef.Rules` | §5.1 |
| 级联取数 / 异步选项 / 计算 | P1 | `FormFieldDependencyDef.Action` | §5.2 |
| 表单发布**历史快照**（真正冻结） | P1 | `FormDefinition` + `WorkflowInstance` | §5.4 |
| 字段映射 `ColumnName` 的**运行时投影** | P1 | `ResourceCrudExecutor` | §5.5 |
| 直属部门主管解析**排除发起人** | P1 | `WorkflowOrgResolver.ResolveDirectManagerAsync` | §4.6 |
| 抄送通知消费端（站内/IM/邮件） | P1 | `WorkflowNotifier` 下游 | — |
| 超时与催办 | P1 | `WfProcessNode` + `WorkflowTask` + 后台 Job | [`oa-contract.md`](./oa-contract.md) §5 |
| 审批人去重（跨节点） | P1 | `ResolveAssigneesAsync` + `HistoryJson` | 同上 |
| 流程版本迁移 `MigrationPlan` | P1 | `WorkflowDefinition` | 同上 |
| 批量审批 | P1 | `WorkflowRuntimeAppService` + API | 同上 |
| 子表行级权限/联动 | P2 | `table` 控件 `ControlProps` | §5.3 |
| 多语言/布局预设 | P2 | `FormFieldDef` + `FormDef` | §5.6 |
| 加签/减签/委办、撤回/自选审批人 | P2 | `CompleteWorkflowTaskDto.Action` | 同上 |
| 模板库/导出导入 | P2 | `WorkflowDefinition` | 同上 |
| 并行网关/汇合、子流程嵌套 | P3 | `WfProcessNode` fork/join、`callActivity` | 同上 |

**落地顺序建议**：**先修 §4 的高危缺陷**（驳回语义、已发布定义可改写、补丁丢失、并发），再补 P1 能力。原因是：P1 能力都建立在"定义可信、流转可信"之上，而当前 §4 的高危项会让任何新增能力的效果不可观测。P2 再做加签/撤回/子表行级；P3 评估并行网关与子流程是否必须，否则继续自研轻量引擎不上 bpmn-js。

---

## 9. 修订记录（本次重构，2026-10-13）

**代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）：Release 构建 0 错误，62/62 单测通过。

**文档结构变更**：按统一骨架重排为 9 章；**事实（§1/§3/§6）、缺陷（§4）、规划（§5）三向隔离**；删除旧的 §0 实现状态表与 §8 缺陷表的**重复内容**，收敛为 §2 唯一权威总表（含 OA 侧 W1–W9 逐条复核）；OA 侧细节迁移到 [`oa-contract.md`](./oa-contract.md) 并在 §4 仅留摘要与链接，两文互链不重复。

### 9.1 删除/改写的「与代码矛盾的断言」

| # | 原断言（旧版位置） | 代码真相 | `file:line` |
|--:|--------------------|----------|-------------|
| 1 | §0 结论：「§8.1/§8.2 所列 F1–F7、W1–W9 定义侧健壮性缺陷**已全部修复**」 | 降级为「主体落地，遗留项见 §4」。F3 未合并（§4.14）、F4 未真正冻结（§4.2）、F9/§5.5 `ColumnName` 无运行时消费（§4.4）、F13 重同步不重推 `Fields`（§4.11）、W2 仍 fail-open（§4.12②）、W6 `IsDefault` 不校验（§4.12③）、W7 可自审批（§4.6）、W8 未声明字段放行（§4.7）、W9 索引失败仅告警（§4.2） | `FormDefinitionAppService.cs:213-228`、`:182-206`；`WorkflowRuntimeAppService.cs:443-446`、`:983-986`；`SaaSMongoDbModule.cs:101-104` |
| 2 | §8.5.1 F1：「`EnsureFlattenedFields` 改为以 Layout 为准的覆盖式同步……Layout 中已删除的字段从 `Fields` 中移除」 | **部分成立**：覆盖逻辑在，但被 `if (extracted.Count > 0)` 门控，抽出 0 字段时不覆盖（幽灵字段） | `FormDefinitionAppService.cs:224-227` |
| 3 | §8.5.1 F3：「保留双模型但**消除漏拷贝根因**：两类的字段集按 F2 对齐为同一超集语义」 | **"消除根因"不成立**：两类仍是独立声明的类，属性逐条重复；新增属性仍需同改 `BuildField`/`CloneWidget`/`CloneField` 三处。当前只是"属性集已补齐、暂时不复发"。（本轮初稿曾误称 `FormWidgetDef` 缺 `VisibleOnDetail`，已更正——见 §4.14） | `ResourceSchema.cs:317-375` vs `:381-442`；`FormDefinitionAppService.cs:275-297`、`:426-454`、`:456-479` |
| 4 | §5.5 / §8.5.1 F5：「提交 `record` 时按 `ColumnName` 投影到库」 | 属性存在，**运行时零消费点** | `ResourceSchema.cs:325`；全仓 8 处 `ColumnName` 无运行时读取 |
| 5 | §8.5.1 F4：「发布生成**不可变快照**」+ §5.4「OA 启动时固化 `FormSnapshotJson`」 | 快照是**单份可变引用**、无历史版本；`FormSnapshotJson` **字段不存在**；`PublishedSchema=null` 时回落可变草稿 | `FormDefinition.cs:81-86`、`:89`；`WorkflowInstance.cs` 无 `FormSnapshotJson` |
| 6 | §8.5.1 F5：「发布校验 `ValidateAgainstResourceAsync`（与绑定资源列对齐）」 | 校验存在但**三处 fail-open**（无 SourceResourceCode / 资源查不到 / 资源无列）+ 跳过明细字段 | `FormDefinitionAppService.cs:330-345`、`:348` |
| 7 | §8.5.1 F6：「被 OA `FormRef`、报表 `ActionDef.FormKey`、资源表单引用时拒绝删除」 | `FormKey` 的比对**永远不命中**——`ActionDef.FormKey` 被赋成 `"create"`/`"update"`/`"detail"`（模式名）；资源内联表单 `AppResource.Form` **未被检查**；先查后删非原子 | `FormDefinitionAppService.cs:150`；`AppResource.cs:159`/`:165`/`:171`/`:280`；`SaaSMongoDbModule.cs:27-28` |
| 8 | §8.5.1 F7：「`ResyncCatalogFromResourceAsync`……资源表结构变更后可重新对齐**字段目录与拍平字段**」 | **只刷新 `FieldCatalog`**，第 4 参传原样 `entity.Schema`，不重推 `Schema.Fields` | `FormDefinitionAppService.cs:196-203` |
| 9 | §8.5.4 / §0：「并行网关/汇合、循环仍为规划」 | **误记**。循环 `ForEach` 与并行 `Parallel` 属**逻辑编排引擎**且**已实现**；未实现的是编排的独立 `Sql`/`Switch`，与人审侧的 fork/join、`callActivity` | `FlowExecutor.cs:759-773`（已实现）；`FlowExecutor.cs:791-792`（无 `sql`/`switch`） |
| 10 | §8.5.3 测试：「新增 `tests/Meta.Dow.Orchestration.Tests`（xUnit，**23 个用例**）：Validator(11)/Flattening(7)/RecordPatch(4)/Secrets(1)」；§8.6「单元测试 **23/23** 通过」 | 实测 **62 个用例**（58 个测试特性 = 56 `[Fact]` + 2 `[Theory]`，+6 `[InlineData]`）；分文件 Validator 11 / Flattening 7 / RecordPatch 4 / Secrets 1 / **FlowExecutorControlFlowTests 39**（旧版完全漏记）；且**全部运行时路径零覆盖** | `tests/Meta.Dow.Orchestration.Tests/`（§2.4） |
| 11 | §5.1 / §5.1.1：`List<FormFieldRuleDef> Rules`，含 `Kind`/`Len`/`Enum`/`Validator`/`Deps`/`Trigger`/`Severity` + `ValidateFlowKey`；§5.7.5 `ButtonDef` | 代码里 `Rules` 是**单个可空对象**（`FormFieldRuleDef` 仅 7 个属性）；`ButtonDef`/`ValidateFlowKey`/`modePermissions` 全仓**不存在** | `ResourceSchema.cs:287-302`、`:356`、`:426`；grep 无匹配 |
| 12 | §5.2：`loadOptions`/`fetchValue`/`compute`/`cascade` 四个 Action | **不存在**，`Action` 只有 show/hide/enable/disable/require/optional/setValue（自由字符串，无枚举校验） | `ResourceSchema.cs:312` |
| 13 | §2.2：前端渲染器与**后端校验器**根据 `dependencies` 实现动态显隐/禁用/必填 | **后端零求值点**；`Dependencies` 仅被拍平与克隆拷贝 | `FormDefinitionAppService.cs:295`、`:452`、`:473` |
| 14 | §3.4：「补丁内容与 `instance.RecordJson` 进行**深度字段合并**」 | 实现是**顶层键覆盖** | `WorkflowRuntimeAppService.cs:1071-1074` |
| 15 | §8.5.2 W6：「`ChooseNext` 多条件出边时按 `Priority` 排序取首个命中……**不再依赖数组顺序**」 | 全 `Priority=0` 时 `OrderBy` 稳定排序＝**数组顺序**；`IsDefault` 在校验器里**从未解析**；优先级查重只覆盖 `Priority>0` 的重复 | `WorkflowRuntimeAppService.cs:462`；`WorkflowDefinitionValidator.cs:156-178` |
| 16 | §8.4 W2：「连线目标节点不存在时**显式报错**，不再回落为 `Complete("approved")`」 | 目标不存在确实报错；但**无出线节点**仍 `(null,false)` → `Complete("approved")` | `WorkflowRuntimeAppService.cs:443-446` → `:433` |
| 17 | §0：「前端审批时间轴 Timeline ✅ **已落地**」 | 降级为 `⚠️ 未核验（前端在 vue-demo）`；本仓只能核验后端供数 `HistoryJson` | `AppBuilderDtos.cs:750-751` |
| 18 | 全文 `file:///d:/Project/demo-microservice/...` 绝对链接 | **全部改为仓库相对路径**（`src/services/saas/...`），无盘符路径残留 | — |

### 9.2 保留的原文内容

- §1.3 初始缺陷诊断表（状态列按代码校正，原诊断与业务影响保留）；
- §5.1–§5.8 全部协议设计稿（明确标注 ❌ 规划 + "与现有模型的三处冲突"提示），包括双层校验模型的信任边界论证、`ctx` 沙箱约束、按钮 before→flow→after 事件流、"对照钉钉/飞书"的对标段落；
- §6.2 表单设计器「容器删不掉」的四处根因与真机 12 条验证记录；
- §6.4 / §6.5 两起登录事故的完整复盘（现象 / 根因 / 数据取证 / 修复矩阵 / 验证记录 / 长期规则）——**真实复盘，按原样保留**；
- §6.5.1 网关真机验证记录、§6.5.2 全站路由巡检结论（含 Fragment 根缺陷与"无需整改"判定）、§6.6 Aspire Dashboard 解密告警分析（含"不要删密钥环"的明确警告）。

### 9.3 本次新增

- §2 唯一权威能力总表（表单 15 项 + OA 8 项 + 编排 5 项 + 测试 5 行，每条带 `file:line`）；
- §4 十六条缺陷（F 系列 4.1–4.16，其中 §4.1 幽灵字段、§4.3 联动零求值、§4.4 `ColumnName` 无消费、§4.5 校验 fail-open、§4.10 删除检查两处盲点、§4.11 重同步不完整、§4.12 三个绕过口、§4.14 F3 未合并、§4.15 `Rules` 单对象、§4.16 自由字符串无枚举校验为本次逐行核实所得）；
- §3.2 拍平真实语义（含 `Title` 用字段名兜底的细节）、§3.3 发布链六步、§3.4 快照与版本；
- OA 侧缺陷与规划迁移到 [`oa-contract.md`](./oa-contract.md)，本文只留摘要与链接，消除两文重复。
