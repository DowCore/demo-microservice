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

行号和缺陷机理在 `detail/`。`detail/` 里有过时句子：编排契约 §15 仍写 Switch「校验已放行」；数据集合与报表契约仍把 `batchupdate` 写成不可用。这两处都以模块文档为准——Switch 是发布期拒绝，批量更新已在 `91d75bd` 修掉。

看板没有后端模型，不单列模块。添加微服务的步骤在 [add-service.md](./add-service.md)，不是产品功能。
