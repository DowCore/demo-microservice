# Meta.Dow

开源 ABP 微服务骨架（.NET 10 / ABP 10.0.2）。官方 `abp new -t microservice` 需要商业许可，本仓库基于社区开源模板 [Anto.Abp.Microservice.Template](https://github.com/antosubash/abp-microservice)（MIT）生成，并改为 **MongoDB + YARP + .NET Aspire**。

AuthServer 使用开源主题 **LeptonX Lite**。

## 目录

```
src/
  apps/          AppHost（Aspire 编排）、AuthServer（OpenIddict + LeptonX Lite）
  gateway/       YARP API 网关
  services/      administration / identity / projects / saas
  shared/        宿主公共库、ServiceDefaults、DbMigrator
```

## 本地一键启动

需要：.NET 10 SDK、Docker Desktop（Aspire 拉起 MongoDB / Redis / RabbitMQ / Seq）、Node.js + Yarn、[ABP CLI](https://abp.io/docs/latest/cli)（`abp install-libs`）。

首次克隆后在 AuthServer 安装登录页前端库（`wwwroot/libs` 不进 Git）：

```bash
cd src/apps/Meta.Dow.AuthServer
abp install-libs
```

缺少 `wwwroot/libs` 时，构建 AuthServer 也会自动跑一次 `abp install-libs`。

```bash
dotnet restore Meta.Dow.slnx
dotnet run --project src/apps/Meta.Dow.AppHost
```

Aspire Dashboard 会打印本地地址。默认管理员账号与 ABP 一致：`admin` / `1q2w3E*`。

网关（开发）：`https://localhost:7500`  
认证：`https://localhost:7600`（以 launchSettings 为准）

## 技术选择

| 组件 | 实现 |
| --- | --- |
| 应用框架 | ABP 10 开源模块 |
| 数据库 | MongoDB（本地单机关闭多文档事务） |
| 网关 | YARP |
| 编排 | .NET Aspire 13.5.3（MongoDB、Redis、RabbitMQ、Seq） |
| 认证 | OpenIddict |

## MongoDB 数据进 Git

容器里的库默认随 AppHost 结束而清空。要把当前业务数据给同事用：在 AppHost 跑着时导出并提交 `data/mongo`。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mongo-dump.ps1
git add data/mongo
```

他人拉取后启动 AppHost：Mongo 就绪后会自动 `mongorestore`，再跑 DbMigrator。说明见 [data/mongo/README.md](data/mongo/README.md)。

## 文档

- [添加新微服务](docs/add-new-service.md)

## 说明

不要用 `abp new -t microservice` 覆盖本仓库，除非已恢复 ABP Business 许可。
