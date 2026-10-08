# Meta.Dow

开源 ABP 微服务后端（.NET 10 / ABP 10）。基于社区模板改造为 **MongoDB + YARP + .NET Aspire**，AuthServer 使用开源主题 **LeptonX Lite**。

## 已实现能力

### 平台与基础设施

- **微服务拆分**：Administration / Identity / Projects / SaaS，统一经 YARP 网关对外
- **Aspire 一键编排**：MongoDB、Redis、RabbitMQ、Seq + DbMigrator + 各服务
- **认证授权**：OpenIddict（密码模式 / 授权码 + PKCE / refresh_token），JWT 鉴权
- **多租户**：SaaS 租户管理与租户解析
- **可观测**：Serilog + Seq，OpenTelemetry 链路

### 身份与权限（IAM）

- 用户 / 角色 / 组织单元管理
- 动态菜单与按钮权限（按权限码过滤）
- 数据范围（Data Scope）配置
- 邮件等系统设置

### 低代码逻辑编排（SaaS）

将可视化流程发布为系统 API（`POST /api/logic/{flowKey}`）：

| 能力 | 说明 |
| --- | --- |
| 流程定义 / 版本 / 发布 | 草稿编辑、版本历史、发布缓存 |
| 执行实例 | 试运行与正式调用记录、节点轨迹 |
| 节点类型 | Start / End、Condition、HttpCall、Code（沙箱）、Sql、Assign、Log、Mask、Throw |
| 数据连接 | MySQL / Oracle / SQL Server / Redis / Mongo（白名单 DataSource） |
| 入参与系统参数 | 请求参数校验、系统上下文、日期表达式 |
| 出参整形 | 嵌套 `map.item`、聚合、分组、可见性 / 脱敏；支持 `promote` 使 `data` 直接为数组或对象 |

详细规格见 [docs/lowcode-logic-orchestration.md](docs/lowcode-logic-orchestration.md)。

表单库、报表与 OA 审批也落在 SaaS，和逻辑编排分开建模。表单按提交数据预览；审批流用画布拖节点、连线，一个流程只绑定一张表单，节点前后调用已发布的 `flowKey`。见文末架构评价与 [文档](#文档)。

## 目录

```
src/
  apps/          AppHost（Aspire）、AuthServer（OpenIddict）
  gateway/       YARP API 网关
  services/      administration / identity / projects / saas
  shared/        公共库、ServiceDefaults、DbMigrator
docs/            方案与开发文档
```

## 本地启动

需要：.NET 10 SDK、Docker Desktop、Node.js + Yarn、[ABP CLI](https://abp.io/docs/latest/cli)（`abp install-libs`）。

首次克隆后安装 AuthServer 登录页前端库（`wwwroot/libs` 不进 Git）：

```bash
cd src/apps/Meta.Dow.AuthServer
abp install-libs
```

```bash
dotnet restore Meta.Dow.slnx
dotnet run --project src/apps/Meta.Dow.AppHost
```

Aspire Dashboard 会打印本地地址。默认管理员：`admin` / `1q2w3E*`。

| 入口 | 地址（开发） |
| --- | --- |
| 网关 | `https://localhost:7500` |
| 认证 | `https://localhost:7600` |

若 IDE F5 启动时 Gateway 显示 Finished、出现 `run_session` 超时：优先用上面的 `dotnet run`（不走 IDE run session）。AppHost 已将 `DCP_IDE_REQUEST_TIMEOUT_SECONDS` 调至 300。

> Docker 里残留的 Aspire 容器若提示网络不存在 / HTTP 404，请删除后重新 `dotnet run` AppHost，勿手动 Start 旧容器。

## 技术选择

| 组件 | 实现 |
| --- | --- |
| 应用框架 | ABP 10 开源模块 |
| 数据库 | MongoDB（本地单机关闭多文档事务） |
| 网关 | YARP |
| 编排 | .NET Aspire（MongoDB、Redis、RabbitMQ、Seq） |
| 认证 | OpenIddict |

## MongoDB 数据进 Git

容器库默认随 AppHost 结束清空。导出当前数据供同事使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mongo-dump.ps1
git add data/mongo
```

他人拉取后启动 AppHost 会自动 `mongorestore`，再跑 DbMigrator。见 [data/mongo/README.md](data/mongo/README.md)。

## 文档

- [低代码逻辑编排](docs/lowcode-logic-orchestration.md)
- [表单 / 工作流 / 看板边界](docs/lowcode-form-workflow-board.md)
- [表单预览](docs/lowcode-form-preview.md)
- [OA 审批流](docs/lowcode-oa-workflow.md)
- [报表管理](docs/lowcode-report-management.md)
- [报表配置交互](docs/lowcode-report-config-ux.md)
- [IAM / 菜单 RBAC](docs/iam-rbac-menu.md)
- [添加新微服务](docs/add-new-service.md)

## 架构评价

依据 `.cursor/rules/frontend-vue-vben.mdc` 与 `docs/` 中的方案文档，对当前系统的定位如下。

### 前端位置

本仓库只做 ABP 微服务后端。Angular 与 Blazor WebApp 不再使用。

| 角色 | 路径 | 用途 |
| --- | --- | --- |
| 实际前端 | `D:\Project\vue-demo` | Vue Vben Admin 5.x / Ant Design Vue。主应用 `apps/web-antd`，开发端口 `http://localhost:5666` |
| 前端参考 | `D:\Vue\vue-vben-admin` | 对照登录、鉴权、路由与 Vite 代理 |
| 认证客户端 | `MetaDow_Vue` | password / authorization_code / refresh_token |

前端经网关 `https://localhost:7500` 访问后端。低代码交互（AntV X6 逻辑画布与审批画布、24 栅格表单设计器、报表条件组）在 `vue-demo` 实现，契约以本仓库 API 与 `docs/` 为准。

### 全景

```
vue-demo/apps/web-antd (:5666)
        │  https://localhost:7500
        ▼
YARP Gateway
        ├─ AuthServer        :7600   OpenIddict
        ├─ Identity          :7002   用户 / 角色 / 组织单元
        ├─ Administration    :7001   菜单 / 设置 / 数据范围
        ├─ SaaS              :7003   租户 + 低代码（编排、表单、报表、审批）
        └─ Projects          :7004   业务项目域
.NET Aspire：MongoDB、RabbitMQ、Redis、Seq、DbMigrator
```

### 优势

- **技术栈与本地编排**：.NET 10、ABP 10、Aspire 9.5.2。`AppHost` 拉起 MongoDB、RabbitMQ、Redis、Seq 与 DbMigrator；`tools/mongo-dump.ps1` 与启动时 `mongorestore` 让种子数据可进 Git。网关用 YARP，认证独立在 AuthServer。
- **低代码三分离**：逻辑编排解决可热更的业务逻辑，节点共享 `vars`，发布为 `POST /api/logic/{flowKey}`，不做成第三方连接器市场。方案上对齐 THEN / IF / SWITCH / WHEN / FOR；已落地节点见上文能力表，SWITCH、并行与循环仍以 [逻辑编排文档](docs/lowcode-logic-orchestration.md) 为准。表单库 Schema 独立，可被资源、报表、审批复用。OA 只做人审：一个流程绑定一张表单，画布上定义节点再配办理人、会签规则和离开条件；算数、改状态、发消息仍调用已发布 `flowKey`，不在审批引擎里另写一套写入逻辑。
- **权限**：功能授权以 Permission 为准。动态菜单引用权限码，服务端过滤后再由前端生成路由。数据范围与功能权限分开。见 [docs/iam-rbac-menu.md](docs/iam-rbac-menu.md)。

### 演进风险

1. **SaaS 职责过重**。租户管理与数据源、表设计、逻辑编排、表单、报表、审批运行时目前同在 SaaS。低代码继续变大时，应按 [docs/add-new-service.md](docs/add-new-service.md) 抽成独立服务，避免 SaaS 成为单点。
2. **MongoDB 与跨文档一致性**。本地单机关闭多文档事务。主子表、审批状态与内部元数据若要一起回滚，一致性要靠流程设计，不能假设单机 Mongo 提供跨集合事务。外部 MySQL / Oracle 等数据源的事务边界另按数据源规划。
3. **脚本与动态 SQL**。编排里的 Code、Sql 节点已有数据源白名单和参数化查询。多租户下仍须限制脚本的内存、超时与反射能力，避免一个租户拖垮宿主或读到别人的数据。
4. **跨仓库契约**。后端在本仓库，前端在 `vue-demo`，DSL、节点配置和筛选条件两边都要手写。没有从 Swagger 自动生成 TypeScript 类型时，字段变更容易一边改了另一边不知道。

### 评分

| 维度 | 评分 | 点评 |
| --- | --- | --- |
| 架构前沿性 | 9.5 / 10 | .NET 10、ABP 10、Aspire、YARP，本地微服务调试成本低 |
| 文档与设计规范 | 9.8 / 10 | `docs/` 写明对标、边界、反模式和分期，方案可复核 |
| 低代码完整性 | 9.0 / 10 | 编排、表单、报表、审批分开，又用 `flowKey` 串起来 |
| 微服务职责边界 | 7.5 / 10 | 基础服务划分清楚；低代码堆在 SaaS，后续应拆出 |
| 工程协同 | 8.5 / 10 | Aspire 与数据迁移顺手；前后端分仓，缺自动契约同步 |

后续重点是把 SaaS 里的低代码模块抽成独立服务，并补上前后端类型契约的生成与校验。

## 说明

官方 `abp new -t microservice` 需要商业许可；本仓库基于社区开源模板改造。不要用商业模板命令覆盖本仓库，除非已恢复 ABP Business 许可。
