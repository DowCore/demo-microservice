# 逻辑编排 · 引擎评估与整改重构

这是一份**评审视角**的文档，不描述功能，只评价质量。它回答两个问题：这套引擎现在有多可靠，以及它相对成熟项目差在哪里。

- 被评估对象：[逻辑编排总文档](../logic-orchestration.md)
- 证据来源：[契约](./logic-orchestration-contract.md)（§2 唯一权威能力表）、[评审](./logic-orchestration-review.md)、[方案](./logic-orchestration-plan.md)、[对标](./logic-orchestration-benchmark.md)、[取舍](./logic-orchestration-tradeoffs.md)
- 代码基线：`91d75bd`；文档基线：`2dd0179`
- 单测基线：后端 91/91、前端 336/336
- **整改后**：§8.4 记录落地时 **297** 条。2026-10-15 对 Debug 构建 `dotnet test --list-tests` 为 **302** 条（§8.9，含控制流演示用例）

> **引用纪律**：本文**只引用符号名，不引用行号**。契约 §2–§13 的行号对应旧提交 `0c4f7eb`，`91d75bd` 批次使 `FlowExecutor.cs` 净增约 190 行，旧行号整体下移 0～+188。请一律按符号名检索。沿用这条纪律本身，就是本文对 §四 计数类断言错误问题的直接整改。
>
> **状态标注**：§二轨道 A 与 §三 清单中的条目已按§八的实施结果标注`✅` / `⬜`。**§一至§七 的评价与数字是整改前的快照，不再随后续修改回溯更新** —— 否则文档会失去"当时判断"这一独立价值。要看当前状态请读§八与[总文档](../logic-orchestration.md)。

## 一、总体评价

**综合约 5.4 / 10。一份"优秀的架构决策 + 不合格的可靠性兑现"的引擎。**

设计品味明显高于工程完成度。骨架做对了，而且做对的地方在同类自研低代码里并不常见：

- 全仓**只有一个调度原语** `RunSegmentAsync`，主链 / ForEach / Parallel 三处复用，结构上杜绝了"第二套调度器"。
- 节点分发只有一个点 `DispatchNodeAsync`，超时包裹挂在这一个点上，因此不存在"某些节点忘了包超时"。
- 出参 `visibleTo` 做角色/权限级裁剪，未知 mode 在发布期拒绝。
- 发布期有图级校验：可达性 BFS、出边基数、`retryOnWrite` 强制幂等键、32 种节点类型白名单、输出契约必填。
- 多租户谓词在 `FilterSqlBuilder` 里是**结构保证**（用户条件树与租户谓词并列后统一 `AND`），不是字符串拼接。
- 节点 I/O 显式契约 + 路径糖（`x.count`、`x.sum.amount`）在这一点上**优于 LiteFlow**。

短板恰好集中在最不该短的一块——**运行保障**，而这块正是引擎对外的承诺面。文档自评运行保障 3.5、可验证性 3.0，这个自评是诚实的，也是准确的。

### 一句话根因

**这套引擎按"被代码内嵌调用"设计，却被当"对外服务"在用。**

内嵌场景下，调用方的 `IUnitOfWork` 就是事务边界、调用方进程就是可靠边界。引擎本身不需要事务、不需要幂等、不需要续跑。一旦它发布成 REST + Rabbit/Kafka/MQTT + 多副本 + `async:true`，这些隐含边界同时消失，而护栏没有跟上。

这不是新问题，而是同一笔"身份升级"欠账的剩余部分。R1–R4（HTTP 安全默认、超时包裹、读写分离弹性、批量单事务）补上了最先漏水的四块板，其余 P0/P1 都是同一笔账。

### 越线对照表

| 越过的安全线 | 随之失效的隐含假设 | 是否已补 |
|---|---|---|
| 发布为 REST API | 调用方已鉴权、已幂等、已处理错误语义 | 部分 |
| 接入 Rabbit / Kafka / MQTT | 消息不会重复、失败会重投、有死信 | 否 |
| 提供 `async: true` | 结果有人回收、失败有人告警 | 否 |
| 提供批量写 | 单节点事务即足够 | 部分（仅 `db.batch`） |
| 多副本部署 | 进程内状态就是全局状态 | 否 |

## 二、优先整改：护栏 vs 节点，两条不同的轨道

原文档的规划表把"补护栏"和"补节点"混在同一优先级里排队。**这两类性质完全不同，不该抢同一批人**：护栏不做就不能对外，补节点只是能力增强，不影响正确性。

### 轨道 A · 护栏（P0，不做就不能对外）

排序原则：同一笔债的一起做，先做会让"配置生效性"恢复可信的那批。

| # | 事项 | 对应缺陷 | 为什么是 P0 | 状态 |
|---|---|---|---|---|
| A1 | **未知 DSL 字段改发布报错** | C4/E4 | tradeoffs 第二原则"静默零容忍"被自己违反。这是所有"配了等于没配"问题的总开关 | ✅ |
| A2 | `txMode=sameDataSource`：同一数据源上多次 `db.execute` 共用事务 | C4/E4 | 必须与 A1 同批落地，否则修好 `txMode` 仍会被未知字段静默吞掉 | ⬜ |
| A3 | 异步节点持久化 JobId + 写节点状态 + 失败回写 | C1/C1b | "承诺先于能力"最典型的一处，且宿主重启即丢任务 | ✅ |
| A4 | 补 `Wait` 节点；未实现前禁止引用尚未返回的异步出参 | E3 | 无 `Wait` ⇒ 异步出参在下游**永远不可用**，A3 单独做完仍不闭环 | ✅ |
| A5 | `Disable` / `Delete` 也失效缓存，并跨进程广播失效 | C3/E8 | 已下线流程在其他副本仍可调用，最长 10 分钟 | ✅（最终一致） |
| A6 | `Orchestration.Logic.{flowKey}` 逐流程调用权限 | S8/S14/E10 | 一个 `Instances.Run` 目前可调全部已发布流程 | ⬜ |
| A7 | Http 出站拒绝私网、回环、链路本地地址 | S5 | 多租户环境下可配置出站 HTTP 节点 = 内网探测面 | ✅ |
| A8 | 只读 SQL 判定统一：覆盖 `WITH` 前缀与 `db.query` | S4 | `WITH x AS (…) DELETE FROM t` 当前无需 `Sql.Write` 即可通过 | ✅ |
| A9 | 报表/资源级授权下沉；数据范围**接线或从产品口径移除** | S8/S9 | `DataPermissionQueryableExtensions` 全仓零调用点。防线已存在但未接线 | ⬜ |
| A10 | 幂等键运行时去重（不只发布期检查"声明了"） | Temporal 差距 | 是 `retryOnWrite` 与断点续跑共同的前置 | ⬜ |
| A11 | MQTT/Rabbit/Kafka 触发器分布式锁，client id 固定 | S15 | 仅 Cron 有锁；MQTT client id 含随机 Guid ⇒ 多副本必然重复触发 | ⬜ |
| A12 | OA：驳回语义修正 + `ConcurrencyStamp` + 已发布禁原地改写 | C5/C6/C7/C8 | 驳回被静默当成通过；审批表单补丁静默丢失 | ⬜ |

A9 遵循一条"二选一、不留中间态"原则：数据范围要么接上 `ApplyDataPermission`，要么明确从产品口径移除，不能保留"看起来有"。

> ⚠️ **本表的编号与总文档不完全一致**：总文档沿用了更早一版编排，把 A11/A12 分别记作「报表和资源级授权下沉」与「触发器分布式锁」，并把「逐流程调用权限」记作 A6。核对时按**事项名称**而非编号对齐。落地情况与取舍说明见 §八。

### 轨道 B · 补节点（P2，不影响正确性）

| # | 事项 | 前置 |
|---|---|---|
| B1 | 独立 `Switch` 节点 | 先有执行分支，再从发布拒绝改为放行 |
| B2 | 独立 `Sql` 节点 | 数据源白名单校验 |
| B3 | `while`、range-`for`、`break` / `continue` | `ForEach` 的 `failFast` 取消语义先修准 |
| B4 | EL 文本 / 侧栏表达式（THEN、IF） | 受限但真正的求值器，加白名单函数，**不引入 eval** |
| B5 | 画布内 `MqttPublish` 节点 | — |
| B6 | 领域组件目录（注册式，替代堆 HTTP） | — |

### 轨道 C · 质量门禁（与 A 并行，因为它是发现问题的前提）

| # | 事项 | 对应缺陷 |
|---|---|---|
| C-1 | **引入 Testcontainers 集成测试宿主，替换测试脚手架的 `null!` 注入** | G2 |
| C-2 | 启用分析器 + `TreatWarningsAsErrors` | G3 |
| C-3 | CI 增加 `docker build` 步骤，修 7/7 Dockerfile 的 `COPY --from=publish` | G1 |
| C-4 | DSL JSON Schema 入 CI | 可验证性 3.0 的直接成因 |
| C-5 | `version` 字段真校验 + 变更日志 + 迁移指南 | DSL 已三次演进无版本记录 |

**C 轨道必须与 A 轨道并行，不能推后。**理由见 §五。

## 三、逐条不足清单

按"承诺 vs 能力"的错配程度排序，不按文档原编号排。

### 3.1 会造成错误结果的（结果不可信）

| 严重度 | 缺陷 | 机理（根因） | 位置 |
|---|---|---|---|
| 高 | 异步节点结果与失败**全部丢失** | `Task.Run` + `CancellationToken.None`；不写 outputs、不落 JobId、不更新节点状态；失败仅 `LogWarning`；节点永停 `AsyncQueued` 而实例判 `Succeeded`；宿主重启即丢 | `FlowExecutor.ExecuteNodeAsync` |
| 高 | 异步不受节点超时约束，`failWhen` / `failItems` 永不被求值 | 同上，`CancellationToken.None`；异步分支不调 `CheckFailWhen` | `FlowExecutor.ExecuteNodeAsync` |
| 高 | 跨节点写库非原子，且**配置了也不报错** | 无 `txMode` 字段 + 未知 DSL 字段静默忽略 ⇒ 使用方误以为已开启；`db.execute` 每句新建连接自动提交，仅 `db.batch` 单节点内单 SQL 事务 | `ParameterizedSqlExecutor` |
| 高 | 只读权限被 `WITH` 前缀绕过 | `EnsureSqlWrite` 把 `select` / `with` 同判为读；`db.query` 完全不检查写权限 | `CodeSandboxExecutor` |
| 中 | 条件节点可静默走错边 | 节点无 `items`、只在边上写 `combine` → 掉进 legacy `expression` / `when` 分支，`combine` 被忽略并静默走第一条边 | `FlowExecutor` 条件求值 |
| 中 | `strategy` 语义与命名不符 | 非 `firstMatch` 时不 break ⇒ **最后一条命中的边生效**；取值从不校验，拼错静默改走向 | `FlowExecutor` 条件求值 |
| 中 | `entry` 只配 `combine` 恒真 | `items` 为空直接 `return true`；发布校验也只在 `items.Count > 0` 时查 `combine` | `FlowExecutor` / `ValidateDsl` |
| 中 | 条件算子缺 `in` / `notIn` / `between` | 未实现 → 抛 `Unsupported condition op`（旧文档曾列为支持） | `FlowExecutor` 条件算子表 |
| 中 | `aggregate` 与 `map` 同配时 `map` 静默失效 | `Aggregate` 分支先 return | `FinalOutputAssembler` |
| 低 | 未命中 End 仍返回成功 | `Succeeded=true` 而输出为 null ⇒ `success:true, data:null` | `FlowExecutor` |
| 低 | `group.header[].visibleTo.permissions` 永不生效 | 判定侧会读，收集侧 `CollectPermissionNames` 从不遍历 `group.header` | `FinalOutputAssembler` |
| 低 | 旧字段 `responseVariable` 绕过可见性 | 直写 `vars`，不经 `outputs` / `visibleTo` | `FlowExecutor` |
| 低 | `joinMode=any` 不取消其余分支 | 只移除已完成 Task，其余副作用在流程继续后仍发生（**模型注释却写着"会取消"**） | `FlowExecutor` Parallel |
| 低 | `timeoutMs` 对 Code 节点无效 | Jint 硬顶 8s，校验却允许配到 600s 且无提示；`db.*` 同步阻塞，不受沙箱超时覆盖 | `OrchestrationConsts` |

### 3.2 会造成越权或泄露的（安全）

| 严重度 | 缺陷 | 机理（根因） | 位置 |
|---|---|---|---|
| 高 | Http 节点**零 SSRF 防护** | url 由节点配置 + 模板拼出，headers 任意；全仓无 host 白名单 / 私网 / 回环 / 链路本地拦截 | `FlowExecutor` Http 分支 |
| 高 | 报表数据范围可整体绕过 | `/api/logic/{flowKey}` 仅需 `Instances.Run` 且 `skipValidation:true`；`sys.resource.query` 可自带 `resourceCode` / `filter` / `summaryFields` ⇒ 被绕的是资源级授权（跨租户仍被顶层谓词挡住） | `LogicApiAppService` |
| 高 | IAM 行级数据范围**零落地** | `DataPermissionQueryableExtensions` 全仓零调用点；`DataScopeResolver` 只被"我的"接口消费 ⇒ 数据范围目前是配置界面，不是执行约束 | `Meta.Dow.Shared` |
| 高 | 表列 `Origin` 客户端可控 → 跨租户改写 | `Origin` 来自客户端 DTO；强制覆盖 `TenantId` 的逻辑**只在 Create 里** | `TableDefinitionAppService` / `ResourceCrudExecutor` |
| 高 | 破坏性 DDL 门禁缺失 | 只拦 `dropTable` / `truncate`；`DROP COLUMN` / `RENAME` / 破坏性 `ALTER` 直接执行；`Destructive` 标记只回传前端 | `SqlDdlExecutor` |
| 高 | 默认管理员口令是公开值 | 写在启动说明 + 新租户回退口令硬编码 | `TenantCreatedEventHandler` / `README` |
| 中 | 网关 12 条路由不挂授权策略 | 授权完全下沉到应用服务，而应用服务正是 A2/A6 要改的地方 | `gateway/appsettings.json` |
| 中 | 敏感信息进日志 | `IdentityModelEventSource.ShowPII = true` 无条件开启，覆盖 4 个进程 | 4 处 Host / AuthServer Module |
| 中 | `PruneObsoleteClients` 默认 `true` | 与 ID2052 事故同源的破坏性清理开关默认开 | `DbMigrator/appsettings.json` |

> 说明：S6（子图旁路 `visibleTo`）、S7（无契约时外泄整个 `ctx.Vars`）、S12（`visibleTo` 未知 mode 兜底放行）已在 `91d75bd` 修复，**不在此列**。

### 3.3 会造成治理失效的（多副本 / 版本）

| 严重度 | 缺陷 | 机理（根因） |
|---|---|---|
| 高 | 已下线流程仍可被调用 | `InvalidateByDefinition` 唯一调用点是 `PublishAsync`；`Disable` / `Delete` 不失效；进程内 `IMemoryCache` 跨副本不共享，绝对 10 分钟 + 滑动 3 分钟 TTL |
| 中 | 触发层多副本不安全 | 仅 `FlowScheduleHostedService` 用分布式锁；MQTT client id 含随机 Guid ⇒ 必然重复触发 |
| 中 | 无单条流程调用权限 | 只有 `Orchestration.Instances.Run`，持该权限者可调全部已发布流程 |
| 中 | 无在途实例版本锁 | 实例记 `version` ✅，但运行中实例不锁版本，也不订阅失效事件 |

### 3.4 配置"看起来生效、实际不生效"清单

这一类单独列出，因为它直接违反 tradeoffs 第二原则"**静默零容忍：配置被接受就必须生效**"。

| 字段 | 声称 | 实际 |
|---|---|---|
| `txMode` | （未定义但用户会写） | DSL 无此字段，未知字段静默忽略 |
| `source=expr` 的入参 | 支持表达式来源 | **未实现** |
| `inputs[].type` 强制 | 类型校验 | 仅规则层校验，`datetime` 不校验，无强制转换 |
| `transform`、`from` 模板、`outputs` 字面量 | 出参变换 | **无消费点** |
| `wrap` | 包裹输出 | 只识别 `"array"` |
| `description`、`displayName` | 文档作用 | 无消费点 |
| `sql`（节点字段） | SQL 节点 | 无 Sql 节点，该字段悬空 |
| `asyncJobId`（文档曾承诺） | 异步任务句柄 | **模型中不存在** |
| `previewAsRole` | 按角色预览 | DTO 无该字段 |
| `timeoutMs > 8000`（Code 节点） | 节点超时 | 沙箱硬顶 8s，无提示 |
| `db.execute` / `db.batch` 的重试 | 节点级重试 | 只覆盖资源 CRUD 与 Code 的 `db` 读 |

**这一张表是 A1（未知字段改报错）存在的全部理由。** 只要静默忽略还在，用户就无法区分"我配错了"和"引擎不支持"。

## 四、对比优秀项目的差距

benchmark 用五个阵营做十维矩阵——LiteFlow（编排主线）、n8n / Zapier（运行保障）、Temporal / Airflow / Camunda（运行保障真正参照物）、Dify / Coze、宜搭 / 简道云 / 微搭。

结论很清晰：**优势集中在契约、控制流原语、数据接入、版本治理、作为库嵌入；劣势集中在运行保障四格（重试、事务、持久化执行、可观测）全灭。**

### 4.1 逐项差距

| 维度 | 成熟项目做法 | 本仓现状 | 性质 |
|---|---|---|---|
| **持久化执行** | Temporal / n8n：事件溯源 + 确定性重放 + 断点续跑 | 内存态游标，实例**事后**落库，崩溃即丢，无重放。200 步硬上限，状态仅 4 态，无 `Paused` / `ResumeFrom` | **结构性**（物种差异） |
| **多路分支** | LiteFlow SWITCH、Camunda 排他网关是一等公民 | 无独立 Switch；`sql` / `switch` / `wait` 发布期被拒；只能靠 Condition，而 `strategy` 拼错会静默改走向 | 结构性 |
| **表达式语言** | LiteFlow EL、SpEL、宜搭公式 | 自写正则分词布尔求值，无函数、无 `in` / `notIn` / `between` | 结构性 |
| **循环语义** | `for` / `while` / `break` / `continue` + 明确作用域 | 仅 `ForEach`；`failFast` 整窗跑完才抛；`joinMode=any` 不取消输家 | 能力缺口 + 语义不严 |
| **事务与补偿** | Temporal：session / Saga / Outbox | 无 `txMode`、无补偿、仅单节点批量事务；未知字段静默忽略 | **结构性** |
| **幂等** | Temporal：Activity 至少一次 + 幂等键去重 | 只落了"发布时检查你声明了"这一半，**运行时不去重** | 结构性 |
| **节点级重试 + 错误工作流** | n8n：每节点 `retryOnFail` + 失败转错误工作流 | 仅全局 `NodeResiliencePolicy`（读 2 次 / 写 0 次），DSL 无 `retry`，无错误分支 | 能力缺口 |
| **版本治理** | Temporal：版本化部署 + 在途实例锁版本 + 事件失效 | 实例记 `version` ✅，缓存进程内、只在发布失效、无在途锁 | 结构性 |
| **触发层多副本** | 统一 registry + 争抢安全 | 触发器齐备 ✅，但仅 Cron 加锁 | 实现缺口 |
| **可观测与回放** | 节点级输入输出快照 + 重放可视化 | 无 OpenTelemetry；子图节点审计明细丢弃；无节点级 replay | 能力缺口 |
| 授权落点 | ABP / 主流：数据访问层强制 | 在应用服务与调用点：一个 `Instances.Run` 调全部流程 | **结构性** |
| 最小可见 | 默认拒绝，服务端强制 | 设计不错，但无契约时曾整包外泄、子图曾整体旁路（**均已修**） | 曾语义相反 |
| 设计期校验 | JSON Schema / XSD，非法图设计期被拒 | 强类型模型 + `ValidateDsl` 已补齐白名单与可达性；**但无环检测（运行期才查）、无 Warning 通道、`version` 不校验** | 结构性 |
| 节点 I/O | item 传递 / 数据关联 | 显式 `inputs[]` / `outputs[]` + 路径糖 | **持平或领先** |
| 节点类型覆盖 | 广泛 + 连接器生态 | 32 种内置节点；连接器生态弱（**明确不做**，见 tradeoffs） | 取舍 |

### 4.2 三个阵营的得失

**vs LiteFlow（编排主线，最接近）**
赢：画布 + 属性面板 + DSL 规范化（LiteFlow 本体是规则文件）、发布前图级校验、出参角色可见性、`FlowUsage` 影响面索引、节点 I/O 契约。
输：EL 表达式生态、热更新成熟度、`async` 分支不受超时包裹。
同构：都主张"控制流 + 已注册组件"，都明确"不做审批"。

**vs n8n / Zapier（差距最大）**
赢：可当库调用、契约 + 角色过滤 + 日志脱敏、发布前校验、库内计算。
输：节点级重试、错误工作流、部分重跑、执行数据 pin、连接器数量、queue mode 分布式执行。
**结论：n8n 的"执行可重放、失败可续跑"是产品级基础设施，也是最该抄的一块**（对应 A3 / A4 / A10）。

**vs Temporal / Airflow / Camunda（物种级差距）**
赢：零额外基础设施、DSL 由设计器产出、领域组件 vs 通用 Activity。
输：事件溯源 + 确定性重放、Activity 至少一次 + 幂等、Saga / 补偿、Timers / Signals、历史可回放调试。
关键判断：**不需要变成它们，但必须借"给每个外部副作用的下一步加护栏"这条纪律**——这条已经在 R1–R4 兑现了一半。

**vs 宜搭 / 简道云 / 微搭**
赢：`flowKey` 是跨应用**共享资产**（它们多为应用内私有）、代码调用入口、审计与脱敏粒度。
输：生态与开箱体验、完整交付闭环（应用外壳层仍在补）。

### 4.3 结构性天花板

单次 burst + 200 步游标 + 4 态状态机。这与 Temporal / Airflow 的"事件溯源 + 重放"是**两种物种**，不是同一物种的差实现。

所以 P2 的"评估持久化执行（Elsa 3 / Temporal 风格）"是**决定引擎适用边界的根本问题**，不是普通功能排期。而"短流程、跨重启不可续跑"这条边界目前**未被代码或文档显式声明**——这是最大的期望管理风险：用户会照 REST API 的心理预期配流程，然后踩坑。

## 五、两个被低估的问题

### 5.1 测试覆盖实质为零（G2）

91 个测试全是纯单测，且新增的 29 例同样只断言 `ValidateDsl` 的拒绝/放行。测试脚手架把 `httpClientFactory` / `permissionChecker` / `dataSourceResolver` / `codeSandboxExecutor` / `publishedFlowCache` / `resourceCrudExecutor` 以 `null!` 注入，意味着**HTTP、SQL、Code、子流程、资源 CRUD 的运行时路径一触达必然 NRE**。

叠加：

- **7/7 Dockerfile 不可构建**（阶段名 `build` / `final`，终阶却写 `COPY --from=publish`），且 CI 无 `docker build` 步骤 ⇒ 缺陷永不暴露
- 分析器未启用，162 个警告无门禁
- 前端 `designer.vue`（1640 行）/ `runtime.vue`（921 行）零覆盖
- 无集成测试宿主，认证 / 网关 / Mongo 链路无任何自动化验证

**对团队而言，这比任何单个功能缺陷都更危险**：当前没有任何自动化能发现系统坏了。所以 §二 的 C 轨道必须与 A 轨道并行，而不是排在它后面。

### 5.2 编排层缺陷被所有低代码能力同步放大

SaaS 服务占全仓约 78% 代码，`Orchestration/` 一个目录里塞了 18 个 AppService，而**平台自带的 CRUD 默认就走 `sys.*` 系统流程**。

也就是说，编排引擎的每一个坑——缓存失效、权限粒度、SQL 写闸门、跨节点事务——数据集合、表单、报表、OA 全部共享同一份缺陷。

这正是"P0 止血优先于补新节点"的理由，也是本文把 A 轨道排在 B 轨道之前的根本原因。

## 六、文档治理缺陷

这部分单列，因为它会持续污染后续所有判断。

**系统性问题：文档把"目标态"当"现状"写。** 同一能力在四处有四种说法。已确认的例子：

| 错误陈述 | 实际情况 |
|---|---|
| benchmark §1.5：代码沙箱是 Roslyn `CSharpScript` | 实为 **Jint 4.4.1**（契约 §8 + `Directory.Packages.props`） |
| 编排契约 §15：Switch「校验已放行」 | 实为**发布期拒绝** |
| 数据集合 / 报表契约：`batchupdate` 不可用 | 已在 `91d75bd` 修复 |
| 契约称 `NodeExecution.asyncJobId` | **模型中不存在** |
| `src/CLAUDE.md`：分析器已启用 | 未启用 |

**计数类断言系统性错误**：测试用例 23→62、docs 行数 4,600→6,104、网关路由 11→13、代码行数口径（`Measure-Object -Line` 跳过空行少算约 14%）。

**行号大面积失效**：契约 §14.0 已按 `91d75bd` 核对，但 §2–§13 仍对应 `0c4f7eb`。

**未落地的机制**：可运行 quickstart、DSL JSON Schema 入 CI、变更日志与迁移指南、计数类断言脚本化。DSL 已经历 `expression/when` → `combine/isDefault` → `loopBody/branches` **三次演进，无任何版本记录**——对一个让用户配置 JSON 的引擎，这是会直接造成生产事故的缺失。

**一条必须强制执行的方法论纪律**：上一轮报告曾得出"全链无重试"的错误结论，因为策略注册在共享工程 `Meta.Dow.ServiceDefaults`，只在 `saas` 内搜关键词导致漏判。因此写入了纪律：

> **凡"某能力不存在"的结论，必须跨工程搜索后再下。**

按当前标准，benchmark 里那些"0 命中"结论的可信度并不高，重排规划时应重新核验。

## 七、结论

1. **设计是对的，兑现没跟上。** 骨架、单调度器、出参可见性、图级校验、多租户谓词都做对了，不要在补节点的方向上浪费精力。
2. **先护栏，后节点。** A 轨道 12 项做完才谈对外；B 轨道可以让位。两者混排是当前规划表最大的问题。
3. **护栏与门禁并行。** 测试覆盖为零意味着补完护栏也无法验证是否真的修好了。C 轨道不能推后。
4. **补持久化执行。** n8n 的续跑与 Temporal 的重放是最该抄的一块；`Paused` + `ResumeFrom(nodeId)` 是低成本起点。但先决条件是 A10 幂等键运行时落地。
5. **把边界写进契约。** "单次 burst、短流程、可容忍重跑、不支持跨重启续跑"必须显式声明，并进发布期校验。当前这条边界只存在于评审文档的自觉里，是最大的期望管理风险。
6. **编排层是所有低代码能力的公共放大器。** 修编排的收益远大于修单个业务模块。

## 八、整改实施记录

§一至§七 是整改**前**的评估快照。本节记录实际落地的内容、做法上的关键决策，以及**没做的部分为什么没做**。

### 8.1 已落地（轨道 A 的 7 项 + 控制流语义）

| 编号 | 事项 | 落点 |
|---|---|---|
| A1 | 未知 DSL 字段发布报错 | `DslSchemaValidator.ValidateJson` |
| A3 | 异步作业持久化、失败回写 | `FlowAsyncJob` / `FlowAsyncJobDispatcher` / `FlowAsyncJobWorker` |
| A4 | Wait 节点 | `ExecuteWaitAsync` |
| A5 | 停用 / 删除失效缓存 + 跨副本广播 | `FlowDefinitionInvalidationBroadcaster` / `PublishedFlowCache` / `DisableAsync` / `EnableAsync` |
| A7 | Http 出站 SSRF 防护 | `HttpEgressGuard` |
| A8 | SQL 写判定覆盖 `WITH` 与 `db.query` | `SqlWriteClassifier` / `CodeSandboxExecutor.Query` |
| — | 条件算子补齐与控制流语义 | `EvaluateConditionItem` / `SupportedConditionOps` / `ValidateConditionOp` |

轨道 A 的A2、A6、A9、A10、A11、A12 未做。其中 **A9（数据范围接线）与 A11（授权下沉）属治理决策**（接线还是从产品口径移除），**A2、A10 是A11/A12 的前置**，需按依赖顺序排期。

### 8.2 实施中三个非显然的决策

**一、可测试性本身就是缺陷修复的前提，不是事后附加项。**

`EnsureSqlWrite`（写闸门）与出站 URL 校验原先都"存在但无法测试"：前者内联在依赖四个外部执行器的 `CodeSandboxExecutor` 里，测试脚手架一构造就 `null!` NRE。这意味着一条安全边界从未被验证过。

因此修复的第一步不是写判定逻辑，而是**把判定从依赖图里摘出来**——`SqlWriteClassifier` 与 `HttpEgressGuard` 都拆成无依赖的纯静态类。判定逻辑本身并不复杂，但它第一次变得可以被断言。§5.1 说的"测试覆盖实质为零"，在这一层是字面意义上的零。

**二、跨副本失效用版本号而非共享缓存，是有意识的取舍。**

最直觉的做法是把已发布 DSL 放进 `IDistributedCache`，让各副本直接读。代价有二：DSL 反序列化的跨版本格式兼容问题，以及热路径每请求走网络。

改成「共享一个单调递增的**失效代数**，本地快照记住自己读到的代数」后，失效只需在 Redis 上一次读写；各副本读本地快照前先比对代数。**代价是最终一致**——停用/删除在其他副本上于下一次读取时生效，而非即时。若业务要求零窗口，仍需在调用入口二次校验状态。这一取舍已写入总文档的「已知的实现取舍」表，避免下次重新评估。

**三、失败与超时必须是数据，不能是异常。**

原实现的 fire-and-forget 有两个致命属性：失败只 `LogWarning`（于是"实例成功"与"外呼失败"并存），以及结果无处可取。修复时把作业做成持久化实体，状态、失败原因、重试次数全部落库。

Wait 节点进而要求：**超时是一种结果，不是一种异常**。为此专门引入了 `FlowAsyncJobTimeoutException` 而非复用 `UserFriendlyException`——上层必须能区分「作业失败了」与「等不到结果」，靠字符串匹配来分辨是不可维护的。同理，`WaitFailOnTimeout` 默认 `true`：等不到结果却继续往下走，会把不确定性带进后续节点。

### 8.3 顺带收紧的「配置被接受但不生效」

除A1 的通用机制外，三处具体语义在发布期被拒，因为它们都曾静默走错分支：

- `condition.strategy` 拼错曾静默退化成「最后一条命中生效」，与命名**完全相反**。现加白名单并明确 `firstMatch` / `allMatch` 语义。
- `entry` 只配 `combine` 而 `items` 为空时，运行期直接 `return true`（恒真）。
- 出边用了 `combine` 但节点没有 `items` 时，掉进legacy 分支把 `combine` 完全忽略并静默走第一条边。

### 8.4 测试

后端单测从 **91 条增至 297 条**，全部通过。新增覆盖：

- `SecurityGateTests` —— SQL 读写判定（含 `WITH` 绕过、注释与字面量藏关键字、IPv4-mapped IPv6、云元数据）与出站协议判定。
- `DslSchemaAndAsyncJobTests` —— 未知/未实现字段、拼写建议、作业状态机的全部流转与终态不可回退。
- `WaitNodeAndConditionOpTests` —— Wait 的四类发布期校验、23 个条件算子白名单、`strategy` 取值。
- `InvalidationBroadcastTests` —— 代数单调递增、跨实例共享，以及**故障降级方向**（Redis 挂了必须退化为进程内语义，不能让发布/停用/删除全部失败）。
- `PublishValidationCoverageTests` —— **校验的覆盖范围**（第二轮新增，见 8.6）。
- `OutputVisibilityTests` —— 权限名收集范围与可见性判定（第二轮新增）。

最后一类最值得说明：它的正确性不体现在"功能能不能跑"，而体现在**故障时倒向哪边**。这类性质最容易在重构中被写反，且写反了不会编译失败、也不会在正常环境下暴露。

### 8.5 仍未解决的结构性问题

整改没有触及§4.3 的结构性天花板：

- **状态仍常驻内存**，一次请求内最多 200 步。长流程仍会随进程重启丢失。
- **仍无持久化执行**。`FlowAsyncJob` 让异步外呼可追溯，但流程本身的中断续跑未做——这是轨道 A10 的前置，也是适用边界无法放宽的根本原因。
- **步骤预算与适用边界仍未进发布期校验**。§七 结论 5 依然成立：这条边界只存在于文档自觉中，是当前最大的期望管理风险。

### 8.6 第二轮：按文档回读代码，暴露的「只查根级」失效模式

第一轮结束后，本轮**反向操作**——不是"按代码核对文档"，而是**按文档逐条回读代码**，专门找"文档声称已修、但实际只修了一半"的地方。结果 5 处，**其中 1 处是第一轮自己引入的回归**。

#### 8.6.1 一个值得单独记住的失效模式

> **校验逻辑本身写对了，只遍历了"最显眼的那一层"，于是嵌套分支与 End 节点成为后门。**

5 处修复中有 4 处命中这个模式。它的隐蔽性在于：代码里有校验、测试也绿、文档也写着"已修"——但三者测的是同一条根级路径，于是"覆盖范围"从未被任何人验证。

这直接推翻了 8.4 隐含的前提：**"加了校验 + 加了测试"不等于"校验有效"**。若不刻意区分这两件事，重构会以完全相同的方式再犯一次。因此第二轮的两个新测试**故意针对覆盖范围而非具体取值**——断言"End 节点里的 conflict 也会被拒"、"第二层嵌套的 `visibleTo` 也会被拒"，而不是"某个具体错误文案是什么"。

#### 8.6.2 逐条修正

| 问题 | 性质 | 修法 |
|---|---|---|
| `payload.item` 被误列入「未实现字段」黑名单，该形态**根本无法发布**——而提示语恰好写着"仅支持 raw 与 payload.item" | ❗**第一轮引入的回归** | 移除黑名单条目；改由 `ValidateSemantics` 按**取值**接管 |
| `payloadMode=array` 无代码分支，配了静默退化为整包 `inputs` | 静默失效 | 发布期取值白名单（`object` / `raw`） |
| `aggregate`+`map` 冲突、`wrap` 取值只遍历根级 `outputs` | 覆盖不全 | 遍历范围扩到 `End.finalOutputs`（契约条目 21 转 ✅） |
| 嵌套 `visibleTo`（`map.item[]` / `group.header[]` / `group.children`）未经校验 | 覆盖不全 | 新增递归校验 |
| `group.header[].visibleTo.permissions` 不被收集，权限模式下分组表头对所有人消失 | 覆盖不全 | 补全 `CollectPermissionNames` 范围（条目 5 转 ✅） |

#### 8.6.3 那个回归的教训

`payload` 被放进「模型有但无消费点」黑名单时，依据是"文档里没写清楚它怎么用"——**而不是"grep 不到消费点"**。文档没写全和代码没实现是两件事，而黑名单是按前者做出的判断。

这与 §6 批评的"文档与代码漂移"是同一个根因的另一面：文档既可能被**高估**（写了没实现），也可能被**低估**（实现了没写全）。治理手段只能防后者的一半，前者必须靠"改黑名单前先读消费点"这条纪律来防。

**顺带产出的一条通用结论**：`DslSchemaValidator` 的反射白名单只能回答"字段名存不存在"，回答不了"这个**取值**有没有消费点"。因此新增 `ValidateSemantics` 承担取值约束——两类校验分工明确，不用互相冒充。

### 8.7 第二轮新发现的残留缺陷（文档此前完全没有记录）

- **`FlowAsyncJobWorker` 多副本重复消费**：`GetQueuedJobsAsync` 无 claim/乐观锁语义，`MarkRunning` 也只有"终态不可回退"而**没有 CAS 条件**。两个副本可同时取到同一条 `Queued` 作业并各自外呼一次。根因与 §4.3 的"状态常驻内存"同源——持久化作业做了一半（存了，但没做互斥）。
  单副本部署无此问题，因此优先级低于A10，但**一旦横向扩容就会静默重复调用外部系统**，属于必须在部署文档中显式声明的约束。
- **`AttemptCount` 是只增不减的死字段**：`MarkRunning` 自增，但失败后无任何路径把它退回 `Queued` 重排队。故它反映的是"被 worker 捞起过几次"，而非"重试了几次"——字段名承诺的比实际提供的多。

### 8.8 前端影响（前端在 `D:\Project\vue-demo`，不在本仓）

整改**不是纯后端改动**。前端设计器有一批**双向硬编码**的清单（节点类型、条件算子、流程操作），必须跟着改，否则新能力在 UI 上不可达。

**四项必须改**

| 项 | 前端落点 | 说明 |
|---|---|---|
| **接入 Wait 节点** | `flow-meta.ts` 的 `FlowExecutableKind` / `EXECUTABLE_NODE_KINDS` / `NODE_PALETTE`；`designer.vue` 的 `paletteGroups`；`dsl-graph.ts` 的 `DslNode` 接口 / `defaultNodeData` / `dslToGraph` / `graphToDsl` / `nodeCaption`；`designer.vue` 的 `selected` 初始化 / `applySelection` / `syncSelectionToCanvas` + 属性面板 | 注意 `paletteGroups` 是**另一份**白名单过滤：只加 `NODE_PALETTE` 左栏仍不显示 |
| **条件算子补7 个** | `condition-items-editor.vue` 的 `OP_OPTIONS`（现仅 10 项） | ⚠️ 必须同改3 处配套，否则 `between` 产出非法 DSL：`ConditionItem.right` 类型放宽以容纳数组、`needsRight()` 对 `isNull`/`isNotEmpty` 返回 false、`between` 提供两个输入并组装成 `[min, max]` |
| **停用 / 启用按钮** | `api/saas/orchestration.ts` 补两个函数；`definitions/index.vue` 操作列 | 状态渲染层**已就绪**（`definitionStatusMeta` 含 `2: 已禁用`、筛选项、列表 Tag 都已存在），只缺 API 与按钮 |
| **出边 `combine` 无 `items` 的本地兜底** | `graphToDsl` 的 Condition 分支 | ⚠️ 会**真实触发**：用户「一键编号」后清空条件列表即产出后端拒绝的 DSL。前端规则文件本就要求「提交时兜底校验」，目前完全缺失 |

**建议改**

- 异步 Http 的提示文案现写「下游不可引用本节点出参（需 Wait，规划中）」——**既过时又语义相反**，现在可通过 Wait 消费。
- Condition 面板补`strategy` 下拉：前端目前恒为 `firstMatch`（`designer.vue` 中硬编码），后端新增的 `allMatch` 语义**对前端不可达**。
- Http 面板提示内网地址会被出站闸门拒绝，减少反复排查。
- 规则文件 `.cursor/rules/logic-orchestration.mdc` 的现状段已在 2026-10-15 改为：`wait` 已实现，`sql` / `switch` 仍发布期拒绝。§8.8 写下时该文件仍把三者都当成未实现。

**不需要改**

- 出站白名单 / 重定向策略是纯服务端出站策略，不影响 DSL 契约。
- 未知字段校验：前端 `graphToDsl` 是白名单式构造，产不出未知字段（除非手工导入 DSL JSON）。
- `entry` 空 `items` 校验：前端 `graphToDsl` 从不写 `entry`、`dslToGraph` 从不读 `entry`，该校验对前端永不触发。

**一处易踩的坑**：`shared/filter-model.ts` 里也有 `between` / `in` / `startswith`，但它服务的是**资源与报表筛选器**（右值为 `{min,max}`，走 `FilterSqlBuilder`），与编排 Condition 的 DSL 语义是两套东西，**不可混用、不可"顺手对齐"**。

**一条更根本的问题**：前端节点清单与后端 `SupportedNodeKinds` 是**双向手工对齐**，无任何机制保证同步。后端也没有暴露「支持哪些节点」的端点。这正是前端规则文件第 4 条「新增节点类型必须服务端先行」想防的情况，但目前靠人守。若后续还要继续补轨道 B 的节点（Switch / Sql / MqttPublish），建议先加一个 `GET .../node-kinds` 端点，让前端清单由服务端驱动——否则每加一个节点就要改两处，且没有任何东西会在漂移时报错。

### 8.9 控制流编写方式（2026-10-15，代码 `73aec3d` / 前端 `6f88348`）

§8.8 的四项在这一天复核后仍然成立：设计器没有 Wait、条件算子仍是 10 个、列表没有停用/启用、`graphToDsl` 仍会在没有 `items` 时写出 `combine`。

同一次复核里，文档此前没写、代码已经在跑的有：

- ForEach 一项的编辑面是脚本。`cf-line-check` 的循环体是 Code，返回对象经 `promote` 成为 `items[i]`。
- Parallel 一条分支的编辑面是入参、出参和 `subFlowKey`。`FlowDslBranchDsl.Outputs` 由 `ProjectBranchOutputs` 投影。内联 `body` 仍可执行，且在与 `subFlowKey` 并存时优先。
- `cf-` 演示可以更新和发布。删除、停用、启用仍锁全部 `IsSystem`。资源系统流保持锁定。种子在下一次 DbMigrator 之后写成 `isSystem: false`。
- 上面四项前端缺口没有改成已完成。轨道 A 的 A2、A6、A9、A10、A11、A12 也没有。
