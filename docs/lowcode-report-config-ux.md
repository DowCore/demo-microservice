# 报表配置器 UX 重设计（R1.5）

> **文档性质**：**前端交互规格**（配置器怎么用、怎么摆）。本文**不是后端契约**——只描述交互与信息架构，凡涉及后端能力的断言都附 `相对路径:行号` 作为「可实现性证据」，其余一律是前端规格。
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）：Release 构建 0 错误、单测 62/62 通过。
> **核对方式**：后端结论附 `相对路径:行号`；前端结论标 ⚠️ 未核验（前端在 `D:\Project\vue-demo`，不在本仓）。
> **图例**：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划） ｜ 🐞 已知缺陷
> **后端契约以 [`lowcode-report-management.md`](./lowcode-report-management.md) 为准**，本文不重复、不扩写。

---

## 1. 定位与边界

### 1.1 这篇管什么

- 报表配置页的**交互与信息架构**：区块怎么拆、查询条件怎么按类型给范围、字典/着色怎么让人看懂、按钮放哪、表单设计器怎么摆。
- 只保留**能落到交互上**的内容，并标注它依赖的后端事实是否真的存在。

### 1.2 不管什么

- **不是后端契约**：不定义 API、不定义领域模型、不承诺服务端行为。存储与查询契约见 [`lowcode-report-management.md`](./lowcode-report-management.md)。
- 不写运行时页面的完整设计（另见 `lowcode-report-management.md` §10）。
- 不覆盖表单设计器本身的编辑能力（见 [`lowcode-form-preview.md`](./lowcode-form-preview.md) §6）。

### 1.3 一个必须先划清的界线

配置器里出现的很多东西（列格式化、着色、render、KPI、预设、按钮动作）在**领域模型里存在并且会被持久化**，但本仓的**服务端不执行它们**——执行发生在报表运行时页面（前端）。因此本文这些部分写的是**前端运行时规格**，而不是「后端已支持」。唯一真正由后端执行的是：

- 条件树 → 参数化 SQL 的编译（§3.1）；
- 查询编排走 Query Flow（§3.2）；
- 汇总函数的服务端计算（§3.3）。

---

## 2. 现状能力总表（配置器相关）

前缀简写展开见 §3 开头。前端行一律 ⚠️ 未核验。

| 配置器区块 | 后端是否支撑 | 状态 | 证据（`相对路径:行号`） | 备注 |
|------------|--------------|------|--------------------------|------|
| 查询条件 → `eq`/`ne`/`gt`/`gte`/`lt`/`lte`/`contains`/`notcontains`/`startswith`/`between`/`in`/`notin` | 是（编成参数化 SQL） | ✅ | `APP/FilterSqlBuilder.cs:332-396` | 见 §3.1 |
| 条件组 AND/OR 树 | 是 | ✅ | `APP/FilterSqlBuilder.cs:214-287` | 嵌套深度上限 3：`SHARED/OrchestrationConsts.cs:14` |
| 「空着不算」（空值条件跳过） | 是 | ✅ | `APP/FilterSqlBuilder.cs:243-247` | 与原文 §2「空着不算」一致 |
| 「任一满足」分组（`SearchFormFieldDef.OrGroup`） | 仅模型，无消费点 | 🐞 | `SHARED/ResourceSchema.cs:53`（全仓仅此 1 处声明） | 实际 OR 能力在条件树 `op:or` 或 `combine` 表达式（§3.1） |
| 「更多条件 / 高级筛选」（`AdvancedFilter`） | 仅持久化，无消费点 | 🐞 | `DOM/ReportDefinition.cs:35`、`APP/ReportDefinitionAppService.cs:423` | 只持久化 + 回显 |
| 报表查询执行（`{code}/query`） | 是 | ✅ | `API/AppBuilderControllers.cs:219`、`APP/ReportDefinitionAppService.cs:225-312` | 经 Query Flow 调用，见 §3.2 |
| 报表分页 `pageSize` | 是（**上限 200**） | ⚠️ | `APP/ReportDefinitionAppService.cs:271`、`SHARED/OrchestrationConsts.cs:13` | 配置实体侧也钳到 1–200：`DOM/ReportDefinition.cs:127`；超过 200 会被静默截断 → 配置页应提示 |
| 列汇总 `summaryFn` | 是 | ✅ | `APP/ResourceCrudExecutor.cs:249-263,931-965`、`APP/ReportDefinitionAppService.cs:294-299` | 唯一被服务端消费的「列管道」环节 |
| 列字典 `dictCode` / `dictionaries` | 仅模型与持久化 | ❌ | `DOM/ReportDefinition.cs:47`、`SHARED/ResourceSchema.cs:188,284` | 服务端不解析；映射在运行时页（前端） |
| 列值映射 `dictMap` / `render` / `styleRules` | 仅模型与持久化 | ❌ | `SHARED/ResourceSchema.cs:183,181,196`；`DOM/ReportDefinition.cs:261-275` | 服务端零消费点（grep 实测） |
| 格式化 `formatFn` / `formatPreset` | 仅模型与持久化 | ❌ | `SHARED/ResourceSchema.cs:191,170`；`SHARED/ResourceSchema.cs:203-224`（仅 `ApplyLayoutDefaults` 用 preset 推对齐/列宽/省略） | 单元格格式化在运行时页（前端） |
| 渲染 `renderFn` | 仅模型与持久化 | ❌ | `SHARED/ResourceSchema.cs:194` | 服务端零消费点 |
| 按钮 `ActionDef`（`formKey`/`formMode`/`open`/`position`/`batchLimit`） | 仅模型与持久化 | ❌ | `SHARED/ResourceSchema.cs:227-272`、`DOM/ReportDefinition.cs:43`、`APP/ReportDefinitionAppService.cs:427` | 服务端**无动作执行端**；仅删除保护会读 `formKey`（`APP/FormDefinitionAppService.cs:144,150`） |
| 报表绑定 `Form`（同一份 `FormDef`） | 仅模型与持久化 | ❌ | `DOM/ReportDefinition.cs:45`、`APP/ReportDefinitionAppService.cs:428` | 渲染在运行时页（前端） |
| KPI `kpis` / 预设 `presets` | 仅模型与持久化 | ❌ | `DOM/ReportDefinition.cs:37,39`、`APP/ReportDefinitionAppService.cs:424-425` | 服务端零消费点 |
| 配置页 Tab / 抽屉 / 按类型给范围 / 行内右侧固定 / 新标签页表单页 | 前端 | ⚠️ 未核验 | 前端 `D:\Project\vue-demo` | 本文 §6 是规格 |

---

## 3. 契约与实现细节（只写与配置器相关的后端事实）

> 本文 `file:line` 均相对仓库根 `D:\Project\demo-microservice`。简写展开：
> - `API/` = `src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/`
> - `APP/` = `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/`
> - `DOM/` = `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/`
> - `SHARED/` = `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/`

### 3.1 查询条件最终编成什么 SQL（决定 §6.2 的映射能不能兑现）

`APP/FilterSqlBuilder.cs`：

- **支持的运算符**（`:332-396`）：`eq` `ne` `gt` `gte` `lt` `lte` `contains` `notcontains` `startswith` `between` `in` `notin`；其余抛 `Unsupported filter operator`（`:398`）。
- **全参数化**：值进 `args`，SQL 里只出现参数名（如 `:336` `col = @pa`），无字符串拼接值。
- **类型兜底**：若列不是 `String`/`Text`/`Enum`，`contains`/`notcontains`/`startswith` 会被**静默改写为 `eq`**（`:322-327`）。→ 配置页给非文本列提供「模糊」选项会造成「选了模糊实际是精确」的错觉，应在 UI 层按列类型屏蔽。
- **`between` 是半开区间**：`col >= @pa AND col < @pb`（`:363-375`）。→ 日期范围配置的右端**不包含**当日/当秒；配置页文案与默认值需与此一致（例如日期右端取次日 00:00），否则用户会漏掉边界数据。
- **空值跳过**：`right` 为空（且 op 非 `isempty`/`isnotempty`）时该条件**不产生 SQL**（`:243-247`）。与原文「空着不算」一致。
- **AND/OR 树**：`FilterNode`（`kind=group`，`op=and|or`，`children`）递归编译（`:214-287`），嵌套超过 `MaxFilterGroupDepth=3` 抛错（`:228-233`）。
- **字符串表达式仍然被服务端接受**：`combine` 支持 `1 or 2 and 3` 形式的表达式并做括号/优先级解析（`:432-449`、`TokenRegex :26-29`）。原文 §6「不在查询区手写」是**UX 决定**（不暴露给用户），不是服务端不支持。

### 3.2 报表查询的实际执行路径

- 路由：`POST /api/orchestration/reports/{code}/query`（`API/AppBuilderControllers.cs:219`）。
- `QueryAsync`（`APP/ReportDefinitionAppService.cs:225-312`）要求报表已发布（`:229-232`），随后把查询体（`page`/`pageSize`/`sorting`/`columns`/`filter`/`summaryFields`）序列化后交给 **Query Flow**：`_invoker.InvokeAsync(entity.QueryFlowKey, ...)`（`:305-312`）。
- `QueryFlowKey` 默认 `SystemResourceFlowKeys.Query`（`DOM/ReportDefinition.cs:27`），可通过 `SetQueryFlowKey` 覆盖（`:188-190`）。
- `DataScope` 与用户条件由 `CombineFilters` 合并（`:283-287`）。
- 因此「配置器配的东西如何生效」最终取决于 Flow 编排，而不是配置页直接拼 SQL；配置页只需要保证**产出的结构**（`FilterNode`/`columns`/`summaryFields`）是服务端认得的形状。

### 3.3 列管道在后端到底走到哪一步

- **被消费**：汇总。`QuerySummaryAsync`（`APP/ResourceCrudExecutor.cs:249-263`、`:852-929`）解析 `summaryFields` 与列上的 `SummaryFn`（`:931-965`）。
- **不被消费**（只在模型与 DTO 间搬运、持久化）：`DictCode`、`DictMap`、`Options`、`Render`、`RenderFn`、`FormatFn`、`StyleRules`、`FormatPreset`（除 `ApplyLayoutDefaults` `SHARED/ResourceSchema.cs:203-224` 用它推 `Align`/`Width`/`Ellipsis`）、`SummaryScope`。
- 搬运路径示例：`DOM/ReportDefinition.cs:257-275`（从资源列复制到报表列）、`APP/ReportDefinitionAppService.cs:428-431`（实体 → DTO）。
- 结论：原文 §3 的「管道：原值 → 字典 → 格式 → renderFn → 条件着色」是**运行时页面（前端）规格**，后端只负责把它存下来。文档不得让读者以为服务端会执行它。

### 3.4 按钮

- `ActionDef` 有完整字段（`SHARED/ResourceSchema.cs:227-272`：`Key`/`Label`/`Position`/`Scene`/`Scope`/`Kind`/`FlowKey`/`Open`/`FormKey`/`FormMode`/`Fields`/`PayloadFields`/`After`/`Confirm`/`BatchLimit`/`MultiSelect`）。
- 报表实体持有 `Actions`（`DOM/ReportDefinition.cs:43`），更新时接收（`APP/ReportDefinitionAppService.cs:173`），读取时回显（`:427`）。
- 后端**没有动作执行端点**：唯一读取 `FormKey` 的地方是表单删除前的引用保护（`APP/FormDefinitionAppService.cs:144` 报表动作、`:150` 资源动作）。
- 结论：「点击按钮 → 打开表单/跑流程」是前端 + Flow 编排的事；配置页的职责是产出合法 `ActionDef`。

### 3.5 存储过程类数据源的列表代价（影响列表页交互预期）

`APP/ResourceCrudExecutor.cs:268-292`：当 QueryObject 是「不可 `SELECT FROM` 的存储过程」时，走 `QueryExecAsync` —— **结果集全量读入内存后再分页**（`:283-286`）。因此这类报表的列表页在数据量大时首屏慢、且分页不减少数据库开销。配置器在选择存储过程数据源时应给出提示。

---

## 4. 行为边界与已知缺陷

| # | 问题 | 影响配置器的点 | 证据 |
|---|------|----------------|------|
| 4.1 | **`OrGroup`「任一满足」无服务端消费** | 配置页若让用户「勾选任一满足 + 选组名」，后端不会据此拼 OR；实际 OR 必须落到条件树 `op:or` 或 `combine` 表达式 | `SHARED/ResourceSchema.cs:53`（仅声明）；能力在 `APP/FilterSqlBuilder.cs:252-287` |
| 4.2 | **`AdvancedFilter` 无服务端消费** | 「更多条件」开关只是持久化标记，不改变查询行为 | `DOM/ReportDefinition.cs:35`、`APP/ReportDefinitionAppService.cs:423` |
| 4.3 | **`pageSize` 静默钳到 200** | 配置页填 500 实际按 200 生效，用户无感知 | `APP/ReportDefinitionAppService.cs:271`、`SHARED/OrchestrationConsts.cs:13`、`DOM/ReportDefinition.cs:127` |
| 4.4 | **非文本列「模糊」被静默改写为精确** | 配置页给数字/日期列提供模糊选项会产生「看起来生效、其实变了语义」 | `APP/FilterSqlBuilder.cs:322-327` |
| 4.5 | **`between` 右端开区间** | 日期范围的「结束日期」不包含当天 | `APP/FilterSqlBuilder.cs:363-375` |
| 4.6 | **条件树嵌套上限 3** | 深层嵌套条件在运行时抛 `UserFriendlyException`；配置页应提前拦住 | `APP/FilterSqlBuilder.cs:228-233`、`SHARED/OrchestrationConsts.cs:14` |
| 4.7 | **存储过程数据源全量载入内存再分页** | 列表页性能与「分页」直觉不符 | `APP/ResourceCrudExecutor.cs:283-286` |
| 4.8 | **`kpis`/`presets` 无任何消费者** | 配置了也不会有服务端行为 | `DOM/ReportDefinition.cs:37,39`、`APP/ReportDefinitionAppService.cs:424-425` |

以上 4.1–4.8 均**不影响**配置器把配置**存下来**，但都影响「配了到底有没有用」的预期，需在 UI 上给出提示或按实际能力收紧选项。

---

## 5. 规划（未实现）

| 项 | 说明 | 状态 |
|----|------|------|
| 让 `OrGroup` 或「任一满足」有服务端语义 | 二选一：要么消费 `OrGroup`，要么在配置页直接产出 `FilterNode op:or` | ❌ |
| 「更多条件」开关真正参与查询 | 目前 `AdvancedFilter` 无消费点 | ❌ |
| `kpis` / `presets` 的服务端或运行时消费 | 目前只持久化 | ❌ |
| 按列类型屏蔽非法查询方式（模糊/范围） | 消除 §4.4 的静默改写 | ❌ |
| `pageSize` 超限的显式校验与提示 | 替代静默钳断 | ❌ |
| 服务端执行 `formatFn` / `renderFn` / `styleRules` | 当前定位是前端运行时；是否上移需另行评审 | ❌ |

---

## 6. 交互规格（⚠️ 全部未核验：前端在 `vue-demo`）

> 本节是**前端规格**，不是现状结论。§1–§6 为原文保留并修正后的内容。

### 6.1 配置页不再摊成一行

针对当前「一行填空」配置页：日期范围配不出来、字典/着色看不懂、按钮打不开独立页。
配置器学 **NocoBase Filter Form、Amis mapping、Jeecg 列表+表单页、Ant Design 右侧固定操作列**，不引入整套画布。

| 区块 | 现在（low，⚠️ 未核验） | 改为 |
|------|------------------------|------|
| 查询 | 标题×字段×控件×运算符×任一满足组名 | **先选字段**，按类型给出「模糊 / 精确 / 日期范围 / 时间范围 / 数值范围」 |
| 列 | 字典 `Approved:已审`、着色值一个框 | 点「展示」打开抽屉：**值映射表**（码 → 文案 → 颜色）+ 金额类条件着色 |
| 按钮 | 弹窗/抽屉/逗号字段 | 位置 + 打开方式（含**新标签页**）+ 绑定哪张表单 |
| 表单 | 没有 | **表单设计器**：字段布局，供标签页/抽屉共用 |

报表配置用 Tab：查询 · 列 · 按钮 · 表单 · 基本信息。

### 6.2 查询条件：范围怎么加

选字段后 **查询方式跟类型走**，不要同时填控件和运算符。

| 字段类型 | 默认查询方式 | 运行时控件 | 编成 Filter（后端支持，见 §3.1） |
|----------|--------------|------------|----------------------------------|
| 文本 | 模糊 | 输入框 | `contains` ✅ |
| 状态/枚举 | 精确（可改多选） | 下拉 | `eq` / `in` ✅ |
| **日期** | **日期范围** | RangePicker | `between` ✅（⚠️ 右端**开区间**，见 §4.5） |
| **日期时间** | **时间范围** | RangePicker + 时分秒 | `between` ✅（同上） |
| 数字/金额 | 数值范围 | 最小 ~ 最大 | `between` ✅（⚠️ 数字列不要给「模糊」，会被静默改成 `eq`，见 §4.4） |
| 是否 | 是/否 | Switch | `eq`（未拨动不参与）✅（空值跳过，`:243-247`） |

「添加字段」弹出列勾选，已选过的禁用。点「订单日期」即得到日期范围，不必再把「文本/等于」改成「日期区间/介于」。

任一满足：勾选「与下列字段任一满足」并选组名（客户名 **或** 手机号），不要让用户填「组名」字符串。
⚠️ **后端现状**：`SearchFormFieldDef.OrGroup`（`SHARED/ResourceSchema.cs:53`）**没有消费点**。要让「任一满足」真正生效，配置器必须把该分组落成条件树里的 `FilterNode{kind:"group", op:"or"}`（后端支持，`APP/FilterSqlBuilder.cs:252-287`），而不是只写 `OrGroup`。

运行时仍：空着不算；已填默认全部满足；复杂逻辑用「更多条件」。
⚠️ 「更多条件」对应的 `AdvancedFilter` 目前**只持久化、无服务端消费**（§4.2）；条件树本身（`FilterNode`）是有服务端支持的，深度上限 3。

### 6.3 「字典」和「着色」分别是什么

**值映射（字典）**：库里存码，列表显示给人看的字，并可带颜色。

优先建 **通用字典**（报表/资源上的 `dictionaries`），列只引用 `dictCode`。本列自定义仍可用，并可「存为通用字典」。
⚠️ 模型齐备（`DOM/ReportDefinition.cs:47`、`SHARED/ResourceSchema.cs:188,284`），但**服务端不解析**，映射在运行时页执行。

| 存储值 | 显示 | 颜色 |
|--------|------|------|
| `Approved` | 已审核 | 成功绿 |
| `Draft` | 草稿 | 默认 |

**条件着色**：给金额、天数等连续值。「金额 > 10000 → 危险红」。枚举列应优先用字典颜色。
模型：`ColumnStyleRule`（`SHARED/ResourceSchema.cs:146-155`）、`ListColumnDef.StyleRules`（`:196`）——persisted only（§3.3）。

**格式化函数 `formatFn`**：JS，参数 `value`（映射后文案）、`row`（当前行）、`raw`（原值），返回展示字符串。
模型：`ListColumnDef.FormatFn`（`SHARED/ResourceSchema.cs:191`）——persisted only。

**Render `renderFn`**：JS，同样三个参数，返回 HTML（脚本标签会被去掉）。有 HTML 时不再走 Tag/纯文本。
模型：`ListColumnDef.RenderFn`（`:194`）——persisted only。

管道：原值 → 字典（文案+色）→ 预设格式或 formatFn → renderFn（可选 HTML）→ 条件着色覆盖。
⚠️ **这是运行时页面（前端）的规格**；后端只有 `summaryFn` 与 `ApplyLayoutDefaults` 生效（§3.3）。

### 6.4 按钮：行内右侧固定 + 新标签页

| 位置 | 表现 |
|------|------|
| 工具栏 | 查询区旁边（新增、导出） |
| **行内** | 表格 **最右固定列**（`fixed: 'right'`），详情/编辑/删除；多了进「更多」 |
| 批量 | 勾选后亮起 |

| 打开方式 | 用途 |
|----------|------|
| 新标签页 | **列表默认**：详情/编辑/新增打开独立页，可后接固定详情配置 |
| 抽屉 / 弹窗 | 短表单、单字段改值 |
| 不打开 | 删除等直接执行 |

路由（KeepAlive 标签）——⚠️ 未核验（前端路由）：

- 列表 `/app/reports/{code}`
- 表单 `/app/reports/{code}/form/{create|update|detail}/{id?}`

按钮绑定 `formKey`（新增/编辑/详情），页面用报表上的 **同一份 FormDef**。
⚠️ 模型支持：`ActionDef.FormKey`/`FormMode`（`SHARED/ResourceSchema.cs:247-249`）、`ReportDefinition.Form`（`DOM/ReportDefinition.cs:45`）；服务端**无动作执行端**（§3.4），打开行为在前端。

### 6.5 表单设计器（本期）

不是 Excel 画布。左右栏：

- 左：从列勾选字段，上移/下移，点开改控件、栅格、新增/编辑/详情可见与必填。
- 右：按真实表单预览。

详情页只读；编辑/新增按可见性过滤。后续再接分区、主从、独立 Form 资源。
字段属性与 `FormFieldDef` 一致（见 [`lowcode-form-preview.md`](./lowcode-form-preview.md) §3.4）；「按真实表单预览」的实现路径与交互细则见同文 §6。

### 6.6 明确不做

不在查询区手写 `1 or 2 and 3`；不引入 Amis/NocoBase 全量运行时。列上的 formatFn / renderFn 仅由配置者填写，运行时在单元格内执行（前端）。

> 注：「不在查询区手写 `1 or 2 and 3`」是 UX 决定——服务端**仍然接受**该表达式语法（`APP/FilterSqlBuilder.cs:432-449`），只是不向用户暴露。

---

## 7. 参考链接

| 类型 | 链接 |
|------|------|
| 后端契约与规划（R1/R2/R3） | [`lowcode-report-management.md`](./lowcode-report-management.md) |
| 表单库预览能力方案 | [`lowcode-form-preview.md`](./lowcode-form-preview.md) |
| 表单库 / 资源 CRUD 规划 | [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md) |
| NocoBase | https://github.com/nocobase/nocobase |
| Amis | https://github.com/baidu/amis |
| Ant Design 列表页 | https://ant.design/docs/spec/research-list-cn |

---

## 8. 修订记录

| 日期 | 修订 | 说明 |
|------|------|------|
| 2026-10-13 | 定位校正 + 事实核对（基线 `267d375`） | 明确本文是**前端交互规格**而非后端契约（§1），并指向 `lowcode-report-management.md`；新增 §2 能力总表、§3 后端可实现性证据、§4 缺陷清单、§5 规划；原文 §1–§6 内容全部保留为 §6，仅修正与后端实际能力矛盾之处（`OrGroup` 无消费点、`AdvancedFilter` 无消费点、`between` 半开区间、非文本列 contains 被改写为 eq、`pageSize` 上限 200、列管道仅前端执行）；前端内容统一标 ⚠️ 未核验 |
