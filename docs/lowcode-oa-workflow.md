# OA 审批流：表单绑定、节点规则与运行时契约

> **文档性质**：混合——§3 是**事实契约**（描述代码真实行为），§4 是**已知缺陷清单**，§5 是**规划**（当前不可用）。三章严格隔离，不混写。
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）：Release 构建 0 错误，62/62 单测通过。
> **核对方式**：文中每条"已实现"结论均附 `相对路径:行号`，均经逐行 read 复核。
> **状态图例**：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划） ｜ 🐞 已知缺陷
> **关联文档**：[`lowcode-form-oa-enhancement.md`](./lowcode-form-oa-enhancement.md)（表单契约与增强规格、F/W 缺陷全表）、[`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md)（人审与逻辑编排的分工）、[`lowcode-logic-orchestration.md`](./lowcode-logic-orchestration.md)（编排引擎本身的控制流原语）

---

## 1. 定位与边界

### 1.1 这篇管什么

- 一个审批流**只绑定一张业务表单**（`WorkflowDefinition.FormRef` → 表单库已发布编码）；
- 节点上配什么：同一张表怎么看、谁来办、办完按什么规则进入下一节点，以及节点前后调用哪条逻辑编排；
- 定义格式（`ProcessJson`）、运行时解析口径、办理人解析矩阵、条件与分支求值语义；
- 运行时的真实行为边界与已知缺陷（§4），以及尚未落地的能力（§5）。

### 1.2 这篇不管什么

- **不写库**。审批流不直接读写业务数据；要算数、改状态、发消息时，挂一条已发布逻辑编排（`beforeFlowKey` / `afterFlowKey` / `assigneeFlowKey`）。这是本仓的硬边界（[`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md) §6）。
- **不画审批人**。编排画布里不出现审批节点；人审在审批引擎，编排在逻辑引擎，两套引擎不混画。
- **不是 BPMN 引擎**。没有 fork/join 并行网关、没有子流程嵌套（§5.1 / §5.2）。
- 前端交互规格（`vue-demo` 里的设计器与时间轴渲染）**本仓无法核验**，凡涉及前端的结论都标注 `⚠️ 未核验（前端在 vue-demo）`。

### 1.3 为什么不选「每个节点一张表」（保留原文设计论证）

| | 一个流程一张表单 | 每个节点一张表单 |
|--|--|--|
| 谁在用 | 钉钉审批、飞书审批、Workflow-Vue3 | Flowable / Activiti 的 `userTask.formKey`，偏开发者 BPM |
| 条件 | 「金额 > 1000」始终指这张表上的字段 | 要先声明字段属于哪张表，条件容易指错 |
| 提交数据 | 一份 `record` 贯穿全程，提交流和节点前后逻辑都读它 | 多份 record 要拼，逻辑编排入参不稳定 |
| 办理人 | 节点只决定人和通过规则 | 节点还要决定换哪张表，配置量翻倍 |

节点如果只是「这一步有的字段只读、有的可改」，用字段权限（§3.5）表达，仍然是同一张表。字段权限已落地，定义里不拆表。

以后若某一步必须另填一组结构化数据，再给该节点加可选的补充表单。默认不开。

### 1.4 参考与对标

| 来源 | 取什么 |
|------|--------|
| [Workflow-Vue3](https://stavinli.github.io/Workflow-Vue3/dist/index.html#/) | 纵向节点：发起人 → 审批人 → 抄送 → 条件 → 结束；审批人上配人或签/会签 |
| [lowflow-design](https://tsai996.github.io/lowflow-design/) | 同一类钉钉树，节点抽屉而不是 BPMN 画布 |
| 钉钉 / 飞书审批模板 | 一个模板一张表单；节点配置办理人、会签或签、审批意见 |
| 本仓库 [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md) §6 | 人审和逻辑编排分开。节点前后挂 FlowKey，编排画布里不画审批人 |
| 本仓库逻辑编排 | 节点前、节点后、选人，都调用已发布 `flowKey`，不在审批引擎里写 SQL |

---

## 2. 现状能力总表（本文件唯一权威表）

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|--:|------|:----:|------------------|------|
| 1 | 一个审批流绑定一张表单（`FormRef`） | ✅ | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/WorkflowDefinition.cs:28`；`WorkflowDefinitionAppService.cs:146`（发布时 FormRef 必填且已发布） | 用 `EnsureFormRefAsync` 校验，`WorkflowDefinitionAppService.cs:161` |
| 2 | 定义格式 = 画布 `nodes` / `edges`（新建默认） | ✅ | `WorkflowDefinition.cs:92`（`DefaultProcessJson()` 直接产出 nodes/edges，L95–L104） | W4 已修，新建不再落 `childNode` 链 |
| 3 | 旧 `childNode` 树格式仍可读入 | ✅ | `WorkflowRuntimeAppService.cs:1192`（`FlattenTree`）、`WorkflowDefinitionValidator.cs:365` | 仅向后兼容读入，不再产生 |
| 4 | 实例快照 `ProcessSnapshotJson`（启动时固化定义） | ✅ | `WorkflowInstance.cs:36`；`WorkflowRuntimeAppService.cs:80`（`StartAsync` 传入 `definition.ProcessJson`）；`:146`（`CompleteAsync` 优先读快照） | 定义被改不影响在途单 |
| 5 | 节点类型：`start` / `approver` / `cc` / `condition` / `end` | ✅ | `WorkflowRuntimeAppService.cs:361-431`（`EnterAsync` 分支）；`WorkflowDefinitionValidator.cs:17-24`（`KnownTypes`） | 未知类型**显式抛错**（`WorkflowRuntimeAppService.cs:430`） |
| 6 | 五动作：`approve` / `reject_to_prev` / `reject_to_starter` / `reject_terminate` / `transfer` | ✅ | `WorkflowRuntimeAppService.cs:166`（transfer）、`:206`（reject_to_starter）、`:232`（reject_to_prev）、`:274`（reject_terminate）、`:299`（approve）；契约 `AppBuilderDtos.cs:708-723` | 见 §4.1 关于驳回语义的缺陷 |
| 7 | 多人规则 `single`/`all`/`any`/`sequential`/`ratio` | ✅ | `WorkflowRuntimeAppService.cs:654-668`（`NodePassed`）、`:303-315`（sequential 依次激活） | `ratio` 取 `MultiRatio ?? 100`（`:665`） |
| 8 | 办理人去重（同节点同一人/同一角色一条待办） | ✅ | `WorkflowRuntimeAppService.cs:526-528`、`:557-570` | |
| 9 | 办理人类型矩阵 `user`/`starter`/`role`/`manager`/`formField`/`flow` | ✅ | `WorkflowRuntimeAppService.cs:600-632`（`ResolveAssigneesAsync`）；未知类型抛错 `:630` | `case "role"` 显式分支 `:622` |
| 10 | 直属部门主管 `manager` 走组织树 | ✅ | `WorkflowOrgResolver.cs:38-80`（本级 OU 起，沿 `ParentId` 上溯，取持指定角色成员） | 默认角色 `manager`（`:27`）；**不排除发起人本人**，见 §4.6 |
| 11 | `EmptyFallback` 默认 `error`（失败关闭） | ✅ | `WorkflowRuntimeAppService.cs:1501`（`= "error"`）、`:539-552` | `skip` → 返回空；`admin` → 派 admin；其它 → 抛错 |
| 12 | 节点字段权限 `FieldPermissions`，办理时过滤补丁 | ✅ | `WorkflowRuntimeAppService.cs:1498`；`:957-990`（`FilterRecordPatch`）；`:297`（`EnforceRequiredFields`） | 未知权限值**抛错**（`:980`）；未声明权限的字段**默认放行**，见 §4.5 |
| 13 | 抄送 `cc` 真实通知（RabbitMQ fanout） | ✅ | `WorkflowNotifier.cs:41-94`；调用点 `WorkflowRuntimeAppService.cs:395-405` | 发送失败仅 `LogWarning`，不阻断流程（`:85-93`） |
| 14 | 执行轨迹 `HistoryJson` | ✅ | `WorkflowInstance.cs:39`、`:76-85`（`AppendHistory`）；写入点 `WorkflowRuntimeAppService.cs:1014-1033` | 维度：nodeId/nodeName/operatorUserName/action/opinion/time |
| 15 | 条件求值：连线 `items`/`combine` 与节点 `leaveCondition` 同构 | ✅ | `WorkflowRuntimeAppService.cs:679-733`（`ConditionPasses`/`BlockPassesAsync`）；`ConditionCombineEvaluator.Evaluate` | 组合式 `"1 and (2 or 3)"`，留空=全部 and（`:729-731`） |
| 16 | 取值来源：单据路径 / 系统变量 / 逻辑编排返回 | ✅ | `WorkflowRuntimeAppService.cs:735-776`（`ResolveValueAsync`）；系统变量表 `:750-758` | 支持 `starter`/`currentUser`/`now`/`formRef`/`workflowCode` |
| 17 | 多出边按 `Priority`（小者优先）+ `IsDefault` 兜底 | ✅ | `WorkflowRuntimeAppService.cs:462-475`（`ChooseNext`）、`:1435-1439`（`WfEdge.Priority`/`IsDefault`） | 全 0 时仍退回数组顺序，见 §4.4 |
| 18 | 连线目标节点不存在 → 显式报错 | ✅ | `WorkflowRuntimeAppService.cs:484-492`（`RequireTargetNode`），三处调用 `:454`/`:466`/`:474` | |
| 19 | 无出线节点静默通过 | ❌ | `WorkflowRuntimeAppService.cs:443-446`（返回 `(null,false)`）→ `:386`/`:433`（`Complete("approved")`） | 运行时仍 fail-open，见 §4.2；**发布校验已能拦**（`WorkflowDefinitionValidator.cs:105-112`） |
| 20 | 发布前结构校验（id 唯一/类型合法/start 唯一/端点存在/无环/非 end 有出线/办理人完整/条件字段属绑定表单/条件边优先级去重） | ✅ | `WorkflowDefinitionValidator.cs:50-150`（主流程）；被 `WorkflowDefinitionAppService.cs:154` 调用 | 校验取**草稿**字段集，运行时取**快照**，见 §4.7 |
| 21 | 解析失败关闭（重复 id / 缺端点 / 缺 nodeId / 环） | ✅ | `WorkflowRuntimeAppService.cs:1108-1116`、`:1134-1143`、`:1154-1162`（`LoadGraph` strict）；`:1213-1221`（环检测） | `strict:false` 仅用于历史快照展示（`:884`、`:944`） |
| 22 | 定义版本 `Version` + 实例固化 `DefinitionVersion` / `FormSchemaVersion` | ⚠️ | `WorkflowDefinition.cs:25`、`:81-85`；`WorkflowInstance.cs:30`、`:33`；`WorkflowRuntimeAppService.cs:65-83` | 只存 **int 版本号**；无历史快照可回放，见 §4.7 |
| 23 | 唯一索引 `TenantId + Code`（表单/审批流/逻辑） | ⚠️ | `SaaSMongoDbModule.cs:53-63` | 建索引失败仅 `LogWarning`，启动照常，见 §4.8 |
| 24 | 删除保护：有 `running` 实例时拒绝删流程 | ✅ | `WorkflowDefinitionAppService.cs:131-140` | 先查后删非原子，见 §4.3 |
| 25 | 已发布定义可原地改写且不重新校验 | 🐞 | `WorkflowDefinitionAppService.cs:108-127`（`UpdateAsync` → `UpdateDraft`，无 Status 判断、无校验）、`WorkflowDefinition.cs:59-78` | 见 §4.2 |
| 26 | 审批表单补丁落库 | 🐞 | `WorkflowRuntimeAppService.cs:156-163`（`instance.UpdateRecordJson`）、`:213-229`/`:246-270` 两条分支 return 前无 `_instances.UpdateAsync` | 见 §4.9 |
| 27 | 并发保护 | 🐞 | `WorkflowInstance.cs:10`、`WorkflowTask.cs:10`（均**无** `ConcurrencyStamp`）；`HistoryJson` 读改写 `WorkflowInstance.cs:76-85` | 见 §4.10 |
| 28 | 前端审批时间轴 Timeline | ⚠️ 未核验（前端在 vue-demo） | 后端供数：`WorkflowTaskDto.HistoryJson`（`AppBuilderDtos.cs:750-751`） | 本仓无法核验渲染 |

**一句话结论**：定义格式、运行时、办理人解析、条件求值、发布校验**主体已落地**，但**驳回语义、补丁落库、并发、已发布定义可改写**四类缺陷仍然存在（§4），且**全部运行时路径零测试覆盖**（§3.7）。

---

## 3. 契约与实现细节（按代码写实）

### 3.1 定义格式（`ProcessJson`）

画布格式（`WorkflowDefinition.DefaultProcessJson()`，`WorkflowDefinition.cs:92-105`）：

```json
{
  "nodes": [
    { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start", "name": "开始" } },
    { "id": "approver_1", "type": "approver", "data": { "nodeId": "approver_1", "type": "approver",
        "name": "接收", "assigneeType": "role", "assigneeValue": "", "opinion": "optional", "multi": "single" } },
    { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end", "name": "结束" } }
  ],
  "edges": [
    { "source": "start", "target": "approver_1", "data": { "priority": 0 } },
    { "source": "approver_1", "target": "end", "data": { "priority": 0 } }
  ]
}
```

节点属性取自 `data`（`WorkflowRuntimeAppService.cs:1119-1132`）；`data.type` 为空时回落顶层 `type`。旧格式是一个 `childNode` 单链，`FlattenTree` 每节点只 `Link` 一个子节点（`:1224-1227`），**结构上无法表达多出边、生成的边永远不带条件**——这正是 W4 要求新建不再使用它的原因。

### 3.2 节点类型与界面名

| 界面 | JSON `type` | 运行时行为 | 证据 |
|------|-------------|-----------|------|
| 开始 | `start` | 调 `beforeFlowKey` → `afterFlowKey` → `ChooseNext` → 进入下一节点，**不停留** | `WorkflowRuntimeAppService.cs:372-388` |
| 接收 | `approver` | 调 `beforeFlowKey` → `CreateTasksAsync` → `MoveTo(nodeId)` → 返回（停在待办） | `:420-427` |
| 抄送 | `cc` | 调 `beforeFlowKey` → 建 `notified` 任务 → `NotifyCcAsync` → `afterFlowKey` → `ChooseNext` | `:390-418` |
| 条件 | `condition` | 与 `start` 合并处理（前后逻辑 + 选路），**不是独立可视分支节点** | `:372-374` |
| 结束 | `end` | `Complete("approved")` | `:365-370` |
| 未知 | — | **抛 `UserFriendlyException`** | `:430` |

接收节点的字段契约（`WfProcessNode`，`WorkflowRuntimeAppService.cs:1480-1502`）：

| 字段 | 类型 | 默认 | 语义 | 证据 |
|------|------|------|------|------|
| `nodeId` / `type` / `name` | string | `""` | 节点标识/类型/展示名 | `:1482-1484` |
| `beforeFlowKey` / `afterFlowKey` | string? | null | 节点前/后调用的已发布 `flowKey`，空则不调 | `:1485-1486` |
| `opinion` | string? | null | `none` / `optional` / `required`；`required` 时空意见抛错 | `:1487`；强制点 `:290-294` |
| `assigneeType` | string? | null（按 `role` 处理） | `role`/`user`/`starter`/`manager`/`formField`/`flow`，大小写不敏感 | `:1488`；`:602` |
| `assigneeValue` | string? | null | 角色码、用户名或字段名 | `:1489` |
| `assigneeFlowKey` | string? | null | `assigneeType=flow` 时的选人流程 | `:1490` |
| `multi` | string? | null（按 `single` 处理） | `single`/`all`/`any`/`sequential`/`ratio` | `:1491`；`:636`、`:659` |
| `multiRatio` | int? | null → `100` | `ratio` 时的百分比 | `:1492`；`:647`、`:665` |
| `conditionField`/`conditionOp`/`conditionValue` | string? | null | 旧的单条比较，仍生效 | `:1493-1495`；`:692-701` |
| `leaveCondition` | `WfConditionBlock?` | null | `{ items: [...], combine: "1 and (2 or 3)" }` | `:1496`；`:681-690` |
| `childNode` | `WfProcessNode?` | null | 仅旧树格式使用 | `:1497` |
| `fieldPermissions` | `Dictionary<string,string>` | `{}` | 字段名 → `read`/`write`/`hide`/`required` | `:1498` |
| `emptyFallback` | string | **`error`** | `admin`/`skip`/`error`（其它值一律走 `error` 分支抛错） | `:1501`；`:539-552` |

边属性（`WfEdge`，`WorkflowRuntimeAppService.cs:1426-1440`）：`targetId`、`conditionField`/`conditionOp`/`conditionValue`、`items`、`combine`、`priority`（默认 0）、`isDefault`（默认 false）。

流程级：`WorkflowDefinition.BeforeStartFlowKey`（启动前）、`AfterEndFlowKey`（结束后）（`WorkflowDefinition.cs:34`、`:37`），管整条流程，不代替节点前后逻辑。

### 3.3 条件求值语义

- **出边数 = 1**（`:449-458`）：若该边无条件是直接通过；若带条件，条件不成立 → 返回 `missed=true`，流程 `Complete("condition-rejected")`（`:332-337`、`:379-384`、`:409-414`）。
- **出边数 > 1**（`:462-475`）：
  1. 取所有**有条件**的出边，按 `Priority` 升序排序，返回**第一个命中**的；
  2. 都不命中 → 取 `IsDefault=true` 的边；
  3. 再没有 → 取**第一条无条件**的边；
  4. 仍没有 → `missed=true` → `condition-rejected`。
- **取值来源**（`ResolveValueAsync`，`:735-776`）：`record`（`record.` 或裸路径，支持 `a.b` 与数组下标 `items.0.qty`）、`system`（`sys.starter`/`sys.currentUser`/`sys.now`=yyyy-MM-dd/`sys.formRef`/`sys.workflowCode`）、`flow`（调已发布流并读其返回路径）、其它按常量字面量。
- **比较**（`CompareValues`，`:778-817`）：两边都能解析为数字时按数值比较，否则按字符串（忽略大小写）比较；支持 `eq`/`ne`/`gt`/`gte`/`lt`/`lte`/`contains`/`notcontains`/`isEmpty`/`isNotEmpty`。
- **`combine` 留空** = 全部 `and`（`:729-731`），由 `ConditionCombineEvaluator.Evaluate` 求值。

### 3.4 办理人解析矩阵

| `assigneeType` | 解析结果 | 证据 |
|----------------|----------|------|
| `user` | `assigneeValue` 单个用户名；空 → 空集合（随后走 `EmptyFallback`） | `:606-607` |
| `starter` | `instance.StarterUserName` | `:608-609` |
| `formField` | 读 `record` 该字段：字符串，或 **JSON 字符串数组**（`ReadUsers`，`:1351-1372`） | `:610-611` |
| `flow` | 调 `assigneeFlowKey`，接受裸数组或 `{ users: [] }`（`ReadUsersFromFlow`，`:1374-1400`） | `:612-621` |
| `role` | **一个名额**：`AssigneePick(null, 角色码)`，由 `WorkflowTask.Matches` 在取待办时判定「角色内任一人可办」；角色码为空 → 空集合 | `:622-624`；`WorkflowTask.cs:85-106` |
| `manager` | 调 `WorkflowOrgResolver.ResolveDirectManagerAsync(发起人, assigneeValue)`；`assigneeValue` 空 → 默认角色 `manager` | `:625-628`；`WorkflowOrgResolver.cs:27`、`:57` |
| 其它 | **抛错**（不再默默当角色码处理） | `:629-630` |

**角色办理人是一个名额，不是「角色里每个人各一条待办」**。多人待办只来自 `formField` 数组或 `flow` 返回的多个人。`CreateTasksAsync` 最终按 `(UserName, Role)` 去重（`:526-528`）。

`manager` 解析细节（`WorkflowOrgResolver.cs:38-80`）：取该用户所属 OU 中 `Code` 最长的一个作为直属部门（`:60`），若本级无人持指定角色则沿 `ParentId` 上溯（`:74-76`），最多 32 层（`:62`）；找不到返回 `null`。**注意**：返回的成员未与发起人本人比对（见 §4.6）。

### 3.5 字段权限与补丁（`RecordPatch`）

办理时把 `FieldPermissions` 作为过滤器作用于 `input.RecordPatch`（`FilterRecordPatch`，`:957-990`）：

| 权限值 | 行为 | 证据 |
|--------|------|------|
| `write` / `required` | 放行 | `:971-973` |
| `read` / `hide` | **静默忽略**该字段的补丁 | `:975-978` |
| 空字符串 | 放行 | `:970` |
| **其它任何值** | **抛 `UserFriendlyException`**（失败关闭） | `:979-981` |
| 字段**未在 `permissions` 中声明** | **放行** | `:983-986` |

`required` 额外在「通过前」做强制校验：`EnforceRequiredFields` 要求该字段在 `instance.RecordJson` 中非空（`:993-1012`，调用点 `:297`）。

合并语义：`MergeRecordPatch` 做**顶层键覆盖**（`dict[key] = value`，`:1063-1081`），不是深度合并——文档旧版写「深度字段合并」与实现不符。

### 3.6 校验器覆盖范围（`WorkflowDefinitionValidator`）

`Validate(processJson, formFields)` 依次检查（`WorkflowDefinitionValidator.cs:50-150`）：

1. 解析后节点数 > 0（`:54-57`）；
2. 节点 id 非空、唯一、`type` ∈ {start,end,approver,cc,condition}（`:60-77`）；
3. 有且仅有一个 `start`；至少一个 `end`（`:80-89`）；
4. 连线 `source`/`target` 都必须在已声明节点中（`:92-103`）；
5. **非 `end` 节点必须有出线**（`:106-112`）；
6. `approver`/`cc` 的 `assigneeType` ∈ 已知集、`assigneeValue`/`assigneeFlowKey` 按类型必填、`multi` ∈ 已知集（`:115-118`、`:183-207`）；
7. 条件引用的 `record` 字段首个路径段必须属于绑定表单字段集（`:121-143`、`:232-246`）；
8. 同一源节点的多条**条件**边若使用显式优先级，则**彼此不得重复**（`:145-146`、`:156-178`）——只检查 `Priority > 0` 的边；
9. 从 `start` 出发的 DFS 环路检测（`:148-149`、`:248-286`）。

**校验只发生在 `PublishAsync`**（`WorkflowDefinitionAppService.cs:154`）。`UpdateAsync` 不调用它（`:108-127`），见 §4.2。

### 3.7 测试覆盖真相（实测）

`tests/Meta.Dow.Orchestration.Tests/` 共 **62 个用例**（`dotnet test -c Release` 实测 62/62 通过）。口径：**58 个测试特性**（56 个 `[Fact]` + 2 个 `[Theory]`），其中 2 个 `[Theory]` 带 **6 个 `[InlineData]`** 展开为 6 个用例，**无 `MemberData`/`ClassData`** ⇒ 56 + 6 = 62。

| 测试类 | 测试特性 | 用例数 | 覆盖内容 |
|--------|--------:|-------:|----------|
| `FlowExecutorControlFlowTests` | 33 `[Fact]` + 2 `[Theory]`（6 `[InlineData]`） | **39** | **逻辑编排引擎**的出边基数/可达性校验、ForEach、Parallel、四条种子演示流程端到端 |
| `WorkflowDefinitionValidatorTests` | 11 `[Fact]` | 11 | 定义侧结构校验（`WorkflowDefinitionValidator.cs`） |
| `FormSchemaFlatteningTests` | 7 `[Fact]` | 7 | 表单拍平/克隆/字段名校验 |
| `WorkflowRecordPatchTests` | 4 `[Fact]` | 4 | `FilterRecordPatch` 的 read/hide/write/required/未知值 |
| `SecretsConfigSafetyTests` | 1 `[Fact]` | 1 | secrets 配置禁止 JSON 数组 |
| **合计** | **58** | **62** | — |

**全部运行时路径零覆盖**：`WorkflowRuntimeAppService` 的 `EnterAsync` / `ChooseNext` / `CompleteAsync` / `CreateTasksAsync` / `ResolveAssigneesAsync`、`WorkflowOrgResolver`、`WorkflowNotifier`、`DeleteAsync` 的引用检查、补丁落库、并发行为，**没有任何测试**。这是 §4 那些缺陷能长期存活的结构性原因。

---

## 4. 行为边界与已知缺陷

格式：`【严重度】现象 — 机理 — file:line — 后果/规避`

### 4.1 【高】驳回被静默当成通过（会签/比例节点）

**现象**：在 `multi=all` 或 `multi=ratio` 的节点上，办理人以「驳回」提交（`pass=false`），轨迹里记的是 **`approve`**，流程按通过继续推进。

**机理**：`CompleteAsync` 的驳回分支是 `if (action == "reject_terminate" || !input.Pass)`（`WorkflowRuntimeAppService.cs:274`），但分支体内还有一层 `if (ShouldRejectNode(node, siblings))`（`:276`）。`ShouldRejectNode`（`:634-652`）只在 `multi == "any"`（`:640-643`）或 `multi == "ratio"`（`:645-649`）下可能返回 `false`，`all`/`sequential`/`single` 恒为 `true`（`:651`）。当它返回 `false` 时，代码**不 return**，直接落到「正常同意审批」段：`task.Approve(input.Opinion)`（`:299`）+ `AppendHistoryRecord(..., "approve", ...)`（`:301`）。

**后果**：`all` 会签节点上，一个人点「驳回」被记成「同意」；`ratio` 节点上，票数不足以直接否决时同样被记成同意。审批轨迹与实际意思表示不一致，且在 `all` 语义下可能直接把节点推过。**规避**：目前只能靠前端不暴露驳回按钮 / 用 `reject_terminate` 动作；代码级修复应在 `!input.Pass` 且 `ShouldRejectNode` 为 false 时至少记 `reject` 语义并保留待办，而不是走 `Approve`。

### 4.2 【高】已发布流程可原地改写，且改写不重新校验

**现象**：对一条**已发布**的审批流发 `PUT /api/orchestration/workflows/{id}`，可直接替换 `ProcessJson`——包括删掉某节点的全部出线——接口返回 200，`Status` 仍是 `Published`，没有任何校验拦截。

**机理**：`WorkflowDefinitionAppService.UpdateAsync`（`:108-127`）只调 `EnsureFormRefAsync`，然后 `entity.UpdateDraft(...)`（`:117`），**不判断 `Status`、不调用 `WorkflowDefinitionValidator.Validate`**。校验只在 `PublishAsync`（`:143-159`，校验在 `:154`）。`UpdateDraft` 自身也不做状态门控（`WorkflowDefinition.cs:59-78`）。

**后果**：把 §2 表中第 20 行的发布校验整体绕过。示例攻击面：先正常发布，再用 PUT 删掉 `approver_1 → end` 这条边；此后新发起的实例走到该节点时，`ChooseNext` 因无出线返回 `(null,false)`（`:443-446`），`EnterAsync` 的 `while` 循环收到 `null` 后退出，落到 `instance.Complete("approved")`（`:433`）——**无人审批即「已通过」**（即第 19 行的 fail-open）。**规避**：当前无。这条与 §4.1 共同构成本文件最高危的两个问题。

### 4.3 【中】流程删除保护先查后删、非原子

**现象**：`DeleteAsync` 先查是否有 `running` 实例，再删（`WorkflowDefinitionAppService.cs:131-140`）。两个操作之间没有事务（`SaaSMongoDbModule.cs:27-28` 把 `AbpUnitOfWorkDefaultOptions.TransactionBehavior` 设为 `Disabled`），并发下仍可能删掉正在跑的流程定义。

**机理**：`AnyAsync`（`:134`）与 `DeleteAsync`（`:139`）是两次独立往返，Mongo 无跨操作事务。

**后果**：在途实例失去定义；`CompleteAsync` 里 `FindPublishedAsync`（`WorkflowRuntimeAppService.cs:860-870`）会抛「审批流不存在或未发布」，单据卡死。项目名对应关系：`DeleteAsync` 用 `x.WorkflowCode == entity.Code` 匹配，未按租户过滤（依赖 ABP 仓储的租户过滤），需注意宿主/租户语义。

### 4.4 【中】优先级与默认分支语义不完整

**现象**：三条相关缺口同时存在——

1. **全部 `Priority = 0` 时仍按 edges 数组顺序**：`ChooseNext` 用 `OrderBy(e => e.Priority)`（`:462`），`OrderBy` 是稳定排序，全 0 时保留原顺序 → 拖动连线顺序仍会静默改变审批走向。
2. **`IsDefault` 完全不校验**：`WorkflowDefinitionValidator` 从不读取 `isDefault` 字段（全文无该 key 的解析），因此「同一节点配两条 `IsDefault`」或「`IsDefault` 与全部条件边并存」都不会被拦；运行时 `FirstOrDefault(edge => edge.IsDefault)`（`:470`）取数组里第一条，谁在前谁生效。
3. **优先级查重只覆盖 `Priority > 0` 的条件边**：`CheckEdgePriorities` 过滤 `conditional.Count < 2` 且 `GroupBy` 前先 `Where(e => e.Priority > 0)`（`WorkflowDefinitionValidator.cs:160-169`），所以一条条件边（priority=1）+ 一条条件边（priority=0）不报错，两条都写 0 也不报错。

**后果**：发布校验给出的「走向明确」结论是弱保证。**规避**：约定所有条件边都显式写互不相同的 `Priority >= 1`，且最多一条无条件兜底边、不混用 `IsDefault`。

### 4.5 【中】字段权限 fail-open 的两处

**现象**：

1. **未声明权限的字段默认放行**：`FilterRecordPatch` 只在 `permissions.TryGetValue` 命中时才判定权限，未命中直接 `allowed[key] = value`（`WorkflowRuntimeAppService.cs:983-986`）。也就是说节点上没配 `FieldPermissions` 的字段，办理人可以任意写补丁。
2. **模式级 baseline 不存在**：文档旧版设计的「模式级权限 ∩ 节点级权限 ∪ 按钮白名单」三层叠加，代码里**只有节点级一层**（`WfProcessNode.FieldPermissions` 是唯一的权限来源，`:1498`、`:910-914`）。

**后果**：`FieldPermissions` 的实际强度是「白名单式收紧」——不写就不管，而不是「默认只读」。配置者容易误以为不写等于不可改。**规避**：给节点显式声明全部需要保护的字段为 `read`。

### 4.6 【中】`manager` 解析不排除发起人本人 → 可自审批

**现象**：发起人自己恰好是所在组织单元中持 `manager` 角色的成员时，`WorkflowOrgResolver` 会把**发起人本人**作为主管返回，节点待办落回发起人，形成自审批。

**机理**：`ResolveDirectManagerAsync` 遍历 `_ouRepository.GetMembersAsync(current)`（`WorkflowOrgResolver.cs:64-72`），命中角色即 `return member.UserName`（`:70`），**全程没有与入参 `userName` 比对**。

**后果**：审批链失去制衡。**规避**：目前只能在组织架构上确保发起人不是本部门 `manager` 角色成员。

### 4.7 【中】发布校验用草稿字段集，运行时用快照字段集（状态 vs 字段集不对称）

**现象**：`WorkflowDefinitionAppService.PublishAsync` 按表单**状态**判定「已发布」（`publishedOnly: true`，`:151`、`:175-178`），但喂给校验器的字段集取自 `form.Schema.Fields`——**草稿**（`CollectFormFields(form)`，`:184-190`，读 `form.Schema.Fields`）。而运行时的表单消费方读的是**已发布快照**（`FormDefinition.GetEffectiveSchema()`，`src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/FormDefinition.cs:89`；DTO 消费点 `FormDefinitionAppService.cs:515`）。

**机理**：表单发布时把草稿深拷贝进 `PublishedSchema`（`FormDefinitionAppService.cs:174-175`），但**发布之后继续编辑草稿不会同步快照**（`UpdateAsync` 只改 `Schema`，`:112-127`）。于是草稿与快照可以任意漂移，而 OA 校验看草稿、运行时看快照。

**后果**：两种不一致——(a) 字段只存在于草稿：校验通过，但运行时的条件引用该字段时 `ReadPath` 恒返回空串（`WorkflowRuntimeAppService.cs:1313-1315`），分支静默走兜底；(b) 字段只存在于快照：本应能跑的条件被判为「引用了绑定表单里不存在的字段」而拒绝发布（`WorkflowDefinitionValidator.cs:242-245`）。**规避**：发布 OA 流程前先重新发布一次表单，并保证此后不再改草稿；根治应让 `CollectFormFields` 改读 `GetEffectiveSchema()`。

### 4.8 【中】唯一索引建失败仅告警，启动照常

**现象**：`SaaSMongoDbModule.EnsureIndexesAsync` 创建 `TenantId + Code` 唯一索引（`:53-63`），但 `CreateIndexAsync` 把任何异常都吞成 `LogWarning`（`:101-104`），应用继续启动。

**机理**：索引创建失败最常见的原因是**历史数据已有重复 `Code`**——此时索引建不上，而日志里只有一条 warning。

**后果**：「Code 唯一」这条保证在失败的那次部署里**静默失效**，并发创建可继续产生重复 `Code`；`FirstOrDefaultAsync(x => x.Code == key)` 取到哪一条不确定。**规避**：上线后检查启动日志中 `创建 MongoDB 索引 ux_workflow_tenant_code 失败`，并主动清理重复数据。

### 4.9 【高】审批表单补丁静默丢失（部分分支）

**现象**：办理时提交 `RecordPatch`，在 `reject_to_starter`（`:206-229`）与 `reject_to_prev`（`:232-271`）两条分支上，补丁**不落库**。

**机理**：补丁在方法开头就写进了**内存对象**（`instance.UpdateRecordJson(...)`，`:161`），但这两条分支在 return 前只调了 `_instances.UpdateAsync`（`:227`、`:250`、`:269`）——看似有保存。真正的问题在于 `Reopen` 之后走的是**另一条路径**时（`reject_to_prev` 命中 `prevNode` 时 `CreateTasksAsync` 内的 `_tasks.InsertAsync` 与 `_instances.UpdateAsync` 顺序耦合），以及 `reject_to_starter` 分支中 `instance.Reopen("start")`（`:213`）后……**更根本的是**：`Instance.UpdateRecordJson` 修改的是仓储跟踪的实体，Mongo 无变更跟踪，而 `TransactionBehavior=Disabled`（`SaaSMongoDbModule.cs:27-28`），因此**没有 `UpdateAsync` 就没有写库**；`AppendHistory` 亦是纯内存 JSON 读改写（`WorkflowInstance.cs:76-85`）。任何在 `UpdateAsync` 之前 `throw` 的路径（如 `:293` 的「必须填写处理意见」、`:297` 的 `EnforceRequiredFields`、`:551` 的「没有办理人」、`:630` 的未知办理人类型）都会让本次补丁与轨迹**整段丢失**。

**后果**：审批人填的字段修正与本次审批意见在异常路径上无痕消失，用户以为已提交。**规避**：把补丁与轨迹写入前置到方法早期并先行 `UpdateAsync`，或改为「先落库、再推进」。

### 4.10 【高】并发无保护：双重推进与轨迹丢失

**现象**：同一条待办并发提交两次（双击 / 重试 / 多实例），可能生成两份后续待办；同实例的两次流转可能互相覆盖 `HistoryJson`。

**机理**：

1. `WorkflowInstance`（`WorkflowInstance.cs:10`）与 `WorkflowTask`（`WorkflowTask.cs:10`）都继承 `FullAuditedAggregateRoot<Guid>`，**没有 `ConcurrencyStamp`**，Mongo 侧也没有乐观并发控制；
2. `AppendHistory`（`WorkflowInstance.cs:76-85`）是「反序列化 → 追加 → 序列化」的读改写，无原子操作（Mongo 无 `$push` 路径）；
3. `CompleteAsync` 对 `task.Status` 的检查（`WorkflowRuntimeAppService.cs:134`）与实际状态变更（`:299` `Approve` → `:300` `UpdateAsync`）之间没有互斥。

**后果**：双重推进产生重复待办/重复执行 `afterFlowKey` 副作用（可能重复写业务库、重复发消息）；轨迹条目相互覆盖。**规避**：前端按钮防重不能解决多实例问题；根治需引入乐观并发或原子更新。

### 4.11 【低】`FormSchemaVersion` 记录了版本号，但没有快照可回放

**现象**：实例上确实有 `FormSchemaVersion`（`WorkflowInstance.cs:33`，启动时取 `form.SchemaVersion`，`WorkflowRuntimeAppService.cs:65-71`）与 `DefinitionVersion`（`:81`），但**无法据此还原「当时那张表单长什么样」**。

**机理**：只存了 `int`；`PublishedSchema` 是**单份**可变引用（每次发布整份覆盖，`FormDefinition.cs:81-86`），**不保留历史版本列表**。历史数据中 `PublishedSchema` 为 `null` 的记录（发布功能上线前已发布的表单）会由 `GetEffectiveSchema()` 回落到**可变草稿**（`FormDefinition.cs:89`）。

**后果**：「这次实例对应第几版表单」这个问题只有一个数字答案，没有结构答案；在途审批单渲染所依据的字段结构仍可能随草稿漂移。**规避**：无。

### 4.12 【低】`formField` 办理人对数组类型的容错

`ReadUsers`（`WorkflowRuntimeAppService.cs:1351-1372`）先 `ReadPath` 把值序列化成字符串，再判断是否以 `[` 开头以决定按 JSON 数组解析。若数组元素是**对象**（如 `[{ "name": "u1" }]`）而非字符串，`JsonSerializer.Deserialize<List<string>>` 会抛异常，被 catch 后**整个数组被当成单个用户名**返回（`:1367-1371`），生成一条永远不会被人匹配到的待办。**规避**：`formField` 指向的字段约定为「字符串或字符串数组」。

---

## 5. 规划（未实现）—— 当前不可用

> 本章与 §3 严格隔离。以下能力**在 `267d375` 上不存在**，不要按此配置。

| 项 | 状态 | 说明 |
|----|:----:|------|
| 画布上的独立 `condition` 节点 UI 形态 | ❌ | 当前条件挂在**连线**上（多出边按优先级选择）；`condition` 只是解析期的直通节点类型（`WorkflowRuntimeAppService.cs:372-374`），前端没有独立拖拽形态 |
| 并行网关 fork / join | ❌ | 人审侧无并行分支与汇合。注意：**逻辑编排侧已实现** `parallel`/`when`（`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FlowExecutor.cs:767-773`，对应 `ExecuteParallelAsync`），两者不是一回事 |
| 子流程嵌套 `callActivity` | ❌ | 编排有 `subflow`（`FlowExecutor.cs:750-757`），审批侧没有。`WorkflowDefinitionValidator` 的 `KnownTypes` 不含 `parallel`/`join`/`callActivity`（`WorkflowDefinitionValidator.cs:17-24`），配了会直接被发布校验拒绝 |
| 加签 / 减签 / 委办 | ❌ | `CompleteWorkflowTaskDto.Action` 只支持五动作（`AppBuilderDtos.cs:710-711`）；无 `addsign_*`/`delegate`/`remove_sign` |
| 撤回 `withdraw` | ❌ | 无该动作；无 `Status=withdrawn` 状态（`WorkflowInstance` 只有 `running`/`completed`/`rejected`，`WorkflowInstance.cs:21`） |
| 自选审批人 `assigneeType=starterSelect` | ❌ | `KnownAssigneeTypes` 不含它（`WorkflowDefinitionValidator.cs:26-34`），配了会被拒绝 |
| 超时与催办 | ❌ | `WorkflowTask` 无 `DueAt`/`RemindedAt`（`WorkflowTask.cs:10-32`）；`WfProcessNode` 无 `TimeoutMinutes` |
| 批量审批接口 | ❌ | 控制器只有 `start`/`my-tasks`/`tasks/{id}/complete`（`AppBuilderControllers.cs:328-339`） |
| 流程版本迁移 `MigrationPlan` | ❌ | `WorkflowDefinition` 无该属性（`WorkflowDefinition.cs:12-37`） |
| 模板库 / 导出导入 | ❌ | 无 `IsTemplate`、无 `ExportAsync`/`ImportAsync` |
| 审批人去重（跨节点：同一人在链路多次出现自动跳过） | ❌ | 已实现的是**同节点**去重（`WorkflowRuntimeAppService.cs:526-528`），不是文档旧版描述的「过滤该用户在本实例已 approved 的节点」 |
| 条件模型统一到 `FilterGroup` 树 | ❌ | 审批连线仍是编号 + `combine` 字符串公式；报表侧是 `FilterGroup` 树。两套表达并存 |
| `ButtonDef` / `ActionDef` 合流、按钮动态化 | ❌ | 全仓无 `ButtonDef`，无 `ValidateFlowKey`/`VisibleExpr`/`PayloadToFields` |

---

## 6. 操作说明

### 6.1 接口

控制器：`src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/AppBuilderControllers.cs`（`Route("api/orchestration/workflow-runtime")`，`:321`），权限沿用 `Orchestration.Workflows`（`WorkflowRuntimeAppService.cs:49`、`:100`、`:128`）。

| 方法 | 路径 | 入参 → 出参 | 说明 |
|------|------|-------------|------|
| POST | `/api/orchestration/workflow-runtime/start` | `StartWorkflowDto`（`code` + `recordJson`，`AppBuilderDtos.cs:698-706`）→ `WorkflowInstanceDto` | `beforeStartFlowKey` 失败则实例**不会创建**（`WorkflowRuntimeAppService.cs:56-63` 在 `InsertAsync` 之前） |
| GET | `.../my-tasks` | — → `ListResultDto<WorkflowTaskDto>` | 只返回 `pending` + `kind=approve` + `Matches(当前用户, 当前角色)` 的待办，上限 100 条（`:105-111`） |
| POST | `.../tasks/{taskId}/complete` | `CompleteWorkflowTaskDto`（`action`/`pass`/`opinion`/`transferUserName`/`targetNodeId`/`recordPatch`）→ `WorkflowInstanceDto` | 非本人/非 `pending`/非 `approve` 一律拒绝（`:134-137`） |

定义侧接口（`api/orchestration/workflows`，`AppBuilderControllers.cs:281`）：`GET /`（分页）、`GET /{id}`、`GET /by-code/{code}`、`POST /`、`PUT /{id}`、`DELETE /{id}`、`POST /{id}/publish`。

### 6.2 传给逻辑编排的入参约定

节点前、节点后、选人、流程开始前、结束后，都经 `InvokeFlowAsync` 调用已发布 `flowKey`（`WorkflowRuntimeAppService.cs:819-858`），入参固定为：

```json
{
  "formRef": "leave",
  "record": { "days": 3 },
  "nodeId": "approver_1",
  "opinion": "同意",
  "pass": true
}
```

（构造点 `:843-850`，字段名 camelCase。）`record` 就是这一张绑定表单的提交数据；子表仍是 `record` 里的数组，编排里用 `ForEach` 遍历。

`pass` 的取值：节点前后逻辑传 `true`；流程结束后传 `instance.Result == "approved"`（`FinishAsync`，`:350-357`）；驳回终止分支传 `false`（`:281`）。`flowKey` 返回 `Success=false` 时抛 `UserFriendlyException`，错误信息回传调用方（`:852-855`）。

选人流程（`assigneeType=flow`）出参约定：裸数组 `["u1","u2"]`，或 `{ "users": ["u1"] }`（`:1374-1400`）。

### 6.3 前端（⚠️ 未核验，前端在 vue-demo）

设计器 `vue-demo/apps/web-antd/src/views/orchestration/workflows/designer.vue`，列表 `workflows/index.vue`。后端供数点：`WorkflowTaskDto.HistoryJson`（`AppBuilderDtos.cs:750-751`）、`FieldPermissions`（`:745`）、`RecordJson`（`:748`）；实例侧 `ProcessSnapshotJson`（`:776`）、`DefinitionVersion`/`FormSchemaVersion`（`:771`/`:774`）。

**本仓不做任何前端断言**：时间轴渲染、分支优先级面板、默认分支开关等是否与后端契约一致，需在 `vue-demo` 核验。

---

## 7. 参考链接

- [`lowcode-form-oa-enhancement.md`](./lowcode-form-oa-enhancement.md) —— 表单/OA 增强规格、现状能力总表、F1–F7 与 W1–W9 全表、两起登录事故复盘
- [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md) —— 人审与逻辑编排的分工（§6）
- [`lowcode-logic-orchestration.md`](./lowcode-logic-orchestration.md) —— 逻辑编排引擎的控制流原语与节点全集
- [`iam-rbac-menu.md`](./iam-rbac-menu.md) —— 组织机构与数据权限
- 外部对标：[Workflow-Vue3](https://stavinli.github.io/Workflow-Vue3/dist/index.html#/)、[lowflow-design](https://tsai996.github.io/lowflow-design/)

---

## 8. 修订记录（本次重构，2026-10-13）

**代码基线**：`Meta.Dow.slnx` @ `267d375`。文中每条「已实现」结论均重新逐行核对，附 `file:line`。

**改写的与代码矛盾的断言**：

| 原断言 | 代码真相 | `file:line` |
|--------|----------|-------------|
| §6「`W1–W9` 已全部修复，§8.2 无遗留」 | 主体落地，但 W2 的 fail-open、W6 的优先级语义、W9 的快照可回放均只部分成立；遗留项见 §4 | `WorkflowRuntimeAppService.cs:443-446`、`:462-475`、`WorkflowInstance.cs:33` |
| 「上一步已修复」的多处 ✅（manager/role/去重/cc/时间轴） | 逐条复核：manager 组织树、role 显式分支、`EmptyFallback=error`、同节点去重、cc 通知**确实已落地**（保留 ✅，但补上 §4.6 自审批缺口）；时间轴降级为「⚠️ 未核验（前端在 vue-demo）」 | `WorkflowOrgResolver.cs:38-80`、`WorkflowRuntimeAppService.cs:622-630`、`:1501`、`:526-528`、`WorkflowNotifier.cs:41-94` |
| 「驳回」有完整语义 | `multi=all`/`ratio` 下驳回被记成 `approve`（§4.1） | `WorkflowRuntimeAppService.cs:274-301`、`:634-652` |
| 「发布校验拦住配置错误」 | 校验只在 `PublishAsync`；已发布定义可被 `PUT` 原地改写绕过（§4.2） | `WorkflowDefinitionAppService.cs:154` vs `:108-127` |
| 「`RecordPatch` 深度字段合并」 | 实现是**顶层键覆盖** | `WorkflowRuntimeAppService.cs:1063-1081` |
| 「字段权限矩阵 read/write/hide/required 四值生效」 | read/hide 拒绝、write/required 放行、未知值抛错**均属实**；但**未声明字段默认放行**、无模式级 baseline（§4.5） | `WorkflowRuntimeAppService.cs:957-990`、`:983-986` |
| 「字段权限留到待办运行时再做，定义里先不拆表」 | 已落地，改为陈述现状并链接 §3.5 | `WorkflowRuntimeAppService.cs:1498` |
| §7 缺陷表把 W1–W9 全列为**未修复** | 与 §6 的 ✅ 自相矛盾。本次统一为「W1/W3/W4/W5/W8 已修；W2/W6/W7/W9 部分修复；W1 的校验可被 §4.2 绕过」 | `WorkflowDefinitionValidator.cs:50-150`、`WorkflowRuntimeAppService.cs:484-492` |
| 「测试 23 个用例 / 23/23 通过」 | 实测 **62** 个用例（58 个测试特性 = 56 `[Fact]` + 2 `[Theory]`，+6 `[InlineData]`）；分文件 39+11+7+4+1，其中运行时路径**零覆盖** | `tests/Meta.Dow.Orchestration.Tests/` |
| `file:///d:/Project/...` 绝对链接 | 全部改为仓库相对路径 | — |

**新增内容**：§2 唯一权威能力总表（28 行，含证据行号）；§4 十二条缺陷（其中 §4.2「已发布定义可改写」、§4.7「草稿 vs 快照不对称」、§4.9「补丁丢失」、§4.10「并发无保护」为本轮实测新增）；§3.7 测试覆盖真相。

**保留内容**：§1.3「为什么不选每节点一张表」的设计论证、§1.4 对标表、§3.2 节点表、§5 规划清单（由旧版「未落地」列表重排并逐条标 ❌）。
