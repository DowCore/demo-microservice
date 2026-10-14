# Meta.Dow

> ABP 微服务后端：**.NET 10 / ABP 10.6 / MongoDB / YARP / .NET Aspire**。低代码逻辑编排、表单库、报表与 OA 审批集中在 SaaS 服务，经 YARP 网关对外，AuthServer 使用开源主题 **LeptonX Lite**。
>
> **本仓库只有后端**：前端 Vue 工程在 `D:\Project\vue-demo`，不在本仓。
>
> **文档基线**：代码基线 `267d375`（2026-10-13）。该基线实测 `dotnet build Meta.Dow.slnx -c Release` = **0 错误 / 162 警告**，`dotnet test Meta.Dow.slnx -c Release` = **62/62 通过**。本文所有断言均可回溯到代码或配置，`file:line` 指本仓库相对路径。

## 1 这个仓库是什么

- **后端在本仓**。四个业务服务（Administration / IdentityService / SaaS / Projects）+ YARP 网关 + OpenIddict AuthServer + Aspire AppHost + 集中式 DbMigrator，解决方案为 `Meta.Dow.slnx`。
- **前端不在本仓**：Vue Vben Admin 工程位于 `D:\Project\vue-demo`（参考实现 `D:\Vue\vue-vben-admin`），经网关 `https://localhost:7500` 访问后端。逻辑编排画布、审批画布、表单设计器、报表配置器的交互都在前端仓实现，本仓只提供 API 与 DSL 契约。前端相关描述均为 ⚠️ **未核验（前端在 vue-demo）**。
- **重心在 SaaS 的低代码平台**：`src/services/saas` 共 28,756 行、127 个文件（其中 `Orchestration` 目录 98 个文件、27,768 行），占全仓 `.cs` 代码量（`src/` + `tests/` 合计 36,886 行）的 **78.0%**。
- 持久层是 MongoDB。**没有 EF Core 工程、没有 SQL Server / PostgreSQL 作为平台库、没有 Makefile、没有 Blazor。**

## 2 技术栈事实表

版本以 `global.json` / `Directory.Packages.props` / 各 `.csproj` 为准（集中包管理，版本只声明在 `Directory.Packages.props`）。

| 组件 | 事实 | 依据 |
| --- | --- | --- |
| 目标框架 | `net10.0` | 各 `.csproj` |
| .NET SDK | 10.0.401（`rollForward: latestFeature`） | `global.json:3-4` |
| 应用框架 | ABP 10.6.0 开源模块 | `Directory.Packages.props:53-124` |
| .NET Aspire | **13.5.3**（`Aspire.Hosting.MongoDB` / `RabbitMQ` / `Redis` / `Seq`、`Aspire.Seq`、`Aspire.StackExchange.Redis` 全部 13.5.3） | `Directory.Packages.props:9-15` |
| 持久层 | MongoDB，`MongoDB.Driver` 3.4.0 | `Directory.Packages.props:33` |
| 网关 | YARP，`Yarp.ReverseProxy` 2.3.0 | `Directory.Packages.props:127` |
| 认证 | OpenIddict，`OpenIddict.Abstractions` 7.7.0 + `Volo.Abp.OpenIddict.*` 10.6.0 | `Directory.Packages.props:37,97-99` |
| 登录页主题 | LeptonX Lite，`Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite` 5.6.0 | `Directory.Packages.props:62` |
| 脚本沙箱 | Jint 4.4.1 | `Directory.Packages.props:20` |
| 消息 / 缓存 / 锁 | `RabbitMQ.Client` 7.1.2、`StackExchange.Redis` 2.8.31、`DistributedLock.Redis` 1.1.1 | `Directory.Packages.props:44,51,18` |
| 日志与链路 | Serilog.AspNetCore 10.0.0 + Seq、OpenTelemetry 1.18.0（OTLP） | `Directory.Packages.props:46-50,38-42` |
| 测试 | xUnit 2.9.2 | `Directory.Packages.props:125` |
| 包管理 | 集中包管理（CPM） | `Directory.Packages.props:4` |
| 低代码 SQL 方言 | PostgreSQL / MySQL / SQL Server / Oracle（+ Redis / MongoDB 非 SQL 族） | `src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/DataSourceProvider.cs`、`.../Application/Orchestration/SqlDialect.cs` |

## 3 目录结构

```
Meta.Dow.slnx                    新式 XML 解决方案
Directory.Packages.props         集中包版本（CPM）
global.json                      SDK 10.0.401
src/
  apps/
    Meta.Dow.AppHost/            Aspire 编排入口（Program.cs = 启动图）
    Meta.Dow.AuthServer/         OpenIddict OAuth2/OIDC（LeptonX Lite 登录页）
  gateway/Meta.Dow.Gateway/      YARP 反向代理 + JWT + 限流
  services/
    administration/  identity/  projects/  saas/
      host/{Service}.HttpApi.Host/            API 宿主（进程入口）
      src/{Service}.Domain.Shared/            常量、枚举、共享 Schema
      src/{Service}.Domain/                   实体、聚合、领域服务
      src/{Service}.Application.Contracts/    DTO 与服务接口
      src/{Service}.Application/              应用服务（业务逻辑）
      src/{Service}.MongoDB/                  MongoDbContext、集合、索引
      src/{Service}.HttpApi/                  控制器
      src/{Service}.HttpApi.Client/           客户端代理（见 §7，当前无调用方）
  shared/
    Meta.Dow.Shared/                 服务名常量（MetaDowNames）、数据范围、多租户常量
    Meta.Dow.ServiceDefaults/        OpenTelemetry、/health 与 /alive
    Meta.Dow.Hosting.Shared/         Redis 缓存、RabbitMQ、分布式锁、本地化
    Meta.Dow.Microservice.Shared/    各服务共用的 JWT / CORS / Swagger 配置
    Meta.Dow.DbMigrator/             集中式 Mongo 迁移与种子（OpenIddict 客户端的唯一事实来源）
tests/Meta.Dow.Orchestration.Tests/  xUnit 单元测试
tools/                              mongo-dump.ps1、mongo-restore.ps1
data/mongo/                         mongodump 落盘目录（见 §6）
docs/                               12 篇方案与契约文档（见 §8）
```

持久层是 `{Service}.MongoDB`，**不存在 `{Service}.EntityFrameworkCore`**。

SaaS 之外的三个服务（Administration / IdentityService / Projects）代码量合计不足全仓的四分之一，Projects 基本还是脚手架。**读这个仓库，重点读 `src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration`。**

## 4 本地启动

### 前置条件

| 依赖 | 用途 | 如何验证就绪 |
| --- | --- | --- |
| .NET 10 SDK（10.0.401） | 还原 / 构建 / 运行 | `dotnet --version` 输出 `10.0.401` |
| Docker Desktop | Aspire 拉起 MongoDB / RabbitMQ / Redis / Seq | `docker ps` 正常返回列表 |
| ABP CLI + Node.js + Yarn | 仅用于 `abp install-libs` | `abp --version` 正常输出 |
| PowerShell 5.1+ | AppHost 以 `powershell` 启动 `tools/mongo-restore.ps1` | `powershell -Command $PSVersionTable.PSVersion` |

### 步骤

**1. 还原**

```bash
dotnet restore Meta.Dow.slnx
```

成功标志：无 `NU*` 还原错误。

**2. 构建（可选）**

```bash
dotnet build Meta.Dow.slnx -c Release
```

成功标志：`0 个错误`。本基线的 162 个警告来自编译器而非分析器（见 §6）。

**3. 安装 AuthServer 登录页前端库**（`wwwroot/libs` 不进 Git，首次克隆必做）

```bash
cd src/apps/Meta.Dow.AuthServer
abp install-libs
```

成功标志：`src/apps/Meta.Dow.AuthServer/wwwroot/libs` 下出现 `@abp/`、`@volo/`、`bootstrap` 等目录。

**4. 一键启动全部服务**

```bash
dotnet run --project src/apps/Meta.Dow.AppHost
```

启动顺序由 `src/apps/Meta.Dow.AppHost/Program.cs` 决定：MongoDB（唯一带持久卷）→ `mongo-restore`（`tools/mongo-restore.ps1`，无 dump 时跳过）→ DbMigrator（`WaitForCompletion` 后其余资源才启动）→ RabbitMQ / Redis / Seq → Administration / IdentityService / SaaS / Projects / Gateway / AuthServer。RabbitMQ、Redis、Seq 是**临时容器**，AppHost 结束即销毁。

成功标志：

- Aspire Dashboard 打印本地地址，全部资源 Running、`metadowdbmigrator` 为 Finished；
- 任一服务 `https://localhost:<port>/health` 返回 JSON `Healthy`——`/health` 与 `/alive` 在**所有环境**都映射（`src/shared/Meta.Dow.ServiceDefaults/Microsoft/Extensions/Hosting/Extensions.cs:154-170`）。

### 端口（开发）

下表取自各 Host `Properties/launchSettings.json` 的 `Aspire` 配置档；运行时以 Dashboard 显示的实际端点为准（Aspire 可能为服务分配代理端口）。

| 入口 | 地址 |
| --- | --- |
| AppHost（Dashboard） | `https://localhost:17166`（http `15124`） |
| 网关 | `https://localhost:7500`（http `7501`） |
| AuthServer | `https://localhost:7600` |
| Administration | `https://localhost:7001` |
| IdentityService | `https://localhost:7002` |
| SaaS | `https://localhost:7003` |
| Projects | `https://localhost:7004` |

默认管理员：`admin` / `1q2w3E*`。

> ⚠️ 这是**公开的硬编码口令**，不是一个"仅首次登录有效"的临时值：同一字符串还作为**新租户管理员回退口令**写死在 `src/services/identity/host/Meta.Dow.IdentityService.HttpApi.Host/EventHandler/TenantCreatedEventHandler.cs:35`，并出现在 `src/reset-databases.ps1:34` 与 `src/reset-databases.sh:36`。任何非一次性的本地/测试环境都必须先改掉。

### 已知坑

- **IDE F5 启动时 Gateway 显示 Finished、出现 `run_session` 超时**：优先用上面的 `dotnet run`（不走 IDE run session）。AppHost 已把 `DCP_IDE_REQUEST_TIMEOUT_SECONDS` 调到 300（`src/apps/Meta.Dow.AppHost/Properties/launchSettings.json`），`Program.cs:135-150` 还会清空 `DEBUG_SESSION_*` 环境变量以绕开 IDE 调试桥。
- **Docker 里残留的 Aspire 容器**若提示网络不存在 / HTTP 404，请删除后重新 `dotnet run` AppHost，勿手动 Start 旧容器。

## 5 已实现能力

图例：**✅** 已实现且可用 ｜ **⚠️** 部分实现或存在明确缺陷 ｜ **❌** 未实现（规划）

### 5.1 Gateway（7500）

- ✅ **YARP 路由表**（`src/gateway/Meta.Dow.Gateway/appsettings.json`）：`/api/administration`、`/api/abp`、`/api/setting-management`、`/api/permission-management`、`/api/menu` → administration；`/api/identity`、`/api/account` → identity（7002）；`/api/multi-tenancy`、`/api/orchestration`、`/api/logic`、`/api/feature-management` → saas；`/api/projects` → projects
- ✅ **JWT 校验**（`AuthServer:Authority`）、限流为**按用户名（未登录按来源 IP）固定窗口 300 请求/分钟、无排队**、401/429 统一返回 `{ error: { code, message } }`、集群请求超时 30s（`src/gateway/Meta.Dow.Gateway/Program.cs:58-110`）
- ✅ 默认授权策略 = 已认证，catch-all `{**catch-all}` → administration 且**要求认证**（未知路径不再匿名可达）
- ⚠️ 网关显式关闭 audience 校验（`ValidateAudience = false`，`src/gateway/Meta.Dow.Gateway/Program.cs:56`），`AuthServer:ValidAudiences` 也是空数组

### 5.2 AuthServer（7600）

- ✅ **OpenIddict OAuth2/OIDC**：`authorization_code` / `password` / `refresh_token`，公有客户端强制 PKCE（`src/shared/Meta.Dow.DbMigrator/OpenIddictDataSeeder.cs:346-349`）
- ✅ LeptonX Lite 登录页（前端库由 `abp install-libs` 还原）
- ✅ **配置即事实**的客户端种子：`Clients` 配置先校验再写库，残缺时抛错中止且不改动数据库（`OpenIddictDataSeeder.cs:37-47,89-127`）；由配置拥有的集合（RedirectUris / Permissions）每轮先清空再重建（`:309-322`）
- ✅ `PruneObsoleteClients` 默认关闭，删除未登记客户端须显式开启且逐个 Warning（`OpenIddictDataSeeder.cs:164-185`）

### 5.3 Administration（7001）

- ✅ **动态菜单与路由**：`api/administration/menus`、`api/menu/routes`（按当前用户权限码过滤后返回前端路由）、`api/menu/all`（`src/services/administration/src/Meta.Dow.Administration.HttpApi/Menus/MenuController.cs:13,53,65,72`）
- ✅ **权限管理 / 设置管理 / 功能管理** HTTP API（`src/services/administration/src/Meta.Dow.Administration.HttpApi/AdministrationHttpApiModule.cs:15-17`）
- ✅ **审计日志**（Mongo 集合 `AbpAuditLogging`，`src/services/administration/src/Meta.Dow.Administration.MongoDB/AdministrationMongoDbModule.cs:14,36`）
- ⚠️ **数据范围（Data Scope）只有配置侧**：`RoleDataScopeAppService` 提供 `GetListByRoleAsync` / `SetAsync` / `GetMyAsync`（`src/services/administration/src/Meta.Dow.Administration.Application/DataPermission/RoleDataScopeAppService.cs`），但查询层没有任何注入点（见 §7 缺点 6）

### 5.4 IdentityService（7002）

- ✅ **用户 / 角色**（ABP Identity HTTP API，`src/services/identity/src/Meta.Dow.IdentityService.HttpApi/IdentityServiceHttpApiModule.cs:14`）
- ✅ **组织单元** `OrganizationUnitAppService`
- ✅ 网关把 `/api/account/*` 路由到本服务
- ⚠️ 新租户管理员回退口令硬编码为 `1q2w3E*`（`TenantCreatedEventHandler.cs:35`）

### 5.5 SaaS（7003）—— 多租户与低代码

- ✅ **多租户**：租户管理（`AbpTenantManagementHttpApiModule`，`src/services/saas/src/Meta.Dow.SaaS.HttpApi/SaaSHttpApiModule.cs:13`）
- ✅ **逻辑编排**
  - 定义 / 版本 / 发布：`api/orchestration/definitions`（`{id}/publish`、`{id}/versions`、`by-code/{code}`、`clone-as-custom`、`usages/by-callee`、`reusable-lookup`）——`.../HttpApi/Orchestration/FlowDefinitionController.cs:12`
  - 执行实例：`api/orchestration/instances`（含 `dry-run` 试运行、`{id}/cancel`、`delete-many`）——`FlowInstanceController.cs:12,42,49`
  - **发布为系统 API**：`POST /api/logic/{flowKey}`（`LogicApiController.cs:11,22-23`）
  - 系统上下文与日期表达式：`api/orchestration/system-parameters`、`system-parameters/preview-date`（`LogicApiController.cs:57,67,74`）
  - **节点类型**（`FlowExecutor.cs:642-789`）：`start/end`、`log`、`assign|setvariable`、`mask`、`throw|assert`、`condition`、`http|httpcall`、`code`、`rabbitmqpublish|rabbitpublish|broadcast`、`subflow|logiccomponent|component`、`foreach|loop`、`parallel|when`、`resourcequery|resourceget|resourcecreate|resourceupdate|resourcedelete|resourcecrud|resourcebatch*`；未知类型直接抛错
  - **数据连接**：PostgreSQL / MySQL / SQL Server / Oracle / Redis / MongoDB 白名单（`DataSourceProvider.cs`），`api/orchestration/data-sources`（`providers`、`lookup`、`test`、`{id}/test`）
  - **低代码建表**：`api/orchestration/tables`（`{id}/preview`、`{id}/apply`、`{id}/resources`）；查询对象 `api/orchestration/query-objects`（`catalog`、`import`、`{id}/refresh`）
  - **应用资源 CRUD**：`api/orchestration/resources`（`{id}/publish`、`{code}/query|get|create|update|delete|batch-delete`）
  - **触发器与定时**：`api/orchestration/message-sources`（RabbitMQ / Kafka / MQTT 消费，见 `KafkaMessageTriggerHostedService.cs`、`MqttMessageTriggerHostedService.cs`、`RabbitMessageTriggerHostedService.cs`）、`api/orchestration/triggers`、`api/orchestration/schedules`（`{id}/run-now`，`FlowScheduleHostedService` + 分布式锁）
  - **入参与出参整形**：`InputRuleValidator`；`DateExpressionEvaluator`（`sys.Now - 3d` 一类）；`FinalOutputAssembler` 支持嵌套 `map.item`、聚合、分组、可见性/脱敏，`promote` 使 `data` 直接为数组或对象（最多一项，`FinalOutputAssembler.cs:36-46`）
- ✅ **表单库**：`api/orchestration/forms`（`published/{code}`、`published-lookup`、`{id}/publish`、`{id}/resync-catalog`）——`AppBuilderControllers.cs:226`
- ✅ **报表**：`api/orchestration/reports`（`from-resource`、`from-table`、`published/{code}`、`{code}/query`）——`AppBuilderControllers.cs:167`
- ✅ **OA 审批流**：`api/orchestration/workflows`（`{id}/publish`）与 `api/orchestration/workflow-runtime`（`start`、`my-tasks`、`tasks/{taskId}/complete`）——`AppBuilderControllers.cs:281,321`；校验器覆盖节点 id 唯一、start/end 唯一、连线端点存在、**无环**、非结束节点必须有出线、办理人/多人规则白名单（`WorkflowDefinitionValidator.cs:56-110,188-203`）
- ✅ **系统流程**：`sys.resource.query|get|create|update|delete` 由种子发布（`src/services/saas/src/Meta.Dow.SaaS.Domain.Shared/Orchestration/TablePlatformType.cs:60-66`、`.../Domain/Orchestration/SystemResourceFlowSeedContributor.cs`）
- ⚠️ **报表 DataScope 可被旁路**：报表查询先把报表的 `DataScope` 合并进 filter，再调用流程（`src/services/saas/src/Meta.Dow.SaaS.Application/Orchestration/ReportDefinitionAppService.cs:283,305`）；但同一个已发布系统流程 `sys.resource.query`（`TablePlatformType.cs:62`）也能被 `POST /api/logic/{flowKey}` 直接调用，该入口不经过报表的 `DataScope` 合并
- ⚠️ **表列 `Origin` 由客户端提交**：`Origin` 是列元数据（`Convention` / `User`），DTO 直接接收客户端值（`AppBuilderDtos.cs:30,50` → `TableDefinitionAppService.cs:198,210`），写入路径以 `Origin != Convention` 决定哪些列可写（`ResourceCrudExecutor.cs:718`）。把约定列改判为 `User` 列，即可在资源 CRUD 中写入本应服务端托管的字段（如 `TenantId`），构成跨租户改写行的路径

### 5.6 Projects（7004）

- ⚠️ **只有脚手架**：`ProjectsAppService`、`SampleAppService`，无业务域实现
- ⚠️ **受众不匹配导致 401**：服务以 `ConfigureMicroservice(MetaDowNames.ProjectsApi)` 要求 `aud = MetaDowProjects`（`src/shared/Meta.Dow.Shared/Microsoft/Extensions/Hosting/MetaDowNames.cs:25`、`MetaDowMicroserviceModule.cs:89`），但 DbMigrator 注册的 `ApiResource` 只有 `MetaDowAdministration / MetaDowAuthServer / MetaDowIdentityService / MetaDowSaaS`（`src/shared/Meta.Dow.DbMigrator/appsettings.json`），而 `Projects_API` 客户端的 scope 是同样未注册的 `ProjectsService`。⇒ 拿有效令牌访问 Projects 会 401
- ❌ 无业务域 API

## 6 工程化现状

| 方面 | 真实状态 |
| --- | --- |
| **构建** | `dotnet build Meta.Dow.slnx -c Release` = **0 错误 / 162 警告**（本基线）。`src/Directory.Build.props` 只设 `LangVersion` / `Nullable` / `TreatWarningsAsErrors=false`，**没有引入任何分析器包**（无 StyleCop / SonarAnalyzer / Roslynator / NetAnalyzers），这 162 个警告不会被任何门禁拦住 |
| **测试** | `dotnet test Meta.Dow.slnx -c Release` = **62/62 通过**。全部是纯单元测试，不需要 Mongo/Redis/RabbitMQ。分布：`FlowExecutorControlFlowTests` 39 个、`WorkflowDefinitionValidatorTests` 11 个、`FormSchemaFlatteningTests` 7 个、`WorkflowRecordPatchTests` 4 个、`SecretsConfigSafetyTests` 1 个。**没有集成测试或端到端测试**：HTTP、SQL、Code 沙箱、资源 CRUD、认证链路均无覆盖；`FlowExecutorControlFlowTests.cs:704-711` 把 7 个协作者以 `null!` 传入，一旦执行触达这些路径必然 NRE |
| **CI** | `.github/workflows/ci.yml` 单 job，`ubuntu-latest`，30 分钟超时：restore → build → test（上传 trx）。**没有 `docker build` 步骤**，没有镜像扫描、没有发布 |
| **密钥治理** | ✅ **真实有效**。`.gitignore:1-7` 排除 `**/appsettings.secrets.json`、`**/tempkey.jwk`、`**/tempkey.rsa`；已核对 `git ls-files`，仓库只跟踪 6 个 `appsettings.secrets.json.example` 模板，**没有任何真实密钥文件入库**。`SecretsConfigSafetyTests` 是唯一的自动化护栏：禁止 secrets 覆盖文件（含 `.example` 模板）出现 JSON 数组——这是 2026-10-10 登录事故（`invalid_request / ID2052`）的回归测试 |
| **Docker** | 7 个 Dockerfile（AuthServer / Gateway / 4 个服务 Host / DbMigrator）。**当前全部不可用**：7/7 的第二阶段命名为 `final`，却写 `COPY --from=publish`，引用不存在的阶段名，`docker build` 必然失败（如 `src/apps/Meta.Dow.AuthServer/Dockerfile:11`）。仓库内无 `docker-compose.yml`，本地编排只走 Aspire |
| **数据** | `data/mongo/` 目前只有 `.gitkeep` 与 `README.md`，没有任何 dump |

### MongoDB 数据进 Git（⚠️ 操作前必读）

容器库默认随 AppHost 结束清空。导出当前数据供同事使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/mongo-dump.ps1
git add data/mongo
```

他人拉取后启动 AppHost 会自动 `mongorestore`，再跑 DbMigrator。详见 [data/mongo/README.md](data/mongo/README.md)。

> ⚠️ **这条流程会把密钥级数据写进 Git 历史，提交前必须先脱敏。**
>
> `.gitignore` **没有**排除 `*.bson` / `*.metadata.json` / dump 目录——全文只有 `*.mdf`、`*.ldf`（`.gitignore:219-221`）。一旦执行 `git add data/mongo`，下列内容就会入库：
>
> - `MetaDowIdentityServiceDb`：`AbpUsers`（**用户口令哈希**）与 `AbpOpenIddict*`（**OpenIddict 客户端密钥**，`OpenIddictApplications`）。
> - `MetaDowAdministrationDb` / `MetaDowSaaSDb` / `MetaDowProjectsDb`：全部业务与租户数据。
>
> `tools/mongo-dump.ps1` 会先清空 `data/mongo` 再落盘，所以"只提交种子数据"需要你自己控制导出范围。若要长期保持这条能力，建议先给 `.gitignore` 加规则或改为私有渠道分发脱敏 dump——当前仓库**没有任何脚本或 CI 检查**替你把关。

## 7 架构评价

评分只描述**代码里能证实的部分**；没有证据的能力不计分。

### 优点

1. **技术栈前沿，本地编排确实出色**。net10.0 + ABP 10.6 + Aspire 13.5.3 + YARP + OpenIddict，版本全部收敛在 `Directory.Packages.props`。`src/apps/Meta.Dow.AppHost/Program.cs` 的启动图（Mongo 持久卷 → restore → DbMigrator → 基础设施 → 各服务）是有序的，5 个服务与网关、AuthServer 都 `WaitForCompletion(migrator)`；`/health`、`/alive` 在所有环境都映射。这是本仓库最扎实的部分。
2. **低代码"三分离"的架构判断正确**。编排算逻辑、表单定义数据、OA 只做人审，三者用**已发布 `flowKey`** 串接（`WorkflowNotifier`、`PublishedFlowInvoker`），审批引擎里不重复实现写入逻辑。这避免了"审批流里再长一套业务写入"的常见退化，是这个规模的低代码平台里最关键的一个正确决定。
3. **编排引擎主循环失败关闭（fail-closed）**。无出边、无匹配分支、检测到环、超过 `DefaultMaxSteps`（200）一律写错误并让执行失败，而不是静默走完（`FlowExecutor.cs:248,298,315,337,344`）；DSL 校验还会提前拒绝非 Condition 节点的多出边（回归测试见 `FlowExecutorControlFlowTests.cs:29-53`）。
4. **Code 沙箱是真加固，不是摆设**。Jint 4.4.1，超时 8s、最多 20,000 条语句、递归上限 64，`fetch` / `eval` / `Function` 一律置为 `undefined`，全仓无 `AllowClr`（`CodeSandboxExecutor.cs:76-86`、`OrchestrationConsts.cs:20-21`）。
5. **SQL 层防线清晰且顺序正确**。方言抽象覆盖 PostgreSQL / MySQL / SQL Server / Oracle（`SqlDialect.cs`），标识符经引用/白名单处理，值全部走参数（`ParameterizedSqlExecutor.cs`）；**租户谓词与用户条件树是并列 predicate，最后由 `string.Join(" AND ", predicates)` 拼接**（`FilterSqlBuilder.cs:59,121,146,152`），因此用户条件树里的 `OR` 逃不出租户边界。这个先后顺序是刻意设计，值得保留。
6. **密钥治理有自动化护栏**。见 §6：忽略规则生效、模板形态由测试强制校验，且两起真实事故都有可复核的记录与回归测试。
7. **文档体量大且带真实复盘**。`docs/` 12 篇、约 7,000 行，明确区分"事实契约"与"规划"；2026-10-10 OpenIddict 客户端索引合并事故的复盘写在 `OpenIddictDataSeeder.cs:52-62`，并被 `SecretsConfigSafetyTests` 固化成不变量。

### 缺点与风险

1. **容器化路径实际不可用**。7/7 Dockerfile 的 `COPY --from=publish` 引用不存在的阶段名（阶段是 `build` / `final`，如 `src/apps/Meta.Dow.AuthServer/Dockerfile:11`），而 CI 里没有 `docker build`，所以这个错误从未被任何门禁暴露。任何"用 Docker 部署"的说法目前都不成立。
2. **公开的默认口令**。`1q2w3E*` 既写在启动说明里，又作为新租户回退口令硬编码（`TenantCreatedEventHandler.cs:35`），还出现在重置脚本（`src/reset-databases.ps1:34`）。它不是一个只在首次登录时有效的秘密。
3. **Projects 服务受众对不上**。`MetaDowProjects` 不在注册的 `ApiResource` 中，`Projects_API` 的 scope 是同样未注册的 `ProjectsService`（`src/shared/Meta.Dow.DbMigrator/appsettings.json`）⇒ 有效令牌访问 Projects 必 401（`MetaDowNames.cs:25`、`MetaDowMicroserviceModule.cs:89`）。
4. **表列 `Origin` 客户端可控** ⇒ 持表更新权限者可把约定列（含 `TenantId`）改判为用户列，在资源 CRUD 写入时跨租户改写行（`AppBuilderDtos.cs:30`、`ResourceCrudExecutor.cs:718`）。
5. **报表 DataScope 存在旁路**：`/api/logic/sys.resource.query` 可绕过报表查询前的 `DataScope` 合并（`ReportDefinitionAppService.cs:283` vs `TablePlatformType.cs:62`）。
6. **IAM 行级数据范围零落地**。`DataPermissionQueryableExtensions.ApplyDataPermission` 全仓**无调用点**，且扩展只对实现 `IHasOrganizationId` 的实体生效（`src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/DataPermissionQueryableExtensions.cs:5-24`）；`RoleDataScopeAppService` 只有配置读写与"我的"解析。数据范围目前是**配置界面**，不是执行约束。
7. **分析器未启用**。`src/Directory.Build.props` 无任何分析器包，162 个警告没有门禁。（`src/CLAUDE.md` 声称"分析器已通过 Directory.Build.props 启用"，与该文件实际内容不符。）
8. **测试覆盖实质为零**。62 个用例全是纯单元测试，39 个集中在编排控制流；HTTP、SQL、Code 沙箱、资源 CRUD、认证链路零覆盖，且脚手架把 7 个依赖以 `null!` 注入（`FlowExecutorControlFlowTests.cs:704-711`），触达即 NRE。没有集成测试，也没有任何针对 Dockerfile、网关路由、OpenIddict 受众一致性的校验。
9. **微服务之间实际不是 HTTP 调用**。4 个 `*.HttpApi.Client` 项目存在，但**没有任何 Host 的 `[DependsOn(...)]` 引用它们**；服务间靠共享 Mongo 上下文（`MetaDowMicroserviceModule.cs:23` 依赖 `AdministrationMongoDbModule`，各 Host 再互相引用 `*MongoDbModule`）与各进程内的 ABP 模块协作。这是务实的取舍，但意味着"微服务边界"目前是**部署边界而非数据边界**——共享集合的结构变更会同时影响多个进程。

### 评分

| 维度 | 评分 | 理由与证据 |
| --- | --- | --- |
| 架构前沿性 | 7.5 / 10 | **加分**：技术选型新、本地编排出色（Aspire 13.5.3 一键拉起 5 服务 + 3 基础设施 + DbMigrator，启动顺序有依赖编排）。**扣分**：7/7 Dockerfile 不可用、CI 无镜像构建，工程化落地明显落后于选型 |
| 文档与设计规范 | 7.0 / 10 | **加分**：`docs/` 12 篇约 7,000 行，显式区分"事实契约"与"规划"，含两起真实事故复盘与回归测试。**扣分**：本次核对发现本 README 自身有与代码相反的硬断言，`src/CLAUDE.md` 关于"分析器已启用"的说法也与 `Directory.Build.props` 不符——文档可信度不齐 |
| 低代码完整性 | 7.5 / 10 | **加分**：编排 / 表单 / 报表 / 审批四块都真实落地并用 `flowKey` 串接，节点类型与触发器覆盖广，出参 `promote`、试运行、版本发布、系统流程种子齐备。**扣分**：报表 DataScope 有旁路、表列 `Origin` 可被客户端改写、编排之外几乎没有测试 |
| 微服务职责边界 | 4.5 / 10 | **加分**：四个服务 + 网关 + 认证服务的划分本身清楚，数据库名与服务名收敛在 `MetaDowNames`。**扣分**：SaaS 占全仓 78.0% 代码，编排/表单/报表/审批运行时全在同一个进程；4 个 `HttpApi.Client` 无任何调用方，服务间靠共享 Mongo 上下文；Projects 受众未注册，实际不可用 |
| 工程协同 | 5.5 / 10 | **加分**：CPM 集中版本、`.editorconfig`、gitignore 密钥治理、一条可用的 CI 流水线（restore→build→test）。**扣分**：分析器未启用、无集成测试、Docker 路径坏死、前后端分仓且无自动契约同步（DSL 与节点配置两边手写） |
| 安全与多租户 | 5.0 / 10 | **加分**：Code 沙箱真加固（8s / 2 万语句 / 递归 64 / 无 `AllowClr`）、SQL 全参数化且租户谓词收在最终 `AND` 之前、secrets 有忽略规则 + 护栏测试、公有客户端强制 PKCE。**扣分**：默认管理员口令公开且作新租户回退、表列 `Origin` 客户端可控导致跨租户改写行、`sys.resource.query` 绕过报表 DataScope、行级数据范围零落地 |
| 可验证性 / 测试 | 3.0 / 10 | **加分**：62 个用例可离线运行、有 1 个真正针对历史事故的配置护栏测试。**扣分**：62 个用例全部是纯单元测试且高度集中在编排控制流（39 个）；HTTP、SQL、Code 沙箱、资源 CRUD、认证链路零覆盖；7 个依赖以 `null!` 注入；无集成/端到端测试，Dockerfile 与网关路由无任何校验 |

**结论**：技术选型先进、文档体量大、编排引擎的失败关闭与沙箱加固可信，但"选型先进 + 文档多"不等于工程质量高。当前最该补的三件事是：**容器化与 CI 门禁**（修 Dockerfile 阶段名 + 加 `docker build` + 启用分析器）、**安全默认值**（改默认口令、让 `Origin` 服务端可信、给 `sys.resource.query` 补 DataScope）、以及**真正的集成测试**（HTTP / SQL / 认证链路），而不是继续扩大 SaaS。

## 8 文档索引

`docs/` 共 13 篇、约 7,300 行。多篇文件自带"事实契约 / 规划"章节标注，引用前请注意章节性质。这些文档正在被持续修订，**文件名是稳定标识**，标题与"文档性质"可能变化。

**低代码逻辑编排**
- [低代码逻辑编排实现契约](docs/lowcode-logic-orchestration.md) —— 事实契约（§1 含系统目标：通用稳定型编排底座）
- [低代码逻辑编排完善意见稿](docs/lowcode-logic-orchestration-review.md) —— 评审意见（缺陷清单 S/C/E/G + 整改批次与验收标准），与上篇契约互补、不重复
- [逻辑编排补齐实施方案](docs/lowcode-logic-orchestration-remediation-plan.md) —— 落地计划（R1–R12 分批与验收标准，含已实施状态）
- [逻辑编排补齐：实现前取舍对比](docs/lowcode-logic-orchestration-remediation-tradeoffs.md) —— 每项改动落地前"现有 vs 主流"的取舍论证
- [逻辑编排能力盘点与主流实现对比](docs/lowcode-logic-orchestration-benchmark.md) —— 对标分析（优势/不足/主流差异，全部不足项对照 review 的 S/C/E/G 编号）
- [添加新微服务](docs/add-new-service.md) —— 以 `src/services/projects` 为模板的接线操作手册

**资源 / 表 / 表单 / 审批 / 报表**
- [资源 / 表设计 / 查询对象 / 表单 / 审批 / 报表 / 看板：事实契约与规划](docs/lowcode-form-workflow-board.md)
- [表单与 OA 审批：能力契约、行为边界与增强规划](docs/lowcode-form-oa-enhancement.md)
- [表单库预览能力方案](docs/lowcode-form-preview.md) —— 混合（后端契约 + 前端规格 + 规划）
- [OA 审批流：表单绑定、节点规则与运行时契约](docs/lowcode-oa-workflow.md)
- [通用报表管理：事实契约与规划](docs/lowcode-report-management.md)
- [报表配置器 UX 重设计](docs/lowcode-report-config-ux.md) —— 前端交互规格

**身份与权限**
- [身份与权限体系说明](docs/iam-rbac-menu.md)

前端固定为 Vue Vben Admin，工程与参考仓路径约定见 `.cursor/rules/frontend-vue-vben.mdc`；`MetaDow_Vue` OpenIddict 客户端（password / authorization_code / refresh_token）已注册。系统目标（通用稳定型逻辑编排底座）与架构红线见 `.cursor/rules/logic-orchestration.mdc`。

## 9 说明

官方 `abp new -t microservice` 需要商业许可；本仓库基于社区开源模板改造。不要用商业模板命令覆盖本仓库，除非已恢复 ABP Business 许可。

本仓库根目录未见 `LICENSE` 文件。
