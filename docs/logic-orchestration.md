# 逻辑编排

把 if、顺序、循环、并行和内部调用抽成 `POST /api/logic/{flowKey}`。主对标是 LiteFlow。运行保障对照 n8n 和 Temporal，不照搬它们的产品形态。

证据在 [契约](./detail/logic-orchestration-contract.md)、[评审](./detail/logic-orchestration-review.md)、[方案](./detail/logic-orchestration-plan.md)。代码基线 `91d75bd`。

## 已实现

调度只有 `RunSegmentAsync`。主链、ForEach、Parallel 共用它。

- 节点：Start / End、Condition、Http、Code（Jint）、Assign / Log / Mask / Throw、RabbitMQ 广播、SubFlow、ForEach、Parallel、资源 CRUD（含批量三入口）。
- 发布校验：出边基数、可达性、控制流结构、超时取值、写重试必须声明幂等键、节点类型白名单、输出契约。`sql` / `switch` / `wait` 在发布期拒绝。
- 运行保障：HTTP 非安全方法不重试；节点超时包在唯一分发点；非 HTTP 写默认不重试；批量写在一个 SQL 事务里，批量删除仍走 `DeleteFlowKey`。
- 出参按角色和权限在接口层裁剪，子图透传调用方角色。
- 触发：REST、RabbitMQ、Kafka、MQTT、Cron。
- 版本快照、`FlowUsage` 影响索引、发布时失效进程内缓存。

## 相对对标的不足

| 对标有的 | 本仓现状 |
|---|---|
| LiteFlow 的 SWITCH、EL 文本 | 多路分支只用 Condition。独立 Switch 会在发布时被拒绝，不是「配了就能跑」 |
| LiteFlow / 宜搭的 while、break | 只有 ForEach。`joinMode=any` 不取消其余分支 |
| n8n 的错误工作流、执行可重放；Temporal 的活动结果 | `async` 仍是 `Task.Run`，结果和失败不回写，实例却可能已成功 |
| Temporal 的幂等与超时护栏 | 幂等键只在发布时检查「声明了」，运行时不去重 |
| 会话事务或 Saga | 无 `txMode`。未知 DSL 字段被静默忽略，配了等于没配 |
| 多副本下的版本失效 | 缓存只在发布时失效，且只在本进程。停用或删除后，其他副本仍可能调用 |
| 单条流程的调用权限 | 持有 `Instances.Run` 即可调用全部已发布流程 |
| 出站请求白名单 | Http 节点没有 SSRF 防护 |

执行是一次请求内最多 200 步。适合短流程。不适合跨重启的长流程。

## 后续规划

| 优先级 | 事项 |
|---|---|
| P1 | `txMode=sameDataSource`：同一数据源上多次 `db.execute` 共用事务 |
| P1 | 未知 DSL 字段改为发布报错 |
| P1 | 异步节点写下 JobId 和失败；补 Wait，或禁止引用尚未返回的异步出参 |
| P1 | 停用和删除也失效缓存，并跨进程通知 |
| P1 | `Orchestration.Logic.{flowKey}`，没有该权限不能调用对应流程 |
| P1 | Http 出站拒绝私网、回环和链路本地地址 |
| P1 | 只读 SQL 判定覆盖 `WITH` 前缀和 `db.query` |
| P2 | 独立 Sql / Switch / Wait，以及 while、range-for、break / continue。Switch 要先有执行分支，再从发布拒绝改为放行 |
| P2 | 评估持久化执行（断点续跑），低成本形态是 `Paused` + `ResumeFrom` |
| P2 | DSL 的 JSON Schema、`version` 校验、节点级 OpenTelemetry |
| P3 | 侧栏 EL 文本、出参字段级审计、画布内 MqttPublish |

明确不做：连接器市场、把审批并进这个引擎、无沙箱脚本。
