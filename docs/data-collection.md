# 数据集合定义

外部库白名单、物理表、已有视图和存储过程、以及资源怎么查怎么写。对标 NocoBase 的 Collection 和 Directus 的 Data Model：数据和界面分开，而不是一张万能画布。

证据在 [数据集合契约](./detail/data-collection-contract.md) §2.1–§2.4、§4、§5。契约基线 `267d375`。其中 §5.3 把批量更新写成待修，该缺陷已在 `91d75bd` 修掉，不要再排期。

## 已实现

| 段 | 已实现 |
|---|---|
| 数据源 `DataSource` | postgres / mysql / sqlserver / oracle / redis / mongodb。访问模式和允许的读写操作 |
| 表 `TableDefinition` | 托管表带 10 个约定列和 3 个索引。DDL 可预览、可应用。四家 SQL 可反读结构。约定列不能删。只读数据源不能下发 DDL |
| 查询对象 `DbQueryObject` | 登记已有视图和存储过程，刷新列与参数，再转成资源 |
| 资源 `AppResource` | 筛选、列表、表单和 5 个 flowKey。查询和增删改全部进入编排。条件树参数化，租户条件与用户 OR 并列后再 AND |

Redis / Mongo 只作为 Code 节点的数据源，没有表设计器。

## 相对对标的不足

| 对标有的 | 本仓现状 |
|---|---|
| Directus / NocoBase 把导入表当成一等公民 | 只有托管表。同步状态没有 remoteAhead / conflict 的写入 |
| 系统字段由服务端锁定 | 列 `Origin` 来自客户端。Update 可能改到 `TenantId` 和审计列 |
| 破坏性结构变更默认拒绝 | 删列、改类型、改列名会直接执行。`DROP TABLE` / `TRUNCATE` 被拦住了 |
| Supasheet 同一集合多种视图 | 一个资源一份列表。看板、日历、画廊没有 |
| NocoDB 条件组在界面和执行两侧一致 | `SearchForm`、`AdvancedFilter`、`Presets` 只保存，不参与 SQL |
| 列级可见、可写 | 查询可以按物理列要数据。没有按资源登记的列权限 |
| 列的唯一、默认值、注释进入 DDL | `Unique` 不生成约束。默认值只覆盖少数硬编码情况。注释基本不下发 |

## 后续规划

| 优先级 | 事项 |
|---|---|
| P0 | 破坏性 DDL 默认关闭，要显式开关，并核对表名后才执行 |
| P0 | `Origin` 改由服务端按约定列名判定。Update 的 SET 排除主键、租户和审计列 |
| P0 | 约定列的类型和可空性锁定，不只校验名字还在 |
| P1 | 资源列级可见 / 可写，查询、读取、写入三处都执行 |
| P1 | 发布资源时登记 `App.{code}` 权限，运行时菜单按资源校验 |
| P1 | 隐藏条件支持 `sys` / `user` / `query` 取值。缺 `TenantId` 时拒绝或告警，不要静默不加租户条件 |
| P1 | 列格式与渲染走描述符白名单（文本、标签、链接、进度），禁止返回 HTML |
| P2 | 导入表来源；DDL 审计；`remoteAhead` / `conflict` |
| P2 | 列 `Unique`、默认值、注释真正下发 |
| P3 | 看板作为资源的一种视图：按字段分列，拖拽走 `resourceupdate` |

明确不做：在这个模块里创建视图、存储过程、触发器；把 ALTER TABLE 画进业务编排。
