# 定时任务：重试次数与失败记录

> 编制日期：2026-10-15。代码基线 `73aec3d`。**同日已按第 9 节落地**（接口与 `vue-demo` 定时任务页）。下文第 1 节是落地前的对照，当前行为以 [logic-orchestration.md](../logic-orchestration.md) 为准。

定时任务继续做触发层。一次到点调用仍走已有的 `IPublishedFlowInvoker` → `FlowExecutor`。不引入 Hangfire、Quartz 或第二套调度器。

## 1. 现在做到哪一步

`FlowSchedule` 保存 Cron、时区、固定入参、`LastFiredAt`、`NextFireAt`。`FlowScheduleHostedService` 约每 15 秒扫一次已启用计划，用 `IAbpDistributedLock`（键 `meta-dow:flow-schedule:{code}`）保证同一计划同时只有一个实例在触发。

一次触发的实际行为：

| 能力 | 现状 |
|---|---|
| 到点调用已发布 `flowKey` | 有。`InvokeAsync`，`triggerSource` 为 `Schedule:{code}` |
| 多副本不重复触发 | 有分布式锁。锁只包住这一次 `FireAsync` |
| 失败后的 Cron 水位 | 调用抛错也会 `MarkFired`，`NextFireAt` 照常往前走 |
| 流程业务失败 | `InvokeAsync` 返回 `Success=false` 并写入 `FlowInstance.Status=Failed`。调度器不看这个返回值，只接住异常 |
| 重试次数 | 没有字段，没有重试队列 |
| 按「这一次触发」查询失败 | 没有。只能在全部 `FlowInstance` 里按 `TriggerSource` 字符串过滤 |
| 立即执行 | `POST /api/orchestration/schedules/{id}/run-now` 会调用流程，不改 `LastFiredAt` / `NextFireAt`，也不留调度侧记录 |
| 错过的整段时刻 | 宕机期间积压的多个时刻合并成补跑一次，然后从当前时间重算下一次 |
| 上一次还在跑 | 靠那把锁跳过。锁释放前的重叠不会再开一次 |
| 设计器 | `vue-demo` 没有定时任务页面 |

`FlowInstance` 已经记下节点轨迹和错误文本。缺的是调度自己的账：哪一次正点、第几次尝试、下次什么时候再试、最终是否放弃。

## 2. 对照三个公开实现

只取和「重试 + 失败记录」直接相关的部分。三个项目都把「时刻表」和「这一次执行」拆开。

| 来源 | 时刻表怎么走 | 失败的这一次怎么走 | 不照搬的部分 |
|---|---|---|---|
| [Hangfire `AutomaticRetryAttribute`](https://github.com/HangfireIO/Hangfire/blob/main/src/Hangfire.Core/AutomaticRetryAttribute.cs) | 周期任务继续排下一轮 | 失败任务自己计 `RetryCount`，按 `DelaysInSeconds` 或指数退避再入队。次数用尽后进入 `Failed`，可手工再试；也可配置为直接删除 | 不嵌入 Hangfire 服务端，不用它的全局过滤器默认 10 次 |
| [Quartz.NET 触发器与 misfire](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/more-about-triggers.html) | Cron 的默认智能策略是错过之后补跑一次，再按时刻表继续（`FireAndProceed`） | `DisallowConcurrentExecution` 保证同一作业不重叠。执行事实落在已触发记录上，而不是只改触发器的下次时间 | 不部署 Quartz 集群表，不提供 `IgnoreMisfires`（宕机后按错过次数连续狂奔） |
| [Temporal Schedule](https://github.com/temporalio/temporal/blob/main/docs/architecture/schedules.md) | 水位向前走。重叠策略默认 `Skip`。补跑窗口决定宕机后还补不补 | 每一次 `BufferedStart` 自带 `Attempt` 和 `BackoffTime`。重试挂在这一次上，不挡住后面的正点。失败记录留在最近动作里 | 不把计划做成持久化工作流，不做 `PauseOnFailure` 默认打开，不做回填一段历史 |

共同结论：

1. Cron 水位在这次触发开始时就推进。重试是这一次触发自己的队列。
2. 失败记录按「一次正点（或一次手工执行）」保存，里面再记每次尝试。
3. 次数用尽后留下可查询的终态，并允许手工再跑这一次。时刻表不被这一次拖死。
4. 错过多个时刻时默认只补一次，不回放全部。
5. 上一次还在跑时，下一次正点记为跳过，不并行再开。

## 3. 目标行为

计划上可以配置：

- `maxAttempts`：这一次触发最多调用几次，含第一次。范围 1～5。默认 **1**，与现在相同。
- `retryDelaysSeconds`：第 2 次、第 3 次……之前的等待秒数。缺省为 `60, 300, 900`。数组短于剩余次数时，沿用最后一档。单档上限 3600 秒。

默认保持 1 次，是因为运行时幂等（轨道 A10）还没做。把默认改成自动重试，会让写流程在失败后再写一遍。调高次数是显式选择，接口和日后的界面都要写出这个后果。

一次触发结束后可以回答：

- 计划是哪一个，名义时刻是哪一分钟。
- 第几次尝试，对应哪条 `FlowInstance`。
- 成功、等待重试、已放弃，还是因为上次还在跑而跳过。
- 最后一次错误文本，以及下一次重试时间。

## 4. 两本账

### 4.1 计划仍只管水位

`FlowSchedule` 继续保存 Cron 与 `NextFireAt`。增补列表用的摘要，避免列表页去扫全部触发记录：

| 字段 | 含义 |
|---|---|
| `MaxAttempts` | 1～5，默认 1 |
| `RetryDelaysSeconds` | 等待秒数，默认 60 / 300 / 900 |
| `LastFireStatus` | 最近一次触发的终态或进行态 |
| `LastError` | 最近一次错误，成功后清空 |
| `ConsecutiveFailures` | 连续放弃次数。成功或跳过不计。手工执行计入 |

`MarkFired` 仍在**这一次触发被接纳时**调用，不等到重试结束。接纳包括：开始执行，以及因重叠而跳过。这样 `NextFireAt` 不会停在过去，轮询也不会把同一次正点反复当成新任务。

### 4.2 新增触发记录 `FlowScheduleFire`

一条记录代表一次正点或一次手工执行。尝试列表嵌在同一条里。`maxAttempts ≤ 5`，文档不会膨胀。节点轨迹仍只放在 `FlowInstance`，这里只存实例 Id 和错误摘要。

| 字段 | 含义 |
|---|---|
| `ScheduleId` / `ScheduleCode` / `TenantId` / `FlowKey` | 归属 |
| `TriggerKind` | `Cron` 或 `Manual` |
| `NominalTimeUtc` | Cron 的名义时刻。手工执行为实际开始时间 |
| `Status` | `Running` / `Succeeded` / `RetryWait` / `Dead` / `Skipped` |
| `Attempt` | 已经开始的次数 |
| `MaxAttempts` | 创建时从计划复制，之后改计划不影响这一次 |
| `NextRetryAt` | 仅 `RetryWait` |
| `StartedAt` / `FinishedAt` | 整次触发的起止 |
| `Error` | 最后一次错误 |
| `SkipReason` | 仅 `Skipped`，当前只使用 `Overlap` |
| `Attempts[]` | `n`、`instanceId`、`startedAt`、`finishedAt`、`success`、`error` |

索引：

- Cron：`TenantId + ScheduleId + NominalTimeUtc` 唯一。同一分钟只接纳一次。
- 手工：不参与该唯一键，每次 `run-now` 新插入一条。
- 查询：`ScheduleId + Status + NextRetryAt`，供轮询捞 `RetryWait`。
- 列表：`ScheduleId + StartedAt` 降序。

保留：每个计划最近 200 条，更早的在写入新记录时删掉。不做跨计划的全局归档任务。

## 5. 轮询怎么判

`TickAsync` 仍是唯一入口。每一轮做两件事，都先拿该计划的同一把锁。

**到期的 Cron**

1. `NextFireAt <= now` 的计划进入候选。
2. 若该计划已有 `Running` 或 `RetryWait`：插入一条 `Skipped`（`SkipReason=Overlap`），把水位推到 `now` 之后的下一次，结束。这与 Temporal 的 `Skip`、Quartz 的禁止并发一致。
3. 名义时刻取 `NextFireAt`。首次没有 `NextFireAt` 时，名义时刻取本轮 `now`（保持现有首次补算）。
4. 插入 `Running` 记录并推进 `NextFireAt`，然后调用流程。
5. 进程在插入之后、调用之前退出时，留下 `Running`。见第 6 节。

**到期的重试**

1. 捞 `Status=RetryWait` 且 `NextRetryAt <= now`。
2. 该计划另有 `Running` 时，本轮不动这条重试。
3. 把状态改回 `Running`，`Attempt` 加一，再调用。名义时刻不变，所以不会和后面的正点抢同一条唯一键。

**调用结果**

`FireAsync` 必须读取 `PublishedFlowInvokeResult`，不能只接住异常。

| 结果 | 记录 | 水位 |
|---|---|---|
| `Success=true` | `Succeeded`，清空计划上的 `LastError`，连续失败归零 | 已在接纳时推进 |
| 失败且 `Attempt < MaxAttempts` | `RetryWait`，`NextRetryAt = now + delay` | 已推进，不等这次重试 |
| 失败且次数用尽 | `Dead`，`ConsecutiveFailures` 加一 | 已推进 |
| 调用抛异常（流程未发布、基础设施） | 与业务失败同一套次数 | 已推进 |
| 重叠 | `Skipped` | 推进到下一次，避免卡在过去 |

等待秒数取 `retryDelaysSeconds[Attempt - 1]`，越界则用最后一档。`maxAttempts=1` 时没有等待，失败直接 `Dead`。

`run-now` 走同一套结果处理，`TriggerKind=Manual`。它不调用 `MarkFired`，不改 `NextFireAt`。手工失败同样按该计划的 `maxAttempts` 进入重试。

## 6. 崩溃与错过

`Running` 超过 `staleAfter`（默认 15 分钟）仍未结束，视为这次尝试失败，按第 5 节转入 `RetryWait` 或 `Dead`。这只补「进程死了、记录停在 Running」的窗口。

15 分钟短于一次超长流程时，可能出现旧流程还在跑、新尝试又开始。当前引擎没有运行时幂等，方案不在这里补 A10。`staleAfter` 固定为 15 分钟，不做成每个计划一个旋钮。流程若可能超过这个时间，不要把 `maxAttempts` 调到 1 以上。

错过多个正点：保持现在的补跑一次（Quartz Cron 的 `FireAndProceed`）。不回放中间每一个时刻。跳过的那些时刻不逐条写 `Skipped`，只在这一次触发的错误或摘要里不出现它们；水位直接算到 `now` 之后。若以后要逐条审计错过，再单独立项，不放进这一批。

## 7. 接口

计划的创建和更新增加 `maxAttempts`、`retryDelaysSeconds`。非法 Cron、时区、次数和等待秒数在应用服务里拒绝。

| 方法 | 作用 |
|---|---|
| `GET /api/orchestration/schedules/{id}/fires` | 分页。可按 `status` 过滤 |
| `POST /api/orchestration/schedules/{id}/fires/{fireId}/retry` | 仅 `Dead` 可调用。把该条改回 `RetryWait` 且 `NextRetryAt=now`，`MaxAttempts` 在原值上加 1，让自动规则再跑一次。不新开名义时刻 |

列表 DTO 带上 `lastFireStatus`、`lastError`、`consecutiveFailures`。

权限沿用 `OrchestrationPermissions.Schedules`。查询用 `Default`，手工再试与 `run-now` 同用 `Run`。

## 8. 明确不做

- 不把 Hangfire / Quartz 嵌进宿主。
- 不把重试做成 DSL 节点，也不改 `RunSegmentAsync`。
- 不默认暂停计划（Temporal 的 pause-on-failure）。连续 `Dead` 只反映在 `ConsecutiveFailures` 上。
- 不回放宕机期间的每一个 Cron 时刻。
- 不在本方案实现运行时幂等。次数默认 1。
- 不在本方案做 `vue-demo` 页面。接口稳定后再做列表、失败抽屉和「再试一次」。
- 不改 Rabbit / Kafka / MQTT。它们的失败记录是另一张账。

## 9. 落地顺序与验收

1. **只记账。** 插入 `FlowScheduleFire`，Cron 与 `run-now` 都写成功 / `Dead`。`maxAttempts` 固定为 1。现有「失败也推进水位」保持不变。验收：失败的流程能按计划 Id 查到一条 `Dead`，且带 `FlowInstance` Id；成功为 `Succeeded`；重叠为 `Skipped`。
2. **加重试。** 配置次数和等待秒数。`RetryWait` 到点后同一名义时刻再调用。验收：`maxAttempts=3` 时前两次失败后记录仍是一条，`Attempt` 递增；第三次 `Dead`；期间 `NextFireAt` 已经是更后的正点；默认 1 次的旧数据行为不变。
3. **手工再试与摘要。** `Dead` 的再试接口、计划列表上的最近状态。验收：再试不改 `NextFireAt`；成功后连续失败归零。

决策收成纯函数（给定计划、未完成记录、当前时间，返回「开一次新的 / 跳过 / 重试 / 判僵死」），用编排单测覆盖，不依赖 Mongo。唯一索引和轮询的集成测试不在这三步里。

## 10. 参考

- Hangfire：[AutomaticRetryAttribute.cs](https://github.com/HangfireIO/Hangfire/blob/main/src/Hangfire.Core/AutomaticRetryAttribute.cs)，[异常与 Failed 状态](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html)
- Quartz.NET：[触发器与 misfire](https://www.quartz-scheduler.net/documentation/quartz-4.x/tutorial/more-about-triggers.html)，[MisfireInstruction.cs](https://github.com/quartznet/quartznet/blob/main/src/Quartz/MisfireInstruction.cs)
- Temporal：[Schedule 架构](https://github.com/temporalio/temporal/blob/main/docs/architecture/schedules.md)，[错过动作](https://docs.temporal.io/troubleshooting/schedule-missed-actions)
