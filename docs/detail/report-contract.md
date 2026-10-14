> **功能描述**见 [../report.md](../report.md)。本文件是该功能的证据附录（契约 / 评审 / 方案），不单独作为阅读入口。

# Meta.Dow 通用报表管理：事实契约与规划

> **文档性质**：混合文档，**已按章拆开**。第 1–4、6 章是**事实契约**（只描述代码真实行为）；第 5 章是**规划**（未实现，与事实严格隔离）。
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）｜ Release 构建 0 错误、单测 62/62 通过。
> **核对方式**：每条「已实现 / 部分实现」断言附 `相对路径:行号`（相对仓库根）。图例：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划） ｜ 🐞 已知缺陷
> **前端**：不在本仓库（`D:\Project\vue-demo`，web-antd / Ant Design Vue）。凡涉及前端交互/渲染的断言一律标注**未核验**。
> **后端**：SaaS 编排运行时（`PublishedFlowInvoker` + `ReportDefinitionAppService` + `ResourceCrudExecutor` + `FilterSqlBuilder`）。

**一句话**：报表是「已发布的查询页配方」——一份 `ReportDefinition` 聚合，加上一棵运行时编译成参数化 SQL 的 `FilterNode` 条件树。它不是第二套 CRUD，也不是积木报表那种 Excel 画布。**后端的筛选运行时只有一个模型：树**；「查询表单 / 更多条件」是产品侧的两种录入界面，不是两条编译路径。

---

## 1. 定位与边界

### 1.1 为何单独成文

现有 `AppResource` 已能：绑表、组合筛选、列表、抽屉表单、`sys.resource.*`。缺口是「业务报表」常见能力：

| 缺口 | 现状 | 报表要补 |
|------|------|----------|
| 查询交互 | 条件行 + 手写 `1 and (2 or 3)` | **查询表单** + **可视化条件组** |
| 一资源多页 | 一个 ListView | 同一资源可发多份报表（待审 / 汇总 / 只读台账） |
| 列表现 | 仅 `formatPreset` | 字典转换、阈值着色、进度、Tag、链接 |
| 汇总 | 只有 `total` 行数 | 筛选结果合计、均值、计数 |
| 按钮 | 增删改详情骨架 | 弹窗填报、单行 / 多行 / 单列批量改、确认、后置刷新 |
| 入口 | 资源码路由 | **发布即登记菜单 + 权限** |
| 跨表查询 | 文档写「另开页」 | 报表可绑自定义 `queryFlowKey`，契约兼容 |

资源仍是数据与默认 CRUD 的一等公民；**报表是挂在资源（或独立查询流）上的页面配方**。看板、人审、Excel 套打仍不进本文实现范围。

```
数据源 → 表设计器 → AppResource（结构 + 默认 CRUD）
                         │
                         ├── 默认列表（实施自用 / 简单台账）
                         └── ReportDefinition[]（报表配方，可发布）
                                      │
                                      ▼
                    ReportDefinitionAppService.QueryAsync
                      ├─ 注入 resourceCode / columns / sorting / summaryFields
                      ├─ filter := { and, [dataScope, 用户树] }
                      ▼
                    PublishedFlowInvoker(queryFlowKey)   ← 默认 sys.resource.query
                                      │
                                      ▼
                    ResourceCrudExecutor(query) → FilterSqlBuilder → 参数化 SQL
                                      │
                                      ▼
                         { items, total, summary? }
```

**关键事实**：报表**不自带查询引擎**。报表只做「契约装配」（注入资源码、可见列、默认排序、汇总字段、把设计器数据范围与用户条件合成一棵树），真正的 SQL 由编排节点 `sys.resource.query` 背后的 `ResourceCrudExecutor` 生成（`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ReportDefinitionAppService.cs:250`、`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ResourceCrudExecutor.cs:162`）。

### 1.2 开源对标（GitHub）

下列只学信息架构，不引入整套运行时。**本章为设计论证，不代表这些能力已实现**（实现状态见第 2 章）。

| 项目 | 学什么 | 不照搬 |
|------|--------|--------|
| **NocoBase** [nocobase/nocobase](https://github.com/nocobase/nocobase) | Collection 与 UI 解耦；**Filter Form 块**（填表即筛）；Filter Action 嵌套条件；Action scene = collection / record；表格 summary | 插件宇宙、整页 Block 拼装 |
| **NocoDB** [nocodb/nocodb](https://github.com/nocodb/nocodb) | Toolbar Filter 递归 **条件组**（AND/OR、最多数层）；空值忽略；视图级保存筛选 | 电子表格编辑心智 |
| **Airtable / Supasheet** | 「全部匹配 / 任一匹配」文案；+ 条件 / + 条件组；同一表多 View | 闭源交互细节 |
| **Amis CRUD** [baidu/amis](https://github.com/baidu/amis) | `headerToolbar` / `bulkActions` / 行内 `operation`；`dialog` / `ajax` / `drawer`；列 `mapping`/`status` | 不引入 Amis 全量 JSON 运行时 |
| **ToolJet / ILLA** | Query 与 Table 分离；按钮绑 Action | 万能画布 App |
| **Metabase** [metabase/metabase](https://github.com/metabase/metabase) | 保存问题、筛选器小部件、汇总、钻取、仪表问题 | 分析师 SQL 笔记本不是我们的主路径 |
| **JimuReport** [jeecgboot/jimureport](https://github.com/jeecgboot/jimureport) | 分组小计 vs 全量合计（`sum` vs `dbsum`）的产品区分 | **不做** 类 Excel 无限格子、套打、交叉表画布 |

对本仓库最贴合的组合：

1. **页模型**：NocoBase / Supasheet（资源 + 多报表）。
2. **普通人筛选**：NocoBase Filter Form + NocoDB/Airtable 条件组 —— 二者在后端**收敛为同一棵 `FilterNode` 树**。
3. **按钮**：Amis Action + NocoBase scene（**当前后端只持久化配置，不执行**，见 §3.6）。
4. **列与汇总**：Amis mapping + NocoBase table summary；合计语义学积木报表的「本页 vs 全量」。
5. **执行**：继续 `PublishedFlowInvoker`，不另起报表引擎。

逻辑编排 Condition 节点可以 **继续** 用编号组合式（给实施/研发）。**报表运行时禁止把这套语法暴露给业务员。**

### 1.3 为什么淘汰运行时手写 `1 or 2 and 3`

| 问题 | 后果 |
|------|------|
| 要记编号、and/or、括号优先级 | 业务员不会用，只能全 AND |
| 删第 2 条后编号错乱 | 组合式 silently 错或报错 |
| 和「查询表单」两套心智 | 实施配一套、用户用另一套 |
| 与编排 Condition 绑定过死 | 编排可以保留公式；报表必须白话 |

**代码现状（写实）**：编号组合式**没有被删除**，而是降级为**兼容路径**。`FilterSqlBuilder` 先判断运行时节点是不是树（有 `children`，或 `kind` 为 `group`/`rule`），是树就走递归编译；否则回落到 `items[] + combine` 解析，并复用 `ConditionCombineEvaluator.Validate` 与递归下降的 `ParseOr/ParseAnd/ParseUnary`（`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FilterSqlBuilder.cs:116`、`:198`、`:432`、`:470`–`:536`）。设计器侧「旧版暴露给用户的编号条件」在资源读取时被升格为查询表单字段（`FilterDef.EnsureSearchForm`，`src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs:111`；调用点 `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/AppResourceAppService.cs:308` 与 `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/ReportDefinition.cs:143`）。

即：**运行时仍接受组合式字符串，树是首选路径**。产品上不暴露公式框，是前端约束；后端并未禁止。

### 1.4 明确不做什么

- 不在报表运行时**面向业务员**出现 `1 and (2 or 3)` 输入框（后端兼容路径仍在，但不作为产品路径）。
- 不引入 JimuReport / UReport / Excel 画布、套打、无限行列。
- 不引入 Amis/NocoBase 全量前端运行时。
- 不做即席 SQL 编辑器给业务员（那是编排 Code/`db.query`）。
- 不把人审画进报表按钮（按钮可 `StartWorkflow`，引擎仍是能力 B）。
- 不在单元格内执行任意 JS 写库。

---

## 2. 现状能力总表（唯一权威表）

> 本表是**唯一**权威状态表。其余章节出现的「已实现 / 规划」字样，一律以本表为准。
> 前端行为（渲染、条件组 UI、列设置）**均未核验**，不列入本表的 ✅。

| 能力 | 状态 | 证据 `file:line` | 备注 |
|------|------|------------------|------|
| `ReportDefinition` 聚合（草稿/发布、多租户） | ✅ | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/ReportDefinition.cs:11`、`:228`、`:230` | `IMultiTenant` + `FullAuditedAggregateRoot`；`Publish/Unpublish` 只切 `Status` |
| `ReportDefinition` CRUD | ✅ | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ReportDefinitionAppService.cs:44`–`:186` | 路由 `api/orchestration/reports`（`src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/AppBuilderControllers.cs:167`） |
| 一键从资源生成 | ✅ | `ReportDefinition.cs:136`；`ReportDefinitionAppService.cs:110` | 复制 SearchForm/DataScope/Presets/Columns/Actions/Form/Dictionaries；`POST from-resource` |
| 一键从物理表生成 | ✅ | `ReportDefinition.cs:161`；`ReportDefinitionAppService.cs:133` | 按平台类型给 `FormatPreset`；`POST from-table` |
| 发布前校验 | ✅ | `ReportDefinitionAppService.cs:189`–`:222` | 校验 queryFlow 已发布、resource 已发布、table 已 apply |
| **`FilterNode` 真条件树**（递归编译 SQL） | ✅ | `FilterSqlBuilder.cs:214`–`:288`；模型 `ResourceSchema.cs:24` | 递归 + 括号；`or`/`and` 组天然成括号 |
| 字段必须存在于物理列映射 | ✅ | `FilterSqlBuilder.cs:300`–`:303` | `colMap` = **表的全部列**，不是设计器勾选的暴露列 |
| 值全部参数化 | ✅ | `FilterSqlBuilder.cs:329`–`:396` | `eq/ne/gt/gte/lt/lte/contains/notcontains/startswith/between/in/notin/isempty/isnotempty` |
| 空值规则编译期丢弃 | ✅ | `FilterSqlBuilder.cs:243`、`:407`–`:430` | 空串/空数组丢弃；`isempty`/`isnotempty` 例外 |
| 条件树深度上限 | ⚠️ | `FilterSqlBuilder.cs:228`；`OrchestrationConsts.cs:14` | `MaxFilterGroupDepth = 3`，判据 `depth > 3` 抛错 ⇒ 实际可嵌套 **4 层**（根 depth=0） |
| **租户谓词独立、不可被 OR 逃逸** | ✅ | `FilterSqlBuilder.cs:54`–`:71`、`:152` | `tenantId`/`isDeleted` 作为独立谓词，最终 `string.Join(" AND ", ...)` ⇒ 用户 OR 无法逃逸 |
| **DataScope 谓词独立、不可被 OR 逃逸** | ✅ | `FilterSqlBuilder.cs:73`–`:81`、`:152` | 同上；与租户谓词平级 |
| 设计器数据范围（`DataScope`）参与运行时 | ✅ | `ReportDefinitionAppService.cs:283`；`FilterSqlBuilder.cs:73` | 报表侧先合成 `{and,[dataScope,runtime]}`，再由构建器编译 |
| 查询表单（`SearchForm`）持久化 | ✅ | `ReportDefinition.cs:33`、`:114`；`AppBuilderDtos.cs:303` | 字段模型：`key/field/title/op/control/span/placeholder/orGroup` |
| 「查询表单」运行时独立编译路径 | ❌ | 无调用点 | 后端把表单值当普通树；没有「表单层」概念，`SearchForm.Fields` 不被 `FilterSqlBuilder` 读取 |
| 「任一满足」卡片 | ⚠️ | `ResourceSchema.cs:53`（`OrGroup`） | 模型有 `OrGroup` 字段，**无服务端消费点** |
| 可视化条件组 UI / 缩进卡片 | ❌ | — | 前端（`vue-demo`）事项，本仓库无证据；**未核验** |
| `AdvancedFilter` 开关生效 | ❌ | `ReportDefinition.cs:35`；全仓无消费点 | 只回读；关掉后运行时仍接受任意树 |
| `Presets`（快捷筛选芯片） | ❌ | `ReportDefinition.cs:37`；仅 `:116`/`:147` 读写 | 无运行时消费 |
| `Kpis` | ❌ | `ReportDefinition.cs:39`；`ResourceSchema.cs:461` | 无运行时消费 |
| `SelectionEnabled` | ⚠️ | `ReportDefinition.cs:53`、`:154` | 存、回读，且 `ApplyFromResource` 会推导；**请求体不注入**，前端需自查 |
| `DefaultSorting` 生效 | ✅ | `ReportDefinitionAppService.cs:272`–`:279`；`ResourceCrudExecutor.cs:807` | 未传 `sorting` 时用报表默认；字段不在列内回落 `CreationTime`/首列 |
| `PageSize` 生效（下限/上限） | ✅ | `ReportDefinition.cs:127`；`ReportDefinitionAppService.cs:271` | 聚合侧 1–`ResourceQueryMaxPageSize`(=200)；执行侧 `Math.Clamp` 同上限（`ResourceCrudExecutor.cs:194`） |
| `SkipCount`/`MaxResultCount` 限幅 | 🐞 | `ReportDefinitionAppService.cs:62`–`:63` | 报表**列表管理**接口；ABP 默认值，无业务上限需求，低危 |
| 列模型 `ListColumnDef` | ✅ | `ResourceSchema.cs:157`–`:224` | `Field/Title/Width/Sortable/Visible/FormatPreset/Align/Ellipsis/Fixed/Render/DictMap/Options/DictCode/FormatFn/RenderFn/StyleRules/SummaryFn/SummaryScope` |
| 列可见性白名单注入（报表路径） | ✅ | `ReportDefinitionAppService.cs:281`–`:282` | `columns` = `Visible == true` 的列 |
| 列级过滤（服务端强制不可越界取值） | ❌ | `ResourceCrudExecutor.cs:783`–`:805` | 请求 `columns` 只做「必须是物理列」校验；`columns` 为空/全非法时回落 **全部列**，可返回 `SELECT *` |
| `columns` 覆盖可见列（隐藏列） | 🐞 | 同上 | 客户端可自带 `columns` 取隐藏列 —— 见 §4.2 |
| 列 `transform`（dict/enumMap/unit/datePart） | ❌ | 无 `transform` 模型 | 仅 `FormatPreset` + `DictMap`/`DictCode`/`FormatFn`（`ResourceSchema.cs:170`–`:194`） |
| 列 `styleRules`（阈值着色） | ⚠️ | `ResourceSchema.cs:146`–`:156`、`:196` | 实际是扁平 `ColumnStyleRule{Op,Value,Tone,Target}`，**不是**条件树；`Tone` 无白名单校验 |
| 列 `render`（Tag/链接/进度） | ⚠️ | `ResourceSchema.cs:181`、`:194` | `Render` 为自由字符串，默认 `text`；无组件白名单、无服务端校验 |
| 列 `FormatFn`/`RenderFn`（JS 沙箱） | 🐞 | `ResourceSchema.cs:191`、`:194` | **注释称返回 HTML**；与「不返回 HTML」的安全设计目标冲突，前端执行，**未核验** |
| 列汇总（`filtered`） | ✅ | `ResourceCrudExecutor.cs:852`–`:928` | 第二条聚合 SQL，`COUNT(1)` + 每请求一个聚合；与列表同 WHERE、同参数 |
| 汇总 `fn` 白名单 | ⚠️ | `ResourceCrudExecutor.cs:964`–`:972` | 未知 `fn` 静默回落 `sum`；`countDistinct` 无实现；数值校验在 `:884`–`:890` |
| 汇总字段来源（设计器 vs 客户端） | 🐞 | `ResourceCrudExecutor.cs:931`–`:961` | 客户端传了 `summaryFields` 就**完全覆盖**设计器；无「列是否可见/是否配了 sum」的交叉校验 |
| 汇总 `scope` = `page` / `group` | ❌ | `ResourceSchema.cs:200` | 模型只有 `SummaryScope` 字符串（默认 `filtered`），无实现分支 |
| 汇总输出契约 `{_count, Field:{fn}}` | ✅ | `ResourceCrudExecutor.cs:913`–`:928` | 空结果返回 `null`（不产出 `summary` 键） |
| `ActionDef` 模型 | ✅ | `ResourceSchema.cs:227`–`:272` | `Key/Label/Position/Scene/Scope/Kind/FlowKey/Open/Fixed/FormKey/FormMode/FieldsMode/Fields/PayloadFields/After/Confirm/ConfirmText/BatchLimit/Description/MultiSelect` |
| **`ActionDef` 服务端执行端** | ❌ | 仅模型 + 种子 | grep 仅命中 `ResourceSchema.cs:227`、`src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/AppResource.cs:173`–`:184`；按钮靠前端调 `/api/logic/{flowKey}` |
| `PayloadFields` / `BatchLimit` / `After` / `VisibleWhen` 运行时消费 | ❌ | `ResourceSchema.cs:245`、`:256`、`:259`、`:265` | 无消费点；`VisibleWhen`/`DisabledWhen`/`Acl` **模型里也不存在** |
| 报表内表单片段（`forms[]`） | ⚠️ | `ReportDefinition.cs:45` | 实际是**单个** `FormDef Form`，不是 `forms[]` |
| 报表发布 | ✅ | `ReportDefinitionAppService.cs:188`；路由 `:215` | 只改 `Status`；附带前置校验 |
| 发布即登记 `SysMenu` | ❌ | 全仓 grep `SysMenu`/`App.Report` 无命中；`ReportDefinitionDto` 无 `menu` 字段（`AppBuilderDtos.cs:283`–`:328`） | 无「每报表建菜单」代码 |
| 通用报表运行时路由 | ✅ | `src/services/administration/src/Meta.Dow.Administration.Domain/Menus/MenuDataSeedContributor.cs:565` | 隐藏菜单 `AppReportRuntime` → `/app/reports/:code`，权限 `Orchestration.Reports.Execute`；`:582` 另有 `AppReportForm` |
| 报表权限（分组） | ✅ | `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Permissions/SaaSPermissionDefinitionProvider.cs:76`–`:84` | `Reports.Default/Create/Update/Delete/Publish/Execute` |
| 每报表独立权限 `App.Report.{code}` | ❌ | 同上 | 无动态权限定义；`App.Report.*` 全仓无命中 |
| 报表数据范围 = 权限强制 | ❌ | `ReportDefinition.cs:31` | `DataScope` 只是**查询条件**；IAM 行级数据范围零落地 —— 见 §4.1 |
| 按钮级 `acl` | ❌ | — | 模型无 `acl` 字段 |
| `batchcreate` / `batchdelete` | ✅ | `ResourceCrudExecutor.cs:170`、`:174`、`:604`、`:676` | 单连接 + 单事务（`ParameterizedSqlExecutor.BatchAsync`） |
| **`batchupdate`** | 🐞 | `ResourceCrudExecutor.cs:631`–`:673` | **实际不可用** —— 见 §4.3 |
| 存储过程结果分页 | 🐞 | `ResourceCrudExecutor.cs:268`–`:292` | **全量读入内存**再 `Skip/Take` —— 见 §4.5 |
| 四层过滤（租户→范围→表单→更多条件） | ✅ | `FilterSqlBuilder.cs:54`/`:73`/`:116`/`:152` | 后端实现为**独立谓词 AND 拼接**，四层概念成立 |
| 前端运行时页面结构（KPI 条/芯片/更多条件 Drawer） | ❌ | — | 全部前端事项，**未核验** |
| FilterSqlBuilder / SqlDialect / DataScope / 租户过滤 单测 | ❌ | `tests/Meta.Dow.Orchestration.Tests/`（仅 5 个测试文件） | 无任何相关测试 |

---

## 3. 契约与实现细节（按代码写实）

### 3.1 领域模型（`ReportDefinition` 聚合）

```
ReportDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
  TenantId, Code, Name, Kind(resource|table|flow), Status(Draft|Published)
  ResourceCode?          资源查询源；table/flow 的可选写入资源
  DataSourceCode?        table 查询源
  TableName?
  QueryFlowKey           默认 sys.resource.query
  DataScope: FilterNode  设计器隐藏条件（默认 {group, and, []}）
  SearchForm: SearchFormDef      查询表单（Columns 默认 3，Combine 默认 and）
  AdvancedFilter: bool   默认 true（无消费点）
  Presets: NamedFilterPreset[]   默认 []
  Kpis: KpiDef[]                 默认 []
  Columns: ListColumnDef[]       默认 []
  Actions: ActionDef[]           默认 []
  Form: FormDef                  默认 new（单表单，不是 forms[]）
  Dictionaries: NamedDict[]      默认 []
  DefaultSorting: string?        默认 "CreationTime desc"
  PageSize: int                  默认 20，更新时钳制 1..200
  SelectionEnabled: bool         默认 false
  Description: string?
```

证据：`src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/ReportDefinition.cs:11`–`:55`（属性）、`:61`–`:79`（构造函数默认值）、`:81`–`:134`（`UpdateDraft`）、`:199`–`:226`（`ColumnsFromTable`）。

**与旧文档不符、按代码改正的点**：

- 没有 `schemaVersion` 字段（版本靠草稿/发布 `Status` 切换；`ReportDefinition.cs:29`、`:228`）。
- 没有 `summaryBar`、没有 `menu: { bind, parentId, title, icon }`、没有 `selection: { enabled, preserveOnPage }`。
- 没有 `advancedFilter` 之外的「是否允许更多条件」控制；`AdvancedFilter` 本身无消费点。
- `formRef` / `forms[]` 不存在，只有单个 `Form`。
- `KpiDef` 只有 `Title/Field/Fn`（`ResourceSchema.cs:461`），**没有** `filters`；`fn` 默认 `count`。

### 3.2 查询编译：`FilterSqlBuilder`（核心契约）

`FilterSqlBuilder.Build(provider, columns, designerFilter, runtimeFilters, tenantId)` 是**唯一**的 WHERE 生成入口（`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FilterSqlBuilder.cs:42`）。

**谓词装配顺序与隔离性（真实优势，写实）**：

```csharp
var predicates = new List<string>();          // 每条是独立、自洽的谓词
// ① 租户（依赖物理 TenantId 列存在）
if (colMap.ContainsKey("TenantId")) { ... predicates.Add("TenantId = @tenantId"); }
// ② 软删（依赖物理 IsDeleted 列存在）
if (colMap.ContainsKey("IsDeleted")) { ... predicates.Add("IsDeleted = @isDeleted"); }
// ③ 设计器数据范围（FilterNode 树）
if (designerFilter?.DataScope != null) { var s = CompileNode(...); if (s != null) predicates.Add(s); }
// ④ 设计器隐藏编号条件（旧模型，兼容）
if (designerFilter?.Items is { Count: > 0 }) { ... predicates.Add("(" + CombineToSql(...) + ")"); }
// ⑤ 运行时条件：是树就递归编译，否则回落 items+combine
...
return new FilterSqlResult {
    WhereSql = predicates.Count == 0 ? "1=1" : string.Join(" AND ", predicates),
    Args = args
};
```

（`FilterSqlBuilder.cs:50`–`:155`）

因此：

- **租户谓词与数据范围是最终 `AND` 之前的独立谓词**，且 `CompileNode` 对多子节点组自带括号（`FilterSqlBuilder.cs:287`）⇒ **用户条件树的 OR 无法逃逸租户/数据范围**。这是树的真实安全收益。
- 用户树内 `or` 与租户 `AND` 的相对优先级由 SQL 默认优先级 + 括号保证，不依赖调用方自律。
- `runtimeFilters` 若是 `JsonObject` 且含 `filter` 键，会自动解包一层（`FilterSqlBuilder.cs:110`–`:114`）。

**递归与深度**：`CompileNode` 在 `depth > OrchestrationConsts.MaxFilterGroupDepth`（=3）时抛 `UserFriendlyException`（`FilterSqlBuilder.cs:228`；`OrchestrationConsts.cs:14`）。根节点 `depth=0`，因此**允许的嵌套深度是 4 层（含根）**，与旧文档「组最多 3 层（根不算）」不符。

**字段校验**：`left` 必须命中 `colMap`（表列，大小写不敏感），否则抛「Filter field is not allowed」（`FilterSqlBuilder.cs:300`–`:303`）。**`colMap` 是表的全部列**，不是设计器暴露给用户的列子集 —— 所以「字段白名单」的实际语义是「必须是真实物理列」。

**空值规则**：`right` 为 `null` / 空白串 / 空数组 / 全空白数组时规则被丢弃（`FilterSqlBuilder.cs:243`、`:407`–`:430`）；`isempty` / `isnotempty` 例外，走方言函数（`FilterSqlBuilder.cs:307`–`:315`）。

**运算符与类型修正**：`contains`/`notcontains`/`startswith` 在非 String/Text/Enum 列上被**静默改写为 `eq`**（`FilterSqlBuilder.cs:322`–`:327`）；`between` 是 `>= min AND < max`（左闭右开，`FilterSqlBuilder.cs:375`）；`in` 空数组 → `1=0`，`notin` 空数组 → `1=1`（`FilterSqlBuilder.cs:381`–`:384`）。

**方言**：Postgres `ILIKE`，其余 `LIKE`（`FilterSqlBuilder.cs:402`）。标识符/参数名经 `SqlDialect.QuoteIdent`/`ParamName`。

**参数命名**：租户/软删为 `tenantId`/`isDeleted`；数据范围前缀 `ds`；设计器隐藏条件 `d{item.No}`；运行时树 `rt{g0}{g1}...{a|b|i0}`；运行时编号式 `r{item.No}`。前缀冲突不会发生，因为各层前缀不同。

### 3.3 设计器数据范围 vs 运行时条件

**报表侧**（`ReportDefinitionAppService.QueryAsync`，`ReportDefinitionAppService.cs:224`–`:327`）：

1. 取已发布报表（未发布抛错，`:229`）。
2. `kind=resource` 校验资源已发布并注入 `resourceCode`；`kind=table` 校验表存在；`kind=flow` 走自定义流。
3. 注入 `reportCode`、钳制后的 `page`/`pageSize`、`sorting`（默认取报表 `DefaultSorting`）。
4. 注入 `columns` = `Visible` 列的 `Field`（`:281`）。
5. `filter = CombineFilters(entity.DataScope, input.Filter)` —— 若两侧都有内容，合成 `{kind:"group", op:"and", children:[dataScope, runtime]}`（`:438`–`:472`）。
6. `summaryFields`：请求传了就用请求的；否则取报表列里配了 `SummaryFn` 的列（`:294`–`:303`）。
7. 交 `PublishedFlowInvoker.InvokeAsync(entity.QueryFlowKey, body, triggerSource:"report:{code}", filterOutputsByRole:true, persistOnSuccess:false)`（`:305`–`:312`）。

**执行侧**（`ResourceCrudExecutor.QueryAsync`，`ResourceCrudExecutor.cs:182`–`:266`）：

```csharp
var filter = FilterSqlBuilder.Build(
    provider,
    schema.Columns,
    tableOverride || schema.Kind == QueryObjectKind.Procedure ? null : resource?.Filter,  // ← 资源级设计器过滤
    schema.Kind == QueryObjectKind.Procedure ? null : input["filter"] ?? input["filters"], // ← 运行时树
    _currentTenant.Id
);
```

- `tenantId` 取自 `ICurrentTenant.Id`，**由运行时提供，客户端无法覆盖**。
- 报表路径下 `resourceCode` 被注入 ⇒ 除非 `tableOverride`（`kind=table` 且带 `dataSourceCode+tableName`，`:101`–`:103`），**资源自身的 `Filter.DataScope` 会被再叠加一次**。因此对 `kind=resource` 报表，数据范围可能被应用两次（报表的 + 资源的）；这是幂等的语义（同一条件 AND 两次），不是缺陷，但设计器改「资源数据范围」会影响所有引用该资源的报表。
- 存储过程 / `tableOverride` 路径下**资源和设计器过滤整体被跳过**（`FilterSqlBuilder.cs` 收到 `null`）。

### 3.4 查询执行、分页与投影

- 先 `SELECT COUNT(1)` 取 `total`（`ResourceCrudExecutor.cs:228`–`:235`）。
- 再按方言分页：SQL Server / Oracle 用 `OFFSET ? ROWS FETCH NEXT ? ROWS ONLY`，其余用 `LIMIT ? OFFSET ?`（`ResourceCrudExecutor.cs:825`–`:850`）。
- 顺序：`ORDER BY` 字段必须在列内，否则回落 `CreationTime`（无则首列）；`desc` 显式识别，其余 `ASC`（`ResourceCrudExecutor.cs:807`–`:823`）。
- 投影：请求 `columns` 过滤到物理列白名单，且**强制包含 `Id`**；若过滤后为空则回落**全部列**（`ResourceCrudExecutor.cs:783`–`:805`）。`selectList` 为空时用 `*`（`ResourceCrudExecutor.cs:220`–`:222`）。
- `COUNT`/数值读取统一走 `JsonNodeNumbers.ToInt64` 等，兼容 Oracle decimal / SQL Server int。
- 读操作（`query`/`get`）受弹性重试包裹；写操作默认不重试（`ResourceCrudExecutor.cs:151`–`:163`）。

### 3.5 列管道（实际模型）

旧文档写的 `transform → format → style → render` 四段管道**在代码里不存在**。实际列展示链是**扁平的展示属性**：

| 环节 | 实际字段 | 说明 |
|------|----------|------|
| 布局 | `Width` / `Align` / `Ellipsis` / `Fixed` / `Visible` / `Sortable` | `ApplyLayoutDefaults` 按 `FormatPreset` 给出对齐/默认宽（`ResourceSchema.cs:203`–`:224`） |
| 格式化 | `FormatPreset`（`text`/`date`/`datetime`/`currency`/`number`/`percent`/`boolean`/`ellipsis`） | 字符串预设，无服务端枚举校验 |
| 码表 | `DictMap`（内联）+ `DictCode` + `Options`（`DictOption{Value,Label,Tone}`）+ 报表级 `Dictionaries` | 多列可复用同一 `NamedDict`（`ResourceSchema.cs:66`–`:73`） |
| JS 格式化 | `FormatFn`（返回文案）、`RenderFn`（注释写「返回 HTML」） | 前端执行，**未核验**；与 §1.4「不在单元格内执行任意 JS 写库」不冲突（只读），但**「不返回 HTML」的安全目标未在代码中体现** |
| 着色 | `StyleRules: ColumnStyleRule{Op, Value, Tone, Target}` | 扁平规则，不是条件树；`Target` 默认 `cell`，可 `row`；`Tone` 是自由串，默认 `default`，**无白名单** |
| 组件 | `Render`（默认 `text`） | 自由串，无 `tag/link/progress/boolean/image/dict` 白名单 |
| 汇总 | `SummaryFn` + `SummaryScope`（默认 `filtered`） | `SummaryScope` 无实现分支 |

### 3.6 按钮（`ActionDef`）—— 配置有、执行无

模型见 `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs:227`–`:272`，默认值：`Position="toolbar"`、`Scene=""`、`Scope=""`、`Kind="custom"`、`Open="drawer"`、`FieldsMode="full"`、`After="refresh"`、`BatchLimit=100`、`MultiSelect=false`。

**服务端没有 `ActionDef` 的执行端**：全仓 grep `ActionDef` 只命中模型定义与资源种子（`ResourceSchema.cs:227`、`src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/AppResource.cs:183`）。因此：

- 按钮的实际执行是**前端**拿 `flowKey` 调 `POST /api/logic/{flowKey}`（`src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/LogicApiController.cs:21`–`:24`），服务端只做「已发布逻辑 + `Orchestration.Instances.Run` 权限」校验（`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/LogicApiAppService.cs:35`）。
- `PayloadFields`、`BatchLimit`、`After`、`Confirm`/`ConfirmText`、`FieldsMode`、`Fields` 均为**纯配置**，无服务端消费者。`Confirm` 类语义完全靠前端。
- `Kind` 只影响**从资源克隆时的默认推导**（`ReportDefinition.cs:278`–`:309`：`Open`/`FormKey`/`Fixed`/`MultiSelect` 的默认值），不影响运行时。
- 没有 `acl`、`visibleWhen`、`disabledWhen`、`group`、`after` 组合语义等字段。
- `Scope` 是自由字符串，**没有** `none/row/selection/cell/column` 的枚举约束或执行分支。
- `FromResource` 时 `MultiSelect` 会被扩写：`MultiSelect || Scope=="selection" || Scene=="batch" || Position=="batch"`（`ReportDefinition.cs:305`–`:308`）。

**字段 `FieldsMode` 的写入资源**：报表本身的按钮不写库；写操作必须落到某个 `AppResource` 的 `sys.resource.create/update/delete`，或自定义流。

### 3.7 发布与现实中的菜单/权限

**发布**（`ReportDefinitionAppService.PublishAsync`，`ReportDefinitionAppService.cs:188`–`:222`）依次校验：

1. `QueryFlowKey` 对应的 `FlowDefinition` 存在且 `Status == Published`（`:192`–`:196`）。
2. `kind=resource` → 资源已发布；`kind=table` → `TableSyncState.InSync`；`kind=flow` → `QueryFlowKey` 非空。
3. 通过后 `entity.Publish()` 只把 `Status` 置为 `Published`。

**菜单**：仓库里**没有**「发布报表 → 建 `SysMenu`」的代码。已存在的是一条**隐藏的通用运行时菜单**：`AppReportRuntime`，`Component=/orchestration/reports/runtime`，`Path=/app/reports/:code`，权限 `Orchestration.Reports.Execute`，`isVisible:false`（`src/services/administration/src/Meta.Dow.Administration.Domain/Menus/MenuDataSeedContributor.cs:565`–`:580`），以及 `AppReportForm`（`/app/reports/:code/form/:formKey/:id?`，`:582`–`:597`）。菜单支持 `MenuType.Button` 且侧栏过滤 Button 节点（`src/services/administration/src/Meta.Dow.Administration.Application/Menus/MenuAppService.cs:136`）。

**权限**：只有分组权限 `Orchestration.Reports.{Default,Create,Update,Delete,Publish,Execute}`（`SaaSPermissionDefinitionProvider.cs:76`–`:84`）。**没有** `App.Report.{code}` 形式的每报表权限，也没有按钮级 `App.Report.{code}.Action.{key}`；`Export` 权限不存在。

**数据范围**（`§4.1` 详述）：`RoleDataScope` + `DataScopeResolver` 存在于 Administration（`src/services/administration/src/Meta.Dow.Administration.Application/DataPermission/DataScopeResolver.cs:20`），但**从不参与报表查询**。

---

## 4. 行为边界与已知缺陷

格式：`【严重度】现象 — 机理 — file:line — 后果/规避`。严重度：**高 / 中 / 低**。

### 4.1 【高】IAM 行级数据范围在报表上零落地

**现象**：角色配了 `RoleDataScope`（仅本人 / 本部门），进报表仍能看到全部数据。
**机理**：`DataPermissionQueryableExtensions.ApplyDataPermission` 全仓**无调用点**；扩展方法只对实现 `IHasOrganizationId` 的实体生效（`src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/DataPermissionQueryableExtensions.cs:10`–`:24`），而报表走的是**原生 SQL**，根本没有 `IQueryable`。`DataScopeResolver` 只被「我的」接口消费（`src/services/administration/src/Meta.Dow.Administration.Application/DataPermission/RoleDataScopeAppService.cs:16`、`:67`）。
**后果**：报表的 `DataScope`（`ReportDefinition.cs:31`）目前**只是设计器配的查询条件，不是权限强制**。任何「数据范围」文案若暗示权限，都是失真的。
**规避**：需要按角色限制行时，只能用设计器 `DataScope` 硬编码条件；或自定义 query 流自行加谓词。

### 4.2 【高】数据范围可被整体绕过（直连 `/api/logic/sys.resource.query`）

**现象**：持 `Orchestration.Instances.Run` 的调用者可跳过 `Reports.Execute` 与报表的 `DataScope` 取数。
**机理**：`sys.resource.query` 的入参 schema 把 `resourceCode`/`dataSourceCode`/`tableName`/`page`/`sorting`/`filter`/`filters`/`columns`/`summaryFields` **全部声明为来自调用方输入**（`src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/SystemResourceFlowSeedContributor.cs:46`–`:55`），端点只校验 `Orchestration.Instances.Run`（`LogicApiAppService.cs:35`）。
**后果**：`ReportDefinitionAppService.QueryAsync` 注入的 `dataScope`、可见列白名单、`DefaultSorting` 全部失效。租户谓词仍生效（`FilterSqlBuilder.cs:54`），因此**不跨租户**，但**跨报表权限边界**。
**规避**：把 `sys.*` 系统流排除在通用 `/api/logic/{flowKey}` 之外，或为报表查询引入独立的、绑定报表码的端点。

### 4.3 【高】`batchupdate` 实际不可用（必然 0 行）

**现象**：调 `batchupdate` 要么抛 SQL 缺参，要么返回 `affected: 0`，数据不落。
**机理**（`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ResourceCrudExecutor.cs:631`–`:673`）：

1. WHERE 子句复用 `filter.WhereSql`（`:672`），而 `filter` 由 `FilterSqlBuilder.Build(provider, table, null, null, tenantId)` 生成，含 `TenantId = @tenantId`（`:595`、`FilterSqlBuilder.cs:54`–`:60`）；但 update 分支为每条 item 新建的 `args`（`:651`）**没有 `filter.Args.DeepClone()`**（对比 delete 分支 `:689` 是并了的）⇒ 参数缺失。
2. 即使补上租户参数：`ConcurrencyStamp` 既被 `SET` 成新戳（`:669`），又被 `WHERE ... = @pConcurrencyStamp` 用**同一个新戳**比较（`:666`）⇒ 新旧值恒不等 ⇒ `affected = 0`。

**后果**：批量改值功能形同虚设；`batchcreate`（`:604`）与 `batchdelete`（`:676`）不受影响。
**规避**：当前不要调用 `batchupdate`；修复需（a）并入 `filter.Args`，（b）WHERE 用**旧**戳、SET 用**新**戳（或按 id 逐条比对）。

### 4.4 【中】列级过滤缺失：`columns` 可越界、`summaryFields` 可覆盖设计器

**现象（a）**：客户端自带 `columns` 可以取到报表设计器标为隐藏的列。
**机理**：`ResolveSelectColumns` 只校验「是物理列」（`ResourceCrudExecutor.cs:783`–`:805`）；且 `columns` 为空或全非法时**回落全部列**，`selectList` 退化为 `*`（`:220`–`:222`）。
**现象（b）**：客户端传 `summaryFields` 可以对自己无权查看的列做 `SUM/AVG`。
**机理**：`ParseSummaryFields` 一旦收到数组就**完全忽略设计器配置**（`ResourceCrudExecutor.cs:931`–`:959`），随后只按物理列 + 类型放行（`:870`–`:890`）——**没有**「该列是否 `Visible`、是否配了 `SummaryFn`」的交叉校验。
**后果**：「隐藏列」不是安全边界，只是展示约定。报表注入的 `columns`（`ReportDefinitionAppService.cs:281`）只保护走报表入口的正常前端。
**规避**：把列白名单做成服务端强校验（请求列 ⊆ 资源/报表可见列），并对 `summaryFields` 做同样的子集校验。注意：这条与 §4.2 叠加后，直连路径连「物理列」这层都不再有设计器约束。

### 4.5 【中】存储过程结果全量入内存再分页

**现象**：对 `QueryObjectKind.Procedure && !CanSelectFrom`（即不可 `SELECT` 的存储过程查询对象，`src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/TablePlatformType.cs:75`、`src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/DbQueryObject.cs:76`）的分页，在大结果集下可能 OOM/超时。
**机理**：`QueryExecAsync` 先 `QueryRoutineAsync` 取全部行，再在 C# 侧 `Skip(offset).Take(pageSize)`（`ResourceCrudExecutor.cs:282`–`:286`）。
**规避**：存储过程自身分页；或改用可 `SELECT` 的视图/表查询源。

### 4.6 【中】租户过滤静默失效（缺列无告警）

**现象**：表里没有物理 `TenantId` 列时，查询**不报错也不加租户谓词**，直接返回全租户数据。
**机理**：`if (colMap.ContainsKey("TenantId"))` 才加谓词，`else` 分支不存在；无日志、无异常（`FilterSqlBuilder.cs:54`–`:65`）。`IsDeleted` 同理（`:67`–`:71`）。
**后果**：多租户隔离**依赖表结构约定**。外部数据源自带的表、手工建的托管表若漏了 `TenantId`，等于裸奔。
**规避**：托管表/资源创建时强制校验 `TenantId`、`IsDeleted` 列存在；或在 `FilterSqlBuilder` 中缺列即抛错。

### 4.7 【中】设计器开关不生效：`AdvancedFilter` / `SearchForm` 关闭态无效

**现象**：设计师关掉「高级筛选」，用户仍可提交任意条件树。
**机理**：`FilterSqlBuilder` 的运行时分支**无条件**接受任意 group tree（`FilterSqlBuilder.cs:116`–`:123`），从不读取 `ReportDefinition.AdvancedFilter`（`ReportDefinition.cs:35`）或 `SearchForm`。二者全仓只在 `ReportDefinition`/`AppResource` 的读写与克隆中出现（`ReportDefinition.cs:114`、`:145`、`:168`；`ReportDefinitionAppService.cs:168`、`:169`、`:422`、`:423`）。
**后果**：开关是纯 UI 提示。用户可绕过设计器意图构造条件。
**规避**：如需硬约束，服务端必须校验「请求条件树的字段 ⊆ SearchForm.Fields 的字段」，并在 `AdvancedFilter=false` 时拒绝非表单来源的树。

### 4.8 【低】`ReportKind` 自由字符串与 `Kind` 归一化

**现象**：`CreateAsync`/`UpdateAsync` 传入未知 `kind` 会被静默当作 `resource`。
**机理**：`ReportKind.Normalize` 只认 `table`/`flow`，其余（含拼错）一律 `resource`（`ResourceSchema.cs:478`–`:482`）。
**后果**：拼错 `kind` 不会报错，而是走资源分支，可能报出误导性的「资源不存在」。

### 4.9 【低】报表列表分页无业务上限

**现象**：`GetListAsync` 的 `SkipCount`/`MaxResultCount` 直接来自 `PagedAndSortedResultRequestDto`（`ReportDefinitionAppService.cs:62`–`:63`）：服务端未做业务钳制，边界完全由 ABP 框架默认行为决定（**该默认值未在本仓库代码中显式固定，故不在此断言具体数字**）。这是元数据列表，风险低于数据查询；**作为对照，查询侧上限 200 是代码里真实现的**（`ResourceCrudExecutor.cs:194`–`:198`、`ReportDefinitionAppService.cs:271`），旧文档把二者混为一谈。

### 4.10 【低】列渲染契约与安全目标不一致

**现象**：`ListColumnDef.RenderFn` 的注释明写「返回 HTML」（`ResourceSchema.cs:193`–`:194`），而文档安全目标要求「不返回 HTML、组件白名单」。
**机理**：`Render` 与 `RenderFn` 均无服务端白名单或净化。
**后果**：XSS 风险完全落在前端实现上，**前端行为未核验**。服务端无法背书。

### 4.11 【低】测试覆盖为零

**现象**：`tests/Meta.Dow.Orchestration.Tests` 仅 5 个测试文件（`WorkflowDefinitionValidatorTests`、`FormSchemaFlatteningTests`、`WorkflowRecordPatchTests`、`SecretsConfigSafetyTests`、`FlowExecutorControlFlowTests`），**没有**任何针对 `FilterSqlBuilder` / `SqlDialect` / `DataScope` / 租户过滤 / `ResourceCrudExecutor` 的测试。
**后果**：§4.2–§4.7 这些缺陷**不会被现有 62 个单测捕获**；「62/62 通过」不能作为查询链路的正确性证据。

---

## 5. 规划（未实现）

> **本章全部内容均未实现**，与第 3 章严格隔离。任何 ≥ P-Report.1 的能力都不得在第 2 章标 ✅。
> 分期口径与旧文档一致，但**归位到「未实现」**。

| 能力 | 说明 | 分期 |
|------|------|------|
| **查询表单独立层** | 服务端区分「表单编译结果」与「更多条件」，并按 `SearchForm.Fields` 强制字段子集 | P-Report.1 |
| **「任一满足」卡片** | 消费 `SearchFormFieldDef.OrGroup`，把同组字段编成 `op=or` 子组 | P-Report.1 |
| **`AdvancedFilter=false` 服务端强制** | 关闭态拒绝非表单来源的条件树 | P-Report.1 |
| **列白名单服务端强校验** | 请求 `columns` ⊆ 可见列；`summaryFields` ⊆ 已配汇总列 | P-Report.1 |
| **KPI 条** | 顶部 2–4 张统计卡；现模型 `KpiDef{Title,Field,Fn}` 需先扩 `filters` | P-Report.1 |
| **快捷筛选芯片** | 消费 `Presets: NamedFilterPreset[]` | P-Report.1 |
| **导出** | 当前筛选 Excel，列与可见列一致，上限（如 1 万行）异步 | P-Report.1 |
| **发布向导（菜单 + 权限）** | 每报表登记 `SysMenu`、生成 `App.Report.{code}` 与按钮级权限 | P-Report.1 |
| **`batchupdate` 修复** | 见 §4.3 | P-Report.1（**缺陷修复**） |
| **分组小计** | `GROUP BY` 后的聚合；现 `SummaryScope` 只有 `filtered` 有实现 | P-Report.2 |
| **列 `transform` 管道** | `dict`/`enumMap`/`unit`/`datePart` 结构化模型（当前只有 `FormatPreset`+`DictMap`） | P-Report.2 |
| **`styleRules` 树 + `tone` 白名单** | 现为扁平规则、自由 `tone` | P-Report.2 |
| **`scope` 枚举 + 单列白名单弹窗** | `cell`/`column` scope、`fieldsMode=whitelist` 的服务端语义 | P-Report.2 |
| **`ActionDef` 服务端执行端** | 消费 `PayloadFields`/`BatchLimit`/`After`；按钮 `acl` | P-Report.2 |
| **钻取 / 主从 / 图表+表** | `link` 到另一报表并带 filter；子表；简易柱折（`groups[]`） | P-Report.2 |
| **保存我的视图** | 用户列宽/显隐/默认筛选 | P-Report.2 |
| **自定义列 JS 沙箱** | 受控 `render` 函数返回描述符（非 HTML），规则与 board 文档 5.11 同源 | P-Report.3 |
| **订阅** | 定时把当前筛选导出发邮件 | 后置 |
| **打印/套打** | 对标积木报表 | **不做**（见 §1.4） |
| **IAM 行级数据范围接入报表** | 见 §4.1 | 未排期（**安全项，建议提前**） |

KPI 示例（**规划**）：`{ "title": "待审金额", "field": "Amount", "fn": "sum", "filters": <待审 FilterGroup> }`。

### 5.1 分期与评审要点（保留原文论证）

**R1 须先改现有资源列表筛选**，否则报表与资源两套交互。编排 IF 节点不强迫改。

**评审清单（开放问题，需拍板）**：

1. 运行时筛选是否同意 **查询表单为主、条件组为辅**，公式仅留在编排？
   —— 现状提醒：后端**两者已收敛为同一棵 `FilterNode` 树**，「表单为主」是前端约束，不是服务端约束（§3.2）。
2. 「任一满足」是否只出现在：表单里的或组卡片、更多条件的组头切换？
   —— 现状提醒：`SearchFormFieldDef.OrGroup` 已建模但**无消费点**（§2）。
3. 合计默认 `filtered`（当前筛选全量）是否可接受多一次聚合 SQL？
   —— 现状：该实现**已存在**（§3.5 汇总行），代价是每次查询多一条聚合 SQL。
4. 报表权限独立 vs 复用资源权限，默认选哪个？
   —— 现状提醒：**两条都未实现**；现在只有分组权限（§3.7）。
5. R1 是否同步改掉现 `runtime.vue` 的 combine 输入框？
   —— 现状提醒：`vue-demo` **未核验**；后端兼容路径仍在（§1.3），前端改与不改，服务端都能跑。

**建议提前的安全项**（本版新增，见 §4.1/§4.2）：IAM 行级数据范围接入报表、堵住 `/api/logic/sys.resource.query` 的直连旁路。这两条不该等到 P-Report.2/3。

---

## 6. 运行时页面结构（前端部分 ⚠️ 未核验）

> 本仓库不含前端。以下**只是期望的页面结构与已确认的服务端支撑点**，不代表 `vue-demo` 的实现现状。凡标注 ⚠️ 的均**未核验**。

| 区域 | 期望 | 服务端支撑 |
|------|------|------------|
| 页头（标题/说明/收藏） | ⚠️ 未核验 | `ReportDefinitionDto.Name/Description` |
| KPI 条 | ⚠️ 未核验 | ❌ 无（`Kpis` 无消费点） |
| 查询表单 + 快捷芯片 | ⚠️ 未核验 | ⚠️ `SearchForm` 只回读；`Presets` 无消费点 |
| 工具栏 / 批量按钮 / 列设置 / 导出 | ⚠️ 未核验 | ⚠️ `Actions` 只回读；导出无实现 |
| 表格（多选 / 着色 / Tag / 链接 / 行按钮） | ⚠️ 未核验 | ⚠️ `StyleRules`/`Render` 为自由配置，无白名单 |
| 表尾筛选合计行 | ⚠️ 未核验 | ✅ `summary`（`{_count, Field:{fn}}`） |
| 分页 | ⚠️ 未核验 | ✅ `page`/`pageSize`（≤200）、`total` |
| 「更多条件」Drawer | ⚠️ 未核验 | ⚠️ 服务端接受任意树，无开关约束（§4.7） |
| 弹窗/抽屉表单（按钮 open） | ⚠️ 未核验 | ⚠️ 前端按 `flowKey` 自调 `/api/logic/{flowKey}` |

**已确认的服务端路由**：

- 报表管理：`api/orchestration/reports`（列表 `GET`、`GET {id}`、`GET by-code/{code}`、`GET published/{code}`、`POST`、`POST from-resource`、`POST from-table`、`PUT {id}`、`DELETE {id}`、`POST {id}/publish`、`POST {code}/query`）—— `src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/AppBuilderControllers.cs:167`–`:222`。
- 逻辑 API：`POST /api/logic/{flowKey}` —— `src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/LogicApiController.cs:11`、`:23`。
- 运行时页面菜单：`/app/reports/:code` → `Component=/orchestration/reports/runtime`（隐藏，权限 `Orchestration.Reports.Execute`）—— `MenuDataSeedContributor.cs:565`–`:580`。

**报表查询入参契约（实际）**：`ResourceRuntimeQueryInput{ Page=1, PageSize=20, Sorting?, Filters?, Filter?, SummaryFields[] }`（`src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Orchestration/AppBuilderDtos.cs:204`–`:224`）。报表服务在其上**追加** `resourceCode`/`dataSourceCode`/`tableName`/`reportCode`/`columns`/`summaryFields`/`filter`（合成树），并把 `page`/`pageSize` 钳到 `[1,200]`（`ReportDefinitionAppService.cs:250`–`:303`）。**出参**：`{ items, total, summary? }` —— **没有 `groups`**（`SystemResourceFlowSeedContributor.cs:72`–`:85`）。

**重要更正**：旧文档「查询入参」示例里的 `"columns": [...]` 由**运行时注入、用户不可改**这一条**只在报表入口成立**；直连 `/api/logic/sys.resource.query` 时 `columns` 完全由调用方决定（§4.2）。

---

## 7. 参考链接

### 7.1 仓库内

- [低代码表单与工作流看板](./data-collection-contract.md)
- [报表配置器 UX](./report-config-ux.md)
- [IAM 角色、菜单与权限](./identity-contract.md)

### 7.2 开源对标

- NocoBase — https://github.com/nocobase/nocobase
- NocoDB — https://github.com/nocodb/nocodb
- Amis — https://github.com/baidu/amis
- Metabase — https://github.com/metabase/metabase
- JimuReport — https://github.com/jeecgboot/jimureport

---

## 8. 修订记录

| 日期 | 代码基线 | 说明 |
|------|----------|------|
| 2026-10-13 | `267d375` | **重写为「事实契约 + 规划」双段式**。核对全部「已实现/支持/字段表/默认值」类断言；删除 27 条与代码矛盾的失真断言（见 §8.1）；删除纯重复段落（目录、混写章节）；补齐 10 条新发现（见 §8.2）。注：原文**不含** `file:///` 绝对链接，全部引用本就是仓库内相对路径。 |
| 2026-09-21 | — | 落地 R1（旧记录，按当时状态，若干断言与 `267d375` 代码不符，已在本版改正） |
| 2026-09-21 | — | 初稿（旧记录，同上） |

### 8.1 本版（2026-10-13）删除/改写的失真断言清单

| # | 原断言（失真） | 代码真相 | 证据 `file:line` |
|---|----------------|----------|------------------|
| 1 | 文档性质为「产品与架构规划」，但正文混入「R1 已落地」 | 已拆为「事实契约（§1–§4、§6）+ 规划（§5）」 | 本文 §2/§5 分界 |
| 2 | 「`filter` 为已经 AND 好的用户层树（表单 + 更多条件 + 芯片）」 | 报表侧 `filter` = `{and, [dataScope, 用户树]}`，**不含**表单/芯片独立层 | `ReportDefinitionAppService.cs:438`–`:472` |
| 3 | `SearchForm` 是独立编译层，`emptyPolicy`/`visibleWhen`/`dictCode`/`span 1–4 默认三列` | 模型只有 `Key/Field/Title/Op/Control/Span/Placeholder/OrGroup`；`Span` 默认 1；`Columns` 默认 3；**无服务端消费点** | `ResourceSchema.cs:37`–`:83` |
| 4 | 「组最多 3 层（根不算）」 | `MaxFilterGroupDepth=3` + 判据 `depth > 3` ⇒ 实际 4 层（含根） | `FilterSqlBuilder.cs:228`；`OrchestrationConsts.cs:14` |
| 5 | 「字段必须在报表允许列表内」 | 校验对象是**表的全部列** `colMap`，非暴露列子集 | `FilterSqlBuilder.cs:52`、`:300` |
| 6 | 列管道 `transform → format → style → render`，含 `transform{type:dict/enumMap/unit/datePart/expr}` | **无 `transform` 模型**；实际是 `FormatPreset`/`DictMap`/`DictCode`/`FormatFn`/`RenderFn`/`StyleRules` 扁平属性 | `ResourceSchema.cs:157`–`:224` |
| 7 | `styleRules[].when` 是条件树、`cell/row.tone`、`tone` 白名单 | `ColumnStyleRule{Op,Value,Tone,Target}` 扁平规则；`Tone` 自由串无白名单 | `ResourceSchema.cs:146`–`:156` |
| 8 | `render` 组件白名单（`tag`/`link`/`progress`/…） | `Render` 为自由字符串，默认 `text`，无白名单 | `ResourceSchema.cs:181` |
| 9 | `RenderFn` 「不返回 HTML」 | 代码注释明写「返回 HTML」 | `ResourceSchema.cs:193`–`:194` |
| 10 | 汇总 `scope` = `page`/`filtered`/`group` | 只有 `filtered` 语义有实现；`SummaryScope` 无分支 | `ResourceCrudExecutor.cs:852`–`:928`；`ResourceSchema.cs:200` |
| 11 | `fn` 白名单含 `countDistinct` | `NormalizeSummaryFn` 无 `countDistinct`，未知值静默回落 `sum` | `ResourceCrudExecutor.cs:964`–`:972` |
| 12 | 汇总「禁止把 SUM 扫进行数据在前端加」且列级受控 | 聚合 SQL 确实在库侧；但请求 `summaryFields` **覆盖**设计器且无可见列交叉校验 | `ResourceCrudExecutor.cs:906`、`:931`–`:959` |
| 13 | `ActionDef` 字段表含 `acl`/`visibleWhen`/`disabledWhen`/`payload`/`after` 组合/`group` | 这些字段**不存在**；实际字段见 §3.6 | `ResourceSchema.cs:227`–`:272` |
| 14 | 「真正执行只看 `open + formRef + flowKey`」 | 服务端**无 ActionDef 执行端**；前端调 `/api/logic/{flowKey}` | 全仓 grep 仅 `ResourceSchema.cs:227`、`src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/AppResource.cs:173`–`:184`；`LogicApiController.cs:23` |
| 15 | `batchLimit` 默认 100 且生效 | 默认 100 属实，但**无运行时消费** | `ResourceSchema.cs:265` |
| 16 | `scope` 枚举 `none/row/selection/cell/column` 有语义 | `Scope` 是自由字符串，无枚举约束、无执行分支 | `ResourceSchema.cs:237` |
| 17 | 「批量走 `sys.resource.batchUpdate`」 | 实际操作名是 `batchupdate`，且**不可用**（§4.3） | `ResourceCrudExecutor.cs:172`、`:631`–`:673` |
| 18 | 聚合含 `summaryBar` / `forms[]` / `menu{}` / `selection{enabled,preserveOnPage}` / `schemaVersion` | 均**不存在**；实际是 `Form`（单个）、`SelectionEnabled`(bool)、`Status` | `ReportDefinition.cs:11`–`:55` |
| 19 | `KpiDef` 含 `filters` | `KpiDef{Title,Field,Fn}` | `ResourceSchema.cs:461`–`:468` |
| 20 | 发布向导「写入权限定义并绑定到菜单 `Permission`」，权限树 `App.Report.{code}` / `.Export` / `.Action.{key}` | **无每报表权限生成**；只有分组权限；通用运行时菜单是种子 | `SaaSPermissionDefinitionProvider.cs:76`–`:84`；`MenuDataSeedContributor.cs:565` |
| 21 | §8.3「行级仍走 RoleDataScope + 查询层注入」 | `ApplyDataPermission` **无调用点**；报表走原生 SQL，`RoleDataScope` 不参与 | `DataPermissionQueryableExtensions.cs:10`；`RoleDataScopeAppService.cs:67` |
| 22 | §11「标识符/字段白名单…列白名单」暗示列级受控 | 请求 `columns` 只校验物理列，空则回落全部列 | `ResourceCrudExecutor.cs:783`–`:805`、`:220`–`:222` |
| 23 | §12「R1 已落地：查询表单 + 可视化条件组替换公式框」 | 后端**两套并存**（树优先 + `items/combine` 兼容）；前端未核验 | `FilterSqlBuilder.cs:116`–`:148` |
| 24 | 目录中「4. 查询：表单 + 可视化条件组」把产品界面当实现 | 已改为按代码写实的「查询编译」章节 | 本文 §3.2/§3.3 |
| 25 | 「`kind=resource` 只注入 `resourceCode`」 | 还注入 `reportCode`/`columns`/`sorting`/`summaryFields`/`filter`/`page`/`pageSize` | `ReportDefinitionAppService.cs:250`–`:303` |
| 26 | 出参含 `groups?` | 契约与种子均无 `groups` | `SystemResourceFlowSeedContributor.cs:72`–`:85` |
| 27 | 顶层「目录」与「## 14 评审清单」造成重复陈述 | 删除目录（章节固定，收益低）；**评审清单的开放问题原样保留**并迁入 §5.1，另加「现状提醒」以免问题本身暗含已实现假设 | 本文 §5.1 |

### 8.2 新发现（原文档未提及）

| # | 发现 | 严重度 | 证据 |
|---|------|--------|------|
| N1 | **数据范围可整体绕过**：持 `Instances.Run` 直连 `POST /api/logic/sys.resource.query`，自带 `resourceCode`/`filter`/`columns`/`summaryFields` | 高 | `SystemResourceFlowSeedContributor.cs:46`–`:55`；`LogicApiAppService.cs:35` |
| N2 | **`batchupdate` 双重缺陷**（缺 `filter.Args` + 新旧戳同值）⇒ 恒 0 行 | 高 | `ResourceCrudExecutor.cs:651`、`:666`–`:669` vs `:689` |
| N3 | **租户过滤静默失效**：缺物理 `TenantId` 列时不加谓词、无日志无异常 | 中 | `FilterSqlBuilder.cs:54`–`:65` |
| N4 | **`AdvancedFilter`/`SearchForm` 关闭态不被服务端执行** | 中 | `FilterSqlBuilder.cs:116`–`:123` |
| N5 | **数据范围可能被应用两次**（报表 `DataScope` + 资源 `Filter.DataScope`） | 低（语义幂等） | `ReportDefinitionAppService.cs:283`；`ResourceCrudExecutor.cs:209` |
| N6 | **存储过程/`tableOverride` 路径跳过全部设计器过滤** | 中 | `ResourceCrudExecutor.cs:209`、`:101`–`:103` |
| N7 | 存储过程结果全量入内存再分页 | 中 | `ResourceCrudExecutor.cs:282`–`:286` |
| N8 | `contains`/`startswith` 在非文本列被**静默改写为 `eq`**（不报错） | 低 | `FilterSqlBuilder.cs:322`–`:327` |
| N9 | 查询链路（`FilterSqlBuilder`/`SqlDialect`/`DataScope`/`ResourceCrudExecutor`）**零单测** | 低 | `tests/Meta.Dow.Orchestration.Tests/`（5 个文件） |
| N10 | 已存在通用运行时菜单 `AppReportRuntime`（隐藏），但**没有**每报表菜单登记 | — | `MenuDataSeedContributor.cs:565`–`:597` |

### 8.3 抽查结果（本次核对）

| 抽查项 | 方法 | 结果 |
|--------|------|------|
| 代码基线 `267d375` | `git log -1` / `git rev-parse --short HEAD` | ✅ 一致；`docs/report-contract.md` 无未提交改动 |
| 条件树递归 + 括号 | 精读 `FilterSqlBuilder.CompileNode` | ✅ 多子节点 `"(" + join + ")"`（`:287`） |
| 租户/数据范围不可被 OR 逃逸 | 精读 `Build` 的 `predicates` + `string.Join(" AND ")` | ✅ 独立谓词（`:152`） |
| 深度上限 | 精读 `:228` + `OrchestrationConsts.cs:14` | ⚠️ 实际 4 层，文档原写 3 层（已改正） |
| `ActionDef` 无执行端 | 全仓 grep `ActionDef` | ✅ 仅模型 + 资源种子 |
| `Kpis`/`Presets`/`SearchForm`/`AdvancedFilter` 无消费 | 全仓 grep 各属性名 | ✅ 仅定义与 CRUD 读写 |
| IAM 行级范围零落地 | 全仓 grep `ApplyDataPermission`/`DataScopeResolver` | ✅ 扩展方法无调用点；解析器仅「我的」接口 |
| `batchupdate` 不可用 | 精读 `:631`–`:673` 并与 delete 分支对比 | ✅ 两条缺陷均确认 |
| 列级过滤缺失 | 精读 `ResolveSelectColumns` + `ParseSummaryFields` | ✅ `columns` 越界、`summaryFields` 覆盖设计器 |
| 发布向导无菜单/权限生成 | 全仓 grep `SysMenu`/`App.Report` | ✅ 只有种子菜单与分组权限 |
| 测试零覆盖 | 列 `tests/Meta.Dow.Orchestration.Tests/*.cs` | ✅ 5 个文件，无查询链路测试 |
| 引用路径可解析 | glob 各被引 `file:line` | ✅ 全部存在（`docs/` 内相对链接指向同目录文件） |
