# 逻辑编排

把 if、顺序、循环、并行和内部调用抽成 `POST /api/logic/{flowKey}`。主对标是 LiteFlow。运行保障对照 n8n 和 Temporal，不照搬它们的产品形态。

证据在 [契约](./detail/logic-orchestration-contract.md)、[评审](./detail/logic-orchestration-review.md)、[方案](./detail/logic-orchestration-plan.md)。代码基线 `73aec3d`。设计器在 `D:\Project\vue-demo`，对应提交 `6f88348`。

引擎质量评估、逐条不足清单和十维对标矩阵在 [评估](./detail/logic-orchestration-assessment.md)。

## 已实现

调度只有 `RunSegmentAsync`。主链、ForEach、Parallel 共用它。

- 节点：Start / End、Condition、Http、Code（Jint）、Assign / Log / Mask / Throw、RabbitMQ 广播、SubFlow、ForEach、Parallel、Wait、资源 CRUD（含批量三入口）。
- 发布校验：出边基数、可达性、控制流结构、超时取值、写重试必须声明幂等键、节点类型白名单、输出契约、**未知与未实现 DSL 字段**。`sql` / `switch` 在发布期拒绝。
- 运行保障：HTTP 非安全方法不重试；节点超时包在唯一分发点；非 HTTP 写默认不重试；批量写在一个 SQL 事务里，批量删除仍走 `DeleteFlowKey`。
- 出参按角色和权限在接口层裁剪，子图透传调用方角色。
- 触发：REST、RabbitMQ、Kafka、MQTT、Cron。Cron（`FlowScheduleHostedService`）取分布式锁后再触发；Rabbit / Kafka / MQTT 三个 `BackgroundService` 仍没有这把锁。
- 版本快照、`FlowUsage` 影响索引、发布 / 停用 / 删除均失效缓存，并通过共享失效代数跨副本生效。
- **ForEach 的一项**：循环体可以是一段脚本。种子 `cf-line-check` 的 `loopBody` 是 Start → Code → End；Code 的 `return` 对象经 End 的 `finalOutputs`（`from: return`，`promote: true`）成为 `items[i]`。脚本里没有 `break` / `continue`，跳过一行用返回字段 `keep: false`。`itemErrorMode=continueOnError` 时，抛错记入 `failed[]` 并继续。
- **Parallel 的一条分支**：调用一条已发布流程。`FlowDslBranchDsl` 写 `ref`、`subFlowKey`、`inputs`、`outputs`。`ProjectBranchOutputs` 按声明的 `outputs` 投影被调流程的结果；`outputs` 为空则保留被调流程的完整结果。`body` 与 `subFlowKey` 同时存在时执行 `body`。种子 `cf-parallel-demo` 调用 `cf-branch-basic`、`cf-branch-risk`、`cf-branch-audit`。
- **`cf-` 演示可改可发布**。`IsImmutableSystem` 对 code 以 `cf-` 开头的系统行放行 `UpdateAsync` / `PublishAsync`。`Delete` / `Disable` / `Enable` 仍锁全部 `IsSystem`。资源系统流（`sys.*` 与 `SystemResourceFlowKeys`）保持锁定。`ControlFlowDemoSeedContributor` 新建行 `isSystem: false`，重跑迁移时对已有行调用 `ClearSystemMark`。未跑迁移前，库里的旧行仍可能是 `IsSystem=true`，保存和发布仍按 `cf-` 前缀放行。
- **出站与内联 SQL 安全闸门**：Http 出站拒绝私网、回环、链路本地与云元数据地址（可配白名单豁免，默认禁止跟随重定向）；内联 SQL 写判定覆盖 `WITH` 前缀并同时作用于 `db.query`。

## 已修复的不足

评估文档列出的轨道 A 护栏已落地 A1、A3、A4、A5、A7、A8（详见[评估](./detail/logic-orchestration-assessment.md)§8）。条件算子另列一行，不占用 A6：

| # | 事项 | 落点 |
|---|---|---|
| A1 | 未知 DSL 字段发布报错 | `DslSchemaValidator`：反射生成白名单（模型加字段自动跟随）+ Levenshtein 拼写建议 + 显式拒绝「模型有但无消费点」的字段 |
| A3 | 异步作业持久化、失败回写 | `FlowAsyncJob` 实体 + `FlowAsyncJobDispatcher` + `FlowAsyncJobWorker`；替换原`Task.Run` fire-and-forget，失败必落库 |
| A4 | Wait 节点 | `ExecuteWaitAsync`：等待有界（`MaxWaitTimeoutMs`）、超时以专用异常类型表达、默认失败中断可显式放行 |
| A5 | 停用 / 删除失效缓存并跨副本广播 | `FlowDefinitionInvalidationBroadcaster`（共享单调代数）+ `PublishedFlowCache` 记代数比对；新增 `DisableAsync` / `EnableAsync` |
| A7 | Http 出站 SSRF 防护 | `HttpEgressGuard`：协议、主机白名单、`localhost`、解析后逐地址判定（含 IPv4-mapped IPv6、云元数据） |
| A8 | SQL 写判定覆盖 `WITH` 与 `db.query` | `SqlWriteClassifier`（独立成类以便纯单测）+ `CodeSandboxExecutor.Query` 补闸门 |
| — | 条件算子与控制流语义 | 补 `startswith` / `endswith` / `in` / `notin` / `between` / `isnull` / `isnotnull`；`strategy` 加白名单并明确 `firstMatch` / `allMatch` 语义。这一项不是轨道 A 的 A6；A6 仍是下方「单流程调用权限」，未做 |

同时收紧了三处「配置被接受但不生效」：`condition.strategy` 拼错不再静默退化、`entry` 只配 `combine` 不再恒真、出边 `combine` 无 `items` 不再被忽略。

### 校验覆盖范围补齐（第二轮）

整改完成后**按文档逐条回读代码**，发现校验普遍存在同一个缺口：**只遍历了最显眼的那一层**。已修 5 处：

| 问题 | 修法 |
|---|---|
| `payload.item` 被误列入「未实现字段」黑名单，导致该形态无法发布（而提示语恰好说它受支持） | 移除黑名单条目，改由 `ValidateSemantics` 按**取值**接管 |
| `payloadMode=array` 无分支，配了静默退化为整包 inputs | 发布期取值白名单（`object` / `raw`） |
| 出参契约校验只遍历根级 `outputs`，只配 `End.finalOutputs` 的流程可完整绕过 | 遍历范围扩到 End 节点（条目 21 转 ✅） |
| 嵌套 `visibleTo`（`map.item[]` / `group.header[]` / `group.children`）未经校验，拼错即「配了可见性却全可见」 | 新增递归校验 |
| `group.header[].visibleTo.permissions` 不被收集，权限模式下分组表头对所有人消失 | 补全 `CollectPermissionNames` 范围 |

⚠️ **`payload` 那条是第二轮自己引入的回归**——第一轮为了「让静默失效变成显式拒绝」，误把一个已实现字段当成未实现。这类错误的教训是：把"未实现字段"列成黑名单时，必须先确认它真的没有消费点，而不能只看它是不是没被文档写全。

测试从 91 条增至整改落地时的 297 条。2026-10-15 对 Debug 构建执行 `dotnet test tests/Meta.Dow.Orchestration.Tests --list-tests`，展开 `InlineData` 后为 **302** 条。多出的用例覆盖 `cf-branch-*`、`cf-parallel-demo` 与 `cf-line-check`。

## 部署配置

新增的出站闸门默认取最严值，**必须显式配置才能让 Http 节点访问内部服务**：

```jsonc
// src/services/saas/host/Meta.Dow.SaaS.HttpApi.Host/appsettings.json
"Orchestration": {
  "Http": {
    "AllowedHosts": [],      // 可信主机白名单；空 = 不允许任何内网出站
    "AllowRedirects": false  // 默认禁止跟随 3xx，防止「公网 302 跳内网元数据」
  }
}
```

⚠️ **当前 18 个 `appsettings*.json` 均无 `Orchestration` 节**。这不是缺陷（默认即最安全），但若部署后 Http 节点报「resolves to a non-routable address」，原因就在这里。

## 前端影响

前端在 `D:\Project\vue-demo`（**不在本仓**），其节点类型清单、条件算子清单、流程操作都是**双向硬编码**，需同步跟进。完整清单与具体落点见[评估文档](./detail/logic-orchestration-assessment.md)§8.8。

**设计器已跟上的两项**（`vue-demo` `6f88348`）

| 项 | 前端落点 |
|---|---|
| ForEach 用脚本编辑当前项 | `designer.vue` 的循环脚本面板；`dsl-graph.ts` 的 `buildLoopScriptBody` / `readLoopScript`。查看系统流时脚本和示例仍可改，画布、保存、发布保持锁定；改完用「试运行」。`cf-` 可以保存并发布 |
| Parallel 分支写成调用契约 | 每条分支：`ref`、可选、已发布流程、入参行、出参行。选中 `subFlowKey` 时去掉内联 `body`。`DslBranch.outputs` 写回 DSL |

**仍须改的四项**（2026-10-15 复核，设计器里仍然没有）

| 项 | 前端落点 |
|---|---|
| 接入 Wait 节点 | `flow-meta.ts` 的 `FlowExecutableKind` / `EXECUTABLE_NODE_KINDS` / `NODE_PALETTE`，**外加** `designer.vue` 的 `paletteGroups`（另一份白名单，只加前者左栏不显示）；`dsl-graph.ts` 的 `DslNode` / `defaultNodeData` / `dslToGraph` / `graphToDsl` |
| 条件算子补 7 个 | `condition-items-editor.vue` 的 `OP_OPTIONS`（现 10 项）+ `ConditionItem.right` 类型放宽 + `between` 用双输入组装 `[min, max]` |
| 停用 / 启用按钮 | `api/saas/orchestration.ts` 补两个函数 + `definitions/index.vue` 操作列。状态渲染层已就绪，只缺 API 与按钮 |
| 出边 `combine` 无 `items` 的本地兜底 | `graphToDsl` 的 Condition 分支——用户「一键编号」后清空条件会产出后端拒绝的 DSL |

⚠️ **别改错文件**：`shared/filter-model.ts` 也有 `between` / `in`，但它服务资源与报表筛选器（右值 `{min,max}`，半开区间），与编排 Condition 的 DSL 语义是两套东西。

规则文件 `.cursor/rules/logic-orchestration.mdc` 的现状段已按 `73aec3d` 改过：`wait` 视为已实现，`sql` / `switch` 仍是发布期拒绝。

**根因**：前端清单与后端 `SupportedNodeKinds` 靠人守，无同步机制。若轨道 B 还要继续补节点，建议先加 `GET .../node-kinds` 端点让清单由服务端驱动。

## 剩余不足

| 对标有的 | 本仓现状 |
|---|---|
| LiteFlow 的 SWITCH、EL 文本 | 多路分支只用 Condition。独立 Switch 会在发布时被拒绝，不是「配了就能跑」。表达式只有编号布尔组合，无函数 |
| LiteFlow / 宜搭的 while、break | 只有 ForEach。`joinMode=any` 不取消其余分支，`failFast` 整窗跑完才抛 |
| Temporal 的幂等与超时护栏 | 幂等键只在发布时检查「声明了」，运行时不去重 |
| 会话事务或 Saga | 无 `txMode`，同一数据源上多次 `db.execute` 不共用事务 |
| 单条流程的调用权限 | 持有 `Instances.Run` 即可调用全部已发布流程（`Orchestration.Logic.{flowKey}` 未做） |
| ABP 的数据访问层强制授权 | IAM 行级数据范围已写好但零调用点，报表与资源级授权可被 `skipValidation` 绕过 |
| 配置必须生效 | `source=expr`、旧 `responseVariable` 等字段仍无发布期拦截（`responseVariable` 是当前唯一"有消费点、不在黑名单、也无拦截"的数据暴露路径）；`inputs[].type` 不参与必填校验 |
| 多副本异步作业消费 | `FlowAsyncJobWorker` 无 claim/乐观锁，多副本会重复消费同一条 `Queued` 作业并各自外呼一次（单副本部署无此问题） |
| 步骤预算与适用边界 | 一次请求内最多 200 步、状态常驻内存，这条边界仍未进发布期校验，也未对外声明 |

## 适用边界

一次请求内最多 200 步，状态常驻内存，实例事后落库。**适合单次 burst、短流程、可容忍重跑。不适合跨重启的长流程、跨节点强一致写、需要续跑的流程。**

这条边界目前只在评审文档里自觉，没有进发布期校验，也没有对外声明。这是最大的期望管理风险——见[评估](./detail/logic-orchestration-assessment.md)§4.3。

## 后续规划

分三条轨道。**护栏和补节点不抢同一批人**：护栏不做就不能对外，补节点只是能力增强。轨道 C 与轨道 A 并行，因为它是发现问题的前提。

### 轨道 A · 护栏（P0，不做就不能对外）

| # | 事项 | 缺陷 | 状态 |
|---|---|---|---|
| A1 | 未知 DSL 字段改发布报错 | 总开关 | ✅ 已实现 |
| A2 | `txMode=sameDataSource`：同一数据源上多次 `db.execute` 共用事务 | 与 A1 同批 | ⬜ 待做 |
| A3 | 异步节点持久化 JobId、写节点状态、失败回写 | C1 | ✅ 已实现 |
| A4 | 补Wait；未实现前禁止引用尚未返回的异步出参 | A3 不闭环 | ✅ 已实现 |
| A5 | 停用和删除也失效缓存，并跨进程通知 | 多副本 | ✅ 已实现（最终一致） |
| A6 | `Orchestration.Logic.{flowKey}`，没有该权限不能调对应流程 | 授权粒度 | ⬜ 待做 |
| A7 | Http 出站拒绝私网、回环和链路本地地址 | SSRF | ✅ 已实现 |
| A8 | 只读 SQL 判定覆盖 `WITH` 前缀和 `db.query` | 写闸门绕过 | ✅ 已实现 |
| A9 | 数据范围**接线 `ApplyDataPermission` 或从产品口径移除**，二选一 | 不留中间态 | ⬜ 待做 |
| A10 | 幂等键运行时去重，不只在发布期检查「声明了」 | A11、A12 的前置 | ⬜ 待做 |
| A11 | 报表和资源级授权下沉 | `skipValidation:true` 绕过 | ⬜ 待做 |
| A12 | Rabbit/Kafka/MQTT 触发器分布式锁，client id 固定 | 多副本重复触发 | ⬜ 待做 |

### 控制流语义（已并入实现）

条件算子补齐（`startswith` / `endswith` / `in` / `notin` / `between` / `isnull` / `isnotnull`），
`strategy` 加白名单并明确 `firstMatch` / `allMatch` 语义，`entry` 与出边 `combine` 的空 `items` 在发布期即被拒。

> 轨道编号与[评估文档](./detail/logic-orchestration-assessment.md)§二 的表格存在历史错位（评估文档把「触发器分布式锁」记作 A11、「报表授权下沉」记作 A12，本文档顺序相反）。**核对时按事项名称对齐，不要按编号。**

### 轨道 B · 补节点（P2，不影响正确性）

| # | 事项 | 前置 |
|---|---|---|
| B1 | 独立 Switch 节点 | 先有执行分支，再从发布拒绝改为放行 |
| B2 | 独立 Sql 节点 | 数据源白名单校验 |
| B3 | while、range-for、break / continue | 先修准 `failFast` 与 `joinMode=any` 的取消语义 |
| B4 | 侧栏 EL 文本 | 受限求值器加白名单函数，不引入 eval |
| B5 | 画布内 MqttPublish | — |
| B6 | 领域组件目录（注册式，替代堆 HTTP） | — |

### 轨道 C · 质量门禁（与 A 并行）

轨道 A 已顺带完成其中一项的轻量版：把安全判定从「依赖外部执行器、无法测试」的位置抽成纯静态类（`SqlWriteClassifier` / `HttpEgressGuard` / `DslSchemaValidator`），测试数从 91 增至 297。完整形态仍待做。

| # | 事项 |
|---|---|
| C-1 | Testcontainers 集成测试宿主，替换测试脚手架的 `null!` 注入 |
| C-2 | 启用分析器与 `TreatWarningsAsErrors` |
| C-3 | CI 加 `docker build`；修 7/7 Dockerfile 的 `COPY --from=publish` |
| C-4 | DSL JSON Schema 入 CI |
| C-5 | `version` 真校验、变更日志、迁移指南 |

### 待评估

持久化执行（断点续跑）是**决定引擎适用边界的根本问题**，不是普通排期。低成本形态是 `Paused` + `ResumeFrom`；是否引入 Elsa 3 / Temporal 风格尚未决策。

节点级 OpenTelemetry 依赖轨道 A 的埋点，字段级审计依赖 A9 落地，均未排期。

## 已知的实现取舍

这些是主动的选择，不是遗漏。记在这里避免下次重新评估。

| 取舍 | 选择 | 理由 |
|---|---|---|
| 跨副本失效 |共享单调「失效代数」比对，不用分布式缓存存 DSL 本体 | DSL 本体进 Redis 会带来跨版本格式兼容问题，且热路径每请求走网络。最终一致，广播失败只降级不阻断发布 |
| Wait 超时表达 | 用专用异常 `FlowAsyncJobTimeoutException`，不用通用 `UserFriendlyException` | 上层必须能区分「作业失败了」和「等不到结果」；靠字符串匹配分辨不可维护 |
| `between` 区间 | 闭区间 `[min, max]` | 与 DSL 条件语义一致；与 `FilterSqlBuilder` 的半开区间不一致属已知缺陷（C20），未统一 |
| SQL 写判定 | 认不出关键字时保守按写处理 | 写闸门误拒只是多要一个权限，漏放则是越权，代价不对称 |
| 出站重定向 | 默认禁止跟随（`HttpAllowRedirects=false`） | 允许 3xx 跟随会让「公网 302 跳内网元数据」绕过首跳判定 |

## 明确不做

连接器市场、拖拽页面渲染引擎、把审批并进这个引擎、无沙箱脚本、无白名单的生产库 SQL、跨库 XA / 两阶段提交、分布式 worker 集群。
