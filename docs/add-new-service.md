# 添加新微服务

> **文档性质：操作手册**。按顺序执行每一步即可把一个新微服务接入本仓库的微服务网格。
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）。实测 Release 构建 0 错误、单测 62/62 通过。
> **每一步末尾都有「✅ 如何验证这一步成功」**——验不过就不要进入下一步。
>
> 下文统一用新服务名 **`Ordering`** 举例。
>
> 本仓库的接线顺序（Architecture）：
> `MetaDowNames` 常量 → 各层项目 → Host 本地配置 → Aspire AppHost → 网关 YARP → OpenIddict 种子 → DbMigrator → （可选）Dockerfile。

---

## 1 前置条件

真正需要的东西，逐项说明：

| 需要 | 为什么 | 来源 / 依据 |
| --- | --- | --- |
| **.NET SDK 10.0.401** | 全部项目 `TargetFramework=net10.0` | `global.json`（`rollForward: latestFeature`，更高补丁版本的 10.0.x SDK 也可） |
| **Docker（可运行容器）** | Aspire 以容器方式拉起 MongoDB、RabbitMQ、Redis、Seq（另含 Mongo Express / Redis Commander）；仅 MongoDB 挂数据卷 | `src/apps/Meta.Dow.AppHost/Program.cs:18-21` |
| **PowerShell（`powershell` 命令在 PATH）** | AppHost 以 `powershell -File tools/mongo-restore.ps1` 拉起 `mongo-restore` 资源，5 个服务与 DbMigrator 都会等它完成 | `src/apps/Meta.Dow.AppHost/Program.cs:29-40` |
| **Node.js** | **仅**在需要重新生成 AuthServer 前端静态资源时才要（`src/apps/Meta.Dow.AuthServer` 下已有 `node_modules` 与 `wwwroot/libs`，属 `abp install-libs` 产物） | `src/apps/Meta.Dow.AuthServer/node_modules`、`src/apps/Meta.Dow.AuthServer/wwwroot/libs` |
| **ABP CLI** | **不需要**。本仓库既有项目已生成完毕，新服务靠复制现有服务，不跑 `abp new` | 见「11 不必每次都做的」 |

**不需要**：EF Core、PostgreSQL、Makefile、Blazor、`Tasky.*` 相关的一切——本仓库**没有**这些东西。

> ⚠️ 已知环境坑：`dotnet build` / `dotnet restore` 会使用机器上的 NuGet 缓存目录（本机为 `E:\nuget-cache`、`E:\nuget-packages`，见用户级 `%APPDATA%\NuGet\NuGet.Config`）。若这些目录不可写（例如受限沙箱、权限变更），`dotnet build` 会**以非零码退出但只打印「0 个警告 0 个错误」**，不给出任何错误行。遇到这种现象先确认 NuGet 缓存目录可写。

**✅ 如何验证这一步成功**

```powershell
dotnet --version                 # 应为 10.0.401（或 10.0.x）
docker info                      # 能连上 Docker 守护进程
powershell -NoProfile -Command "Get-Command powershell"   # 能找到
```

---

## 2 步骤 1 复制分层项目

以 `src/services/projects/` 为模板整棵目录复制为 `src/services/ordering/`（模板服务的目录布局是
`host/<项目>` + `src/<各层项目>`，见 `Meta.Dow.slnx` 的 `/src/services/projects/` 分组）。

按层改名，**一个服务共 8 个 csproj**，与 `src/services/projects/` 完全同构：

| 层 | 目录 | 项目名 |
| --- | --- | --- |
| Domain.Shared | `src/services/ordering/src/` | `Meta.Dow.Ordering.Domain.Shared` |
| Domain | `src/services/ordering/src/` | `Meta.Dow.Ordering.Domain` |
| Application.Contracts | `src/services/ordering/src/` | `Meta.Dow.Ordering.Application.Contracts` |
| Application | `src/services/ordering/src/` | `Meta.Dow.Ordering.Application` |
| HttpApi | `src/services/ordering/src/` | `Meta.Dow.Ordering.HttpApi` |
| HttpApi.Client | `src/services/ordering/src/` | `Meta.Dow.Ordering.HttpApi.Client` |
| MongoDB（持久层） | `src/services/ordering/src/` | `Meta.Dow.Ordering.MongoDB` |
| Host（入口） | `src/services/ordering/host/` | `Meta.Dow.Ordering.HttpApi.Host` |

1. 全局替换命名空间 `Meta.Dow.Projects` → `Meta.Dow.Ordering`，模块类 `ProjectsXxxModule` → `OrderingXxxModule`
   （例如 `ProjectsMongoDbModule` → `OrderingMongoDbModule`）。
2. 把 8 个 csproj 加进解决方案 `Meta.Dow.slnx`。这是新式 XML 解决方案，`dotnet sln` 可用：

   ```powershell
   dotnet sln Meta.Dow.slnx add src/services/ordering/src/Meta.Dow.Ordering.Domain.Shared/Meta.Dow.Ordering.Domain.Shared.csproj
   # ……其余 7 个同理，或手动按 Meta.Dow.slnx 里 projects 分组的样子补 <Folder>/<Project Path>
   ```

3. **包版本一律不写进 csproj**：本仓库启用 CPM（`Directory.Packages.props`，`ManagePackageVersionsCentrally=true`），
   `PackageReference` 只写 `Include`，**不写 `Version`**。复制的 csproj 顺带改名时不要引入任何 `Version=`。
4. 先改 `RootNamespace` / 命名空间，**再**处理路由（步骤 6 与下面的说明）。

**关于路由前缀（重要，别照抄旧说法）**

HTTP 路由前缀**不是**由 `RemoteServiceConsts.ModuleName` 自动生成的。看真实代码：

- `src/services/projects/src/Meta.Dow.Projects.Application.Contracts/ProjectsRemoteServiceConsts.cs:7`
  → `ModuleName = "projects"`
- `src/services/projects/src/Meta.Dow.Projects.HttpApi/Samples/SampleController.cs:8-10`
  → `[Area(ProjectsRemoteServiceConsts.ModuleName)]` + `[Route("api/Projects/sample")]`

即 `ModuleName` 只作 MVC **Area** 名，真正决定对外路径的是控制器上的 `[Route("api/...")]`。
所以新服务的控制器应照抄写成 `[Route("api/Ordering/...")]`（大小写不影响匹配，ASP.NET Core 路由与
YARP 路径匹配默认都不区分大小写，现有网关路由 `/api/projects/{*any}` 对应的控制器前缀就是 `api/Projects`）。

**✅ 如何验证这一步成功**

```powershell
dotnet build src/services/ordering/host/Meta.Dow.Ordering.HttpApi.Host/Meta.Dow.Ordering.HttpApi.Host.csproj -c Release
# 期望：生成成功，0 错误
Select-String -Path Meta.Dow.slnx -Pattern "Meta.Dow.Ordering"   # 应能列出 8 行 Project Path
```

---

## 3 步骤 2 登记名字与数据库（MetaDowNames.cs）

文件：`src/shared/Meta.Dow.Shared/Microsoft/Extensions/Hosting/MetaDowNames.cs`（30 行，所有服务/库名的唯一出处）。

按现有命名规律（`XxxApi` = `MetaDowXxx`、`XxxDb` = `MetaDowXxxDb`）追加两个常量：

```csharp
public const string OrderingApi = "MetaDowOrdering";   // JWT Audience / Aspire 资源名
public const string OrderingDb  = "MetaDowOrderingDb"; // MongoDB 库名 = 连接字符串名
```

既有值供对照（`MetaDowNames.cs:12-29`）：`MetaDowAdministration`/`MetaDowAdministrationDb`、
`MetaDowAuthServer`/`MetaDowAuthServerDb`、`MetaDowIdentityService`/`MetaDowIdentityServiceDb`、
`MetaDowProjects`/`MetaDowProjectsDb`、`MetaDowSaaS`/`MetaDowSaaSDb`，以及
`mongodb`/`rabbitmq`/`redis`/`seq`/`MetaDowDbMigrator`/`MetaDowGateway`。

然后在新服务的 MongoDB 模块里接线（照抄 `src/services/projects/src/Meta.Dow.Projects.MongoDB/ProjectsMongoDbModule.cs:13-26`）：

```csharp
[ConnectionStringName(MetaDowNames.OrderingDb)]      // OrderingDbContext / IOrderingDbContext 上都要有
Configure<AbpDbConnectionOptions>(options => options.Databases.Configure(MetaDowNames.OrderingDb, db => { }));
context.Services.AddMongoDbContext<OrderingDbContext>(options => options.AddDefaultRepositories(true));

// 本仓库 Mongo 是单节点、非副本集，ABP 的 UoW 默认事务必须关掉
Configure<AbpUnitOfWorkDefaultOptions>(options => options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);
```

`TransactionBehavior = Disabled` 是**每个**服务的 MongoDB 模块都设的，4 个现有服务无一例外：
`AdministrationMongoDbModule.cs:27`、`IdentityServiceMongoDbModule.cs:23`、`ProjectsMongoDbModule.cs:19`、`SaaSMongoDbModule.cs:27`。
（Aspire 单节点 Mongo 没有跨文档事务；`Meta.Dow.ServiceDefaults` 还会给所有 `MetaDow*Db` 连接串补 `retryWrites=false`，
见 `src/shared/Meta.Dow.ServiceDefaults/Microsoft/Extensions/Hosting/Extensions.cs` 的 `ConfigureMongoConnectionStrings`。）

Host 里必须用同一个 API 名调用 `ConfigureMicroservice`（`ProjectsHttpApiHostModule.cs:33` 是现成例子）：

```csharp
context.ConfigureMicroservice(MetaDowNames.OrderingApi);
```

`ConfigureMicroservice` 内部做的事（`src/shared/Meta.Dow.Microservice.Shared/Meta.Dow/MetaDowMicroserviceModule.cs:59-123`）：
JWT Bearer（`Audience = 传入的名字`）、分布式缓存 KeyPrefix、DataProtection、Swagger OAuth —— **Audience 对不上就 401**。

**✅ 如何验证这一步成功**

```powershell
Select-String -Path src/shared/Meta.Dow.Shared/Microsoft/Extensions/Hosting/MetaDowNames.cs -Pattern "Ordering"
Select-String -Path src/services/ordering/host/Meta.Dow.Ordering.HttpApi.Host/ProjectsHttpApiHostModule.cs -Pattern "ConfigureMicroservice"
# 期望：OrderingApi / OrderingDb 各出现一次，Host 传的就是 MetaDowNames.OrderingApi
```

---

## 4 步骤 3 Host 本地配置（真实端口、appsettings、secrets）

对照 `src/services/projects/host/Meta.Dow.Projects.HttpApi.Host/`。

### 4.1 `Properties/launchSettings.json`

必须有一个名为 **`Aspire`** 的 profile —— AppHost 用 `launchProfileName: "Aspire"` 启动各资源
（`src/apps/Meta.Dow.AppHost/Program.cs:14`）。

**现有端口占用实况**（逐文件核对 `Properties/launchSettings.json`）：

| 组件 | 本地 HTTPS | 出处 |
| --- | --- | --- |
| Aspire AppHost（Dashboard） | `https://localhost:17166`（http `15124`） | `src/apps/Meta.Dow.AppHost/Properties/launchSettings.json` |
| Gateway | `https://localhost:7500`（http `7501`） | `src/gateway/Meta.Dow.Gateway/Properties/launchSettings.json` |
| AuthServer | `https://localhost:7600` | `src/apps/Meta.Dow.AuthServer/Properties/launchSettings.json` |
| Administration | `https://localhost:7001` | `...Meta.Dow.Administration.HttpApi.Host/Properties/launchSettings.json` |
| IdentityService | `https://localhost:7002` | `...Meta.Dow.IdentityService.HttpApi.Host/Properties/launchSettings.json` |
| SaaS | `https://localhost:7003` | `...Meta.Dow.SaaS.HttpApi.Host/Properties/launchSettings.json` |
| Projects | `https://localhost:7004` | `...Meta.Dow.Projects.HttpApi.Host/Properties/launchSettings.json` |

所以新服务用 **7005** 是对的（7001–7004 已占）。但**必须自己确认 7005 在本机空闲**：

```powershell
Get-NetTCPConnection -LocalPort 7005 -ErrorAction SilentlyContinue   # 无输出 = 未被占用
```

新 profile 长这样（照抄 Projects 的 `Aspire` profile 写法）：

```json
"Aspire": {
  "commandName": "Project",
  "launchBrowser": false,
  "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" },
  "applicationUrl": "https://localhost:7005"
}
```

> 现有 profile 的 `launchBrowser` 不统一（Projects/Administration 是 `true`，Identity/SaaS/AuthServer/Gateway 是 `false`）——
> 新服务建议 `false`，与多数一致。

### 4.2 `appsettings.json`

Projects 的完整配置（`src/services/projects/host/Meta.Dow.Projects.HttpApi.Host/appsettings.json`）只有 4 段，
逐键照抄改值即可：

| 键 | 值 | 说明 |
| --- | --- | --- |
| `AuthServer:Authority` | `https://localhost:7600/` | **末尾必须带 `/`**（4 个 Host 与 Gateway 都是这个写法） |
| `AuthServer:RequireHttpsMetadata` | `"false"` | 本地自签证书 |
| `AuthServer:SwaggerClientId` | `Ordering_API` | 必须与步骤 7 种子的 `ClientId` 完全一致 |
| `RabbitMQ:EventBus:ClientName` | `MetaDow_Ordering` | 每个服务唯一（现有：`MetaDow_Administration`/`MetaDow_Identity`/`MetaDow_Projects`/`MetaDow_SaaS`） |
| `RabbitMQ:EventBus:ExchangeName` | `Meta.Dow` | 全仓库统一，**不要改** |
| `App:CorsOrigins` | 逗号分隔 | 至少含 `https://localhost:7500`（网关）、`https://localhost:7600`（AuthServer）、`http://localhost:5666`（Vue 前端）与自身 `https://localhost:7005` |

**注意**：现有 Projects `appsettings.json:3` 的 `App:CorsOrigins` **已经包含 `https://localhost:7005`**（模板复制来源遗留），
但 `appsettings.json:20-21` 的 `RemoteServices:IdentityService:BaseUrl` 写的是 `https://localhost:7004/` —— 与 IdentityService 真实端口 7002 不符。
新服务如果不需要 ABP 客户端代理就别抄这两段；需要的话，`IdentityService` 的 `BaseUrl` 要写 `https://localhost:7002/`。

### 4.3 `appsettings.secrets.json.example`（必须一起提交）

明文密钥**不进仓库**。根 `.gitignore` 已忽略 `**/appsettings.secrets.json`；仓库只提交
`appsettings.secrets.json.example` 模板（4 个 Host 都有）。

Host 启动时自动加载同目录的可选文件：`builder.Host.AddAppSettingsSecretsJson()`（`ProjectsHttpApiHostModule` 所在 Host 的
`Program.cs:27`；5 个 Host + AuthServer + DbMigrator 全部调用）。

Projects 的模板内容就是：

```json
{
  "_comment": "复制本文件为 appsettings.secrets.json 并填写真实值。请勿把 appsettings.secrets.json 提交到仓库。",
  "AuthServer": {
    "SwaggerClientSecret": "<Swagger 客户端密钥，须与 DbMigrator 的 Clients[].ClientSecret 一致>"
  }
}
```

**铁律：`appsettings.secrets.json` 里永远不要出现 JSON 数组。**
.NET 配置对数组是**按索引**合并的，会造成客户端列表「错位混血」（2026-10-10 事故：`MetaDow_Vue` 消失 → 登录
`invalid_request / ID2052`）。该约束由 `tests/Meta.Dow.Orchestration.Tests/SecretsConfigSafetyTests.cs` 强制
（扫描全仓库 `appsettings*.secrets.json*`，含 `*.example`）。需要按键覆盖时用**对象**，例如
`"ClientSecrets": { "Ordering_API": "<secret>" }`。

### 4.4 Host 模块依赖

新 Host 模块的 `[DependsOn]` 应包含（对照 `ProjectsHttpApiHostModule.cs:17-26`）：

- `OrderingApplicationModule`、`OrderingMongoDbModule`、`OrderingHttpApiModule`
- `MetaDowMicroserviceModule`（JWT/CORS/Swagger）
- `MetaDowServiceDefaultsModule`（健康检查/Otel/服务发现）
- 若要读写共享的权限/设置/审计库：`AbpAuditLoggingMongoDbModule`、`AbpPermissionManagementMongoDbModule`、
  `AbpSettingManagementMongoDbModule`、`AbpTenantManagementMongoDbModule`
  （这些 ABP 库映射在 Administration / Identity / SaaS 的库里，见步骤 4.5）
- `AbpAspNetCoreMvcUiMultiTenancyModule`（现有 Host 都带，配合 `ConfigureMicroservice` 的多租户）

Host 的 `Program.cs` 三件套不要漏：`builder.AddServiceDefaults()`、`builder.AddSharedEndpoints()`、`app.MapDefaultEndpoints()`。

### 4.5 连接字符串从哪来

**本地没有任何 `ConnectionStrings` 写在 appsettings 里**——全部由 Aspire 通过 `WithReference(<db>)` 注入，
键名就是 `AddDatabase(<MetaDowNames.XxxDb>)` 用的那个名字，与 `[ConnectionStringName(...)]` 一一对应。
脱离 Aspire 单跑（`dotnet run`）时你必须自备 Mongo/RabbitMQ/Redis 并自己提供连接串。
共享库映射（同库多服务，属有意设计，不是越界）：

- `AbpIdentity` + `AbpOpenIddict` → `MetaDowIdentityServiceDb`（`IdentityServiceMongoDbModule.cs:28-36`）
- `AbpAuditLogging`/`AbpFeatureManagement`/`AbpPermissionManagement`/`AbpSettingManagement` → `MetaDowAdministrationDb`（`AdministrationMongoDbModule.cs:32-42`）
- `AbpTenantManagement` → `MetaDowSaaSDb`（`SaaSMongoDbModule.cs:31`）

**✅ 如何验证这一步成功**

```powershell
Get-Content src/services/ordering/host/Meta.Dow.Ordering.HttpApi.Host/Properties/launchSettings.json   # 有 Aspire profile + 7005
Get-Content src/services/ordering/host/Meta.Dow.Ordering.HttpApi.Host/appsettings.json                  # Authority 末尾有 /
# 且 secrets 模板不含任何数组
dotnet test tests/Meta.Dow.Orchestration.Tests --filter "FullyQualifiedName~SecretsConfigSafetyTests"
# 期望：1 个测试通过
```

---

## 5 步骤 4 接入 Aspire（AppHost/Program.cs + csproj）

涉及文件：

- `src/apps/Meta.Dow.AppHost/Program.cs`
- `src/apps/Meta.Dow.AppHost/Meta.Dow.AppHost.csproj`

1. **csproj** 加一条项目引用（当前 6 个 `ProjectReference` 指向 AuthServer / Gateway / 4 个 Host / DbMigrator）：

   ```xml
   <ProjectReference Include="..\..\services\ordering\host\Meta.Dow.Ordering.HttpApi.Host\Meta.Dow.Ordering.HttpApi.Host.csproj" />
   ```

2. 在 `var projectsDb = mongo.AddDatabase(MetaDowNames.ProjectsDb);` 之后加数据库：

   ```csharp
   var orderingDb = mongo.AddDatabase(MetaDowNames.OrderingDb);
   ```

3. `migrator` 上加 `.WithReference(orderingDb)`（`Program.cs:43-51` 那一串）。
4. 加资源。**类型名由 Aspire 从项目名生成，把 `.` 换成 `_`**，所以是 `Meta_Dow_Ordering_HttpApi_Host`
   （现有：`Meta_Dow_Projects_HttpApi_Host`，`Program.cs:99`）。照抄 Projects 那段（`Program.cs:98-111`）：

   ```csharp
   builder
       .AddProject<Meta_Dow_Ordering_HttpApi_Host>(MetaDowNames.OrderingApi, launchProfileName: LaunchProfileName)
       .WithExternalHttpEndpoints()
       .WithReference(adminDb)        // 需要共享权限/设置库时才加
       .WithReference(orderingDb)
       .WithReference(rabbitMq)
       .WithReference(redis)
       .WithReference(seq)
       .WaitFor(rabbitMq)
       .WaitFor(redis)
       .WaitForCompletion(migrator);
   ```

   `WithExternalHttpEndpoints()` 不可省——没有它网关/浏览器访问不到。
5. Gateway 与 AuthServer 目前只 `.WithReference(seq).WaitForCompletion(migrator)`（`Program.cs:113-117`、`119-130`）。
   若希望网关在新服务起来后再启动，在 Gateway 上补 `.WaitFor(<ordering 资源>)` —— 需要把上面的 `builder.AddProject<...>` 结果
   赋给变量（Projects 那段现在没赋值，要改）。

> `Program.cs:12` 的 `DisableAspireIdeRunSession()` 会清空 `DEBUG_SESSION_*` 环境变量，这是**有意为之**（本机 IDE 调试桥超时会让子进程以空参数启动并卡满 300s），不要删。

**✅ 如何验证这一步成功**

```powershell
dotnet build src/apps/Meta.Dow.AppHost/Meta.Dow.AppHost.csproj -c Release
# 期望：生成成功。若报找不到 Meta_Dow_Ordering_HttpApi_Host，说明引用还没生效/类型名拼错
dotnet run --project src/apps/Meta.Dow.AppHost    # Dashboard 里应出现 ordering 资源
```

---

## 6 步骤 5 网关 YARP

文件：`src/gateway/Meta.Dow.Gateway/appsettings.json`（Routes 在 `:10-90`，Clusters 在 `:91-142`）。

1. **Route**——匹配路径要与控制器 `[Route("api/Ordering/...")]` 的前缀一致：

   ```json
   "ordering": {
     "ClusterId": "ordering",
     "Match": { "Path": "/api/ordering/{*any}" }
   }
   ```

2. **Cluster**——`Address` 用 Host 的本地 HTTPS 端口：

   ```json
   "ordering": {
     "Destinations": { "ordering": { "Address": "https://localhost:7005" } },
     "HttpRequest": { "ActivityTimeout": "00:00:30" }
   }
   ```

3. **顺序**：`administration-fallback` 是 `{**catch-all}`，且是**唯一**挂了 `"AuthorizationPolicy": "Default"` 的路由
   （`appsettings.json:83-89`）。新路由必须写在它**前面**，否则请求被 Administration 吃掉。

### 网关鉴权的现状（别照抄旧说法）

- 网关**只在 `administration-fallback` 这一条**要求认证（`AuthorizationPolicy: "Default"`）。
  其余业务路由（`/api/projects/*`、`/api/orchestration/*` …）**不挂授权策略**，即匿名可达，鉴权由下游服务自己把关。
  这是现状，不是笔误——要改就得单独决策。
- `Default` 策略的定义在 `src/gateway/Meta.Dow.Gateway/Program.cs:76-78`：`RequireAuthenticatedUser()`。
- 网关 JWT 校验**默认不校验 Audience**（`Program.cs:48-57`）：`AuthServer:ValidAudiences` 为空数组，
  于是 `ValidateAudience = false`，受众交给下游服务用各自的 `MetaDowNames.XxxApi` 把关。
  **所以"经网关 401"通常不是网关造成的，而是下游服务的 Audience 判定。**

**✅ 如何验证这一步成功**

```powershell
dotnet run --project src/gateway/Meta.Dow.Gateway
# 另开一个窗口：
curl.exe -s -o NUL -w "%{http_code}" https://localhost:7500/api/ordering/sample        # 期望 401/200 而非 404
curl.exe -s -o NUL -w "%{http_code}" https://localhost:7500/api/ordering/sample/authorized
# 期望：404 说明网关路由没生效；502/503 说明路由通了但后端没起
```

---

## 7 步骤 6 OpenIddict 种子

文件：`src/shared/Meta.Dow.DbMigrator/appsettings.json`；逻辑在同目录 `OpenIddictDataSeeder.cs`。
不配这一步，Swagger 授权与经网关的调用都会失败。

1. **`ApiResource` 加 `MetaDowOrdering`**（与 `MetaDowNames.OrderingApi` 同值）。

   ⚠️ **必须加，否则新服务对任何有效令牌都返回 401。** seeder 只读 `ApiResource`
   （`OpenIddictDataSeeder.cs:189`：`_configuration.GetSection("ApiResource")`），
   `CreateApiResourceAsync` 会用这个名字创建 OpenIddict **Scope**，并把 `Resources = { name }`
   写进 scope（`:199-211`）。下游服务的 `options.Audience = MetaDowNames.XxxApi`（`MetaDowMicroserviceModule.cs:89`）
   校验的是令牌 `aud`，而 `aud` 来自 scope 关联的资源名。**注册表里没有该名字 ⇒ 令牌里就没有该 audience ⇒ 401。**

   现状（`src/shared/Meta.Dow.DbMigrator/appsettings.json:9-14`）只有 4 项：
   `MetaDowAdministration`、`MetaDowAuthServer`、`MetaDowIdentityService`、`MetaDowSaaS`。
   **`MetaDowProjects` 不在其中**，而 `MetaDowNames.ProjectsApi = "MetaDowProjects"`
   （`MetaDowNames.cs:25`）并要求 Host 以它为 audience —— 这就是 Projects 服务当前对所有有效令牌返回 401 的**根因**。
   新增服务时把 `MetaDowOrdering` 加进去，顺手把 `MetaDowProjects` 也补上。

2. **`ApiScope` 键从未被读取。** `appsettings.json:3-8` 有 `ApiScope` 数组，但 `OpenIddictDataSeeder.cs` 里
   `ApiScope` 字面量**一次都没出现**（全仓库仅此一处配置、零处读取）。加不加它都不影响行为；
   真正生效的是 `ApiResource`。建议仍同步维护以免误导，但不要以为写了 `ApiScope` 就注册了 scope。

3. **`Clients` 加 Swagger 客户端**，照抄 `Projects_API`（`appsettings.json:90-113`）：

   ```json
   {
     "ClientId": "Ordering_API",
     "RootUrls": [ "https://localhost:7005" ],
     "Scopes": [ "MetaDowOrdering" ],
     "GrantTypes": [ "authorization_code" ],
     "RedirectUris": [ "https://localhost:7005/swagger/oauth2-redirect.html" ],
     "PostLogoutRedirectUris": [ "https://localhost:7005" ]
   }
   ```

   - `ClientSecret` **不要写在这里**，写到 git-ignored 的 `appsettings.secrets.json` 的
     `ClientSecrets:{clientId}` 下（`OpenIddictDataSeeder.cs:63-80` 按键合并；secrets 里出现数组会踩上述事故）。
     自行生成强随机串，不要复用示例值。
   - `Scopes` 至少含 `MetaDowOrdering`；若该服务还要调 Identity/Administration，再补 `MetaDowIdentityService`/`MetaDowAdministration`。
   - **`AllowedCorsOrigins` 是死配置**：模型 `Model/ServiceClient.cs:19` 有这个属性，但 `OpenIddictDataSeeder.cs`
     从不使用它（`AllowedCorsOrigins` 在全仓库仅出现这一次声明）。写了没效果；CORS 由各服务
     `App:CorsOrigins` 与 `MetaDowMicroserviceModule.ConfigureCors` 决定。
   - `PostLogoutRedirectUris` / `RedirectUris` 必须是绝对且合法的 URI，否则 seeder 抛 `BusinessException`（`:441-447`、`:463-469`）。

4. **已有前端/其它客户端要访问新服务时，也要把 scope 加进它们的 `Scopes`**。现状：`MetaDow_Vue`
   （`appsettings.json:16-43`）的 Scopes 是 `MetaDowAuthServer`/`MetaDowSaaS`/`MetaDowIdentityService`/`MetaDowAdministration`
   —— 注意它**也没有** Projects 的 scope。浏览器令牌要带上新 audience，就得在这里补 `MetaDowOrdering`。
5. **DbMigrator 侧接线**：
   - `MetaDowDbMigratorModule` 加 `[DependsOn(typeof(OrderingMongoDbModule))]`，需要权限/种子时再加
     `OrderingApplicationContractsModule`（现有 4 个服务就是这么成对写的，`MetaDowDbMigratorModule.cs:21-28`）。
   - `Meta.Dow.DbMigrator.csproj` 加对应 `ProjectReference`（现有 8 条服务引用，`:18-31`）。
6. 重跑 DbMigrator（Aspire 启动时会自动执行）。

> 提示：`PruneObsoleteClients` 当前为 `true`（`appsettings.json:2`），配置里**没登记**的客户端会被**软删除**并打 Warning
> （`OpenIddictDataSeeder.cs:164-185`）。所以「先把客户端从配置里删干净再改」这种做法很危险。
> 另外 `OpenIddictDataSeeder` 是**配置即事实**：每次跑都会清空并按配置重建 Permissions/RedirectUris（`:309-322`），
> 数据库里手工改的会被覆盖回去。
> OpenIddict 的 applications/scopes/tokens 存在 **Identity 库**（`AbpOpenIddict` → `MetaDowIdentityServiceDb`，
> `IdentityServiceMongoDbModule.cs:34`）。只清 OpenIddict 相关集合或直接删库重建即可重新种子。

**✅ 如何验证这一步成功**

```powershell
Select-String -Path src/shared/Meta.Dow.DbMigrator/appsettings.json -Pattern "MetaDowOrdering"
# 期望：ApiResource 与 Clients 里都能看到
dotnet run --project src/shared/Meta.Dow.DbMigrator    # 期望：日志无异常，跑完退出
```

---

## 8 步骤 7 纳管数据库迁移（DbMigrator 依赖各服务 MongoDbModule）

**本仓库没有迁移。** 没有 EF Core、没有 `Migrations` 目录、没有 `dotnet ef`。持久层是
`{Service}.MongoDB`，集合结构由 Mongo 驱动 + 显式索引 + 应用层数据修正来演进。

DbMigrator 的职责只有两件：

1. `MetaDowDbMigratorModule` 依赖每个服务的 `XxxMongoDbModule` —— 这样各服务的 `AbpDbConnectionOptions`
   映射、`AddMongoDbContext`、ABP 的权限/设置/Feature/租户仓储才会在迁移进程里被注册。
   **新服务不加这个依赖，DbMigrator 就看不到它的上下文**（现有 4 个：`MetaDowDbMigratorModule.cs:21-28`）。
2. `MetaDowDbMigrationService.MigrateAsync` 对 host 与每个租户跑 `IDataSeeder`，密码取自
   `IdentityDataSeedContributor.AdminPasswordDefaultValue`（`MetaDowDbMigrationService.cs:41-59`）。

**索引要自己建，并且在 `MongoDbModule` 里建。** 现成范例是 SaaS：
`src/services/saas/src/Meta.Dow.SaaS.MongoDB/SaaSMongoDbModule.cs:44-50` 在
`OnApplicationInitializationAsync` 里调 `EnsureIndexesAsync`，内部用
`collection.Indexes.CreateOneAsync(new CreateIndexModel<T>(keys, options))`（`:91-99`），
失败只告警、不阻断启动。

- 需要唯一键（如 `Code`）时必须在这里显式建索引 —— ABP 集合默认**零显式索引**，靠 `AnyAsync` 预检挡不住并发重复。
- 多租户实体的唯一索引必须带上 `TenantId`（`TenantId + Code`），否则两个租户会撞同一个 code。

**✅ 如何验证这一步成功**

```powershell
Select-String -Path src/shared/Meta.Dow.DbMigrator/MetaDowDbMigratorModule.cs -Pattern "OrderingMongoDbModule"
Select-String -Path src/services/ordering/src/Meta.Dow.Ordering.MongoDB/OrderingMongoDbModule.cs -Pattern "EnsureIndexesAsync|CreateOneAsync"
# 期望：两处都能命中；跑一次 DbMigrator 后到 Mongo 里 db.<集合>.getIndexes() 能看到自建索引
```

---

## 9 步骤 8 容器化（Dockerfile 模板 —— 注意下面那条坑）

仓库有 **7 个 Dockerfile**（AuthServer、Gateway、DbMigrator、4 个 Host），位置就在各项目目录下，例如
`src/services/projects/host/Meta.Dow.Projects.HttpApi.Host/Dockerfile`。

**所有 7 个当前都构建不了。** 阶段名是 `build` / `final`，但最终阶段写的是：

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .      # ← publish 不是阶段名；Docker 会当作外部镜像名去解析并失败
```

实例：`src/gateway/Meta.Dow.Gateway/Dockerfile:11`、`src/services/projects/host/Meta.Dow.Projects.HttpApi.Host/Dockerfile:11`。
**新服务照抄模板时必须改成 `COPY --from=build /app/publish .`。**

可用的模板（已按上述修正）：

```dockerfile
# 多阶段构建。构建上下文必须是仓库根目录：
#   docker build -f src/services/ordering/host/Meta.Dow.Ordering.HttpApi.Host/Dockerfile -t meta-dow:meta.dow.ordering.httpapi.host .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet publish "src/services/ordering/host/Meta.Dow.Ordering.HttpApi.Host/Meta.Dow.Ordering.HttpApi.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Meta.Dow.Ordering.HttpApi.Host.dll"]
```

另外三点：

- csproj 里的 `<DockerfileContext>` 要指向仓库根（Host 是 `..\..\..\..`，DbMigrator 是 `..\..`）——
  因为 `COPY . .` 需要整个仓库来 restore 全部项目引用。
- 容器内监听 **8080**（`EXPOSE 8080`），**不是** launchSettings 里的 7005。
  若让容器走网关，网关 Cluster 的 `Address` 要按部署方式另行确认。
- **CI 不构建镜像**：`.github/workflows/ci.yml` 只有 restore → build Release → test → 上传 trx
  （ubuntu-latest），**没有 `docker build` 步骤**。所以镜像坏了不会被流水线拦住，得自己构建验证。

**✅ 如何验证这一步成功**

```powershell
docker build -f src/services/ordering/host/Meta.Dow.Ordering.HttpApi.Host/Dockerfile -t meta-dow:ordering .
# 期望：构建成功。若报 "failed to resolve source metadata for docker.io/library/publish" 就是漏改 --from=build
```

---

## 10 步骤 9 联调检查清单

1. 构建 + 测试：

   ```powershell
   dotnet build Meta.Dow.slnx -c Release        # 期望 0 错误
   dotnet test Meta.Dow.slnx -c Release         # 期望 62/62 通过
   ```

2. 起全栈：`dotnet run --project src/apps/Meta.Dow.AppHost`（Dashboard `https://localhost:17166`）。
   在 Dashboard 里确认：mongo → mongo-restore → migrator → 各服务 的启动顺序，以及新资源状态为 Running。
3. **健康端点**：`/health`（全部检查）与 `/alive`（liveness，只跑 tag 为 `live` 的检查）
   由 `MapDefaultEndpoints()` 映射，且**在所有环境**都映射（`src/shared/Meta.Dow.ServiceDefaults/Microsoft/Extensions/Hosting/Extensions.cs:154-177`，报告体 `WriteReportAsync` 在 `:179-201`）。
   调用点共 **6 处**：4 个业务 Host（Administration、Identity、Projects、SaaS）+ AuthServer + Gateway 的 `Program.cs`
   （`MapDefaultEndpoints` 调用行：Administration`:32`、Identity`:31`、Projects`:32`、SaaS`:32`、AuthServer`:33`、Gateway`:116`）。
   ⚠️ DbMigrator 只调用 `AddServiceDefaults()`（`src/shared/Meta.Dow.DbMigrator/Program.cs:13`），**不映射**健康端点。
   返回 JSON；**生产环境只返回聚合状态字符串，不返回逐项明细**（开发环境才有 `checks[]` 明细）。

   ```powershell
   curl.exe -s https://localhost:7005/alive     # 期望 "Healthy"
   curl.exe -s https://localhost:7005/health    # 期望 "Healthy"
   ```

4. **Swagger**：`https://localhost:7005`，Authorize 用 `Ordering_API`（`SwaggerClientId` 与 seed 的 `ClientId` 必须一致）。
5. **经网关**：`https://localhost:7500/api/ordering/...`。
6. **令牌受众**：拿一个令牌看 `aud`（jwt.ms / jwt.io）—— 里面应出现 `MetaDowOrdering`。
   没有就是步骤 7 的 `ApiResource` 没加。
7. **排错对照**：

   | 现象 | 先查 |
   | --- | --- |
   | **401** | 服务端 `aud` 是否等于 `MetaDowNames.OrderingApi`；`ApiResource` 是否登记了该名字；种子客户端 `Scopes` 是否含它；令牌是否过期 |
   | **403** | 授权策略/ABP 权限（网关 `Default` 策略只要求已认证） |
   | **404** | 网关路由前缀与控制器 `[Route("api/...")]` 是否一致；新路由是否被 `{**catch-all}` 抢走（顺序） |
   | **502/503** | Cluster `Address` 端口是否与 `launchSettings.json` 一致；Host 是否已启动（`WaitForCompletion(migrator)`） |
   | **网关自身 401 但下游没问题** | 网关 `AuthServer:ValidAudiences` 是否被设成了不匹配的值 |
   | 登录 `invalid_request / ID2052` | 是否在 secrets 文件里写了 JSON 数组导致客户端被索引合并/软删 |

默认管理员账号与 ABP 一致：`admin` / `1q2w3E*`
（`src/services/identity/host/.../EventHandler/TenantCreatedEventHandler.cs:35` 与
`MetaDowDbMigrationService.cs:52-57` 使用的 ABP 默认值）。

---

## 11 不必每次都做的

- 不必新建 AuthServer、Gateway、Redis、RabbitMQ —— 都是共享的。
- 不必为每个服务单独起 Mongo 容器：Aspire 里同一个 Mongo 再 `AddDatabase` 即可（现有 4 个库共用 `mongodb`）。
- 不必写 EF Core 迁移 / `dotnet ef`：本仓库没有 EF Core、没有 PostgreSQL、没有迁移（见步骤 7）。
- 不必写 Makefile：仓库里没有（`make` 相关文件为零）。
- 不必跑 `abp new` / ABP CLI：开源 CLI **没有**「向现有解决方案追加微服务」的向导；
  本仓库的做法就是**复制 `projects` 再改接线**。
- 不必给每个服务单独配 Dockerfile 才能本地跑：本地走 Aspire。
- 不必手改 Gateway 的 `AuthServer:ValidAudiences`（保持 `[]`，受众由下游把关）。

---

## 12 已知缺陷 / 待清理项（低危，但别被它们带偏）

1. **`ApiResource` 缺 `MetaDowProjects` ⇒ Projects 服务对任何有效令牌返回 401**
   （`src/shared/Meta.Dow.DbMigrator/appsettings.json:9-14` vs `MetaDowNames.cs:25`）。新增服务时必须同时补自己的。
2. **`ApiScope` 是死配置**（`appsettings.json:3-8`，`OpenIddictDataSeeder.cs` 从不读取）。
3. **`AllowedCorsOrigins` 是死配置**（`Model/ServiceClient.cs:19`，seeder 从不使用）。
4. **7 个 Dockerfile 全部不可构建**：`COPY --from=publish` 应为 `--from=build`
   （如 `src/gateway/Meta.Dow.Gateway/Dockerfile:11`）。
5. **4 个失效的旧解决方案文件**：`src/services/{administration,identity,projects,saas}/Meta.Dow.*.sln`，
   内部指向不存在的 `Tasky.*` 项目与 `Tasky.Projects.EntityFrameworkCore`。**不要用它们构建**，
   权威解决方案只有仓库根的 `Meta.Dow.slnx`。同理 4 个 `src/services/*/.gitignore` 里残留 5 处 `Tasky` 路径。
6. **`src/reset-databases.ps1` / `.sh` 完全失效**：`Push-Location "shared\Tasky.DbMigrator"` 指向不存在的目录，
   且它设置的 `$env:RESET_DATABASES` **没有任何 C# 代码读取**。想重置库就删 Mongo 数据卷/库后重跑 DbMigrator。
7. **`tools/generate_from_tasky.py` 不要用**：硬编码他人机器路径（`D:\C#\abp-microservice\src`），且不在任何流水线中。
8. **`src/Directory.Build.props` 在 `src/` 下**（不在仓库根），只设 `LangVersion`/`Nullable`/
   `TreatWarningsAsErrors=false` + 2 个 global using；**分析器实际并未启用**（与 `src/CLAUDE.md` 中
   "Analyzers are enabled via Directory.Build.props" 的描述不符）。根目录**没有** `Directory.Build.props`。
9. **CI 不构建 Docker 镜像**（`.github/workflows/ci.yml`）。
10. **`Meta.Dow.ServiceDefaults` 里 `ConfigureMongoConnectionStrings` 硬编码了 5 个库名**
    （`Extensions.cs`，`AdministrationDb`/`IdentityServiceDb`/`ProjectsDb`/`SaaSDb`/`AuthServerDb`），
    新服务的库**不在其中 ⇒ 不会自动补 `retryWrites=false`**。用 `AddDatabase` 后若遇到 retryable write 报错，
    需要把这些常量加进该方法，或自行在连接串里加 `retryWrites=false`。

---

## 13 修订记录

| 日期 | 代码基线 | 改了什么 |
| --- | --- | --- |
| 2026-10-13 | `Meta.Dow.slnx` @ `267d375` | 按代码逐条核实并重写为操作手册： |
| | | **删/改的失真断言**：① 删除「ABP 10」这类无版本来源的表述，改为标明 ABP 10.6.0 / Aspire 13.5.3 / `MongoDB.Driver` 3.4.0 / SDK 10.0.401（`global.json`）。② 「`HttpApi` 中 `RemoteServiceConsts.ModuleName` 会成为路由前缀」→ 假：路由来自控制器 `[Route("api/Projects/...")]`，`ModuleName` 只是 MVC Area（`Samples/SampleController.cs:8-10`）。③ 「`ApiScope`、`ApiResource` 都加上」→ 假：seeder 只读 `ApiResource`（`OpenIddictDataSeeder.cs:189`）。④ 「端口 7001–7004 已占用、用 7005」→ 数字对，但补上真实出处（各 `launchSettings.json`）与自检命令。⑤ `file:///d:/Project/...` 绝对链接 → 全部改为仓库相对路径。⑥ 原文第 129–132 行的 DbMigrator 接线与「已有库不会自动更新」说法予以保留并补充 `PruneObsoleteClients=true` 与「配置即事实」的真实行为。 |
| | | **补充的新事实**：API 路由前缀真相、网关只有 catch-all 挂授权策略、网关默认不校验 audience、`AllowedCorsOrigins`/`ApiScope` 死配置、`ApiResource` 缺 `MetaDowProjects` 导致 Projects 全 401、Dockerfile `--from=publish` 全坏、健康端点 `/health`+`/alive` 在全环境映射且生产只回聚合状态、无迁移（索引在 `MongoDbModule` 的 `EnsureIndexesAsync` 建）、`TransactionBehavior=Disabled`、NuGet 缓存目录不可写会导致「0 错误但非零退出」、`ConfigureMongoConnectionStrings` 硬编码库名清单、以及第 12 节的清理项。 |
| | | **删掉的失效步骤**：无（原文没有描述 EF Core/PostgreSQL/Blazor/`abp new` 的步骤）；但明确声明这些**都不存在**，避免后来者去找。 |
