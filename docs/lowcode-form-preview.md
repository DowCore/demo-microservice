# 表单库预览能力方案（Form Preview）

> **性质**：实现前方案文档。先定参考模型与交互边界，再改代码。  
> **背景**：表单库已可建、设计、发布，但**缺少独立、可感知的「预览」入口**；设计器画布虽能切换新增/编辑/详情，仍夹杂选中框与设计态控件，不等于成熟产品里的预览。  
> **关联**：[`lowcode-form-workflow-board.md`](./lowcode-form-workflow-board.md)（表单库 / FormDefinition）、[`lowcode-report-config-ux.md`](./lowcode-report-config-ux.md)（报表绑表单）。  
> **前端**：`D:\Project\vue-demo` · **后端**：`demo-microservice` SaaS Orchestration。

**一句话**：学 FcDesigner / VariantForm（VForm3）——**设计器工具栏「预览」弹窗 + 列表行「预览」**；用现有 `RecordFormFields` 同源渲染，按 create / update / detail 试填与只读，预览数据不写回 Schema。

---

## 1. 现状缺口（为何「还是不能预览」）

| 位置 | 现状 | 用户体感 |
|------|------|----------|
| 表单库列表 | 仅有「设计 / 发布 / 删除」 | **没有预览按钮**，已发布也无法一眼看最终效果 |
| 设计器顶栏 | 仅「返回 / 保存 / 发布」 | **没有「预览」**，与成熟设计器习惯不符 |
| 画布内 Segmented | 有 create / update / detail | 仍是**设计态**：可选中、拖改、右侧属性；不是干净运行态 |
| 试填 / 校验 | 画布可改 `previewRecord`，无提交校验反馈 | 无法像真实页一样「故意不填必填 → 看提示」 |
| 设备宽度 | 无 | 无法核对 24 栅格在窄屏下的换行 |
| Schema / 样例数据 | 无独立预览页、无样例 JSON | OA / 报表引用前无法对外演示 |

结论：画布 mode 切换 ≠ 预览。必须补齐**显式预览入口 + 运行态壳（无设计装饰）**。

---

## 2. 成熟参考模型（学什么、不照搬什么）

### 2.1 总览对照

| 产品 / 开源 | 地址 | 预览怎么做 | 对本仓库要学的点 | 不宜照搬 |
|-------------|------|------------|------------------|----------|
| **FcDesigner / form-create Pro** | [预览文档](https://pro.form-create.com/doc/help/feature/preview)、[开源预览](https://view.form-create.com/book/feature/preview-form) | 工具栏 **预览** → **弹窗**；表单模式试填、**阅读模式**只读；PC/移动端宽度；可选出码/SQL | **弹窗预览**、表单/阅读双态、预览数据不回写画布、设备切换 | 生成 SFC/HTML/SQL 首期不做 |
| **VariantForm / VForm3** | [vform3](https://www.vform666.com/vform3/)、`previewForm()` | 设计器默认带 **预览表单按钮**；弹窗内用 **Render** 渲染同一份 JSON；业务侧 `setFormData` + `disableForm` 做详情 | **设计器 / 渲染器分离**：设计态与预览态同一 Schema、不同壳 | 不强绑 Element Plus 整套 VForm |
| **Formily Designable** | [alibaba/formily](https://github.com/alibaba/formily) | 视口 `ViewPanel type="PREVIEW"`，与 DESIGNABLE / JSONTREE 并列 | **设计态 / 预览态切换**心智清晰 | React Designable 不迁入 |
| **form-js（bpmn-io）** | [bpmn-io/form-js](https://github.com/bpmn-io/form-js) | Editor + Viewer；Viewer 纯渲染 | 轻量 Viewer 组件思路 | 字段集偏流程表单 |
| **Ant Design 规范** | [表单页](https://ant.design/docs/spec/research-form-cn)、[详情页](https://ant.design/docs/spec/detail-page-cn) | 填写态 vs 详情只读平铺 | create/update 高效填写；detail **阅读态**排版 | — |
| **JeecgBoot Online** | Jeecg 表单设计 | 设计后「功能测试 / 预览」进可用页 | 列表也可进预览/测试 | Online 代码生成链路过重 |
| **NocoBase** | [nocobase](https://github.com/nocobase/nocobase) | 配置态 / 使用态一键切换 | 「配置完立刻像终端用户一样用」 | 整平台块体系 |

### 2.2 FcDesigner 预览信息架构（主对标）

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

### 2.3 VForm3 设计器 / 渲染器分离（实现模型）

| 组件角色 | VForm3 | Meta.Dow 对应 |
|----------|--------|----------------|
| 设计器 | `VFormDesigner` + `previewForm()` | `FormLayoutDesigner` + 顶栏「预览」 |
| 渲染器 | `VFormRender`（`setFormData` / `disableForm`） | **`RecordFormFields`**（`mode` + `modelValue`，无 `designer`） |
| Schema | formJson | `FormDefinition.schema`（`FormDef`） |
| 业务打开 | 弹窗/抽屉 + Render | 列表「预览」、报表/OA `formRef` 运行时 |

核心原则：**预览必须走渲染器路径，禁止在预览壳里打开设计器属性/选中/Sortable。**

### 2.5 已落地管线（2026-09 调整）

```
[左侧] 容器（栅格 1–4 / 卡片 / 数据表格 / 子表单）
        │ 选中容器
        ▼
[基础组件] 单行 / 多行 / 数字 / 日期 / 时间 / 开关
        │
        ▼
FormDef.layout  JSON（容器树）
        │ flatten → fields（兼容校验与旧运行时）
        ▼
FormLayoutView → RecordFormFields
```

对标截图 [VForm3 Pro](https://www.vform666.com/)：组件库分「容器 / 基础字段」，画布按容器嵌套，右侧改当前节点。本期不做标签页、弹出窗口、出码。

| 文件 | 角色 |
|------|------|
| `form-layout-designer.vue` | 设计器（产出 JSON） |
| `form-schema.ts` | 规范化 / 解析 / 样例数据 |
| `form-schema-renderer.vue` | 只读 JSON → 渲染入口 |
| `record-form-fields.vue` | 基础控件实现 |
| `form-preview-modal.vue` | 正式预览（非设计态） |

### 2.4 Formily 视口模式（次对标）

| ViewPanel | 含义 | 我们是否采用 |
|-----------|------|--------------|
| DESIGNABLE | 拖拽设计 | 已有画布 |
| PREVIEW | 纯运行预览 | **采用**（弹窗或全屏壳） |
| JSONTREE | Schema 编辑 | P2 可选「查看 JSON」 |

首期不做画布内整页切 PREVIEW（易与「选中编辑」打架）；**优先弹窗预览**，与 FcDesigner/VForm 一致。

---

## 3. Meta.Dow 目标体验

### 3.1 两个入口（必须都有）

| 入口 | 位置 | 行为 |
|------|------|------|
| **A. 设计器预览** | 表单设计页顶栏「预览」 | 用**当前未保存也可**的内存 Schema 打开预览弹窗（与 FcDesigner 一致：预览的是当前画布） |
| **B. 列表预览** | 表单库行操作「预览」 | 拉该条 `FormDefinition`（草稿或已发布均可）；已发布可标注「发布态」 |

可选后续：

| 入口 | 说明 |
|------|------|
| C. 独立路由 | `/orchestration/forms/preview?id=` 或 `?code=`，便于分享给实施同学（P1.5） |
| D. 已发布 lookup 旁路 | OA/报表选 `formRef` 时右侧小预览（P2） |

### 3.2 预览弹窗结构

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

### 3.3 三种模式（对齐现有 FormFieldDef）

| UI 文案 | 内部 mode | 行为 |
|---------|-----------|------|
| 填写·新增 | `create` | 按 `visibleOnCreate` / `requiredOnCreate` / `readonlyOnCreate` |
| 填写·编辑 | `update` | 按 update 可见/必填/只读；预填**样例数据**（可编辑） |
| 阅读·详情 | `detail` | 全只读（或控件 disabled）；对齐 Ant 详情页 |

明细 `table` / `list` / `tree` 在预览中必须可增删行（填写态）或只读展示（详情态），与运行时一致。

### 3.4 设备宽度

| 档位 | 内容区 max-width | 用途 |
|------|------------------|------|
| 电脑端 | 720px（或 100%） | 默认 |
| 移动端 | 375px 居中 + 边框模拟 | 看 24 栅格换行 |

不改 Schema，只改预览容器 CSS。

### 3.5 样例数据与校验

| 能力 | 规则 |
|------|------|
| 样例数据 | 打开预览时生成空对象 + 明细空数组；可「填充示例」按 control 类型塞假数据 |
| 编辑/详情预填 | 使用同一份 `sampleRecord`；切换 mode 不清空（除非点重置） |
| 校验 | 前端按 `requiredOn*` + `rules` 本地校验；成功则 `message` + 展示 JSON；**不调** create/update Flow |
| 隔离 | `sampleRecord` 仅存预览组件；关闭弹窗丢弃；**绝不**写回 `FormDef` |

---

## 4. 架构与组件拆分

### 4.1 组件关系

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

### 4.2 数据流

```
设计器预览：
  schema = 当前画布 FormDef（可能未保存）
  → FormPreviewModal({ schema, name, code })

列表预览：
  GET /api/orchestration/forms/{id}
  → FormPreviewModal({ schema: dto.schema, name, code, status })

已发布只读演示（可选）：
  GET /api/orchestration/forms/published/{code}
  → 同上，Title 带「已发布」Tag
```

后端首期**可不新增 API**；复用已有 Get / GetPublishedByCode。若后续要「匿名分享预览」，再加只读 token（明确不在首期）。

### 4.3 与发布态关系

| 场景 | Schema 来源 | 备注 |
|------|-------------|------|
| 设计中预览 | 内存草稿 | 可与库中不一致；标题提示「未保存更改」若 dirty |
| 列表预览草稿 | 库中草稿 | |
| 列表预览已发布 | 库中已发布 Schema | 与 OA `formRef` 实际引用一致（以 Publish 快照为准；若当前实现 Publish 即写同一 Schema，文档注明） |

> 实现时核对：`PublishAsync` 是否生成不可变快照。若发布=改 Status，则预览已发布与草稿编辑中可能不同——列表预览应按**当前库记录**展示，并在 UI 标明状态。

---

## 5. 交互细则（可验收）

### 5.1 设计器

1. 顶栏在「保存」左侧或右侧增加主按钮 **预览**。  
2. 无字段时点预览 → `message.warning('请先添加字段')`。  
3. 打开弹窗默认：**填写·新增** + **电脑端**。  
4. 弹窗内可切换三种 mode、两种设备；试填后点「校验」看必填/规则。  
5. 关闭弹窗，画布选中态与属性面板不变；预览数据丢弃。

### 5.2 表单库列表

1. 行操作增加 **预览**（放在「设计」前或后）。  
2. 草稿 / 已发布均可预览；Tag 显示状态。  
3. 空 Schema（0 字段）→ 提示去设计。

### 5.3 与画布内 mode 切换的关系

| 能力 | 画布 Segmented | 预览弹窗 |
|------|----------------|----------|
| 用途 | 设计时快速看可见性/只读配置 | **交付前核对最终效果** |
| 装饰 | 有选中框、拖拽 | 无 |
| 是否保留 | **保留**（设计辅助） | **新增**（正式预览） |

二者并存，不互相替代。文档与 UI 文案区分：「画布预览模式」可改称「场景」避免用户以为已是预览。

建议文案调整（实现时一并改）：

- 画布：`场景：新增 | 编辑 | 详情`（辅助设计）  
- 按钮：`预览`（打开弹窗）

---

## 6. 明细 / 树在预览中的期望

对齐 form-create 子表单与当前 Schema：

| control | 填写态预览 | 阅读态预览 |
|---------|------------|------------|
| `table` | 表格 + 添加行/删行 | 只读表格或描述列表 |
| `list` | 重复块增删 | 只读块 |
| `tree` | 根节点增删；P1 可先扁平根级（与现状一致） | 只读；P2 再补「添加子节点」 |

预览阶段发现树编辑不足时，优先修 `RecordFormFields`，保证预览=运行时。

---

## 7. 分期

| 期次 | 范围 | 验收 |
|------|------|------|
| **P0（本方案落地）** | 设计器顶栏预览弹窗；列表行预览；create/update/detail；PC/移动宽度；本地校验 + JSON 查看；样例重置 | 无需发布也能预览；预览无设计装饰；关弹窗不污染 Schema |
| **P1** | dirty 提示；「填充示例」；独立预览路由；画布文案改为「场景」 | 实施可把预览 URL 发给同事 |
| **P1.5** | 预览内展示字段校验错误锚点；明细树「添加子节点」 | 复杂表单可测全 |
| **P2** | 选 formRef 时旁路小预览；可选 Schema JSON 只读 Tab；导出说明文案 | 配置联调体验 |
| **不做（首期）** | 生成 Vue SFC/HTML/建表 SQL；真实提交到 Flow；匿名公网预览；多语言预览壳 | — |

---

## 8. 实现清单（开工用）

### 8.1 前端

- [x] 新增 `form-preview-shell`/`form-preview-modal`（已用 Modal 一体）
- [x] `forms/designer`：设计器内「预览」+「Schema JSON」
- [x] `forms/index`：行「预览」
- [x] 画布文案改为「场景」
- [x] `form-schema.ts` 规范化 / 校验复用 `form-validation`
- [x] `form-schema-renderer.vue` + 运行时页改走渲染器

### 8.2 后端

- [ ] P0：**无强制改动**（复用 Get / GetPublishedByCode）
- [ ] 核对 Publish 是否快照；若需「预览发布态 vs 草稿」分端点，再补文档修订

### 8.3 文档 / 菜单

- [ ] 本文件评审通过后再改 UI  
- [ ] 不需要新菜单；预览为页面内能力

---

## 9. 参考链接（精选）

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

---

## 10. 评审结论栏（实现前勾选）

- [x] 同意主对标：**FcDesigner 弹窗预览 + VForm 设计器/渲染器分离**
- [x] 同意双入口：设计器 + 列表
- [x] 同意三种 mode + PC/移动；首期不出码/不真实提交
- [x] 同意预览只用渲染器（`FormSchemaRenderer`），画布保留「场景」切换作设计辅助
- [x] 同意 P0 后端不新增 API
- [x] **设计 → FormDef JSON → 基础渲染器** 管线已落地（见 §2.5）

**评审通过后**按 §8 开工；未通过前不改表单库预览相关代码。
