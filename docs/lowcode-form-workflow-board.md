# Meta.Dow 表单定义 / 工作流 / 看板规划

> **性质**：产品与架构规划文档。**本期只定边界、模型与分期，不实现功能。**  
> **关联**：逻辑编排见 [`lowcode-logic-orchestration.md`](./lowcode-logic-orchestration.md)（能力 D）。本文覆盖此前刻意拆开的 **能力 A（表单/CRUD 页）**、**能力 B（人审工作流）**，以及 **看板/定制视图**。  
> **前端**：`D:\Project\vue-demo`（web-antd）  
> **后端**：`demo-microservice`（建议仍落 SaaS 或后续独立 App 服务，见 §8）

**一句话**：以「数据源 → **表设计器（P1 即新建并应用到库，托管表默认 ABP 约定列）** → 资源 → **前端组合查询 + 列表** → 新建/编辑表单」为轴；**查询与写入一律走已落地的逻辑编排运行时**（简单 CRUD 用系统逻辑开箱即用，复杂规则换成自定义 `flowKey`）；编排不算人审待办。

---

## 目录

1. [为何单独成文](#1-为何单独成文与逻辑编排的边界)
2. [开源对标（GitHub）](#2-开源对标github-设计较好的参考)
3. [目标用户与场景](#3-目标用户与场景)
4. [产品心智模型](#4-产品心智模型你描述的链路)
5. [表单与资源定义系统](#5-表单与资源定义系统规划)（含 [表设计器](#57-表设计器datasource--物理表)、[组合查询](#59-组合式查询复用编排-condition-模型)、[编排 CRUD](#510-查询与写入一律走逻辑编排简单-crud-用系统逻辑兼容)、[列 format/render](#511-列表列显示--format--render)、[按钮与表单挂钩](#512-按钮表单与编排挂钩)）
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
[资源页：组合筛选 / 列表 / 表单 / 看板]
        │ 查询翻页 / 提交 / 删除 / 自定义按钮 / 拖卡片
        ▼
[逻辑编排 FlowKey]  ←→  PublishedFlowInvoker（与 /api/logic/{flowKey} 同源）
        │                 简单 CRUD = 系统逻辑 sys.resource.*
        │                 复杂 = 用户已发布 flow（可 SubFlow 调用系统 CRUD 节点）
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
2. 表设计器新建托管表：自动带 ABP 约定列与默认索引；实施只加 `OrderNo`/`Amount`/`Status` 等业务列；可选 `(TenantId, Status)` 索引；预览 DDL 后应用到库。  
3. 一键创建资源「订单」；或对已有库「从库拉取」`orders` 再绑资源。  
4. 配置组合查询：`Status eq`、`CustomerName contains`、`Amount between`，组合 `(1 or 2) and 3`；隐藏条件 `CreatorId = sys.userId`。  
5. 配置列表列：金额用 currency format；状态用 tag render；工具栏新增、行上编辑/删除。  
6. 新建表单可写全部业务字段；编辑时 `OrderNo` 只读、`Status≠Draft` 时金额只读。  
7. 一键创建资源时自动绑定系统逻辑 `sys.resource.query/get/create/update/delete`，列表/表单即可用（实施不必手动画流）。  
8. 需要发号/写子表/发消息时：把 `order.create` 从系统 CRUD **另存为自定义逻辑**，画布上保留 ResourceCreate 节点，再串 Code / Rabbit；按钮 `flowKey` 改指新编码。  
9. 金额超限启动工作流「风控审批」（能力 B，非本段）。  
10. 看板按 `Status` 分列（后置）。

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
        │     P1 即提供给终端用户组合；运行时把 Filter AST 交给 queryFlowKey
        │
⑤ 设计列表：可见列、排序分页、列 format / render、工具栏与行按钮
        │
⑥ 设计表单（新建 / 编辑 / 详情）：布局、只读/必填、校验
        │     提交 / 删除 / 详情加载一律 PublishedFlowInvoker(flowKey)
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
| **逻辑编排** | **所有查询与写入的运行时**；系统 CRUD 逻辑 + 用户自定义流 | 不做人审；不在画布里跑 DDL |

简单 CRUD 应「建表即可用」：**不是绕开编排直写库**，而是自动绑定只读的系统逻辑 `sys.resource.*`（画布节点 ResourceQuery/Create/…）。复杂规则「另存为」自定义 `flowKey`，契约不变，列表/表单不用改。不在列表页再发明第三套脚本引擎。

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

**表设计器（P1 主路径）**：新建托管表 → 默认带 ABP 约定列 → 预览 DDL → **应用到库**；加列/主键/索引后再次同步。从库拉取为辅（已有表）。  

**查询条件（P1 即给终端组合）**：运行时以 **查询表单** + **可视化条件组**（全部满足 / 任一满足）为主，**不**让业务员手写 `1 and (2 or 3)`。编号公式仅留在逻辑编排 IF 节点。详见 [`lowcode-report-management.md`](./lowcode-report-management.md) §4。  

**列表**：列显隐、宽度、固定、预设 format、受控 render、排序分页；工具栏/行/批量按钮。  

**表单**：新建/编辑/详情共用 Schema、mode 覆盖只读/必填/可见；校验复用 InputSchema `rules`。  

**按钮**：新增/编辑/删除/详情默认绑系统 CRUD 逻辑；自定义必须绑已发布 `flowKey`。

### 5.3 Schema 协议（规划原则）

- **存库一份 JSON**（FormSchema / ListSchema / FilterSchema），前后端契约稳定。  
- 运行时渲染优先：**Ant Design Vue 组件 + 自研轻量 Schema 解释器**，或评估 Formily Vue。  
- 不在首期引入完整 Amis 运行时（与 Vben 皮肤、权限、路由冲突成本高）。  
- 设计器可先「配置面板生成 Schema」，后「拖拽画布」；**Schema 稳定比设计器炫酷更重要**。

### 5.4 数据读写路径（规划）

```
List 筛选/翻页 ──► PublishedFlowInvoker(queryFlowKey)     默认 sys.resource.query
详情 / 编辑回填 ──► PublishedFlowInvoker(getFlowKey)        默认 sys.resource.get
Create/Update  ──► 表单 rules 校验 ──► create/update FlowKey 默认 sys.resource.create/update
Delete         ──► deleteFlowKey                            默认 sys.resource.delete（软删）
自定义按钮     ──► 用户已发布 flowKey
```

安全底线（与编排 Sql / Code `db.*` 一致）：

- 仅白名单 DataSource；禁止终端拼接任意 SQL。  
- 写操作需 Resource 级权限 + DS `accessMode` +（系统/自定义流内）`Orchestration.Sql.Write`。  
- 字段投影：列表不得默认 `SELECT *`（按 ListView 列配置，由 ResourceQuery 节点投影）。  
- 组合式解析为表达式树，**不得**把 `1 and (2 or 3)` 当字符串拼进 WHERE。  
- 无旁路 WriteService：简单增删改查也是编排节点，只是实施不用画布。

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
    ├─【表设计器 P1】新建托管表 + 默认 ABP 约定列
    │     预览 DDL → 应用到库（CREATE TABLE / INDEX）
    │
    └─【资源】一键创建 → 自动绑定系统逻辑
          │
          ├─ FilterDef     组合查询（与 Condition 同构，P1 终端可用）
          ├─ queryFlowKey  默认 sys.resource.query，可换成自定义
          ├─ get/create/update/delete FlowKey  同上
          ├─ ListView      列 + format/render + ActionDef[]
          └─ FormDef       create / update / detail
                │
                └─ ActionDef.submit → PublishedFlowInvoker(flowKey)
```

**编排主路径 + 简单 CRUD 兼容（已拍板）**：

| 场景 | 实施要做什么 | 实际执行 |
|------|----------------|----------|
| 建表后立刻列表增删改查 | 一键创建资源，**不画布** | 系统逻辑 `sys.resource.*`（只读、种子发布） |
| 提交前校验 / 写子表 / 发消息 / 调 HTTP | 「另存为自定义逻辑」改 DSL | 同一 Input/Output 契约，按钮改 `flowKey` |
| 列展示 | 预设 format | 受控 JS / 描述符 render 后置 |
| 自定义按钮 | 必须选已发布 flowKey | 同源 Invoker |

不要做成「每个列表都强迫实施手动画查询流」——系统逻辑已经是编排。也不要做成「列表页内嵌任意 SQL/JS」或「另开 WriteService 旁路」——那会绕开实例日志、`txMode`、`sys.*`、`visibleTo`。

---

### 5.7 表设计器（DataSource → 物理表）

当前 `DataSource` 只登记连接（code / provider / accessMode / 密钥），**没有表结构**。表设计器补的是：在白名单库上可视化管理列/主键/索引，并把变更同步到真实库。

**P1 已拍板：主路径是「新建托管表并应用到库」**，不是先做只读 introspect。第一次应用即 `CREATE TABLE` + 默认索引，随后非破坏性 `ALTER`（加列/加索引）同样走预览 → 确认 → 应用。

#### 5.7.1 两种表来源

| 来源 | P1 | 含义 |
|------|----|------|
| **托管表 managed** | **必做** | 设计器新建，平台拥有结构；默认带 ABP 约定列；可 CREATE/ALTER |
| **导入表 imported** | 可后置到 P1 末 / P1.5 | 「从库拉取」已有业务表；默认只读结构 |

对平台自己沉淀的业务表：P1 用托管表从零建。对已有 ERP 库：后置导入，避免 P1 范围膨胀。

**不做表设计器的 DataSource**：Redis（无表）；Mongo 首期只做「集合名 + 字段推断」，完整 JSON Schema 后置。表设计器 **仅 SQL 族**（postgres / mysql / sqlserver / oracle）。

#### 5.7.2 平台类型 → 各库映射（设计器里选平台类型，不让用户手写 `varchar(max)`）

| 平台类型 | Postgres | MySQL | SQL Server | Oracle | 说明 |
|----------|----------|-------|------------|--------|------|
| `guid` | `uuid` | `char(36)` | `uniqueidentifier` | `RAW(16)` | 默认主键候选 |
| `string` | `varchar(n)` | `varchar(n)` | `nvarchar(n)` | `VARCHAR2(n)` | 必填 length |
| `text` | `text` | `text` | `nvarchar(max)` | `CLOB` | 长文本 |
| `int` / `long` | `integer` / `bigint` | `int` / `bigint` | 同 | `NUMBER(10)` / `NUMBER(19)` | |
| `decimal` | `numeric(p,s)` | `decimal(p,s)` | 同 | `NUMBER(p,s)` | 金额用这个，不用 float |
| `boolean` | `boolean` | `tinyint(1)` | `bit` | `NUMBER(1)` | Oracle 绑定 0/1 |
| `date` | `date` | `date` | `date` | `TIMESTAMP` | Oracle 无纯 DATE 日类型（DATE 含时分秒），平台 `date` 也落 TIMESTAMP |
| `datetime` | `timestamptz` | `datetime(3)` | `datetimeoffset` | `TIMESTAMP` | 存 UTC，展示用 `sys.timeZone` |
| `json` | `jsonb` | `json` | `nvarchar(max)` | `CLOB` | SQL Server / Oracle 无原生 JSON 列类型时用文本 |
| `enum` | `varchar` + 字典 | 同 | `nvarchar` + 字典 | `VARCHAR2` + 字典 | 不首期建 DB ENUM（迁移痛） |

列属性：name、displayName、type、length/precision/scale、nullable、default（字面量或 `now()`/`gen_random_uuid()` 白名单函数）、unique、comment。  
标识符只允许 `[a-z][a-z0-9_]*`，禁止把用户输入拼进 DDL 字符串。

#### 5.7.3 主键、索引、ABP 约定列（托管表默认开启）

| 对象 | 规则 |
|------|------|
| **主键** | 托管表固定 `Id guid`（对应 ABP `FullAuditedAggregateRoot<Guid>`） |
| **唯一约束** | 业务列可加；约定列不加 unique（除 PK） |
| **普通索引** | 见下表默认索引；业务列可再加复合索引 |
| **外键** | P2 |

**已拍板：新建托管表默认带齐 ABP 约定列**（对齐 `FullAuditedAggregateRoot<Guid> + IMultiTenant`）。设计器「新建表」时这些列自动出现且标记 `origin=convention`，允许改注释，**不允许删、不允许改类型**（避免 CRUD 节点失约）。导入表不强制补这些列。

| 列 | 平台类型 | 可空 / 默认 | 用途 |
|----|----------|-------------|------|
| `Id` | `guid` | NOT NULL，默认 `gen_random_uuid()` / `NEWID()` | PK |
| `TenantId` | `guid` | NULL | 多租户；ResourceQuery 自动 `= sys.tenantId`（Host 策略另见） |
| `ConcurrencyStamp` | `string(40)` | NOT NULL | 乐观并发；更新带原值，冲突 409 |
| `CreationTime` | `datetime` | NOT NULL | 创建时间 ← `sys.Now` |
| `CreatorId` | `guid` | NULL | 创建人 ← `sys.userId` |
| `LastModificationTime` | `datetime` | NULL | 修改时间 |
| `LastModifierId` | `guid` | NULL | 修改人 |
| `IsDeleted` | `boolean` | NOT NULL，默认 `false` | 软删；查询默认 `= false` |
| `DeleterId` | `guid` | NULL | 删除人 |
| `DeletionTime` | `datetime` | NULL | 删除时间 |

默认不建（避免低代码表被 ExtraProperties 搞乱）：`ExtraProperties`。  
`OrganizationId`（数据权限）默认不建，P2 与 IAM 数据范围对接时再作为可选约定列。

**默认索引（随 CREATE TABLE 一起应用）**：

| 索引 | 列 | 用途 |
|------|----|------|
| `PK_*` | `Id` | 主键 |
| `IX_*_Tenant_Deleted` | `(TenantId, IsDeleted)` | 列表默认过滤 |
| `IX_*_Tenant_Creation` | `(TenantId, CreationTime DESC)` | 列表默认排序 |

业务列（如 `OrderNo`、`Status`）由实施自行加索引；建议 `(TenantId, Status)` 这类组合在设计器里点一下即可。

**约定列在 UI 中的默认行为**（一键创建资源时写入 FormDef/ListView，可改）：

| 列 | 新建表单 | 编辑表单 | 列表 |
|----|----------|----------|------|
| `Id` / `TenantId` / `ConcurrencyStamp` / 软删三列 | 隐藏 | 隐藏 | 隐藏 |
| `CreatorId` / `LastModifierId` | 隐藏 | 隐藏 | 隐藏 |
| `CreationTime` | 隐藏 | 只读隐藏 | **默认显示**（datetime format） |
| `LastModificationTime` | 隐藏 | 隐藏 | 默认不显示，可勾选 |

CRUD 节点写入约定列（实施不用配）：

| 操作 | 自动写入 |
|------|----------|
| create | `Id`（空则 new guid）、`TenantId=sys.tenantId`、`CreationTime=sys.Now`、`CreatorId=sys.userId`、`IsDeleted=false`、`ConcurrencyStamp=new` |
| update | `LastModificationTime`、`LastModifierId`；WHERE 带 `Id` + `TenantId` + `ConcurrencyStamp`；禁止改 `Id`/`TenantId`/`CreationTime`/`CreatorId` |
| delete | 软删：`IsDeleted=true`、`DeleterId`、`DeletionTime`；非约定表（导入且无 `IsDeleted`）才物理删 |
| query / get | `AND TenantId = sys.tenantId AND IsDeleted = false`（Host 且未切租户：P1 仅 Host 库或显式租户，禁止跨租户漏数） |

Postgres 首次应用示意（标识符仍走白名单生成器，不手写拼接用户输入）：

```sql
CREATE TABLE orders (
  "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  "TenantId" uuid NULL,
  "ConcurrencyStamp" varchar(40) NOT NULL,
  "CreationTime" timestamptz NOT NULL,
  "CreatorId" uuid NULL,
  "LastModificationTime" timestamptz NULL,
  "LastModifierId" uuid NULL,
  "IsDeleted" boolean NOT NULL DEFAULT false,
  "DeleterId" uuid NULL,
  "DeletionTime" timestamptz NULL
  -- 随后是实施添加的业务列
);
CREATE INDEX "IX_orders_Tenant_Deleted" ON orders ("TenantId", "IsDeleted");
CREATE INDEX "IX_orders_Tenant_Creation" ON orders ("TenantId", "CreationTime" DESC);
```

#### 5.7.4 同步策略（P1：新建即应用到库）

设计时结构存在 Mongo（`TableDefinition` + 版本），物理表在目标 DataSource。**禁止静默 DROP。**

P1 主路径：新建托管表（自动插入 ABP 约定列 + 默认索引）→ 实施加业务列 → 保存草稿 → 「预览变更」→ 「应用到库」→ `syncState=inSync` → 一键创建资源。

```
保存草稿（只改元数据）
        │
        ▼
「预览变更」= diff(设计时, introspect(真实库))
        │  首次多为 CREATE TABLE + CREATE INDEX
        ▼
确认（权限 DataSources.Ddl，DS 必须 write）
        │
        ▼
目标库事务内按序执行 → 回写 syncState / lastAppliedAt
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

预览变更用表格列出「将执行的操作」，破坏性行红色；**第一次成功应用后即可一键创建资源**（字段字典 + 绑定 `sys.resource.*` + 约定列的表单/列表默认值）。

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
  "tenantField": "TenantId",
  "concurrencyField": "ConcurrencyStamp",
  "queryFlowKey": "sys.resource.query",
  "getFlowKey": "sys.resource.get",
  "createFlowKey": "sys.resource.create",
  "updateFlowKey": "sys.resource.update",
  "deleteFlowKey": "sys.resource.delete"
}
```

一键创建时以上 flowKey 自动填系统逻辑；实施把其中任一条改成自定义已发布编码即可「升级」。`kind=query`（自定义 SQL 资源）放到更后：P1 只绑托管表。  
字段字典可覆盖：显示名、字典/枚举、默认控件；底层类型与可空仍以 TableDefinition 为准。

---

### 5.9 组合式查询（复用编排 Condition 模型）

**要支持组合式查询。** 不要再发明第三套语法：与逻辑编排 Condition 使用同一套 `items[] + combine`。

#### 5.9.1 一条条件

| 字段 | 说明 |
|------|------|
| `no` | 编号，从 1 |
| `left` | **与编排 ConditionItem 同构**：资源字段名（白名单）。不用第三套 `field` 键，便于复用前端 `ConditionItemsEditor` 与后端 `ConditionCombineEvaluator` |
| `op` | 匹配模式，按下表按字段类型裁剪 |
| `right` | 值；`between` 为 `[min,max]`；`isEmpty` 可空 |
| `valueSource` | 仅设计器默认条件使用：`user` / `literal` / `sys` / `query` |
| `exposed` | 设计器：是否出现在列表页；`false` 则只作默认隐藏条件 |

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

#### 5.9.3 终端用户：查询表单 + 可视化条件组（取代手写公式）

列表 / 报表运行时 **P1 就要能组合**，但交互给普通人，**禁止**再出现 `1 and (2 or 3)` 输入框。编排 IF 节点仍可用编号公式。运行时拍板（细节见 [`lowcode-report-management.md`](./lowcode-report-management.md) §4）：

1. **查询表单（主）**：实施把高频字段摆成表单（状态、关键字、日期区间）。空控件不参与；已填控件默认 **全部满足**。需要「客户名或手机号」时，设计器放进「任一满足」卡片。  
2. **更多条件（辅）**：Airtable / NocoDB 式条件组——组头在「全部满足 / 任一满足」间切换，可嵌套条件组（最多 3 层），用缩进表达括号。  
3. 内部模型为 `FilterGroup` 树，由 `FilterSqlBuilder` 递归生成 WHERE。现有 `FilterDef.items + combine` 读取时升格为树。  
4. 用户只编辑表单与条件组；系统隐式（租户/软删）和设计器「数据范围」由 ResourceQuery **AND 上去**，运行时不可见。  
5. 空筛选：表单全空且无更多条件 → 只走系统隐式 + 数据范围。空控件不生成 rule。  
6. 字段必须在允许列表内；`left` 含 `.` / SQL 关键字 → 拒绝。  
7. 不再提供「简单模式 / 公式模式」双轨。URL 可序列化 `filter` 树；快捷筛选芯片见报表文档。

提交给 query 流的用户层载荷为树（字段仍白名单）：

```json
{
  "filter": {
    "op": "and",
    "children": [
      { "kind": "rule", "left": "Status", "op": "eq", "right": "Approved" },
      { "kind": "rule", "left": "Amount", "op": "gte", "right": 1000 },
      {
        "kind": "group",
        "op": "or",
        "children": [
          { "kind": "rule", "left": "CustomerName", "op": "contains", "right": "张" }
        ]
      }
    ]
  },
  "page": 1,
  "pageSize": 20,
  "sorting": "CreationTime desc"
}
```

用户只编辑表单与条件组；系统隐式（租户/软删）和设计器「数据范围」由 ResourceQuery **AND 上去**，运行时不可见。空控件不生成 rule。字段必须在允许列表内。URL 可序列化 `filter` 树。

---

### 5.10 查询与写入：一律走逻辑编排（简单 CRUD 用系统逻辑兼容）

**已拍板**：查询、详情、新增、编辑、删除都走已落地的 `PublishedFlowInvoker`（与 `POST /api/logic/{flowKey}` 同源）。这样立刻能用 Condition、`sys.*`、InputSchema `rules`、SubFlow、Code/`db.*`、Rabbit、`txMode`、`visibleTo`、实例日志，而不再维护一套旁路 WriteService。

简单增删改查的「兼容」不是绕开编排，而是 **种子发布 5 条系统逻辑 + 5 个领域节点**。实施建表后一键创建资源即可用；要变强时「另存为」自定义流，契约不变。

#### 5.10.1 领域节点（编排画布新增，对齐 LiteFlow「组件」）

节点库除现有 Http/Code/Condition/SubFlow 外，增加 **资源 CRUD 组件**（一种 kind，五种 palette 预设）：

| 节点 | `op` | 职责 |
|------|------|------|
| ResourceQuery | `query` | 参数化 COUNT + 分页 SELECT；吃 `filters` AST |
| ResourceGet | `get` | 按 PK + 租户 + 未删 取一行 |
| ResourceCreate | `create` | 白名单 INSERT + 填约定列 |
| ResourceUpdate | `update` | 白名单 UPDATE + 并发戳 |
| ResourceDelete | `delete` | 软删（有 `IsDeleted`）或物理删 |

公共配置：`resourceCode`（可来自 `input.resourceCode`）。内部用 TableDefinition 白名单生成 SQL，**禁止**把用户 `combine` 拼进文本；与 Code `db.*` 共用 DataSource 连接，可挂 `txMode=sameDataSource`。

这些节点就是以前规划里的 QueryService/WriteService，只是变成编排组件，可被系统流和自定义流同样调用。

#### 5.10.2 系统逻辑（种子，只读 DSL）

DbMigrator / SaaS 种子写入并发布（Host，`isReusable=true`，`isSystem=true`，管理端不可改 DSL、不可删）：

| flowKey | 图 | End `data` |
|---------|----|------------|
| `sys.resource.query` | Start → ResourceQuery → End | `{ items, total }`（可 `promote`） |
| `sys.resource.get` | Start → ResourceGet → End | 一行对象 |
| `sys.resource.create` | Start → ResourceCreate → End | `{ id }` |
| `sys.resource.update` | Start → ResourceUpdate → End | `{ id, concurrencyStamp }` |
| `sys.resource.delete` | Start → ResourceDelete → End | `{ id, deleted: true }` |

`sys.resource.query` 入参契约（自定义查询流必须兼容，列表才不用改）：

```json
{
  "inputs": [
    { "name": "resourceCode", "type": "string", "required": true, "source": "input" },
    { "name": "page", "type": "number", "default": 1 },
    { "name": "pageSize", "type": "number", "default": 20 },
    { "name": "sorting", "type": "string" },
    { "name": "filters", "type": "object" },
    { "name": "columns", "type": "array" }
  ]
}
```

`resourceCode` / `columns` 由资源运行时注入，**不允许终端用户在筛选框里改**（防越权查别的表、防 `SELECT *`）。`filters` 即 §5.9 载荷。

create 入参：`resourceCode` + `record`（object，表单字段）。系统节点丢掉约定列里应由引擎填写的键，再插入。

#### 5.10.3 简单 CRUD 如何开箱即用

```
应用到库成功 → 一键创建资源「订单」
  → Resource.queryFlowKey = sys.resource.query（其余四个同样）
  → ActionDef 新增/编辑/删除指向对应 flowKey
  → 运行时：
       列表翻页  Invoker(sys.resource.query, { resourceCode:"order", filters, page… })
       点编辑    Invoker(sys.resource.get, { resourceCode:"order", id })
       保存      Invoker(sys.resource.update, { resourceCode:"order", id, record, concurrencyStamp })
```

实施全程不打开编排设计器。列表仍有实例可观测（见下节 persist 策略）。

#### 5.10.4 升级为自定义逻辑（变强大的路径）

管理端资源上点「自定义查询/新建/…」：

1. **克隆**对应 `sys.resource.*` 为 `order.create`（新编码、可编辑、仍 `isReusable`）。  
2. 画布上 **保留** ResourceCreate 节点（继续安全写库），在前后插入已有节点：  
   - 前：Condition / Input `rules` / Code 校验 / Http 取号  
   - 后：ResourceCreate（子表）+ `db.batch` / RabbitMqPublish / SubFlow 计价  
   - 失败：Throw；同库多写开 `txMode=sameDataSource`  
3. 发布 `order.create`；把资源 `createFlowKey` 改成 `order.create`。  
4. 表单 Schema **不用改**（入参仍是 `record` + 系统字段）。

自定义查询同理：以 ResourceQuery 为底，前面加 Condition（按角色分流），或改用 Sql/Code 自己查，但 End 必须仍是 `{ items, total }`。  
禁止自定义流把 `resourceCode` 暴露给匿名调用方乱填——发布校验：系统注入字段 `source` 应为运行时绑定，或 `visibleTo.none`。

列表/按钮只认 **已发布** 版本，与现有编排一致。

#### 5.10.5 与已实现编排能力的对应

| 已有能力 | 资源页怎么用 |
|----------|----------------|
| `PublishedFlowInvoker` | 列表、表单、按钮唯一执行入口 |
| `POST /api/logic/{flowKey}` | 自定义流也可被别的系统调，不单给列表用 |
| Condition + `1 and (2 or 3)` | 列表筛选 AST 同构；自定义流里仍可再加分支 |
| `ConditionCombineEvaluator` | ResourceQuery 生成 WHERE 前先 Validate |
| `sys.userId` / `sys.tenantId` / `sys.Now - 3d` | 约定列填充、默认筛选、日期区间 |
| InputSchema `rules` | 表单校验与 create 流入参校验同一套 |
| SubFlow | 自定义 `order.create` 里调用计价组件 |
| Code + `db.query/execute/batch` | 子表、复杂更新；表名仍建议走 Resource* 节点 |
| RabbitMqPublish | 保存后广播 |
| `txMode=sameDataSource` | 主表+明细原子写 |
| End `visibleTo` / `map` | 查询结果按角色藏列（与 ListView 投影叠加，取更严） |
| FlowInstance | 写操作必落实例；查询默认可降噪（下节） |
| DataSource 白名单 | Resource* 节点只打资源绑定的 DS |

#### 5.10.6 性能：查询也走编排会不会炸

每次翻页都跑 Flow。P1 规则：

| 项 | 约定 |
|----|------|
| 查询/get | `traceMode=errors`：成功不写 `FlowInstance`（或只写采样）；失败必写 |
| 增删改 | `traceMode=always`：每次落实例，便于审计 |
| 资源可覆盖 | 某张表要追查谁查过谁，可把 query 改为 always |
| DryRun | 仅设计器试运行；列表运行时走正式 Invoker |
| 超时 | query 默认短超时（如 8s），pageSize 上限 200 |

系统 query 流保持「单节点 ResourceQuery」，开销应接近直查；自定义流若串 Http，由实施自己承担。

#### 5.10.7 报表 / 跨库

通用报表（查询表单、条件组、列着色/转换/汇总、按钮、菜单）见独立规划 [`lowcode-report-management.md`](./lowcode-report-management.md)。  
自定义 `queryFlowKey` 可以跨表，须返回 `{ items, total, summary? }`，SQL 必须参数化。资源默认列表与报表 **共用** `FilterGroup` 查询模型，不再在运行时暴露编号组合式。

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
| `flowKey` | 已发布逻辑；一键创建资源时填 `sys.resource.*`，可换成自定义；**不允许空**（无旁路直写） |
| `confirm` | 二次确认文案（删除默认开） |
| `visibleWhen` | 同行条件模型（如 `1. Status eq Draft`，组合式 `1`） |
| `acl` | 权限码；无权限则不渲染 |
| `after` | `refresh` / `close` / `goto` |

#### 5.12.2 新增

```
点「新增」→ 打开 FormDef(create)
  → 用户填（默认值可用 sys.* / 日期表达式）
  → 前端按 FormSchema rules 预校验
  → Invoker(createFlowKey, { resourceCode, record })
       默认 sys.resource.create → ResourceCreate 填约定列并 INSERT
       自定义 order.create → 校验 / 取号 / ResourceCreate / 子表 / 发消息
  → after=refresh
```

表单字段装进 `record`；约定列不要让用户填。`inputMap` 仅当自定义流入参名与 `record` 不一致时才需要。

#### 5.12.3 编辑（含只读）

```
点「编辑」→ Invoker(getFlowKey, { resourceCode, id })
  → 打开 FormDef(update)，套入行数据
  → 字段按 mode 覆盖：visible / required / readonly / disabled
  → 提交 Invoker(updateFlowKey, { resourceCode, id, record, concurrencyStamp })
```

只读有三层，从硬到软：

| 层 | 例 |
|----|----|
| 表结构 / 约定列 | PK、审计、租户、并发戳：永远不可改（ResourceUpdate 会丢弃这些键） |
| 表单 mode | 编辑时 `OrderNo` readonly；详情全部 disabled |
| 表达式 | `readonlyWhen`: 条件模型 `Status ne Draft`（不写任意 JS） |

同一份 FormSchema，用 `modes.create|update|detail.overrides` 覆盖。  
详情 = get + 全只读 + 无提交。  
托管表默认有 `ConcurrencyStamp`：更新带原值，冲突 409，前端提示刷新。

#### 5.12.4 删除与自定义

- 默认 `sys.resource.delete`：有 `IsDeleted` 则软删。  
- 自定义按钮：「提交」→ 已发布 `order.submit`；「同步 ERP」`open=none` 直接 Invoker。  
- 批量：入参 `ids[]`；须自定义流内 `db.batch` / 循环；列表限制单次勾选上限。

#### 5.12.5 表单设计要点（承接 5.2）

- 控件：输入、数字、日期、下拉（静态字典）、开关、文本域；关联选择 P2。  
- 布局：单列/双列、分组；Tab 后置。  
- 校验：复用编排 InputSchema `rules`，**表单上不跑任意 JS**；过程式校验放 create/update 流的 Code 节点。  
- 联动：显示/必填/禁用用条件模型（与筛选、编排同一套），不首期 onChange JS。

#### 5.12.6 写路径安全（与编排一致）

- 仅白名单 DataSource + 字段投影；表单多出来的键写不进库。  
- Resource* 节点写库需要 `Orchestration.Sql.Write` + DS `accessMode` write。  
- 同单多表：自定义流开 `txMode=sameDataSource`。  
- 无 WriteService 旁路，因此审计/可见性/事务语义只有一套。

---

### 5.13 运行时页、发布与菜单

配置态（设计器）与使用态（业务列表）分开，学 NocoBase：

1. 资源发布（草稿 → 已发布），运行时只读已发布 Schema（与 FlowVersion 同一套心智）。  
2. 发布时登记菜单：`/app/{resourceCode}`，权限 `App.{resourceCode}` + `.Create/.Update/.Delete`。  
3. 列表页结构：组合筛选区（§5.9.3）→ 工具栏按钮 → 表格 → 分页 → 抽屉表单。  
4. 字段级权限后置：无权限列不进 ResourceQuery 投影、不进表单。  
5. 资源发布前须校验五个 flowKey 均指向**已发布**逻辑，且 query 的 End 含 `items`/`total`。

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
| `DataSource` | 表设计器挂在 SQL 族 DS 下；Resource* 节点只打资源绑定的 DS |
| 编排 Condition / `ConditionCombineEvaluator` | 列表筛选 **同构** `items + combine`；前端复用 `ConditionItemsEditor` |
| `PublishedFlowInvoker` / `/api/logic/{flowKey}` | **查询与写入唯一入口** |
| 系统逻辑 `sys.resource.*` | 种子只读流；一键创建资源的默认 flowKey |
| ResourceQuery/Create/… 节点 | 新领域组件；系统流与自定义流共用 |
| SubFlow / Code `db.*` / Rabbit / `txMode` | 自定义 CRUD 流里增强 |
| `sys.userId` / `sys.tenantId` / `sys.Now` | 约定列自动填、默认筛选 |
| InputSchema `rules` | 表单与 create 流入参同一套校验 |
| End `visibleTo` / `map` | 查询结果按角色藏列 |
| 表设计器 DDL | **独立 DdlExecutor**，不进业务 Flow |
| ABP 权限 / 菜单 | `App.{resourceCode}.*`；DDL 单独权限 |
| 多租户 | TableDefinition / Resource `IMultiTenant`；查询注入 TenantId |
| Vue Vben | 运行时表+抽屉；设计器：数据连接/表、资源、列表、表单 |

**服务落点建议（评审项）**：

- **方案 α**：继续放 `Meta.Dow.SaaS`（与编排同库，迭代快；表结构元数据与 DataSource 同处）。  
- **方案 β**：新建 `Meta.Dow.AppBuilder` 服务（边界清晰，跨服务调用编排）。  

首期规划推荐 **α**，待模型稳定再拆 β。

---

## 9. 领域模型草案（仅规划）

```
DataSource (已有)
  └── TableDefinition          ★ 新增
        - origin: managed（P1）| imported（后置）
        - columns[]: origin=convention|user …
        - primaryKey = Id；默认 IX (TenantId,IsDeleted) / (TenantId,CreationTime)
        - syncState, schemaVersion, lastAppliedAt, ddlHistory[]

Resource
  - binding.table；primaryKey, titleField
  - softDeleteField, tenantField, concurrencyField
  - query/get/create/update/delete FlowKey   ★ 默认 sys.resource.*
  - FilterDef: { items[], combine }          ★ 与 Condition 同构
  - ListView / FormDef / ActionDef[]

系统 FlowDefinition（种子，isSystem）
  sys.resource.query|get|create|update|delete
  节点 kind=ResourceCrud op=query|get|create|update|delete

（用户自定义流只引用 flowKey；不复制第二套执行器）
```

版本策略：先「应用到库」，再「发布资源页」。系统逻辑不可改 DSL；自定义流走现有草稿 → 发布。查询 `traceMode=errors`，写 `always`。

---

## 10. 非功能与风险

| 风险 | 缓解 |
|------|------|
| 做成第二个 Retool，与编排抢主题 | 坚持 Resource 一等公民；画布 App 不做 |
| 工作流与编排概念混淆 | 文档、菜单、设计器入口强制分离 |
| Schema 频繁破坏兼容 | 协议版本号 `schemaVersion`；迁移器 |
| 动态 SQL 注入 | 禁止字符串拼 SQL；参数化 + 字段/标识符白名单；组合式走 AST |
| 静默 DDL 丢数据 | 预览变更；破坏性默认关；打字确认；DDL 独立权限与审计 |
| 列表每页都跑 Flow，实例爆炸 | query/get 默认 `traceMode=errors`；系统 query 保持单节点；pageSize 上限 |
| 实施不会画布，CRUD 不可用 | 系统逻辑 `sys.resource.*` 种子 + 一键绑定；「另存为」才要画布 |
| 自定义流改掉分页契约 | 发布资源时校验 query End 含 items/total |
| 列上任意 JS / HTML → XSS | format 纯函数沙箱；render 只返回组件描述符白名单 |
| 设计器技术债（React 生态） | Vue 优先配置式；慎引 Designable/Amis 全量 |
| 看板拖拽与审批冲突 | onDrop 前检查是否存在进行中任务 |
| 表结构与列表配置漂移 | 拉取后引用缺失列标红；运行时跳过，不直接报死页 |

---

## 11. 分期建议（实现前必须评审）

### P0 — 规划冻结（当前）

- 表设计器 P1 = **新建托管表并应用到库**（已拍板）。  
- 查询/写入 = **一律编排**；简单 CRUD = 系统逻辑兼容（已拍板）。  
- 组合查询 P1 即上到终端（已拍板）。  
- 托管表默认 ABP 约定列（已拍板）。  
- 仍待选：表单 Schema（自研 vs Formily）；工作流引擎（P3）。  

### P1 — 托管表 + 系统 CRUD + 组合列表（无工作流、无看板）

- 表设计器：新建托管表、**默认 ABP 约定列 + 默认索引**、加业务列/索引、预览 DDL、**应用到库**（P1 禁用破坏性 DDL）。  
- 编排：新增 ResourceCrud 五节点；种子发布 `sys.resource.query/get/create/update/delete`。  
- 一键创建资源：绑定系统 flowKey；约定列的表单/列表默认显隐。  
- 列表运行时：`PublishedFlowInvoker`；**前端组合筛选**（复用 ConditionItemsEditor）；分页排序；预设 format。  
- 表单：create/update/detail；编辑可配只读；提交走对应 flowKey。  
- query/get `traceMode=errors`；写操作落实例。  
- 菜单与权限种子（含 Ddl、`App.{resource}.*`）。  

### P1.5 — 导入表与自定义升级体验

- 从库拉取 imported 表（不强制约定列）。  
- 资源上「另存为自定义逻辑」向导（克隆 sys.resource.* + 改绑 flowKey）。  
- 查询 `traceMode` 可按资源覆盖为 always。  

### P2 — 体验、安全、列渲染

- 字段级权限、关联选择、表单条件联动。  
- 列自定义 format JS（沙箱）+ render 描述符白名单。  
- 破坏性 DDL + syncState 冲突。  
- 发布版本、导入导出 JSON、配置审计。  
- `OrganizationId` 可选约定列 + 数据权限。  

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
- **不另开 WriteService 旁路**（简单 CRUD 也是系统逻辑，不是直写 API）。  
- **不强迫实施为每张表手动画查询流**（系统 `sys.resource.*` 已覆盖）。  
- Redis DataSource 不做表设计器与通用列表。  

---

## 13. 评审清单（规划合理后再开工）

请产品/架构确认后再进入实现（已拍板项保留备查）：

1. [x] P1 表设计器：**新建托管表 + 应用到库**（导入表后置）。  
2. [ ] 破坏性 DDL（删列/改类型）P1 是否彻底禁止？（规划默认禁止）  
3. [x] Resource P1 只绑托管表；query 一律 flowKey（默认系统逻辑）。  
4. [x] 查询/写入一律走编排；简单 CRUD 用 `sys.resource.*` 兼容。  
5. [x] 组合查询 P1 即上到前端（`items + combine`）。  
6. [x] 新建托管表**默认** ABP 约定列（Id/TenantId/ConcurrencyStamp/审计/软删）。  
7. [ ] 列 render：P1 仅预设 format；自定义 JS 是否必须沙箱 + 描述符白名单（推荐是）？  
8. [ ] 表单 Schema：自研 / Formily / form-js？  
9. [ ] 工作流：P3 自研轻量是否够用？何时必须上 Elsa/Flowable？  
10. [ ] 服务落点：SaaS 内 vs 独立服务？  
11. [ ] 看板是否必须与列表同一 FilterDef？  
12. [ ] 与现有「编排 InputSchema 试运行表单」命名如何区分（避免两个「表单」菜单）？  
13. [ ] 多租户下 Resource / 表模板是否支持 Host 下发？  
14. [ ] 查询 `traceMode=errors` 是否满足审计？哪些表必须 always？  
15. [ ] Host 未切租户时，托管表 query 是禁查还是只查 TenantId 为空？  

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
- [`lowcode-report-management.md`](./lowcode-report-management.md) — 通用报表、查询表单/条件组、列汇总、按钮、菜单  
- [`iam-rbac-menu.md`](./iam-rbac-menu.md) — 权限与菜单  

---

## 修订记录

| 日期 | 说明 |
|------|------|
| 2026-09-18 | 初稿：开源对标 + 表单/工作流/看板规划，明确不实现 |
| 2026-09-21 | 拍板并细化：P1 新建表即应用到库；查询/写入一律 `PublishedFlowInvoker`（系统逻辑 `sys.resource.*` 兼容简单 CRUD）；前端 P1 即可组合查询；托管表默认 ABP 约定列 |
| 2026-09-21 | 平台类型映射补齐 SQL Server / Oracle；查询 COUNT/分页/空值谓词/日期布尔绑定按引擎方言处理 |
| 2026-09-21 | 运行时筛选改为查询表单 + 可视化条件组；通用报表规划见 `lowcode-report-management.md` |
