# Meta.Dow 通用报表管理规划

> **性质**：产品与架构规划。本文在 [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md) 的资源 CRUD 之上，补齐 **报表页**（查询表单、可视化条件组、列标记/转换/汇总、按钮功能、菜单发布）。  
> **R1 已落地**：FilterGroup 后端、查询表单 + 可视化条件组、ReportDefinition CRUD、列 format/tag/dict/汇总、基础按钮、菜单种子。R2/R3 仍按分期。  
> **前端**：`D:\Project\vue-demo`（web-antd / Ant Design Vue）  
> **后端**：SaaS 编排运行时（`PublishedFlowInvoker` + `FilterSqlBuilder`）

**一句话**：报表是「已发布的查询页配方」，不是第二套 CRUD，也不是积木报表那种 Excel 画布。普通人用 **查询表单** 和 **「全部满足 / 任一满足」条件组** 组合条件，**绝不手写** `1 or 2 and 3`。

---

## 目录

1. [为何单独成文](#1-为何单独成文)
2. [开源对标](#2-开源对标github)
3. [产品心智](#3-产品心智)
4. [查询：表单 + 可视化条件组](#4-查询表单--可视化条件组普通人能用)
5. [列：转换、着色、汇总](#5-列转换着色汇总)
6. [按钮与功能定义](#6-按钮与功能定义)
7. [KPI、分组、导出与其它增强](#7-kpi-分组导出与其它增强)
8. [菜单与权限](#8-菜单与权限)
9. [领域模型与契约](#9-领域模型与契约)
10. [运行时页面结构](#10-运行时页面结构)
11. [安全与方言](#11-安全与方言)
12. [分期](#12-分期)
13. [明确不做什么](#13-明确不做什么)

---

## 1. 为何单独成文

现有 `AppResource` 已能：绑表、组合筛选、列表、抽屉表单、`sys.resource.*`。缺口是「业务报表」常见能力：

| 缺口 | 现状 | 报表要补 |
|------|------|----------|
| 查询交互 | 条件行 + 手写 `1 and (2 or 3)` | **查询表单** + **可视化条件组** |
| 一资源多页 | 一个 ListView | 同一资源可发多份报表（待审 / 汇总 / 只读台账） |
| 列表现 | 仅 `formatPreset` | 字典转换、阈值着色、进度、Tag、链接 |
| 汇总 | 只有 `total` 行数 | 本页 / 筛选结果合计、均值、计数 |
| 按钮 | 增删改详情骨架 | 弹窗填报、单行 / 多行 / 单列批量改、确认、后置刷新 |
| 入口 | 资源码路由 | **发布即登记菜单 + 权限** |
| 跨表查询 | 文档写「另开页」 | 报表可绑自定义 `queryFlowKey`，契约兼容 |

资源仍是数据与默认 CRUD 的一等公民；**报表是挂在资源（或独立查询流）上的页面配方**。看板、人审、Excel 套打仍不进本文实现范围。

```
数据源 → 表设计器 → AppResource（结构 + 默认 CRUD）
                         │
                         ├── 默认列表（实施自用 / 简单台账）
                         └── ReportDefinition[]（可发布到菜单的业务报表）
                                      │
                                      ▼
                         PublishedFlowInvoker(queryFlowKey)
                                      │
                                      ▼
                         { items, total, summary?, groups? }
```

---

## 2. 开源对标（GitHub）

下列只学信息架构，不引入整套运行时。

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

1. **页模型**：NocoBase / Supasheet（资源 + 多视图/报表）。  
2. **普通人筛选**：NocoBase Filter Form + NocoDB/Airtable 条件组。  
3. **按钮**：Amis Action + NocoBase scene。  
4. **列与汇总**：Amis mapping + NocoBase table summary；合计语义学积木报表的「本页 vs 全量」。  
5. **执行**：继续 `PublishedFlowInvoker`，不另起报表引擎。

逻辑编排 Condition 节点可以 **继续** 用编号组合式（给实施/研发）。**报表运行时禁止把这套语法暴露给业务员。**

---

## 3. 产品心智

### 3.1 两种查询入口（必须同时有）

| 入口 | 给谁 | 交互 | 默认组合 |
|------|------|------|----------|
| **查询表单** | 所有人 | 像传统业务系统：状态、日期区间、关键字排成表单，点查询 | 已填字段之间 **全部满足（AND）**；空字段 **不参与** |
| **可视化条件组** | 需要「或 / 分组」时 | 「全部满足 / 任一满足」+ 添加条件 / 添加条件组 | 组内统一 AND 或 OR，用嵌套表达括号，不写公式 |

二者结果 **AND** 到最终 WHERE（再 AND 系统隐式与设计器数据范围）。

### 3.2 报表类型

| `kind` | 数据从哪来 | 典型 |
|--------|------------|------|
| `resource` | `AppResource` + `sys.resource.query`（可换成自定义 query 流） | 订单台账、待审列表 |
| `flow` | 仅 `queryFlowKey`，须返回报表契约 | 跨表、只读统计、ERP 视图 |

`resource` 报表仍可声明 `queryFlowKey` 覆盖默认查询（例如先关联客户名再分页）。

### 3.3 配置态 / 使用态

学 NocoBase：设计器改草稿；**发布** 后运行时只读已发布 Schema；发布时可勾选写入 `SysMenu`。

---

## 4. 查询：表单 + 可视化条件组（普通人能用）

### 4.1 为什么淘汰运行时手写 `1 or 2 and 3`

| 问题 | 后果 |
|------|------|
| 要记编号、and/or、括号优先级 | 业务员不会用，只能全 AND |
| 删第 2 条后编号错乱 | 组合式 silently 错或报错 |
| 和「查询表单」两套心智 | 实施配一套、用户用另一套 |
| 与编排 Condition 绑定过死 | 编排可以保留公式；报表必须白话 |

内部仍可把树编译成现有 `ConditionCombineEvaluator`（便于复用测试），但 **UI 只编辑树，不编辑字符串。**

### 4.2 统一内部模型：`FilterGroup`（树）

```json
{
  "op": "and",
  "children": [
    { "kind": "rule", "left": "Status", "op": "eq", "right": "Approved" },
    {
      "kind": "group",
      "op": "or",
      "children": [
        { "kind": "rule", "left": "CustomerName", "op": "contains", "right": "张" },
        { "kind": "rule", "left": "Phone", "op": "contains", "right": "138" }
      ]
    }
  ]
}
```

| `kind` | 含义 |
|--------|------|
| `group` | 一组，组内 `op` 只能是 `and` 或 `or`（界面文案：全部满足 / 任一满足） |
| `rule` | 一条：字段 × 运算符 × 值 |

规则：

- 空值 / 空串的 `rule` **编译时丢弃**（空着不算条件）。  
- 丢弃后若组空，该组也不产出 SQL。  
- 组最多 **3 层**（根不算），防止套娃。  
- `not` 不做独立组类型；需要「不等于」用运算符 `ne` / `notcontains` / `isnotempty`。  
- 字段必须在报表允许列表内（与现 Filter 白名单同一套）。

后端 `FilterSqlBuilder.BuildFromGroup(provider, table, group, args)` **递归生成括号 SQL**，不再要求前端传 combine 字符串。编号公式仅作为可选 `Debug` 导出。

### 4.3 查询表单（主路径，给普通人）

设计器拖出「查询区」，每个控件绑定 **一个字段 + 默认运算符**，不是绑任意表达式。

```
┌ 查询 ──────────────────────────────────────────────────────────────────┐
│  状态 [已审核 ▾]     客户 [张    ]     金额 [1000] ~ [     ]            │
│  下单日 [2026-09-01] ~ [2026-09-21]     □ 仅看我的                      │
│                                                                        │
│  [查询]  [重置]   已筛 3 项   [更多条件 ▾]   快捷：[待审核] [本周] [超期] │
└────────────────────────────────────────────────────────────────────────┘
```

| 设计器字段 | 说明 |
|------------|------|
| `key` | 表单键，稳定，不随标题改 |
| `field` | 表字段白名单 |
| `op` | 默认运算符：`eq` / `contains` / `between` / `in` / `gte`… |
| `control` | `input` / `select` / `multiSelect` / `date` / `dateRange` / `number` / `numberRange` / `switch` / `user` |
| `span` | 栅格 1–4，默认三列 |
| `placeholder` / `dictCode` | 展示 |
| `emptyPolicy` | 始终 `ignore`（空不参与）；禁止「空也当条件」除非显式 `isEmpty` 控件 |
| `visibleWhen` | 可按其它查询字段显隐（条件树，P2） |

**组合规则（写进产品文案，不要写进输入框）：**

1. 查询表单里 **填了的控件之间默认全部满足**。  
2. 设计器可把若干控件放进一个 **「任一满足」卡片**（例如「客户名或手机号」）。卡片对外仍是表单的一块，对内是 `op=or` 的 `FilterGroup`。  
3. 用户 **不必** 选择 AND/OR；需要复杂逻辑时点「更多条件」。

提交时前端把表单值编成 `FilterGroup`，空控件不生成 `rule`。

日期区间、数字区间自动 `between`（右开或闭由平台统一：列表与 ResourceQuery 已有 between 语义，保持一致）。多选自动 `in`。

### 4.4 可视化条件组（「更多条件」/ 高级筛选）

学 Airtable / NocoDB，文案用中文白话：

```
┌ 更多条件                                          匹配方式：● 全部满足  ○ 任一满足 ┐
│  1  [状态 ▾]     [等于 ▾]       [已审核 ▾]                                   [×] │
│  2  [金额 ▾]     [大于等于 ▾]   [1000    ]                                   [×] │
│                                                                                  │
│  ┌ 条件组 · 任一满足 ──────────────────────────────────────────── [删除组] ─┐   │
│  │  [客户名 ▾]  [包含 ▾]  [张]                                            [×] │   │
│  │  [客户名 ▾]  [包含 ▾]  [李]                                            [×] │   │
│  │  [+ 添加条件]                                                             │   │
│  └──────────────────────────────────────────────────────────────────────────┘   │
│  [+ 添加条件]   [+ 添加条件组]                                                   │
└──────────────────────────────────────────────────────────────────────────────────┘
```

交互约定：

| 操作 | 普通人看到的 |
|------|----------------|
| 组头切换 | 「全部满足」= 组内 AND；「任一满足」= 组内 OR |
| 添加条件 | 新的一行字段/运算符/值 |
| 添加条件组 | 缩进一块，块自己再选全部/任一 |
| 删除 | 行或整组；最后一条删空 = 无高级条件 |
| 运算符 | 随字段类型裁剪（与现 `OP_OPTIONS` 相同），界面显示「等于 / 包含 / 介于…」 |
| 值控件 | 随运算符变：介于两个框，属于多选，为空无值 |

**禁止**：编号、`and`/`or`/`()` 输入框、模板「全 AND」当唯一组合手段（可保留为「把当前组设为全部满足」的按钮，但不要露出公式）。

嵌套语义用缩进 + 卡片表达括号：`全部满足( 状态=已审核 , 金额≥1000 , 任一满足( 姓名含张 , 姓名含李 ) )`。

### 4.5 四层过滤（用户改不了前两层）

```
最终 WHERE =
    系统隐式（TenantId / IsDeleted）
AND 设计器「数据范围」（可视化条件组，exposed=false）
AND 查询表单（已填控件）
AND 更多条件（可视化条件组，用户自建）
AND 点中的快捷筛选（若有）
```

| 层 | 谁配 | UI |
|----|------|-----|
| 系统隐式 | 引擎 | 无 |
| 数据范围 | 实施 | 设计器里的条件组，运行时不可见 |
| 查询表单 | 实施配字段，用户填值 | 报表顶部 |
| 更多条件 | 用户 | 折叠面板，默认收起 |
| 快捷筛选 | 实施配预设 | 芯片，可多选（之间 AND）或单选 |

快捷筛选是 **命名好的 `FilterGroup` 片段**，例如「待审核」= `Status in (Submitted,Reviewing)`，「本周」= `CreationTime between sys.StartOfWeek and sys.Now`。

### 4.6 查询表单与条件组如何同时存在而不打架

- 查询表单适合 **高频、固定、业务员每天都填** 的 4–8 个字段。  
- 更多条件适合 **临时、少见、或/且混用**。  
- 同一字段允许两边都出现：结果 AND（更严）。设计器可勾选「此字段仅出现在表单，高级里隐藏」。  
- URL 序列化整棵 `FilterGroup`（含表单编译结果 + 更多条件），便于分享「待审核且金额>1000」。

### 4.7 与编排 Condition 的关系

| 场景 | 模型 | UI |
|------|------|-----|
| 逻辑编排 IF 节点 | 可继续 `items[] + combine` | 设计器给实施，可保留编号（熟手） |
| 报表 / 资源运行时 | **只认 `FilterGroup` 树** | 查询表单 + 条件组 |
| 后端 SQL | 一棵树递归 | `FilterSqlBuilder` |

迁移：现有 `FilterDef.items + combine` 在读取时 **升格为一棵树**（`and`/`or`/`()` 解析为 group）；保存报表后只存树。资源 P1 列表同样切换，避免两套运行时。

### 4.8 查询表单的布局与校验

- 布局：`inline`（一行）/ `grid`（默认 3 列，可配置 2/4）。  
- 展开：字段 > 6 时默认折一行，「展开更多字段」。  
- 提交：`manual`（点查询）默认；`auto`（改值即查）仅快捷筛选与开关。  
- 前端不做任意 JS 校验；必填只用于「这个报表强制要先选组织再查」类字段（`required=true`，空则拦截）。  
- 值仍走现有类型绑定（空日期不传 `''`，布尔/区间按方言）。

---

## 5. 列：转换、着色、汇总

列配置挂在报表上，**不改表结构**。与现 ListView 的 `format` / `render` 衔接并扩展。

### 5.1 管道（从左到右）

```
原值 → transform（字典/单位/计算）→ format（字符串或类型化展示）→ style（颜色规则）→ render（Tag/进度/链接）
```

`style` 只改外观；`transform` 只改展示值，**不写回数据库**（写回必须走按钮 + Flow）。

### 5.2 transform（转换）

| `type` | 例 | 说明 |
|--------|----|------|
| `none` | | 默认 |
| `dict` | `order.status` | 码表 → 文案；缺省显示原值 |
| `enumMap` | `{ "0": "草稿" }` | 静态映射 |
| `unit` | 分 → 元 `/100` | `factor` + `suffix` |
| `datePart` | 只要日期 | 复用现 date format |
| `expr` | P2 | 只读沙箱 `(value,row,ctx)=>`，禁止 DOM/fetch |

计算列 `source=expr` 或 `source=flowField`：查询流 End 已带出的派生字段（如 `OverdueDays`），报表只负责展示。

### 5.3 style（标记颜色）

学积木报表「预警」和 Excel 条件格式，但用 **同一套条件树**（单条 rule 即可），禁止 CSS 字符串。

```json
{
  "styleRules": [
    {
      "when": { "kind": "rule", "left": "$value", "op": "gt", "right": 10000 },
      "cell": { "tone": "danger" },
      "row": { "tone": "danger-subtle" }
    },
    {
      "when": { "kind": "rule", "left": "Status", "op": "eq", "right": "Approved" },
      "cell": { "tone": "success" }
    }
  ]
}
```

`$value` = 当前单元格；也可引用同行其它字段。

`tone` 白名单：`default / info / success / warning / danger` 及 `-subtle`。映射到 Ant Design Token（红/绿/橙），**同时**用图标或前缀文字，不只靠颜色。

整行着色：`rowStyleRules` 挂在报表级，避免每列重复。

### 5.4 render（组件描述符，不返回 HTML）

P1 预设即可覆盖大部分报表：

| component | 用途 |
|-----------|------|
| `text` | 默认 |
| `tag` | 状态 |
| `link` | 站内钻取 `/app/reports/{code}?id=` |
| `progress` | 0–100 |
| `boolean` | 是/否 |
| `image` | 缩略图 |
| `dict` | 与 transform.dict 合并 |

P2 再开放受控 `render` 函数，返回描述符，规则与 board 文档 5.11 相同。

### 5.5 summary（汇总）

学 NocoBase table summary + 积木 `sum` vs `dbsum`：

| `scope` | 语义 | SQL |
|---------|------|-----|
| `page` | 当前页行（前端可算，或后端顺带） | 可不打库 |
| `filtered` | **当前筛选下全表**（推荐默认，业务要的「合计」） | 与列表同一 WHERE 再 `SUM/AVG/...` |
| `group` | 分组小计 | `GROUP BY` 后的聚合 |

列上：

```json
{
  "field": "Amount",
  "summary": { "fn": "sum", "scope": "filtered", "format": "currency" }
}
```

`fn` 白名单：`sum / avg / min / max / count / countDistinct`。仅数值/日期列可 sum/avg；其它只能 count。

查询契约扩展：

```json
{
  "items": [ ... ],
  "total": 1280,
  "summary": {
    "Amount": { "sum": 128900.5, "avg": 100.7 },
    "_count": 1280
  }
}
```

`sys.resource.query` 在 COUNT 之外按列配置生成第二条聚合 SQL（同一 WHERE、参数化、方言函数）。禁止把 SUM 扫进行数据在前端加（分页会错）。

页脚展示：表格 `summary` 行 + 可选顶部统计条（与 KPI 共用 `summary`）。

### 5.6 其它列行为

冻结、宽度、显隐、排序、对齐、多级表头（P2）、用户自定义列显隐存本地（P2）。投影仍按可见列白名单 `SELECT`，禁止 `SELECT *`。

---

## 6. 按钮与功能定义

学 Amis Action + NocoBase scene。按钮是报表一等配置，**功能 = 打开方式 + 作用范围 + 表单 + flowKey**。

### 6.1 ActionDef（报表版）

| 字段 | 说明 |
|------|------|
| `key` / `label` / `icon` / `tone` | 展示；`tone`：primary/default/danger |
| `scene` | `toolbar` / `row` / `batch` / `cell` / `empty` |
| `scope` | 见下表 |
| `open` | `none` / `modal` / `drawer` / `page` / `link` |
| `formRef` | 指向报表内 `forms[]` 或资源 FormDef 的 `create\|update\|detail` 或独立 Form 片段 |
| `flowKey` | 已发布逻辑；`open=none` 时必填 |
| `confirm` | 文案；危险操作默认开 |
| `visibleWhen` / `disabledWhen` | `FilterGroup` 对 **当前行**（或批里每一行）求值 |
| `acl` | 权限码，默认 `App.Report.{code}.Action.{key}` |
| `payload` | 入参映射，见 6.3 |
| `after` | `refresh` / `close` / `goto` / `toast` 可组合 |
| `group` | 行按钮过多时进「更多」 |
| `batchLimit` | 批量上限，默认 100 |

`kind` 仅作模板：`create` / `update` / `delete` / `detail` / `export` / `custom`。真正执行只看 `open + formRef + flowKey`。

### 6.2 作用范围 `scope`（覆盖「单列 / 多列 / 多行」）

用户说的「单列操作 / 多列操作」按产品拆成 **选了谁** 和 **改哪些字段**：

| `scope` | 选中什么 | 典型 |
|---------|----------|------|
| `none` | 不依赖行 | 新增、导入、跳转 |
| `row` | 当前一行 | 编辑、详情、提交、作废 |
| `selection` | 勾选多行 | 批量删除、批量提交 |
| `cell` | 当前单元格 | 点状态 Tag 快速改状态 |
| `column` | 当前列 + 勾选行（无勾选则当前筛选，须二次确认） | 「把选中行的负责人改成…」 |

| `fieldsMode` | 表单里出现什么 |
|---------------|----------------|
| `full` | 完整 FormDef（多列/多字段填报） |
| `whitelist` | 只出现 `fields[]`（**单列或少数字段**弹窗） |
| `none` | 无表单，直接跑 Flow |

因此：

- **弹窗填报（多字段）**：`open=modal` + `fieldsMode=full` + `scope=row`。  
- **单列改值**：`open=modal` + `fieldsMode=whitelist` + `fields:["Status"]` + `scope=row|selection`。  
- **多行同一操作**：`scope=selection`，入参 `ids[]` + 表单值（一份表单应用到全部选中行）。  
- **无表单动作**：`open=none` + confirm + Flow（同步 ERP）。

批量必须走自定义流或系统 `sys.resource.batchUpdate`（P2 种子）：白名单列 + `ids` + 单组值，禁止前端循环打 N 次 update（可先 P2；P1 批量删除可循环但有上限与同一 DS 事务视情况）。

### 6.3 入参契约

```json
{
  "resourceCode": "order",
  "reportCode": "order-pending",
  "ids": ["..."],
  "record": { "Status": "Approved", "Comment": "..." },
  "row": { },
  "filters": { },
  "concurrencyStamps": { }
}
```

- 行按钮：`ids` 长度 1，`row` 为当前行投影（只读上下文，写库仍以 Flow/ResourceUpdate 白名单为准）。  
- 批量：`ids` 为勾选；`record` 为弹窗填写。  
- 新增：无 `ids`，`record` 为表单。  
- `filters` 仅导出/「按当前筛选批量」需要，默认 **不** 把筛选当更新范围（防误伤）；若 `scope=column` 且未勾选，必须 confirm 文案写明「将更新当前筛选下 N 条」。

### 6.4 执行管道

```
点击
 → 无权限则不渲染
 → visibleWhen / disabledWhen
 → 校验 scope（批量未选则 toast「请先勾选」）
 → confirm?
 → open 表单? 预填 getFlowKey / 当前行
 → 前端 rules 预校验
 → Invoker(flowKey, payload)
 → after：关弹窗、刷新列表、toast
```

写路径与资源 CRUD 相同：无旁路 WriteService。查询类按钮（导出）可用只读 flow，`traceMode=errors`。

### 6.5 内置按钮模板（一键从资源生成报表时带出）

工具栏：新增、导出当前筛选。  
行：详情、编辑、删除。  
批量：删除（须确认）。  

实施再加「审核」「分配」等自定义按钮。

---

## 7. KPI、分组、导出与其它增强

超出原始需求、但报表页几乎总会用到：

| 能力 | 说明 | 分期 |
|------|------|------|
| **KPI 条** | 顶部 2–4 张统计卡，数据来自同一 `summary` 或独立 `kpiFlowKey` | P-Report.1 |
| **快捷筛选芯片** | 见 4.5 | P-Report.1 |
| **分组行** | 按一列 group，组头显示小计；P1 可只做单层 | P-Report.2 |
| **导出** | 当前筛选 Excel，列与可见列一致，上限（如 1 万行）异步 | P-Report.1 |
| **钻取** | 单元格 `link` 到另一报表并带 filter | P-Report.2 |
| **主从** | 下行展开子表（另一 resource 或 flow，外键=当前 Id） | P-Report.2 |
| **图表+表** | 简易柱/折，数据 `groups[]`；不是大屏 | P-Report.2 |
| **保存我的视图** | 用户列宽/显隐/默认筛选 | P-Report.2 |
| **订阅** | 定时把当前筛选导出发邮件 | 后置 |
| **打印/套打** | 对标积木报表 | **不做**（见 §13） |

KPI 例：`{ "title": "待审金额", "field": "Amount", "fn": "sum", "filters": <待审 FilterGroup> }`。可与主表筛选 AND，或自带独立范围。

---

## 8. 菜单与权限

对齐 [`iam-rbac-menu.md`](./iam-rbac-menu.md)：Permission 是授权真相，菜单只引用权限名。

### 8.1 发布向导

报表点「发布到菜单」：

1. 选择父级目录（现有 `SysMenu` 树）。  
2. 菜单标题默认报表名，可改；图标、排序。  
3. Path：`/app/reports/{reportCode}`（与资源 `/app/{resourceCode}` 分开，避免抢路由）。  
4. Component：运行时通用页 `AppReportRuntime`（Vben 动态路由）。  
5. 写入权限定义并绑定到菜单 `Permission`。

取消发布：菜单停用或删除（可选），权限定义可保留以免角色授权丢失。

### 8.2 权限树

```
App.Report.{reportCode}                 查看（进页、查询）
App.Report.{reportCode}.Export
App.Report.{reportCode}.Action.{key}    每个按钮
```

从资源生成的报表可额外依赖 `App.{resourceCode}.*`，或发布时勾选「复用资源权限」（查看=资源查看，按钮映射 create/update/delete）。默认 **报表自有权限**，便于「只能看待审报表、不能进完整资源」。

按钮 `acl` 与菜单 Type=`Button` 可选同步，供其它入口复用；侧栏不含 Button 节点。

### 8.3 数据范围

行级仍走 Administration `RoleDataScope` + 查询层注入（CreatorId / Org）。报表数据范围（§4.5）是实施配的固定 WHERE，与角色数据权限 **AND**。

---

## 9. 领域模型与契约

### 9.1 聚合

```
ReportDefinition  (IMultiTenant, 草稿/已发布, schemaVersion)
  code, name, icon, description
  kind: resource | flow
  resourceCode?
  queryFlowKey                 默认 sys.resource.query
  dataScope: FilterGroup       设计器隐藏条件
  searchForm: SearchFormDef    查询表单
  advancedFilter: bool         是否允许「更多条件」
  presets: NamedFilter[]       快捷芯片
  kpis: KpiDef[]
  columns: ReportColumnDef[]
  summaryBar: bool
  actions: ReportActionDef[]
  forms: ReportFormDef[]       按钮弹窗用的表单片段
  selection: { enabled, preserveOnPage }
  pageSize, defaultSorting
  menu: { bind, parentId, title, icon }  发布快照
```

版本：与资源相同，运行时读已发布 JSON；草稿不影响线上菜单。

### 9.2 查询入参（自定义 query 流必须兼容）

```json
{
  "resourceCode": "order",
  "reportCode": "order-pending",
  "page": 1,
  "pageSize": 20,
  "sorting": "CreationTime desc",
  "filter": { "op": "and", "children": [ ] },
  "columns": ["Id", "OrderNo", "Amount", "Status"],
  "summaryFields": [
    { "field": "Amount", "fn": "sum" }
  ]
}
```

`filter` 为 **已经 AND 好的用户层树**（表单 + 更多条件 + 芯片）。ResourceQuery 再 AND 系统隐式与 `dataScope`。`columns` / `resourceCode` 仍由运行时注入，用户不可改。

出参：`{ items, total, summary? }`。

### 9.3 从资源一键生成

应用到库 → 一键创建资源 → 「生成报表」：

- 查询表单：取 FilterDef 里 `exposed` 字段，生成对应控件（字符串 contains、枚举 select、日期 dateRange）。  
- 列：复制 ListView + 按类型默认 format。  
- 按钮：复制 ActionDef 并升格 `scene/scope`。  
- `queryFlowKey` = 资源的 queryFlowKey。  
- 不自动发菜单，实施点发布。

---

## 10. 运行时页面结构

```
页头：标题 / 说明 / 收藏
KPI 条（可选）
查询表单 + 快捷芯片 + 查询/重置
工具栏：主按钮 | 批量按钮（有勾选才亮）| 列设置 | 导出
表格：多选 | 列（颜色/Tag/链接）| 行按钮（过多进更多）
表尾：筛选合计行
分页
「更多条件」Drawer
弹窗/抽屉表单（按钮 open）
```

空状态：无数据时展示 `empty` 场景按钮（如新增）。

KeepAlive：与现资源运行时相同；报表码为路由参数。

---

## 11. 安全与方言

- 标识符/字段白名单；树递归生成参数化谓词；禁止拼接用户 combine 文本。  
- 空日期/空串规则与现 ResourceQuery 一致。  
- 聚合函数按列类型与引擎白名单（SUM 仅数值）。  
- COUNT / SUM 类型仍走 `JsonNodeNumbers`（Oracle decimal、SQL Server int 等）。  
- 导出异步有上限；按钮 batchLimit。  
- 列 `tone` / render 组件白名单，无原始 HTML。  
- 查询 `traceMode=errors`；按钮写操作 `always`。

---

## 12. 分期

| 阶段 | 范围 |
|------|------|
| **R0 规划** | 本文评审；拍板查询 UI 淘汰运行时公式 |
| **R1 查询体验 + 报表骨架（已落地）** | `FilterGroup` 后端；查询表单 + 可视化条件组替换资源运行时公式框；ReportDefinition CRUD；列预设 format/tag/dict；filtered 合计；基础按钮（弹窗全表单 / 行 / 批量删除）；发布菜单 |
| **R2 报表增强** | 单列白名单弹窗、cell/column scope、styleRules、KPI、导出、快捷芯片、从资源生成向导 |
| **R3** | 分组小计、钻取、主从、图表、自定义列 JS 沙箱、batchUpdate 系统流 |
| **后置** | 订阅推送、交叉表、打印套打、独立 BI |

R1 须先改 **现有资源列表筛选**，否则报表与资源两套交互。编排 IF 节点不强迫改。

---

## 13. 明确不做什么

- 不在报表运行时出现 `1 and (2 or 3)` 输入框。  
- 不引入 JimuReport / UReport / Excel 画布、套打、无限行列。  
- 不引入 Amis/NocoBase 全量前端运行时。  
- 不做即席 SQL 编辑器给业务员（那是编排 Code/`db.query`）。  
- 不把人审画进报表按钮（按钮可 `StartWorkflow`，引擎仍是能力 B）。  
- 不在单元格内执行任意 JS 写库。

---

## 14. 评审清单

1. 运行时筛选是否同意 **查询表单为主、条件组为辅**，公式仅留在编排？  
2. 「任一满足」是否只出现在：表单里的或组卡片、更多条件的组头切换？  
3. 合计默认 `filtered`（当前筛选全量）是否可接受多一次聚合 SQL？  
4. 报表权限独立 vs 复用资源权限，默认选哪个？  
5. R1 是否同步改掉现 `runtime.vue` 的 combine 输入框？

---

## 修订记录

| 日期 | 说明 |
|------|------|
| 2026-09-21 | 落地 R1：FilterGroup、查询表单/条件组、ReportDefinition、列着色转换汇总、基础按钮、菜单 |
| 2026-09-21 | 初稿：通用报表 + 查询表单/可视化条件组（取代编号公式）+ 列着色转换汇总 + 按钮作用域 + 菜单发布 |
