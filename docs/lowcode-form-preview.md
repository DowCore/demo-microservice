# 表单库预览能力方案（Form Preview）

> **文档性质（混合，已拆开）**：§2/§3/§4 是**后端事实契约**（逐条核到本仓代码）；§6 是**前端交互规格**（实现在前端仓 `D:\Project\vue-demo`，本仓无法核验）；§5 是**规划**（未实现）。
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）：Release 构建 0 错误、单测 62/62 通过。
> **核对方式**：后端结论均附 `相对路径:行号`（前缀简写见 §3 开头）；前端结论一律标 ⚠️ 未核验。
> **图例**：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划） ｜ 🐞 已知缺陷

**一句话**：学 FcDesigner / VariantForm（VForm3）——**设计器工具栏「预览」弹窗 + 列表行「预览」**；用现有 `RecordFormFields` 同源渲染，按 create / update / detail 试填与只读，预览数据不写回 Schema。**这套能力目前全部属于前端；本仓没有任何表单预览端点或预览管线。**

---

## 1. 定位与边界

### 1.1 这篇管什么

- 定义「表单库预览」的目标体验与交互细则（前端交互规格）。
- 把表单库后端**真实已实现**的部分写实，作为预览方案的前置事实。
- 保留对成熟产品的对标结论与信息架构论证，作为设计依据。

### 1.2 不管什么

- 不是后端契约文档：本仓不为「预览」设计 API（§5 只列规划）。
- 不定义前端组件实现细节；`vue-demo` 的具体代码不在本仓，本文凡涉及前端之处均为**规格**而非**核验结论**。
- 不覆盖表单设计器本身的编辑能力（见 [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md)）。

### 1.3 关键判定：本仓无后端预览管线（必须先读）

**判定：预览能力全部在前端 `vue-demo`，本仓库不存在表单预览端点，也不存在「渲染表单 Schema」的服务端能力。**

核对证据（全仓大小写不敏感 `preview` 搜索，`src/services/saas/src/` 仅 2 组命中，均与表单无关）：

| 命中 | 位置 | 实际含义 | 与表单预览的关系 |
|------|------|----------|------------------|
| DDL 预览 | `API/AppBuilderControllers.cs:40` `[Route("{id}/preview")]` → `APP/TableDefinitionAppService.cs:122` → `SqlDdlExecutor.Preview` | 表定义**建表/改表 SQL 语句预览** | ❌ 无关 |
| 日期表达式预览 | `API/LogicApiController.cs:75` `system-parameters/preview-date` → `APP/LogicApiAppService.cs:137` | 系统参数**日期表达式**求值预览 | ❌ 无关 |

表单库控制器 `FormDefinitionController`（`API/AppBuilderControllers.cs:226-277`）暴露的全部路由为：列表、`published-lookup`、`{id}`、`by-code/{code}`、`published/{code}`、创建、更新、删除、`{id}/publish`、`{id}/resync-catalog`——**没有任何 preview 路由**。

因此原文中读起来像「后端已实现/已有预览管线」的表述，在本版中一律降级为**前端交互规格或规划**。原文「后端首期可不新增 API；复用已有 Get / GetPublishedByCode」是**正确**的，予以保留（见 §6.6）。

### 1.4 对标与信息架构（保留原文论证）

#### 总览对照

| 产品 / 开源 | 地址 | 预览怎么做 | 对本仓库要学的点 | 不宜照搬 |
|-------------|------|------------|------------------|----------|
| **FcDesigner / form-create Pro** | [预览文档](https://pro.form-create.com/doc/help/feature/preview)、[开源预览](https://view.form-create.com/book/feature/preview-form) | 工具栏 **预览** → **弹窗**；表单模式试填、**阅读模式**只读；PC/移动端宽度；可选出码/SQL | **弹窗预览**、表单/阅读双态、预览数据不回写画布、设备切换 | 生成 SFC/HTML/SQL 首期不做 |
| **VariantForm / VForm3** | [vform3](https://www.vform666.com/vform3/)、`previewForm()` | 设计器默认带 **预览表单按钮**；弹窗内用 **Render** 渲染同一份 JSON；业务侧 `setFormData` + `disableForm` 做详情 | **设计器 / 渲染器分离**：设计态与预览态同一 Schema、不同壳 | 不强绑 Element Plus 整套 VForm |
| **Formily Designable** | [alibaba/formily](https://github.com/alibaba/formily) | 视口 `ViewPanel type="PREVIEW"`，与 DESIGNABLE / JSONTREE 并列 | **设计态 / 预览态切换**心智清晰 | React Designable 不迁入 |
| **form-js（bpmn-io）** | [bpmn-io/form-js](https://github.com/bpmn-io/form-js) | Editor + Viewer；Viewer 纯渲染 | 轻量 Viewer 组件思路 | 字段集偏流程表单 |
| **Ant Design 规范** | [表单页](https://ant.design/docs/spec/research-form-cn)、[详情页](https://ant.design/docs/spec/detail-page-cn) | 填写态 vs 详情只读平铺 | create/update 高效填写；detail **阅读态**排版 | — |
| **JeecgBoot Online** | Jeecg 表单设计 | 设计后「功能测试 / 预览」进可用页 | 列表也可进预览/测试 | Online 代码生成链路过重 |
| **NocoBase** | [nocobase](https://github.com/nocobase/nocobase) | 配置态 / 使用态一键切换 | 「配置完立刻像终端用户一样用」 | 整平台块体系 |

#### FcDesigner 预览信息架构（主对标）

```
[设计器工具栏] ──点「预览」──► [预览弹窗]
                                  ├─ Tab：表单模式（可试填、可点提交看校验）
                                  ├─ Tab：阅读模式（只读详情观感）
                                  ├─ 切换：电脑端 / 移动端（仅改预览区宽度）
                                  └─ （可选）生成代码 / SQL ── 本仓库 P2+
```

要点（官方文档共识）：

1. 预览改的是**弹窗内临时数据**，不写回字段配置。
2. 关弹窗回到设计。
3. 电脑端/移动端只影响预览区宽度，与设计器工作区尺寸不是同一概念。

#### VForm3 设计器 / 渲染器分离（实现模型）

| 组件角色 | VForm3 | Meta.Dow 对应（前端） |
|----------|--------|------------------------|
| 设计器 | `VFormDesigner` + `previewForm()` | `FormLayoutDesigner` + 顶栏「预览」 |
| 渲染器 | `VFormRender`（`setFormData` / `disableForm`） | **`RecordFormFields`**（`mode` + `modelValue`，无 `designer`） |
| Schema | formJson | `FormDefinition.schema`（`FormDef`，见 §3.4） |
| 业务打开 | 弹窗/抽屉 + Render | 列表「预览」、报表/OA `formRef` 运行时 |

核心原则：**预览必须走渲染器路径，禁止在预览壳里打开设计器属性/选中/Sortable。**

#### Formily 视口模式（次对标）

| ViewPanel | 含义 | 我们是否采用 |
|-----------|------|--------------|
| DESIGNABLE | 拖拽设计 | 已有画布 |
| PREVIEW | 纯运行预览 | **采用**（弹窗或全屏壳） |
| JSONTREE | Schema 编辑 | P2 可选「查看 JSON」 |

首期不做画布内整页切 PREVIEW（易与「选中编辑」打架）；**优先弹窗预览**，与 FcDesigner/VForm 一致。

---

## 2. 现状能力总表（唯一权威表）

前缀简写展开见 §3 开头。前端行一律 ⚠️ 未核验。

| 能力 | 状态 | 证据（`相对路径:行号` 或「前端」） | 备注 |
|------|------|-----------------------------------|------|
| 表单库 CRUD 端点（10 条路由） | ✅ | `API/AppBuilderControllers.cs:226-277` | 无 preview 路由 |
| 按 code 取草稿（设计器编辑态） | ✅ | `APP/FormDefinitionAppService.cs:63` | 返回 `Schema`（草稿） |
| 按 code 取**已发布快照** | ⚠️ | `APP/FormDefinitionAppService.cs:67-76` | 要求 `Status=Published`；`PublishedSchema=null` 时回落可变草稿（§4.2） |
| 发布时写**深拷贝不可变快照** + 版本号自增 | ✅ | `DOM/FormDefinition.cs:81-86`、`APP/FormDefinitionAppService.cs:175`（`CloneForm`） | 快照非共享引用，改草稿不污染快照 |
| `SchemaVersion` / `PublishedSchema` / `GetEffectiveSchema()` | ✅ | `DOM/FormDefinition.cs:25,37,89` | `GetEffectiveSchema() => PublishedSchema ?? Schema` |
| 用版本号**回放历史 Schema** | ❌ | `DOM/WorkflowInstance.cs:33,66`、`CONTRACTS/AppBuilderDtos.cs:774` | 只存 `int`，无版本化 Schema 存储 |
| Layout→Fields **覆盖式**拍平 | ⚠️ | `APP/FormDefinitionAppService.cs:213-228` | 被 `if (extracted.Count > 0)`（`:224`）门控 → 幽灵字段（§4.1） |
| 整树提取字段（含容器嵌套） | ✅ | `APP/FormDefinitionAppService.cs:235-262` | `kind=field` 或明细容器才产出字段 |
| 明细容器子列聚合为 `Children` | ✅ | `APP/FormDefinitionAppService.cs:240-250,264-273` | `data-table`/`subform`/`list`/`tree`；子列不再扁平到顶层 |
| 发布期字段名唯一性校验 | ✅ | `APP/FormDefinitionAppService.cs:299-325` | 大小写不敏感 |
| 发布期与绑定资源的列对齐校验 | ✅ | `APP/FormDefinitionAppService.cs:328-358`（`:350`） | **唯一**读取 `ColumnName` 的地方 |
| 深拷贝克隆 `CloneForm`/`CloneWidget`/`CloneField`/`CloneRule`/`CloneDependencies` | ✅ | `APP/FormDefinitionAppService.cs:416-503` | 单测覆盖：`tests/Meta.Dow.Orchestration.Tests/FormSchemaFlatteningTests.cs:117-137` |
| `FormFieldDef`/`FormWidgetDef` 控件协议（Rules/Dependencies/ControlProps/MinItems/MaxItems/TreeChildrenField） | ✅ | `SHARED/ResourceSchema.cs:317-375`、`:381-442` | 模型齐备，前端消费 |
| `FormFieldDef.ColumnName` **运行时**按列名落库/读取 | ❌ | `APP/ResourceCrudExecutor.cs` 全文 0 处引用 `FormFieldDef`/`ColumnName`/`resource.Form` | 见 §4.3 |
| 工作流发布按**已发布快照**校验条件字段 | ⚠️ | `APP/WorkflowDefinitionAppService.cs:151` 查 `publishedOnly`，但 `:184-190` 用 `form.Schema.Fields`（草稿） | 🐞 见 §4.4 |
| 后端**表单预览端点 / 预览管线** | ❌ | 全仓 `preview` 命中仅 DDL（`API/AppBuilderControllers.cs:40`）与日期表达式（`API/LogicApiController.cs:75`） | 属规划；预览全在前端 |
| 后端样例数据（sample）生成 | ❌ | 全仓 `sample` 命中仅 `Samples/SampleAppService.cs` 示例服务 | 前端能力 |
| 设计器顶栏「预览」入口 | ⚠️ 未核验 | 前端 `D:\Project\vue-demo` | 交互规格见 §6 |
| 表单库列表行「预览」入口 | ⚠️ 未核验 | 前端 | 交互规格见 §6 |
| `FormPreviewModal` / `FormPreviewShell` 组件 | ⚠️ 未核验 | 前端 | 组件拆分见 §6.6 |
| `RecordFormFields` 运行态渲染（`designer=false`） | ⚠️ 未核验 | 前端 | 预览必须走渲染器路径 |
| 设备宽度 / 本地校验 / 样例重置 / 看 JSON | ⚠️ 未核验 | 前端 | 交互规格见 §6.4-6.5 |

---

## 3. 契约与实现细节（后端按代码写实）

> 本文所有 `file:line` 均相对仓库根 `D:\Project\demo-microservice`。为可读性，重复引用时使用以下简写（展开即完整相对路径）：
> - `API/` = `src/services/saas/src/Meta.Dow.SaaS.HttpApi/Orchestration/`
> - `APP/` = `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/`
> - `DOM/` = `src/services/saas/src/Meta.Dow.SaaS.Domain/Orchestration/`
> - `SHARED/` = `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/`
> - `CONTRACTS/` = `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Orchestration/`

### 3.1 表单库后端端点清单（唯一事实）

控制器：`API/AppBuilderControllers.cs:226` `[Route("api/orchestration/forms")]`。

| 方法 | 路由 | 行为 | 证据 |
|------|------|------|------|
| GET | `/api/orchestration/forms` | 分页列表（映射**草稿** Schema） | `:233-235`、`APP/FormDefinitionAppService.cs:34-56` |
| GET | `/forms/published-lookup` | 已发布下拉（`Status=Published`，最多 200 条；`FieldCount` 取有效 Schema） | `:237-241`、`APP/FormDefinitionAppService.cs:520-547` |
| GET | `/forms/{id}` | 按 Id 取**草稿**（`Map` 默认 `published:false`） | `:243-245`、`APP/FormDefinitionAppService.cs:59,515` |
| GET | `/forms/by-code/{code}` | 按 code 取**草稿** | `:247-249`、`APP/FormDefinitionAppService.cs:63` |
| GET | `/forms/published/{code}` | 按 code 取**已发布快照**；未发布抛 `FormNotPublished` | `:251-254`、`APP/FormDefinitionAppService.cs:67-76` |
| POST | `/forms` | 创建（`UpdateDraft` + 首拍平） | `:256-258`、`APP/FormDefinitionAppService.cs:79-109` |
| PUT | `/forms/{id}` | 更新草稿（先 `EnsureFlattenedFields`） | `:260-263`、`APP/FormDefinitionAppService.cs:112-127` |
| DELETE | `/forms/{id}` | 删除（先做审批流/报表动作/资源动作引用完整性检查） | `:265-267`、`APP/FormDefinitionAppService.cs:134-156` |
| POST | `/forms/{id}/publish` | 拍平 → 字段名校验 → 列对齐校验 → 深拷贝快照 + `SchemaVersion++` | `:269-271`、`APP/FormDefinitionAppService.cs:162-178` |
| POST | `/forms/{id}/resync-catalog` | 从绑定资源同步字段库（不覆盖已有 Schema） | `:273-276`、`APP/FormDefinitionAppService.cs:182-206` |

**不含任何预览端点。** 因此 §6 的全部入口（设计器预览、列表预览、独立路由、lookup 旁路）都是**前端行为**，本仓无对应 API。

### 3.2 发布快照与版本（原文 §4.3 的待核项，现已核清）

- `Publish(FormDef snapshot)`：写入 `PublishedSchema`、`SchemaVersion++`、`Status=Published`（`DOM/FormDefinition.cs:81-86`）。
- 传入的是**深拷贝**：`entity.Publish(CloneForm(entity.Schema))`（`APP/FormDefinitionAppService.cs:175`），`CloneForm` 逐字段拷贝 `Fields`/`Layout` 并对子节点递归（`:416-454`）。
- 消费方读取快照：`GetPublishedByCodeAsync` → `Map(entity, published: true)` → `Schema = published ? e.GetEffectiveSchema() : e.Schema`（`APP/FormDefinitionAppService.cs:75,515`）。
- **原文的两种猜测（“发布=改 Status” vs “发布=不可变快照”）答案是后者**：发布确实生成不可变快照，继续编辑草稿不会改变已发布内容。但有两个边界，见 §4.2 / §4.4。

### 3.3 Layout → Fields 拍平契约

- 入口：`UpdateAsync`（`:117`）与 `PublishAsync`（`:165`）都先调 `EnsureFlattenedFields`。
- 规则（`APP/FormDefinitionAppService.cs:213-228`）：
  1. `Layout` 为空/无节点 → **直接返回**，保留既有 `Fields`（兼容无 Layout 的历史表单）。
  2. `Layout` 非空 → 整树提取（`:235-262`）；**仅当提取结果非空**才整体替换 `Fields`（`:224`）。这是缺陷来源，见 §4.1。
- 提取规则：`kind=field` 且有 `Field` → 产出字段；明细容器（`Kind=container` 且 `Container ∈ {data-table, subform, list, tree}` 且有 `Field`）→ 产出一个带 `Children` 的字段（`:240-250`、`IsDetailContainer` `:264-273`）。
- `BuildField` 完整搬运 `Rules`/`MinItems`/`MaxItems`/`TreeChildrenField`/`ColumnName`/`Dependencies`（深拷贝）/`ControlProps`（新字典）（`:275-297`）。
- 单测锁定：`tests/Meta.Dow.Orchestration.Tests/FormSchemaFlatteningTests.cs:14-114`（覆盖写入、收缩、明细子列、规则联动）。

### 3.4 控件协议（`SHARED/ResourceSchema.cs`）

| 类型 | 行 | 关键属性 |
|------|----|----------|
| `FormFieldDef` | `:317-375` | `Field`、`ColumnName:325`、`Control:334`（input/textarea/number/date/datetime/switch/userPicker/deptPicker/upload/radio/checkbox/select/cascader/table/list/tree）、`Span:337`、`VisibleOnCreate/Update/Detail:341-345`、`ReadonlyOnCreate/Update:347-349`、`RequiredOnCreate/Update:351-353`、`Rules:356`、`Dependencies:359`、`ControlProps:362`、`Children:365`、`MinItems:368`、`MaxItems:371`、`TreeChildrenField:374` |
| `FormWidgetDef` | `:381-442` | `Id`、`Kind`（container/field）、`Container`（grid/card/data-table/subform）、`Columns:392`、`Hidden:396`、`Children:398`、`Field:400`、`ColumnName:403`、同套可见/只读/必填、`Rules:426`、`MinItems:429`、`MaxItems:432`、`TreeChildrenField:435`、`Dependencies:438`、`ControlProps:441` |
| `FormDef` | `:444-459` | `Fields:447`、`Layout:450`、`VformJson:455`、`SubmitFlowKey:458` |

`Rules`/`MinItems`/`MaxItems`/`TreeChildrenField`/`Dependencies`/`ControlProps` 在 `FormWidgetDef` 上的存在性有注释明确标注为 F2 补齐（`:425-441`），与「拍平丢配置」问题的修复对应。

### 3.5 `ColumnName` 的真实状态（若称「按 columnName 落库」即为失真）

- 属性存在：`FormFieldDef.ColumnName`（`SHARED/ResourceSchema.cs:325`）、`FormWidgetDef.ColumnName`（`:403`）。
- 被拍平搬运：`APP/FormDefinitionAppService.cs:279,437,460`。
- **唯一消费点**：发布期列对齐校验 `ValidateAgainstResourceAsync`（`:328-358`），取 `actual = ColumnName ?? Field`（`:350`），与资源表单字段 + 列表视图列名求交。
- **运行时无消费点**：`APP/ResourceCrudExecutor.cs` 全文件对 `FormFieldDef`、`ColumnName`、`resource.Form` 的引用数为 **0**（grep 实测）；查询/写入按 `TableColumn` 物理列名直取。
- 结论：`ColumnName` 目前是**发布期契约与校验项**，不是运行时落库映射。

### 3.6 与工作流 / OA 的关系

- 工作流定义通过 `FormRef`（code）引用表单（`DOM/WorkflowDefinition.cs:28`）。
- 发布工作流时：`EnsureFormRefAsync(entity.FormRef, publishedOnly: true)` 只校验**状态**为已发布（`APP/WorkflowDefinitionAppService.cs:151,175-178`），随后 `CollectFormFields(form)` 却读取 `form.Schema.Fields`——**草稿**（`:184-190`）。
- 工作流实例启动时记录 `FormSchemaVersion = form?.SchemaVersion ?? 0`（`APP/WorkflowRuntimeAppService.cs:65-71`），存为 `int`（`DOM/WorkflowInstance.cs:33,66`），并在 DTO 回显（`CONTRACTS/AppBuilderDtos.cs:774`、`APP/WorkflowRuntimeAppService.cs:901`）。

---

## 4. 行为边界与已知缺陷

### 4.1 🐞 幽灵字段：Layout 只剩容器时旧 `Fields` 全部保留

`EnsureFlattenedFields` 已是覆盖式同步，但替换被 `if (extracted.Count > 0)` 门控（`APP/FormDefinitionAppService.cs:224`）。当 `Layout` 非空、却没有任何 `kind=field` 节点也没有带 `Field` 的明细容器（例如只剩 grid/card 容器）时，`extracted` 为空 → 旧 `Fields` **原样保留**。后果：

- 设计器里删掉的字段仍留在 `Fields`，仍可通过 `ValidateFieldNames`，**仍可被发布**（`PublishAsync` 只要求 `Fields.Count > 0`，`:166-169`）。
- 现有单测只覆盖「Layout 里有其它字段、幽灵字段应被移除」的场景（`tests/Meta.Dow.Orchestration.Tests/FormSchemaFlatteningTests.cs:33-50`），**未覆盖「Layout 有容器但抽不出字段」**，所以该缺陷不会被测试拦住。

### 4.2 ⚠️/🐞 `PublishedSchema=null` 时回落可变草稿；无历史版本可回放

- `GetEffectiveSchema() => PublishedSchema ?? Schema`（`DOM/FormDefinition.cs:89`）。对「在 F4 快照机制上线之前就已发布、库里 `PublishedSchema` 为 null」的历史记录，`/forms/published/{code}` 会返回**可继续漂移的草稿**，与注释承诺的「不可变快照」不符。
- 快照只有**一份最新**，没有按版本号存档；实例只记 `int FormSchemaVersion`（§3.6），**没有任何代码按版本号解析旧 Schema**。结论：表单并未真正冻结，**无法回放旧 schema**。

### 4.3 ❌ `ColumnName` 无运行时消费

见 §3.5。预览/运行态若按 `ColumnName` 组数据，会与后端实际行为不一致。

### 4.4 🐞 工作流发布校验用草稿字段，与已发布快照可能不一致

`publishedOnly: true` 只验证状态（`APP/WorkflowDefinitionAppService.cs:175-178`），条件字段集却取自 `form.Schema.Fields`（`:184-190`）。若草稿在发布后新增/删除了字段，「已发布表单 vs 工作流校验字段集」可以互相矛盾：工作流可能通过校验，但运行时消费的 `GetEffectiveSchema()`（快照）里并没有那个字段（或反之）。

### 4.5 前端行为边界（⚠️ 未核验）

预览不落库、不调用真实提交 Flow、`sampleRecord` 不写回 `FormDef` 等，均为**前端规格**（§6.5），本仓无对应实现可核。

---

## 5. 规划（未实现）

> 以下为本仓**尚未存在**的能力。若将来要做服务端预览，需另行评审，不在当前方案承诺范围内。

| 项 | 说明 | 状态 |
|----|------|------|
| 表单 Schema 预览端点 | 例如 `GET /api/orchestration/forms/{id}/preview` 或 `published/{code}/preview`，返回可直接渲染的 Schema（可含样例数据） | ❌ |
| 服务端样例数据生成 | 按 `Control` 类型造占位值 | ❌ |
| 服务端渲染/校验预览 | 复用 `Rules`/`RequiredOn*` 做服务端校验预演 | ❌ |
| 只读分享预览（匿名 token） | 明确不在首期 | ❌ |
| 历史 Schema 版本存档与回放 | 需先解决 §4.2（版本化存储 + 按 `FormSchemaVersion` 解析） | ❌ |
| 修复 §4.1 幽灵字段 | 门控改为「Layout 非空即替换」，或对容器-only 场景显式清空 | ❌ |

---

## 6. 交互规格（可验收的前端细则）

> ⚠️ **本节全部未核验（前端在 `vue-demo`）**。本节是**规格**，不是现状结论；原文中以 `[x]` 标注「已完成」的项在本版一并降级为**待核验**。

### 6.1 两个入口（必须都有）

| 入口 | 位置 | 行为 |
|------|------|------|
| **A. 设计器预览** | 表单设计页顶栏「预览」 | 用**当前未保存也可**的内存 Schema 打开预览弹窗（与 FcDesigner 一致：预览的是当前画布） |
| **B. 列表预览** | 表单库行操作「预览」 | 取该条 `FormDefinition`；草稿或已发布均可。⚠️ 后端语义：`GET /forms/{id}` 与 `by-code/{code}` **只返回草稿**（§3.1）；要拿已发布快照必须用 `GET /forms/published/{code}`，且未发布时抛错 |

可选后续：

| 入口 | 说明 |
|------|------|
| C. 独立路由 | `/orchestration/forms/preview?id=` 或 `?code=`，便于分享给实施同学（P1.5） |
| D. 已发布 lookup 旁路 | OA/报表选 `formRef` 时右侧小预览（P2） |

### 6.2 预览弹窗结构

```
┌─────────────────────────────────────────────────────────┐
│ 预览 · {表单名称} ({code})          [电脑端|移动端] [×] │
├─────────────────────────────────────────────────────────┤
│ [ 填写·新增 ] [ 填写·编辑 ] [ 阅读·详情 ]                │
├─────────────────────────────────────────────────────────┤
│                                                         │
│   ┌─ 内容区宽度：PC ≈ 720 / 移动 ≈ 375 ─────────────┐   │
│   │  RecordFormFields（designer=false）              │   │
│   │  + 底部：重置样例 | 校验并查看 JSON（仅填写态）   │   │
│   └──────────────────────────────────────────────────┘   │
│                                                         │
│  说明：预览不落库、不调用真实提交 Flow                   │
└─────────────────────────────────────────────────────────┘
```

### 6.3 三种模式（对齐 `FormFieldDef` 的 `VisibleOn*` / `RequiredOn*` / `ReadonlyOn*`）

| UI 文案 | 内部 mode | 行为（字段依据见 §3.4） |
|---------|-----------|--------------------------|
| 填写·新增 | `create` | 按 `VisibleOnCreate` / `RequiredOnCreate` / `ReadonlyOnCreate` |
| 填写·编辑 | `update` | 按 update 可见/必填/只读；预填**样例数据**（可编辑） |
| 阅读·详情 | `detail` | 按 `VisibleOnDetail`；全只读（或控件 disabled）；对齐 Ant 详情页 |

明细 `table` / `list` / `tree` 在预览中必须可增删行（填写态）或只读展示（详情态），与运行时一致；行数上下限应能体现 `MinItems` / `MaxItems`（已被拍平，见 §3.3）。

### 6.4 设备宽度

| 档位 | 内容区 max-width | 用途 |
|------|------------------|------|
| 电脑端 | 720px（或 100%） | 默认 |
| 移动端 | 375px 居中 + 边框模拟 | 看 24 栅格（`Span`）换行 |

不改 Schema，只改预览容器 CSS。

### 6.5 样例数据与校验

| 能力 | 规则 |
|------|------|
| 样例数据 | 打开预览时生成空对象 + 明细空数组；可「填充示例」按 `Control` 类型塞假数据（后端无此能力，见 §2） |
| 编辑/详情预填 | 使用同一份 `sampleRecord`；切换 mode 不清空（除非点重置） |
| 校验 | 前端按 `RequiredOn*` + `Rules` 本地校验；成功则 `message` + 展示 JSON；**不调** create/update Flow |
| 隔离 | `sampleRecord` 仅存预览组件；关闭弹窗丢弃；**绝不**写回 `FormDef` |

### 6.6 组件拆分与数据流

```
forms/index.vue          ──「预览」──► FormPreviewModal
forms/designer.vue       ──「预览」──► FormPreviewModal
                              │
                              ▼
                     FormPreviewShell
                       · mode Segmented
                       · device Segmented
                       · sampleRecord
                       · 校验 / 重置 / 看 JSON
                              │
                              ▼
                     RecordFormFields
                       designer=false
                       :mode :fields :model-value
```

| 组件（建议路径） | 职责 |
|------------------|------|
| `shared/form-preview-modal.vue` | Modal 壳、标题、开关 |
| `shared/form-preview-shell.vue` | mode/device/样例/校验；可被路由页复用 |
| 现有 `record-form-fields.vue` | 唯一渲染器；预览禁止传 `designer` |
| 现有 `form-layout-designer.vue` | 保持设计态；**不把预览混进画布选中逻辑** |

数据流：

```
设计器预览：
  schema = 当前画布 FormDef（可能未保存）
  → FormPreviewModal({ schema, name, code })

列表预览（草稿）：
  GET /api/orchestration/forms/{id}          ← 只返回草稿
  → FormPreviewModal({ schema: dto.schema, name, code, status })

已发布只读演示：
  GET /api/orchestration/forms/published/{code}   ← 已发布快照；未发布抛 FormNotPublished
  → 同上，Title 带「已发布」Tag
```

后端首期**可不新增 API**；复用已有 Get / GetPublishedByCode（已核实：后端确实无预览端点，现有 Get 系列足够支撑只读预览）。若后续要「匿名分享预览」，再加只读 token（明确不在首期）。消费端须意识到：已发布快照只在 `published/{code}` 分支有效，且存在 §4.2 的回落边界。

### 6.7 交互细则（可验收）

**设计器**

1. 顶栏在「保存」左侧或右侧增加主按钮 **预览**。
2. 无字段时点预览 → `message.warning('请先添加字段')`。
3. 打开弹窗默认：**填写·新增** + **电脑端**。
4. 弹窗内可切换三种 mode、两种设备；试填后点「校验」看必填/规则。
5. 关闭弹窗，画布选中态与属性面板不变；预览数据丢弃。

**表单库列表**

1. 行操作增加 **预览**（放在「设计」前或后）。
2. 草稿 / 已发布均可预览；Tag 显示状态。已发布态需走 `published/{code}` 才能拿到快照。
3. 空 Schema（0 字段）→ 提示去设计。

**与画布内 mode 切换的关系**

| 能力 | 画布 Segmented | 预览弹窗 |
|------|----------------|----------|
| 用途 | 设计时快速看可见性/只读配置 | **交付前核对最终效果** |
| 装饰 | 有选中框、拖拽 | 无 |
| 是否保留 | **保留**（设计辅助） | **新增**（正式预览） |

二者并存，不互相替代。文档与 UI 文案区分：「画布预览模式」可改称「场景」避免用户以为已是预览。

建议文案调整（实现时一并改）：

- 画布：`场景：新增 | 编辑 | 详情`（辅助设计）
- 按钮：`预览`（打开弹窗）

### 6.8 明细 / 树在预览中的期望

对齐 form-create 子表单与当前 Schema：

| control | 填写态预览 | 阅读态预览 |
|---------|------------|------------|
| `table` | 表格 + 添加行/删行 | 只读表格或描述列表 |
| `list` | 重复块增删 | 只读块 |
| `tree` | 根节点增删；P1 可先扁平根级（与现状一致） | 只读；P2 再补「添加子节点」 |

预览阶段发现树编辑不足时，优先修 `RecordFormFields`，保证预览=运行时。

### 6.9 分期

| 期次 | 范围 | 验收 |
|------|------|------|
| **P0（本方案落地）** | 设计器顶栏预览弹窗；列表行预览；create/update/detail；PC/移动宽度；本地校验 + JSON 查看；样例重置 | 无需发布也能预览；预览无设计装饰；关弹窗不污染 Schema |
| **P1** | dirty 提示；「填充示例」；独立预览路由；画布文案改为「场景」 | 实施可把预览 URL 发给同事 |
| **P1.5** | 预览内展示字段校验错误锚点；明细树「添加子节点」 | 复杂表单可测全 |
| **P2** | 选 formRef 时旁路小预览；可选 Schema JSON 只读 Tab；导出说明文案 | 配置联调体验 |
| **不做（首期）** | 生成 Vue SFC/HTML/建表 SQL；真实提交到 Flow；匿名公网预览；多语言预览壳 | — |

### 6.10 开工清单

**前端**（⚠️ 未核验：勾选状态以 `vue-demo` 实际代码为准，本仓无法确认）

- [ ] 新增 `form-preview-shell`/`form-preview-modal`
- [ ] `forms/designer`：设计器内「预览」+「Schema JSON」
- [ ] `forms/index`：行「预览」
- [ ] 画布文案改为「场景」
- [ ] `form-schema.ts` 规范化 / 校验复用 `form-validation`
- [ ] `form-schema-renderer.vue` + 运行时页改走渲染器

**后端**

- [x] P0：**无强制改动**（复用 `GET /forms/{id}` / `GET /forms/published/{code}`）——已核，见 §3.1
- [ ] 若要做「预览发布态 vs 草稿」分端点，需先解决 §4.1/§4.2 的真缺陷，再补文档修订

**文档 / 菜单**

- [ ] 本文件评审通过后再改 UI
- [ ] 不需要新菜单；预览为页面内能力

### 6.11 评审结论栏

- [x] 同意主对标：**FcDesigner 弹窗预览 + VForm 设计器/渲染器分离**
- [x] 同意双入口：设计器 + 列表
- [x] 同意三种 mode + PC/移动；首期不出码/不真实提交
- [x] 同意预览只用前端渲染器（`RecordFormFields`），画布保留「场景」切换作设计辅助
- [x] 同意 P0 后端不新增 API（已核实后端确实无预览端点，现有 Get 系列足够）
- [ ] ⚠️ 原「设计 → FormDef JSON → 基础渲染器管线已落地」的勾选**已撤销**：该管线在前端仓，本仓无法核验；后端侧**仅**确认 Layout→Fields 拍平与发布快照机制（§3.2/§3.3）
- [ ] 待决：是否把 §4.1 幽灵字段与 §4.2 无版本回放列为预览上线的**前置修复**

**评审通过后**按 §6.9/§6.10 开工；未通过前不改表单库预览相关代码。

---

## 7. 参考链接

| 类型 | 链接 |
|------|------|
| FcDesigner Pro 预览 | https://pro.form-create.com/doc/help/feature/preview |
| FcDesigner 预览操作 | https://view.form-create.com/book/feature/preview-form |
| FcDesigner 阅读模式 | https://view.form-create.com/preview |
| form-create 设计器 | https://www.form-create.com/designer/ |
| VariantForm / VForm3 | https://www.vform666.com/vform3/ |
| Formily Form Builder | https://github.com/alibaba/formily （Designable PREVIEW 面板） |
| form-js | https://github.com/bpmn-io/form-js |
| Ant 表单页 | https://ant.design/docs/spec/research-form-cn |
| Ant 详情页 | https://ant.design/docs/spec/detail-page-cn |
| 本仓库表单规划 | [`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md) |
| 本仓库报表配置交互规格 | [`lowcode-report-config-ux.md`](./lowcode-report-config-ux.md) |
| 本仓库报表管理规划 | [`lowcode-report-management.md`](./lowcode-report-management.md) |

---

## 8. 修订记录

| 日期 | 修订 | 说明 |
|------|------|------|
| 2026-10-13 | 事实契约化重写（基线 `267d375`） | 按「事实契约 / 前端交互规格 / 规划」三段拆分：新增 §1.3 关键判定（本仓无表单预览端点）、§2 唯一权威能力表（逐条附 `相对路径:行号`）、§3 后端写实、§4 缺陷清单、§5 规划；原文对标/信息架构/分期/评审清单保留为 §1.4/§6；原文中「后端已实现预览」类暗示全部降级；前端内容统一标 ⚠️ 未核验；外链均为相对路径与官方 URL |
