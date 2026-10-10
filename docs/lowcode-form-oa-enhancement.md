# Meta.Dow 表单编辑与 OA 流程重构设计规格书

> **文档性质**：表单设计器与 OA 审批流的核心架构重构与能力落地规格书。  
> **关联文档**：[`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md)、[`lowcode-oa-workflow.md`](./lowcode-oa-workflow.md)、[`lowcode-form-preview.md`](./lowcode-form-preview.md)  
> **前后端边界**：前端 `D:\Project\vue-demo`（Vben Admin / Ant Design Vue）；后端 `Meta.Dow.SaaS` 编排引擎。

> **实现状态（2026-10 核对）**：本文原为"重构规格书"，下方 §2、§3 所列能力**绝大多数已落地**。仍待补的项标注 ⚠，详见 §0。  
> **定义侧健壮性缺陷**：表单定义链（F1–F7）与 OA 流程定义链（W1–W9）的代码级缺陷汇总见 **§8**——§0 回答"有没有落地"，§8 回答"稳不稳"。

---

## 0. 实现状态核对（2026-10）

> 本节由代码核对补入，作为下方原文的"实施真相"。原文 §1–§4 保留作为能力契约参考，实施时以本表为准。

| 章节 | 能力 | 状态 | 代码位置 |
|------|------|------|----------|
| §2.1 | 标准控件协议扩展（input/select/cascader/userPicker/deptPicker/upload/grid/card/table/tabs 等） | ✅ 已落地 | [ResourceSchema.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs) `FormFieldDef.Control` + `ControlProps` |
| §2.2 | 字段动态联动 `dependencies`（show/hide/enable/disable/require/setValue） | ✅ 已落地 | `FormFieldDependencyDef`（同文件，`SourceField/Op/Value/Action/SetValue`） |
| §2.3 | 容器树自动拍平 `Fields`，消除发布硬编码阻断 | ✅ 已落地 | [FormDefinitionAppService.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FormDefinitionAppService.cs) `EnsureFlattenedFields`，`UpdateAsync`/`PublishAsync` 均调用 |
| §3.1 | 流程实例快照 `ProcessSnapshotJson`，流转基于快照寻址 | ✅ 已落地 | [WorkflowInstance.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/WorkflowInstance.cs) `ProcessSnapshotJson` + [WorkflowRuntimeAppService.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/WorkflowRuntimeAppService.cs) 全程读快照 |
| §3.2 | 五动作 `approve`/`reject_to_prev`/`reject_to_starter`/`reject_terminate`/`transfer` | ✅ 已落地 | `WorkflowRuntimeAppService.CompleteAsync` |
| §3.3 | 节点字段权限矩阵 `FieldPermissions`（read/write/hide/required） | ✅ 已落地 | `WfProcessNode.FieldPermissions` + 办理时 `FilterRecordPatch` 按权限过滤 |
| §3.3 | `EmptyFallback` 容错（admin/skip/error） | ✅ 已落地 | `WfProcessNode.EmptyFallback`，缺人时按策略处理 |
| §3.4 | 审批时表单数据回写 `RecordPatch` | ✅ 已落地 | `CompleteAsync` 调 `FilterRecordPatch(node.FieldPermissions, input.RecordPatch)` 后合并到 `instance.RecordJson` |
| §3.5 | 执行轨迹 `HistoryJson` | ✅ 已落地 | `WorkflowInstance.HistoryJson` 字段 + `CompleteAsync` 写入流转记录 |
| §3.2 | 部门主管 `manager` 解析为**直属部门主管** | ⚠ **未真正落地** | `ResolveAssigneesAsync` 的 `case "manager"` 仍返回 `AssigneePick(null, 角色码)`，**未接组织树**。需 Identity 组织机构接口落地后改为查 `AbpOrganizationUnits` 直属主管 |
| §3.2 | `assigneeType=role` 显式分支 | ⚠ **隐式实现** | `ResolveAssigneesAsync` 默认值是 `role`，但 switch 无显式 `case "role"`，落到 `default` 把 value 当角色码传 `AssigneePick(null, value)`。语义可用（角色=一个名额，任一人可办），但分支不显式，建议补 `case "role"` 与 `manager` 区分语义 |
| §2 / §3 | 编排引擎节点不全：独立 `Sql` 节点、`Switch`、并行、循环 | ⚠ **未落地** | [FlowExecutor.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FlowExecutor.cs) 节点 dispatch 仅覆盖 `start/end/log/assign/mask/throw/assert/condition/http/code/rabbitmqpublish/subflow/resourcecrud*`；SQL 经 Code 节点 `db.*` 间接实现；Switch/并行/循环见 [lowcode-logic-orchestration.md](./lowcode-logic-orchestration.md) §11.3 仍为规划 |
| §3.5 | 前端审批时间轴 Timeline 渲染 | ⚠ **前端待补** | 后端 `HistoryJson` 已提供数据，前端 Timeline 组件未在本仓库（前端在 `vue-demo`） |

**结论**：§2 表单侧、§3 流程侧核心能力已落地；剩余缺口集中在①组织树未接入导致 `manager`/`role` 仍按角色码匹配、②编排引擎控制流原语（Switch/并行/循环/独立 Sql）不全、③前端时间轴未渲染。下方原文保留作为能力契约。

---

## 1. 现状缺陷诊断与重构目标

在现有实现中，表单设计器与 OA 流程存在严重的业务脱节与架构断层：

| 模块 | 现状致命缺陷 | 业务影响 | 重构方案与标准 |
|:---|:---|:---|:---|
| **表单编辑** | 缺少组织协同控件（用户/部门/级联/上传等） | 无法录入请假人、报销部门、凭证附件等 OA 基础信息 | 扩充标准控件协议，引入 `userPicker`、`deptPicker`、`upload`、`radio`、`checkbox`、`select`、`cascader` 等控件 |
| **表单编辑** | 缺少字段动态联动与依赖（显隐/只读/必填联动） | 无法根据条件动态展示输入项（如“请假类型=病假”才显示“上传医院证明”） | 增加 `dependencies` 规则协议，支持条件驱动的 show/hide/require/disable/setValue |
| **表单编辑** | `Fields`、`Layout` 与 `VformJson` 多轨割裂，发布硬编码阻断 | 设计器使用容器树时若未手动同步 `Fields`，发布直接报 `FormFieldsRequired` 异常 | 统一以组件树/容器为核心，后端自动拍平同步 `Fields`，消除发布硬编码阻断 |
| **OA 流程** | **缺少“退回/驳回”、“转办”等核心审批动作** | 审批人选不通过直接导致整条流程异常终结，无法退回修改重提 | 扩展任务完成动作：`approve`（同意）、`reject_to_prev`（驳回上一步）、`reject_to_starter`（退回发起人）、`reject_terminate`（终止作废）、`transfer`（转办） |
| **OA 流程** | **缺少节点级表单字段权限控制** | 无法做到“发起人可写、主管只读、财务填报销金额”，权限控制形同虚设 | 节点引入 `fieldPermissions`（字段级 `read`/`write`/`hide`/`required` 矩阵） |
| **OA 流程** | **审批过程无法补充或修改表单数据** | 审批人只能输入一段意见，无法在审核时纠正或补充表单字段 | `CompleteWorkflowTaskDto` 支持 `RecordPatch`（字段补丁增量合并到流程表单） |
| **OA 流程** | **流程无版本快照，导致在途审批死锁** | 管理员修改发布流程后，在途任务按最新 JSON 寻址报错“流程里已无此节点” | 启动流程实例时固化保存 `ProcessSnapshotJson`，实例流转全生命周期基于快照寻址 |
| **OA 流程** | 部门主管被硬编码为全局 `manager` 静态角色，空审批人直接崩溃 | 不是发起人直属主管；某节点无人时系统直接抛异常卡死 | 完善直属部门主管解析，并增加 `emptyFallback`（转管理员/自动跳过/报错）容错策略 |

---

## 2. 表单系统重构升级规格

### 2.1 标准控件协议扩展

在 [ResourceSchema.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs) 中扩展控件类型定义：

```json
{
  "control": "userPicker", // 控件类型
  "multiple": false,       // 是否多选
  "controlProps": {
    "placeholder": "请选择审批人",
    "orgScope": "currentDept" // 范围限制：全员 / 本部门 / 指定角色
  }
}
```

支持的标准控件枚举：
- **基础输入**：`input`、`textarea`、`number`、`password`
- **选择与多选**：`radio`、`checkbox`、`select`、`cascader`（级联）
- **日期时间**：`date`、`datetime`、`time`、`dateRange`
- **组织架构**：`userPicker`（用户选择器）、`deptPicker`（部门选择器）
- **文件媒体**：`upload`（附件/图片上传）
- **开关与评价**：`switch`、`rate`、`slider`
- **布局与容器**：`grid`（栅格）、`card`（卡片）、`table`（明细子表）、`tabs`（标签页）

### 2.2 字段动态联动协议（Dynamic Dependencies）

在 `FormFieldDef` 与 `FormWidgetDef` 中增加联动规则 `dependencies`：

```csharp
public class FormFieldDependencyDef
{
    /// <summary>依赖的触发字段名</summary>
    public string SourceField { get; set; } = null!;

    /// <summary>操作符：eq | ne | in | notin | contains | isEmpty | isNotEmpty</summary>
    public string Op { get; set; } = "eq";

    /// <summary>比较的目标值</summary>
    public string? Value { get; set; }

    /// <summary>触发的动作：show | hide | enable | disable | require | optional | setValue</summary>
    public string Action { get; set; } = "show";

    /// <summary>当 Action=setValue 时的赋值内容或公式</summary>
    public string? SetValue { get; set; }
}
```

前端渲染器（`RecordFormFields`）与后端校验器根据此规则实现动态显隐、禁用与切换必填。

### 2.3 统一 Schema 规范与自动拍平

- 无论前端传入的是扁平 `Fields` 还是容器树 `Layout`，`FormDefinitionAppService` 在保存与发布时，**自动从容器树中递归提取所有叶子字段，自动填充并校准 `Fields`**。
- 彻底移除“必须手动维护扁平 Fields 才能发布”的硬编码限制，确保 WYSIWYG 栅格设计器与底层数据校验无缝衔接。

---

## 3. OA 流程系统重构升级规格

### 3.1 流程实例快照（Snapshot）与版本隔离

在 `WorkflowInstance` 实体中增加：
- `ProcessSnapshotJson`：在流程启动（`StartAsync`）时，直接复制当期已发布的 `definition.ProcessJson`。
- **执行原则**：流转期间所有找节点（`graph.Nodes.GetValueOrDefault`）、选出口（`ChooseNext`）、执行条件（`ConditionPasses`）全部**严格读取实例自身的 `ProcessSnapshotJson`**。
- 无论后台管理员如何编辑、新增、删除流程节点，均不影响既有历史在途审批单。

### 3.2 完备的审批流转动作机制

`CompleteWorkflowTaskDto` 扩充动作指令：

```csharp
public class CompleteWorkflowTaskDto
{
    /// <summary>
    /// 审批动作：
    /// approve (同意流转)
    /// reject_to_prev (驳回至上一审批节点)
    /// reject_to_starter (退回给发起人修改)
    /// reject_terminate (彻底作废终止)
    /// transfer (转办给他人)
    /// </summary>
    public string Action { get; set; } = "approve";

    /// <summary>兼容旧字段</summary>
    public bool Pass { get; set; } = true;

    /// <summary>审批处理意见</summary>
    public string? Opinion { get; set; }

    /// <summary>转办目标人（Action=transfer 时必填）</summary>
    public string? TransferUserName { get; set; }

    /// <summary>退回目标节点 ID（Action=reject_to_prev 时可选，空则默认上一节点）</summary>
    public string? TargetNodeId { get; set; }

    /// <summary>审批人在当前节点补充或修改的表单字段增量补丁</summary>
    public Dictionary<string, object?>? RecordPatch { get; set; }
}
```

#### 流转行为语义：
1. **`approve`（同意）**：
   - 记录当前节点通过；若满足多实例（会签/或签/比例）通过条件，推进至下一节点。
2. **`reject_to_starter`（退回给发起人）**：
   - 当前节点未完成任务全部取消；
   - 流程当前节点退回至 `start` 节点；
   - 为发起人生成一条状态为 `pending` 的修改待办，待发起人更新表单后重新提交。
3. **`reject_to_prev`（驳回到上一节点）**：
   - 从流程执行轨迹栈（History Stack）中查出上一经由的审批节点；
   - 取消当前节点待办，为上一节点的办理人重新激活并生成待办任务。
4. **`reject_terminate`（彻底作废）**：
   - 将流程标记为 `rejected` 终止态。
5. **`transfer`（转办）**：
   - 将当前任务的 `AssigneeUserName` 移交给指定的接手人，原任务生成转办日志。

### 3.3 节点级表单字段权限（Field Permission Matrix）

在 `WfProcessNode` 节点定义中引入字段权限字典：

```csharp
public sealed class WfProcessNode
{
    // ... 原有属性 ...

    /// <summary>
    /// 字段名 -> 权限类型：
    /// "read" (只读) | "write" (可编辑) | "hide" (隐藏不可见) | "required" (必填)
    /// 缺省字段默认按表单原生配置回退
    /// </summary>
    public Dictionary<string, string> FieldPermissions { get; set; } = [];

    /// <summary>
    /// 当选人算出来为空时的容错策略：
    /// "admin" (转交管理员) | "skip" (自动跳过该节点) | "error" (阻断并提示报错)
    /// </summary>
    public string EmptyFallback { get; set; } = "admin";
}
```

### 3.4 审批时表单数据回写（Form Patching）

- 审批人在办理待办提交 `RecordPatch` 时，`WorkflowRuntimeAppService` 将补丁内容与 `instance.RecordJson` 进行深度字段合并更新；
- 支持在审核阶段由关键角色（如 HR 填核定假种、财务填实付金额）在线补全单据信息。

### 3.5 审批历史流转轨迹记录（Workflow Execution History）

在 `WorkflowInstance` 中维护 `HistoryJson`（或专门的执行轨迹列表），记录每一次流转：
- `NodeId`、`NodeName`
- `OperatorUserName`
- `Action`（发起 / 同意 / 驳回 / 转办）
- `Opinion`
- `ExecutionTime`
- `Duration`

前端审批详情页基于该轨迹渲染清晰的审批时间轴（Timeline），并为“驳回到上一步”提供准确的目标节点索引。

---

## 4. 实施阶段计划

1. **第一阶段（后端领域与接口升级）**：
   - 升级 [ResourceSchema.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs) 数据模型（扩展控件类型、联动规则、字段权限矩阵）。
   - 改造 [FormDefinitionAppService.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FormDefinitionAppService.cs)，实现容器树自动提取同步 `Fields`。
   - 升级 [WorkflowInstance.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/WorkflowInstance.cs)，引入 `ProcessSnapshotJson` 与 `HistoryJson`。
   - 升级 [WorkflowRuntimeAppService.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/WorkflowRuntimeAppService.cs)，完整实现快照驱动流转、`RecordPatch` 数据回写、`reject_to_prev` / `reject_to_starter` 驳回退回机制。
2. **第二阶段（编译验证与测试保障）**：
   - 全解决方案执行 `dotnet build` 确保零错误。
   - 编写单元/集成测试验证退回、快照、字段合并流转。
3. **第三阶段（前端对接指引）**：
   - 更新文档为前端 `D:\Project\vue-demo` 团队提供对接字段与规范。

---

## 5. 表单定义能力深化（对照 form-create / VForm3 / Formily / 钉钉）

> §0 已确认 §2 表单侧核心控件、联动 `dependencies`、自动拍平已落地。本节补的是对照成熟方案后**仍缺的能力**，按优先级排列。参考：[form-create](https://www.form-create.com/designer/) 的 `validate`/`control`、[VForm3](https://www.vform666.com/vform3/) 的设计器/渲染器分离、[Formily](https://github.com/alibaba/formily) 的 `x-reactions`/`x-validator`、钉钉审批的字段权限矩阵。

### 5.1 字段校验规则协议（P1，当前缺标准化）

**现状不足**：`FormFieldDef` 有 `requiredOnCreate/Update/Detail` 与 `Dependencies`，但缺统一的 `rules` 协议。文档反复提"复用编排 InputSchema rules"，但表单侧没有与编排一致的规则 DSL，前端校验靠各控件自实现。

**对照**：form-create 每字段挂 `validate: [{ required, message, pattern, min, max }]`；Formily 用 `x-validator` 支持内置规则 + 自定义函数名；Ant Design Vue `rules` 也是这套。

**协议设计**（补入 `FormFieldDef`）：

```csharp
public class FormFieldRuleDef
{
    public string Kind { get; set; } = "required";
    // required | pattern | min | max | len | enum | custom
    public string? Pattern { get; set; }       // 正则
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public List<string>? Enum { get; set; }
    public string? CustomFlowKey { get; set; }  // 跨字段/查库校验走已发布 flowKey
    public string Message { get; set; } = "";  // 违规文案
    public string Trigger { get; set; } = "blur"; // change | blur
}
public List<FormFieldRuleDef> Rules { get; set; } = [];
```

**约束（原）**：`custom` 不内联任意 JS，只挂 `flowKey`（入参 `record`，出参 `{ valid: bool, message }`），与编排 Code 沙箱同级；前端先跑 `required/pattern/min/max`，`custom` 提交时调 `/api/logic/{flowKey}`。

> ⚠ **下方 §5.1.1 修订本约束**：经对照 form-create/VForm3/Formily 实践，"前端不内联 JS"过于保守，剥夺了前端灵活度。改为**双层校验**：前端 JS 灵活校验为体验，后端逻辑编排校验为权威。上方 `CustomFlowKey` 字段保留兼容，新增 `Validator`/`Deps`/`Severity`/`ValidateFlowKey`。

### 5.1.1 双层校验模型（2026-10 修订）

**原则：前端灵活 + 后端权威**

| 层 | 目的 | 形态 | 信任度 |
|----|------|------|--------|
| **前端层** | 即时反馈、用户体验 | 声明式规则 + JS 函数体（跨字段、异步、联动清空） | 不可信（用户可绕，等于绕自己） |
| **后端层** | 防绕过、权威 | 提交时走逻辑编排 `create/update` 流的 Code/Condition 节点；可选 `validateFlowKey` 试提交 | 唯一可信 |

> 对照：form-create `validate: [{required}, {validator:(rule,val,cb)=>cb()}]`；Formily `x-validator` 支持函数+字符串函数名；Ant Design Vue `rules.validator: async (rule,val)=>true|string`；VForm3 `onValidate` 配置扩展。**成熟方案都给前端 JS 灵活度，权威校验在后端。** 本协议对齐此分工。

**扩展协议（补入 `FormFieldRuleDef`，与上方原字段共存）**：

```csharp
public class FormFieldRuleDef
{
    /// required | pattern | min | max | len | enum | validator | asyncValidator
    public string Kind { get; set; } = "required";

    // —— 声明式参数 ——
    public string? Pattern { get; set; }       // 正则
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public int? Len { get; set; }
    public List<string>? Enum { get; set; }

    /// 前端 JS 灵活校验函数体（Kind=validator/asyncValidator 时用）
    /// 签名：(value, row, ctx) => boolean | string | {valid,message} | Promise<同左>
    /// 返回 true=通过；string=失败文案；{valid,message}=显式；Promise=异步
    public string? Validator { get; set; }

    /// 该规则依赖的其它字段名；任一变化时自动重校验本字段（跨字段联动校验）
    public List<string> Deps { get; set; } = [];

    public string Message { get; set; } = "";         // 静态文案（Validator 返回非空时优先）
    public string Trigger { get; set; } = "blur";    // change | blur | submit
    public string Severity { get; set; } = "error";  // error | warning
}
public List<FormFieldRuleDef> Rules { get; set; } = [];

/// 提交时权威校验流（可选；create/update 流自带校验时可不填）
/// 入参 record，出参 { valid, errors:[{field,message}] }
public string? ValidateFlowKey { get; set; }
```

**前端 `ctx` 校验上下文（只读快照，沙箱隔离）**：

| 字段 | 说明 |
|------|------|
| `ctx.fields` | 当前表单所有字段值（跨字段校验用） |
| `ctx.sys` | `{ userId, tenantId, now, culture }` 只读快照 |
| `ctx.dict(code)` | 取字典项（与列表 `transform.dict` 同源） |
| `ctx.t(key)` | i18n 文案 |
| `ctx.formRef` | 当前表单实例引用（调控件 API，如清空/聚焦） |

**禁止**：直连 DB；`document/window/storage` 沙箱隔离；`eval/new Function` 由运行时统一注入受控（不开放给设计器用户直接写）。

**前端 JS 校验示例**：

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
// （rule.Severity = "warning"；前端可拦截 warning 让用户确认后继续提交）

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

**后端权威校验（提交时必跑）**：

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

- `validateFlowKey`（可选）：独立的"试提交"校验流，前端点"校验"按钮或异步 rule 调用，入参 `record`，出参 `{ valid, errors:[{field,message}] }`；正式 `create/update` 流仍**自带**校验，不依赖前端是否调了 `validateFlowKey`。
- 后端校验代码在编排 Code 节点，与编排安全同级：参数化、白名单、`sys.*` 注入、`[Authorize]` + flowKey 必跑。

**安全边界（关键）**：

| 关注点 | 前端 | 后端 |
|--------|------|------|
| 信任度 | 不可信（浏览器用户可控） | 唯一可信 |
| 绕过后果 | 用户绕自己，无漏洞 | 漏洞，必须防 |
| JS 执行 | 自由写函数体，沙箱可选（防意外 DOM 污染，不防恶意） | 不跑用户 JS，只跑编排 Code 节点（白名单+参数化） |
| 查库 | 只调 `validateFlowKey`（受控流），不直连 DB | 编排节点 `db.*`，白名单 DS |
| 防滥用 | 异步 rule 限流（同字段 change 内只跑一次） | `traceMode=errors` + `visibleTo` 受控 |

**与联动（§5.2）的关系**：

- §5.2 联动改值后，自动触发 `Deps` 命中字段重校验。
- `compute` 联动可在前端用 JS 表达式（与校验同沙箱），但**写库值以 `create/update` 流 Code 节点重算为准**——前端 `compute` 结果仅作展示，后端不信任前端算的值，防止篡改计算结果绕过金额计算等关键逻辑。

### 5.2 高级联动：级联取数 / 异步选项 / 表达式计算（P1，当前仅显隐）

**现状不足**：`dependencies` 只做 show/hide/enable/disable/require/setValue，但常见的"改省份自动拉城市""改客户自动带出信用额度""金额=单价×数量"都没有协议。

**对照**：Formily `x-reactions` 用 `fulfill.state` + `fulfill.run`（受控表达式）；VForm3 `eventFunc` 在 onChange 触发；form-create `control` 触发其它字段显隐/赋值。钉钉审批有"公式字段"。

**协议设计**（扩展 `FormFieldDependencyDef.Action` 枚举与 `SetValue` 语义）：

| 新 Action | 含义 | SetValue 形态 |
|-----------|------|---------------|
| `loadOptions` | 改 A 后异步拉 B 的选项（如城市） | `flowKey` + 输出映射到 B 的 `options` |
| `fetchValue` | 改 A 后查库回填 B（如信用额度） | `flowKey`，入参取 A，出参写 B |
| `compute` | 表达式计算（金额=单价×数量） | 前端 JS 表达式（与 §5.1.1 校验同沙箱），写库以 `create/update` 流重算为准 |
| `cascade` | 级联清空依赖项（改省时清空市） | 目标字段名数组 |

**约束**：`loadOptions`/`fetchValue` 必须挂已发布 `flowKey`，不在前端跑任意查询；`compute` 可在前端用 JS 表达式（与 §5.1.1 校验同沙箱，支持字段路径与算术），**但写库值以 `create/update` 流 Code 节点重算为准**——前端 `compute` 仅作展示，后端不信任前端算的值；旧"白名单解析器禁止 eval"方案废弃，改为双层信任（前端灵活 + 后端权威）。

### 5.3 子表 / 明细的行级权限与行联动（P2，当前缺）

**现状不足**：`table`/`list` 控件已支持，但缺①行级字段权限（与节点 `FieldPermissions` 类似但按行）、②行级联动（改某行单价时该行金额重算）、③行数量上下限、④行必填校验。

**对照**：钉钉明细表支持行级公式、行级必填、行数量限制；VForm3 子表 `subForm` 支持行级联动。

**协议设计**（`table` 控件的 `ControlProps` 扩展）：

```json
{
  "control": "table",
  "controlProps": {
    "minRows": 0, "maxRows": 20,
    "rowRules": [ { "field": "Amount", "kind": "required", "when": "Qty > 0" } ],
    "rowCompute": [ { "field": "Amount", "expr": "Price * Qty" } ],
    "addColumnFlowKey": "order.addLine"   // 加行时可调流（如带默认值）
  }
}
```

行级联动复用 §5.2 的 `compute`/`fetchValue`，作用域限当前行。

### 5.4 表单版本与发布快照（P1，当前缺）

**现状不足**：`FormDefinition` 有草稿/已发布 `Status`，但发布是否生成不可变快照未确认（`lowcode-form-preview.md` §4.3 已标注"实现时核对 PublishAsync 是否生成不可变快照"）。在途 OA 实例引用 `formRef`，若发布即改 Schema，审批中的表单结构会漂移。

**对照**：Flowable 表单有 `FormVersion`；飞书审批模板有版本号；Formily Schema 带 `version`。

**协议设计**：
- `FormDefinition` 增 `PublishedSchemaJson` + `PublishedVersion`（int，每次发布 +1）。
- `PublishAsync` 把当前草稿 `SchemaJson` 深拷贝到 `PublishedSchemaJson`，`Status=Published`，`PublishedVersion++`。
- OA `WorkflowInstance` 启动时除 `ProcessSnapshotJson` 外，再固化 `FormSnapshotJson = form.PublishedSchemaJson`（与流程快照同生命周期）。
- 运行时渲染/校验**只读** `FormSnapshotJson`，草稿编辑不影响在途单。
- 与 `lowcode-form-workflow-board.md` §5.13"资源发布运行时只读已发布 Schema"对齐。

### 5.5 表单与资源的字段映射契约（P1，当前隐式）

**现状不足**：`FormDef.Fields` 与 `TableDefinition.Columns` 的映射靠字段名同名隐式匹配，缺显式 `columnName` 映射，改名后表单提交会丢字段。

**协议设计**：`FormFieldDef` 增 `ColumnName`（默认等于 `Name`），提交 `record` 时按 `ColumnName` 投影到库；列改名后只需改 `ColumnName`，不用改表单控件 key。

### 5.6 多语言与布局预设（P2）

**现状不足**：标题/占位符是单语言字符串；布局无紧凑/宽松预设。

**协议设计**：`title`/`placeholder`/`description` 支持 `i18n: { "zh-CN": "...", "en-US": "..." }`，运行时按 `sys.culture` 取；`FormDef.layoutPreset: compact | default | spacious`，控制栅格间距与字号。

### 5.7 表单设计细化与动态按钮配置（P1，当前缺操作细项）

> §5.1–5.6 补的是"协议字段"，本节补的是"设计器怎么用 + 按钮怎么动态挂"。对照 form-create/VForm3 设计器三栏交互、钉钉/飞书审批的按钮动态化。

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

**通用项（FormCommonProps，所有控件）**：

| 分组 | 字段 | 说明 |
|------|------|------|
| 基本 | `key` | 字段名（提交 key，唯一） |
| 基本 | `columnName` | 库列映射（§5.5） |
| 基本 | `label`/`labelI18n` | 标题/多语言 |
| 基本 | `placeholder`/`help`/`tooltip` | 占位/帮助/提示气泡 |
| 校验 | `rules` | §5.1.1 规则数组 |
| 校验 | `requiredOnCreate/Update/Detail` | 节点级必填快捷（与 FieldPermissions 联动） |
| 布局 | `span` | 栅格占列（1-24），默认 12 |
| 布局 | `labelWidth`/`labelPosition` | 标签宽/位置（top/left） |
| 布局 | `offset`/`push`/`pull` | 栅格偏移 |
| 联动 | `dependencies` | §5.2 显隐/赋值/级联 |
| 联动 | `defaultValue`/`defaultValueExpr` | 创建默认值（字面量或 JS 表达式，如 `ctx.sys.now`） |
| 权限 | `fieldPermissions` | 按节点 read/write/hide/required（§3.3） |
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

#### 5.7.4 数据源与字典统一管理

**现状不足**：`select`/`cascader` 选项来源零散，有的硬编码、有的调 flowKey、有的查字典，无统一协议。

**协议**：选项来源三选一，优先级 `dictCode` > `optionsFlowKey` > `options`：
- `dictCode`：挂 `sys_dict` 字典码，前端缓存 5 分钟，后端 `SettingManagement` 维护（与报表 `transform.dict` 同源）。
- `optionsFlowKey`：挂已发布流，入参 `{ parentValue? }`（级联用），出参 `[{label,value,disabled?}]`，`isReusable`+`visibleTo` 受控，前端按 `parentValue` 缓存。
- `options`：静态数组，适合性别这类固定枚举。

**字典管理**：管理端增"字典管理"菜单（`sys_dict` + `sys_dict_item`），支持树形字典（省市区）；表单设计器选项面板可"从字典选"下拉。

#### 5.7.5 动态化按钮配置协议（ButtonDef，核心）

**现状不足**：表单底部按钮（提交/保存/暂存）、列表 action 按钮、审批节点按钮各自硬编码，无统一动态配置协议。钉钉/飞书审批的按钮是按节点 + 权限 + 流程状态动态渲染的。

**统一协议**（补入 `FormDef` / `AppResource.ListView` / `WfProcessNode`）：

```csharp
public class ButtonDef
{
    public string Key { get; set; } = "";          // 唯一，如 submit/save/approve/reject
    public string Label { get; set; } = "";        // 文案（支持 i18n）
    public string? LabelI18n { get; set; }
    public string? Icon { get; set; }              // 图标名
    public string Type { get; set; } = "default";  // primary|default|dashed|danger|link
    public string Scene { get; set; } = "form";    // form(表单底部)|toolbar(列表工具栏)|row(行内)|node(审批节点)
    public string? Permission { get; set; }        // 权限码，如 Order.Approve
    public string? FlowKey { get; set; }           // 点击调用的已发布流（提交/审批/自定义业务）
    public string? ValidateFlowKey { get; set; }   // 点击前试校验流（可选，§5.1.1）
    public string? BeforeExpr { get; set; }        // 前端 JS：点击前钩子（二次确认/数据预处理），返回 false 取消
    public string? AfterExpr { get; set; }         // 前端 JS：成功后（跳转/刷新/发消息）
    public string? VisibleExpr { get; set; }      // 前端 JS：可见条件（按状态/角色/节点）
    public string? DisabledExpr { get; set; }      // 前端 JS：禁用条件
    public string? Confirm { get; set; }           // 二次确认文案（非空则弹确认框）
    public int? BatchLimit { get; set; }            // 行/选择场景单批上限（与报表 ActionDef.batchLimit 同义）
    public string? Redirect { get; set; }          // 成功后跳转路由
    public bool RefreshAfter { get; set; } = true; // 成功后刷新当前列表/详情
}
public List<ButtonDef> Buttons { get; set; } = [];
```

**按钮三类来源的统一**：

| 场景 | 挂载点 | 示例 |
|------|--------|------|
| 表单底部 | `FormDef.Buttons` | 提交(save→create流)/暂存(draft)/取消 |
| 列表工具栏/行内 | `AppResource.ListView.Buttons`（与报表 `ActionDef` 合流） | 新增/导出/批量删除/行内编辑 |
| 审批节点 | `WfProcessNode.Buttons`（覆盖默认 approve/reject） | 自定义"加签""委办""退回上级" |

**关键：审批按钮也走 ButtonDef**。当前 `approve/reject_*` 是硬编码动作，改为节点配 `Buttons: [{Key:"approve",FlowKey:"order.afterApprove",...},{Key:"reject_to_prev",...}]`，前后端统一渲染；缺省时节点按 `multi`+`assigneeType` 自动生成默认按钮。

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

**与 §5.1.1 双层校验对齐**：`BeforeExpr` 是体验层（可绕），`FlowKey` 内 Code 节点是权威层（不可绕）；`ValidateFlowKey` 是按钮级的"试提交"，与字段级 `validateFlowKey` 互补。

#### 5.7.7 按钮与权限/状态/节点的动态联动

| 联动维度 | 实现 |
|----------|------|
| 按角色显隐 | `Permission` 权限码 + `VisibleExpr`（`ctx.sys.roles.includes('admin')`） |
| 按表单状态显隐 | `VisibleExpr` 读 `ctx.record.Status`（草稿显示"提交"，已提交显示"撤回"） |
| 按审批节点显隐 | 审批按钮挂在 `WfProcessNode.Buttons`，仅当前节点渲染；`VisibleExpr` 读 `ctx.nodeId===current` |
| 按数据状态禁用 | `DisabledExpr`（库存为 0 时禁用"出库"按钮） |
| 按选择数动态 | 列表 `BatchLimit` + `DisabledExpr`（未选行时禁用批量按钮） |
| 按钮顺序 | `Buttons` 数组顺序即渲染顺序；审批节点默认按钮可在数组前补自定义按钮 |

**对照钉钉/飞书**：钉钉审批按钮按节点 + 角色动态渲染（审批人见"同意/拒绝/转办"，发起人见"撤回"，抄送人无按钮）；飞书列表 action 按状态显隐。本协议用 `VisibleExpr`/`DisabledExpr`/`Permission` 三件套覆盖这些场景，且表达式走前端 JS（与 §5.1.1 同沙箱），权威判断在后端 `FlowKey` 内 Code 节点（防前端绕过）。

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

### 5.8 报表按钮与表单关联的缺陷与补充（P1，当前 ActionDef 与 ButtonDef 割裂）

> §5.7.5 把 `ButtonDef` 统一到 `FormDef.Buttons` / `ListView.Buttons` / `WfProcessNode.Buttons`，并写"列表 `AppResource.ListView.Buttons` 与报表 `ActionDef` 合流"。但 `lowcode-report-management.md` §6 的 `ActionDef` 字段（scene/scope/open/fieldsMode/payload/after/batchLimit）与 `ButtonDef` 字段（FlowKey/BeforeExpr/AfterExpr/VisibleExpr/DisabledExpr/Permission）**并未真正对齐**，且"按钮打开表单"这条主链路有多处缺陷。本节补全。

#### 5.8.1 缺陷清单（12 项）

| # | 缺陷 | 影响 |
|---|------|------|
| 1 | `ActionDef` 无 `formKey`/`formMode`，打开表单只能用资源默认 form | 一个资源多表单（紧凑列表/完整编辑/只读详情）无法按场景选 |
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

#### 5.8.2 统一协议：ActionDef ⊂ ButtonDef（合流）

**决策**：`ActionDef` 不再独立，改为 `ButtonDef` 的"列表/报表场景特化"。`ButtonDef` 增报表相关字段，`ActionDef` 保留为别名兼容：

```csharp
public class ButtonDef
{
    // —— §5.7.5 已有字段 ——
    public string Key; public string Label; public string? Icon;
    public string Type; public string Scene;     // form|toolbar|row|node
    public string? Permission; public string? FlowKey; public string? ValidateFlowKey;
    public string? BeforeExpr; public string? AfterExpr;
    public string? VisibleExpr; public string? DisabledExpr;
    public string? Confirm; public int? BatchLimit;
    public string? Redirect; public bool RefreshAfter;

    // —— 报表/列表场景补充（原 ActionDef 字段合流）——
    public string? Scope { get; set; }          // none|row|selection|cell|column（报表行/选择/单元格/列）
    public string? Open { get; set; }            // none|modal|drawer|page|link|inline（打开方式，inline=行内编辑）
    public string? FormKey { get; set; }         // 打开的表单（缺省=资源默认 form）
    public string? FormMode { get; set; }        // create|edit|view|audit（缺省按 Key 推断：submit→create, edit→edit）
    public string? FieldsMode { get; set; }      // full|whitelist|none（表单字段白名单，用于精简行内编辑）
    public List<string>? FieldsWhitelist { get; set; } // fieldsMode=whitelist 时的字段列表
    public string? PayloadToFields { get; set; } // payload → 表单 defaultValue 映射（JSON：{formField: "expr|payloadKey"}）
    public string? DetailFlowKey { get; set; }   // 打开详情时取完整 record（含子表）的流
    public string? CanOpenExpr { get; set; }     // 打开前预校验（如 Status!='approved'），前端 JS，返回 false 禁用并 tooltip
    public string? RefreshScene { get; set; }    // list(刷报表)|detail(刷详情)|none（覆盖 RefreshAfter 的细化）
    public bool CloseAfterSubmit { get; set; } = true; // 表单提交后关弹窗（false=留在表单继续录）
}
```

**对应关系**：原 `ActionDef.after` → `ButtonDef.AfterExpr` + `RefreshScene` + `CloseAfterSubmit`；原 `ActionDef.fieldsMode` → `ButtonDef.FieldsMode` + `FieldsWhitelist`；原 `ActionDef.batchLimit` → `ButtonDef.BatchLimit`。废弃 `ActionDef` 独立类，保留别名兼容期。

#### 5.8.3 表单模式与模式级字段权限（补 §5.7.2 通用项）

**协议**：`FormMode: create | edit | view | audit`（audit=OA 审批节点办理）。表单字段权限三层叠加：

```
最终字段权限 = 模式级 baseline(FieldPermissions by mode) 
              ∩ OA 节点级(若 audit 模式，WfProcessNode.FieldPermissions)
              ∪ 显式覆盖(按钮 FieldsWhitelist)
```

| 模式 | baseline 默认 | 按钮场景 |
|------|--------------|----------|
| `create` | 所有字段可写（除系统列） | 表单底部"提交/暂存" |
| `edit` | 主键+系统列只读，余可写 | 行内/弹窗"编辑" |
| `view` | 全部只读 | "查看详情"，无提交按钮 |
| `audit` | 按 OA 节点 `FieldPermissions` | 审批节点按钮（§5.7.5） |

`FormFieldDef` 增 `modePermissions: { create:{read,write,hide,required}, edit:{...}, view:{...} }`，非 OA 场景用此；OA 场景 `audit` 模式读 `WfProcessNode.FieldPermissions` 覆盖。

#### 5.8.4 数据回填与 payload 映射

**行 record → 表单**（`Open=modal/drawer/page` + `FormMode=edit/view`）：
- 默认：报表当前行 record 整体作为表单 `initialValues`，按 `FormFieldDef.ColumnName` 投影（§5.5）。
- 子表回填：若表单含 `table` 子表且行 record 不含子表数据，调 `DetailFlowKey` 取完整 record（含子表），入参 `{ id: row.Id }`，出参 `{ record, subTables: {lines:[...]} }`。

**payload → 表单默认值**（`PayloadToFields`，"基于当前行新建"场景）：
```json
{
  "PayloadToFields": {
    "CustomerId": "row.CustomerId",      // 取报表行的 CustomerId
    "OrderDate": "ctx.sys.now",            // 取系统时间
    "Source": "'referral'"                 // 字面量
  }
}
```
映射值支持：`row.{field}`（报表行字段）、`ctx.sys.*`、`'{literal}'`（字面量字符串）。与 §5.7.2 `defaultValueExpr` 同沙箱，前端 JS 求值后作为 `create` 模式初始值。

#### 5.8.5 提交后刷新策略（统一 after 协议）

废弃 `ActionDef.after`，统一用 `ButtonDef` 三字段：

| 字段 | 取值 | 行为 |
|------|------|------|
| `CloseAfterSubmit` | true/false | 提交后关弹窗（false=留在表单继续录，适合连续录入） |
| `RefreshScene` | list/detail/none | 刷新目标（list=刷报表，detail=刷表单详情，none=不刷） |
| `AfterExpr` | JS | 自定义（跳转 `Redirect`/发消息/调其它流），最后执行 |

默认：`CloseAfterSubmit=true` + `RefreshScene=list` + `RefreshAfter=true`（与 §5.7.5 默认一致）。

#### 5.8.6 权限解析链（打开表单全链路）

```
报表按钮点击
  → ① 报表访问权限：App.Report.{code}（已落地，iam-rbac-menu §7）
  → ② 按钮权限：ButtonDef.Permission，如 Order.Edit（前端 VisibleExpr 先过滤，后端 flowKey 内 [Authorize] 二次确认）
  → ③ 打开表单：按 FormMode 取 modePermissions baseline
  → ④ 若 audit 模式：叠加 WfProcessNode.FieldPermissions
  → ⑤ 表单内按钮权限：FormDef.Buttons[*].Permission（如 Order.Approve 仅审批节点显示）
  → ⑥ 提交流 FlowKey 内 Code 节点：权限最终确认（防前端绕过 ① ② ⑤）
```

关键：前端 `VisibleExpr`/`DisabledExpr`/`Permission` 是体验过滤（可绕），后端 `FlowKey` 内 Code 节点 `[Authorize]` + 权限码校验是权威（不可绕）。

#### 5.8.7 批量编辑表单（补 §6.4 之外的批量场景）

**协议**：`ButtonDef` 增 `BatchEdit: { enabled: true, fields: [...] }`（仅 `Scope=selection` 时生效）。
- 用户选 N 行 → 点"批量编辑" → 弹精简表单（只含 `BatchEdit.fields` 字段）→ 填公共值 → 提交。
- 提交流入参 `{ ids: [...], patch: {字段:值} }`，流内 Code 节点循环 `UpdateAsync` 或批量 SQL（参数化），受 `BatchLimit` 上限。
- 校验：每个字段的 `rules` 跑一次（不按行跑，因为是公共值）；行级业务校验在流内按 id 循环。

#### 5.8.8 行内编辑与表单校验统一

**协议**：`Open=inline`（行内编辑）时：
- 单元格用对应字段的 `control` + `controlProps` + `rules` 渲染（与表单同协议，不是独立控件）。
- 失焦时跑该字段 `rules`（含异步），失败标红 + tooltip。
- 提交时调 `FlowKey`（或资源默认 `updateFlowKey`），入参 `{ id, patch: {field:value} }`，流内 Code 节点做权威校验。
- `FieldsMode=whitelist` + `FieldsWhitelist` 控制哪些列可行内编辑（与弹窗表单字段白名单同源）。

**关键**：行内编辑与弹窗表单**共用** `FormFieldDef.rules` + `FlowKey` 校验，不再两套校验逻辑。

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

## 6. OA 流程能力深化（对照 Workflow-Vue3 / lowflow-design / 钉钉 / 飞书 / BPMN）

> §0 已确认五动作、会签/或签/比例、快照、字段权限、`RecordPatch`、`EmptyFallback` 已落地。本节补的是对照成熟审批产品后**仍缺的能力**。参考：[Workflow-Vue3](https://stavinli.github.io/Workflow-Vue3/dist/index.html#/)、[lowflow-design](https://tsai996.github.io/lowflow-design/)、钉钉/飞书审批、BPMN 网关语义。

### 6.1 直属部门主管解析（P1，已知缺口）

**现状**：`case "manager"` 返回 `AssigneePick(null, 角色码)`，按角色匹配，未接组织树（[lowcode-oa-workflow.md](./lowcode-oa-workflow.md) §6 自述）。

**协议设计**：
- `ResolveAssigneesAsync` 增 `case "manager"` 真正分支：取 `instance.StarterUserId` → 查 `AbpOrganizationUnits` 主部门 → 取部门 `manager` 角色 OU 关联用户。
- 兜底：无主管时按 `EmptyFallback`（admin/skip/error）。
- 增 `case "role"` 显式分支：与 `manager` 区分——`role` 是"角色内任一人可办"（一个名额），`manager` 是"组织树解析直属主管"。
- 与 `iam-rbac-menu.md` §5 组织机构 + §8 数据权限复用同一 `DataScopeResolver` 的 OU 解析。

### 6.2 抄送 `cc` 节点的真实通知（P1，当前只记不送）

**现状**：`cc` 节点"只记通知，不挡流程"，但通知如何送达未定义（站内/IM/邮件？）。

**协议设计**：`cc` 节点办理时调 `afterFlowKey` 或直接走 `RabbitMqPublish`，载荷含 `assignee/users` + `instance` 摘要；消费端对接站内消息 / 飞书 IM（`lark-im`）/ 邮件（`SettingManagement.Emailing`）。抄送不生成 `WorkflowTask`，只写 `HistoryJson` 一条 `cc` 记录。

### 6.3 超时与催办（P1，当前缺）

**现状**：文档 §6.1 提"超时提醒"未落地；`WorkflowTask` 无到期字段。

**协议设计**：
- `WfProcessNode` 增 `TimeoutMinutes` + `RemindMinutes`（提前提醒）+ `TimeoutAction: skip | escalate | notify`。
- `WorkflowTask` 增 `DueAt`（启动时算）+ `RemindedAt`。
- 后台 Job（Hangfire/Quartz）扫 `DueAt < now && Status=pending`，按 `TimeoutAction` 处理：`skip` 自动通过、`escalate` 转上级、`notify` 发消息。
- 前端"我的待办"标红超时项；列表支持"催办"按钮（给办理人发提醒）。

### 6.4 加签 / 减签 / 委办（P2，对照钉钉）

**现状**：仅 `transfer`（转办，整体移交）。缺钉钉常见的加签/减签/委办。

| 动作 | 语义 | 与 transfer 区别 |
|------|------|------------------|
| `addsign_before` | 前加签：当前人提交前，先让指定人审 | 不移交，加一关 |
| `addsign_after` | 后加签：当前人通过后，加指定人再审 | 当前人不算办完 |
| `countersign` | 会签加签：临时把单人节点变会签 | 改 multi |
| `delegate` | 委办：委托他人代办，办完记回原人 | 不移交，原人仍可收回 |
| `remove_sign` | 减签：撤销某候选人的审批权 | 仅多实例节点 |

**协议设计**：`CompleteWorkflowTaskDto.Action` 扩展上述枚举；加签在 `HistoryJson` 记一条 `addsign` 轨迹，原任务不取消，新增 `WorkflowTask` 挂同节点。

### 6.5 流程撤回与自选审批人（P2，对照钉钉/飞书）

**撤回**：发起人对未进入审批的实例主动撤回。`Action: withdraw`，仅当首个 `approver` 节点无任何 `approved` 任务时允许；撤回后实例 `Status=withdrawn`。
**自选审批人**：`approver` 节点 `assigneeType=starterSelect`，发起人在提交表单时指定该节点的办理人；存入 `record.__approver_{nodeId}`，运行时按 `formField` 读取。

### 6.6 审批人去重（P1，当前缺）

**现状**：同一人在链路多次出现会收到多条待办。

**协议设计**：`ResolveAssigneesAsync` 返回候选后，过滤"该用户在本实例 `HistoryJson` 已 `approved` 的节点"，去重后不生成新任务，直接记 `autoSkip` 轨迹。参考钉钉"审批人去重自动跳过"。

### 6.7 并行网关与汇合（P3，对照 BPMN）

**现状**：当前是纵向链式 + 连线条件分支，缺并行（多分支同时走、全部到齐才向下）。

**协议设计**：`WfProcessNode` 增 `type=parallel`（fork，一进多出全激活）+ `type=join`（汇合，等所有入边任务完成才向下）。与编排引擎的 `Parallel`/`Wait`（[lowcode-logic-orchestration.md](./lowcode-logic-orchestration.md) §11.3 P2）区分——人审并行在审批引擎，不在编排画布。P3 评估是否引入 bpmn-js 画布。

### 6.8 子流程嵌套（P3）

**现状**：编排有 `SubFlow`，审批无子流程。

**协议设计**：`WfProcessNode` 增 `type=callActivity`，`callActivityDef: { workflowKey, inputMapping, outputMapping }`；父实例创建子实例，子实例结束按 `outputMapping` 回写父 `record`。用于"报销含出差申请"场景。

### 6.9 流程版本迁移（P1，当前靠快照规避）

**现状**：`ProcessSnapshotJson` 保证在途实例不受定义变更影响，但**新版本如何让在途实例迁移**未定义。

**协议设计**：
- `WorkflowDefinition` 增 `PublishedVersion` + `MigrationPlan`（节点 ID 映射表：旧节点 → 新节点）。
- 管理员发布新版时可选填 `MigrationPlan`；后台 Job 扫在途实例，按映射把当前节点 ID 改到新版本对应节点，`ProcessSnapshotJson` 替换为新版 DSL。
- 无映射的实例继续按旧快照跑完，不强制迁移。

### 6.10 流程模板库与导出导入（P2）

**协议设计**：`WorkflowDefinition` 增 `IsTemplate`；管理端"模板库"一键克隆为草稿；`ExportAsync` 输出 `ProcessJson + FormSnapshotJson` 单文件 JSON，`ImportAsync` 校验后入库。参考飞书审批模板中心。

### 6.11 批量审批（P1，对照钉钉）

**现状**：`my-tasks` 一次一条。

**协议设计**：`POST /tasks/batch-complete`，入参 `taskIds[]` + 共同 `Action` + `Opinion`；后端循环 `CompleteAsync`，同 DS 事务；前端列表多选 + 批量通过/驳回。限制单批上限（如 50）。

---

## 7. 优先级总览

| 能力 | 优先级 | 关联代码 |
|------|--------|----------|
| §5.1 字段校验 rules 协议 | P1 | `FormFieldDef` |
| §5.2 级联取数 / 异步选项 / 计算 | P1 | `FormFieldDependencyDef` |
| §5.4 表单发布快照 | P1 | `FormDefinitionAppService.PublishAsync` + `WorkflowInstance.FormSnapshotJson` |
| §5.5 字段映射 `ColumnName` | P1 | `FormFieldDef` |
| §6.1 直属部门主管 + role 显式分支 | P1 | `WorkflowRuntimeAppService.ResolveAssigneesAsync` |
| §6.2 抄送通知 | P1 | `cc` 节点 + `RabbitMqPublish` |
| §6.3 超时与催办 | P1 | `WfProcessNode` + `WorkflowTask` + 后台 Job |
| §6.6 审批人去重 | P1 | `ResolveAssigneesAsync` + `HistoryJson` |
| §6.9 版本迁移 | P1 | `WorkflowDefinition.MigrationPlan` |
| §6.11 批量审批 | P1 | `WorkflowRuntimeAppService` + API |
| §5.3 子表行级权限/联动 | P2 | `table` 控件 `ControlProps` |
| §5.6 多语言/布局预设 | P2 | `FormFieldDef` + `FormDef` |
| §6.4 加签/减签/委办 | P2 | `CompleteWorkflowTaskDto.Action` |
| §6.5 撤回/自选审批人 | P2 | `Action: withdraw` + `assigneeType=starterSelect` |
| §6.10 模板库/导出导入 | P2 | `WorkflowDefinition.IsTemplate` |
| §6.7 并行网关/汇合 | P3 | `WfProcessNode` fork/join |
| §6.8 子流程嵌套 | P3 | `callActivity` 节点 |

**落地顺序建议**：先 P1 表单侧（§5.1/5.2/5.4/5.5）与 P1 流程侧（§6.1/6.2/6.3/6.6/6.9/6.11）同步推进，前者保证表单契约稳定，后者补齐审批闭环；P2 再做加签/撤回/子表行级；P3 评估并行网关与子流程是否必须，否则继续自研轻量引擎不上 bpmn-js。

---

## 8. 定义侧健壮性缺陷（2026-10 代码审计补充）

> **本节定位**：§0 的核对表回答“能力**有没有**落地”，本节回答“定义链**稳不稳**”。两者不冲突——表单/流程定义侧目前**几乎没有约束**，配置错误不会被拦在发布口，而是在运行时以最坏的形态爆发。以下每条均附代码位置与实证。
>
> **一句话结论**：两处定义链的共同病根是「把语义存成自由字符串，然后信任调用方」。表单侧有强类型但三套 Schema 各说各话；OA 流程侧连类型都没有（`ProcessJson` 就是 `string`）。更关键的是——**逻辑编排有发布校验（`FlowDefinitionAppService` 调 `FlowExecutor.ValidateDsl`），表单和 OA 流程一条都没有**。

### 8.1 表单定义缺陷（F1–F7）

| 编号 | 缺陷 | 触发条件 / 实证 | 影响 | 代码位置 | 修复方向 |
|:--:|:---|:---|:---|:---|:---|
| **F1** | **拍平只增不覆盖，改了等于没改** | 设计器修改一个**已存在**字段（改必填/控件/标题）时，`EnsureFlattenedFields` 用 `if (!existingMap.ContainsKey(field.Field))` 判定，已有同名字段直接跳过，新定义不写入；删除字段时 `Fields` 也不收缩 | `PublishAsync` 仅校验 `Fields.Count > 0`，于是**发布的是一份陈旧字段定义**；长期残留“幽灵字段” | [FormDefinitionAppService.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FormDefinitionAppService.cs) `EnsureFlattenedFields` L139–159、`PublishAsync` L129–132 | 改为「以 Layout 为准的覆盖式同步」：对同名字段做属性覆盖，并删除 Layout 中已不存在的字段 |
| **F2** | **拍平与克隆会丢字段（明细子表直接失真）** | ① `ExtractFieldsFromWidgets` 构造 `FormFieldDef` 时**不复制** `Rules` / `Children` / `MinItems` / `MaxItems` / `TreeChildrenField`；② `FormWidgetDef` 本身也缺 `MinItems`/`MaxItems`/`TreeChildrenField`；③ `CloneWidget` 与 `CloneField` **都不复制** `Dependencies` / `ControlProps` | `table`/`list`/`tree` 明细经 Layout→Fields 拍平后**子列结构、行数限制、校验规则全部丢失**；从资源同步表单时**字段联动规则与控件配置被静默清空** | 同文件 `ExtractFieldsFromWidgets` L161–192、`CloneWidget` L237–258、`CloneField` L260–280；[ResourceSchema.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs) `FormWidgetDef` L375+ | 合并 F3 的重复模型，拷贝时用整体映射而非逐属性手抄 |
| **F3** | **`FormFieldDef` 与 `FormWidgetDef` 是两个几乎重复的类** | `Field`/`Title`/`Control`/`Span`/`Placeholder`/`VisibleOn*`/`ReadonlyOn*`/`RequiredOn*`/`Dependencies`/`ControlProps` 在两个类里各写一遍，差异只在明细相关字段 | 是 F2 那些漏拷贝 bug 的**根因**——每加一个属性就要改三处拷贝代码，必然漏 | `ResourceSchema.cs` `FormFieldDef` L315–369、`FormWidgetDef` L375–421 | 只保留一个模型，另一处用组合表达 |
| **F4** | **发布不产生快照，契约随时漂移** | `Publish()` 只是把 `Status` 置为 Published，`Schema` 还是**同一个可变对象**；`GetPublishedByCodeAsync` 返回的就是当前 Schema | **已发布的表单可以被下一版直接改写**；OA 流程冻结了流程快照，却引用着一份**会变的表单**，在途审批单结构随之漂移 | [FormDefinition.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/FormDefinition.cs) `Publish()` L70；`FormDefinitionAppService.GetPublishedByCodeAsync` L58–68 | 发布时生成**不可变 Schema 快照 + `SchemaVersion`**；实例引用快照而非 Code |
| **F5** | **字段无唯一性与合法性校验** | 字段名不查重（提交两个同名字段不被拒）；**不校验字段是否属于绑定资源/表的真实列**；文档 §5.5/§5.8（第 439、679 行）明确写了“按 `FormFieldDef.ColumnName` 投影”，但**代码里没有 `ColumnName` 这个属性** | 字段名拼错在 Mongo 无 schema 的情况下就是**静默丢数据**；文档描述了一个不存在的契约 | `ResourceSchema.cs` `FormFieldDef`（无 `ColumnName`）；`FormDefinitionAppService.PublishAsync` L129 | 发布校验字段名唯一 + 与资源列对齐；文档与代码二选一对齐 |
| **F6** | **引用完整性为零** | `DeleteAsync` 不做任何引用检查 | 被 OA `FormRef`、报表 `ActionDef.FormKey` 引用的表单可直接删除，**运行时才报 `FormNotFound`** | `FormDefinitionAppService.DeleteAsync` L121–122 | 删除前检查引用（对标逻辑编排 `FlowUsageIndexer`） |
| **F7** | **`FieldCatalog` 与 `Schema.Fields` 两套字段库** | `FormDefinition` 同时持有设计器字段库 `FieldCatalog` 与真实字段 `Schema.Fields`；`CreateAsync` 有 `SourceResourceCode` 时会把资源默认表单克隆进 Schema；`SyncCatalogFromResourceAsync` **只在创建时跑一次**，资源列后续变更无法重新同步、也无同步入口 | “新建即复制资源表单”与“空白表单 + 字段库”两种心智混在一起，字段来源不唯一 | `FormDefinition` `FieldCatalog` L30；`FormDefinitionAppService.CreateAsync` L70–101、`SyncCatalogFromResourceAsync` L194–209 | 明确单一事实源，补资源列重新同步入口 |

### 8.2 OA 流程定义缺陷（W1–W9）

| 编号 | 缺陷 | 触发条件 / 实证 | 影响 | 代码位置 | 修复方向 |
|:--:|:---|:---|:---|:---|:---|
| **W1** | **定义侧零校验，坏流程能“发布成功”** | `UpdateDraft` 对 `ProcessJson` 只判非空；`PublishAsync` 只校验 `FormRef` 存在且已发布。**不校验** start/end 是否存在、节点 id 是否唯一、是否有环、审批节点是否配了办理人、条件引用的字段是否存在 | 任何字符串都存得进去；配置错误不在发布口拦截 | [WorkflowDefinition.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/WorkflowDefinition.cs) `UpdateDraft` L56–75；[WorkflowDefinitionAppService.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/WorkflowDefinitionAppService.cs) `PublishAsync` L129–142 | 照 `FlowExecutor.ValidateDsl` 模式加结构校验 |
| **W2** | **指向不存在节点的连线 = 单据被静默通过（最严重）** | `ChooseNext` 用 `graph.Nodes.GetValueOrDefault(edge.TargetId)` 取下一节点，取不到返回 `null`；`EnterAsync` 收到 null 后 `while` 循环退出，直接执行 `instance.Complete("approved")` | **一条打错目标的连线，会让审批单在没有任何人审批的情况下变成“已通过”**——审批场景里的 fail-open 数据事故 | [WorkflowRuntimeAppService.cs](file:///d:/Project/demo-microservice/src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/WorkflowRuntimeAppService.cs) `ChooseNext` L406/416/423、`EnterAsync` L385–386 | 目标节点不存在时**显式报错**，不得回落为“通过” |
| **W3** | **未知节点类型被当作审批节点** | `EnterAsync` 只显式判断 `end`/`start`/`condition`/`cc`，其余**全部落到“创建待办”分支** | 把 `approver` 拼成 `aprpver`，流程照样跑，只是行为不是你要的，且**没有任何告警** | `WorkflowRuntimeAppService.EnterAsync` L339–387 | 未知 `type` 抛异常并记录 |
| **W4** | **双格式长期分叉，且默认格式是残废的那一个** | `ParseProcess` 同时支持画布 `nodes/edges` 与旧的 `childNode` 链；`FlattenTree` 每个节点只能 `Link` 一个子节点（**结构上无法表达多出边**），生成的边**永远不带条件**；而 `DefaultProcessJson()` 生成的正是这个树格式 | **新建流程默认落在一个既不能分支、也不能带条件的格式上**——而“同一节点多条出线按条件选择”恰是 §6 要求的能力 | `WorkflowRuntimeAppService.ParseProcess` L938–1012、`FlattenTree` L1014–1029；`WorkflowDefinition.DefaultProcessJson` L79–100 | 让 `DefaultProcessJson` 直接产出 `nodes/edges`，或给树格式补多出边支持；不要长期养两套 |
| **W5** | **解析容错过度，坏数据静默降级** | `graph.Nodes[id] = node` —— 重复节点 id **后者静默覆盖前者**；连线缺 `source`/`target` 直接 `continue`；`FlattenTree` 同样覆盖同名节点 | 一个写坏的流程“解析成功”，但**节点少了一半**，问题被推迟到运行时 | `WorkflowRuntimeAppService.ParseProcess` L974/L983、`FlattenTree` L1020 | 重复 id / 缺关键字段直接报错 |
| **W6** | **条件分支无语义，靠数组顺序决定** | 多出边时 `foreach (var edge in edges.Where(EdgeHasCondition))` 返回**第一个匹配**；无优先级字段、无互斥性校验、无告警；条件用编号 + `combine` 字符串公式（`"1 and (2 or 3)"`） | 前端设计器里**拖动连线顺序就会静默改变审批走向**；同一系统里“条件”存在三种表达（连线编号公式 / 节点 `leaveCondition` / 报表 `FilterGroup` 树） | `WorkflowRuntimeAppService.ChooseNext` L412–418 | 加优先级/互斥校验；统一迁移到 `FilterGroup` 树 |
| **W7** | **办理人相关全是魔法字符串，且默认值危险** | `assigneeType=role` 无显式 case（落 `default`）；`manager` 未接组织树，只把角色码 `manager` 丢给匹配；`EmptyFallback` **默认 `"admin"`**；`multi`/`multiRatio`/`opinion` 无枚举校验，`multi=ratio` 但 `MultiRatio` 为空的行为未定义（实现取 `?? 100`） | **一个配置失误会把审批单直接派给管理员账号**（审批场景默认值应为 `error`）；`manager` 名不副实；非法枚举静默走兜底 | `WorkflowRuntimeAppService.ResolveAssigneesAsync` L502–529、`EmptyFallback` 默认值 L1256、`CreateTasksAsync` L459–475 | `EmptyFallback` 默认改 `error`；补显式 `case "role"`；接组织树实现 `manager` |
| **W8** | **字段权限矩阵服务端只有两个有效值且 fail-open** | `FieldPermissions` 是 `Dictionary<string,string>`（键任意、不校验是否真实字段）；`FilterRecordPatch` **只对 `read`/`hide` 阻止写入，其它任何值（含拼错的 `"reade"`）一律放行** | 服务端 `write` 和 `required` **实际不起作用**；权限写错静默无效；文档 §5.8.3 设计的“模式级 baseline ∩ 节点级 ∪ 按钮白名单”三层叠加，代码里只有节点级一层 | `WorkflowRuntimeAppService.FilterRecordPatch` L846–866、`WfProcessNode.FieldPermissions` L1255 | 未知权限值改为**拒绝**；补模式级 baseline 层 |
| **W9** | **无版本、无唯一索引** | 流程无版本号/版本表（对比 `FlowDefinition` 有 `FlowVersion`）；实例上 `ProcessSnapshotJson` 只是字符串，**无法回答“这次实例对应第几版定义”**；`Code` 全系统作为引用键却**没有唯一索引**，仅靠 `AnyAsync` 预检；实例只存 `FormRef` 字符串、**不存表单 schema 快照** | 并发创建可产生重复 `Code`；流程冻结了、**表单没冻结** | `WorkflowDefinitionAppService.CreateAsync` L80；`WorkflowRuntimeAppService.WfProcessNode` L1240–1256 | 加 `Version` 字段 + 唯一索引；实例保存表单 schema 快照引用 |

### 8.3 交叉影响与修复优先级

**最坏组合**：在途审批单读取的字段结构随表单编辑漂移（F4 + W9），而流程快照让它看起来“很安全”。

**建议动手顺序**（前两步改动集中、风险可控）：

1. **止血（改动小、收益大）**：W2 的 `null` 目标改为报错；`EnterAsync` 对未知 type 抛异常；`EmptyFallback` 默认改 `error`；`FilterRecordPatch` 未知权限值改为拒绝。
2. **补发布校验**：照 `FlowExecutor.ValidateDsl` 的模式给 `WorkflowDefinition.PublishAsync` 加结构校验（start/end 唯一、id 唯一、无环、approver 必填办理人、条件字段属于绑定表单）；给 `FormDefinition.PublishAsync` 加字段名唯一 + 与资源列对齐校验。
3. **修 F1/F2**：把拍平改成“以 Layout 为准的覆盖式同步”；合并 `FormFieldDef`/`FormWidgetDef`，消除漏拷贝的根因。
4. **补快照与唯一索引**：表单发布生成不可变 Schema 快照 + `SchemaVersion`；给 `FormDefinition.Code`、`WorkflowDefinition.Code` 建唯一索引；实例保存表单快照引用。
5. **统一条件模型**：把审批连线条件从编号公式迁移到 `FilterGroup` 树，与报表对齐。
6. **明确双格式去留**：要么让 `DefaultProcessJson` 直接产出 `nodes/edges`，要么给树格式补 `Children` 支持多出边——不要长期养两套。

> **与 §0 的关系**：§0 列的 §2/§3 能力确实“已落地”，但落地质量受本节 F/W 各项约束——例如 §3.3 的 `FieldPermissions` 虽已实现，却因 W8 在服务端 fail-open；§3.1 的流程快照虽已实现，却因 F4/W9 只冻结流程不冻结表单。补本节缺陷前，§0 的“✅”应理解为“已具备该入口，尚未通过健壮性验收”。
