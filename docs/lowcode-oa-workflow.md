# OA 审批流：表单绑定与节点规则

本文记录方案选择、参考，以及已经落地的设计器和待办运行时。一个审批流只绑定一张表单。

## 1. 结论

**一个审批流只绑定一张业务表单**（`WorkflowDefinition.formRef` → 表单库已发布编码）。

不要给每个节点再挂一张独立表单。节点上配置的是：同一张表怎么看、谁来办、办完按什么规则进入下一节点，以及节点前后调用哪条逻辑编排。

处理意见是任务上的一段文字，不是另一张表。

## 2. 为什么不选「每个节点一张表」

| | 一个流程一张表单 | 每个节点一张表单 |
|--|--|--|
| 谁在用 | 钉钉审批、飞书审批、Workflow-Vue3 | Flowable / Activiti 的 userTask.formKey，偏开发者 BPM |
| 条件 | 「金额 > 1000」始终指这张表上的字段 | 要先声明字段属于哪张表，条件容易指错 |
| 提交数据 | 一份 `record` 贯穿全程，提交流和节点前后逻辑都读它 | 多份 record 要拼，逻辑编排入参不稳定 |
| 办理人 | 节点只决定人和通过规则 | 节点还要决定换哪张表，配置量翻倍 |

节点如果只是「这一步有的字段只读、有的可改」，用字段权限表达，仍然是同一张表。字段权限留到待办运行时再做，定义里先不拆表。

以后若某一步必须另填一组结构化数据，再给该节点加可选的补充表单。默认不开。

## 3. 参考

| 来源 | 取什么 |
|------|--------|
| [Workflow-Vue3](https://stavinli.github.io/Workflow-Vue3/dist/index.html#/) | 纵向节点：发起人 → 审批人 → 抄送 → 条件 → 结束；审批人上配人或签/会签 |
| [lowflow-design](https://tsai996.github.io/lowflow-design/) | 同一类钉钉树，节点抽屉而不是 BPMN 画布 |
| 钉钉 / 飞书审批模板 | 一个模板一张表单；节点配置办理人、会签或签、审批意见 |
| 本仓库 [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md) §6 | 人审和逻辑编排分开。节点前后挂 FlowKey，编排画布里不画审批人 |
| 本仓库逻辑编排 | 节点前、节点后、选人，都调用已发布 `flowKey`，不在审批引擎里写 SQL |

## 4. 节点

流程树仍是 `ProcessJson`：`start → … → end`，`childNode` 指向下一节点。`type=approver` 在界面上叫 **接收**（兼容已保存的流程，不改类型名）。

| 界面 | JSON `type` | 作用 |
|------|-------------|------|
| 开始 | `start` | 发起。可配节点前、节点后逻辑 |
| 接收 | `approver` | 办理人处理。意见、多人规则、进入下一节点的条件 |
| 抄送 | `cc` | 通知，不挡流程 |
| 条件 | `condition` | 分支说明（完整多分支后置） |
| 结束 | `end` | 终止。流程级「结束后逻辑」在左侧基本信息 |

接收节点上的字段：

| 字段 | 含义 |
|------|------|
| `beforeFlowKey` / `afterFlowKey` | 本节点开始前、完成后调用的逻辑编排。空则不调 |
| `opinion` | `none` 不填，`optional` 选填，`required` 必填 |
| `assigneeType` | `role` 角色，`user` 指定用户，`starter` 发起人，`manager` 发起人部门主管，`formField` 表单字段里的人，`flow` 逻辑编排返回候选人 |
| `assigneeValue` | 角色码、用户名或字段名 |
| `assigneeFlowKey` | `assigneeType=flow` 时的流程编码。约定出参为用户名数组，或 `{ users: [] }` |
| `multi` | `single` 单人；`all` 会签，全部通过才向下；`any` 或签，一人通过即向下；`sequential` 依次；`ratio` 达到比例后向下 |
| `multiRatio` | `ratio` 时的百分比 |
| `conditionField` / `conditionOp` / `conditionValue` | 旧的单条比较，仍能跑。新的离开条件写在 `leaveCondition`：`items` 是编号条件，`combine` 是 `1 and (2 or 3)`，留空表示全部并且 |

流程级仍保留 `beforeStartFlowKey`、`afterEndFlowKey`，管整条流程的启动前和结束后，不代替节点上的前后逻辑。

## 5. 和逻辑编排的数据

审批流不写库。需要算数、改状态、发消息时，挂已有逻辑编排。

办理时传给这些流程的入参约定：

```json
{
  "formRef": "leave",
  "record": { "days": 3 },
  "nodeId": "approver_1",
  "opinion": "同意",
  "pass": true
}
```

`record` 就是这一张绑定表单的提交数据。子表仍是 `record` 里的数组，编排里用 `for` 遍历，见表单子表实现。

数据调入、节点前后逻辑、选人流程，都是这条约定的不同 `flowKey`，不是新引擎。

办理时多传一个 `pass`：通过为 `true`，驳回或未达到进入下一节点的条件为 `false`。选人流程的出参仍是用户名数组或 `{ users: [] }`。

## 6. 现在做到哪

已落地：

- 设计器对齐逻辑编排的画布：左侧节点库按住拖到点阵画布，从连接点拉出连线，点中节点或连线后在右侧改属性。工具栏有缩放、适应、撤销和小地图。开始节点固定。一条出线直接往下走；同一个节点有多条出线时，按连线上的条件选择，字段为空的那条是默认分支。保存格式是 `nodes` / `edges`。打开旧的 `childNode` 链时会自动铺到画布上。
- 运行时：`WorkflowInstance` + `WorkflowTask`。发布后在列表上「发起」，传入一份 `record` JSON。`beforeStartFlowKey` 失败则实例不会创建。
- 开始节点立刻走完（节点前、节点后逻辑），接收节点生成待办后停下。抄送只记通知，不挡流程。
- 我的待办：同意或驳回。意见必填时不填不能通过。
- 多人：`single` 一人通过即向下；`all` 全部通过才向下，一人驳回即结束；`any` 一人通过即向下，全部驳回才结束；`sequential` 按顺序激活下一人；`ratio` 达到百分比才向下，剩下的票数已经不够时结束。
- 离开接收节点、以及多条出线上的分支，都是同一套条件：多条编号条件，组合式同逻辑编排（`1 and (2 or 3)`，留空则全部并且）。每一边可以取单据路径（`days`、`items.0.qty`）、系统变量（发起人、当前办理人、当前日期、表单编码、流程编码），或一条已发布逻辑编排的返回路径。查库写在那条编排里。只有一条出线且连线上写了条件时，不成立就结束；多条出线时先匹配写了条件的线，都不成立再走没写条件的默认分支。
- 节点前、节点后、选人、流程开始前、结束后，都调用已发布 `flowKey`（`IPublishedFlowInvoker`）。失败则这一步中断，错误返回给调用方。

办理人：

| 规则 | 运行时 |
|------|--------|
| `user` | `assigneeValue` 是用户名 |
| `starter` | 发起人用户名 |
| `role` | 一个名额，角色内任一人可办。角色码为空则报「没有办理人」 |
| `manager` | 当前没有部门树，按角色码匹配。`assigneeValue` 为空时用角色 `manager` |
| `formField` | `record` 里该字段的字符串，或字符串数组 |
| `flow` | 调用 `assigneeFlowKey`，解析用户名数组或 `{ users: [] }` |

角色办理人是一个名额，不是「角色里每个人各一条待办」。多人来自表单字段或选人流程返回的多个人。

未落地：

- 同一张表在不同节点上的字段只读/可编辑。仍然是这一张表，定义里不拆表。
- 画布上的条件节点用来分叉。连线上是一组可组合的条件，不写条件的连线是默认分支。接收节点上的离开条件用同一套规则。

接口：`POST /api/orchestration/workflow-runtime/start`，`GET .../my-tasks`，`POST .../tasks/{id}/complete`。权限沿用 `Orchestration.Workflows`。前端在 OA 审批流列表，不另开菜单。

代码：设计器 `vue-demo/apps/web-antd/src/views/orchestration/workflows/designer.vue`，列表 `workflows/index.vue`，运行时 `WorkflowRuntimeAppService`。
