# Meta.Dow 微服务 — 项目长期记忆

## 技术栈事实（勿被旧文档误导）
- ABP Framework **10.6.0** / .NET **net10.0** / SDK 10.0.401（`global.json`）/ Aspire **13.5.3**。
- 数据库 **MongoDB**（**无 EF Core、无迁移**）；解决方案文件是 **`Meta.Dow.slnx`**；包管理走 **CPM**
  （`Directory.Packages.props`，119 包，csproj 内不写 Version）；**无 Makefile / 无 Blazor / 无 PostgreSQL**。
- 服务目录形如 `src/services/<svc>/src/{Domain.Shared,Domain,Application.Contracts,Application,MongoDB,HttpApi,HttpApi.Client}` + `host/`。
- `AbpUnitOfWorkDefaultOptions.TransactionBehavior = Disabled`（Aspire 单节点 Mongo 非副本集）。

## 本机运行环境
- 全栈由 `Meta.Dow.AppHost`（Aspire）拉起；Mongo/RabbitMQ/Redis/Seq 是 Docker 容器，**端口随机映射**。
- 容器名带随机后缀（`mongodb-<hash>`）。Mongo 凭据在 `docker inspect` 的
  `MONGO_INITDB_ROOT_USERNAME` / `MONGO_INITDB_ROOT_PASSWORD`。
- AppHost **不创建** `MetaDowAuthServerDb`；OpenIddict 数据（applications/scopes/tokens）实际落在
  **`MetaDowIdentityServiceDb`**，因为 `AbpDbConnectionOptions` 把连接名 `AbpOpenIddict` 映射到该库。
- 查库：`docker exec -i <mongo容器> mongosh -u <u> -p <p> --authenticationDatabase admin --quiet < script.js`。
  ABP MongoDB 实体字段是 **PascalCase**；`Permissions`/`RedirectUris` 等在库里是 **JSON 字符串**。

## 硬性配置规则（血的教训，务必遵守）
- **secrets 覆盖文件里永远不要出现 JSON 数组。** .NET 配置对数组是**按索引合并**（不替换、不拼接），
  部分条目会与 `appsettings.json` 同索引条目**逐字段混血**。2026-10-10 就用 `"Clients"` 数组覆盖，
  把 `MetaDow_Vue` 从配置里抹掉 → 被清理逻辑软删除 → 登录报 **`invalid_request / ID2052`**。
  需要按键覆盖时一律用**对象**，例如 `"ClientSecrets": { "<clientId>": "<secret>" }`。
  已由 `tests/Meta.Dow.Orchestration.Tests/SecretsConfigSafetyTests.cs` 全仓强制（含 `.example` 模板）。
- DbMigrator 的 `Clients` 是 OpenIddict 客户端**唯一事实来源**，种子器**配置即事实**
  （每次运行清空并按配置重建 permissions/redirects，可自愈收敛）。
  破坏性清理只在 `PruneObsoleteClients: true` 时执行，且逐条 Warning 日志。
- 密钥一律不入库：各 Host 用 git-ignored 的 `appsettings.secrets.json`，仓库只提交 `.example`。
  `.gitignore` 覆盖 `**/appsettings.secrets.json`、`tempkey.jwk`、`tempkey.rsa`。
- **网关不要写死单一 JWT 受众。** 令牌 `aud` 来自 OpenIddict 作用域的 `Resources`
  （本仓四个作用域各自 `Resources=[自身服务名]`，**从不含 `Meta.Dow`**）。
  2026-10-10 因网关写死 `Audience="Meta.Dow"` + `ValidateAudience=true`，导致**所有有效令牌 401**。
  现默认 `ValidateAudience=false`（仍校验签名/签发者/有效期），要强校验就用
  `AuthServer:ValidAudiences` 数组列全。受众是"每个 API 自己的事"。
  （`MetaDowAuthServerModule` 的 `AddAudiences("Meta.Dow")` 只是 AuthServer **自身**的接受声明。）

## 操作纪律（血的教训）
- **绝不 `docker rm -f` "看起来是孤儿"的容器。** 2026-10-10 误删了用户正在服务的 MongoDB/Redis，
  造成全栈 Mongo 超时。清理前必须确认：无 AppHost/服务进程依赖它、且用户未在使用。**默认不动。**
- 服务"全部挂掉"时先查端口监听 + `AppHost` 日志，别假定是环境残留问题。
- 前端 dev 链路：`.env` 的 `VITE_GLOB_API_URL` 为空 → `/api` 由 Vite 代理到**网关 `https://localhost:7500`**；
  `/connect`、`/.well-known` 直接代理到 AuthServer `7600`。所以前端所有 API 401 都先怀疑网关。
- **不要删 `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`**（机器级共享密钥环，业务服务开发态 DP 也用它）。
  删了会让所有服务重新生成密钥、丢失既有保护负载。
- 启动 AppHost 时的 `Aspire.Hosting.Dashboard...BrowserStorage` + `CryptographicException: The payload was invalid`
  **是控制台自身的 UI 偏好解密失败，属良性**（Data Protection 认证加密 + ApplicationDiscriminator 变化所致，
  非业务代码、非本仓改动）。消除办法在浏览器侧清 `Aspire_*` localStorage；仓库侧已把该日志类别压到 `Error`。

## 常用命令
- 编译：`dotnet build Meta.Dow.slnx -c Release`
- 测试：`dotnet test tests/Meta.Dow.Orchestration.Tests/Meta.Dow.Orchestration.Tests.csproj -c Release`
- 前端（`D:/Project/vue-demo`）：`pnpm -F @vben/web-antd run typecheck`

## 前端编排约定（vue-demo `views/orchestration`）
- VForm 设计器的容器分两类：**外壳容器**（grid/table/tab/card/sub-form/tree，自己渲染 `.vf-node`）
  与**内部单元格**（`grid-col`/`table-cell`/`tab-pane`，`internal: true`，由父容器直接渲染）。
- 内部单元格的 `@click` 会 `stopPropagation()` 抢焦点，`<Tabs>` 还整块 `@click.stop`，
  所以**父容器必须靠"左上角容器抓手"选中**，不能指望点空白区。
- 删除一律走 `removeWidgetSafe()`（不是 `removeWidget`）：删最后一列/最后一页签会连整个容器一起删，
  否则会留下 0 列栅格 / 0 页签这类画布上再也选不中的空壳。

## 已知边界（能力新增，非缺陷）
编排引擎控制流原语仍缺：独立 `Sql` 节点、`Switch`、**并行网关/汇合**、**循环**；
审批连线条件尚未统一迁移到 `FilterGroup` 树模型（当前为优先级 + 默认分支）。
