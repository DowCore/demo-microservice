# Meta.Dow 表单编辑与 OA 流程重构设计规格书

> **文档性质**：表单设计器与 OA 审批流的核心架构重构与能力落地规格书。  
> **关联文档**：[`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md)、[`lowcode-oa-workflow.md`](./lowcode-oa-workflow.md)、[`lowcode-form-preview.md`](./lowcode-form-preview.md)  
> **前后端边界**：前端 `D:\Project\vue-demo`（Vben Admin / Ant Design Vue）；后端 `Meta.Dow.SaaS` 编排引擎。

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
