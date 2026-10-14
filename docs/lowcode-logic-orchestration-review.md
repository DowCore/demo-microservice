# 低代码逻辑编排 —— 完善意见稿

> **文档性质**：整改意见稿（Review & Remediation Proposal）。用于评审、排期与验收，不替代设计规格。
> **代码基线**：`Meta.Dow.slnx` @ commit `0c4f7eb`（2026-10-14，与 `267d375` 之间无代码改动，行号一致有效）。
> **实测基线**：`dotnet build Meta.Dow.slnx -c Release`（全量重建）= **0 错误 / 165 警告**（267d375 记 162，差值为 NU 审计类随漏洞库波动）；`dotnet test tests/Meta.Dow.Orchestration.Tests` = **62/62 通过**。
> **配套阅读**：[实现契约](./lowcode-logic-orchestration.md)（真实行为）、[落地方案](./lowcode-logic-orchestration-remediation-plan.md)（R1–R12）、[主流对标分析](./lowcode-logic-orchestration-benchmark.md)（优势/不足与主流差异，全部不足项对照本文 S/C/E/G 编号）。
> **评估范围**：`Meta.Dow.SaaS` 逻辑编排引擎（`/api/orchestration/*`、`/api/logic/*`），及其连带的**授权落点、租户隔离、发布治理与工程化门禁**——后者直接决定编排能力能否上生产。
> **证据规则**：每条结论附 `相对路径:行号`，均可在上述 commit 复核。状态图例：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现 ｜ 🐞 缺陷。
> **边界声明**：本文**不把规划当现状**。规划项见第 4.4 节；未能核验的内容见附录 B。

---

## 1. 执行摘要

### 1.1 总体判断

逻辑编排引擎的**骨架是可信的**：DSL 强类型、节点 I/O 显式、主循环**失败关闭**、Code 沙箱真加固、SQL 层全参数化且租户谓词次序正确。这些不是"文档写得好"，而是能在代码里逐行验证的设计资产。

但**运行保障层与成熟方案存在结构性差距**，且差距集中在最容易出生产事故的地方：

1. **执行模型是内存态**（无持久化、无重放），异步节点是"发了不管"；
2. **事务边界缺失**——跨节点写库无原子性，且 `txMode` 字段被**静默忽略**，使用方会误以为已开启；
3. **授权落在调用点而非数据访问层**，存在可绕过的旁路面；
4. **字段可见性 fail-open 与 fail-closed 混用**，最坏情况下（未配最终契约）会把内部变量整包输出；
5. **DSL 校验强度不足**，存在"发布成功、调用即 500"的失败模式。

### 1.2 结论

**当前引擎适合"单次 burst、短流程、可容忍重跑"的场景；不适合长流程、跨重启一致性、强授权约束的场景。** 这个适用边界目前没有被文档或代码显式声明，是最大的期望管理风险。

### 1.3 最该做的三件事（详见第 6 节）

| 优先级 | 事项 | 为什么是它 |
|---|---|---|
| **P0** | **让失败关闭真正闭环**：DSL 校验补类型白名单（`Switch`/`Sql` 改为发布期拒绝）、无最终契约时报错而非全量输出、`visibleTo` 未知 mode 报错、子图不再旁路角色过滤 | 改动小、收益大，直接消灭"发布成功即 500"与"静默数据外泄"两类最痛的失败模式 |
| **P0** | **安全默认值**：默认口令、表列 `Origin`、破坏性 DDL 门禁、`PruneObsoleteClients` 默认值 | 都是"一个配置/一个判断"级别的改动，但都是可被直接利用的缺口 |
| **P1** | **补齐事务与持久化语义**：实现 `txMode=sameDataSource`、未知 DSL 字段改为报错、异步节点落 JobId 与节点状态 | 决定引擎能否承载真实业务；设计已在文档中写全，只差实现 |

### 1.4 评分

| 维度 | 评分 | 依据 |
|---|---|---|
| 编排引擎骨架（DSL / I/O / 校验结构） | **7.5 / 10** | 显式节点 I/O 与失败关闭主循环优于多数同类实现；扣分在校验强度与表达式表达力 |
| 控制流完备性 | **6.0 / 10** | ForEach/Parallel/SubFlow 已落地；缺 Switch、Wait、独立 Sql、`txMode`，Condition 算子缺 3 个 |
| 运行保障（超时/重试/异步/持久化） | **3.5 / 10** | 异步结果与失败全部丢失、无持久化执行、重试分类在 PG/Oracle 上失效 |
| 授权与字段可见性 | **4.5 / 10** | 设计不错但存在旁路面与 fail-open 兜底 |
| 多租户隔离 | **6.5 / 10** | 租户谓词次序正确、缓存键含租户；扣分在缺列静默失效与 `sys.resource.query` 旁路 |
| 可验证性 / 测试 | **3.0 / 10** | 62 个用例全为纯单测且 39 个集中在控制流，脚手架 `null!` 注入使运行时路径零覆盖 |
| 文档与知识治理 | **7.0 / 10** | 重构后已分离"事实契约 / 行为边界 / 规划"；仍缺 schema、变更日志、错误码表 |

**综合：约 5.4 / 10** —— 骨架可用的早期实现，尚未达到生产就绪。

---

## 2. 评估方法与基线

### 2.1 方法

1. **读约束**：`.cursor/rules/frontend-vue-vben.mdc`、`.workbuddy/memory/*`、`src/CLAUDE.md`。
2. **读文档**：`docs/` 9 篇（重构前 6,882 行 / 重构后 6,104 行）+ `README.md`。
3. **读代码**：263 个 `.cs` 文件 / **36,886 行**（不含 `bin`/`obj`；口径为**文件总行数**，含空行与注释——见 §2.2 的计量说明）；重点通读 `FlowExecutor.cs`(2,962)、`FlowDslModels.cs`(528)、`FlowContextResolver.cs`(732)、`FinalOutputAssembler.cs`(926)、`CodeSandboxExecutor.cs`(472)、`ResourceCrudExecutor.cs`(1251)、`FilterSqlBuilder.cs`(537) 等。
4. **实测**：Release 构建与单测各执行一次，取真实退出码与计数。
5. **交叉验证**：关键结论由多路独立核对互证；本文引用的每条行号均经复核。

### 2.2 代码量分布（说明"SaaS 有多重"）

> **计量说明**：口径为**文件总行数**（含空行与注释），统计时排除 `bin`/`obj`/`node_modules`。早期版本曾用 PowerShell `Measure-Object -Line` 统计，该 cmdlet **跳过空行**，会把同一份代码少算约 14%（如 SaaS 28,756 → 24,743），据此得出的"77.8%"应为 **78.0%**。本表数字已按文件总行数重算，可直接复核。

| 区域 | 文件 | 总行数 | 占比 |
|---|---:|---:|---:|
| `src/services/saas`（其中 `Orchestration/` 98 文件 / 27,768 行） | 127 | **28,756** | **78.0%** |
| `src/services/administration` | 45 | 2,610 | 7.1% |
| `tests` | 5 | 1,575 | 4.3% |
| `src/shared` | 21 | 1,393 | 3.8% |
| `src/services/identity` | 29 | 1,183 | 3.2% |
| `src/apps` | 6 | 633 | 1.7% |
| `src/services/projects` | 28 | 554 | 1.5% |
| `src/gateway` | 2 | 182 | 0.5% |
| **合计** | **263** | **36,886** | 100% |

其中 `src/**/*.cs` 单独计为 258 文件 / 35,311 行（其余为 `tests/`）。

---

## 3. 总体评价

### 3.1 值得保留的设计资产（对标时要守住，不要推倒重来）

| 资产 | 证据 | 为什么值得保留 |
|---|---|---|
| **主循环失败关闭** | 无出边 / 无匹配分支 / 有环 / 超 200 步一律报错（`.../FlowExecutor.cs:335-346`、`:313-317`、`:351`） | 大量同类引擎在此处 fail-open；本项目 OA 引擎仍是反例（连线指向不存在节点会静默 `Complete("approved")`） |
| **单一调度原语** | `RunSegmentAsync` 被主链 / ForEach 循环体 / Parallel 分支三方复用（`:272-353`） | 从结构上避免"第二套调度器"，是控制流可维护的前提 |
| **节点 I/O 显式化** | `inputs[]`/`outputs[]`/`resultRoot`/`map.item`/`to`/短名与 `ref`（`.../FlowContextResolver.cs:402-439`），另有列表路径糖 `x.count`/`x.sum.amount`（`:166-215`） | 比"组件内部读上下文"的隐式模型更可校验、可文档化 |
| **Code 沙箱加固** | Jint 8s / 20,000 语句 / 递归 64 / `fetch`·`XMLHttpRequest`·`eval`·`Function` 置 `undefined` / **未开 `AllowClr`**（`.../CodeSandboxExecutor.cs:74-86`、`.../OrchestrationConsts.cs:20-24`） | 脚本碰不到 CLR、文件与网络；多租户下的正确取舍 |
| **SQL 防线与次序** | 方言抽象 + 标识符白名单 + 值全参数化；**租户谓词与用户条件树是并列 predicate**，最终统一 `AND`（`.../FilterSqlBuilder.cs:54-65`、`:152`） | 用户写的 `OR` 逃不出租户边界，这是结构保证而非约定 |
| **写重试的幂等门禁** | `retryOnWrite` 必须同时提供幂等键，否则**拒绝发布**（`.../FlowExecutor.cs:2788-2806`） | 把"纪律落进代码而不是文档"，立场与 Temporal/n8n 一致（`src/shared/Meta.Dow.ServiceDefaults/.../Extensions.cs:37-40` 注释） |
| **密钥治理 + 护栏** | 明文密钥外置到 git-ignored `appsettings.secrets.json`，`SecretsConfigSafetyTests` 强制禁止 secrets 文件出现 JSON 数组 | 由真实事故（ID2052）驱动，且护栏会真失败 |

### 3.2 与成熟方案的差异定位

| # | 维度 | 成熟方案通行做法 | 本项目现状（证据） | 差异性质 |
|---|---|---|---|---|
| 1 | 多路分支 | Switch / 排他网关是**一等公民节点** | ❌ 无 `Switch` dispatch（`.../FlowExecutor.cs:791-792`），却在校验层被列为允许多出边的类型（`:2599-2603`） | **结构性** |
| 2 | 表达式语言 | LiteFlow EL；宜搭/微搭公式；SpEL/QLExpress 级 | 自写正则分词布尔求值，`combine` 仅支持**编号 + and/or/not/()**；算子缺 `in`/`notIn`/`between`（`:2297-2310` 直接抛错） | **结构性** |
| 3 | 循环语义 | `for`/`while`/`break`/`continue` + 明确作用域 | 只有 `ForEach(itemsPath)`（`:1597-1720`）；无 range-for、无 while、无 break/continue；🐞 `failFast` 在**整窗跑完后**才抛，不取消同窗其余迭代 | 能力缺口 + 语义不严 |
| 4 | 并发与汇合 | 并行/包含网关 + join；`WhenAny` 含**取消输家** | `parallel/when` 有 `joinMode=all/any/allSettled` ✅，但 🐞 `any` 不取消其余分支（`:1781-1796`） | 语义缺陷 |
| 5 | 异步与等待 | 有 Wait 节点；异步结果是**一等产物** | ❌ 无 `Wait`；异步 Http 是 `Task.Run(..., CancellationToken.None)`（`:911-942`），不写 outputs、不落 JobId、失败仅日志、节点状态停留 `AsyncQueued`、实例仍判 Succeeded | **结构性** |
| 6 | 持久化执行 | 状态持久化 + 确定性重放 + 活动级重试策略 | 内存态 `RunSegmentAsync`；实例**事后**落库；进程崩溃即丢；无重放 | **结构性** |
| 7 | 事务与一致性 | DataSource 级 session/UnitOfWork，或 Saga 补偿、或 Outbox | ❌ 无 `txMode`（DSL 无该字段且**未知字段静默忽略**）；`db.execute` 每次新建连接自动提交，仅 `db.batch` 有事务（`.../ParameterizedSqlExecutor.cs:153-178`） | **结构性** |
| 8 | 重试策略 | 活动级可配重试/退避/上限；区分幂等 | 读重试 2 次 ✅、写默认不重试 + 幂等键门禁 ✅（设计正确）；但 🐞 PG `40P01`/`08xxx` 与 Oracle 全部错误码**实际不重试**（`.../NodeResiliencePolicy.cs:173`、`:175`、`:182-190`） | 设计对、实现对 |
| 9 | 节点 I/O | item 传递 / data association / 组件内部上下文 | **显式 I/O + 路径糖**（`.../FlowContextResolver.cs:166-215`、`:402-439`） | ✅ **持平或领先** |
| 10 | DSL 设计期校验 | XSD / JSON Schema；非法图在设计期被拒 | 强类型模型 + `ValidateDsl` ✅，但 ❌ 无类型白名单、❌ 无环检测（运行期才查 `:313-317`）、❌ 无 Warning 通道、`version` 不校验 | **结构性** |
| 11 | 最小可见 | 默认拒绝，服务端强制 | 设计不错（`visibleTo` + `promote`/`group`），但 🐞 未知 mode **兜底放行**（`.../OutputVisibilityFilter.cs:95`）、🐞 子图整体旁路（`:1518-1527`、`:1943-1952`）、🐞 无最终契约时 `data` = 全部 `ctx.Vars` 且零过滤（`:446-453`） | **安全语义相反** |
| 12 | 授权落点 | 数据访问层强制 | 授权在应用服务/调用点：`/api/logic/{flowKey}` 仅需 `Instances.Run`（`.../LogicApiAppService.cs:35`）且 `skipValidation:true`（`:61`） | **结构性** |
| 13 | 发布治理 | 版本化部署 + 在途实例锁版本 + 事件失效 | 实例记 `version` ✅，但 🐞 缓存为**进程内** `IMemoryCache`（`.../PublishedFlowCache.cs:38`、`:104-112`）、失效**只挂发布**（`.../FlowDefinitionAppService.cs:290` 唯一调用点）、`Disable`/`Delete` 不失效 | 结构性 |
| 14 | 触发层多副本安全 | 统一 registry + 争抢安全 | Rabbit/Kafka/MQTT/Cron 齐备 ✅，但 🐞 仅 `FlowScheduleHostedService` 用分布式锁；MQTT client id 含随机 Guid（`.../MqttMessageTriggerHostedService.cs:225`）⇒ 多副本**必然重复触发** | 实现缺口 |
| 15 | 可观测/回放 | 节点级输入输出快照 + 重放 + 重试可视化 | 实例与节点记录 ✅，但异步无结果、子图节点明细丢弃（`:1926`、`:1953`）、无重放；文档曾承诺的 `NodeExecution.asyncJobId` 在模型中**不存在** | 能力缺口 |

### 3.3 五处结构性差异（补节点追不上）

1. **执行模型：内存态 → 需持久化可重放。** 成熟引擎把流程状态当持久化一等对象，靠确定性重放恢复。我们是单次 burst + 事后落库。**后果**：长流程与跨重启一致性不该由本引擎承接。**该边界必须显式声明。**
2. **事务边界缺失 → session / 补偿 / Outbox 三选一。** 项目已在 `.../FlowDslModels.cs:225-231` 用"幂等键门禁"证明了它有能力把纪律落进代码，但**同一种纪律没有用在事务上**：`txMode` 被静默忽略，使用方以为有原子性。
3. **表达式语言退化 → 需受限但真正的求值器。** 编号布尔组合的优点是安全（无 eval），代价是表达力天花板极低（`type in [...]`、日期比较、字符串函数都做不到）。升级路径应是**加白名单函数的受限表达式语言**，而不是引入 eval。
4. **授权落点 → 需下沉到数据访问层。** 现在 `Instances.Run` 一个权限即可调用全部已发布流程并绕过报表/资源级授权。值得注意的是**防线已存在但未接线**：`FilterSqlBuilder` 的租户谓词次序正确，而 `DataPermissionQueryableExtensions` 全仓**零调用点**。
5. **发布治理 → 需跨进程失效。** 进程内缓存 + 只在发布时失效，在多副本下等于没有版本治理；`Disable`/`Delete` 不失效意味着**已下线流程仍可被调用**。

---

## 4. 缺陷清单

> 编号规则：`S`=安全与租户隔离，`C`=正确性与一致性，`E`=编排引擎能力与语义，`G`=工程化。严重度：**严重 / 高 / 中 / 低**。

### 4.1 安全与租户隔离（S）

| 编号 | 严重度 | 现象 | 机理 | 证据 | 后果 / 建议 |
|---|---|---|---|---|---|
| S1 | **严重** | 表列 `Origin` 客户端可控 → 跨租户改写 | `Origin` 来自客户端 DTO；`UpdateAsync` 对 `Origin != Convention` 的列**一律可 SET**，而强制覆盖 `TenantId` 的逻辑**只在 Create 里** | `.../TableDefinitionAppService.cs:198`、`.../ResourceCrudExecutor.cs:415`、`:372-378` | 持 `Orchestration.Tables.Update` 者把自己的行搬进受害租户命名空间并篡改审计列。**建议**：约定列（`TenantId`/`Id`/`IsDeleted`/审计列）一律服务端强制，禁止客户端声明其 `Origin` |
| S2 | **严重** | 破坏性 DDL 门禁缺失 | 只有 `dropTable`/`truncate` 被拦；`DROP COLUMN`/`RENAME`/破坏性 `ALTER` 直接执行；`Destructive` 标记只回传前端 | `.../SqlDdlExecutor.cs:254-257`、`:242-291` | 一次误操作即不可逆。**建议**：`Destructive` 项默认禁止，需显式开关 + 二次确认 + 审计 |
| S3 | **严重** | 默认管理员口令是公开值 | `1q2w3E*` 既在启动说明里，又作为新租户回退口令硬编码；同文件还有模板残留邮箱与 `NotImplementedException` 的错误路径 | `.../TenantCreatedEventHandler.cs:33`、`:35`、`:47-50`、`README.md` | 每个新租户管理员可被直接登录。**建议**：首次随机口令 + 强制改密；修掉坏的错误处理路径 |
| S4 | **高** | 只读权限可被 `WITH` 前缀绕过 | `EnsureSqlWrite` 把 `select` **和 `with`** 都当读；`db.query` 完全不检查写权限 | `.../CodeSandboxExecutor.cs:402-413`、`:239-245` | `WITH x AS (…) DELETE FROM t` 无需 `Sql.Write`。**建议**：统一写判定（首个 DML 关键字 + 禁止 DML 出现在 `db.query`） |
| S5 | **高** | Http 节点无 SSRF 防护 | url 由节点配置 + 模板拼出，headers 任意可设；全仓无 host 白名单/私网/回环拦截 | `.../FlowExecutor.cs:956`、`:1011-1053` | 任意流程作者可驱动 SaaS 主机访问内网与云元数据端点。**建议**：出站 host 白名单 + 私网/链路本地拦截 |
| S6 | **高** | 字段可见性在子图被整体旁路 | SubFlow 与 ForEach/Parallel 体以 `filterOutputsByRole:false, currentUser:null` 执行 ⇒ `bypass=true` | `.../FlowExecutor.cs:1518-1527`、`:1943-1952`、`:455` | 子图 `finalOutputs.visibleTo` 失效，受限字段冒泡到父输出。**建议**：把调用方角色快照透传进子图 |
| S7 | **高** | 无最终契约时输出内部变量 | 无 `finalOutputs`/`outputs` 时 `data` = 整个 `ctx.Vars`、`visibleFields` = 全部键、**零过滤**，且 `omittedFieldCount` 恒 0 | `.../FlowExecutor.cs:446-453` | 中间值整包外泄且计数掩盖。**建议**：无契约即报错（呼应"契约优先"原则），至少也要走 `visibleTo` |
| S8 | **高** | 报表数据范围可整体绕过 | `/api/logic/{flowKey}` 仅需 `Instances.Run` 且 `skipValidation:true`；`sys.resource.query` 可自带 `resourceCode/filter/summaryFields` | `.../LogicApiAppService.cs:35`、`:61` | 跳过 `Reports.Execute` 与 DataScope。**注**：跨租户仍被独立顶层谓词挡住，被绕的是**报表/资源级授权** |
| S9 | **高** | IAM 行级数据范围零落地 | `DataPermissionQueryableExtensions` 全仓**无调用点**；`DataScopeResolver` 只被"我的"接口消费 | `src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/DataPermissionQueryableExtensions.cs:10` | 数据范围目前是**配置界面**而非执行约束。**建议**：要么接线，要么从产品口径中移除，避免"看起来有" |
| S10 | **高** | 敏感信息进日志 | `IdentityModelEventSource.ShowPII = true` 无条件开启（**4 个进程**：3 个 Host + AuthServer；Projects Host 未开启） | `.../SaaSHttpApiHostModule.cs:74`、`.../AdministrationHttpApiHostModule.cs:96`、`.../IdentityServiceHttpApiHostModule.cs:101`、`.../MetaDowAuthServerModule.cs:138` | 令牌/claims/PII 写入日志与 Seq。**建议**：改为按环境开关 |
| S11 | **中** | 网关 12 条路由不挂授权策略 | 13 条路由中仅 `administration-fallback` 挂 `AuthorizationPolicy: Default` | `src/gateway/Meta.Dow.Gateway/appsettings.json:83-89`、`Program.cs:76-78` | 授权完全下沉，任一下游漏配即匿名可达。**建议**：业务路由显式挂策略 |
| S12 | **中** | `visibleTo` 未知 mode 兜底放行 | `IsVisible` 末尾 `return true`；`roleIds` 与**角色名**比较 | `.../OutputVisibilityFilter.cs:95`、`:81` | 拼错 mode 即变公开；`roleIds` 恒不匹配。**建议**：未知 mode 报错；`roleIds` 改与角色 Id 比较或删除 |
| S13 | **中** | `group.header[].visibleTo.permissions` 永不生效 | 判定侧会读它，收集侧 `CollectPermissionNames` **从不遍历 `group.header`** | `.../FinalOutputAssembler.cs:334` vs `:870-898` | 权限模式表头对所有人被丢弃。**建议**：补齐收集 |
| S14 | **中** | 无单条逻辑调用权限 | 只有 `Orchestration.Instances.Run` | `.../LogicApiAppService.cs:35` | 持该权限者可调用**全部**已发布流程。**建议**：补 `Orchestration.Logic.{flowKey}` |
| S15 | **中** | MQTT 触发器多副本重复执行 | client id 含随机 Guid，且无分布式锁 | `.../MqttMessageTriggerHostedService.cs:225` | 扩容即重复触发。**建议**：固定 client id + `IAbpDistributedLock` |
| S16 | **中** | 隐藏菜单可直达 | `IsVisible=false` 且**有**权限的菜单仍下发并置 `HideInMenu`；父目录补齐时也不复查 `IsVisible` | `.../MenuAppService.cs:145-148`、`:215`、`:161-176` | 隐藏 ≠ 不可达（页面内 API 仍受权限保护）。建议按功能权限收口而非只靠 `IsVisible` |
| S17 | **中** | `PruneObsoleteClients` 默认 `true` | 破坏性清理默认开启 | `src/shared/Meta.Dow.DbMigrator/appsettings.json:2` | 与 ID2052 事故同源的开关默认开。**建议**：默认 `false` |
| S18 | **低** | 健康端点匿名 | `/health`、`/alive` 未加 `RequireAuthorization`（生产仅回聚合状态） | `src/shared/Meta.Dow.ServiceDefaults/.../Extensions.cs:154-177` | 风险有限，建议内网限制 |
| S19 | **低** | 静态检查过松又过严 | 正则词法：`db["execute"](…)` 不匹配；`const sql="…"` 反被拒 | `.../CodeScriptStaticAnalyzer.cs:12-19`、`:44-53` | 建议改为基于语法的检查 |

### 4.2 正确性与一致性（C）

| 编号 | 严重度 | 现象 | 机理 | 证据 |
|---|---|---|---|---|
| C1 | **高** | 异步节点结果与失败**全部丢失** | `Task.Run(..., CancellationToken.None)`；不写 outputs/实例/JobId；失败仅 `LogWarning`；节点状态仍 `AsyncQueued`，实例随后判 Succeeded；异步分支不调 `CheckFailWhen` | `.../FlowExecutor.cs:911-942`、`:880`、`:723-731` |
| C2 | **高** | 发布成功但调用即 500 | 无节点 type/kind 白名单；`Switch` 被校验豁免却无 dispatch；`Logic+kind=Sql` 无 case | `.../FlowExecutor.cs:791-792`、`:2599-2603`、`ValidateDsl` 无白名单 |
| C3 | **高** | 已下线流程仍可被调用 | 缓存失效只挂 `PublishAsync`；`Disable`/`Delete` 不失效；进程内缓存跨副本不共享（TTL 10 分钟） | `.../PublishedFlowCache.cs:38`、`:104-112`；`.../FlowDefinitionAppService.cs:290` |
| C4 | **高** | 跨节点写库非原子 | 无 `txMode`；`db.execute` 每句自动提交；未知 DSL 字段被静默忽略 ⇒ 使用方以为已开启 | DSL 无该字段；`.../ParameterizedSqlExecutor.cs:153-178` |
| C5 | **高** | OA：驳回被静默当成通过 | `ShouldRejectNode` 对 any/ratio 会签恒 false，`!input.Pass` 分支不 return，落到 `task.Approve(...)` | `.../WorkflowRuntimeAppService.cs`（`ShouldRejectNode` 与 `CompleteAsync`） |
| C6 | **高** | OA：已发布流程可原地改写且不重校验 | `UpdateDraft` 不改 `Status`、不校验；仅 `PublishAsync` 校验 ⇒ PUT 删出线即可绕过结构校验，触发"无出线 → `Complete("approved")`" | `.../WorkflowDefinitionAppService.cs:108-127`、`:142-159`；`.../WorkflowRuntimeAppService.cs:443-446`、`:433` |
| C7 | **高** | OA：审批表单补丁静默丢失 | 改内存 `instance.RecordJson`，但 return 前无 `_instances.UpdateAsync`；Mongo 无变更跟踪 + `TransactionBehavior=Disabled` | `.../WorkflowRuntimeAppService.cs:156-163` vs `:303-320` |
| C8 | **高** | OA：并发无保护 | `WorkflowTask`/`WorkflowInstance` 无 `ConcurrencyStamp`；`AppendHistory` 是 JSON 读改写 | `.../WorkflowTask.cs:10`、`.../WorkflowInstance.cs:10`、`:76-85` |
| C9 | **中** | 条件算子缺失 | `in`/`notIn`/`between` 未实现 → 抛 `Unsupported condition op` | `.../FlowExecutor.cs:2297-2310` |
| C10 | **中** | `entry` 只配 `combine` 恒真 | `items` 为空直接 `return true`；发布校验也只在 `items.Count>0` 时查 `combine` | `.../FlowExecutor.cs:2264-2269`、`:2481-2484` |
| C11 | **中** | 条件节点可静默走错边 | 只有边 `combine`、无 `node.items` 时掉进 legacy 分支，`combine` 被忽略 | `.../FlowExecutor.cs:505-508` vs `:548-553` |
| C12 | **中** | `strategy` 语义与命名不符 | 非 `firstMatch` 时"最后一条命中生效"；取值不校验 | `.../FlowExecutor.cs:511-537` |
| C13 | **中** | PG/Oracle 重试分类失效 | `SqlState` 走 `int.TryParse`，`40P01`/`08xxx` 解析失败 → 0 → 不重试；Oracle 无错误码 | `.../NodeResiliencePolicy.cs:173`、`:175`、`:182-190` |
| C14 | **中** | 缺 `TenantId` 列时静默不加谓词 | 有列且无租户值时是 `TenantId IS NULL`；**无列时完全不加谓词**且无日志 | `.../FilterSqlBuilder.cs:54-65` |
| C15 | **中** | `aggregate` 与 `map` 同配时 map 静默失效 | `Aggregate` 分支先 return | `.../FinalOutputAssembler.cs:240-243` |
| C16 | **中** | `batchupdate` 恒 0 行 | 未把 `filter.Args` 合并进每条 item（delete 分支并了）→ 抛 Missing SQL parameter；新戳既当新值又当旧值比对 | `.../ResourceCrudExecutor.cs:645-672` vs `:689` |
| C17 | **中** | 条件组实际允许 4 层 | 常量 `MaxFilterGroupDepth=3`，判据是 `depth > 3`，根 depth=0 | `.../FilterSqlBuilder.cs:228`、`.../OrchestrationConsts.cs:14` |
| C18 | **中** | `summaryFields` 无白名单且覆盖设计器配置 | 客户端数组一旦非空即完全覆盖；随后只放行物理列；未知函数静默回落 `sum` | `.../ResourceCrudExecutor.cs:931-962`、`:964-972` |
| C19 | **中** | 非文本列的 `contains`/`startswith` 静默改 `eq` | 类型不是 String/Text/Enum 时改写算子 | `.../FilterSqlBuilder.cs:322-327` |
| C20 | **中** | `between` 是半开区间 | `>= min AND < max`，边界不含上界 | `.../FilterSqlBuilder.cs:363-375` |
| C21 | **中** | OA：发布校验用草稿、运行时用快照 | `EnsureFormRefAsync(publishedOnly:true)` 只校验**状态**；`CollectFormFields` 读草稿 `form.Schema.Fields`；运行时走 `GetEffectiveSchema()` | `.../WorkflowDefinitionAppService.cs:151`、`:154`、`:184-190`；`.../FormDefinition.cs:89` |
| C22 | **中** | 索引创建失败仅告警 | 建索引异常被 catch 后只 `LogWarning`，启动照常 | `.../SaaSMongoDbModule.cs:101-104` |
| C23 | **低** | 未命中 End 仍返回成功 | `Succeeded=true` 而 `OutputDataJson` 保持 null ⇒ `success:true, data:null` | `.../FlowExecutor.cs:236` 与 `:449`/`:472` |
| C24 | **低** | 字段已声明但无消费点 | `transform`（`.../FlowDslModels.cs:425`）、`from` 模板、`outputs` 字面量、`inputs.type` 强制、`description` 均无消费 | 见对应文件 |
| C25 | **低** | 沙箱硬顶与可配超时冲突 | `timeoutMs` 可配到 600,000ms，但 Jint 硬顶 8s ⇒ 配 >8s 无效且无提示 | `.../OrchestrationConsts.cs:20`、`:49` |
| C26 | **低** | `sensitive` 只进日志 | 唯一读取点是出参级打一条 Information；不脱敏、不裁剪；`map.item`/`group.header` 的 `sensitive` 连日志都没有 | `.../FlowExecutor.cs:476-484` |

### 4.3 编排引擎能力与语义（E）

| 编号 | 缺失项 | 现状 | 影响 |
|---|---|---|---|
| E1 | 独立 `Sql` 节点 | ❌ 无 dispatch 分支；文档与校验曾按已实现处理 | 照示例配置即运行期失败；SQL 只能经 Code `db.*` 或资源 CRUD |
| E2 | `Switch` | ❌ 无 dispatch，但校验允许其多出边 | 发布成功、调用即 500 |
| E3 | `Wait` | ❌ 不存在 | 异步出参在下游**永远不可用** |
| E4 | `txMode=sameDataSource` | ❌ DSL 无该字段（且未知字段静默忽略） | 跨节点写库无原子性 |
| E5 | range-`for` / `while` / break / continue | ❌ 仅 `ForEach` | 表达力受限 |
| E6 | 4 层条件组 / 表达式语言 | ⚠️ 仅编号布尔组合、无函数、4 层 | 复杂业务规则难以表达 |
| E7 | 持久化执行 / 重放 | ❌ 内存态 | 长流程与跨重启一致性不可承接 |
| E8 | 发布缓存跨进程失效 | ❌ 进程内 + 仅发布时失效 | 多副本最长 10 分钟旧 DSL；已下线流程仍可调用 |
| E9 | 子图节点审计明细 | ❌ 丢弃 | 批处理失败无法定位到"第几轮哪一步" |
| E10 | 调用权限粒度 | ❌ 仅 `Instances.Run` | 无法按流程授权 |

### 4.4 工程化与可验证性（G）

| 编号 | 严重度 | 问题 | 证据 |
|---|---|---|---|
| G1 | **高** | **7/7 Dockerfile 不可构建**：阶段名为 `build`/`final`，终阶却写 `COPY --from=publish`（Docker 会按外部镜像名解析并失败）；CI 无 `docker build` 步骤 ⇒ 缺陷永不暴露 | 例：`src/gateway/Meta.Dow.Gateway/Dockerfile:11`；`.github/workflows/ci.yml` |
| G2 | **高** | **测试覆盖实质为零**：62 个用例（56 `[Fact]` + 2 `[Theory]`，其中 6 个 `[InlineData]`）全部为纯单元测试，39 个集中在编排控制流；脚手架把 `httpClientFactory`/`permissionChecker`/`dataSourceResolver`/`codeSandboxExecutor`/`publishedFlowCache`/`resourceCrudExecutor` 以 `null!` 注入 ⇒ HTTP/SQL/Code/子流程/资源 CRUD 一旦触达必然 NRE | `tests/Meta.Dow.Orchestration.Tests/` |
| G3 | **中** | **分析器未启用**：`src/Directory.Build.props` 只有 `LangVersion`/`Nullable`/`TreatWarningsAsErrors=false` + 2 个 global using；全仓无分析器包引用；`.editorconfig` 无 error 级规则 ⇒ 162 个警告无门禁 | `src/Directory.Build.props`、`.editorconfig` |
| G4 | **中** | Release 无 Console 日志 sink（只写文件）⇒ 容器内日志不可采集 | `src/shared/Meta.Dow.Hosting.Shared/.../MetaDowLogging.cs:11-18` |
| G5 | **中** | 无集成测试宿主（无 Mongo/网关/认证链路测试）；Dockerfile、网关路由、OpenIddict 受众一致性均无校验 | `tests/` 仅 1 个纯单测工程 |
| G6 | **中** | 服务间实际不走 HTTP：4 个 `*.HttpApi.Client` 项目存在但**无任何 Host `DependsOn`**，靠共享 Mongo 上下文 + 各进程内 ABP 模块协作 ⇒ "微服务"目前是**部署边界而非数据边界** | 各 `*HttpApiHostModule.cs` |
| G7 | **中** | Projects 服务受众对不上：host 校验 `MetaDowProjects`，但注册的 `ApiResource` 只有 4 项**不含它**；`Projects_API` 的 scope `ProjectsService` 也未注册；`ApiScope` 配置键**从未被读取** ⇒ 有效令牌必 401 | `src/shared/Meta.Dow.Shared/Microsoft/Extensions/Hosting/MetaDowNames.cs:25`、`src/shared/Meta.Dow.DbMigrator/appsettings.json:9-14`、`:99`、`OpenIddictDataSeeder.cs:189` |
| G8 | **低** | 模板残留：4 个失效 `Meta.Dow.*.sln`（指向不存在的 `Tasky.*`/EF Core 项目）、5 个含 `Tasky` 的 `.gitignore`、`src/reset-databases.ps1|.sh`（指向不存在的目录，且其环境变量**无任何 C# 代码读取** ⇒ 完全失效）、`tools/generate_from_tasky.py`（硬编码他人机器路径、不在流水线中） | 见各文件 |
| G9 | **低** | 死配置：`AllowedCorsOrigins`（模型有、seeder 从不读）、`ApiScope`（从不被读取） | `.../Model/ServiceClient.cs:19`；`DbMigrator/appsettings.json:3-8` |

---

## 5. 整改批次与验收标准

### 5.1 P0 —— 止血（建议 3~5 个工作日，均为小改动）

| 序号 | 事项 | 验收标准 |
|---|---|---|
| P0-1 | DSL 校验补节点 type/kind 白名单，`Switch`/`Sql` 改为**发布期拒绝** | 构造三类非法 DSL，发布即被拒且报明确错误；补单测 |
| P0-2 | 无最终契约时**报错**而非输出全部 `ctx.Vars` | 未配 `finalOutputs` 的流程发布或执行被拒；补单测 |
| P0-3 | `visibleTo` 未知 mode 改为报错；`roleIds` 与角色 Id 比较或删除该字段 | 拼错 mode 的流程发布/执行报错；补单测 |
| P0-4 | 子图（SubFlow / ForEach / Parallel）透传调用方角色，不再 `currentUser:null` | 子图内 `visibleTo` 生效；补单测（含受限字段不出现在父输出） |
| P0-5 | 约定列服务端强制：`TenantId`/`Id`/`IsDeleted`/审计列的 `Origin` 不接受客户端声明；`UpdateAsync` 与 Create 对齐 | 提交 `columns[].origin="user"` 无法把 `TenantId` 纳入 SET；补测试 |
| P0-6 | 破坏性 DDL 默认禁止，需显式开关 + 二次确认 | `DROP COLUMN`/`RENAME` 在默认配置下被拒并审计 |
| P0-7 | 默认口令：移除 `1q2w3E*` 回退，改首次随机 + 强制改密；修掉 `NotImplementedException` 错误路径 | 新租户管理员口令非公开值；错误路径有真实处理 |
| P0-8 | `PruneObsoleteClients` 默认改 `false` | 默认配置下不删除未登记客户端 |
| P0-9 | 7 个 Dockerfile `COPY --from=publish` → `--from=build`；CI 增加 `docker build` | 7 个镜像本地可构建；CI 通过 |
| P0-10 | Projects 受众：`ApiResource` 增加 `MetaDowProjects`；`Projects_API` scope 修正 | 用有效令牌访问 Projects 返回 200 而非 401 |
| P0-11 | 网关业务路由显式挂授权策略 | 未认证请求在网关即被拒，不再依赖下游配置 |
| P0-12 | `ShowPII` 改为按环境开关 | 生产环境日志不含令牌与 PII |
| P0-13 | OA：修正驳回语义（`!input.Pass` 必须 return 到真实驳回动作）；补 `UpdateAsync` 落库；两实体加 `ConcurrencyStamp` | 驳回不再记 `approve`；补丁落库；并发提交只有一个成功；各补单测 |
| P0-14 | 已发布工作流禁止原地改写（改稿必须走新版本或回到草稿） | PUT 已发布定义被拒或强制降级为草稿 |

### 5.2 P1 —— 补齐运行保障（建议 2~4 周）

| 序号 | 事项 | 验收标准 |
|---|---|---|
| P1-1 | 实现 `txMode=sameDataSource`（`FlowDbSessionScope` 挂执行上下文，成功 commit / 失败 rollback） | 同库多次 `db.execute` 失败可回滚；补集成测试 |
| P1-2 | **未知 DSL 字段改为报错**（禁止静默忽略） | 传 `txMode` 等未支持字段时发布报错，不再误导 |
| P1-3 | 异步节点落 JobId + 节点状态 + 失败回写；`timeoutMs` 覆盖后台任务；补 `Wait` 或明确禁用异步出参引用 | 异步失败在实例与 API 上可见；补单测 |
| P1-4 | 发布缓存：`Disable`/`Delete` 也失效；改分布式缓存 + 版本比对，或加失效事件广播 | 已下线流程立即不可调用；多副本一致；补测试 |
| P1-5 | SQL 写判定统一（`WITH` 前缀、`db.batch`、`db.query` 走同一判定） | 只读上下文无法执行任何 DML；补测试 |
| P1-6 | Http 节点出站白名单（禁私网/回环/链路本地） | 内网地址被拒；补测试 |
| P1-7 | 数据范围：接线 `ApplyDataPermission`，或明确从产品口径移除并在文档声明 | 二选一，不留"看起来有"的中间态 |
| P1-8 | 报表/资源级授权下沉：`sys.resource.query` 计入资源级校验与 DataScope | 直连系统流不再能绕过报表授权 |
| P1-9 | 补 `Orchestration.Logic.{flowKey}` 调用权限 | 无该权限者无法调用对应流程 |
| P1-10 | 修 `batchupdate`（合并 `filter.Args`、修正时间戳比较） | 批量更新 affected > 0；补测试 |
| P1-11 | 修正 PG/Oracle 重试分类（按 SQLSTATE 字符串匹配而非 int） | `40P01`/`08xxx`/`57P01` 触发重试；补单测 |
| P1-12 | 索引创建失败升级为启动失败或运维门禁 | 唯一索引缺失不再静默 |
| P1-13 | 启用分析器（`EnableNETAnalyzers` + `AnalysisLevel`），警告清零并纳入门禁 | CI 中警告超阈值即失败 |
| P1-14 | 引入集成测试宿主（Testcontainers + Mongo / `WebApplicationFactory`），替换 `null!` 脚手架 | 覆盖 W2/W3/W7/W8、并发、租户隔离、HTTP/SQL/Code |
| P1-15 | MQTT/Kafka/Rabbit 触发器加分布式锁；MQTT client id 固定 | 多副本不重复触发 |

### 5.3 P2 —— 结构性（建议 1~2 个季度，需排期决策）

| 序号 | 事项 | 说明 |
|---|---|---|
| P2-1 | **拆分 SaaS**：优先把编排执行器 + 数据源/表设计抽为独立服务 | SaaS 占全仓 **78.0%** 代码（28,756 / 36,886），是 README 自列的头号风险，且实际数字比其描述更严重 |
| P2-2 | **DSL 机器可校验 schema**（JSON Schema）+ `version` 真校验 + 迁移指南 + 变更日志 | 消除前后端与文档三方靠约定同步 |
| P2-3 | **受限表达式语言**：以白名单函数扩展 `BoolExpressionEvaluator`（禁 eval） | 提升表达力而不牺牲沙箱安全 |
| P2-4 | 评估持久化执行（Elsa 3 / Temporal 风格）承载长流程 | 决定引擎适用边界的根本问题 |
| P2-5 | 补 `Switch`、range-`for`/`while`、独立 `Sql` 节点、`Wait` | 控制流补齐 |
| P2-6 | 错误码表 + 术语表；`UserFriendlyException` 改为带错误码 | 前端可按码分支；降低沟通成本 |
| P2-7 | 清理模板残留（G8）与死配置（G9） | 卫生项 |

---

## 6. 文档与知识治理机制（防止再次漂移）

本次评估发现的最系统性问题不是单个 bug，而是**文档把"目标态"当"现状"写**，且同一能力在四处有四种说法。已完成的整改见附录 C。为防复发，建议固化以下机制：

| 机制 | 做法 |
|---|---|
| **章节性质声明** | 每篇文档头部强制声明「事实契约 / 规划 / 操作说明」，混合的必须拆章（本轮已落地） |
| **单一权威状态表** | 每篇只允许**一张**能力状态表，其余章节引用而不复述（本轮已落地） |
| **证据内联** | 每条"已实现"必须附 `相对路径:行号`；代码变动即失效，迫使同步（本轮已落地） |
| **规划隔离** | 规划统一收进独立章节，禁止与契约章混写（本轮已落地） |
| **可运行示例** | quickstart 提供"复制即可跑通"的最小闭环（含取 token 的完整命令）——**尚未落地** |
| **机器可校验 schema** | DSL 的 JSON Schema 纳入仓库，CI 校验示例与种子 DSL——**尚未落地** |
| **变更日志 / 迁移指南** | DSL 已有 `expression/when` → `combine/isDefault` → `loopBody/branches` 的演进，必须记录版本与迁移——**尚未落地** |
| **计数类断言自动化** | 测试数、文档行数、路由数、代码行数等易漂移数字由脚本生成或校验。本轮已发现 4 处此类错误：测试用例 23→**62**、`docs/` 行数（4,600→**6,104**）、网关路由数（11→**13**，其中 1 条挂策略）、代码行数口径（`Measure-Object -Line` 少算约 14%，77.8%→**78.0%**） |

---

## 附录 A：证据索引（本文引用的关键位置）

| 主题 | 位置 |
|---|---|
| 节点分发与执行 | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FlowExecutor.cs:640-793` |
| 主循环与失败关闭 | 同上 `:272-353`、`:313-317`、`:335-346`、`:351` |
| 条件求值与算子 | 同上 `:497-554`、`:2289-2310`、`:2264-2269` |
| 节点级超时 | 同上 `:586-593`；`.../Domain.Shared/Orchestration/OrchestrationConsts.cs:46-52` |
| 异步 Http | 同上 `:860-944` |
| 出参组装与可见性回退 | 同上 `:436-495`（尤其 `:446-453`、`:455`） |
| 发布校验 | 同上 `:2388-2545`、`:2597-2644`、`:2788-2806` |
| 子图执行（旁路点） | 同上 `:1518-1527`、`:1943-1952` |
| DSL 强类型模型 | `.../Orchestration/FlowDslModels.cs`（全文 528 行） |
| 沙箱与 db 宿主 | `.../Orchestration/CodeSandboxExecutor.cs:74-108`、`:196-237`、`:239-264`、`:402-413` |
| 脚本静态检查 | `.../Orchestration/CodeScriptStaticAnalyzer.cs:12-19`、`:44-53` |
| 弹性与重试分类 | `.../Orchestration/NodeResiliencePolicy.cs:59`、`:93`、`:141-164`、`:166-191` |
| 全局 Http 弹性（禁非安全方法重试） | `src/shared/Meta.Dow.ServiceDefaults/Microsoft/Extensions/Hosting/Extensions.cs:37-53` |
| 路径解析与路径糖 | `.../Orchestration/FlowContextResolver.cs:41-163`、`:166-215`、`:402-439`、`:478-512` |
| 出参整形 | `.../Orchestration/FinalOutputAssembler.cs:36-47`、`:240-243`、`:285-414`、`:870-898` |
| 可见性判定 | `.../Orchestration/OutputVisibilityFilter.cs:57-96` |
| SQL 参数化与租户谓词 | `.../Orchestration/ParameterizedSqlExecutor.cs:68-71`、`:153-178`；`.../FilterSqlBuilder.cs:50-71`、`:228`、`:316-327`、`:363-375` |
| 资源 CRUD 写路径 | `.../Orchestration/ResourceCrudExecutor.cs:372-378`、`:415`、`:645-672`、`:931-972` |
| DDL 执行 | `.../Orchestration/SqlDdlExecutor.cs:242-291` |
| Mongo 索引 | `.../Meta.Dow.SaaS.MongoDB/SaaSMongoDbModule.cs:44-105` |
| 发布缓存 | `.../Orchestration/PublishedFlowCache.cs:38`、`:104-112`、`:137-138`；`.../FlowDefinitionAppService.cs:290` |
| 系统 API 与响应 | `.../Orchestration/LogicApiAppService.cs:35-104`；`.../HttpApi/Orchestration/LogicApiController.cs:22-51`、`:67-79` |
| 表单草稿/快照 | `.../Domain/Orchestration/FormDefinition.cs:80-89`；`.../WorkflowDefinitionAppService.cs:143-197` |
| 网关路由与策略 | `src/gateway/Meta.Dow.Gateway/appsettings.json`、`Program.cs:38-78` |
| OpenIddict 种子与受众 | `src/shared/Meta.Dow.DbMigrator/OpenIddictDataSeeder.cs:187-212`、`appsettings.json:2-14` |
| 权限目录 | `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Permissions/OrchestrationPermissions.cs`、`SaaSPermissionDefinitionProvider.cs:21-131` |
| 数据范围 | `src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/`（`DataPermissionQueryableExtensions.cs`、`DataScopeResolver.cs`） |
| 测试 | `tests/Meta.Dow.Orchestration.Tests/`（5 个文件） |
| 构建与门禁 | `src/Directory.Build.props`、`.editorconfig`、`.github/workflows/ci.yml` |

## 附录 B：未核验与边界

1. **前端一律未核验**：前端在独立仓库（与本仓同级的 `vue-demo`，Vben Admin + AntV X6），涉及交互、渲染、拖拽的结论均为规格描述，未逐行核对。
2. **Docker 未实机构建**：本机沙箱拒绝 Docker named pipe，Dockerfile 结论为静态分析（阶段名语义确定，7/7 同构）。
3. **认证链路为配置级推断**：应用栈未启动，Projects 受众不匹配、`invalid_scope` 等结论由配置与代码推断，建议起栈后用真实令牌验证。
4. **两处刻意不做绝对判断**：`UserFriendlyException` 的 HTTP 映射（不假定 409）；MySQL/Oracle DDL 隐式提交（只断言"不具备回滚语义"，不断言"一定失败"）。
5. **未逐 provider 走通**：`SqlCatalogInspector` 对 SQL Server 表值函数的分支、`IsProtectedIndex` 的保护名单。

## 附录 C：本次已完成的文档重构（同期成果）

评估过程中同步完成了 `docs/` 9 篇 + `README.md` 的结构性重构，原则是"文档只描述代码的真实行为"：

| 文件 | 行数变化 | 主要动作 |
|---|---|---|
| `docs/lowcode-logic-orchestration.md` | 2,723 → 895 | 合并四处重复的节点规格为单一契约；四张互相矛盾的状态表并为一张；新增行为边界章（30 条）；规划隔离 |
| `docs/lowcode-form-oa-enhancement.md` | 1,243 → 1,242 | 结构全换（8 章）；F/W 缺陷逐条复核定级；"已全部修复"降级为"主体落地，遗留项见 §…" |
| `docs/lowcode-form-workflow-board.md` | 1,167 → 1,199 | 新增 18 条缺陷（C1–C18）与 10 条新发现；23 条失真断言清单入修订记录 |
| `docs/lowcode-report-management.md` | 676 → 575 | 44 项能力权威表；纠正"列管道 transform→format→style→render"等 27 条失真 |
| `docs/add-new-service.md` | 155 → 611 | 改写为可执行操作手册（每步含验证方法、真实端口与路由） |
| `docs/iam-rbac-menu.md` | 344 → 445 | 数据范围如实降级为"已建模、未接线"；13 条缺陷 |
| `docs/lowcode-oa-workflow.md` | 152 → 422 | 与 enhancement 分工；补草稿/快照不对称等缺陷 |
| `docs/lowcode-report-config-ux.md` | 87 → 257 | 保持"前端交互规格"定位，未被误扩为后端契约 |
| `docs/lowcode-form-preview.md` | 335 → 458 | 明确"本仓无预览管线，能力全在前端" |
| `README.md` | 167 → 303 | 架构评价改为**有证据的评分**（7 维度各附加减分依据），并新增安全与可验证性两个维度 |

**通用整改（本轮已落地）**：

1. 全部 `file:///d:/Project/...` 绝对盘符链接改为仓库相对路径；
2. 跨文档引用统一为 `./xxx.md` 相对链接（已校验 100% 可解析）；
3. 每篇文档头部强制声明「文档性质 + 代码基线 + 核对方式 + 状态图例」；
4. 保留原文仍有价值的内容（对标、"明确不做什么"、反模式、事故复盘），只删改与代码不符或纯重复的部分；
5. 修正计数类断言（测试用例 62、`docs/` 6,104 行、网关 13 条路由中 1 条挂策略、健康端点 6 处调用点）。

> **待办**：`src/CLAUDE.md` 的重构仍在进行中；它是被自动注入的 AI 指令文件，失真危害最大（原声称"分析器已启用"，实际未启用）。
