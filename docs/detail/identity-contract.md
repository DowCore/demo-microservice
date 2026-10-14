> **功能描述**见 [../identity.md](../identity.md)。本文件是该功能的证据附录（契约 / 评审 / 方案），不单独作为阅读入口。

# Meta.Dow 身份与权限体系说明

> **文档性质**：本文件 = **事实契约**（§1–§7——描述代码**当前真实行为**）+ **规划**（§8，未实现项）+ 修订记录（§9）。
> 两者已分节隔离：除 §8 之外，任何「已实现」断言都可在代码中找到对应行。
> **代码基线**：`Meta.Dow.slnx` @ `267d375`（2026-10-13）；实测 Release 构建 0 错误、单元测试 62/62 通过。
> **核对方式**：每条「已实现」附 `相对路径:行号`。前端在独立仓库（本仓同级 `../vue-demo`），标注为「仓库外」的内容仅核对了文件存在性，未逐行核对。
> **图例**：✅ 已实现 ｜ ⚠️ 部分实现 ｜ ❌ 未实现（属规划） ｜ 🐞 已知缺陷

---

## 1 定位与边界

### 1.1 与 ABP 商业版（ABP Commercial）的能力对照

本文对齐 ABP Commercial 的 **Identity + Permission Management + 导航授权** 模型。商业版典型路径：

```
PermissionDefinition（定义）
        ↓
角色 / 用户 PermissionManagement（授权）
        ↓
菜单 / 路由 requiredPolicy 或 RequirePermissions（绑定）
        ↓
运行时 IPermissionChecker + 前端 grantedPolicies（过滤）
```

本仓库**采用同一路径**，但落地形态不同：动态菜单是自研实体 `SysMenu`（存于 Administration 服务），叶子节点通过 `Permission` 字段绑定权限名，过滤在服务端完成。

| 能力 | ABP 商业版 | Meta.Dow（本仓库） |
|------|-----------|-------------------|
| 用户 / 角色 CRUD | Identity Pro 页面 | ✅ 开源 `Volo.Abp.Identity` 控制器 + 自研 Vue 页（仓库外） |
| 组织机构（OU）树 | Identity Pro | ⚠️ 自研 `OrganizationUnitAppService`（对齐 Pro 路由与权限名，实现为自研） |
| 权限授予 | PermissionManagement | ✅ 开源 PermissionManagement（角色 / 用户 / 客户端） |
| 动态菜单 | `ApplicationMenu` + `RequirePermissions` | ✅ 自研 `SysMenu` + 服务端逐条 `IPermissionChecker` |
| 按钮级权限 | `IAuthorizationService` + 前端 access | ✅ 后端 `[Authorize(...)]` + 前端 `AccessControl`（仓库外） |
| 行级数据权限 | `IDataFilter` / 数据权限模块 | ⚠️ 模型与解析器齐全，**未接入任何业务查询**（§3.7、§4.1） |
| 多租户 | 分库或共享库 + `IMultiTenant` | ✅ 共享库 + `IMultiTenant`（菜单、数据范围均带 `TenantId`） |

### 1.2 设计原则

| 原则 | 说明 | 代码印证 |
|------|------|----------|
| Permission 为唯一功能授权真相 | 不维护「角色 ↔ 菜单」授权表；菜单只**引用**权限名 | `SysMenu` 无角色集合字段：`src/services/administration/src/Meta.Dow.Administration.Domain/Menus/SysMenu.cs:8-40` |
| 身份与授权分层 | Identity：用户 / 角色 / OU；PermissionManagement：谁有哪些权限；菜单：导航与前端路由 | 网关路由分流：`src/gateway/Meta.Dow.Gateway/appsettings.json:9-90` |
| 可见 ≠ 可写 | 侧栏可见用菜单绑定的权限；按钮与 API 用同一权限组的子权限 | 菜单 `Permission` 用组名；前端 `AccessControl` 用子权限码（如 `AbpIdentity.Users.Create`） |
| 数据权限独立于功能权限 | 功能权限决定「能否进功能」，数据范围决定「能看哪些行」 | 两套实体分离：`SysMenu.Permission` vs `RoleDataScope` |
| 多租户隔离 | `IMultiTenant` 实体带 `TenantId`；租户管理员默认获得租户内适用权限 | `SysMenu`/`RoleDataScope` 实现 `IMultiTenant`；`AdministrationPermissionDataSeedContributor` 按 Host/Tenant 侧授全量 |

---

## 2 现状能力总表（唯一权威表）

| 能力 | 状态 | 证据（`file:line`） | 备注 |
|------|------|-------------------|------|
| 菜单实体与 CRUD | ✅ | `src/services/administration/src/Meta.Dow.Administration.Application/Menus/MenuAppService.cs:30-129` | 增删改查各自独立 `[Authorize(Administration.Menus.*)]` |
| 菜单树查询（管理用） | ✅ | 同上 `:31-42` | `GET /api/administration/menus`，需 `Administration.Menus`；返回未按权限过滤的完整树 |
| 当前用户动态路由 | ✅ | 同上 `:131-179`；`src/services/administration/src/Meta.Dow.Administration.HttpApi/Menus/MenuController.cs:52-57,65-77` | `GET /api/administration/menus/routes` 与 `GET /api/menu/all` |
| 服务端按权限过滤菜单 | ✅ | `MenuAppService.cs:141-159` | 逐条 `await _permissionChecker.IsGrantedAsync(menu.Permission)` |
| 子节点可见时补全父目录 | ✅ | 同上 `:161-176` | 沿 `ParentId` 向上递归至根 |
| Button 不生成侧栏路由 | ✅ | 同上 `:136` | `x.Type != MenuType.Button` 过滤 |
| 菜单权限名下发前端 | ✅ | 同上 `:216`；`.../Application.Contracts/Menus/VbenRouteDto.cs:46-47` | 仅放 `meta.Authority`，不做路由级守卫 |
| 隐藏菜单（`IsVisible=false`） | ⚠️ | `MenuAppService.cs:145-148,215` | 仅当同时无 `Permission` 才被丢弃；否则下发并置 `HideInMenu`（§4.2-4） |
| 菜单种子 | ✅ | `.../Domain/Menus/MenuDataSeedContributor.cs:29-43` | 首次全量种子 + 幂等补齐 + 清理演示菜单 |
| 菜单删除保护（有子级拒绝） | ✅ | `MenuAppService.cs:119-129` | 抛 `Administration:MenuHasChildren` |
| 角色数据范围（`RoleDataScope`） | ⚠️ | `.../Domain/DataPermission/RoleDataScope.cs:12-57` | 实体与 CRUD 齐全，**下游无消费者** |
| 数据范围解析（5 种范围） | ✅ | `.../Application/DataPermission/DataScopeResolver.cs:40-141` | 只被 `RoleDataScopeAppService.GetMyAsync` 调用 |
| 数据范围「我的」回传接口 | ✅ | `.../HttpApi/DataPermission/RoleDataScopeController.cs:41-46` | `GET /api/administration/role-data-scopes/my` |
| 行级过滤扩展 `ApplyDataPermission` | ❌ | `src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/DataPermissionQueryableExtensions.cs:10-56` | **全仓无调用点** |
| `IHasOrganizationId` 业务实体 | ❌ | `src/services/projects/src/Meta.Dow.Projects.Domain/Samples/SampleRecord.cs:11` | 仅 1 个示例实体，其注释里的调用也只是示例 |
| 功能权限定义（Administration） | ✅ | `.../Application.Contracts/Permissions/AdministrationPermissionDefinitionProvider.cs:14-45` | 组 `Administration`：Settings / Menus / DataScopes |
| 功能权限定义（Identity OU） | ✅ | `src/services/identity/src/Meta.Dow.IdentityService.Application.Contracts/Permissions/IdentityServicePermissionDefinitionProvider.cs:11-38` | 复用 `AbpIdentity` 组，追加 `.ManageOU/.ManageRoles/.ManageMembers` |
| 功能权限定义（SaaS / Orchestration） | ✅ | `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Permissions/SaaSPermissionDefinitionProvider.cs:14-130` | 组 `Orchestration`，含 `Sql.Write` |
| 功能权限定义（Projects） | ✅ | `src/services/projects/src/Meta.Dow.Projects.Application.Contracts/Permissions/ProjectsPermissionDefinitionProvider.cs:14-21` | 组 `Projects` / `Projects.Issues.*` |
| 权限授予（角色） | ✅ | `.../Domain/Permissions/AdministrationPermissionDataSeedContributor.cs:33-56` | 按 Host/Tenant 侧取全部可授权限授给 `admin` |
| 权限授予（客户端） | ✅ | `src/shared/Meta.Dow.DbMigrator/OpenIddictDataSeeder.cs:296` | OpenIddict 客户端权限 |
| 多租户 | ✅ | `src/shared/Meta.Dow.Shared/Meta.Dow/MultiTenancy/MultiTenancyConsts.cs:5` | `IsEnabled = true`，由 `MetaDowHostingModule` 生效 |
| 服务间 HTTP 客户端代理 | ❌ | 4 个 `*.HttpApi.Client` 模块存在，但 4 个 Host **均未** `DependsOn` 它们 | 服务间从未走 HTTP 客户端代理（§4.4-12） |
| 网关受众校验 | ⚠️（有意） | `src/gateway/Meta.Dow.Gateway/Program.cs:38-57` | 网关不校验 `aud`；各服务用 `ConfigureMicroservice(MetaDowNames.XxxApi)` 自行校验（§4.4-11） |

---

## 3 契约与实现细节

### 3.1 用户（User）

- 存储：Identity 服务 MongoDB。
- API：开源 `Volo.Abp.Identity` 控制器，路由 `/api/identity/users`（网关路由 `src/gateway/Meta.Dow.Gateway/appsettings.json:17-22`）。
- 权限：`AbpIdentity.Users` 及 `.Create` / `.Update` / `.Delete` 子权限（ABP 开源 Identity 模块提供）。
- 与菜单关系：用户**不直接**挂菜单；通过「角色 → 角色权限 → 菜单 `Permission` 字符串」间接可见。
- 前端按钮：`AbpIdentity.Users.Create` / `.Update` / `.Delete` 经 `AccessControl` 控制（仓库外 `../vue-demo/apps/web-antd/src/views/abp/identity/users.vue:117,145,150`）。
- 租户管理员：创建租户时由事件处理器种子化，见 §3.8。

### 3.2 角色（Role）

- 存储：Identity 服务；API 路由 `/api/identity/roles`。
- 权限管理：开源 PermissionManagement，路由 `/api/permission-management/permissions`（网关 `appsettings.json:77-82`）。
- 种子：`AdministrationPermissionDataSeedContributor` 把当前侧（Host/Tenant）**全部可授予**的权限授给 `admin` 角色；筛选条件是「`MultiTenancySide` 匹配」且「无 Provider 限制或允许 Role Provider」（`.../Domain/Permissions/AdministrationPermissionDataSeedContributor.cs:37-55`）。

**角色与菜单的唯一正确链路**：

```
Role → PermissionGrant → 菜单.Permission 字符串匹配 → 服务端过滤后可见
```

不存在 `RoleMenu` 表；菜单实体上也没有「角色列表」。配置新菜单的顺序是：先有 `PermissionDefinition` → 再在角色权限中勾选 → 最后把 `SysMenu.Permission` 填成**同一字符串**（逐字符比对，大小写敏感，`MenuAppService.cs:154`）。

### 3.3 组织机构（Organization Unit）

| 项 | 事实 |
|----|------|
| 存储 | Identity 服务（`AbpOrganizationUnits`） |
| 控制器 | `src/services/identity/src/Meta.Dow.IdentityService.HttpApi/OrganizationUnits/OrganizationUnitController.cs:12`，路由 `api/identity/organization-units` |
| 端点 | `GET all`、`GET`（分页）、`GET {id}`、`POST`、`PUT {id}`、`DELETE {id}`、`PUT {id}/move`、`GET/PUT {id}/members`、`DELETE {id}/members/{memberId}`、`GET/PUT {id}/roles`、`DELETE {id}/roles/{roleId}`、`GET available-users`、`GET available-roles`（同文件 `:22-133`） |
| 权限 | `AbpIdentity.OrganizationUnits`（列表）、`.ManageOU`（结构）、`.ManageMembers`、`.ManageRoles`（`.../Application/OrganizationUnits/OrganizationUnitAppService.cs:13,75,91,103,109,152,164,192,202,208,229`） |
| 与数据权限 | `DataScopeResolver` 读取用户所属 OU 计算部门范围（§3.7） |
| 与菜单 | OU **不控制侧栏菜单**；只参与数据范围计算 |

### 3.4 功能权限（Permission）

- 定义方式：各模块 `PermissionDefinitionProvider`（见 §2 表）。
- 授予来源：Role（主路径）、User（额外权限）、Client（OpenIddict，`src/shared/Meta.Dow.DbMigrator/OpenIddictDataSeeder.cs:296`）。
- 校验方式：
  - 后端：`[Authorize("权限名")]` 或 `IPermissionChecker.IsGrantedAsync`。全仓 `IsGrantedAsync` 仅 4 处：菜单过滤（`MenuAppService.cs:154`）、编排节点权限（`.../SaaS.Application/Orchestration/FlowExecutor.cs:464`）、编排 SQL 写权限（`FlowExecutor.cs:2230`、`ResourceCrudExecutor.cs:539`）。
  - 前端：`application-configuration.auth.grantedPolicies` → access codes → `AccessControl`。
- **仅藏菜单不够**：菜单过滤是「显示层」，服务端 API 上的 `[Authorize]` 才是「执行层」。菜单增删改在服务端强制（`MenuAppService.cs:51,92,119`），不依赖前端。

### 3.5 菜单契约（`SysMenu`）

实体 `src/services/administration/src/Meta.Dow.Administration.Domain/Menus/SysMenu.cs`：

| 字段 | 类型 | 说明 | 行号 |
|------|------|------|------|
| `TenantId` | `Guid?` | `IMultiTenant` | `:10` |
| `ParentId` | `Guid?` | 树结构 | `:12` |
| `Name` | `string` | 路由名，必填，≤128 | `:14`、`:73-76` |
| `Title` | `string` | 显示标题，必填，≤256 | `:16`、`:78-81` |
| `Path` | `string?` | 路径，≤256 | `:18` |
| `Component` | `string?` | Vben 组件，≤256 | `:20` |
| `Redirect` | `string?` | ≤256；为空且有子级时自动指向首个子路由 | `:22`；`MenuAppService.cs:205-207` |
| `Type` | `MenuType` | `Directory=0` / `Menu=1` / `Button=2` | `:24`；`.../Domain.Shared/Menus/MenuType.cs:5-7` |
| `Permission` | `string?` | 权限名，≤256，**可空** | `:26` |
| `Icon` | `string?` | ≤128 | `:28` |
| `SystemCode` | `string` | 多前端系统隔离，**默认 `MetaDow`**，必填，≤64 | `:30`；`.../Domain.Shared/Menus/MenuConsts.cs:5` |
| `Order` | `int` | 排序（先 `Order` 再 `Name`） | `:32`；`MenuAppService.cs:137-138` |
| `IsVisible` | `bool` | **默认 `true`** | `:34` |
| `IsEnabled` | `bool` | **默认 `true`**；仅 `true` 参与路由 | `:36`；`MenuAppService.cs:136` |
| `AffixTab` | `bool` | 默认 `false` | `:38` |
| `KeepAlive` | `bool` | 默认 `false`；种子里仅 `Menu` 类型会置 `true` | `:40`；`MenuDataSeedContributor.cs:764` |

- 构造函数**不接收** `Redirect`/`IsVisible`/`IsEnabled`/`AffixTab`/`KeepAlive`，这些只能在 `Update(...)` 中赋值（`:44-71` vs `:83-116`）——`CreateAsync` 因此先构造再立即 `Update`（`MenuAppService.cs:56-86`）。
- **没有** `RoleMenu` 表，菜单实体上也没有角色集合。
- `MenuDto` 在实体字段之外另带 `Children`，且继承 `ExtensibleFullAuditedEntityDto<Guid>`（`.../Application.Contracts/Menus/MenuDto.cs:8,40`）。
- `CreateUpdateMenuDto` 默认值：`Type = MenuType.Menu`、`SystemCode = MenuConsts.DefaultSystemCode`（`MetaDow`）、`IsVisible = true`、`IsEnabled = true`、`AffixTab = false`、`KeepAlive = false`（`.../Application.Contracts/Menus/CreateUpdateMenuDto.cs:28,38,42-48`）。

**种子事实**（`MenuDataSeedContributor.cs`）：

- 首次种子（`:45-188`）建立 4 个根级目录：`Dashboard`、`AbpAdmin`、`IamAdmin`、`Orchestration`（`Orchestration` 在 `:195-218`）。**这 4 个目录的 `Permission` 均为 `null`**，因此对任何已登录用户可见（§3.6 第 4 步的「`Permission` 为空即放行」）。
- 叶子菜单绑定：`AbpIdentity.Users`、`AbpIdentity.Roles`、`AbpIdentity.OrganizationUnits`、`AbpTenantManagement.Tenants`、`SettingManagement.Emailing`、`Administration.Menus`、`Administration.DataScopes`（`:89-185`）。
- `Orchestration` 下的叶子绑定 `Orchestration.*` 权限名（`:220-614`）。
- 幂等补齐：`EnsureEmailSettingsMenuAsync`（`:617-664`）、`EnsureMenuKeepAliveAsync`（`:666-690`，把所有 `Menu` 类型菜单的 `KeepAlive` 强制置 `true`）、`RemoveDemoMenusAsync`（`:692-717`，删除 Vben 演示菜单）。

### 3.6 菜单运行时过滤（逐步按代码写实）

`MenuAppService.GetCurrentUserRoutesAsync`（`MenuAppService.cs:131-179`）：

1. `systemCode ??= MenuConsts.DefaultSystemCode`（`:133`）——不传参时只返回 `MetaDow` 系统菜单。
2. 取 `SystemCode == systemCode && IsEnabled && Type != Button`，按 `Order`、`Name` 排序（`:136-139`）。
3. 若 `IsVisible == false` **且** `Permission` 为空 → 直接跳过（`:145-148`）。
4. 否则满足以下任一条件即加入：`admin` 角色（不分大小写）/ `Permission` 为空 / `await _permissionChecker.IsGrantedAsync(menu.Permission)` 为真（`:151-156`）。
5. 沿 `ParentId` 向上补全父目录，直到根（`:161-176`）。父目录**不检查** `Permission`；它也**不因 `IsVisible=false` 被排除**（只要它通过了第 2 步的 `IsEnabled` 过滤，就会进入 `byId` 字典被补上）。
6. 映射为 Vben 路由（`:192-223`）：
   - `Meta.HideInMenu = true` 当且仅当 `IsVisible == false`（`:215`）；
   - `Meta.Authority = [Permission]`，为空则不下发（`:216`）；
   - `Meta.AffixTab`/`Meta.KeepAlive` 仅在为真时下发（`:213-214`）；
   - `Meta.Order` 取 `Order`（`:212`）；
   - `Redirect` 未配置且有子路由时自动指向第一个子路由（`:205-207`）；
   - `Component` 为空则不下发（`:204`）。

路径规范化（`ToVbenPath`，`:228-255`）：顶级路径补 `/` 前缀；子级若与父 `Path` 同前缀则裁剪为相对路径；子级为绝对路径时只取末段；父 `Path` 为空则原样返回。

**方法级授权**：`GetCurrentUserRoutesAsync` 只吃类级 `[Authorize]`（需登录），**没有**功能权限要求（`MenuAppService.cs:15`、`:131`）；而管理用的 `GetListAsync`/`GetAsync` 需要 `Administration.Menus`（`:30,44`）。

**前端消费**：`GET /api/menu/all`；`accessMode: 'backend'`，登录后由服务端结果生成路由。前端在独立仓库（本仓同级 `../vue-demo`，web-antd），本文涉及的三个文件已核对存在：`apps/web-antd/src/api/core/menu.ts`（第 9 行请求 `/api/menu/all`）、`apps/web-antd/src/router/access.ts`（第 32 行调用 `getAllMenusApi()`）、`apps/web-antd/src/preferences.ts`（第 24 行 `accessMode: 'backend'`）。前端其余细节未核验。

### 3.7 数据权限（Data Scope）

范围枚举 `src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/DataScope.cs:6-22`：

| 值 | 数值 | 含义 |
|----|------|------|
| `Self` | 0 | 仅本人创建 |
| `Department` | 1 | 本部门（用户直接所属 OU） |
| `DepartmentAndChildren` | 2 | 本部门及下级（按 OU `Code` 前缀展开） |
| `Custom` | 3 | 指定组织 ID 集合 |
| `All` | 4 | 全部 |

**解析规则**（`.../Application/DataPermission/DataScopeResolver.cs:40-141`）：

1. 无当前用户 → `DataScopeResult.Deny(null)`（`:45-49`）。
2. `resource` 为空/空白 → 取 `DataPermissionConsts.DefaultResource`，即 `"*"`（`:51`；`.../Domain.Shared/DataPermission/DataPermissionConsts.cs:6`）。
3. **硬编码角色名放行**：当前用户角色中含 `"admin"`（不分大小写）→ `AllowAll`（`:53-56`）。
4. 用户实体查不到 → `Deny(userId)`（`:58-62`）。
5. 取用户角色 ID 集合，查 `RoleDataScope`：`Resource == resource` **或** `Resource == "*"`（`:64-70`），字符串精确比较（大小写敏感）。
6. 无匹配策略 → `Deny(userId)`（`:72-75`）。
7. **精确资源优先**：存在 `Resource == resource` 的策略时只用这批，否则退回 `"*"` 那批（`:77-82`）。
8. **最宽策略**：按 `(int)Scope` 降序取第一个（`All=4` 最大）——多角色/多策略取最宽（`:84`）。
9. `All` → `AllowAll`（`:86-89`）；`Self` → `{ Scope = Self, UserId }`（`:91-94`）。
10. `Custom` → 把第 7 步那批中**所有** `Scope == Custom` 策略的 `OrganizationIds` 取并集、去重（`:96-109`）。
11. `Department` → 用户所属 OU ID 直传；**用户不在任何 OU** 时 `Deny`（`:111-125`）。
12. `DepartmentAndChildren` → 拉取全部 OU（租户过滤由仓储自动施加），以用户 OU 的 `Code` 为前缀展开（`:127-140`）。

**过滤扩展**（`src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/DataPermissionQueryableExtensions.cs`）：

| 输入 | 行为 | 行号 |
|------|------|------|
| `All` | 原样返回 | `:18` |
| `Self` 且无 `UserId` | `Where(_ => false)` | `:29-32` |
| `Self` 且实体未实现 `IHasCreatorId` | `Where(_ => false)` | `:34-37` |
| `Self` 正常 | `x.CreatorId == UserId` | `:39-44` |
| `Department`/`DepartmentAndChildren`/`Custom` 且组织集合为空 | `Where(_ => false)` | `:50-53` |
| 同上且集合非空 | `x.OrganizationId != null && ids.Contains(x.OrganizationId.Value)` | `:55` |
| 其余（枚举未覆盖值） | 退化为 `Self` | `:22` |

所有「信息不足」的分支都是**收紧**（返回空集），不存在因缺数据而放行。`Deny` 的语义是「退化为 Self」，而不是「拒绝请求」（`DataScopeResult.cs:14-20`）。

`RoleDataScope` 实体（`.../Domain/DataPermission/RoleDataScope.cs`）：`RoleId` + `Resource`（默认 `"*"`，≤128）+ `Scope` + `OrganizationIds`；`SetScope` 会在**非 `Custom`** 时清空 `OrganizationIds`（`:53-57`）。DTO 侧 `SetRoleDataScopeDto` 只对 `RoleId`（`[Required]`）和 `Resource`（`[Required]` + ≤128）做校验（`.../Application.Contracts/DataPermission/RoleDataScopeDto.cs:21-33`）。

**接口路径（已逐个到 controller 核实）**——路由是 `api/administration/role-data-scopes`，**不是** `data-scopes`（`.../HttpApi/DataPermission/RoleDataScopeController.cs:13`）：

| 方法与路径 | 权限 | 行号 |
|-----------|------|------|
| `GET /api/administration/role-data-scopes/by-role/{roleId}` | `Administration.DataScopes` | `:21-26` |
| `PUT /api/administration/role-data-scopes` | `Administration.DataScopes.Manage` | `:28-32` |
| `DELETE /api/administration/role-data-scopes/{id}` | `Administration.DataScopes.Manage` | `:34-39` |
| `GET /api/administration/role-data-scopes/my` | 仅需登录（无功能权限） | `:41-46` |

### 3.8 多租户

| 场景 | 行为 | 证据 |
|------|------|------|
| 总开关 | `IsEnabled = true` | `src/shared/Meta.Dow.Shared/Meta.Dow/MultiTenancy/MultiTenancyConsts.cs:5` |
| 生效点 | `AbpMultiTenancyOptions.IsEnabled` | `src/shared/Meta.Dow.Hosting.Shared/Meta.Dow/MetaDowHostingModule.cs:50` |
| 租户解析 | `__tenant` 头 / `{tenant}.domain` 子域名 / `?__tenant=` | ABP 约定 |
| 租户隔离实体 | `SysMenu`、`RoleDataScope` 均实现 `IMultiTenant` 并显式赋值 `CurrentTenant.Id` | `SysMenu.cs:10`；`RoleDataScope.cs:14`；`RoleDataScopeAppService.cs:48` |
| 创建租户时种子管理员 | 从事件属性取邮箱/口令，取不到则回退**硬编码模板值** | `src/services/identity/host/Meta.Dow.IdentityService.HttpApi.Host/EventHandler/TenantCreatedEventHandler.cs:30-38`（§4.4-10） |
| 创建租户时授权 | 复用 `AdministrationPermissionDataSeedContributor`，按 `Tenant` 侧授全量可授权限 | `.../Domain/Permissions/AdministrationPermissionDataSeedContributor.cs:37-55` |
| 菜单种子 | `MenuDataSeedContributor` 在传入的租户上下文内执行，但「首次全量种子」以 `GetCountAsync() == 0` 为条件，是**全库级**判断 | `.../Domain/Menus/MenuDataSeedContributor.cs:31-43` |

租户切换后必须重新拉 `application-configuration` 与菜单。

---

## 4 行为边界与已知缺陷

格式：`【严重度】现象 — 机理 — file:line — 后果/规避`

### 4.1 数据范围（严重）

1. **【严重】数据范围完全没有作用于业务数据 —— 行级过滤未接通。**
   现象：配置了 `RoleDataScope` 后，任何列表接口返回的行数不变。
   机理：`ApplyDataPermission` 全仓**零调用点**（检索仅命中其自身定义）；唯一实现 `IHasOrganizationId` 的业务实体是示例类 `SampleRecord`，其 XML 注释里的调用示例本身也未执行。
   证据：`src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/DataPermissionQueryableExtensions.cs:10`（定义）、`src/services/projects/src/Meta.Dow.Projects.Domain/Samples/SampleRecord.cs:9,11`（唯一实现）。
   后果：`RoleDataScope` 的实际作用是**把范围回传给前端**（`GET .../role-data-scopes/my`），属展示性/约定性能力，不构成安全边界。任何「数据权限已生效」的表述都是失真的。
   规避：把数据范围当作「已建模、未接线」，不要依赖它做隔离；接线方式见 §8 P1。

2. **【严重】`DataScopeResolver` 的唯一消费者是「我的」接口。**
   机理：`IDataScopeResolver` 仅被 `RoleDataScopeAppService.GetMyAsync` 注入（`.../Application/DataPermission/RoleDataScopeAppService.cs:16,67-76`）。
   证据：同文件 `:69`。
   后果：解析逻辑（部门树展开、最宽策略、`Custom` 并集）目前只影响一个只读响应 DTO。

3. **【高】`admin` 角色在数据范围上绕过全部策略配置。**
   现象：把 `admin` 角色范围配成 `Self`，实际仍得到 `All`。
   机理：`DataScopeResolver` 在读取任何 `RoleDataScope` **之前**就按角色名放行（`.../Application/DataPermission/DataScopeResolver.cs:53-56`）。
   后果：授权配置与实际行为不符；`admin` 无法被限制。
   规避：文档与 UI 都应明确「`admin` 恒为 `All`」。

### 4.2 菜单（中）

4. **【中】菜单可见性存在数据依赖的旁路。**
   现象：`IsVisible = false` 的菜单，前端仍会注册其路由。
   机理：§3.6 第 3 步只跳过「不可见**且无权限**」的节点；「不可见但有权限」的节点会被加入结果并下发 `Meta.HideInMenu = true`（`MenuAppService.cs:145-148`、`:215`）。种子中大量「设计器/运行时」菜单正是这种形态（如 `OrchestrationTableDesigner`、`AppResourceRuntime`，`MenuDataSeedContributor.cs:399-414,449-464`）。
   后果：隐藏 ≠ 不可达——知道路径即可直达页面（页面内 API 仍受功能权限保护）。
   规避：把「不想被直达」的页面按功能权限控制，而不是只靠 `IsVisible`。

5. **【中】`admin` 角色在菜单过滤中同样硬编码放行，与「权限授予」构成双轨。**
   机理：`IsAdminUser()` 直接比对角色名 `"admin"`（`MenuAppService.cs:27-28`），在过滤中短路（`:151-153`）。
   证据：同文件。
   后果：即使 `admin` 角色的权限被清空，菜单仍全量可见；反之自定义超管角色（如 `superadmin`）拿不到这一豁免，只能靠权限授予。两处「admin 特权」（菜单 + 数据范围）彼此独立，容易漂移。

6. **【低】父目录补全不复查可见性。**
   机理：`:161-176` 只按 `ParentId` 向上加节点，不复查 `IsVisible`；父目录自身 `IsVisible=false` 时仍会作为目录结构下发。
   后果：可能产生前端侧「空目录」或结构异常，具体取决于前端渲染（前端未核验）。
   补充：4 个根目录（`Dashboard`/`AbpAdmin`/`IamAdmin`/`Orchestration`）的 `Permission` 均为空，因此它们对**任何已登录用户**可见（§3.5）。

### 4.3 组织树与资源匹配（中）

7. **【中】`DepartmentAndChildren` 的 OU 展开用字符串前缀，缺少分隔符边界。**
   现象：`Code = "00001"` 的节点会同时匹配 `"000011"`（不同分支）。
   机理：`u.Code.StartsWith(code, StringComparison.Ordinal)`，未校验 ABP 组织树 `Code` 的层级分隔符（`.../Application/DataPermission/DataScopeResolver.cs:127-133`）。
   后果：若租户的 OU `Code` 未带分隔符（或被改为纯数字串），部门范围会**多包含**不相干分支。ABP 默认在层级间插入分隔符，因此默认形态下不触发。
   规避：按分隔符边界匹配（`code + separator` 前缀，或直接走 OU 父子关系）。

8. **【中】资源键匹配大小写敏感且无校验。**
   机理：`x.Resource == resource` 为精确字符串比较（`DataScopeResolver.cs:68`）；`RoleDataScopeAppService.SetAsync` 不校验 `RoleId` 是否存在，也不校验 `Resource` 是否符合调用方命名约定（`RoleDataScopeAppService.cs:32-59`、`RoleDataScopeDto.cs:21-33`）。
   后果：资源名拼写/大小写不一致时策略静默不命中 → 回落 `"*"` 或 `Deny`；角色删除后 `RoleDataScope` 成为孤儿记录（无级联清理代码）。

### 4.4 安全与运维（中）

9. **【中】全部 5 个承载进程开启 `IdentityModelEventSource.ShowPII = true`。**
   证据：`src/apps/Meta.Dow.AuthServer/MetaDowAuthServerModule.cs:138`、`src/services/administration/host/Meta.Dow.Administration.HttpApi.Host/AdministrationHttpApiHostModule.cs:96`、`src/services/identity/host/Meta.Dow.IdentityService.HttpApi.Host/IdentityServiceHttpApiHostModule.cs:101`、`src/services/saas/host/Meta.Dow.SaaS.HttpApi.Host/SaaSHttpApiHostModule.cs:74`。
   后果：令牌内容、claims、PII 进入日志（再流向 Seq / OTLP）。属调试期设置，生产应按环境开关。

10. **【中】租户管理员口令存在公开硬编码回退值，且错误路径未实现。**
    现象：创建租户的事件里，若调用方未提供管理员邮箱/口令，就使用模板残留值 `admin@antosubash.com` / `1q2w3E*`。
    机理：`?? "admin@antosubash.com"`、`?? "1q2w3E*"`（`src/services/identity/host/Meta.Dow.IdentityService.HttpApi.Host/EventHandler/TenantCreatedEventHandler.cs:33,35`）。
    后果：任何知道该公开口令的人可尝试登录「未显式传参创建」的租户管理员账号（等同默认口令）。同文件 `HandleErrorTenantCreatedAsync` 直接 `throw new NotImplementedException()`（`:47-50`）——一旦种子抛 `InvalidOperationException`，错误处理自身再抛异常，租户创建链路以未处理异常收场。
    规避：租户创建强制显式传入随机强口令；补齐错误处理分支（记录 + 告警 + 可重试）。

11. **【低】网关受众校验关闭是有意设计，不是缺陷。**
    事实：默认 `ValidateAudience = false`（`src/gateway/Meta.Dow.Gateway/Program.cs:48-57`），只校验签名 / 签发者 / 有效期；各服务在自己的认证配置里以服务专属受众把关（`ConfigureMicroservice(MetaDowNames.XxxApi)` → `options.Audience = audience`；`src/shared/Meta.Dow.Microservice.Shared/Meta.Dow/MetaDowMicroserviceModule.cs:77-95`，调用点 `IdentityServiceHttpApiHostModule.cs:41`、`AdministrationHttpApiHostModule.cs:45`、`ProjectsHttpApiHostModule.cs:33`、`SaaSHttpApiHostModule.cs:35`）。
    机理：`Program.cs:38-47` 的注释记录了一次真实事故——写过单一 `Audience` 并开启校验后，**所有有效令牌都被判无效**（OpenIddict 把「作用域关联的资源名」写入 `aud`，与解决方案名 `Meta.Dow` 不同），表现为网关对所有需认证请求返回 401。
    边界：网关 `appsettings.json` 定义了 13 条路由（`:11-89`），其中只有 `administration-fallback`（`{**catch-all}`）挂了 `AuthorizationPolicy: "Default"`（`:83-89`），其余 12 条 API 路由在网关侧**不做**授权，完全依赖下游各自的 `[Authorize]`。因此不能把网关当作 API 授权层。
    若要改为网关强校验，只需在 `AuthServer:ValidAudiences` 显式列出允许的受众。

12. **【低】服务间从未使用 HTTP 客户端代理。**
    事实：`Meta.Dow.Administration.HttpApi.Client`、`Meta.Dow.IdentityService.HttpApi.Client`、`Meta.Dow.Projects.HttpApi.Client`、`Meta.Dow.SaaS.HttpApi.Client` 四个模块存在，但 4 个 Host 的 `[DependsOn]` 列表中**都没有**它们。
    边界：菜单与权限的实际形态是**各服务直连共享 Mongo 库**（Administration / Identity / SaaS 之间共享 `MetaDowAdministrationDb`、`MetaDowIdentityServiceDb`、`MetaDowSaaSDb`），不是跨服务 HTTP 调用。文档中不应表述为「通过客户端代理跨服务取权限」。

### 4.5 接口作用域说明

13. **【低】`GetListAsync` 返回的是未按权限过滤的完整菜单树。**
    机理：它只按 `SystemCode` 过滤，不做权限判定，包含 `Button` 节点（`MenuAppService.cs:30-42`），且需要 `Administration.Menus` 权限。
    后果：「管理用菜单树」与「我的菜单」是两个不同接口，不可混用；前者不能作为前端侧栏数据源。

---

## 5 配置清单（落地检查）

新增一个受控业务页时：

1. 在对应模块的 `PermissionDefinitionProvider` 增加 `PermissionDefinition`（含 CRUD 子权限）。
2. 把权限授给需要的角色：`admin` 由 `AdministrationPermissionDataSeedContributor` 自动全量授予；其它角色走 PermissionManagement。
3. 菜单管理新增叶子：`Name` 唯一，`SystemCode` 与目标前端一致（默认 `MetaDow`），`Permission` = 定义名（同一字符串），`Component`/`Path` 指向 Vue 页面，`Type = Menu`。
4. API 方法加 `[Authorize("权限名")]`——**这是唯一真正的执行层**。
5. 前端按钮用同一权限名控制显示。
6. 若要做行级隔离：**当前没有可用机制**（§4.1）。需先完成 §8 P1 的接线，再谈「配置数据范围」。
7. 验证：用非 `admin` 角色登录 → 无权限时无菜单且 API 返回 403；有权限时可见可调。注意 `admin` 角色在菜单与数据范围上都有硬编码豁免，**不能用它做回归验证**。

**契约要点速查**：

- 菜单过滤发生在**服务端**，每次 `GET /api/menu/all` 实时判定；角色授权变更后前端**重新拉菜单**即可看到变化（无需重新登录），但 `grantedPolicies`（按钮权限）需重新获取 `application-configuration`。
- 权限判定单位为「权限名字符串」，必须逐字符一致：同一个字符串同时是 `PermissionDefinition` 名、`SysMenu.Permission`、前端 `AccessControl` 的 code。
- 非 `admin` 用户的菜单过滤对每个带 `Permission` 的节点调用一次 `IPermissionChecker`（N 次判定，非批量）。

---

## 6 端到端示例（每个接口路径均已到 controller 核实）

**需求**：业务员可进「用户」菜单，能改不能删。

1. **权限定义**（开源 Identity 模块已提供）：`AbpIdentity.Users`、`AbpIdentity.Users.Update`、`AbpIdentity.Users.Delete`。
2. **角色**：`POST /api/identity/roles` 创建 `sales`（网关路由 `appsettings.json:17-22`）。
3. **授权**：`PUT /api/permission-management/permissions`（网关 `appsettings.json:77-82`）勾选 `AbpIdentity.Users` + `.Update`，**不**勾 `.Delete`。
4. **菜单**：种子已存在 `AbpIdentityUsers`，其 `Permission = "AbpIdentity.Users"`、`Path = /abp/identity/users`（`MenuDataSeedContributor.cs:89-100`），无需新建。
5. **用户归属**：`PUT /api/identity/users/{userId}/organization-units`（前端调用点 `../vue-demo/apps/web-antd/src/api/identity/organization-units.ts:147`）。
6. **菜单可见性验证**：`GET /api/menu/all`（`.../HttpApi/Menus/MenuController.cs:71-76`）→ `sales` 登录时应能拿到 `AbpIdentityUsers` 节点，且 `meta.authority = ["AbpIdentity.Users"]`。
7. **API 层验证**：`DELETE /api/identity/users/{id}` 由开源 Identity 控制器按 `AbpIdentity.Users.Delete` 判定 → `sales` 应被拒绝。
8. **行级过滤**：❌ 未实现。配置 `PUT /api/administration/role-data-scopes` 后，`GET /api/administration/role-data-scopes/my` 会返回 `Department` + 组织 ID，但**没有任何用户列表接口读取它**。

**实际结果**：有侧栏「用户」、可编辑、无删除按钮（前端 `AccessControl` 用 `AbpIdentity.Users.Delete`）；删除接口被拒；**列表仍返回全部用户**（第 8 步缺失）。

---

## 7 代码索引

| 主题 | 路径 |
|------|------|
| 菜单实体 / 常量 / 类型 | `src/services/administration/src/Meta.Dow.Administration.Domain/Menus/SysMenu.cs`；`src/services/administration/src/Meta.Dow.Administration.Domain.Shared/Menus/MenuConsts.cs`、`MenuType.cs` |
| 菜单过滤与路由映射 | `src/services/administration/src/Meta.Dow.Administration.Application/Menus/MenuAppService.cs` |
| 菜单 HTTP 入口 | `src/services/administration/src/Meta.Dow.Administration.HttpApi/Menus/MenuController.cs` |
| 菜单 DTO | `src/services/administration/src/Meta.Dow.Administration.Application.Contracts/Menus/MenuDto.cs`、`CreateUpdateMenuDto.cs`、`VbenRouteDto.cs` |
| 菜单种子 | `src/services/administration/src/Meta.Dow.Administration.Domain/Menus/MenuDataSeedContributor.cs` |
| Administration 权限 | `src/services/administration/src/Meta.Dow.Administration.Application.Contracts/Permissions/AdministrationPermissions.cs`、`AdministrationPermissionDefinitionProvider.cs` |
| 权限种子（admin 全量） | `src/services/administration/src/Meta.Dow.Administration.Domain/Permissions/AdministrationPermissionDataSeedContributor.cs` |
| 数据范围解析 | `src/services/administration/src/Meta.Dow.Administration.Application/DataPermission/DataScopeResolver.cs` |
| 数据范围应用服务 / 控制器 | `.../Application/DataPermission/RoleDataScopeAppService.cs`；`.../HttpApi/DataPermission/RoleDataScopeController.cs` |
| 数据范围实体 / 常量 / DTO | `.../Domain/DataPermission/RoleDataScope.cs`；`.../Domain.Shared/DataPermission/DataPermissionConsts.cs`；`.../Application.Contracts/DataPermission/RoleDataScopeDto.cs` |
| 共享数据权限（枚举 / 结果 / 扩展 / 标记接口） | `src/shared/Meta.Dow.Shared/Meta.Dow/DataPermission/{DataScope,DataScopeResult,DataPermissionQueryableExtensions,IHasOrganizationId,IHasCreatorId}.cs` |
| 多租户开关 | `src/shared/Meta.Dow.Shared/Meta.Dow/MultiTenancy/MultiTenancyConsts.cs` |
| 租户创建事件（Identity Host） | `src/services/identity/host/Meta.Dow.IdentityService.HttpApi.Host/EventHandler/TenantCreatedEventHandler.cs` |
| 组织机构 API | `src/services/identity/src/Meta.Dow.IdentityService.HttpApi/OrganizationUnits/OrganizationUnitController.cs`；`.../Application/OrganizationUnits/OrganizationUnitAppService.cs` |
| Identity OU 权限 | `src/services/identity/src/Meta.Dow.IdentityService.Application.Contracts/Permissions/IdentityServicePermissions.cs`、`IdentityServicePermissionDefinitionProvider.cs` |
| Orchestration 权限 | `src/services/saas/src/Meta.Dow.SaaS.Application.Contracts/Permissions/OrchestrationPermissions.cs`、`SaaSPermissionDefinitionProvider.cs` |
| 网关路由与认证 | `src/gateway/Meta.Dow.Gateway/appsettings.json`、`Program.cs` |
| 服务侧认证配置（受众） | `src/shared/Meta.Dow.Microservice.Shared/Meta.Dow/MetaDowMicroserviceModule.cs` |
| OpenIddict 客户端与客户端权限 | `src/shared/Meta.Dow.DbMigrator/OpenIddictDataSeeder.cs`、`src/shared/Meta.Dow.DbMigrator/appsettings.json` |
| 唯一实现 `IHasOrganizationId` 的示例实体 | `src/services/projects/src/Meta.Dow.Projects.Domain/Samples/SampleRecord.cs` |
| 前端（**仓库外**，仅核验存在性） | `../vue-demo/apps/web-antd/src/api/core/menu.ts`、`src/router/access.ts`、`src/preferences.ts`、`src/api/administration/menus.ts`、`src/api/administration/data-scopes.ts`、`src/api/identity/{users,roles,organization-units}.ts`、`src/components/permission/permission-tree-select.vue`、`src/components/permission/role-permission-modal.vue`、`src/views/abp/identity/{users,roles,organization-units}.vue`、`src/views/abp/settings/emailing.vue`、`src/views/iam/menus/index.vue`、`src/views/iam/data-scopes/index.vue` |

---

## 8 规划（未实现）

| # | 目标 | 现状 | 落地要点 |
|---|------|------|----------|
| P1 | 行级数据权限真正生效 | ❌ `ApplyDataPermission` 无调用点（§4.1） | 业务实体实现 `IHasOrganizationId`（需要时加 `IHasCreatorId`）；列表查询处注入 `IDataScopeResolver` 并 `query.ApplyDataPermission(await resolver.ResolveAsync("<资源名>"))`；补集成测试证明「配置 `Self` 后行数下降」 |
| P2 | 资源名注册与校验 | ❌ `Resource` 为自由字符串，大小写敏感、无角色存在性校验（§4.3-8） | 引入资源常量类；`SetAsync` 校验 `RoleId` 存在、`Resource` 属于白名单；角色删除时清理 `RoleDataScope` |
| P3 | `DepartmentAndChildren` 按分隔符边界展开 | ❌ 前缀匹配（§4.3-7） | 依据 OU `Code` 的层级分隔符做边界匹配，或直接走 OU 父子关系 |
| P4 | 去掉 `admin` 硬编码豁免 | ❌ 两处独立判定（§4.1-3、§4.2-5） | 改为按「是否拥有全部权限」或显式配置的超管角色名集合判定；保证菜单与数据范围使用同一判定 |
| P5 | 租户管理员凭据与错误路径 | ❌ 硬编码回退口令 + `NotImplementedException`（§4.4-10） | 强制显式传参并生成随机强口令；实现 `HandleErrorTenantCreatedAsync`（记录、告警、可重试） |
| P6 | `ShowPII` 按环境开关 | ❌ 5 个进程均为 `true`（§4.4-9） | 仅开发环境开启；生产关闭 |
| P7 | 服务间调用形态 | ❌ Host 未 `DependsOn` `*.HttpApi.Client`（§4.4-12） | 若确需跨服务调用，显式引用客户端模块；否则在文档中固定「共享库」形态 |
| P8 | 按钮级菜单（`MenuType.Button`）落地 | ⚠️ 枚举与侧栏过滤已支持，种子中零使用 | 前端按钮当前用 `AccessControl` + 权限码，不依赖 `SysMenu.Button`；若引入，需先定义其在前端的消费方式 |
| P9 | 隐藏菜单的直达防护 | ⚠️ `IsVisible=false` + 有权限 ⇒ 仍注册路由（§4.2-4） | 需要「不可直达」的页面按其功能权限收口，或在前端路由守卫中额外校验 |

---

## 9 修订记录

| 日期 | 代码基线 | 改了什么 |
|------|----------|----------|
| 2026-10-13 | `267d375` | 按代码逐条重写为「事实契约 + 规划」双段式。全文断言附 `file:line`；数据范围如实降级为「已建模、未接线」；端到端示例的接口路径全部到 controller 核实；`file:///d:/Project/...` 与 `D:\Project\...` 改为相对路径或明确标注仓库外；删除与代码矛盾或纯重复的段落。 |

**删除/修正的失真断言清单**：

| 原断言 | 代码真相 | 证据 |
|--------|----------|------|
| 数据权限接口前缀 `/api/administration/data-scopes/*` | 实际为 `/api/administration/role-data-scopes` | `RoleDataScopeController.cs:13` |
| 「业务查询：实体实现 `IHasOrganizationId`，使用 `ApplyDataPermission` 扩展过滤」 | 无任何调用点；`IHasOrganizationId` 仅 1 个示例实体 | `DataPermissionQueryableExtensions.cs:10`；`SampleRecord.cs:11` |
| 「数据权限：`RoleDataScope` + `DataScopeResolver`」被列为已落地能力 | 仅为模型 + 只读回传；对业务数据零影响 | `RoleDataScopeAppService.cs:16,67-76` |
| 「数据范围无权限时 → 空列表或拒绝」 | 当前口径是「范围只回传前端」；`Deny` 语义是 Self 而非拒绝 | `DataScopeResult.cs:14-20`；`DataPermissionQueryableExtensions.cs:22` |
| 「`Custom` 指定组织 ID 集合」（未说明并集与最宽策略） | 精确资源优先 + `(int)Scope` 降序取最宽 + `Custom` 取并集去重 | `DataScopeResolver.cs:77-109` |
| 「`admin` 角色 → `All`」未说明是硬编码角色名 | 在读策略**之前**按角色名短路；菜单侧另有独立硬编码 `IsAdminUser()` | `DataScopeResolver.cs:53-56`；`MenuAppService.cs:27-28` |
| 未提及菜单过滤对 `admin` 短路 | 存在，且是显式设计（注释「admin 默认可见全部启用菜单」） | `MenuAppService.cs:150-156` |
| 「按钮｜填子权限｜不进侧栏；供页面按钮控制（待完善）」 | 枚举与侧栏过滤已支持，但种子零使用；按钮控制实际由前端 `AccessControl` + 权限码完成 | `MenuAppService.cs:136`；`views/abp/identity/users.vue:117,145,150` |
| 「待加强（相对商业版体验）」表中 5 项全标 ✅（自相矛盾） | 拆开：这些是**已实现**的前端行为，移出「待加强」；后端侧对应能力见 §2 | `views/iam/menus/index.vue:242`；`router/routes/modules/abp.ts:1`；`role-permission-modal.vue:158` |
| 「存储｜Identity 服务 MongoDB」等无行号断言 | 补充 `file:line` 或改为可核验表述 | 见 §3、§7 |
| 「前端：`D:\Project\vue-demo`」绝对盘符路径 | 改为仓库外标注（相对本仓 `../vue-demo`） | §1 头部、§3.6、§7、§6 |
| 「当前落地与差距」整章（结论与代码不符、自相矛盾） | 拆为 §2 权威表（事实）+ §8（规划） | — |
| 未提及网关受众校验 | 补 §4.4-11：关闭是**有意设计**（附事故复盘）；13 条路由仅 catch-all 挂策略 | `Program.cs:38-57`；`appsettings.json:11-89` |
| 未提及 `ShowPII` | 补 §4.4-9：5 个承载进程均为 `true` | 各 Host 模块（见该条证据） |
| 未提及租户管理员默认口令 | 补 §4.4-10：`1q2w3E*` / `admin@antosubash.com` 硬编码回退 + `NotImplementedException` | `TenantCreatedEventHandler.cs:33,35,47-50` |
| 未提及服务间调用形态 | 补 §4.4-12：`*.HttpApi.Client` 存在但 4 个 Host 均未引用 ⇒ 实际是共享库形态 | 各 Host `[DependsOn]` 列表 |
| 未提及隐藏菜单旁路、OU 前缀展开、资源名大小写等边界 | 补 §4.2-4/6、§4.3-7/8 | 见各条证据 |
| 保留了原文仍正确的内容 | ABP 商业版能力对照、设计原则、契约与功能权限分离的论证、配置清单 | §1.1、§1.2、§2、§5 |
