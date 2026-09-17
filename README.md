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
- [IAM / 菜单 RBAC](docs/iam-rbac-menu.md)
- [添加新微服务](docs/add-new-service.md)

## 说明

官方 `abp new -t microservice` 需要商业许可；本仓库基于社区开源模板改造。不要用商业模板命令覆盖本仓库，除非已恢复 ABP Business 许可。

配套前端为独立仓库（Vue Vben Admin / `web-antd`），通过网关与 AuthServer 对接。
