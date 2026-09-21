# Meta.Dow 表单定义 / 工作流 / 看板规划

> **性质**：产品与架构规划文档。**本期只定边界、模型与分期，不实现功能。**  
> **关联**：逻辑编排见 [`lowcode-logic-orchestration.md`](./lowcode-logic-orchestration.md)（能力 D）。本文覆盖此前刻意拆开的 **能力 A（表单/CRUD 页）**、**能力 B（人审工作流）**，以及 **看板/定制视图**。  
> **前端**：`D:\Project\vue-demo`（web-antd）  
> **后端**：`demo-microservice`（建议仍落 SaaS 或后续独立 App 服务，见 §8）

**一句话**：以「数据源 → **表设计器（结构/主键/索引，同步 DDL）** → 资源 → **组合查询 + 列表（列 format/render + 按钮）** → 新建/编辑表单 → 逻辑编排挂钩 → 可选工作流」为轴，做可配置的业务应用壳；逻辑编排只负责「算与写」，不做人审待办。

---

## 目录

1. [为何单独成文](#1-为何单独成文与逻辑编排的边界)
2. [开源对标（GitHub）](#2-开源对标github-设计较好的参考)
3. [目标用户与场景](#3-目标用户与场景)
4. [产品心智模型](#4-产品心智模型你描述的链路)
5. [表单与资源定义系统](#5-表单与资源定义系统规划)（含 [表设计器](#57-表设计器datasource--物理表)、[组合查询](#59-组合式查询复用编排-condition-模型)、[列 format/render](#511-列表列显示--format--render)、[按钮与表单挂钩](#512-按钮表单与编排挂钩)）
6. [工作流引擎规划](#6-工作流引擎规划)
7. [看板与复杂视图定制](#7-看板与复杂视图定制)
8. [与现有平台能力的衔接](#8-与现有平台能力的衔接)
9. [领域模型草案](#9-领域模型草案仅规划)
10. [非功能与风险](#10-非功能与风险)
11. [分期建议](#11-分期建议实现前必须评审)
12. [明确不做什么（首期）](#12-明确不做什么首期)
13. [评审清单](#13-评审清单规划合理后再开工)
14. [参考链接](#14-参考链接)

---

## 1. 为何单独成文：与逻辑编排的边界

| 能力 | 文档 | 解决什么 | 典型节点/对象 |
|------|------|----------|----------------|
| **D. 逻辑编排** | `lowcode-logic-orchestration.md` | 无阻塞业务逻辑：校验、算价、调 API、SQL、消息 | Start / IF / HTTP / Code / Sql |
| **A. 表单与资源页** | **本文** | 表结构、筛查、列表、新建/编辑表单 | Table / Resource / View / FormSchema / Filter |
| **B. 人审工作流** | **本文** | 待办、会签、转办、超时、状态机 | UserTask / Gateway / Candidate |
| **E. 看板/仪表** | **本文** | 列/泳道/卡片规则、多视图、轻量仪表 | Board / ColumnRule / Widget |

三者可互相调用，但**模型与运行时必须分离**：

```
[资源页：列表/表单/看板]
        │ 提交 / 字段变更 / 拖卡片
        ▼
[逻辑编排 FlowKey]  ←→  同步算完即返回（无人工等待）
        │
        ▼（可选）
[工作流实例]  ←→  人审、待办、阻塞，可再调 FlowKey 做前后置逻辑
```

**禁止**把人审画进逻辑编排画布（与现有文档「不做 BPM」一致）；工作流单独设计器与引擎。

---

## 2. 开源对标（GitHub）：设计较好的参考

下列项目按「可借鉴点」筛选，**不是直接 fork 进仓库**。规划评审时对照其信息架构即可。

### 2.1 表单 / CRUD / 数据驱动应用（能力 A）

| 项目 | 地址 | 亮点（建议学什么） | 不宜照搬 |
|------|------|-------------------|-----------|
| **NocoBase** | [nocobase/nocobase](https://github.com/nocobase/nocobase) | **数据模型与 UI 解耦**；块（Block）拼页面；数据源可外挂；工作流插件化；配置态/使用态一键切换 | 整平台替换 ABP；插件宇宙过重 |
| **Supasheet** | [supasheet/supasheet](https://github.com/supasheet/supasheet) | 从 Schema **自动生成 CRUD**；Grid / Kanban / Calendar / Gallery **多视图**；表单分区、条件字段、FK 级联 | 强绑 Supabase/Postgres |
| **ToolJet** | [ToolJet/ToolJet](https://github.com/ToolJet/ToolJet) | 数据源 + Query 编辑器 + 拖拽组件；表格/表单分离；JS 表达式点缀 | 通用 Retool 式，缺「资源一等公民」模型 |
| **ILLA Builder** | [illacloud/illa-builder](https://github.com/illacloud/illa-builder) | Action（查询）与 Component 绑定清晰；适合内部工具 | 偏画布 App，不是「业务资源」 |
| **Budibase** | [Budibase/budibase](https://github.com/Budibase/budibase) | 连库后 **自动生成表单**；自动化工作流 | 自带 DB 心智与我们「白名单 DataSource」不同 |
| **Formily + Designable** | [alibaba/formily](https://github.com/alibaba/formily) | JSON Schema 表单运行时强；设计器可嵌入；联动/校验成熟 | Designable 偏 React；Vue 侧需适配成本 |
| **form-js** | [bpmn-io/form-js](https://github.com/bpmn-io/form-js) | 轻量 Schema 表单 + 可视化编辑；与 BPMN 生态一致 | 字段类型偏流程表单，复杂 CRUD 不足 |
| **Amis** | [baidu/amis](https://github.com/baidu/amis) | JSON 描述整页（CRUD、弹窗、图表）；国内落地多 | 运行时体积大；与 Vben 组件体系两套 |

**对本仓库最贴合的组合参考**：

1. **资源模型**：学 NocoBase / Supasheet（Collection + Views，而不是一张「万能画布页」）。  
2. **表结构**：学 NocoBase Collection / Directus Data Model（可视化列/主键/索引，diff 后同步到库；导入已有表）。  
3. **表单 Schema**：学 Formily JSON Schema 或 form-js（二选一做运行时协议，设计器可后换）。  
4. **查询/列表**：学 ToolJet Query + Table，但查询定义挂在「资源」下；**条件组合对齐本仓库已有 Condition 节点**，不要第三套语法。

### 2.2 工作流 / BPM（能力 B）

| 项目 | 地址 | 亮点 | 不宜照搬 |
|------|------|------|-----------|
| **bpmn-js / Camunda Modeler** | [bpmn-io/bpmn-js](https://github.com/bpmn-io/bpmn-js)、[camunda/camunda-modeler](https://github.com/camunda/camunda-modeler) | 标准 BPMN 设计器；属性面板；与 form-js 可组合 | 完整 Camunda 运行时运维成本高 |
| **Flowable / Camunda 引擎** | Flowable、Camunda 7/8 | 成熟人审、多实例、监听器、历史 | 与 ABP 权限/多租户要深度适配 |
| **Elsa Workflows** | [elsa-workflows/elsa-core](https://github.com/elsa-workflows/elsa-core) | **.NET 原生**；Activity 模型；可自托管 | 偏开发者工作流，业务审批 UX 需自建 |
| **Open BPMN** | [imixs/open-bpmn](https://github.com/imixs/open-bpmn) | 可嵌入 IDE/Web 的建模；纯 Java 元模型 | 与 Vue/ABP 栈略远 |
| **国内审批设计器** | 各类 X6 审批 demo | 钉钉式「发起人→审批人」极简 UX | 扩展到会签/条件网关后常崩 |

**规划倾向（待评审拍板）**：

- **设计器 UX**：业务侧可用「钉钉式简化设计器」；高级态可导出/导入 BPMN（可选）。  
- **引擎**：优先评估 **Elsa 3**（同栈）vs **自研轻量状态机**（仅 UserTask + 排他网关 + 并行会签）vs **嵌入 Flowable**。首期不直接上完整 BPMN 引擎。

### 2.3 看板 / 仪表（能力 E）

| 项目 | 地址 | 亮点 | 不宜照搬 |
|------|------|------|-----------|
| **Supasheet Views** | 同上 | 同一资源多视图（Kanban 只是 ViewType） | — |
| **github-board** | [TomzxCode/github-board](https://github.com/TomzxCode/github-board) | **列/泳道 = 过滤表达式**；预设可分享 | 只读 GitHub，无写回 |
| **Easy Kanban / Agila** | [drenlia/easy-kanban](https://github.com/drenlia/easy-kanban) | 多看板、WIP、List/Gantt、保存筛选 | 独立产品，不是「资源视图」 |
| **LobsterBoard** | [Curbob/LobsterBoard](https://github.com/Curbob/LobsterBoard) | 拖拽 Widget 仪表盘 | 偏运维监控，非业务看板 |
| **NocoBase Blocks** | 同上 | 页面由块拼装（表、图、看板） | 块系统工程量大 |

**规划倾向**：看板是 **资源的一种 View**，不是独立 App；列定义 = 字段枚举或表达式；拖拽改状态 = 写回字段 + 可选触发工作流/逻辑编排。复杂「大屏 Widget」放到更后分期。

---

## 3. 目标用户与场景

| 角色 | 诉求 |
|------|------|
| 实施 / 业务配置员 | 在数据源上设计/导入表，配组合查询与列表列，拖出新建/编辑表单，给按钮挂 FlowKey，发布菜单 |
| 业务主管 | 看看板、改状态、催办 |
| 审批人 | 待办、通过/驳回、转办 |
| 研发 | 登记 DataSource、写可复用 Flow、扩展列 render 组件白名单；不写每个 CRUD 页 |

**典型故事**（规划验收用例，非实现）：

1. 管理员登记 Postgres 数据源 `erp_write`（`accessMode=readWrite`）。  
2. 表设计器新建托管表 `orders`：主键 `Id`、约定审计列、`OrderNo`/`Amount`/`Status`；建 `(TenantId, Status)` 索引；预览 DDL 后应用到库。  
3. 一键创建资源「订单」；或对已有库「从库拉取」`orders` 再绑资源。  
4. 配置组合查询：`Status eq`、`CustomerName contains`、`Amount between`，组合 `(1 or 2) and 3`；隐藏条件 `CreatorId = sys.userId`。  
5. 配置列表列：金额用 currency format；状态用 tag render；工具栏新增、行上编辑/删除。  
6. 新建表单可写全部业务字段；编辑时 `OrderNo` 只读、`Status≠Draft` 时金额只读。  
7. 简单保存走 WriteService；「提交」按钮调已发布逻辑 `order.submit`（校验、写库、发消息）。  
8. 金额超限启动工作流「风控审批」（能力 B，非本段）。  
9. 看板按 `Status` 分列（后置）。

---

## 4. 产品心智模型（你描述的链路）

```
① 选择数据源（复用 Orchestration DataSource 白名单；SQL 族可进表设计器）
        │
② 表设计器：定义列 / 主键 / 索引 / 注释 → 预览 DDL → 同步到目标库
        │     （也可「从库拉取」已有表，作为只读或有限变更的导入表）
        │
③ 基于表创建资源（字段字典、标题字段、软删/租户列约定）
        │
④ 设计查询条件（组合式：条件行 × 匹配运算符 × `1 and (2 or 3)`）
        │     默认由 QueryService 生成安全 SQL；复杂查询可绑 queryFlowKey
        │
⑤ 设计列表：可见列、排序分页、列 format / render、工具栏与行按钮
        │
⑥ 设计表单（新建 / 编辑 / 详情）：布局、只读/必填、校验；提交调编排或直写
        │
⑦ （可选）绑定工作流（创建后 / 更新前 / 状态变更时）
        │
⑧ （可选）增加看板等视图（同一资源，不同 ViewType）
        │
⑨ 挂菜单 + 权限码 → 终端用户使用
```

这与 NocoBase「Collection → UI」和 Supasheet「Resource → Views」一致，避免 ToolJet「先画页面再绑查询」带来的配置散乱。

**分层原则（本次补充的核心）**：

| 层 | 管什么 | 不负责 |
|----|--------|--------|
| **表设计器** | 物理结构：列类型、主键、索引、同步 DDL | 不做人机交互、不算业务规则 |
| **资源 / 列表 / 表单** | 给人看、给人填：筛选项、列展示、按钮、字段只读 | 不写任意 SQL、不做审批 |
| **逻辑编排** | 提交后怎么算、怎么写、怎么发消息、复杂查询怎么取数 | 不做人审、不替代列表运行时 |

简单 CRUD 应「建表即可用」；复杂规则一律挂已发布 `flowKey`，而不是在列表页再发明第三套脚本引擎。

---

## 5. 表单与资源定义系统（规划）

### 5.1 核心对象

| 对象 | 含义 |
|------|------|
| **DataSource** | 已有：连接白名单（postgres/mysql/sqlserver/oracle/mongo/redis） |
| **TableDefinition** | **新增**：某 DS 下的一张表的设计时结构（列/PK/索引）；可与物理表 diff 后同步 |
| **Resource（资源）** | 业务对象元数据：绑定 DS + 表（或后置自定义 Query）；字段字典、标题字段 |
| **Field** | 业务侧：标题、控件、字典、只读、校验、关联（FK）；底层类型来自 TableDefinition |
| **FilterDef** | 查询条件：条件行 + 运算符 + 组合式 `1 and (2 or 3)`；区分默认筛与用户可改筛 |
| **ListView** | 列、排序、分页、密度；列 `format` / `render`；工具栏/行/批量按钮 |
| **FormDef** | Create / Update / Detail 的 Schema（可共用一份，按 mode 覆盖只读/可见/必填） |
| **ActionDef** | 按钮：位置、可见条件、打开表单、调用 `flowKey`、确认框、完成后刷新 |
| **PageApp** | 可选：把多个 View 组成一个菜单页（Tab：列表 \| 看板） |
| **Permission** | 与 ABP 权限：资源级 CRUD + 字段级可见/可写（后置） |

结构 vs 展示必须拆开：**改列类型走表设计器；改「这一列怎么显示」走 ListView。** 否则一次「隐藏金额列」会被误做成 DROP COLUMN。

### 5.2 设计器能力（目标态，细节见 5.7–5.12）

**表设计器**：列网格、主键/唯一/索引、从库拉取、预览 DDL、应用到库。  

**查询条件**：条件行（字段 × 匹配运算符 × 值来源）+ 组合式 `1 and (2 or 3)`；区分系统隐式 / 配置默认 / 用户可改。  

**列表**：列显隐、宽度、固定、预设 format、受控 render、排序分页；工具栏/行/批量按钮。  

**表单**：新建/编辑/详情共用 Schema、mode 覆盖只读/必填/可见；提交直写或 `flowKey`。  

**按钮**：新增/编辑/删除/详情为内置 kind；自定义必须绑表单和/或编排。

### 5.3 Schema 协议（规划原则）

- **存库一份 JSON**（FormSchema / ListSchema / FilterSchema），前后端契约稳定。  
- 运行时渲染优先：**Ant Design Vue 组件 + 自研轻量 Schema 解释器**，或评估 Formily Vue。  
- 不在首期引入完整 Amis 运行时（与 Vben 皮肤、权限、路由冲突成本高）。  
- 设计器可先「配置面板生成 Schema」，后「拖拽画布」；**Schema 稳定比设计器炫酷更重要**。

### 5.4 数据读写路径（规划）

```
List/Filter ──► QueryService（默认）或 queryFlowKey
Create/Update ──► 校验 Schema ──► WriteService 或 create/update FlowKey
Delete ──► 权限 + 软删/物理删 或 deleteFlowKey
```

安全底线（与编排 Sql 节点一致）：

- 仅白名单 DataSource；禁止终端拼接任意 SQL。  
- 写操作需 Resource 级权限 + DS `accessMode`。  
- 字段投影：列表不得默认 `SELECT *` 到前端敏感列（按 ListView 列配置）。  
- 组合式解析为表达式树，**不得**把 `1 and (2 or 3)` 当字符串拼进 WHERE。

### 5.5 与「纯逻辑编排入参表单」的区别

| | 资源表单（本文） | 编排 InputSchema（已有） |
|--|------------------|---------------------------|
| 目的 | 持久化业务数据 CRUD | 调用 Flow 的试跑/系统 API 入参 |
| 数据落点 | DataSource 表/集合 | 编排上下文 vars |
| 列表/看板 | 有 | 无 |

二者可共享控件与表达式引擎，**定义入口分开**，避免一个「表单」概念两用混淆。

### 5.6 总链路（本次补充的产品主路径）

```
DataSource（已有连接）
    │
    ├─【表设计器】TableDefinition
    │     列 / 主键 / 唯一 / 索引 / 注释
    │     预览 DDL → 应用到库（或从库拉取）
    │
    └─【资源】Resource 绑定 table
          │
          ├─ FilterDef     组合查询（对齐编排 Condition）
          ├─ queryMode     generated | flowKey
          ├─ ListView      列 + format/render + ActionDef[]
          └─ FormDef       create / update / detail
                │
                └─ ActionDef.submit → 直写 WriteService
                                   或 PublishedFlowInvoker(flowKey)
```

**默认能用、复杂可挂编排**：

| 场景 | 默认 | 挂编排 |
|------|------|--------|
| 列表查询 | QueryService 按 Filter AST 生成参数化 SQL | `queryFlowKey` 返回 `{ items, total }` |
| 新建/编辑/删除 | WriteService 按字段白名单 INSERT/UPDATE/DELETE | `createFlowKey` / `updateFlowKey` / `deleteFlowKey` 接管或前后置 |
| 列展示 | 预设 format（日期、金额、枚举） | 受控 JS `format` / 描述符 `render` |
| 自定义按钮 | — | 必须绑 `flowKey`（或打开表单后再提交到 Flow） |

不要做成「每个列表都必须先画一条查询流」——那会把简单 CRUD 拖死；也不要做成「列表页内嵌任意 SQL/JS」——那会绕开编排已有的沙箱与白名单。

---

### 5.7 表设计器（DataSource → 物理表）

当前 `DataSource` 只登记连接（code / provider / accessMode / 密钥），**没有表结构**。表设计器补的是：在白名单库上，用可视化方式管理「有哪些表、列、主键、索引」，并把变更同步到真实库。

#### 5.7.1 两种表来源（必须同时支持）

| 来源 | 含义 | 设计器权限 |
|------|------|------------|
| **托管表 managed** | 在设计器里新建，平台拥有结构，可完整 ALTER | 增列、改可空/默认/注释、建/删索引；删列/改类型属破坏性操作 |
| **导入表 imported** | 「从库拉取」已有业务表（introspect） | 默认只读结构；P2 允许「只加列 / 只加索引」，禁止改名、改类型、删列，除非显式升级为托管 |

对已有 ERP/业务库：先导入，列表/表单就能配；对平台自己沉淀的业务表：用托管表，从零建。

**不做表设计器的 DataSource**：Redis（无表）；Mongo 首期只做「集合名 + 字段推断」，完整 JSON Schema 后置。表设计器 **仅 SQL 族**（postgres / mysql / sqlserver / oracle）。

#### 5.7.2 平台类型 → 各库映射（设计器里选平台类型，不让用户手写 `varchar(max)`）

| 平台类型 | Postgres | MySQL | 说明 |
|----------|----------|-------|------|
| `guid` | `uuid` | `char(36)` | 默认主键候选 |
| `string` | `varchar(n)` | `varchar(n)` | 必填 length |
| `text` | `text` | `text` | 长文本 |
| `int` / `long` | `int` / `bigint` | 同 | |
| `decimal` | `numeric(p,s)` | `decimal(p,s)` | 金额用这个，不用 float |
| `boolean` | `boolean` | `tinyint(1)` | |
| `date` | `date` | `date` | |
| `datetime` | `timestamptz` | `datetime(3)` | 存 UTC，展示用 `sys.timeZone` |
| `json` | `jsonb` | `json` | |
| `enum` | `varchar` + 字典 | 同 | 不首期建 DB ENUM（迁移痛） |

列属性：name、displayName、type、length/precision/scale、nullable、default（字面量或 `now()`/`gen_random_uuid()` 白名单函数）、unique、comment。  
标识符只允许 `[a-z][a-z0-9_]*`，禁止把用户输入拼进 DDL 字符串。

#### 5.7.3 主键、索引、约定列

| 对象 | 规则 |
|------|------|
| **主键** | 单列或联合；托管表推荐 `Id guid`；导入表尊重已有 PK |
| **唯一约束** | 单列 unique 或联合 unique |
| **普通索引** | 列序、ASC/DESC；复合索引；可选 `WHERE` 部分索引（仅 PG，P2） |
| **外键** | P2：指向同 DS 另一张托管/导入表；列表里可做关联选择 |

新建托管表时提供 **「ABP 约定列」模板**（可全选/全不选）：

| 列 | 用途 |
|----|------|
| `Id` | PK guid |
| `TenantId` | 多租户；QueryService 自动注入 `sys.tenantId` |
| `CreationTime` / `CreatorId` | 审计 |
| `LastModificationTime` / `LastModifierId` | 审计 |
| `IsDeleted` / `DeleterId` / `DeletionTime` | 软删；查询默认 `IsDeleted = false` |

这些列在表单里默认隐藏；列表可选择展示创建时间。不要强制每张导入表都有这些列。

#### 5.7.4 同步策略（编辑后更新真实表——最关键）

设计时结构存在 Mongo（`TableDefinition` + 版本），物理表在目标 DataSource。**禁止静默 DROP。**

```
设计器保存（只改元数据，尚未改库）
        │
        ▼
「预览变更」= diff(设计时, introspect(真实库))
        │  生成参数化/白名单 DDL 列表
        ▼
人工确认（破坏性操作二次确认 + 权限 DataSources.Ddl）
        │
        ▼
在目标库事务内按序执行 → 回写 TableDefinition.syncState / lastAppliedAt
```

| 变更 | 风险 | 默认策略 |
|------|------|----------|
| 新建表 / 加列（可空或有默认） | 低 | 允许 |
| 加索引 / 加唯一（需验数据） | 中 | 允许；失败回滚并提示重复值 |
| 列改注释、加长 varchar | 低 | 允许 |
| 列改短、改类型、改不可空 | 高 | 必须确认；建议先导出数据 |
| 删列 / 删表 / 删索引 | 高 | 默认关闭；需 `allowDestructiveDdl=true` + 打字确认表名 |
| 改列名 | 高 | 视为「加新列 + 可选迁数据 + 删旧列」，不首期做自动 RENAME |

同步状态：

| `syncState` | 含义 |
|-------------|------|
| `inSync` | 设计时与库一致 |
| `localAhead` | 有未应用的设计 |
| `remoteAhead` | 库被外部改过（DBA 手工 DDL） |
| `conflict` | 两边都有变更；必须先拉取或强制覆盖（覆盖要权限） |

「从库拉取」只更新 TableDefinition，不改 ListView/FormDef；若拉取后缺了已被列表引用的列 → 设计器标红，运行时该列跳过。

#### 5.7.5 安全与权限

- DataSource `accessMode` 必须含 write 才允许 DDL；只读 DS 只能拉取、不能应用。  
- 新权限：`Orchestration.DataSources.Ddl`（或独立 `AppBuilder.Tables.Ddl`），与「改连接串」分开。  
- 禁止操作系统表 / `pg_*` / `information_schema`；表名黑名单。  
- DDL 审计：谁、何时、哪张表、DDL 指纹、成功/失败。  
- 与编排 Code `db.*` 共用连接，但 **表设计器走独立 DdlExecutor**，不把 ALTER 写进业务 Flow（避免误在下单流里建表）。

#### 5.7.6 设计器 UX（配置态，挂在数据连接下）

```
数据连接 → [某 DS] → 表
┌──────────┬─────────────────────────────┬──────────────────┐
│ 表列表    │ 列网格                        │ 列属性           │
│ · orders │ name   type    pk  null 注释 │ 类型/长度/默认   │
│ · lines  │ Id     guid    ●             │                  │
│ + 新建表 │ Status string                │ 索引 Tab：        │
│ 从库拉取 │ …                            │  PK / Unique / IX │
└──────────┴─────────────────────────────┴──────────────────┘
顶栏：保存草稿 │ 预览变更 │ 应用到库 │ 同步状态 Tag
```

预览变更用表格列出「将执行的操作」，破坏性行红色；应用成功后可从该表 **一键创建资源**（带上字段字典）。

---

### 5.8 资源与字段字典

表是物理的，资源是业务的。同一张 `orders` 可以有「全部订单」和「我的草稿」两个 Resource（不同默认筛选、不同按钮），但 **不要复制两份表结构**。

Resource 绑定：

```json
{
  "code": "order",
  "name": "订单",
  "dataSourceCode": "erp_write",
  "binding": { "kind": "table", "table": "orders" },
  "primaryKey": ["Id"],
  "titleField": "OrderNo",
  "softDeleteField": "IsDeleted",
  "tenantField": "TenantId"
}
```

`kind=query`（自定义查询）放到 P2：仅只读列表，无表设计器写回。  
字段字典可覆盖：显示名、字典/枚举、默认控件（Input/Select/DatePicker）、敏感标记。底层类型与可空仍以 TableDefinition 为准，避免「表单说数字、库是 varchar」。

---

### 5.9 组合式查询（复用编排 Condition 模型）

**要支持组合式查询。** 不要再发明第三套语法：与逻辑编排 Condition 使用同一套 `items[] + combine`。

#### 5.9.1 一条条件

| 字段 | 说明 |
|------|------|
| `no` | 编号，从 1 |
| `field` | 资源字段（左值只允许字段白名单，禁止任意表达式防注入） |
| `op` | 匹配模式，按下表按字段类型裁剪 |
| `valueSource` | `user`（运行时输入）/ `literal`（配置死）/ `sys`（如 `sys.userId`）/ `query`（URL 参数） |
| `exposed` | 是否出现在列表页筛选项；`false` 则只作默认隐藏条件 |

运算符与编排对齐，并按类型裁剪：

| 类型 | 允许 `op` |
|------|-----------|
| string | `eq` `ne` `contains` `notContains` `startsWith` `in` `isEmpty` `isNotEmpty` |
| number / decimal | `eq` `ne` `gt` `gte` `lt` `lte` `between` `in` |
| datetime / date | `eq` `gte` `lt` `lte` `between`（半开区间，与 `sys.MonthEnd` 约定一致） |
| boolean / enum / guid | `eq` `ne` `in` `isEmpty` |
| 通用 | `isEmpty` `isNotEmpty` |

「左边对应内容选择匹配模式」= 每行：`[字段] [运算符▾] [值控件]`。值控件随 `op` 变：`between` 出两个框，`in` 出多选，`isEmpty` 无值。

#### 5.9.2 条件之间的组合

```
1. Status        eq        用户选
2. CustomerName  contains  用户填
3. Amount        gte       用户填
4. CreatorId     eq        sys.userId     （exposed=false，默认「只看我的」）
组合式： (1 or 2) and 3 and 4
```

规则与编排 5.5 节相同：只允许编号与 `and` / `or` / `not` / `()`。  
设计器：条件表 + 组合输入框 + 模板（全 and / 全 or / `1 and (2 or 3)`）+ 非法编号标红。

两层筛选，避免把「租户隔离」暴露给用户改：

| 层 | 谁填 | 例 |
|----|------|----|
| **系统隐式** | 引擎 | `TenantId = sys.tenantId`；`IsDeleted = false` |
| **配置默认** | 实施 | `Status in ('Open','Submitted')`；`CreatorId = sys.userId` |
| **运行时用户** | 终端用户 | 筛选项里露出的 1、2、3 |

最终 WHERE = 系统隐式 **AND** 配置默认 **AND** 用户组合式。用户改不了前两层。

#### 5.9.3 终端用户交互分档

| 档 | 行为 | 适用 |
|----|------|------|
| **简单（默认）** | 筛选项平铺，条件之间固定 AND；用户不看组合式 | 业务员 |
| **高级** | 可增删条件行、改运算符、写组合式 | 实施 / 超级用户；由 ListView `filterUi=simple\|advanced` 打开 |

URL 可序列化当前筛选（便于分享「待审核订单」链接）；保存「筛选预设」后置。

---

### 5.10 查询执行：QueryService vs 逻辑编排

列表查询 **可以**走编排，但 **默认不走**。

#### 5.10.1 默认：QueryService（生成安全查询）

```
Filter AST（items + combine + 用户值）
  + sort + page + pageSize
  + 列投影（仅 ListView 可见列 + PK）
        │
        ▼
QueryService
  · 字段/op 白名单校验
  · 生成参数化 SQL（标识符来自 TableDefinition，值进 Parameters）
  · 注入租户 / 软删
  · COUNT + 分页 SELECT
        │
        ▼
{ items, total }
```

禁止 `SELECT *`；禁止把 combine 字符串拼进 SQL（应解析成表达式树再生成 AND/OR）。  
Mongo：同样 AST → 驱动 Filter Definition。Redis：不做通用列表。

#### 5.10.2 可选：`queryFlowKey`（复杂取数）

适用：跨表聚合、先调 HTTP 再本地过滤、按角色走不同 SQL、结果要做编排侧 map/脱敏。

约定契约（发布为系统 API 的入参/出参）：

```json
{
  "inputs": [
    { "name": "page", "type": "number" },
    { "name": "pageSize", "type": "number" },
    { "name": "sorting", "type": "string" },
    { "name": "filters", "type": "object" }
  ],
  "outputs": [
    {
      "name": "result",
      "type": "object",
      "promote": true,
      "map": {
        "item": [
          { "name": "items", "type": "array", "from": "items" },
          { "name": "total", "type": "number", "from": "total" }
        ]
      }
    }
  ]
}
```

`filters` 原样传入编排已有的条件模型，Flow 内可用 Sql/Code 节点解释，或先用标准 QueryService 作为子步骤（P2 做成注册组件 `resource.query`）。  
列表运行时调 `PublishedFlowInvoker`（与 `/api/logic/{flowKey}` 同源）。

约束：

1. 必须返回 `items` + `total`，否则列表无法分页。  
2. 每次翻页都会跑一遍 Flow：简单表不要绑 Flow。  
3. DryRun 不作为列表运行时路径。  
4. 编排里仍禁止拼接 SQL；写查询用参数化 Sql/Code `db.query`。

#### 5.10.3 推荐选择

| 情况 | 路径 |
|------|------|
| 单表、字段筛选、排序分页 | **QueryService** |
| 单表 + 提交前后复杂校验/写子表/发消息 | 查询仍 QueryService；**写**走 Flow |
| 列表本身就是「逻辑 API 的结果」 | `queryFlowKey` |
| 报表跨库 | 不做；用编排 API + 独立页，不塞进本资源列表 |

---

### 5.11 列表列：显示 / format / render

每列独立配置展示，**不改库结构**。

```json
{
  "field": "Amount",
  "title": "金额",
  "width": 120,
  "align": "right",
  "sortable": true,
  "format": { "type": "preset", "preset": "currency", "options": { "currency": "CNY" } },
  "render": { "type": "none" }
}
```

#### 5.11.1 format vs render

| | **format** | **render** |
|--|------------|------------|
| 输入 | 单元格原值 + 行 + ctx | 同左 |
| 输出 | **字符串**（纯文本） | **描述符**（再交给 Vue 组件画） |
| 典型 | `2026-09-18`、`¥1,234.00`、`已审核` | 状态 Tag、链接、开关、进度、图片 |
| 安全 | 易沙箱 | 禁止返回原始 HTML 字符串 |

**预设 format（P1 先做，覆盖 80%）：** `text` / `date` / `datetime` / `currency` / `number` / `percent` / `enum` / `boolean` / `ellipsis`。  
**自定义 format JS（P2）：** 纯函数，签名固定：

```js
// (value, row, ctx) => string
function format(value, row, ctx) {
  return value == null ? '-' : (row.Currency + ' ' + Number(value).toFixed(2));
}
```

`ctx` 只读：`sys.userId`、`sys.culture`、字典。禁止 `fetch` / `document` / 改 `row`。超时与字节上限与编排 Code 沙箱同级（更严：同步、无 `db`）。

**render（P2）：返回描述符，由运行时映射到 Ant Design Vue 组件，不 `eval` 出 VNode。**

```js
function render(value, row, ctx) {
  if (value === 'Approved') return { component: 'tag', props: { color: 'success', text: '已通过' } };
  if (value === 'Rejected') return { component: 'tag', props: { color: 'error', text: '驳回' } };
  return { component: 'text', props: { text: String(value ?? '') } };
}
```

允许的 `component` 白名单：`text` / `tag` / `link` / `image` / `progress` / `switch`(只读) / `dict`。  
`link` 的 `href` 只允许站内路由或字段值，禁止 `javascript:`。  
**不首期开放任意 HTML / 任意 Vue SFC**（XSS、与 Vben 构建耦合）。实施若只需变色/字典，用预设 + 描述符即可，不必写 JS。

列上 `format` 与 `render` 同时存在时：先 format 成字符串，再作为 render 的 `value`（或 render 直接读原值，由配置 `render.input=raw|formatted` 决定）。

---

### 5.12 按钮、表单与编排挂钩

通用按钮：**新增、编辑、删除、详情**；其余一律「自定义按钮 → 表单和/或 FlowKey」。

#### 5.12.1 ActionDef

| 字段 | 说明 |
|------|------|
| `key` / `label` / `icon` | 展示 |
| `position` | `toolbar` / `row` / `batch` |
| `kind` | `create` / `update` / `delete` / `detail` / `custom` |
| `open` | `drawer` / `modal` / `page` / `none`（无表单，直接跑 Flow） |
| `formMode` | `create` / `update` / `detail`；指向 FormDef |
| `flowKey` | 已发布逻辑；无则走 WriteService |
| `confirm` | 二次确认文案（删除默认开） |
| `visibleWhen` | 同行条件模型（如 `1. Status eq Draft`，组合式 `1`） |
| `acl` | 权限码；无权限则不渲染 |
| `after` | `refresh` / `close` / `goto` |

#### 5.12.2 新增

```
点「新增」→ 打开 FormDef(create)
  → 用户填（可默认 sys.userId、日期表达式）
  → 提交：
       A. 无 flowKey：Schema 校验 → WriteService INSERT（白名单列 + 审计列自动填）
       B. 有 createFlowKey：表单值作为 input 调编排
            编排内：校验、写主表、batch 写子表、发 Rabbit、回传 id
  → after=refresh 关闭并刷新列表
```

编排入参建议与表单字段同名，便于实施零映射；也可在 ActionDef 配 `inputMap`（表单字段 → flow input）。  
**推荐：简单表走 A；有明细行、要发消息、要算单号走 B。** 不要强迫每张表都画一条「插入 Flow」。

也可「A + 钩子」：`beforeCreateFlowKey`（可阻断）/ `afterCreateFlowKey`（异步即可）。P1 只做「整段替换」或「纯直写」两种，钩子 P2 再拆，避免三种语义并存搞混。

#### 5.12.3 编辑（含只读）

```
点「编辑」→ QueryService.getById（或 getFlowKey）
  → 打开 FormDef(update)，套入行数据
  → 字段按 mode 覆盖：visible / required / readonly / disabled
  → 提交：直写 UPDATE 或 updateFlowKey
```

只读有三层，从硬到软：

| 层 | 例 |
|----|----|
| 表结构 | PK、审计列、租户列：永远不可改 |
| 表单 mode | 编辑时 `OrderNo` readonly；详情全部 disabled |
| 表达式 | `readonlyWhen`: `input.Status ne 'Draft'`（条件模型，不写任意 JS） |

同一份 FormSchema，用 `modes.create|update|detail.overrides` 覆盖，避免维护三份拖拽稿。  
详情 = 编辑的全只读 + 无提交按钮；也可独立排版。

乐观并发：若表有 `LastModificationTime` 或 `ConcurrencyStamp`，UPDATE 带上原值，冲突返回 409。

#### 5.12.4 删除与自定义

- 软删列存在则 UPDATE `IsDeleted`；否则物理删除（需确认）。可绑 `deleteFlowKey`。  
- 自定义按钮：「提交审核」打开无字段表单或带意见框 → `flowKey=order.submit`；「同步到 ERP」`open=none` 直接调 Flow。  
- 批量：入参 `ids[]`；Flow 内循环/`db.batch`；列表限制单次最大勾选数。

#### 5.12.5 表单设计要点（承接 5.2）

- 控件：输入、数字、日期、下拉（静态字典 / 远程 Resource 列表）、开关、文本域、关联选择（FK，P2）。  
- 布局：单列/双列、分组；Tab 后置。  
- 校验：复用编排 InputSchema 的声明式 `rules`（required/min/max/pattern/expr），**表单上不跑任意 JS**；过程式校验放提交 Flow 的 Code 节点。  
- 联动：显示/必填/禁用用条件模型或受控表达式（与编排 `expr` 同一套），不首期做「onChange 写 JS」。

#### 5.12.6 写路径安全（与编排一致）

- 仅白名单 DataSource + 字段投影；禁止按表单多出来的键写库。  
- 直写需要 Resource 权限 + DS `accessMode` write；Flow 写库另需 `Orchestration.Sql.Write`。  
- 同单多表写入：走编排 `txMode=sameDataSource`，不要在 WriteService 里开分布式事务。

---

### 5.13 运行时页、发布与菜单

配置态（设计器）与使用态（业务列表）分开，学 NocoBase：

1. 资源发布（草稿 → 已发布），运行时只读已发布 Schema（与 FlowVersion 同一套心智）。  
2. 发布时登记菜单：`/app/{resourceCode}`，权限 `App.{resourceCode}` + `.Create/.Update/.Delete`。  
3. 列表页结构：筛选区（FilterDef）→ 工具栏按钮 → 表格（ListView）→ 分页 → 抽屉表单。  
4. 字段级权限后置：无权限列不进投影、不进表单。

---

## 6. 工作流引擎规划

### 6.1 要解决的问题

- 人审、会签、或签、转办、驳回回退、超时提醒。  
- 与组织/角色/用户（Identity）集成。  
- 与资源状态字段、待办中心、消息通知联动。  
- 节点前后可挂 **逻辑编排 FlowKey**（自动算、写库、发消息），但不在编排画布里画「审批人」。

### 6.2 推荐分期形态

| 阶段 | 形态 | 说明 |
|------|------|------|
| **W0 规划** | 本文 | 选型与边界 |
| **W1 轻量引擎** | 自研状态机 | 仅：开始 → 审批节点（角色/指定人）→ 排他条件 → 结束；单实例待办 |
| **W2** | 会签/或签、驳回、转办、抄送 | 仍可不引入 BPMN 文件 |
| **W3** | 评估 Elsa / Flowable | 复杂并行、子流程、定时边界事件；设计器可上 bpmn-js |

### 6.3 设计器 UX（规划）

- **简易模式**（默认）：纵向卡片流（发起人 → 审批人 → …），类似钉钉。  
- **高级模式**（可选）：BPMN 画布（bpmn-js），仅管理员。  
- 审批人来源：固定用户、角色、部门主管（需组织模型）、表单字段（申请人自选）、逻辑编排返回候选人列表。

### 6.4 运行时对象（草案）

- `WorkflowDefinition`（版本、发布）  
- `WorkflowInstance`（业务键、资源类型+主键、状态）  
- `WorkflowTask`（待办：办理人、候选、到期）  
- `WorkflowHistory`（审计）  

触发：资源「创建后/更新后/字段变更」；或 API `StartWorkflow(defKey, bizKey, vars)`。

### 6.5 与逻辑编排的调用约定

| 时机 | 调用 | 失败策略 |
|------|------|----------|
| 流程启动前 | FlowKey（校验） | 阻止启动 |
| 任务完成后 | FlowKey（写业务状态） | 重试 / 人工补偿（规划里写清） |
| 流程结束 | FlowKey / 消息 | 异步可接受 |

**禁止**工作流引擎内嵌执行任意 SQL；写库走 Resource WriteService 或编排节点。

---

## 7. 看板与复杂视图定制

### 7.1 看板 = 资源视图

```
Resource
  ├── ListView
  ├── FormDef (create/update/detail)
  ├── KanbanView
  │     ├── columnField（如 status）
  │     ├── columns[]（值、标题、WIP、颜色）
  │     ├── cardTitleField / cardMetaFields
  │     ├── swimlaneField?（可选）
  │     └── onDrop → 更新字段 + 可选 Workflow / FlowKey
  └── (后) CalendarView / GalleryView
```

借鉴：

- **Supasheet**：多视图挂同一资源。  
- **github-board**：列可用规则表达式（适合「超期」「我的」等虚拟列）；首期用枚举字段即可，表达式列放后期。

### 7.2 「复杂看板定制」范围切分

| 层级 | 能力 | 分期 |
|------|------|------|
| L1 | 按单字段分列、拖拽改字段、筛选条复用 FilterDef | 与资源 CRUD 同期或紧随 |
| L2 | 泳道、WIP、保存筛选预设、卡片自定义摘要 | 中期 |
| L3 | 跨资源聚合看板、自定义 Widget 大屏、实时多人光标 | 长期；可对标独立看板产品 |

### 7.3 仪表盘（与看板区分）

- **看板**：面向同一资源的状态流转。  
- **仪表盘**：多 Widget（统计卡、图表、嵌入列表）；数据来自聚合查询或 FlowKey。  
- 仪表盘工程量大，**默认放在表单/工作流稳定之后**；避免与「资源 Kanban」抢同一套概念。

---

## 8. 与现有平台能力的衔接

| 现有能力 | 衔接方式 |
|----------|----------|
| `DataSource` | 表设计器挂在 SQL 族 DS 下；资源绑定同一白名单；`accessMode` 管拉结构 vs 跑 DDL vs 业务写 |
| 编排 Condition | FilterDef **复用** `items + combine`，同一套 op / and-or-not，避免第三套语法 |
| 逻辑编排 `flowKey` | 列表 `queryFlowKey`；按钮 `create/update/delete/custom`；看板 onDrop；工作流监听器 |
| `PublishedFlowInvoker` | 列表/表单运行时与 `/api/logic/{flowKey}` 同源调用 |
| Code `db.*` / 规划中 Sql 节点 | 复杂写、子表 batch、同库 `txMode`；表设计器 DDL **不**走业务 Flow |
| 消息连接 / Trigger / Schedule | 工作流超时提醒、外部事件启动流程（后）；列表按钮也可间接触发已有 Flow |
| ABP 权限 / 菜单 | 资源发布时生成 `App.{resourceCode}.*`；DDL 单独权限 |
| 多租户 | TableDefinition / Resource / Workflow 均 `IMultiTenant`；查询注入 `TenantId` |
| Vue Vben | 运行时 Ant Design Vue 表+抽屉；设计器独立路由（数据连接/表、资源、列表、表单） |

**服务落点建议（评审项）**：

- **方案 α**：继续放 `Meta.Dow.SaaS`（与编排同库，迭代快；表结构元数据与 DataSource 同处）。  
- **方案 β**：新建 `Meta.Dow.AppBuilder` 服务（边界清晰，跨服务调用编排）。  

首期规划推荐 **α**，待模型稳定再拆 β。

---

## 9. 领域模型草案（仅规划）

```
DataSource (已有)
  └── TableDefinition          ★ 新增
        - dataSourceCode, tableName, origin: managed|imported
        - columns[]: name, platformType, nullable, default, comment, length…
        - primaryKey[], indexes[]: unique, columns, direction
        - syncState, schemaVersion, lastAppliedAt
        - ddlHistory[]（审计）

Resource
  - code, name, dataSourceCode
  - binding: { kind: table|collection|query, table }
  - primaryKey, titleField, statusField?, softDeleteField?, tenantField?
  - fields[]: FieldMeta（展示/控件/字典；类型跟 TableDefinition）
  - queryMode: generated | flow
  - queryFlowKey?
  - FilterDef: { items[], combine, filterUi }
  - ListView: { columns[]: format/render, sort, pageSize, actions[] }
  - FormDef: { schema, modes: create|update|detail overrides }
  - ActionDef[]: kind, formMode, flowKey, visibleWhen, confirm

ResourcePermission / MenuBinding

WorkflowDefinition / Version / Instance / Task
WorkflowBinding: resourceCode + event(create|update|field:status)

（不存第二份 FlowDefinition；只引用 flowKey）
```

版本策略：TableDefinition 的「应用到库」与 Resource/Form 的「发布给用户」分开——**可以先同步表、后发布页面**。Resource / Form / Workflow 均 **草稿 → 发布**；运行时只读已发布版本。

---

## 10. 非功能与风险

| 风险 | 缓解 |
|------|------|
| 做成第二个 Retool，与编排抢主题 | 坚持 Resource 一等公民；画布 App 不做 |
| 工作流与编排概念混淆 | 文档、菜单、设计器入口强制分离 |
| Schema 频繁破坏兼容 | 协议版本号 `schemaVersion`；迁移器 |
| 动态 SQL 注入 | 禁止字符串拼 SQL；参数化 + 字段/标识符白名单；组合式走 AST |
| 静默 DDL 丢数据 | 预览变更；破坏性默认关；打字确认；DDL 独立权限与审计 |
| 列表每页都跑 Flow，性能崩 | 默认 QueryService；`queryFlowKey` 仅复杂查询；分页契约强制 |
| 列上任意 JS / HTML → XSS | format 纯函数沙箱；render 只返回组件描述符白名单 |
| 设计器技术债（React 生态） | Vue 优先配置式；慎引 Designable/Amis 全量 |
| 看板拖拽与审批冲突 | onDrop 前检查是否存在进行中任务 |
| 表结构与列表配置漂移 | 拉取后引用缺失列标红；运行时跳过，不直接报死页 |

---

## 11. 分期建议（实现前必须评审）

### P0 — 规划冻结（当前）

- 确认本文边界、对象名、与编排/工作流分工。  
- 确认：表设计器 P1 是否包含「应用到库」，还是先只「从库拉取」。  
- 选定表单 Schema 方向（自研轻量 vs Formily）。  
- 选定工作流引擎方向（自研轻量 vs Elsa）。  

### P1 — 表 + 资源 + 列表 + 表单 MVP（无工作流、无看板）

- SQL DataSource：**从库拉取** introspect + 表列表。  
- **托管表设计器**：建表、加列、PK、普通/唯一索引；预览 DDL；非破坏性应用到库。  
- Resource 绑定 table；字段字典。  
- Filter：简单档（字段筛选项，固定 AND）+ 系统隐式租户/软删。  
- List 运行时：分页排序、列显隐、**预设 format**（日期/金额/枚举）。  
- 内置按钮：新增/编辑/删除/详情；FormDef 配置面板；编辑可配只读。  
- 写路径：WriteService 直写；按钮可选挂一个 `flowKey` 整段替换。  
- 菜单与权限种子。  

### P1.5 — 组合查询与编排对齐

- Filter 升级为完整 `items + combine`（复用 Condition 求值器生成 SQL AST）。  
- 高级筛 UI。  
- 可选 `queryFlowKey`（契约 `items/total`）。  

### P2 — 体验、安全、列渲染

- 字段级权限、关联选择、表单条件联动。  
- 列自定义 format JS（沙箱）+ render 描述符白名单。  
- 破坏性 DDL（删列/改类型）+ syncState 冲突处理。  
- 发布版本、导入导出 JSON、配置审计。  
- before/after 钩子拆分（若 P1 只做整段替换）。  

### P3 — 看板 L1 + 工作流 W1

- KanbanView；拖拽更新状态字段。  
- 轻量审批流 + 待办列表。  
- 资源事件绑定工作流。  

### P4 — 增强

- 会签/驳回/转办；超时通知。  
- 看板 L2；Calendar 等。  
- 评估 Elsa/Flowable / bpmn-js。  
- 仪表盘 Widget（可选）。  
- Mongo 集合设计器；外键可视化。  

**在 P0 评审通过前，不新增后端实体与前端路由实现。**

---

## 12. 明确不做什么（首期）

- 不替换现有逻辑编排为审批流。  
- 不做连接器市场式「百种 SaaS」。  
- 不做完整 Retool 自由画布 App。  
- 不做跨库分布式事务（与编排一致）。  
- 不首期上完整 BPMN 引擎与复杂 DMN。  
- 不首期做多人实时协同编辑设计器。  
- **不做通用 DBA 工具**（存储过程、视图、触发器、分区、随意 RENAME）。  
- **不在列表单元格渲染任意 HTML / 任意 Vue SFC**。  
- **不把 ALTER TABLE 画进业务编排画布**。  
- **不要求每张简单表都先画查询 Flow**。  
- Redis DataSource 不做表设计器与通用列表。  

---

## 13. 评审清单（规划合理后再开工）

请产品/架构确认后再进入实现：

1. [ ] P1 表设计器：只「从库拉取」还是必须含「新建托管表 + 应用到库」？  
2. [ ] 破坏性 DDL（删列/改类型）P1 是否彻底禁止？  
3. [ ] Resource P1 是否只支持「表」，自定义 Query / `queryFlowKey` 放到 P1.5？  
4. [ ] 列表查询默认 QueryService，编排仅可选——是否同意？（反对则每个列表都强制 Flow）  
5. [ ] 写库默认直写、按钮可选 `flowKey` 整段替换——是否同意？（反对则强制所有写走编排）  
6. [ ] 组合查询：P1 先 AND，P1.5 再 `1 and (2 or 3)`，还是 P1 一次做齐？  
7. [ ] 列 render：P1 仅预设 format；自定义 JS 是否必须沙箱 + 描述符白名单（推荐是）？  
8. [ ] 表单 Schema：自研 / Formily / form-js？  
9. [ ] 工作流：P3 自研轻量是否够用？何时必须上 Elsa/Flowable？  
10. [ ] 服务落点：SaaS 内 vs 独立服务？  
11. [ ] 看板是否必须与列表同一 FilterDef？  
12. [ ] 与现有「编排 InputSchema 试运行表单」命名如何区分（避免两个「表单」菜单）？  
13. [ ] 多租户下 Resource / 表模板是否支持 Host 下发？  
14. [ ] 新建托管表是否默认带 ABP 约定列（Id/TenantId/审计/软删）？  

---

## 14. 参考链接

**表单 / CRUD / 表设计器**

- https://github.com/nocobase/nocobase  
- https://github.com/supasheet/supasheet  
- https://github.com/directus/directus （数据模型 + 表结构同步，可对照 DDL 心智）  
- https://github.com/ToolJet/ToolJet  
- https://github.com/illacloud/illa-builder  
- https://github.com/Budibase/budibase  
- https://github.com/alibaba/formily  
- https://github.com/bpmn-io/form-js  
- https://github.com/baidu/amis  

**工作流**

- https://github.com/bpmn-io/bpmn-js  
- https://github.com/camunda/camunda-modeler  
- https://github.com/elsa-workflows/elsa-core  
- https://github.com/imixs/open-bpmn  

**看板 / 视图**

- https://github.com/TomzxCode/github-board  
- https://github.com/drenlia/easy-kanban  
- https://github.com/Curbob/LobsterBoard  

**本仓库**

- [`lowcode-logic-orchestration.md`](./lowcode-logic-orchestration.md) — 逻辑编排（能力 D）；Condition 组合式、DataSource、`PublishedFlowInvoker`  
- [`iam-rbac-menu.md`](./iam-rbac-menu.md) — 权限与菜单  

---

## 修订记录

| 日期 | 说明 |
|------|------|
| 2026-09-18 | 初稿：开源对标 + 表单/工作流/看板规划，明确不实现 |
| 2026-09-18 | 补充能力 A：表设计器（DDL 同步）、组合式查询（复用 Condition）、QueryService vs queryFlowKey、列 format/render、按钮/表单与编排挂钩 |
