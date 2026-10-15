# 低代码功能总览

文档按功能模块命名。每一篇都是同一结构：

1. **已实现**：代码里已经可用的能力。
2. **相对对标的不足**：参考项目有、本仓还没有或做弱了的地方。已修好的项不写在这里。
3. **后续规划**：当前不可用。规划不是契约，不能按规划去配置。

逻辑编排是底座。其余模块靠已发布的 `flowKey` 调用它。

| 模块 | 文档 | 对标 |
|---|---|---|
| 逻辑编排 | [logic-orchestration.md](./logic-orchestration.md) | LiteFlow；运行保障看 n8n / Temporal |
| 数据集合 | [data-collection.md](./data-collection.md) | NocoBase Collection、Directus |
| 表单 | [form.md](./form.md) | Formily、FcDesigner / VForm |
| 报表 | [report.md](./report.md) | NocoBase 筛选页、NocoDB 条件组 |
| OA 人审 | [oa.md](./oa.md) | 钉钉 / 飞书审批 |
| 身份与菜单 | [identity.md](./identity.md) | ABP 的权限与导航路径 |

行号和缺陷机理在 `detail/`。模块文档是能力现状的唯一权威，`detail/` 是证据和推理。

| 文档 | 作用 |
|---|---|
| [logic-orchestration-contract.md](./detail/logic-orchestration-contract.md) | 编排 DSL 与 API 契约（§2 是能力状态的唯一权威表） |
| [logic-orchestration-assessment.md](./detail/logic-orchestration-assessment.md) | 编排引擎质量评估、逐条不足清单、十维对标矩阵、整改轨道划分、**整改实施记录与前端影响** |

**前端不在本仓**：Vue 工程在 `D:\Project\vue-demo`，本仓只提供 API 与 DSL 契约。因此「功能是否在界面上可用」无法由本仓文档回答——编排相关的前端影响记在[评估文档](./detail/logic-orchestration-assessment.md)§8.8。

`detail/` 里已确认的过时句子，一律以模块文档为准：

| 过时陈述 | 实际情况 |
|---|---|
| 编排契约 §15：Switch「校验已放行」 | Switch 是**发布期拒绝** |
| 编排契约 §16.3：条件算子下拉「不应提供 `in`/`notIn`/`between`」 | 这三个算子**已实现**（共 23 个），该指引已作废 |
| 编排契约 §2：Wait「❌ 无」 | **已实现**（§6.4 有规格） |
| 编排契约 §2：Http「SSRF 无防护」 | 已加**出站闸门**（§11.6） |
| 数据集合与报表契约：`batchupdate` 不可用 | 已在 `91d75bd` 修掉 |
| benchmark §1.5：沙箱是 Roslyn `CSharpScript` | 实为 **Jint**，`Directory.Packages.props` 钉在 **4.17.0** |
| 编排契约：承诺 `NodeExecution.asyncJobId` | 原模型中不存在，**现已补上** `NodeExecutionRecord.AsyncJobId` |
| `src/CLAUDE.md`：分析器已启用 | 未启用 |
| 前端规则文件 `logic-orchestration.mdc`：`wait` 未实现 | 规则现状段已改为 **已实现**；设计器面板仍未接入 |
| 编排契约 §14-22：`from` 模板与字面量「无消费点」 | ❗**这条陈述本身是错的**——两者**均已实现**（`FlowContextResolver.ResolveTemplate` / `ResolvePath`）。真正不生效的只有 `inputs[].type` 不参与必填校验 |

两条引用纪律：

1. **不引用行号，只引用符号名。** 契约 §2 与 §14 的主要落点已改为符号名，但**仍有零星行号残留**（整改多轮持续移动位置）。彻底清理需要一次独立的文档治理 pass。
2. **凡「某能力不存在」的结论，必须跨工程搜索后再下。** 上一轮曾因只在 `saas` 内搜关键词，误判「全链无重试」，而策略实际注册在共享工程 `Meta.Dow.ServiceDefaults`。benchmark 里的「0 命中」结论重排规划时需重新核验。
3. **把某字段列入「未实现/拒绝」黑名单前，必须先 grep 到消费点。** 第二轮曾因「文档里没写清`payload` 怎么用」而把它当成未实现字段加入黑名单，误杀了已实现的 `payload.item` 形态。文档**没写全**与代码**没实现**是两件事，前者不能作为后者的判据。

看板没有后端模型，不单列模块。添加微服务的步骤在 [add-service.md](./add-service.md)，不是产品功能。
