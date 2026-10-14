> **功能描述**见 [../data-collection.md](../data-collection.md)。本文件是该功能的证据附录（契约 / 评审 / 方案），不单独作为阅读入口。

# Meta.Dow 资源 / 表设计 / 查询对象 / 表单 / 审批 / 报表 / 看板：事实契约与规划

> **文档性质（混合，已按章拆开）**
>
> | 章 | 性质 | 说明 |
> |----|------|------|
> | 第 1 章 | 边界论证（长期有效） | 定位、分层、明确不做 |
> | **第 2 章** | **事实契约（唯一权威表）** | 能力 / 状态 / 证据 `file:line` |
> | 第 3 章 | 事实契约 | 字段表、语义、默认值、上限，全部按代码写实 |
> | 第 4 章 | 事实契约 | 行为边界与已知缺陷（含严重度） |
> | **第 5 章** | **规划（未实现）** | 与第 3 章严格隔离：凡未落地一律只出现在此章 |
> | 第 6 章 | 操作说明 / 交互规格 | 真实路由表、权限矩阵、配置态与使用态流程 |
> | 第 7 章 | 参考链接与对标 | 开源对标表 + 外部链接 |
> | 第 8 章 | 修订记录 | 基线 + 改了什么 + 删了哪些与代码矛盾的断言 |
>
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）。实测 **Release 构建 0 错误**、**单测 62/62 通过**（`tests/Meta.Dow.Orchestration.Tests`）。
>
> **核对方式**：第 2/3/4 章每条「已实现 / 支持 / 默认值 / 字段表」类断言均附 `相对路径:行号`。凡在本仓查不到实现的，一律进第 5 章（规划）或第 4 章（缺陷），**不标 ✅**。
>
> **图例**：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划） ｜ 🐞 已知缺陷
>
> **未核验范围**：前端在仓库外的 `D:\Project\vue-demo`（web-antd）。凡涉及前端渲染、交互、拖拽、时间轴的行为，本文标 `⚠️ 未核验（前端在 vue-demo）`，只写后端契约。

---

## 文件索引（本文所有 `简写:行号` 的解析表）

仓库根为 `D:\Project\demo-microservice`，下列路径均为仓库相对路径。

| 简写 | 相对路径 |
|------|----------|
| `AppBuilderControllers.cs` | `src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/AppBuilderControllers.cs` |
| `LogicApiController.cs` | `src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/LogicApiController.cs` |
| `ResourceCrudExecutor.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ResourceCrudExecutor.cs` |
| `FilterSqlBuilder.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FilterSqlBuilder.cs` |
| `SqlDdlExecutor.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/SqlDdlExecutor.cs` |
| `SqlDialect.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/SqlDialect.cs` |
| `SqlCatalogInspector.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/SqlCatalogInspector.cs` |
| `TableDefinitionAppService.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/TableDefinitionAppService.cs` |
| `AppResourceAppService.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/AppResourceAppService.cs` |
| `DbQueryObjectAppService.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/DbQueryObjectAppService.cs` |
| `ReportDefinitionAppService.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ReportDefinitionAppService.cs` |
| `FormDefinitionAppService.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FormDefinitionAppService.cs` |
| `WorkflowDefinitionValidator.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/WorkflowDefinitionValidator.cs` |
| `WorkflowRuntimeAppService.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/WorkflowRuntimeAppService.cs` |
| `WorkflowOrgResolver.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/WorkflowOrgResolver.cs` |
| `FlowExecutor.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/FlowExecutor.cs` |
| `LogicApiAppService.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/LogicApiAppService.cs` |
| `ParameterizedSqlExecutor.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ParameterizedSqlExecutor.cs` |
| `SystemResourceFlowSeedContributor.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/SystemResourceFlowSeedContributor.cs` |
| `AppResource.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/AppResource.cs` |
| `TableDefinition.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/TableDefinition.cs` |
| `DbQueryObject.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/DbQueryObject.cs` |
| `ReportDefinition.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/ReportDefinition.cs` |
| `FormDefinition.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/FormDefinition.cs` |
| `WorkflowDefinition.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/WorkflowDefinition.cs` |
| `ResourceSchema.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/ResourceSchema.cs` |
| `TablePlatformType.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/TablePlatformType.cs` |
| `OrchestrationConsts.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/OrchestrationConsts.cs` |
| `OrchestrationPermissions.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Permissions/OrchestrationPermissions.cs` |
| `AppBuilderDtos.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Orchestration/AppBuilderDtos.cs` |
| `MenuDataSeedContributor.cs` | `src/services/administration/src/Meta.Dow.Administration.Domain/Menus/MenuDataSeedContributor.cs` |
| `SaaSPermissionDefinitionProvider.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Permissions/SaaSPermissionDefinitionProvider.cs` |
| `DataSource.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/DataSource.cs` |
| `DataSourceProvider.cs` | `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/DataSourceProvider.cs` |
| `ConditionCombineEvaluator.cs` | `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ConditionCombineEvaluator.cs` |

---

## 目录

1. [定位与边界](#1-定位与边界)
2. [现状能力总表（唯一权威表）](#2-现状能力总表唯一权威表)
3. [契约与实现细节](#3-契约与实现细节)
4. [行为边界与已知缺陷](#4-行为边界与已知缺陷)
5. [规划（未实现）](#5-规划未实现)
6. [操作说明 / 交互规格](#6-操作说明--交互规格)
7. [参考链接与开源对标](#7-参考链接与开源对标)
8. [修订记录](#8-修订记录)

---

## 1 定位与边界

### 1.1 本文覆盖的能力与文档分工

| 能力 | 主文档 | 解决什么 | 典型对象 |
|------|--------|----------|----------|
| **D. 逻辑编排** | [`logic-orchestration-contract.md`](./logic-orchestration-contract.md) | 无阻塞业务逻辑：校验、算价、调 API、SQL、消息 | Start / IF / HTTP / Code / Sql |
| **A. 表单与资源页** | **本文** | 表结构、筛查、列表、新建/编辑表单 | TableDefinition / AppResource / ListView / FormDef / FilterDef |
| **A′. 只读查询对象** | **本文** | 视图 / 存储过程登记与列表化 | DbQueryObject |
| **A″. 通用报表** | **本文 + [`report-contract.md`](./report-contract.md)** | 查询表单、条件树、汇总、列展示 | ReportDefinition / FilterNode / KpiDef |
| **B. 人审工作流（OA）** | **本文 + [`oa-contract.md`](./oa-contract.md)** | 待办、会签/或签、驳回、转办、抄送 | WorkflowDefinition / WorkflowInstance / WorkflowTask |
| **E. 看板/仪表** | **本文** | 列/泳道/卡片规则、多视图、轻量仪表 | Board / ColumnRule / Widget |

三者可互相调用，但**模型与运行时分离**：

```
[资源页：查询表单 / 列表 / 表单 / 报表]
        │ 查询翻页 / 提交 / 删除 / 自定义按钮
        ▼
[逻辑编排 FlowKey]  ←→  PublishedFlowInvoker（与 POST /api/logic/{flowKey} 同源）
        │                 简单 CRUD = 系统逻辑 sys.resource.*
        │                 复杂 = 用户已发布 flow（可 SubFlow 调用资源节点）
        ▼（可选）
[OA 审批流实例]  ←→  人审、待办、阻塞，可再调 FlowKey 做前后置逻辑
```

**禁止**把人审画进逻辑编排画布（与编排文档「不做 BPM」一致）；工作流有独立设计器与运行时。

### 1.2 三分离

| 能力 | 产品入口 | 设计器 | 说明 |
|------|----------|--------|------|
| **逻辑编排** | 「流程定义」 | DSL 画布 | 已落地；查询/写入的唯一执行入口（`PublishedFlowInvoker`） |
| **资源 / 报表 / 表单库** | 「表/视图/存储过程」「资源」「报表」「表单库」 | 配置式面板（`/orchestration/*/editor`） | Schema 可被资源/报表/`FormRef` 复用 |
| **OA 人审** | 「OA 审批流」 | 审批树（`/orchestration/workflows/designer`） | 节点可挂 FlowKey，**不改编排引擎** |

前端交互对齐 Ant Design [表单页](https://ant.design/docs/spec/research-form-cn)、[详情页](https://ant.design/docs/spec/detail-page-cn)：设计器顶栏切换新增/编辑/详情预览。⚠️ 未核验（前端在 vue-demo）。

### 1.3 分层原则

| 层 | 管什么 | 不负责 |
|----|--------|--------|
| **表设计器** | 物理结构：列类型、主键、索引、同步 DDL | 不做人机交互、不算业务规则 |
| **资源 / 列表 / 表单** | 给人看、给人填：筛选项、列展示、按钮、字段只读 | 不写任意 SQL、不做审批 |
| **逻辑编排** | **所有查询与写入的运行时**；系统 CRUD 逻辑 + 用户自定义流 | 不做人审；不在画布里跑 DDL |

**反模式（勿做）**：

- 不要做成「每个列表都强迫实施手动画查询流」——系统逻辑 `sys.resource.*` 已经是编排。
- 不要做成「列表页内嵌任意 SQL / JS」——会绕开实例日志、`txMode`、`sys.*`、`visibleTo`。
- 不要另开 WriteService 旁路——审计/可见性/事务语义只有一套。
- 结构 vs 展示必须拆开：**改列类型走表设计器；改「这一列怎么显示」走 ListView。** 否则一次「隐藏金额列」会被误做成 DROP COLUMN。

### 1.4 与「纯逻辑编排入参表单」的区别

| | 资源表单 / 表单库（本文） | 编排 InputSchema（已有） |
|--|--------------------------|--------------------------|
| 目的 | 持久化业务数据 CRUD、审批单据 | 调用 Flow 的试跑 / 系统 API 入参 |
| 数据落点 | DataSource 表 / 视图 / 存储过程 | 编排上下文 vars |
| 列表/看板 | 有 | 无 |
| 对象 | `FormDefinition`（可复用表单库）、`AppResource.Form` | `FlowDslInput` |

二者可共享控件与条件模型，**定义入口分开**（菜单「表单库」vs「流程定义」），避免一个「表单」概念两用。

### 1.5 明确不做什么（首期，仍然有效）

- 不替换现有逻辑编排为审批流。
- 不做连接器市场式「百种 SaaS」。
- 不做完整 Retool 自由画布 App。
- 不做跨库分布式事务（与编排一致）。
- 不首期上完整 BPMN 引擎与复杂 DMN。
- 不首期做多人实时协同编辑设计器。
- **不做通用 DBA 设计器**：不提供 CREATE/ALTER VIEW、CREATE/ALTER PROCEDURE、触发器、分区。已有视图和存储过程可以登记为只读查询目录再生成查询资源（**已落地**，见 §3.7）。
- **不在列表单元格渲染任意 HTML / 任意 Vue SFC**。
- **不把 ALTER TABLE 画进业务编排画布**：表设计器走独立 `ISqlDdlExecutor`，与业务 Flow 分离。
- **不另开 WriteService 旁路**。
- **不强迫实施为每张表手动画查询流**。
- Redis / Mongo 不做表设计器与通用列表（表设计器与 DDL 仅 SQL 族）。

> ⚠️ 与「不做通用 DBA 设计器」相冲突的现状：**设计器删除列/改类型/改列名会真的下发破坏性 DDL**（见 §4-C3），这已超出「不做 DBA 设计器」的边界。

---

## 2 现状能力总表（唯一权威表）

> 本表是本文**唯一**的权威能力清单。第 3 章展开契约，第 5 章只写本表中标 ❌ 的内容。凡本表标 ✅/⚠️ 者，第 5 章不得重复描述为「规划中」。

### 2.1 表设计器与物理结构

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 1 | `TableDefinition` 元数据落 Mongo（`FullAuditedAggregateRoot` + `IMultiTenant`） | ✅ | `TableDefinition.cs:62` | 与 DataSource 同库 |
| 2 | 新建托管表默认带 10 个 ABP 约定列 | ✅ | `TableDefinition.cs:103`、`TableDefinition.cs:280-295` | `Origin=convention` |
| 3 | 默认 3 个索引（PK / IX_*_Tenant_Deleted / IX_*_Tenant_Creation） | ✅ | `TableDefinition.cs:104`、`TableDefinition.cs:297-331` | 名称由表名 sanitize 派生 |
| 4 | 约定列**不可删**（名字必须存在） | ✅ | `TableDefinition.cs:221-228` | 抛 `ConventionColumnRequired` |
| 5 | 约定列**不可改类型 / 可空性** | ❌ | `TableDefinition.cs:175-231` 只校验名字 | 只校验名字与类型合法性，**不改类型这一约束不存在**；见 §4-C10 |
| 6 | DDL 预览（`GET {id}/preview`） | ✅ | `AppBuilderControllers.cs:39-42`、`TableDefinitionAppService.cs:121-138` | 权限 `Orchestration.Tables` |
| 7 | DDL 应用（`POST {id}/apply`，单事务按序执行） | ✅ | `AppBuilderControllers.cs:44-46`、`TableDefinitionAppService.cs:140-153`、`SqlDdlExecutor.cs:242-291` | 权限 `Orchestration.Tables.Ddl` |
| 8 | 首次应用 = CREATE TABLE + CREATE INDEX | ✅ | `SqlDdlExecutor.cs:86-99` | 物理列数为 0 时 |
| 9 | 增量 ALTER（addColumn / alterColumn / renameColumn） | ✅ | `SqlDdlExecutor.cs:130-189` | 见 §4-C3 门禁缺失 |
| 10 | 破坏性 DDL（dropColumn / dropIndex / 列改短改类型改非空） | 🐞 | `SqlDdlExecutor.cs:191-204`、`SqlDdlExecutor.cs:198-204` | **会生成并执行**；无 `allowDestructiveDdl`、无打字确认 |
| 11 | 阻断 DROP TABLE / TRUNCATE | ✅ | `SqlDdlExecutor.cs:254-257`、`SqlDdlExecutor.cs:505-513` | 只允许 `CREATE`/`ALTER`/`DROP INDEX`/`sp_rename COLUMN` |
| 12 | 只读 DataSource 禁止 DDL/预览 | ✅ | `SqlDdlExecutor.cs:79-82` | 走 `DataSource.AllowsWrite()` |
| 13 | 物理结构反向读取（introspect） | ✅ | `SqlDdlExecutor.cs:293-360`、`SqlCatalogInspector.cs:407-480` | postgres / mysql / sqlserver / oracle |
| 14 | 「导入表 imported」作为一等来源（不强制约定列） | ❌ | `TableDefinition.cs:101` 固定 `Origin="managed"` | 无 imported 分支 |
| 15 | 系统表黑名单（`pg_*` / `information_schema` / 表名黑名单） | ❌ | 仅 `SqlIdentifier.IsValid` | 无黑名单；`pg_class` 之类名字可通过 |
| 16 | `syncState` 全状态机 | ⚠️ | `TablePlatformType.cs:45-52` 定义 5 态；实际只写 `draft`/`inSync`/`localAhead` | `remoteAhead`/`conflict` 无写入点 |
| 17 | 删除表元数据 | ⚠️ | `TableDefinitionAppService.cs:108-119` | 有资源引用检查（**先查后删，非原子**），但**不删物理表** |
| 18 | 列 `Unique` 标志生成唯一约束 | ❌ | `SqlDialect.cs:158-191` 未读取 `col.Unique` | 只能靠索引 `Unique=true`；见 §4-C11 |
| 19 | 列 `Default` 支持任意字面量 / 白名单函数 | ⚠️ | `SqlDialect.cs:128-156` | 只硬编码两种：Id guid → `gen_random_uuid()`/`NEWID()`；boolean 且 `default=false` → `FALSE`/`0`；其余返回 `null` |
| 20 | 列注释下发 DDL | ⚠️ | `SqlDialect.cs:158-191` 无 COMMENT | 注释仅在 MySQL 参与 diff（`SqlDialect.cs:375-380`），其它库改注释不产生 DDL |

### 2.2 资源与运行时 CRUD

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 21 | `AppResource` 元数据（绑定 DS + 表/对象 + 5 个 flowKey + Filter/ListView/Form） | ✅ | `AppResource.cs:11-53` | 无独立 Field 实体，字段字典由 ListView/Form 承担 |
| 22 | 一键创建资源（`POST {id}/resources`） | ✅ | `AppBuilderControllers.cs:48-51`、`TableDefinitionAppService.cs:155-182` | 需 `Tables.SyncState == inSync`（`:160-163`）；权限 `Orchestration.Resources.Create` |
| 23 | 一键创建时按表派生 ListView 列 + 5 个 ActionDef + Form + 查询表单 | ✅ | `AppResource.cs:123-227` | 见 §3.4 逐项 |
| 24 | 视图/存储过程一键转资源 | ✅ | `DbQueryObjectAppService.cs:218-263`、`AppResource.cs:229-339` | 过程资源只给 `query` |
| 25 | 发布资源前校验 5 个 flowKey 均已发布 | ✅ | `AppResourceAppService.cs:279-297` | 不校验 query End 是否含 `items`/`total` |
| 26 | 资源运行时 REST：query / get / create / update / delete / batch-delete | ✅ | `AppBuilderControllers.cs:134-162` | 权限 `Orchestration.Resources.Execute` |
| 27 | 编排节点 `ResourceQuery/Get/Create/Update/Delete` + 4 个批量别名 | ✅ | `FlowExecutor.cs:775-786`、`FlowExecutor.cs:1450-1461` | 与 REST 同源（`ResourceCrudExecutor`） |
| 28 | 系统逻辑种子 `sys.resource.*`（5 条，已发布、`isSystem`、`isReusable`） | ✅ | `SystemResourceFlowSeedContributor.cs:38-194` | 见 §3.12 |
| 29 | 写操作强制权限 `Orchestration.Sql.Write` | ✅ | `ResourceCrudExecutor.cs:537-543`；调用点 `:331`/`:407`/`:507`/`:570` | 读操作无此校验 |
| 30 | 写操作受 DataSource `accessMode` 约束 | ✅ | `ParameterizedSqlExecutor.cs:286-294` | 由 `AllowsOp(op)` 判定 |
| 31 | 标识符白名单 + 统一引用（`QuoteIdent` + `IsQuotable`） | ✅ | `SqlDialect.cs:34-48`、`TablePlatformType.cs:174-197` | 拒引号/点/空白；长度 ≤ 128 |
| 32 | 排序列白名单 | ✅ | `ResourceCrudExecutor.cs:807-823` | 不在列集合内回落 `CreationTime desc` |
| 33 | 租户谓词 + 软删谓词自动 AND | ✅ | `FilterSqlBuilder.cs:54-71` | **依赖物理列存在**，缺列静默不加；见 §4-C9 |
| 34 | Create 强制覆盖 `Id`/`TenantId`/`ConcurrencyStamp`/`CreationTime`/`CreatorId`/`IsDeleted` | ✅ | `ResourceCrudExecutor.cs:372-378` | 仅在 Create |
| 35 | Update 强制覆盖 `ConcurrencyStamp`/`LastModificationTime`/`LastModifierId` + 旧戳比对 | ✅ | `ResourceCrudExecutor.cs:459-465`、`:482-486`、`:489-492` | 冲突抛「Update conflict or record was not found.」 |
| 36 | Update 禁止改 `TenantId`/审计列 | 🐞 | `ResourceCrudExecutor.cs:415` | 白名单基于 `Origin != convention`，而 `Origin` 客户端可控；见 §4-C1 |
| 37 | 软删（表有 `IsDeleted` 则 UPDATE，否则物理 DELETE） | ✅ | `ResourceCrudExecutor.cs:516-531` | 列名大小写敏感比较 |
| 38 | 批量写 = 单连接 + 单事务 + 500 条上限 | ✅ | `ResourceCrudExecutor.cs:168-175`、`:587-592`、`:772` | `CodeDbBatchMaxRows = 500`（`OrchestrationConsts.cs:22`） |
| 39 | `batchcreate` / `batchdelete` | ⚠️ | `ResourceCrudExecutor.cs:604-629`、`:676-708` | delete 合并了 `filter.Args`，create 无 WHERE |
| 40 | `batchupdate` | 🐞 | `ResourceCrudExecutor.cs:631-674` | **实际不可用**（缺参数 + 新戳当旧戳）；见 §4-C6 |
| 41 | 列投影按资源 `ListView.Visible` | ⚠️ | `AppResourceAppService.cs:217-221` | **仅资源 REST 路径**；直连编排/报表路径不裁剪，见 §4-C5 |
| 42 | `get` 返回全部物理列且不套 DataScope | ⚠️ | `ResourceCrudExecutor.cs:305-312` | `get`/`update`/`delete` 的 `designerFilter` 传 `null` |
| 43 | `ResourceQuery` 的 `columns` 只对物理列白名单（无字段级权限） | ⚠️ | `ResourceCrudExecutor.cs:783-805` | 请求可索要任意物理列 |
| 44 | 汇总（`summaryFields` / `SummaryFn`，sum/avg/min/max/count） | ✅ | `ResourceCrudExecutor.cs:852-972` | 字段必须命中表列且类型可算（`:879-890`） |
| 45 | 分页上限 200 | ✅ | `ResourceCrudExecutor.cs:193-198`、`OrchestrationConsts.cs:13` | `ResourceQueryMaxPageSize = 200` |
| 46 | 读操作弹性重试 / 写操作默认不重试 | ✅ | `ResourceCrudExecutor.cs:154-179` | 写重试需节点 `retryOnWrite` |

### 2.3 组合式查询

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 47 | 编号条件 + `combine`（`1 and (2 or 3)`）编译为 SQL | ✅ | `FilterSqlBuilder.cs:83-107`、`:432-536` | 复用 `ConditionCombineEvaluator.Validate` |
| 48 | `FilterGroup` 条件树（`FilterNode`，`and`/`or` 递归） | ✅ | `ResourceSchema.cs:24-35`、`FilterSqlBuilder.cs:198-288` | 真条件树，非字符串拼接 |
| 49 | 条件树嵌套深度上限 | ✅ | `FilterSqlBuilder.cs:228-233`、`OrchestrationConsts.cs:14` | `MaxFilterGroupDepth = 3`；根 `depth=0`，即最多 4 层 group |
| 50 | 字段必须在列白名单内，否则抛错 | ✅ | `FilterSqlBuilder.cs:300-303` | 消息 `Filter field is not allowed` |
| 51 | 值全参数化（无字符串拼接） | ✅ | `FilterSqlBuilder.cs:329-396` | 每操作符独立参数名 |
| 52 | 运算符集 eq/ne/gt/gte/lt/lte/contains/notContains/startsWith/between/in/notIn/isEmpty/isNotEmpty | ✅ | `FilterSqlBuilder.cs:307-399` | Postgres `contains` 用 `ILIKE`（`:402-403`） |
| 53 | `between` 半开区间 `>= min AND < max` | ✅ | `FilterSqlBuilder.cs:363-376` | 与 `sys.MonthEnd` 约定一致 |
| 54 | 设计器隐藏条件（`Exposed=false`）参与 WHERE | ⚠️ | `FilterSqlBuilder.cs:83-107` | 见 §4-C7、§4-C8（`valueSource≠literal` 静默失效） |
| 55 | 租户/软删/DataScope 与用户条件树的组合次序 | ✅ | `FilterSqlBuilder.cs:152` | 三者为独立谓词，最终 `AND` 拼接，**用户 OR 逃不出**；见 §3.6 |
| 56 | 报表把 DataScope 并入条件树第一个 AND 子节点 | ✅ | `ReportDefinitionAppService.cs:438-472` | 与 55 叠加，双重保护 |
| 57 | `SearchForm` / `AdvancedFilter` / `Presets` / `Kpis` 运行时消费 | ❌ | 仅持久化与回读：`ResourceSchema.cs:102-106`、`ReportDefinition.cs:33-39`、`ReportDefinitionAppService.cs:422-425` | `FilterSqlBuilder` 完全不读这些字段 |

### 2.4 查询对象（视图 / 存储过程）

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 58 | 目录列举（`GET query-objects/catalog`，视图 + 过程） | ✅ | `AppBuilderControllers.cs:67-70`、`DbQueryObjectAppService.cs:76-109`、`SqlCatalogInspector.cs:196-350` | 4 个 SQL provider |
| 59 | 导入为查询对象（含列与参数） | ✅ | `AppBuilderControllers.cs:72-75`、`DbQueryObjectAppService.cs:111-180`、`DbQueryObject.cs:79-99` | 权限 `Orchestration.QueryObjects.Create` |
| 60 | 刷新结构（`POST {id}/refresh`） | ✅ | `AppBuilderControllers.cs:81-83`、`DbQueryObjectAppService.cs:182-200` | |
| 61 | 查询对象转资源并列表化 | ✅ | `DbQueryObjectAppService.cs:218-263` | 视图可 `SELECT FROM`；过程走 `CALL`/`EXEC` |
| 62 | 过程参数按 `= 值` 条件与 `parameters` 合并绑定 | ✅ | `ResourceCrudExecutor.cs:1130-1154` | |
| 63 | 过程结果的 COUNT/分页 | ⚠️ | `ResourceCrudExecutor.cs:268-292` | 先把全部行读进内存再 `Skip/Take` |
| 64 | 查询对象删除的引用检查（非原子） | ⚠️ | `DbQueryObjectAppService.cs:202-216` | |

### 2.5 通用报表

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 65 | `ReportDefinition`（3 种来源：resource / table / flow） | ✅ | `ReportDefinition.cs:19-27`、`ResourceSchema.cs:470-483` | |
| 66 | 报表运行时 `POST reports/{code}/query` | ✅ | `AppBuilderControllers.cs:218-221`、`ReportDefinitionAppService.cs:224-327` | 权限 `Orchestration.Reports.Execute` |
| 67 | 报表列投影（`Columns.Where(Visible)`） | ✅ | `ReportDefinitionAppService.cs:281-282` | |
| 68 | 报表 DataScope（`FilterNode`）编译进 SQL | ✅ | `ReportDefinitionAppService.cs:283-287`、`:438-472` | |
| 69 | 报表 ActionDef 的服务端执行端 | ❌ | 只在模型与种子/克隆中被引用 | 按钮只能由前端调 `/api/logic/{flowKey}`；见 §4-C14 |
| 70 | `ActionDef.BatchLimit` / `PayloadFields` 运行时消费 | ❌ | 定义 `ResourceSchema.cs:256`、`:265` | 无消费点 |

### 2.6 表单库

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 71 | `FormDefinition` 独立表单库（`Code` 复用，`FormRef` 引用） | ✅ | `FormDefinition.cs:12-40` | 与编排 `InputSchema` 分开 |
| 72 | 覆盖式 Layout → Fields 拍平 | ⚠️ | `FormDefinitionAppService.cs:213-228` | 被 `if (extracted.Count > 0)` 门控；见 §4-C13 |
| 73 | 发布产生不可变快照 + 版本号 | ⚠️ | `FormDefinition.cs:81-86`、`:89` | 快照为空时回落可变草稿 |
| 74 | 发布前校验字段名唯一 + 与绑定资源列对齐 | ✅ | `FormDefinitionAppService.cs:299-358` | `:350` 用到 `ColumnName` |
| 75 | `ColumnName`（字段名↔物理列名映射）参与 CRUD 执行 | ❌ | 仅 `FormDefinitionAppService.cs:350` 校验用 | 任何执行器都不用它做列映射 |
| 76 | 删除表单的引用检查（非原子） | ⚠️ | `FormDefinitionAppService.cs:134-156` | 检查审批流 `FormRef` / 报表 Actions / 资源 Actions |
| 77 | `resync-catalog` 重推字段 | ⚠️ | `FormDefinitionAppService.cs:182-206` | **只刷 `FieldCatalog`**，不动 `Schema.Fields` |
| 78 | `FormDef.SubmitFlowKey` 运行时消费 | ❌ | `ResourceSchema.cs:458` | 资源提交固定走资源 create/update flowKey |

### 2.7 OA 审批流

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 79 | 流程定义（`ProcessJson` + `FormRef` + 前后置 FlowKey + 版本） | ✅ | `WorkflowDefinition.cs:22-37` | |
| 80 | 发布前结构校验（类型/唯一 start/end/出线/无环/办理人/字段引用/条件优先级） | ✅ | `WorkflowDefinitionValidator.cs:50-150` | 节点类型限 `start/end/approver/cc/condition`（`:17-24`） |
| 81 | 节点类型 start / end / approver / cc / condition | ✅ | `WorkflowDefinitionValidator.cs:17-24` | 排他分支由 `condition` + 出边条件表达，无独立 gateway 类型 |
| 82 | 办理人解析 user / starter / formfield / flow / role / manager | ✅ | `WorkflowRuntimeAppService.cs:600-632` | `manager` 走组织树（`WorkflowOrgResolver.cs:38-80`） |
| 83 | 多人规则 single / all / any / sequential / ratio | ✅ | `WorkflowDefinitionValidator.cs:36-43`、`WorkflowRuntimeAppService.cs:634-668` | `ratio` 用 `MultiRatio`（默认 100） |
| 84 | 通过 / 驳回（终止 / 回发起人 / 回上一节点）/ 转办 | ✅ | `WorkflowRuntimeAppService.cs:166-192`、`:206-229`、`:232-...` | |
| 85 | 抄送（通知不挡流程） | ✅ | `WorkflowRuntimeAppService.cs:394-...`、`:521-547` | 通知失败只告警 |
| 86 | 表单字段权限 + 记录增量补丁（`RecordPatch`） | ✅ | `WorkflowRuntimeAppService.cs:155-163`、`:993-1012`、`:1063-1087` | |
| 87 | 待办查询（`GET workflow-runtime/my-tasks`） | ✅ | `WorkflowRuntimeAppService.cs:101-126` | 按办理人 / 角色匹配，上限 100 |
| 88 | 审批历史（`HistoryJson`）与流程快照（`ProcessSnapshotJson`） | ✅ | `WorkflowRuntimeAppService.cs:84`、`:1014-1033` | ⚠️ 前端时间轴未核验 |
| 89 | 超时提醒 / 催办 / 到期升级 | ❌ | 全仓 grep 无 `DueTime`/`Remind`/超时任务 | 见 §5.6 |
| 90 | 排他/并行网关、子流程、边界事件 | ❌ | — | 见 §5.6 |

### 2.8 看板与视图

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 91 | `KanbanView` / 泳道 / 卡片规则 / 拖拽写回 | ❌ | 全仓 grep 无 `Kanban`/`Swimlane` | 见 §5.5 |
| 92 | 仪表盘 / Widget | ❌ | — | 见 §5.5 |

### 2.9 横切：权限、菜单、路由、测试

| # | 能力 | 状态 | 证据 `file:line` | 备注 |
|---|------|------|------------------|------|
| 93 | 权限点 `Orchestration.Tables.*`（含 `Ddl`）、`Resources.*`（含 `Execute`）、`Reports.*`、`Forms.*`、`Workflows.*`、`QueryObjects.*`、`DataSources.Ddl`、`Sql.Write`、`Instances.Run` | ✅ | `OrchestrationPermissions.cs:18-119`；注册 `SaaSPermissionDefinitionProvider.cs:36-130` | |
| 94 | 菜单种子（表/视图/存储过程/表设计器/资源/资源编辑器/报表/报表编辑器/表单库/表单设计/审批流/审批流设计） | ✅ | `MenuDataSeedContributor.cs:351-447`、`:466-563` | |
| 95 | 运行时页菜单（`/app/:code`、`/app/:code/form/...`、`/app/reports/:code`） | ✅ | `MenuDataSeedContributor.cs:449-464`、`:565-614` | 固定权限 `Orchestration.Resources.Execute` / `Reports.Execute` |
| 96 | 按资源动态注册 `App.{resourceCode}.*` 权限与菜单 | ❌ | 无实现 | 见 §4-C12 |
| 97 | 针对 `FilterSqlBuilder` / `SqlDialect` / DataScope / 租户过滤 / `ResourceCrudExecutor` / DDL 的单测 | ❌ | `tests/Meta.Dow.Orchestration.Tests` 仅 5 个测试类 | 见 §4-C18 |

---

## 3 契约与实现细节

### 3.1 DataSource 与物理边界

- Provider 白名单：`postgres` / `mysql` / `sqlserver` / `oracle` / `redis` / `mongodb`（`DataSourceProvider.cs:5-10`）。
- `accessMode` 决定可执行操作：读需 `Read`/`ReadWrite`，写需 `Write`/`ReadWrite`（`DataSource.cs:88-93`），在 SQL 执行器入口统一校验（`ParameterizedSqlExecutor.cs:286-294`）。
- 表设计器与 DDL 连接仅 SQL 族（`TableDefinitionAppService.cs:71-74`、`SqlDialect.cs:24-32`）。
- 执行器不做多语句：SQL 文本中（去字符串字面量后）出现 `;` 直接拒绝（`ParameterizedSqlExecutor.cs:296-312`）。

### 3.2 表设计器：实体与默认结构

`TableDefinition`（`TableDefinition.cs:62-262`）：

| 字段 | 类型 | 默认 | 说明 |
|------|------|------|------|
| `DataSourceCode` | string | — | 必填，`MaxDataSourceCodeLength` |
| `TableName` | string | — | 必填，过 `SqlIdentifier.IsValid`（`TableDefinition.cs:151-164`） |
| `DisplayName` | string | — | 必填，≤ `MaxNameLength`（128） |
| `Origin` | string | `"managed"` | 构造器硬编码（`:101`），无 imported 分支 |
| `SyncState` | string | `draft` | 写点见 §3.3 |
| `LastAppliedAt` | DateTime? | null | 应用成功时写 |
| `Columns` | `List<TableColumn>` | 10 个约定列 | `:103` |
| `Indexes` | `List<TableIndexDef>` | 3 个默认索引 | `:104` |

`TableColumn`（`TableDefinition.cs:11-39`）：`Name` / `DisplayName` / `PlatformType` / `Length` / `Precision` / `Scale` / `Nullable` / `Default` / `Unique` / `Comment` / `Origin` / `AppliedName`。

`ReplaceColumns` 的校验（`TableDefinition.cs:175-231`）：

1. 至少 1 列（否则 `TableColumnsRequired`）；
2. 每列名过 `SqlIdentifier.IsValid`，类型过 `TablePlatformType.IsSupported`；
3. `Origin` 空则回落 `user`（`:200`）——**无白名单，客户端可写任意值**；
4. `AppliedName` 非空时也必须合法；
5. 列名去重（大小写不敏感）；
6. `AbpConventionColumns.RequiredNames` 10 个名字必须全部存在（`:221-228`）。

### 3.3 默认 ABP 约定列与默认索引

`AbpConventionColumns.Create()`（`TableDefinition.cs:280-295`）：

| 列 | 平台类型 | 可空 | 默认 | 用途 |
|----|----------|------|------|------|
| `Id` | guid | NOT NULL | 无（DDL 时按 provider 补 `gen_random_uuid()`/`NEWID()`） | PK |
| `TenantId` | guid | NULL | — | 多租户 |
| `ConcurrencyStamp` | string(40) | NOT NULL | — | 乐观并发 |
| `CreationTime` | datetime | NOT NULL | — | 创建时间（引擎写 UTC） |
| `CreatorId` | guid | NULL | — | 创建人 |
| `LastModificationTime` | datetime | NULL | — | |
| `LastModifierId` | guid | NULL | — | |
| `IsDeleted` | boolean | NOT NULL | `false` | 软删 |
| `DeleterId` | guid | NULL | — | |
| `DeletionTime` | datetime | NULL | — | |

> 原文「`Id` 默认 `gen_random_uuid()` / `NEWID()`」在**元数据层不成立**（`Id` 无 `Default`），只在**生成 DDL 时**按 provider 补（`SqlDialect.cs:131-140`）。MySQL / Oracle 的 `Id` 无 DB 默认值，必须由应用层给值（`ResourceCrudExecutor.cs:372`）。

`CreateDefaultIndexes`（`TableDefinition.cs:297-331`）：

| 索引 | 列 | Origin |
|------|----|--------|
| `PK_{table}` | `Id`（primary + unique） | convention |
| `IX_{table}_Tenant_Deleted` | `TenantId`, `IsDeleted` | convention |
| `IX_{table}_Tenant_Creation` | `TenantId`, `CreationTime DESC` | convention |

`{table}` 由 `Regex.Replace(tableName, @"[^A-Za-z0-9_]", "_")` 派生（`:299`）。

### 3.4 平台类型 → 各库映射（设计器里选平台类型）

按 `SqlDialect.MapColumnType`（`SqlDialect.cs:59-125`）写实；`length` 默认 256（上限 4000），`precision` 默认 18，`scale` 默认 2。

| 平台类型 | Postgres | MySQL | SQL Server | Oracle |
|----------|----------|-------|------------|--------|
| `guid` | `uuid` | `char(36)` | `uniqueidentifier` | `RAW(16)` |
| `string` | `varchar(n)` | `varchar(n)` | `nvarchar(n)` | `VARCHAR2(n)` |
| `enum` | `varchar(n)` | `varchar(n)` | `nvarchar(n)` | `VARCHAR2(n)` |
| `text` | `text` | `text` | `nvarchar(max)` | `CLOB` |
| `int` | `integer` | `int` | `int` | `NUMBER(10)` |
| `long` | `bigint` | `bigint` | `bigint` | `NUMBER(19)` |
| `decimal` | `numeric(p,s)` | `decimal(p,s)` | `decimal(p,s)` | `NUMBER(p,s)` |
| `boolean` | `boolean` | `tinyint(1)` | `bit` | `NUMBER(1)` |
| `date` | `date` | `date` | `date` | `TIMESTAMP` |
| `datetime` | `timestamptz` | `datetime(3)` | `datetimeoffset` | `TIMESTAMP` |
| `json` | `jsonb` | `json` | `nvarchar(max)` | `CLOB` |

- **不建 DB ENUM**：`enum` 落各库的变长字符串（`SqlDialect.cs:72`/`:86`/`:100`/`:114`），字典在应用层。
- 值参数前缀：Postgres/MySQL/SQL Server 用 `@name`，Oracle 用 `:name`（`SqlDialect.cs:50-57`）。
- `DEFAULT` 只有两种情况会下发：`Id`（guid）→ `gen_random_uuid()` / `NEWID()`；boolean 且 `Default == "false"` → `FALSE` / `0`（`SqlDialect.cs:128-156`）。**其它字面量与函数一律忽略**。
- 标识符一律 `QuoteIdent`：MySQL 反引号、SQL Server 方括号、Oracle 双引号 + 大写、其余双引号（`SqlDialect.cs:41-47`）；非 `IsQuotable` 直接抛错。
- `IsQuotable`：首字符须为字母或 `_`，其余只允许字母/数字/`_`，长度 ≤ 128 ⇒ 引号、点号、空白、连字符天然被拒（`TablePlatformType.cs:174-197`）。

### 3.5 同步策略与 diff 语义

状态与写点：

| `syncState` | 含义 | 代码写点 |
|-------------|------|----------|
| `draft` | 从未应用 | 构造器 `TableDefinition.cs:102` |
| `inSync` | 设计与库一致 | `MarkApplied()` `:124-132` |
| `localAhead` | 有未应用的设计 | `UpdateDraft()` `:118-121`、`MarkLocalAhead()` `:134-140` |
| `remoteAhead` | 库被外部改过 | ❌ 无写入点 |
| `conflict` | 两边都有变更 | ❌ 无写入点 |

**流程**（`AppBuilderControllers.cs:39-46` + `TableDefinitionAppService.cs:121-153`）：

```
POST/PUT 保存草稿（只改元数据；inSync → localAhead）
        │
        ▼
GET  {id}/preview  = diff(设计时, 真实库)
        │  物理表不存在 → createTable + createIndex
        │  否则按列/索引差集生成 addColumn / alterColumn / renameColumn / dropColumn / dropIndex / createIndex
        ▼
POST {id}/apply    = 重新计算同一份 diff → 单连接单事务按序执行 → MarkApplied
        │  权限：Orchestration.Tables.Ddl；DS 必须可写（SqlDdlExecutor.cs:79-82）
        ▼
POST {id}/resources = 一键创建资源（要求 SyncState == inSync）
```

diff 生成规则（`SqlDdlExecutor.cs:68-240`）：

| 场景 | 生成 | 行 |
|------|------|----|
| 物理表不存在 | `createTable` + 全部 `createIndex` | `:86-98` |
| 库里有的索引不在设计里（且非 protected） | `dropIndex`（`Destructive = true`） | `:119-128` |
| 设计里有、库里没有的列 | `addColumn` | `:133-140` |
| 约定列改名 | **抛错**「cannot be renamed」 | `:145-153` |
| 列名变化（`AppliedName`→新名） | `renameColumn`（`Destructive = true`） | `:168-176` |
| 类型/可空性变化（MySQL 还含注释） | `alterColumn`（按 `IsColumnAlterDestructive` 标破坏性） | `:179-188` |
| 库里有、设计里没有的非约定列 | `dropColumn`（`Destructive = true`） | `:191-204` |
| 有 `dropColumn` 时 | 额外清理相关索引再重建 | `:206-231` |

`NeedsColumnAlter`：类型不等 或 可空性不等 或（MySQL）注释不等（`SqlDialect.cs:363-381`）。
`IsColumnAlterDestructive`：可空→非空为破坏；类型等价或字符串加宽为非破坏；其余为破坏（`SqlDialect.cs:383-396`）。

### 3.6 资源（AppResource）字段契约

`AppResource`（`AppResource.cs:11-53`），全部属性均有默认值：

| 属性 | 默认 | 说明 |
|------|------|------|
| `Code` | — | 必填，≤ 64，过 `SqlIdentifier.IsValid`（`AppResource.cs:376-386`） |
| `Name` | — | 必填，≤ 128 |
| `DataSourceCode` / `TableName` | — | 绑定目标（表或查询对象名） |
| `SourceKind` | `table` | `table` / `view` / `procedure`（`TablePlatformType.cs:93-115`） |
| `QueryObjectId` | null | 查询对象绑定时写入 |
| `TitleField` | `Id` | 有用户列时取第 1 个用户列（`AppResource.cs:126`） |
| `PrimaryKey` | `Id` | 查询对象无 `Id` 列时取首列（`AppResource.cs:235-242`） |
| `SoftDeleteField` | `IsDeleted` | |
| `TenantField` | `TenantId` | 仅元数据，执行器不读它 |
| `ConcurrencyField` | `ConcurrencyStamp` | 仅元数据，执行器硬编码列名 |
| `QueryFlowKey` | `sys.resource.query` | |
| `GetFlowKey` | `sys.resource.get` | |
| `CreateFlowKey` | `sys.resource.create` | |
| `UpdateFlowKey` | `sys.resource.update` | |
| `DeleteFlowKey` | `sys.resource.delete` | |
| `Status` | `0`（draft） | 发布置 `1`（`:372`） |
| `Filter` / `ListView` / `Form` | 空对象 | |

> 原文 §5.8 的 `binding: { kind, table }` 结构、`primaryKey: ["Id"]` 数组、`softDeleteField` 等键名与代码不符：真实契约是**扁平属性 + 单值 `PrimaryKey`**（`AppBuilderDtos.cs:122-157`）。

一键创建资源时按表派生（`AppResource.cs:123-227`）：

- **ListView.Columns**：所有非 convention 列 + `CreationTime`；`Sortable` 限于 datetime/int/long/decimal/string；`FormatPreset` 按类型给 `datetime`/`date`/`currency`/`boolean`/`text`（`:128-151`）。
- **ListView.Actions**：5 个默认按钮 `create`（toolbar）/ `update`（row）/ `detail`（row）/ `delete`（row，`Confirm`）/ `batchDelete`（toolbar，`BatchLimit = 100`，`MultiSelect`）（`:153-185`）。
- **Form.Fields**：所有列；convention 列 `VisibleOn*=false`、`ReadonlyOn*=true`，`VisibleOnDetail` 对 `CreationTime`/`LastModificationTime` 为 true；必填 = 非 convention 且非空（`:187-211`）。
- **Filter.Items**：前 5 个用户列，`Op` 为 string/text → `contains`，其余 `eq`，`ValueSource="user"`，`Exposed=true`；`Combine = "1 and 2 …"`（`:213-223`）。
- **Filter.AdvancedFilter = true**，`DataScope` 初始化为空 `and` 组，并生成 `SearchForm`（前 5 列，最多 3 列布局）（`:224-226`、`:341-370`）。

查询对象版（`AppResource.cs:229-339`）：`ListView.Actions` 只给 `detail`（仅当存在主键列）；`Filter.Items` 清空；`Filter.AdvancedFilter = (Kind == view)`；过程资源用输入参数生成 `SearchForm`（最多 8 个，`Op="eq"`，字段名 `p1..pN`）。

### 3.7 `FilterSqlBuilder`：谓词组装次序（关键契约）

`FilterSqlBuilder.Build`（`FilterSqlBuilder.cs:42-155`）按**固定顺序**把下列谓词放进 `List<string> predicates`，最后一行用 `" AND "` 连接：

| 顺序 | 谓词 | 条件 | 行 |
|------|------|------|----|
| 1 | `TenantId = @tenantId` | 列映射存在 `TenantId` **且**当前有租户 | `:54-60` |
| 1′ | `TenantId IS NULL` | 列映射存在 `TenantId` **且**当前是 Host | `:61-64` |
| 1″ | 不加任何租户谓词 | 列映射**没有** `TenantId` | 静默跳过，见 §4-C9 |
| 2 | `IsDeleted = @isDeleted`（false） | 列映射存在 `IsDeleted` | `:67-71` |
| 3 | 设计器 `DataScope` 递归编译 | `designerFilter?.DataScope != null` | `:73-81` |
| 4 | 设计器隐藏条件 `Exposed=false` 的 `combine` | `designerFilter.Items` 有隐藏项 | `:83-107` |
| 5 | 运行时条件树（`FilterNode`）或旧式 `items+combine` | 请求带 `filter`/`filters` | `:109-148` |
| — | `WhereSql = predicates 用 " AND " 连接`；空则 `1=1` | | `:152` |

```csharp
return new FilterSqlResult {
    WhereSql = predicates.Count == 0 ? "1=1" : string.Join(" AND ", predicates),
    Args = args
};
```

**结论（真实优势，写实）**：租户谓词、软删谓词、DataScope 都是**独立顶层谓词**，与用户条件树同级 `AND` 连接；用户提交的 `or` 子树无论怎么嵌套都**不可能**把它自己或别的租户的行捞出来。报表路径更严：`ReportDefinitionAppService` 把报表自身 DataScope 作为 `and` 组的第一个子节点并入 `filter`（`ReportDefinitionAppService.cs:283-287`、`:438-472`），再由上面的第 1/2 条兜底。

条件树编译（`FilterSqlBuilder.cs:198-288`）：

- 判 rule：`kind == "rule"` 或（有 `left` 且无 `children` 数组）（`:236-237`）；
- 空值 rule 直接跳过（`isEmpty`/`isNotEmpty` 除外）（`:243-247`）；
- 组：`op` 只接受 `and`/`or`（其它回落 `and`），单子节点不加括号，多子节点加括号（`:252-287`）；
- 深度：`depth > MaxFilterGroupDepth(3)` 抛错（`:228-233`）。

运算符与参数化（`FilterSqlBuilder.cs:290-400`）：`eq/ne/gt/gte/lt/lte` 单参数；`contains/notContains/startsWith` 用 `LIKE`（Postgres `ILIKE`）且 `%` 由服务端加；`between` 生成 `>= min AND < max` 两参数；`in/notIn` 展开为多个参数（空数组 → `1=0` / `1=1`）；`isEmpty/isNotEmpty` 走方言函数（`:307-315`）。

`left` 必须是列映射中的键，否则抛 `Filter field is not allowed: {left}`（`:300-303`）——这同时挡住了 `.`、SQL 关键字等注入面。

### 3.8 `ResourceCrudExecutor`：执行契约

入口分派（`ResourceCrudExecutor.cs:89-180`）：

1. 读 `resourceCode` / `dataSourceCode` / `tableName`；
2. `tableOverride = (op == "query") && dataSourceCode 非空 && tableName 非空`（`:101-103`）——**只有 query 支持覆盖**；
3. 取 `AppResource`（有 `resourceCode` 时；找不到抛 `Resource was not found`）；`tableOverride` 时按 `(dataSourceCode, tableName)` 取 `TableDefinition`；
4. `op != query` 时 `resourceCode` 必填。`op != query` 且 `schema.IsQueryCatalog`（视图/过程）→ 抛「View and procedure resources support query only.」（`:144-147`）；
5. 统一用 `INodeResiliencePolicy` 包裹：`isWrite = op is not ("query" or "get")`；写操作只有在节点声明 `retryOnWrite` 时才允许重试（`:154-179`）。

#### query

| 项 | 行为 | 行 |
|----|------|----|
| `page` | `max(1, page)` | `:193` |
| `pageSize` | clamp 到 `1..200`；缺省取资源 `ListView.PageSize`，再缺省 20 | `:194-198` |
| 过程（不可 `SELECT FROM`） | 走 `CALL`/`EXEC`，**全量取回后在内存分页** | `:200-204`、`:268-292` |
| 设计器 Filter | `tableOverride` 或过程 → **传 `null`**（DataScope、隐藏条件全部丢弃） | `:206-212` |
| 请求条件 | `input["filter"] ?? input["filters"]` | `:210` |
| 投影 | `input["columns"]` 只按**物理列集合**过滤；未传时用全部列；`Id` 强制保留 | `:219-222`、`:783-805` |
| 排序 | `input["sorting"]`，否则资源 `ListView.DefaultSorting`，无效字段回落 `CreationTime desc` | `:223-227`、`:807-823` |
| 计数 | 同一 `WHERE` 的 `COUNT(1)` | `:228-235` |
| 分页 SQL | SQL Server/Oracle 用 `OFFSET/FETCH`，其余 `LIMIT/OFFSET` | `:825-850` |
| 返回 | `{ items, total }`，有汇总时追加 `summary` | `:243-265` |
| 汇总 | 请求 `summaryFields`，缺省取列上 `SummaryFn`；字段须在列映射内且类型可算 | `:852-972` |

#### get

- 取 `id`，`SELECT 全部列 FROM t WHERE {租户+软删} AND pk = @id`（`:294-319`）。
- **不套 DataScope**（`:305` 传 `null`），也**不按 ListView 投影**。
- 命中不到抛 `Record was not found.`。

#### create

1. `EnsureWriteAsync()`（要求 `Orchestration.Sql.Write`）（`:331`、`:537-543`）；
2. 只遍历 `Origin != convention` 的列，从 `record` 大小写不敏感取值；空值：date/datetime 且非空 → 抛必填错，否则跳过（`:335-370`）；
3. 强制覆盖 `Id`（`record.id` 或新 guid）、`TenantId`（当前租户，Host 为 `null`）、`ConcurrencyStamp`（新随机戳）、`CreationTime`（UTC `O`）、`CreatorId`、`IsDeleted=false`（`:372-378`）；
4. 生成 `INSERT INTO t (cols) VALUES (@p...)`，参数名由列名 `Sanitize`（去非字母数字）派生（`:380-392`）。

#### update

1. `EnsureWriteAsync()`；`id` 必填（`:407-409`）；
2. `SET` 列表 = 所有 `Origin != convention` 的列中**在 `record` 里出现且非空**的项；空值 string 跳过，空值 date/datetime 非空则抛必填错（`:415-457`）；
3. 强制追加 `ConcurrencyStamp = @newStamp`、`LastModificationTime`、`LastModifierId`（`:459-465`）；
4. 若无业务列可更新（`sets.Count == 3`）抛「No updatable fields were provided.」（`:467-470`）；
5. `WHERE {租户+软删} AND pk = @id`，可选 `AND ConcurrencyStamp = @stamp`（仅当请求带 `concurrencyStamp` 且表里有该列）（`:472-486`）；
6. `AffectedRows == 0` 且非 DryRun → 抛「Update conflict or record was not found.」（`:489-492`）；返回 `{ id, concurrencyStamp }`。
7. **不套 DataScope**（`:472` 传 `null`）；**不覆盖 `TenantId`**（这是 §4-C1 的机理）。

#### delete

- `EnsureWriteAsync()`；`WHERE {租户+软删} AND pk = @id`（`:497-514`）。
- 表里有 `resource.SoftDeleteField` 列 → `UPDATE SET IsDeleted=true, DeleterId, DeletionTime`；否则 `DELETE FROM`（`:516-531`）。
- **不校验 `AffectedRows`**：删不存在的 id 也返回 `{ id, deleted: true }`（`:533-534`）。
- **不套 DataScope**（`:510` 传 `null`）。

#### batchcreate / batchupdate / batchdelete

- `EnsureWriteAsync()`；仅 SQL 家族，Mongo/Redis 显式拒绝并提示改用 `ForEach + itemErrorMode=continueOnError`（`:570-578`）；
- `items` 必须是数组；空数组直接返回 `{affected:0,count:0}`；超过 `CodeDbBatchMaxRows = 500` 抛错（`:580-592`）；
- 单连接 + 单事务（复用 `IParameterizedSqlExecutor.BatchAsync`），失败整体回滚；返回 `{ op, count, affected, committed }`（`:762-781`）；
- `batchcreate`：与单条 Create 同源列形状，逐条填约定列（`:604-629`）；
- `batchdelete`：每项参数从 `filter.Args` 深拷贝后加 `pId`，固定软删（节点校验层禁 `onDelete`）（`:676-708`、`FlowExecutor.cs:2535-2541`）；
- `batchupdate`：见 §4-C6（**当前不可用**）。

### 3.9 查询对象（视图 / 存储过程）

- 种类只有 `view` / `procedure`（`TablePlatformType.cs:75-91`）；`RoutineKind` 额外区分 `function`（`:117-122`）。
- 实体字段：`Kind` / `ObjectName` / `DisplayName` / `RoutineKind` / `CanSelectFrom` / `Comment` / `ImportWarning` / `LastImportedAt` / `Columns` / `Parameters`（`DbQueryObject.cs:30-54`）。
- 导入即刷新：`ApplyImport`（`DbQueryObject.cs:79-99`）覆盖列与参数并写 `LastImportedAt`；`CanSelectFrom` 对 view 恒为 true（`:93`）。
- 参数契约：`Name` / `DisplayName` / `PlatformType` / `Direction`（`in`/`out`/`inout`）/ `Ordinal` / `Nullable` / `HasDefault`（`DbQueryObject.cs:10-25`）。
- 目录列举 provider 支持：postgres / mysql / sqlserver / oracle；其余抛「Unsupported provider」（`SqlCatalogInspector.cs:196-212`、`:257-349`）。
- 运行时：
  - 视图 → 与表同一套 `SELECT … WHERE … ORDER BY … LIMIT/OFFSET`；
  - 过程（Oracle / MySQL）→ `CALL name(args)`；SQL Server 且不可 `SELECT FROM` → `EXEC name args`（`ResourceCrudExecutor.cs:1107-1128`）；
  - SQL Server / Postgres 且 `CanSelectFrom` → `FROM name(args)`（`:1091-1105`）；
  - 过程参数从 `filter` 条件树的 `=` 规则与 `parameters` 对象两处收集，缺失则绑 `null`（`:1130-1154`）。
- 查询对象资源只允许 `query` / `get`？——**只允许 query**：非 query 的 op 遇到 `IsQueryCatalog` 直接抛错（`ResourceCrudExecutor.cs:144-147`），`RequireTable()` 也会拦（`:1246-1249`）。

### 3.10 通用报表

`ReportDefinition`（`ReportDefinition.cs:11-55`）三种来源（`ResourceSchema.cs:470-483`：`resource`/`table`/`flow`）：

| Kind | 来源 | 行为 |
|------|------|------|
| `resource` | `ApplyFromResource`（`:136-159`） | 复制资源的 SearchForm / AdvancedFilter / DataScope / Presets / Columns / Actions / Form / Dictionaries / 排序 / 分页；`SelectionEnabled` 由 Actions 的 `MultiSelect`/`batch`/`selection` 推导 |
| `table` | `ApplyFromTable`（`:161-181`） | 只按表列生成 Columns；无 Actions / Form / 字典；`DefaultSorting` 视有无 `CreationTime` |
| `flow` | `SetQueryFlowKey`（`:188-191`） | 必须传已发布的 `queryFlowKey`（`ReportDefinitionAppService.cs:388-394`、`:399-406`） |

运行时 `POST /api/orchestration/reports/{code}/query`（`ReportDefinitionAppService.cs:224-327`）：

1. 报表必须已发布；Kind=resource 时被绑资源也必须已发布；Kind=table 时校验表存在（`:228-248`、`:343-354`）；
2. 组装 body：`resourceCode`（resource 来源）/ `dataSourceCode` + `tableName`（**非 resource 来源**，即 table/flow 会带 `dataSourceCode+tableName`）（`:250-267`）；
3. `page` ≥ 1，`pageSize` clamp 到 `1..200`（`:270-271`）；
4. `sorting` = 请求值，否则报表 `DefaultSorting`（`:272-279`）；
5. `columns` = 报表中 `Visible` 的列（`:281-282`）；
6. `filter` = `CombineFilters(报表 DataScope, 请求 filter)`（`:283-287`、`:438-462`）：两者都有内容时包成 `{kind:"group", op:"and", children:[scope, runtime]}`；
7. `summaryFields` = 请求值，否则列上 `SummaryFn`（`:294-303`）；
8. 交给 `PublishedFlowInvoker` 调 `entity.QueryFlowKey`，`filterOutputsByRole: true`，查询不落实例（`:305-312`）。

> 关键：**报表来源不是 resource 时会带 `dataSourceCode+tableName`**，从而在 `ResourceCrudExecutor` 中触发 `tableOverride` 分支（§4-C2）。

### 3.11 表单库

`FormDefinition`（`FormDefinition.cs:12-40`）：`Code` / `Name` / `Description` / `Status` / `SchemaVersion`（0 = 未发布）/ `SourceResourceCode` / `Schema`（草稿）/ `PublishedSchema`（快照，可空）/ `FieldCatalog`。

| 操作 | 行为 | 行 |
|------|------|----|
| Create | 建草稿；带 `SourceResourceCode` 时从资源 `ListView.Columns` 同步 FieldCatalog，Schema 取资源 Form（若非空） | `FormDefinitionAppService.cs:78-109`、`:382-397` |
| Update | `EnsureFlattenedFields(input.Schema)` 后整体替换草稿 + FieldCatalog | `:111-127` |
| Publish | 拍平 → 非空校验 → 字段名唯一 → 与绑定资源列对齐 → `Publish(CloneForm(Schema))` 深拷贝快照并 `SchemaVersion++` | `:161-178`、`FormDefinition.cs:81-86` |
| `published/{code}` | 必须已发布；返回 `GetEffectiveSchema()` = `PublishedSchema ?? Schema` | `:65-76`、`FormDefinition.cs:89` |
| `resync-catalog` | 只把资源 `ListView.Columns` 写进 `FieldCatalog`，Schema 原样传回 | `:182-206` |
| Delete | 检查 `WorkflowDefinition.FormRef`、报表 Actions、资源 Actions 的 `FormKey` 引用后删除 | `:134-156` |

拍平（`FormDefinitionAppService.cs:213-262`）：Layout 为空直接返回；否则递归提取 `kind=field` 与明细容器（`data-table`/`subform`/`list`/`tree`）为扁平字段，`BuildField` 复制 `Rules`/`MinItems`/`MaxItems`/`TreeChildrenField`/`ColumnName`/`Dependencies`/`ControlProps`。

### 3.12 系统逻辑种子（真实 DSL）

`SystemResourceFlowSeedContributor`（`SystemResourceFlowSeedContributor.cs:34-196`）在种子期对每个租户 `SeedOneAsync`，5 条：

| flowKey | 节点 type / ref | 入参 | 出参 |
|---------|-----------------|------|------|
| `sys.resource.query` | `ResourceQuery` / `query` | `resourceCode`、`dataSourceCode`、`tableName`、`page`、`pageSize`、`sorting`、`filter`、`filters`、`columns`、`summaryFields` | `items`、`total`、`summary` |
| `sys.resource.get` | `ResourceGet` / `get` | `resourceCode`(必填)、`id`(必填) | `record` |
| `sys.resource.create` | `ResourceCreate` / `create` | `resourceCode`(必填)、`record`(必填) | `id` |
| `sys.resource.update` | `ResourceUpdate` / `update` | `resourceCode`(必填)、`id`(必填)、`concurrencyStamp`、`record`(必填) | `id`、`concurrencyStamp` |
| `sys.resource.delete` | `ResourceDelete` / `delete` | `resourceCode`(必填)、`id`(必填) | `id`、`deleted` |

图恒为 `Start → 资源节点 → End`（`:277-296`）；DSL `version = "1.1"`，`trigger.type = "Manual"`；`outputs` 全部 `visibleTo.mode = "all"`。

> 与原文差异：**这些入参没有 `default`**（既没有 `page: 1` 也没有 `pageSize: 20`），默认值由执行器 `ResourceCrudExecutor.cs:193-198` 决定。文档里写的 `"default": 1` 与种子 DSL 不符。

已存在时走幂等更新：`MarkSystem()` + `SetReusable(true)` + `UpdateDraft(...)` + `MarkPublished(next)` 并新增一条 `FlowVersion`（`:211-232`）。

编排节点类型识别（`FlowExecutor.cs:1449-1465`）：`resourcequery` / `resourceget` / `resourcecreate` / `resourceupdate` / `resourcedelete` / `resourcebatchcreate` / `resourcebatchupdate` / `resourcebatchdelete` / `resourcebatch`，否则回落 `node.Kind` 或 `nodeInput.op`。

节点声明的发布校验（`FlowExecutor.cs:2496-2543`）：

- `resourcequery`：必须声明 `resourceCode`，**或**同时声明 `dataSourceCode` + `tableName`；
- 其它资源节点：必须声明 `resourceCode`；
- `resourcebatch*`：必须显式声明 `items`；`resourcebatchdelete` 不接受 `onDelete`（固定软删）。

### 3.13 权限矩阵与真实路由表

权限点定义见 `OrchestrationPermissions.cs`，注册见 `SaaSPermissionDefinitionProvider.cs`。授权挂在**应用服务方法**上（控制器类本身无 `[Authorize]`，见 `SaaSController.cs:6-12`）。

| 路由 | 方法 | 应用服务授权 |
|------|------|--------------|
| `/api/orchestration/tables` | GET | `Orchestration.Tables` |
| `/api/orchestration/tables/{id}` | GET | `Orchestration.Tables` |
| `/api/orchestration/tables` | POST | `Orchestration.Tables.Create` |
| `/api/orchestration/tables/{id}` | PUT | `Orchestration.Tables.Update` |
| `/api/orchestration/tables/{id}` | DELETE | `Orchestration.Tables.Delete` |
| `/api/orchestration/tables/{id}/preview` | GET | `Orchestration.Tables` |
| `/api/orchestration/tables/{id}/apply` | POST | `Orchestration.Tables.Ddl` |
| `/api/orchestration/tables/{id}/resources` | POST | `Orchestration.Resources.Create` |
| `/api/orchestration/query-objects` | GET | `Orchestration.QueryObjects` |
| `/api/orchestration/query-objects/catalog` | GET | `Orchestration.QueryObjects` |
| `/api/orchestration/query-objects/import` | POST | `Orchestration.QueryObjects.Create` |
| `/api/orchestration/query-objects/{id}` | GET / DELETE | `Orchestration.QueryObjects` / `.Delete` |
| `/api/orchestration/query-objects/{id}/refresh` | POST | `Orchestration.QueryObjects.Update` |
| `/api/orchestration/query-objects/{id}/resources` | POST | `Orchestration.Resources.Create` |
| `/api/orchestration/resources` | GET | `Orchestration.Resources` |
| `/api/orchestration/resources/by-code/{code}` | GET | `Orchestration.Resources` |
| `/api/orchestration/resources/published/{code}` | GET | `Orchestration.Resources.Execute` |
| `/api/orchestration/resources/{id}` | GET / PUT / DELETE | `Resources` / `Resources.Update` / `Resources.Delete` |
| `/api/orchestration/resources/{id}/publish` | POST | `Orchestration.Resources.Publish` |
| `/api/orchestration/resources/{code}/query` | POST | `Orchestration.Resources.Execute` |
| `/api/orchestration/resources/{code}/get` | POST | `Orchestration.Resources.Execute` |
| `/api/orchestration/resources/{code}/create` | POST | `Orchestration.Resources.Execute` |
| `/api/orchestration/resources/{code}/update` | POST | `Orchestration.Resources.Execute` |
| `/api/orchestration/resources/{code}/delete` | POST | `Orchestration.Resources.Execute` |
| `/api/orchestration/resources/{code}/batch-delete` | POST | `Orchestration.Resources.Execute` |
| `/api/orchestration/reports` | GET / POST | `Reports` / `Reports.Create` |
| `/api/orchestration/reports/from-resource` | POST | `Orchestration.Reports.Create` |
| `/api/orchestration/reports/from-table` | POST | `Orchestration.Reports.Create` |
| `/api/orchestration/reports/{id}` | GET / PUT / DELETE | `Reports` / `Reports.Update` / `Reports.Delete` |
| `/api/orchestration/reports/by-code/{code}` | GET | `Orchestration.Reports` |
| `/api/orchestration/reports/published/{code}` | GET | `Orchestration.Reports.Execute` |
| `/api/orchestration/reports/{id}/publish` | POST | `Orchestration.Reports.Publish` |
| `/api/orchestration/reports/{code}/query` | POST | `Orchestration.Reports.Execute` |
| `/api/orchestration/forms` | GET / POST | `Forms` / `Forms.Create` |
| `/api/orchestration/forms/published-lookup` | GET | `Orchestration.Forms` |
| `/api/orchestration/forms/{id}` | GET / PUT / DELETE | `Forms` / `Forms.Update` / `Forms.Delete` |
| `/api/orchestration/forms/by-code/{code}` / `published/{code}` | GET | `Orchestration.Forms` |
| `/api/orchestration/forms/{id}/publish` | POST | `Orchestration.Forms.Publish` |
| `/api/orchestration/forms/{id}/resync-catalog` | POST | `Orchestration.Forms.Update` |
| `/api/orchestration/workflows` | GET / POST | `Workflows` / `Workflows.Create` |
| `/api/orchestration/workflows/{id}` | GET / PUT / DELETE | `Workflows` / `.Update` / `.Delete` |
| `/api/orchestration/workflows/by-code/{code}` | GET | `Orchestration.Workflows` |
| `/api/orchestration/workflows/{id}/publish` | POST | `Orchestration.Workflows.Publish` |
| `/api/orchestration/workflow-runtime/start` | POST | `Orchestration.Workflows`（`WorkflowRuntimeAppService.cs:49`） |
| `/api/orchestration/workflow-runtime/my-tasks` | GET | `Orchestration.Workflows` |
| `/api/orchestration/workflow-runtime/tasks/{taskId}/complete` | POST | `Orchestration.Workflows`（`:128`） |
| `/api/orchestration/system-parameters` | GET | 仅需登录（类级 `[Authorize]`，`LogicApiAppService.cs:11`、`:106`） |
| `/api/orchestration/system-parameters/preview-date` | POST | 仅需登录 |
| **`/api/logic/{flowKey}`** | POST | **`Orchestration.Instances.Run`**（`LogicApiAppService.cs:35`） |

路由定义位置：`AppBuilderControllers.cs:11/56/97/167/226/281/321`、`LogicApiController.cs:11`、`:57`。

> **`/api/logic/{flowKey}` 是最低门槛的运行时入口**：它只要求 `Instances.Run`，并以 `skipValidation: true` 执行 DSL（`LogicApiAppService.cs:55-62`）。详见 §4-C3。

---

## 4 行为边界与已知缺陷

> 格式：**【严重度】现象 — 机理 — `file:line` — 后果 / 规避**。
> 严重度：【严重】= 跨租户/越权或数据破坏；【高】= 功能失效或数据错误；【中】= 静默失效、误导性成功；【低】= 可读性/一致性问题。

### C1 【严重】持 `Orchestration.Tables.Update` 者可跨租户搬行并篡改审计列

- **现象**：先提交 `columns[].origin = "user"`（任意非 `convention` 值）改掉约定列的 Origin，再提交 `update` 把 `record.TenantId` 设为目标租户、`record.CreationTime`/`CreatorId`/`IsDeleted` 设为任意值 → 行被搬进别的租户命名空间，审计字段被改写。
- **机理**：三处组合。
  1. `TableDefinitionAppService.MapColumn` 直接把 DTO 的 `Origin` 写进实体，无白名单：`TableDefinitionAppService.cs:198`。
  2. `TableDefinition.ReplaceColumns` 仅把空 Origin 回落为 `user`，不校验取值：`TableDefinition.cs:200`。
  3. `ResourceCrudExecutor.UpdateAsync` 的 SET 白名单是「`Origin != convention` 的全部列」：`ResourceCrudExecutor.cs:415`；而 `TenantId` 的强制覆盖**只写在 CreateAsync**：`ResourceCrudExecutor.cs:372-378`（Update 路径完全没有）。
- **后果**：本租户的行可被写入他租户可见区间并把 `CreatorId`/`CreationTime` 改成任意人/任意时间；同时 `Id` 也在可控范围内（约定列 Origin 被改后同样进入 SET 列表）。
- **规避（当前代码）**：把 `Orchestration.Tables.Update` 收敛给极小范围；发布/审计侧的 `CreatorId` 不可信。**根治需改代码**（见 §5.2）。
- **注意**：Update 的 `WHERE` 仍带本租户谓词，所以攻击面是「把自己的行写给他租户 / 篡改审计」，不是「读写他租户已有行」。

### C2 【严重】`tableOverride` 分支主动丢弃资源级 DataScope 与资源校验

- **现象**：`POST /api/logic/sys.resource.query` 只带 `dataSourceCode + tableName`（不带 `resourceCode`），即可对任意**已登记的 `TableDefinition`** 查询，且不受任何资源配置约束。
- **机理**：`ResourceCrudExecutor.cs:101-103` 判定 override；`:116-124` 直接按 `(dataSourceCode, tableName)` 取表；`:209` 在设计器 Filter 位置传 `null`（`tableOverride || Procedure ? null : resource?.Filter`），而 `DataScope` 正是 `FilterDef` 的成员（`ResourceSchema.cs:100`、`FilterSqlBuilder.cs:73-81`）。此处 `resource == null`，因此也没有资源级权限、没有 `ListView` 投影。
- **仍然生效的约束**：目标表必须是**已登记** `TableDefinition`（`:118-121`）；DS 必须启用；租户谓词与软删谓词照旧注入（§3.7 第 1/2 条）；`columns` 仍限物理列白名单。
- **放大点**：报表 Kind=table/flow 的运行时**本来就会**带 `dataSourceCode+tableName`（`ReportDefinitionAppService.cs:256-267`），所以这不是一条「只有攻击者会走」的路径。
- **规避**：不要给租户用户 `Orchestration.Instances.Run`（但这同时会禁掉全部自定义逻辑调用，见 C3）。

### C3 【严重】破坏性 DDL 无门禁：删列/改类型/改列名会直接下发

- **现象**：在设计器里删掉一个业务列后点「应用到库」，会真的执行 `ALTER TABLE … DROP COLUMN`；改类型/改短/改非空同样直接执行；改列名执行 `RENAME`/`sp_rename`。
- **机理**：`SqlDdlExecutor.Preview` 会生成 `dropColumn` / `dropIndex` / `renameColumn` / 破坏性 `alterColumn`（`SqlDdlExecutor.cs:191-204`、`:168-176`、`:179-188`），且 `DdlPreviewItem.Destructive` 只被**回传前端**（`TableDefinitionAppService.cs:130-137`），`ApplyAsync` 完全不读它：`SqlDdlExecutor.cs:242-291` 对 `statements` 逐条执行。全仓无 `allowDestructiveDdl`、无表名打字确认、无「仅非破坏性子集」开关。
- **唯一的硬阻断**：`DROP TABLE` / `TRUNCATE`（`SqlDdlExecutor.cs:254-257`、`:505-513`）——而这两者本来也不会被生成。
- **后果**：一次误删列即数据丢失，且没有 dry-run 之外的第二次确认。原文承诺的「破坏性默认关闭 + 打字确认」**在代码里不存在**。
- **规避**：限制 `Orchestration.Tables.Ddl` 的授予；应用前必备份。根治见 §5.1。

### C4 【严重】任何持 `Orchestration.Instances.Run` 者可绕过 `Reports.Execute` 与报表 DataScope

- **现象**：报表运行时（`POST reports/{code}/query`）要求 `Orchestration.Reports.Execute`；但同一份查询可直接 `POST /api/logic/sys.resource.query`，自带 `resourceCode` / `filter` / `columns` / `summaryFields`，只要 `Instances.Run`。
- **机理**：
  - `LogicApiAppService.cs:35` 只要求 `Instances.Run`，并以 `skipValidation: true` 执行（`:61`），因此连节点级声明校验（`FlowExecutor.cs:2496-2543`）都被跳过；
  - `/api/logic/{flowKey}` 路由无额外授权（`LogicApiController.cs:22-24`）；
  - 执行器只校验 `Sql.Write`（写）与租户谓词，不校验 `Reports.Execute` / `Resources.Execute`（`ResourceCrudExecutor.cs:537-543`、`FilterSqlBuilder.cs:54-71`）。
- **后果**：只要 `Instances.Run`，越过了资源/报表的发布状态与授权语义（报表 DataScope 仍可通过 `filter` 自行构造等价条件，但**不通过** `Reports.Execute` 这条权限边界）。
- **边界（写实，不要夸大）**：**跨租户仍然被挡住**——租户谓词是执行器内部注入的独立谓词，`filter` 里的 `or` 逃不出（§3.7）；被绕开的是「报表/资源级授权与配置」，不是「多租户隔离」。
- **规避**：`Instances.Run` 视为高权权限；若需要细粒度，需按 `Logic.{flowKey}` 拆权限（§5.3）。

### C5 【高】列级过滤缺失：`columns` 只对物理列放行

- **现象**：`POST /api/logic/sys.resource.query` 传 `columns: ["SecretSalary"]` 即可拿到该列，即使资源 `ListView` 里它 `Visible=false`；`get` 干脆返回全部列。
- **机理**：`ResolveSelectColumns` 的白名单是 `schema.Columns`（物理列全量）（`ResourceCrudExecutor.cs:783-805`）；只有资源 REST 路径会在调用前注入 `ListView.Visible` 列（`AppResourceAppService.cs:217-221`）；`get` 用 `schema.Columns` 全量（`:310-312`）且不套 `ListView`。
- **后果**：字段级「可见/可写」目前只有前端语义（⚠️ 未核验），服务端没有强制；如果实施把敏感列放进表单但设 `Visible=false`，它仍然可被查询到。
- **相关**：报表路径反而是安全的（`ReportDefinitionAppService.cs:281-282` 注入 Visible 列）。

### C6 【高】`batchupdate` 三重缺陷，实际不可用

- **现象**：调用 `resourcebatchupdate` 抛「Missing SQL parameter」；即使修掉参数问题，`WHERE` 也永不匹配；同时未在 item 里提供的列会被写成 `NULL`。
- **机理**：
  1. `BatchOp.Update` 每项从**空** `JsonObject` 起建，只填 `p{col}` 与平台字段，**没有**把 `filter.Args` 合并进去（`ResourceCrudExecutor.cs:651-661`），但 SQL 的 `WHERE` 用了 `filter.WhereSql`，其中含租户参数（`:672`、`FilterSqlBuilder.cs:58`）→ 参数缺失。对比 `BatchOp.Delete` 与 create 前的 `filter` 使用：delete 明确 `filter.Args.DeepClone()`（`:689`）。
  2. 新戳被当成旧戳比对：`args["pConcurrencyStamp"] = NewStamp()`（`:657`），随后 `stampSet` 用它赋值、`stampSql` 用它做 `WHERE ConcurrencyStamp = @pConcurrencyStamp`（`:665-670`）→ 除非极小概率命中，`affected` 恒为 0。
  3. `ResolveColumnValue` 对 item 中**缺失**的列返回 `null`（`ResourceCrudExecutor.cs:737-760`），而 SET 列表覆盖**全部**非约定列（`:633-639`）→ 未提供的列被置 `NULL`（非空列还会直接报错）。这与单条 Update 的「缺省跳过」语义（`:434-437`）不一致。
- **规避**：不要用 `resourcebatchupdate`；用 `ForEach + resourceupdate`（`itemErrorMode=continueOnError`）替代。

### C7 【中】设计器隐藏条件的 `valueSource ≠ literal` 静默变成 `1=1`

- **现象**：按文档配置「隐藏条件 `CreatorId = sys.userId`」（`Exposed=false`）后，SQL 里**没有**这个谓词，列表照常返回全部行——没有报错、没有日志。
- **机理**：`ResolveDesignerValue` 只在 `ValueSource == "literal"` 时返回 `item.Literal`，其它一律 `null`（`FilterSqlBuilder.cs:157-165`）；`BuildPredicate` 遇到空 right 直接返回 `"1=1"`（`:317-320`）。`FilterItemDef.SystemKey` 属性存在（`ResourceSchema.cs:19`）但**全仓无人读取**（grep 仅命中定义与编排无关的 `FlowDslInput.SystemKey`）。
- **后果**：任何「按当前用户/租户自动加隐藏条件」的设计都是**静默失效**；§5.9.2 里那条示例条件在代码里不成立。
- **规避**：隐藏条件只能用 `valueSource=literal` + 固定字面量；动态条件请写进资源绑定的 query flow。

### C8 【中】隐藏条件的 `combine` 会引用到不可见编号而抛错

- **现象**：把 5 个默认条件中的一个设为 `Exposed=false` 后，查询直接抛 `Combine references missing condition no N`。
- **机理**：隐藏项编译只把**隐藏项**的编号放进字典（`FilterSqlBuilder.cs:88-105`），`combine` 却沿用设计器里的整串编号（默认由 `AppResource.cs:221-223` 生成 `"1 and 2 and 3 and 4 and 5"`），`CombineToSql` → `ConditionCombineEvaluator.Validate(combine, predicates.Keys)` 对缺失编号抛错（`FilterSqlBuilder.cs:439`、`ConditionCombineEvaluator.cs:47-54`）。
- **规避**：隐藏条件不要与可见条件共用 `Combine`；隐藏项应独立分支（代码层修复见 §5.2）。

### C9 【中】租户谓词依赖物理 `TenantId` 列，缺列时静默不加

- **现象**：一张没有 `TenantId` 列的表，查询/更新/删除的 `WHERE` 里**没有任何租户条件**，且没有日志也没有异常。
- **机理**：`FilterSqlBuilder.cs:54` `if (colMap.ContainsKey("TenantId"))`——列不存在就整段跳过。`IsDeleted` 同理（`:67`）。
- **边界说明**：Host 上下文（`tenantId == null`）不是「不加谓词」，而是加 `TenantId IS NULL`（`:61-64`）——这回答了原评审清单第 15 问。
- **规避**：托管表默认带 `TenantId`（`TableDefinition.cs:285`），但设计器允许删列？——不允许删（`TableDefinition.cs:221-228`），但允许改类型（§C10）。导入的物理表若缺列则无隔离。

### C10 【中】约定列「不可删」成立，但「不可改类型」不存在

- **现象**：可以把 `Id` 改成 `string`、把 `TenantId` 改成 `text`、把 `ConcurrencyStamp` 改成 `int`；只要名字还在，`UpdateDraft` 全部接受。
- **机理**：`ReplaceColumns` 只校验名字存在（`TableDefinition.cs:221-228`）与类型合法（`:191-195`），不比对约定列的期望类型/可空性；DDL 侧也会按新类型生成 `alterColumn`（`SqlDialect.cs:363-381`），并因 `AbpConventionColumns.IsConvention` 而**不会**被 drop（`SqlDdlExecutor.cs:145-153`、`:193`）。
- **后果**：CRUD 执行器对约定列的写入是硬编码类型假设（`ResourceCrudExecutor.cs:372-378`），类型漂移后会出现运行期绑定错误；多租户谓词仍按名字生成（`FilterSqlBuilder.cs:54-60`），类型不匹配时行为取决于数据库隐式转换。
- **规避**：把 `Tables.Update` 权限收紧；代码层补约定列类型锁定（§5.1）。

### C11 【中】`Unique` / `Default` / `Comment` 三个列属性在 DDL 上大部分无效

- **现象 A**：给列勾 `Unique` 不会产生任何唯一约束，也不会产生唯一索引。
  **机理**：`BuildCreateTable` / `BuildCreateIndexes` 都不读 `TableColumn.Unique`（`SqlDialect.cs:158-212`）；全仓 `.Unique` 只出现在 DTO 映射与索引 `Unique`（grep 6 处，无 DDL 消费）。要做唯一约束必须**加一个 `Unique=true` 的 `TableIndexDef`**（`SqlDialect.cs:207-208`）。
- **现象 B**：`Default` 只识别两种情况。
  **机理**：`MapDefault`（`SqlDialect.cs:128-156`）只处理 `Id`(guid) 与 boolean 且 `Default=="false"`；其余返回 `null`，即**任意字面量与 `now()` 等函数都不下发**。
- **现象 C**：改注释在 Postgres / SQL Server / Oracle 上不产生 DDL。
  **机理**：`BuildCreateTable` 不输出 `COMMENT`（`SqlDialect.cs:158-191`）；`NeedsColumnAlter` 只在 MySQL 分支比较注释（`:375-380`）。
- **后果**：设计器 UI 上可配但库上不生效——典型的「配置被接受但不生效」。

### C12 【中】没有字段级/资源级权限，也没有 `App.{resourceCode}.*`

- **现象**：运行时页用固定权限 `Orchestration.Resources.Execute`（`MenuDataSeedContributor.cs:449-464`、`:599-614`），不存在按资源生成的 `App.{code}.Create/Update/Delete` 权限；`ListView` 的列可见性只在资源 REST 路径被用于 SQL 投影（`AppResourceAppService.cs:217-221`），不是权限。
- **机理**：权限注册是静态的（`SaaSPermissionDefinitionProvider.cs`），资源发布流程（`AppResourceAppService.cs:96-104`）只校验 flowKey 是否已发布，不登记任何权限或菜单。
- **后果**：资源之间的读写权限无法区分；谁有 `Resources.Execute` 谁就能调所有已发布资源。

### C13 【中】表单库的四点边界

1. **拍平被门控**：`EnsureFlattenedFields` 是覆盖式同步，但只在 `extracted.Count > 0` 时生效（`FormDefinitionAppService.cs:224`）。把 Layout 里所有字段删空后，`Fields` 保留旧值 → 发布会把陈旧字段定义发出去。
2. **快照为空回落草稿**：`GetEffectiveSchema() => PublishedSchema ?? Schema`（`FormDefinition.cs:89`）；`PublishedSchema` 为空时消费方拿到的是**可继续漂移的草稿**。已发布且 `SchemaVersion>0` 时不会发生，但历史数据（发布前建的记录）会。
3. **`resync-catalog` 不重推字段**：只把资源 `ListView.Columns` 写进 `FieldCatalog`，`Schema` 原样传回（`FormDefinitionAppService.cs:196-203`）——与「重新同步字段」的直觉不同。
4. **`ColumnName` 不参与执行**：只在发布校验时用作「字段 → 资源列」的比对键（`FormDefinitionAppService.cs:350`）；`ResourceCrudExecutor` 只认物理列名，不做字段名映射，所以「字段名 ≠ 列名」的表单提交后**写不进库**（键名对不上，被当作未知键忽略）。
- 删除引用检查是「先查后删」（`FormDefinitionAppService.cs:138-155`），且 Mongo 未启用跨集合事务（`AbpUnitOfWorkDefaultOptions.TransactionBehavior = Disabled`），存在竞态。

### C14 【中】报表按钮 / KPI / 预设 / 高级筛选开关没有服务端执行端

- **按钮**：`ActionDef` 全仓只出现在模型（`ResourceSchema.cs:227-272`）、默认生成（`AppResource.cs:153-185`）、报表克隆（`ReportDefinition.cs:278-309`）、DTO 与引用检查（`FormDefinitionAppService.cs:144`、`:150`）；没有任何服务端执行/校验端。按钮只能由前端自行调 `/api/logic/{flowKey}`（⚠️ 未核验）。因此 `ActionDef.FlowKey`「不允许为空」这条约束**服务端不校验**。
- **`BatchLimit` / `PayloadFields` / `FieldsMode` / `After` / `Confirm` / `Scene` / `Scope` / `Open` / `FormKey`**：均为纯配置字段，无服务端消费点（`ResourceSchema.cs:241-271`）。
- **`Kpis` / `Presets` / `SearchForm` / `AdvancedFilter`**：持久化 + 回读（`ReportDefinition.cs:33-39`、`ReportDefinitionAppService.cs:422-425`、`AppBuilderDtos.cs:301-309`），`FilterSqlBuilder` 完全不读。特别是 **`AdvancedFilter=false` 没有任何服务端效果**——条件树照旧被编译。

### C15 【中】表/查询对象删除只删元数据，且引用检查非原子

- `TableDefinitionAppService.DeleteAsync`（`:108-119`）：有资源引用时拒绝；否则删除元数据文档。**物理表与索引不动**（DDL 层只允许 `DROP INDEX`，`SqlDdlExecutor.cs:520-528`）。再建同名表时 `Preview` 会看到物理列已存在而走 diff 分支，不会重新 `CREATE TABLE`。
- `DbQueryObjectAppService.DeleteAsync`（`:202-216`）同样是先查后删。
- 二者都在 `TransactionBehavior = Disabled` 下运行，存在竞态窗口。

### C16 【中】DDL 事务对 MySQL / Oracle 不具备回滚语义

- `ApplyAsync` 用 `BeginTransactionAsync` 包裹全部语句（`SqlDdlExecutor.cs:264-290`），但 MySQL 的多数 DDL 会**隐式提交**、Oracle 的 DDL 是自动提交的自治事务。因此失败时无法真正回滚已执行的前序语句，`syncState` 仍会被置为 `inSync`（`TableDefinitionAppService.cs:150` 在 `ApplyAsync` 返回后无条件 `MarkApplied`）。

### C17 【低】若干判定细节

- `UpdateAsync` 用 `sets.Count == 3` 判断「没有业务字段」（`ResourceCrudExecutor.cs:467-470`）——这是把三个平台字段的数量写死在判定里，新增平台字段即失效。
- 列名比较混用大小写敏感性：软删判定 `c.Name == resource.SoftDeleteField`（`:516`、`:598`）是**大小写敏感**的序数比较，而列映射 `colMap` 是大小写不敏感的（`FilterSqlBuilder.cs:52`）。表里若有 `isdeleted` 列，软删判定为 false → 走物理删除。
- `DeleteAsync` 不校验 `AffectedRows`（`:533-534`），删不存在的 id 也返回 `deleted: true`。
- `FormDef.SubmitFlowKey`（`ResourceSchema.cs:458`）只被克隆（`FormDefinitionAppService.cs:419`、`ReportDefinition.cs:314`），无执行端。

### C18 【中】关键安全面几乎没有测试

`tests/Meta.Dow.Orchestration.Tests` 只有 5 个测试类（62 个用例）：`FlowExecutorControlFlowTests`（984 行）、`WorkflowDefinitionValidatorTests`（245）、`FormSchemaFlatteningTests`（169）、`SecretsConfigSafetyTests`（109）、`WorkflowRecordPatchTests`（68）。

> ⚠️ **2026-10-14 更新（`91d75bd`）**：该工程已增至 **6 个测试类 / 91 个用例** —— P0 止血批次新增 `FlowExecutorDslValidationTests`（314 行、29 例，断言节点白名单/输出契约/`visibleTo`/PG SQLSTATE），并给存量夹具补了输出契约。**新增用例仍是纯单测**，下文"没有任何测试覆盖…"的判断不变。

**没有任何测试**覆盖：`FilterSqlBuilder`、`SqlDialect`、DataScope、租户过滤、`ResourceCrudExecutor`、`SqlDdlExecutor`/DDL diff。

> 结论：本文第 3.7 / 3.8 章描述的是一批**只靠人工核对保证**的安全不变量（租户谓词位置、参数化、标识符白名单、批量事务）。§C1 / C6 / C7 / C9 这些缺陷正是缺乏该层测试的直接后果。补测试是 §5.7 的第一优先项。

---

## 5 规划（未实现）

> 本章只写**代码里不存在**的能力。第 2 章已标 ✅/⚠️ 的内容不会在此重复出现。每项标注「为什么需要」与「依赖」。

### 5.1 破坏性 DDL 门禁与结构安全（P0，依赖 §C3 / C10）

1. **破坏性子集开关**：`ApplyAsync` 增加 `allowDestructiveDdl`（默认 false）；`DdlPreviewItem.Destructive == true` 的项在未开启时拒绝执行并返回「本次包含 N 条破坏性变更」。当前 `Destructive` 字段已生成但只回传前端（`TableDefinitionAppService.cs:130-137`），属于「声明即弃」。
2. **打字确认**：请求体带 `confirmTableName`，必须等于目标表名才执行。
3. **约定列类型锁定**：`ReplaceColumns` 校验 `Id/TenantId/ConcurrencyStamp/CreationTime/CreatorId/IsDeleted` 的平台类型与可空性等于约定值（现在只校验名字，`TableDefinition.cs:221-228`）。
4. **`Origin` 服务端化**：`Origin` 不再来自 DTO，由服务端按 `AbpConventionColumns.IsConvention(name)` 判定（见 §5.2）。
5. **DDL 审计留痕**：谁、何时、哪张表、哪些语句、成功/失败、是否破坏性。当前只有 IL logger（`SqlDdlExecutor.cs:276-281`），没有持久化审计与查询入口。
6. **`remoteAhead` / `conflict` 落地**：应用前重新 introspect 并与上次 `AppliedName` 比对，检出「库被外部改过」并给出拉取/覆盖选项（状态常量已定义，`TableDefinition.cs:50-51`）。
7. **MySQL/Oracle DDL 回滚语义说明**：或在 UI 上明示「不可回滚」，或改为逐条提交 + 失败点记录（见 §C16）。

### 5.2 权限与字段级边界（P0，依赖 §C1 / C5 / C12）

1. **`Origin` 服务端判定**：DTO 去掉 `Origin` 入参；由 `AbpConventionColumns.IsConvention` 决定。这是 §C1 的最小修复。
2. **Update 显式黑名单**：`ResourceCrudExecutor.UpdateAsync` 在 SET 列表外再排除 `Id`/`TenantId`/`CreationTime`/`CreatorId`/`IsDeleted`/`DeleterId`/`DeletionTime`/`LastModificationTime`/`LastModifierId`/`ConcurrencyStamp`，与 Create 的强制覆盖对称。
3. **字段级权限**：资源/报表增加「列级 visible / writable」并在 `ResolveSelectColumns`、`GetAsync`、`Create/Update` 三处强制执行（现在只有 REST 路径的资源 query 做了投影）。
4. **`App.{resourceCode}` 权限族**：发布资源时登记 `App.{code}` + `.Create/.Update/.Delete`，并把运行时页菜单改为按资源生成或至少按资源校验（当前是固定 `Orchestration.Resources.Execute`，`MenuDataSeedContributor.cs:449-464`）。
5. **`Instances.Run` 细分**：评估拆出 `Logic.{flowKey}` 或「不允许直接调用系统 `sys.resource.*`」的白名单，以关闭 §C4 的绕过路径。
6. **隐藏条件语义补全**：实现 `FilterItemDef.ValueSource` 的 `sys`/`user`/`query` 与 `SystemKey`（现在只有 `literal`，§C7），并让隐藏项与 `Combine` 解耦（§C8）。
7. **软删/租户缺列显式化**：列映射缺 `TenantId` 时按设计器选项「拒绝查询」或「记录警告」，不要静默（§C9）。

### 5.3 `batchupdate` 修复（P1）

- 每项参数合并 `filter.Args`（与 delete 分支一致，`ResourceCrudExecutor.cs:689`）；
- 旧戳从 item 读取（`record.concurrencyStamp`），新戳只用于 SET；
- 未提供的列跳过而不是置 `NULL`（与单条 Update 一致，`:434-437`）；
- 补单测覆盖这三条。

### 5.4 列 `format` / `render` 的服务端沙箱（P1，模型已存在但无执行端）

模型字段已在 `ListColumnDef`：`FormatFn` / `RenderFn` / `StyleRules` / `DictMap` / `Options` / `DictCode` / `SummaryFn` / `SummaryScope`（`ResourceSchema.cs:181-200`）。**无任何执行器**。

保留原设计论证：

| | **format** | **render** |
|--|------------|------------|
| 输入 | 单元格原值 + 行 + ctx | 同左 |
| 输出 | **字符串**（纯文本） | **描述符**（再交给 Vue 组件画） |
| 典型 | `2026-09-18`、`¥1,234.00`、`已审核` | 状态 Tag、链接、开关、进度、图片 |
| 安全 | 易沙箱 | **禁止返回原始 HTML 字符串** |

- 预设 format（先做，覆盖 80%）：`text` / `date` / `datetime` / `currency` / `number` / `percent` / `enum` / `boolean` / `ellipsis`。
- 自定义 format JS（后置）：纯函数 `(value, row, ctx) => string`；`ctx` 只读（`sys.userId` / `sys.culture` / 字典）；禁止 `fetch` / `document` / 改 `row`；超时与字节上限与编排 Code 沙箱同级（更严：同步、无 `db`）。
- render 返回描述符白名单：`text` / `tag` / `link` / `image` / `progress` / `switch`(只读) / `dict`；`link.href` 只允许站内路由或字段值，禁止 `javascript:`。
- **不首期开放任意 HTML / 任意 Vue SFC**。
- `format` 与 `render` 同时存在时：先 format 成字符串再作为 render 的 `value`，或由 `render.input = raw|formatted` 决定。
- ⚠️ 待统一：`report-config-ux.md` 里 `renderFn` 的「返回 HTML」与本表「只返回描述符白名单」冲突，须以描述符白名单为准。

### 5.5 看板与仪表盘（P3+）

看板定位：**资源的一种 View**，不是独立 App；列定义 = 字段枚举或表达式；拖拽改状态 = 写回字段 + 可选触发工作流/逻辑编排。

```
AppResource
  ├── ListView
  ├── FormDef (create/update/detail)
  ├── KanbanView              ❌ 未实现
  │     ├── columnField（如 Status）
  │     ├── columns[]（值、标题、WIP、颜色）
  │     ├── cardTitleField / cardMetaFields
  │     ├── swimlaneField?（可选）
  │     └── onDrop → 更新字段 + 可选 Workflow / FlowKey
  └── (后) CalendarView / GalleryView
```

| 层级 | 能力 | 依赖 |
|------|------|------|
| L1 | 按单字段分列、拖拽改字段、筛选条复用 `FilterDef` | `resourceupdate` flowKey（已可用）+ 新的 `KanbanView` 元数据 |
| L2 | 泳道、WIP、保存筛选预设（`Presets` 已建模但无执行端，见 §C14）、卡片摘要 | `Presets` 消费端 |
| L3 | 跨资源聚合看板、自定义 Widget 大屏、实时多人光标 | 独立聚合查询层 |

仪表盘与看板区分：看板面向同一资源的状态流转；仪表盘是多 Widget（统计卡、图表、嵌入列表），数据来自聚合查询或 FlowKey。仪表盘工程量大，**默认放在资源/工作流稳定之后**。

学习对象：Supasheet「多视图挂同一资源」；github-board「列 = 过滤表达式」（首期用枚举字段即可，表达式列后置）。

### 5.6 OA 审批流增强（P3+，W3）

已落地能力见 §2.7（79–88）。**未落地**：

- **超时提醒 / 催办 / 到期自动升级**：全仓无 `DueTime` / `Remind` / 超时后台任务；`WorkflowTask` 无到期字段消费点。
- **排他网关 / 并行网关 / 子流程 / 定时边界事件**：现在用 `condition` 节点 + 出边条件 + `priority` 表达分支（`WorkflowDefinitionValidator.cs:145-178`），没有独立网关类型。
- **抄送的送达渠道可插拔**：已有通知广播（`WorkflowRuntimeAppService.cs:394`），但无渠道配置模型。
- **评估 Elsa 3 / Flowable / bpmn-js**：仅在复杂度确实超出「UserTask + 条件分支 + 会签」时再引入；当前自研形态已覆盖 W1+W2 的大部分。
- **前端审批时间轴**：后端 `HistoryJson` 已供数（`:1014-1033`），渲染 ⚠️ 未核验（前端在 vue-demo）。

### 5.7 测试与可观测（P0，横切）

1. `FilterSqlBuilder`：谓词顺序（租户/软删/DataScope 永远是独立顶层 `AND`）、深度上限、字段白名单、每个运算符的参数化、`or` 树不逃租户。
2. `ResourceCrudExecutor`：Create 强制覆盖、Update 不得改 `TenantId`/审计列（§C1 的回归测试）、软删/物理删分支、`batchupdate` 三缺陷（§5.3）、`AffectedRows == 0` 语义。
3. `SqlDdlExecutor.Preview` 的 diff 矩阵（新建/加列/改类型/改名/删列/索引增删），以及破坏性子集门禁（§5.1）。
4. `SqlDialect.QuoteIdent` / `IsQuotable` 的拒绝清单（引号、点、空白、超长、Unicode）。
5. 表单拍平与发布快照的边界（Layout 清空、`PublishedSchema` 为空）。

### 5.8 其它已建模但未落地

| 项 | 模型位置 | 缺什么 |
|----|----------|--------|
| `TableDefinition.Origin = "imported"`（导入表，不强制约定列） | `TableDefinition.cs:72`（值固定 `managed`） | 无 imported 分支、无「从库拉取成设计」入口（introspect 已具备） |
| `AppResource.Filter.Presets` / `SearchForm` / `AdvancedFilter` | `ResourceSchema.cs:102-106` | 服务端消费（§C14） |
| 报表 `Kpis` | `ResourceSchema.cs:461-468`、`ReportDefinition.cs:39` | 聚合执行与返回（现在 `summary` 走的是 `summaryFields`） |
| `ActionDef` 全字段 | `ResourceSchema.cs:227-272` | 服务端执行端与 `FlowKey` 必填校验（§C14） |
| 列 `Unique` / 任意 `Default` / `Comment` | `TableDefinition.cs:27-31` | DDL 下发（§C11） |
| `FormFieldDef.ColumnName` / `FormWidgetDef.ColumnName` | `ResourceSchema.cs:325`、`:403` | CRUD 执行期的字段名↔列名映射（§C13-4） |
| `FormDef.SubmitFlowKey` | `ResourceSchema.cs:458` | 提交执行端（§C17） |
| `AppResource.TenantField` / `ConcurrencyField` | `AppResource.cs:33-35` | 执行器硬编码列名，未读这两个属性 |

### 5.9 分期建议（按现状重排）

| 阶段 | 内容 | 依据 |
|------|------|------|
| **已交付** | 表设计器（含 DDL 应用）、资源 + 5 系统逻辑 + 运行时 REST、组合查询与条件树、查询对象（视图/过程）、通用报表、表单库、OA 审批流 W1+W2、菜单与权限种子 | 第 2 章 ✅ 项 |
| **P0（当前应做）** | §5.1 破坏性 DDL 门禁、§5.2-1/2 `Origin` 与 Update 黑名单、§5.3 `batchupdate` 修复、§5.7 测试补齐 | §C1/C3/C6/C18 |
| **P1** | 字段级权限与列投影（§5.2-3/4）、`Instances.Run` 细分（§5.2-5）、隐藏条件语义（§5.2-6/7）、列 format/render 沙箱（§5.4） | §C4/C5/C7/C8 |
| **P2** | DDL 审计与 `remoteAhead`/`conflict`、`App.{resource}.*` 权限族、表单字段映射执行、导入表（§5.8） | §C12/C13/C15/C16 |
| **P3** | 看板 L1 + 审批超时/催办 + 资源事件绑定工作流 | §5.5/§5.6 |
| **P4** | 看板 L2、仪表盘 Widget、Calendar/Gallery、评估 Elsa/Flowable/bpmn-js、Mongo 集合设计器 | §5.5/§5.6 |

### 5.10 待评审（仍需人拍板）

1. `Instances.Run` 是否允许直接调用系统 `sys.resource.*`？还是拆 `Logic.{flowKey}`？（§C4）
2. 破坏性 DDL 是「P0 彻底禁止」还是「开关 + 打字确认」？（§C3）
3. 字段级权限的粒度：资源列级 vs `App.{code}.Field.{name}`？（§5.2-3/4）
4. 列 `render` 是否必须沙箱 + 描述符白名单（推荐是）？（§5.4）
5. 多租户下 `TableDefinition` / 资源模板是否支持 Host 下发到租户？
6. 缺 `TenantId` 列的表：拒绝查询还是照旧放行？（§C9）
7. 看板是否必须与列表共用同一 `FilterDef`？
8. 服务落点：继续在 `Meta.Dow.SaaS`（当前实现）还是拆独立服务？——当前是 **SaaS 内**（表单/资源/工作流/报表/编排同处一个服务）。

---

## 6 操作说明 / 交互规格

### 6.1 从零到可用的主路径（后端契约视角）

```
① 登记 DataSource（SQL 族，accessMode 需含 write 才能 DDL）
        │   POST /api/orchestration/data-sources
        ▼
② 建表元数据（自动带 10 个 ABP 约定列 + 3 个默认索引）
        │   POST /api/orchestration/tables   { dataSourceCode, tableName, displayName }
        │   PUT  /api/orchestration/tables/{id}   ← 增删业务列/索引（同步 origin/列名）
        ▼
③ 预览 → 应用到库
        │   GET  /api/orchestration/tables/{id}/preview   （权限 Tables）
        │   POST /api/orchestration/tables/{id}/apply     （权限 Tables.Ddl；DS 必须可写）
        │   → syncState = inSync、lastAppliedAt 写入
        ▼
④ 一键创建资源（要求 inSync）
        │   POST /api/orchestration/tables/{id}/resources { code, name }
        │   → 5 个 flowKey 自动指向 sys.resource.*；列表列/表单/查询表单按表派生
        ▼
⑤ 调整配置 → 发布
        │   PUT  /api/orchestration/resources/{id}   （Filter / ListView / Form / flowKey）
        │   POST /api/orchestration/resources/{id}/publish
        │   → 发布时校验 5 个 flowKey 均已发布，否则 422/业务错误
        ▼
⑥ 终端用户使用（固定权限 Orchestration.Resources.Execute）
            POST /api/orchestration/resources/{code}/query | get | create | update | delete
            POST /api/orchestration/resources/{code}/batch-delete
            → 内部走 PublishedFlowInvoker(sys.resource.*)，可观测实例
```

### 6.2 升级为自定义逻辑（契约不变）

1. 克隆对应 `sys.resource.*` 为 `order.create`（新编码、可编辑、仍可 `isReusable`）。
2. 画布上**保留** `ResourceCreate` 节点（继续安全写库），在前后插入已有节点：
   - 前：Condition / Input `rules` / Code 校验 / Http 取号；
   - 后：子表 `ResourceCreate` + `db.batch` / RabbitMqPublish / SubFlow；
   - 失败：Throw；同库多写开 `txMode=sameDataSource`。
3. 发布 `order.create`。
4. `PUT /resources/{id}` 把 `createFlowKey` 改成 `order.create`；表单 Schema 不用改（入参仍是 `record` + `resourceCode`）。

自定义查询同理：以 `ResourceQuery` 为底，前面加 Condition，或改用 Sql/Code 自己查，但 **End 的输出必须仍是 `{ items, total, summary? }`**（该契约由 `sys.resource.query` 的 `outputs` 定义，`SystemResourceFlowSeedContributor.cs:72-85`）。

> ⚠️ 原文「发布校验：系统注入字段 `source` 应为运行时绑定，或 `visibleTo.none`」**不存在**。当前节点级校验只检查「是否声明了 `resourceCode`（或 `dataSourceCode`+`tableName`）」（`FlowExecutor.cs:2496-2543`），不校验值的来源。把 `resourceCode` 暴露给匿名调用方的风险由调用侧的 `Instances.Run` 权限承担（§C4）。

### 6.3 表设计器配置态 UX（设计稿，⚠️ 未核验前端在 vue-demo）

```
数据连接 → [某 DS] → 表
┌──────────┬─────────────────────────────┬──────────────────┐
│ 表列表    │ 列网格                        │ 列属性           │
│ · orders │ name   type    pk  null 注释 │ 类型/长度/默认   │
│ · lines  │ Id     guid    ●             │                  │
│ + 新建表 │ Status string                │ 索引 Tab：        │
│          │ …                            │  PK / Unique / IX │
└──────────┴─────────────────────────────┴──────────────────┘
顶栏：保存草稿 │ 预览变更 │ 应用到库 │ 同步状态 Tag
```

- 预览表用「将执行的操作」列表展示，破坏性行标红；后端已把 `Destructive` 回传（`TableDefinitionAppService.cs:130-137`），但**服务端不据此拦截**（§C3）。
- 第一次成功应用后 `syncState = inSync`，即可一键创建资源。
- 「约定列」在网格里应不可删、不可改类型（**当前服务端只保证不可删**，§C10）。

### 6.4 资源运行时页结构

列表页：查询表单（`Filter.SearchForm`，最多 3 列布局）→ 工具栏按钮 → 表格（列来自 `ListView.Columns`，`Visible=false` 不下发）→ 分页 → 抽屉/页面表单（`FormDef`，按 mode 覆盖 visible/required/readonly）。

前端路由（后端菜单已种子，`MenuDataSeedContributor.cs:449-614`）：

| 菜单名 | Path | Component | 权限 |
|--------|------|-----------|------|
| 资源运行时 | `/app/:code` | `/orchestration/app/runtime` | `Orchestration.Resources.Execute` |
| 资源表单 | `/app/:code/form/:formKey/:id?` | `/orchestration/app/form` | `Orchestration.Resources.Execute` |
| 报表运行时 | `/app/reports/:code` | `/orchestration/reports/runtime` | `Orchestration.Reports.Execute` |
| 报表表单 | `/app/reports/:code/form/:formKey/:id?` | `/orchestration/reports/form` | `Orchestration.Reports.Execute` |

设计器/配置态路由：`/orchestration/tables`、`/orchestration/views`、`/orchestration/procedures`、`/orchestration/tables/designer`、`/orchestration/resources`、`/orchestration/resources/editor`、`/orchestration/reports`、`/orchestration/reports/editor`、`/orchestration/forms`、`/orchestration/forms/designer`、`/orchestration/workflows`、`/orchestration/workflows/designer`（`MenuDataSeedContributor.cs:351-563`）。

### 6.5 只读性与并发语义（运行时须知）

| 场景 | 语义 |
|------|------|
| `update` 带 `concurrencyStamp` | `WHERE` 追加 `ConcurrencyStamp = @stamp`；命中 0 行 → 抛「Update conflict or record was not found.」。**注意 ABP 对 `UserFriendlyException` 的 HTTP 映射不是 409**，前端不应硬编码 409 来判断冲突 |
| `update` 不带 `concurrencyStamp` | 不做乐观并发（无条件覆盖） |
| `update` 无业务字段 | 抛「No updatable fields were provided.」 |
| `delete` | 有 `IsDeleted` 则软删并写 `DeleterId`/`DeletionTime`；否则物理删。**不校验影响行数** |
| `batchdelete` | 逐条走 `DeleteFlowKey`，部分失败继续；`>500` 直接拒绝；返回 `affected`/`skipped`/`errors`（`AppResourceAppService.cs:145-198`） |
| `query` / `get` | 不落实例（`persistOnSuccess: false`，`AppResourceAppService.cs:120`/`:124`）；`create`/`update`/`delete` 落实例 |
| `/api/logic/{flowKey}` | 每次都写 `FlowInstance`（`LogicApiAppService.cs:80`） |

---

## 7 参考链接与开源对标

### 7.1 表单 / CRUD / 数据驱动应用（能力 A）

| 项目 | 地址 | 亮点（建议学什么） | 不宜照搬 |
|------|------|-------------------|-----------|
| **NocoBase** | https://github.com/nocobase/nocobase | 数据模型与 UI 解耦；块（Block）拼页面；数据源可外挂；配置态/使用态切换 | 整平台替换 ABP；插件宇宙过重 |
| **Supasheet** | https://github.com/supasheet/supasheet | 从 Schema 自动生成 CRUD；Grid/Kanban/Calendar/Gallery 多视图；条件字段、FK 级联 | 强绑 Supabase/Postgres |
| **Directus** | https://github.com/directus/directus | 数据模型 + 表结构同步，可对照 DDL 心智 | 自成一套管理端 |
| **ToolJet** | https://github.com/ToolJet/ToolJet | 数据源 + Query 编辑器 + 拖拽组件；表格/表单分离 | 通用 Retool 式，缺「资源一等公民」模型 |
| **ILLA Builder** | https://github.com/illacloud/illa-builder | Action（查询）与 Component 绑定清晰 | 偏画布 App，不是「业务资源」 |
| **Budibase** | https://github.com/Budibase/budibase | 连库后自动生成表单 | 自带 DB 心智与白名单 DataSource 不同 |
| **Formily + Designable** | https://github.com/alibaba/formily | JSON Schema 表单运行时强；联动/校验成熟 | Designable 偏 React；Vue 侧适配成本 |
| **form-js** | https://github.com/bpmn-io/form-js | 轻量 Schema 表单 + 可视化编辑 | 字段类型偏流程表单，复杂 CRUD 不足 |
| **Amis** | https://github.com/baidu/amis | JSON 描述整页 | 运行时体积大；与 Vben 组件体系两套 |

**对本仓库最贴合的组合参考**（结论未变）：

1. **资源模型**：学 NocoBase / Supasheet（Collection + Views，而不是一张「万能画布页」）。
2. **表结构**：学 NocoBase Collection / Directus Data Model（可视化列/主键/索引，diff 后同步到库）。
3. **表单 Schema**：本仓库已自研（`FormDef`/`FormFieldDef`/`FormWidgetDef`/`FormFieldDependencyDef`，`ResourceSchema.cs:287-459`），**未引入 Formily / form-js**；保留「Schema 稳定比设计器炫酷更重要」的原则。
4. **查询/列表**：学 ToolJet Query + Table，但查询定义挂在「资源」下；条件组合对齐本仓库已有 Condition 模型，不要第三套语法（已落地为 `FilterNode` 树）。

### 7.2 工作流 / BPM（能力 B）

| 项目 | 地址 | 亮点 | 不宜照搬 |
|------|------|------|-----------|
| **bpmn-js / Camunda Modeler** | https://github.com/bpmn-io/bpmn-js、https://github.com/camunda/camunda-modeler | 标准 BPMN 设计器；属性面板；与 form-js 可组合 | 完整 Camunda 运行时运维成本高 |
| **Flowable / Camunda 引擎** | Flowable、Camunda 7/8 | 成熟人审、多实例、监听器、历史 | 与 ABP 权限/多租户要深度适配 |
| **Elsa Workflows** | https://github.com/elsa-workflows/elsa-core | .NET 原生；Activity 模型；可自托管 | 偏开发者工作流，业务审批 UX 需自建 |
| **Open BPMN** | https://github.com/imixs/open-bpmn | 可嵌入的建模；纯 Java 元模型 | 与 Vue/ABP 栈略远 |
| **国内审批设计器** | 各类 X6 审批 demo | 钉钉式「发起人→审批人」极简 UX | 扩展到会签/条件网关后常崩 |

**现状结论**：本仓库已自研轻量引擎并覆盖 W1+W2 大部分（§2.7），**未引入 Elsa / Flowable / bpmn-js**。是否升级取决于 §5.6 里剩余缺口（超时、网关、子流程）是否成为刚需。

### 7.3 看板 / 仪表（能力 E）

| 项目 | 地址 | 亮点 | 不宜照搬 |
|------|------|------|-----------|
| **Supasheet Views** | https://github.com/supasheet/supasheet | 同一资源多视图（Kanban 只是 ViewType） | — |
| **github-board** | https://github.com/TomzxCode/github-board | 列/泳道 = 过滤表达式；预设可分享 | 只读 GitHub，无写回 |
| **Easy Kanban / Agila** | https://github.com/drenlia/easy-kanban | 多看板、WIP、List/Gantt、保存筛选 | 独立产品，不是「资源视图」 |
| **LobsterBoard** | https://github.com/Curbob/LobsterBoard | 拖拽 Widget 仪表盘 | 偏运维监控，非业务看板 |
| **NocoBase Blocks** | https://github.com/nocobase/nocobase | 页面由块拼装（表、图、看板） | 块系统工程量大 |

### 7.4 本仓库文档

- [`logic-orchestration-contract.md`](./logic-orchestration-contract.md) — 逻辑编排（能力 D）：Condition 组合式、DataSource、`PublishedFlowInvoker`、节点库
- [`oa-contract.md`](./oa-contract.md) — OA 审批流节点规则与表单绑定
- [`report-contract.md`](./report-contract.md) — 通用报表、查询表单/条件组、列汇总、按钮、菜单
- [`form-preview.md`](./form-preview.md) — 表单库与预览；`FormDefinition` 与编排 `InputSchema` 的分工
- [`form-contract.md`](./form-contract.md) — 表单/审批增强项（F1–F7、W1–W9）
- [`report-config-ux.md`](./report-config-ux.md) — 报表配置交互（⚠️ `renderFn` 返回 HTML 与本仓「描述符白名单」冲突，见 §5.4）
- [`identity-contract.md`](./identity-contract.md) — 权限与菜单

---

## 8 修订记录

| 日期 | 代码基线 | 说明 |
|------|----------|------|
| 2026-09-18 | — | 初稿：开源对标 + 表单/工作流/看板规划，明确不实现 |
| 2026-09-21 | — | 拍板并细化：P1 新建表即应用到库；查询/写入一律 `PublishedFlowInvoker`；前端 P1 即可组合查询；托管表默认 ABP 约定列 |
| 2026-09-21 | — | 平台类型映射补齐 SQL Server / Oracle；查询 COUNT/分页/空值谓词/日期布尔绑定按引擎方言处理 |
| 2026-09-21 | — | 运行时筛选改为查询表单 + 可视化条件组；通用报表规划见 `report-contract.md` |
| 2026-10-13 | `267d375` | **按代码重写为「事实契约 + 规划」双段结构**（Release 0 错误、62/62 单测通过）。核实并改写/删除的失真断言如下 |

### 8.1 本次删除或改写的失真断言（原断言 → 代码真相 → 证据）

| # | 原断言（旧版位置） | 代码真相 | 证据 |
|---|-------------------|----------|------|
| 1 | §5.7.3「约定列允许改注释，**不允许删、不允许改类型**」 | 只保证「名字必须存在」；类型/可空性可任意改 | `TableDefinition.cs:221-228`（无类型比对） |
| 2 | §5.7.3「`Id` NOT NULL，默认 `gen_random_uuid()`/`NEWID()`」 | 元数据层 `Id` 无默认值；仅生成 DDL 时按 provider 补；MySQL/Oracle 无默认 | `TableDefinition.cs:284`、`SqlDialect.cs:131-140` |
| 3 | §5.7.3「业务列可加唯一约束」，§5.7.2「default 支持字面量或白名单函数」 | 列 `Unique` 不产生任何 DDL；`Default` 只识别 `Id`(guid) 与 boolean `false` 两种情况 | `SqlDialect.cs:158-212` 未读 `Unique`；`SqlDialect.cs:128-156` |
| 4 | §5.7.4「删列/删表/删索引默认关闭；需 `allowDestructiveDdl=true` + 打字确认表名」 | 无开关、无确认：`ApplyAsync` 对 Preview 的全部语句逐条执行（含 DROP COLUMN / RENAME） | `SqlDdlExecutor.cs:242-291`、`:191-204`、`:168-176` |
| 5 | §5.7.4「改列名不首期做自动 RENAME」 | `renameColumn` 已生成并执行 | `SqlDdlExecutor.cs:168-176` |
| 6 | §5.7.5「禁止操作系统表 / `pg_*` / `information_schema`；表名黑名单」 | 无黑名单，仅 `SqlIdentifier.IsValid` 字符集校验 | `TableDefinition.cs:186-189`、`TablePlatformType.cs:148-172` |
| 7 | §5.7.5「新权限 `Orchestration.DataSources.Ddl`（或 `AppBuilder.Tables.Ddl`）」 | 实际门禁权限是 `Orchestration.Tables.Ddl` | `TableDefinitionAppService.cs:140`、`OrchestrationPermissions.cs:41` |
| 8 | §5.8 资源 JSON（`binding:{kind,table}`、`primaryKey:["Id"]`、`softDeleteField`…） | 真实契约是扁平属性，`PrimaryKey` 为单值字符串；无 `binding` 对象 | `AppResource.cs:19-45`、`AppBuilderDtos.cs:122-157` |
| 9 | §5.9.2 示例「`CreatorId eq sys.userId`（`exposed=false`）」 | `valueSource≠literal` 的隐藏条件被静默编译为 `1=1`；`SystemKey` 无消费点 | `FilterSqlBuilder.cs:157-165`、`:317-320`、`ResourceSchema.cs:19` |
| 10 | §5.9.3-2「可嵌套条件组（最多 3 层）」 | 常量是 `MaxFilterGroupDepth = 3`，且判定为 `depth > 3` 抛错（根 `depth=0` ⇒ 最多 4 层 group） | `FilterSqlBuilder.cs:228-233`、`OrchestrationConsts.cs:14` |
| 11 | §5.10.2 系统逻辑入参 JSON 带 `"default": 1` / `"default": 20` | 种子 DSL 的入参全部只有 `"source": "input"`，**无 default**；默认值在执行器 | `SystemResourceFlowSeedContributor.cs:44-57`、`ResourceCrudExecutor.cs:193-198` |
| 12 | §5.10.4「发布校验：系统注入字段 `source` 应为运行时绑定，或 `visibleTo.none`」 | 不存在；节点级校验只检查「是否声明了 `resourceCode`（或 `dataSourceCode`+`tableName`）」 | `FlowExecutor.cs:2496-2543` |
| 13 | §5.4「字段投影：列表不得默认 `SELECT *`（按 ListView 列配置投影）」 | 仅资源 REST 路径注入 `ListView.Visible` 列；直连编排用**全部物理列**，`get` 返回全部列 | `AppResourceAppService.cs:217-221`、`ResourceCrudExecutor.cs:783-805`、`:310-312` |
| 14 | §5.12.1 `ActionDef` 字段表含 `icon`/`visibleWhen`/`acl` | 模型无这三个字段；`BatchLimit`/`PayloadFields`/`FieldsMode`/`After`/`Confirm` 等无服务端消费 | `ResourceSchema.cs:227-272` |
| 15 | §5.13.2「发布时登记菜单 `/app/{resourceCode}`，权限 `App.{resourceCode}` + `.Create/.Update/.Delete`」 | 菜单是**静态种子**：`/app/:code`、`/app/reports/:code`，权限固定为 `Orchestration.Resources.Execute` / `Reports.Execute`；无按资源生成的权限 | `MenuDataSeedContributor.cs:449-464`、`:565-614` |
| 16 | §5.13.5「发布前须校验 query 的 End 含 `items`/`total`」 | 只校验 5 个 flowKey 存在且已发布 | `AppResourceAppService.cs:279-297` |
| 17 | §5.4「写操作需 Resource 级权限 + DS `accessMode` + `Sql.Write`」 | 无「Resource 级权限」；实际为 `Resources.Execute`（REST 入口）+ `Sql.Write`（写节点）+ `accessMode` | `AppResourceAppService.cs:118-136`、`ResourceCrudExecutor.cs:537-543`、`ParameterizedSqlExecutor.cs:286-294` |
| 18 | §7.1 看板 = 资源视图（`KanbanView` 结构图） | 全仓无 `Kanban`/`Swimlane` 实现 | grep 无命中 |
| 19 | §13.1-9「工作流仍缺 `manager` 直属部门主管解析（未接组织树）」 | **已实现**：`WorkflowOrgResolver` 走 ABP Identity 组织单元 + 角色，逐级上溯 | `WorkflowOrgResolver.cs:38-80`、`WorkflowRuntimeAppService.cs:625-632` |
| 20 | §13.1 新增缺口「`assigneeType=role` 无显式 `case` 分支，落 `default`」 | `role` 有显式分支 | `WorkflowRuntimeAppService.cs:622-624` |
| 21 | §13.1-2「破坏性 DDL 仍按规划默认禁止，代码未见 `allowDestructiveDdl` 开关」 | 结论修正：开关确实不存在，但**破坏性 DDL 会执行**，不是「被禁止」 | 见本表 #4 |
| 22 | §13.1 通篇 `file:///d:/Project/...` 绝对链接 | 全部改为仓库相对路径；跨文档改 `./xxx.md` | — |
| 23 | 旧版「§5 规划」中把已交付能力（表设计器/系统 CRUD/组合查询/表单库/审批流）与规划混写 | 拆分为：第 2/3 章事实契约、第 5 章只写未实现 | 本文结构 |

### 8.2 本次新增的发现（原文档未记载）

| 编号 | 发现 | 证据 |
|------|------|------|
| C1 | `Origin` 客户端可控 + Update 白名单基于 `Origin` ⇒ 跨租户搬行 + 审计篡改 | `TableDefinitionAppService.cs:198`、`TableDefinition.cs:200`、`ResourceCrudExecutor.cs:415` |
| C2 | `tableOverride` 丢弃资源级 DataScope 与投影，且报表 table/flow 来源**主动**走这条路 | `ResourceCrudExecutor.cs:101-124`、`:209`、`ReportDefinitionAppService.cs:256-267` |
| C3 | 破坏性 DDL 无门禁（`Destructive` 只回传前端） | `SqlDdlExecutor.cs:242-291` |
| C4 | `/api/logic/{flowKey}` 只需 `Instances.Run` 且 `skipValidation: true`，绕过 `Reports.Execute`/`Resources.Execute` | `LogicApiAppService.cs:35`、`:61` |
| C6 | `batchupdate` 缺 `filter.Args`、新戳当旧戳、缺列置 NULL | `ResourceCrudExecutor.cs:651-670`、`:737-760` |
| C7 | 隐藏条件 `valueSource≠literal` 静默变 `1=1`，`SystemKey` 无消费 | `FilterSqlBuilder.cs:157-165` |
| C8 | 隐藏项与可见项共用 `combine` 时抛 `missing condition no` | `FilterSqlBuilder.cs:88-105` |
| C9 | 租户谓词依赖物理列；Host 时为 `TenantId IS NULL`（回答了旧评审第 15 问） | `FilterSqlBuilder.cs:54-65` |
| C16 | DDL 事务对 MySQL/Oracle 无回滚语义，且失败后 `syncState` 仍写 `inSync` | `SqlDdlExecutor.cs:264-290`、`TableDefinitionAppService.cs:149-151` |
| C18 | 无任何 `FilterSqlBuilder`/`SqlDialect`/DataScope/租户过滤/`ResourceCrudExecutor`/DDL 测试 | `tests/Meta.Dow.Orchestration.Tests` 文件清单 |
