# 添加新微服务

以现有 **`src/services/projects`** 为模板复制并接线。本文与当前仓库配置一致（ABP 10、MongoDB、YARP、Aspire）。

下文用新服务名 **`Ordering`** 举例：

- 目录：`src/services/ordering/`
- 本地 HTTPS 端口：**7005**（现有服务占用 7001–7004）
- JWT Audience / OpenIddict Scope：必须与 `MetaDowNames` 中的 API 名一致，例如 `MetaDowOrdering`

---

## 1. 复制分层项目

复制 `src/services/projects/` 整棵目录为 `src/services/ordering/`，按层改名：

| 层 | 项目名 |
| --- | --- |
| Domain.Shared / Domain / Application.Contracts / Application | `Meta.Dow.Ordering.*` |
| MongoDB | `Meta.Dow.Ordering.MongoDB` |
| HttpApi / HttpApi.Client | `Meta.Dow.Ordering.HttpApi*` |
| Host | `Meta.Dow.Ordering.HttpApi.Host` |

全局替换命名空间 `Meta.Dow.Projects` → `Meta.Dow.Ordering`，模块类 `ProjectsXxxModule` → `OrderingXxxModule`。

将上述 csproj 加入解决方案：

```bash
dotnet sln Meta.Dow.slnx add <各项目路径>
```

`HttpApi` 中 `RemoteServiceConsts.ModuleName` 会成为路由前缀。例如 `ordering` 对应对外路径 **`/api/ordering/...`**（ABP 约定）。网关匹配必须与该前缀一致。

---

## 2. 登记名字与数据库

在 `src/shared/Meta.Dow.Shared/Microsoft/Extensions/Hosting/MetaDowNames.cs` 增加常量，例如：

- `OrderingApi`：`"MetaDowOrdering"`（JWT Audience、Aspire 资源名）
- `OrderingDb`：`"MetaDowOrderingDb"`（MongoDB 库名 / 连接字符串名）

MongoDB 模块里 `AbpDbConnectionOptions` 的 `Databases.Configure(...)` 使用 `OrderingDb`。本仓库本地 MongoDB 为单机节点，各 `*MongoDbModule` 已关闭多文档事务，新服务保持同样写法。

Host 中 `ConfigureMicroservice(MetaDowNames.OrderingApi)` 必须使用该 API 名，否则 Bearer 的 audience 对不上。

---

## 3. Host 本地配置

对照 `Meta.Dow.Projects.HttpApi.Host`：

1. **`launchSettings.json`**
   - 增加名为 `Aspire` 的 profile（AppHost 通过该名称启动）。
   - `applicationUrl` 使用空闲端口（示例：`https://localhost:7005`）。

2. **`appsettings.json`**
   - `AuthServer:Authority`：`https://localhost:7600/`
   - `SwaggerClientId`：如 `Ordering_API`（与第 6 节种子客户端 Id 一致）
   - `RabbitMQ:EventBus:ClientName`：每个服务唯一，如 `MetaDow_Ordering`
   - `App:CorsOrigins`：包含网关、AuthServer、前端地址（可抄 Projects）

3. **Host 模块依赖**
   - `OrderingApplication` + `OrderingMongoDb` + `OrderingHttpApi`
   - `MetaDowMicroserviceModule` + `MetaDowServiceDefaultsModule`
   - 若要写入权限/设置/审计等 Administration 库，再按 Projects 依赖对应 MongoDB 模块

---

## 4. 接入 Aspire（本地一键拉起）

文件：

- `src/apps/Meta.Dow.AppHost/Program.cs`
- `src/apps/Meta.Dow.AppHost/Meta.Dow.AppHost.csproj`

步骤：

1. csproj 增加对 `Meta.Dow.Ordering.HttpApi.Host` 的 `ProjectReference`。
2. `mongo.AddDatabase(MetaDowNames.OrderingDb)`。
3. `migrator.WithReference(orderingDb)`。
4. `AddProject<Meta_Dow_Ordering_HttpApi_Host>(...)`  
   Aspire 生成的类型名会把项目名中的 `.` 换成 `_`。
   - `launchProfileName: "Aspire"`
   - `WithReference`：`orderingDb`、`adminDb`（若使用权限/设置）、`rabbitMq`、`redis`、`seq`
   - `WaitFor(rabbitMq)`、`WaitFor(redis)`、`WaitForCompletion(migrator)`
5. 网关的 `WaitFor` 中加入新服务，避免网关早于 Host 启动。

重新编译 AppHost 后才会出现 `Meta_Dow_Ordering_HttpApi_Host` 类型。

---

## 5. 网关 YARP

文件：`src/gateway/Meta.Dow.Gateway/appsettings.json`。

1. **Route**（路径匹配 HttpApi 的 `/api/{ModuleName}/`）：

```json
"ordering": {
  "ClusterId": "ordering",
  "Match": { "Path": "/api/ordering/{*any}" }
}
```

2. **Cluster**：`Address` 与 Host 端口一致，例如 `https://localhost:7005`。

**顺序**：`administration` 当前是 `{**catch-all}`。新路由必须写在它**前面**，否则请求会被 Administration 吃掉。

---

## 6. OpenIddict 种子

否则 Swagger 授权或经网关调用会失败。

文件：`src/shared/Meta.Dow.DbMigrator/appsettings.json`。

1. `ApiScope`、`ApiResource` 都加上 `MetaDowOrdering`（与 `OrderingApi` 常量相同）。
2. `Clients` 增加 Swagger 客户端，可照抄 `Projects_API`：
   - `ClientId`：`Ordering_API`
   - `ClientSecret`：`1q2w3e*`
   - `RootUrls` / Redirect：`https://localhost:7005/swagger/oauth2-redirect.html`
   - `Scopes` 至少包含 `MetaDowOrdering`；若调用 Identity / Administration，再补对应 scope
3. 已有前端客户端（如 `MetaDow_WebApp`）的 `Scopes` 也要加上 `MetaDowOrdering`，浏览器令牌才会带上该 audience。

同时：

- `MetaDowDbMigratorModule` 增加 `[DependsOn(typeof(OrderingMongoDbModule))]`，以及 Application.Contracts（若有权限定义或种子）
- DbMigrator 的 csproj 增加对应项目引用

改完种子后重新跑 DbMigrator（Aspire 启动时会执行）。已有库不会自动更新 OpenIddict 客户端时，需要清库或清空 OpenIddict 相关集合后再种子。

---

## 7. 联调检查清单

1. `dotnet build Meta.Dow.slnx`
2. `dotnet run --project src/apps/Meta.Dow.AppHost`
3. 新 Host Swagger：`https://localhost:7005`，Authorize 使用 `Ordering_API`
4. 经网关：`https://localhost:7500/api/ordering/...`
5. **401**：核对 Audience、Scope、种子客户端
6. **404**：核对网关路径，以及 catch-all 路由顺序

默认管理员账号与 ABP 一致：`admin` / `1q2w3E*`。

---

## 不必每次都做的

- 不必新建 AuthServer、Gateway、Redis、RabbitMQ。
- 不必为每个服务单独起 Mongo 容器；Aspire 里同一个 Mongo 再 `AddDatabase` 即可。
- 开源 CLI **没有**「向现有解决方案追加微服务」的向导；本仓库的做法就是复制 `projects` 再改接线。

业务实体仍按 ABP：Domain 聚合 → MongoDB 集合 / `AddMongoDbContext` → AppService 与 DTO → HttpApi（或自动 API）。那是服务**内部**增量，与「接入微服务网格」分开进行。
