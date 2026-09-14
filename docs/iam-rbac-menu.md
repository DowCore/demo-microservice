# Meta.Dow 身份与权限体系说明

> 参考 ABP Commercial（Identity + Permission Management + 导航授权）及常见中后台成熟实践，说明本仓库在 **用户、角色、组织机构、菜单、功能权限、数据权限** 上的目标模型与当前落地方式。  
> 前端：`D:\Project\vue-demo`（Vben Admin，`accessMode: backend`）  
> 后端：`demo-microservice`（Administration / Identity / SaaS）

---

## 1. 设计原则（对齐 ABP 商业版）

| 原则 | 说明 |
|------|------|
| Permission 为唯一功能授权真相 | 不单独维护「角色 ↔ 菜单」授权表；菜单只 **引用** 权限名 |
| 身份与授权分层 | Identity：用户 / 角色 / 组织机构；PermissionManagement：谁有哪些权限；菜单：导航与前端路由 |
| 可见 ≠ 可写 | 侧栏可见用菜单绑定的权限；按钮与 API 用同一权限组的子权限（Create/Update/Delete） |
| 数据权限独立 | 功能权限决定「能否进功能」；数据范围决定「能看哪些行」 |
| 多租户隔离 | Host / Tenant 分库或分租户上下文；租户管理员默认获得租户内适用权限 |

ABP 商业版典型路径：

```
PermissionDefinition（定义）
        ↓
角色 / 用户 PermissionManagement（授权）
        ↓
菜单 / 路由 requiredPolicy 或 RequirePermissions（绑定）
        ↓
运行时 IPermissionChecker + 前端 grantedPolicies（过滤）
```

本方案 **采用同一路径**，动态菜单存储在 Administration 的 `SysMenu`，叶子节点通过 `Permission` 字段绑定权限名。

---

## 2. 总体架构

```
┌─────────────────────────────────────────────────────────────┐
│  Vue Vben（web-antd）                                        │
│  · 登录 → JWT + application-configuration.auth.grantedPolicies│
│  · GET /api/menu/all → 侧栏路由（服务端已按权限过滤）         │
│  · 页面按钮：accessCodes / 权限码（与子权限对齐，待完善）     │
└───────────────────────────┬─────────────────────────────────┘
                            │ Gateway（YARP）
        ┌───────────────────┼───────────────────┐
        ▼                   ▼                   ▼
┌───────────────┐  ┌─────────────────┐  ┌──────────────┐
│ Identity      │  │ Administration  │  │ SaaS         │
│ 用户/角色/OU  │  │ 菜单/设置/数据  │  │ 租户         │
│ 权限定义部分  │  │ 范围/邮件设置   │  │              │
│ Permission    │  │ Permission 管理 │  │              │
│ 存储（共享）  │  │ 菜单过滤 API    │  │              │
└───────────────┘  └─────────────────┘  └──────────────┘
```

| 能力 | 权威模块 | 主要 API 前缀 |
|------|----------|----------------|
| 用户 / 角色 / 组织机构 | Identity（`Volo.Abp.Identity`） | `/api/identity/*` |
| 权限授予 | PermissionManagement | `/api/permission-management/*` |
| 动态菜单 | Administration（自研 `SysMenu`） | `/api/administration/menus/*`、`/api/menu/all` |
| 数据权限 | Administration（`RoleDataScope`） | `/api/administration/data-scopes/*` |
| 租户 | SaaS / TenantManagement | `/api/multi-tenancy/*` |
| 邮件设置 | SettingManagement（Host 在 Administration） | `/api/setting-management/*` |

---

## 3. 用户（User）

### 3.1 ABP 商业版能力

- 用户 CRUD、启用/锁定、重置密码  
- 分配 **角色**、加入 **组织机构**  
- 可对用户做 **额外权限**（User Permission Provider，覆盖或补充角色）  
- 多租户下用户属于当前租户（或 Host 用户）

### 3.2 本仓库约定

| 项 | 说明 |
|----|------|
| 存储 | Identity 服务 MongoDB（`AbpUsers` 等） |
| 前端 | `views/abp/identity/users` |
| 权限 | `AbpIdentity.Users` 及 Create/Update/Delete 子权限 |
| 与菜单关系 | 用户 **不直接** 挂菜单；通过角色权限 → 菜单 `Permission` 间接可见 |
| 租户管理员 | 创建租户时种子用户；在 **该租户上下文** 下登录，Host 用户列表默认看不到 |

### 3.3 运维注意

- Host 与租户用户隔离；切换租户后再管理租户用户。  
- 改用户角色/权限后，目标用户需 **重新获取 application-configuration**（刷新或重登）后侧栏才会变。

---

## 4. 角色（Role）

### 4.1 ABP 商业版能力

- 角色 CRUD、默认角色  
- **权限弹窗**：按权限组勾选（Identity / SaaS / SettingManagement / 业务模块）  
- `admin` 角色通常拥有全部权限（种子）

### 4.2 本仓库约定

| 项 | 说明 |
|----|------|
| 存储 | Identity |
| 前端 | `views/abp/identity/roles` |
| 权限管理 | 应对接 PermissionManagement 弹窗（商业版标准）；当前以角色 + 权限 API 为准 |
| 种子 | Host：`AdministrationPermissionDataSeedContributor` 等；租户创建：`TenantCreatedEventHandler` 授予适用权限 |

### 4.3 角色与菜单（重要）

```
正确：Role → PermissionGrant → 菜单.Permission 匹配 → 可见
错误：Role → MenuId 勾选表（与 ABP 双轨，本方案不采用）
```

配置新菜单时：先确保存在对应 `PermissionDefinition`，再在角色权限中勾选，最后把菜单的 `Permission` 填成 **同一字符串**。

---

## 5. 组织机构（Organization Unit）

### 5.1 ABP 商业版能力

- 树形 OU、用户加入 OU、角色可关联 OU（模块支持范围内）  
- 常用于 **数据范围**（本部门 / 本部门及下级）与汇报线，而不是替代功能权限

### 5.2 本仓库约定

| 项 | 说明 |
|----|------|
| 存储 | Identity（`AbpOrganizationUnits`） |
| API | Identity 自研 `OrganizationUnitAppService`（对齐商业版路由 `/api/identity/organization-units`；开源包无 Pro 控制器） |
| 前端 | 左树 + 右「成员 / 角色」页签 |
| 权限 | `AbpIdentity.OrganizationUnits` / `.ManageOU` / `.ManageMembers` / `.ManageRoles` |
| 与数据权限 | `DataScopeResolver` 读取用户所属 OU，解析 `Department` / `DepartmentAndChildren` / `Custom` |

OU **不直接控制侧栏菜单**；控制「列表能看到哪些业务数据」。

---

## 6. 功能权限（Permission）

### 6.1 定义

在各模块 `PermissionDefinitionProvider` 中声明，例如：

- `AbpIdentity.Users` / `.Create` / `.Update` / `.Delete`  
- `AbpTenantManagement.Tenants.*`  
- `SettingManagement.Emailing` / `.Test`  
- `Administration.Menus.*`、`Administration.DataScopes.*`

### 6.2 授予

| Provider | 含义 |
|----------|------|
| Role | 角色拥有的权限（主路径） |
| User | 用户额外权限（可选） |
| Client | OpenIddict 客户端（API 场景） |

UI 入口（商业版标准）：**身份 → 角色 → 权限**。

### 6.3 校验

- 后端：`[Authorize("权限名")]` 或 `IPermissionChecker.IsGrantedAsync`  
- 前端：`application-configuration.auth.grantedPolicies` → accessCodes  
- **仅藏菜单不够**：API 必须 Authorize，防止直接调接口

---

## 7. 菜单（Menu）与导航授权

### 7.1 数据模型（`SysMenu`）

| 字段 | 说明 |
|------|------|
| `Name` / `Title` | 路由名 / 显示标题 |
| `Type` | Directory / Menu / Button |
| `Path` / `Component` / `Redirect` | Vben 路由 |
| `Permission` | 绑定的 ABP 权限名；**叶子菜单建议必填** |
| `ParentId` / `Order` / `Icon` | 树与展示 |
| `IsVisible` / `IsEnabled` | 可见、启用 |
| `KeepAlive` / `AffixTab` | 前端页签行为 |
| `SystemCode` | 多前端系统隔离，默认 `MetaDow` |

**没有** `RoleMenu` 表；授权不落在菜单实体上的「角色列表」。

### 7.2 运行时过滤（对齐商业版 RequirePermissions）

接口：`GET /api/menu/all`（及 Administration 下 routes）

逻辑概要：

1. 读取启用中的菜单树（排除 Button 类型出侧栏）  
2. 对带 `Permission` 的节点：`IPermissionChecker.IsGrantedAsync`  
3. 子节点有权限时 **补全父级目录**（目录可无 Permission）  
4. 映射为 Vben 路由；`meta.authority` = `[Permission]`（兼容）

前端：`preferences.app.accessMode = 'backend'`，登录后 `getAllMenusApi()` 以服务端结果生成菜单，**不再用前端静态路由做权限菜单源**。

### 7.3 目录与叶子

| 类型 | Permission | 行为 |
|------|------------|------|
| 目录 | 可空 | 有任一授权子节点时出现 |
| 菜单（叶子） | 建议必填 | 未授权则不出现 |
| 按钮 | 填子权限 | 不进侧栏；供页面按钮控制（待完善） |

### 7.4 与商业版对照

| 商业版 | Meta.Dow |
|--------|----------|
| MVC `RequirePermissions` | `SysMenu.Permission` + 服务端过滤 |
| Angular `requiredPolicy` | 动态路由 `meta.authority` + backend 模式 |
| 权限弹窗勾选 | PermissionManagement（角色权限） |
| 菜单管理 | Administration 菜单 CRUD；权限字段使用权限树选择 |

---

## 8. 数据权限（Data Scope）

功能权限解决「能不能进」；数据权限解决「能看哪几行」。

### 8.1 范围枚举

| 值 | 含义 |
|----|------|
| `Self` | 仅本人创建 |
| `Department` | 本部门 |
| `DepartmentAndChildren` | 本部门及下级 |
| `Custom` | 指定组织 ID 集合 |
| `All` | 全部 |

### 8.2 解析规则（`DataScopeResolver`）

- `admin` 角色 → `All`  
- 按用户角色读取 `RoleDataScope`（可按资源名，默认 `*`）  
- 多角色取 **最宽** 范围  
- 依赖用户所属组织机构计算部门树  

业务查询：实体实现 `IHasCreatorId` / `IHasOrganizationId`，使用 `ApplyDataPermission` 扩展过滤。

### 8.3 与菜单的关系

| | 功能权限 | 数据权限 |
|--|----------|----------|
| 配置位置 | 角色权限弹窗 | 数据权限（角色范围）页 |
| 影响 | 菜单 / 按钮 / API | 列表与查询结果集 |
| 无权限时 | 无菜单 / 403 | 空列表或拒绝 |

---

## 9. 多租户

| 场景 | 行为 |
|------|------|
| Host | 管理租户、全局设置、Host 用户 |
| Tenant | 租户内用户 / 角色 / OU / 菜单（若按租户种子） |
| 登录 | 前端 `__tenant` / TenantBox；JWT 含 tenantid |
| 租户管理员 | 创建租户时种子；须在租户上下文登录 |

租户切换后必须重新拉 **application-configuration** 与 **菜单**。

---

## 10. 端到端示例

**需求**：业务员可进「用户」菜单，只能改用户，不能删；且只能看本部门用户数据。

1. **权限定义**（已有）：`AbpIdentity.Users`、`Users.Update`（不授 `Users.Delete`）  
2. **角色**：创建 `sales`，勾选 `Users` + `Users.Update`（及必要的查询权限）  
3. **菜单**：用户菜单 `Permission = AbpIdentity.Users`  
4. **数据权限**：`sales` → `Department`（资源按需）  
5. **用户**：加入某 OU，赋予 `sales`  
6. **API**：删除接口保留 `[Authorize(Users.Delete)]`；列表查询套数据权限过滤  
7. **前端**：删除按钮用 `Users.Delete` 控制显示  

结果：有侧栏「用户」、可编辑、无删除、列表仅本部门。

---

## 11. 当前落地与差距

### 11.1 已落地

- Identity：用户 / 角色 / OU API 与基础前端页  
- Administration：动态菜单、按权限过滤 `/api/menu/all`  
- Vue：backend 菜单模式、租户切换  
- 数据权限：`RoleDataScope` + `DataScopeResolver`  
- 邮件设置等 SettingManagement 权限绑定菜单  

### 11.2 待加强（相对商业版体验）

| 项 | 说明 |
|----|------|
| 角色权限 UI | ✅ Permission 树弹窗（`/api/permission-management`，角色页「权限」） |
| 菜单绑定 UX | ✅ 权限树选择（`PermissionTreeSelect`） |
| 按钮级 | ✅ 关键页 `AccessControl` + 子权限码 |
| 静态路由 | ✅ `abp.ts` 已清空，侧栏仅后端菜单 |
| 权限变更刷新 | ✅ 保存后 `refreshAccessConfiguration` 重拉策略与菜单 |

---

## 12. 配置清单（落地检查）

新增一个受控业务页时：

1. 在对应模块增加 `PermissionDefinition`（含 CRUD 子权限）  
2. DbMigrator / 种子把权限授给需要的角色（至少 admin）  
3. 菜单管理新增叶子，`Permission` = 定义名，`Component`/`Path` 指向 Vue 页面  
4. API 方法加 `[Authorize]`  
5. 前端按钮按子权限控制；需要行级过滤时配置数据权限并在查询处 `ApplyDataPermission`  
6. 用非 admin 角色登录验证：无权限无菜单、有权限可见、API 拒绝越权  

---

## 13. 相关代码索引

| 主题 | 路径 |
|------|------|
| 菜单实体 | `src/services/administration/.../Menus/SysMenu.cs` |
| 菜单过滤 | `.../Application/Menus/MenuAppService.cs` |
| 菜单种子 | `.../Domain/Menus/MenuDataSeedContributor.cs` |
| Administration 权限 | `.../Permissions/AdministrationPermissions.cs` |
| 数据范围 | `.../DataPermission/DataScopeResolver.cs`、`RoleDataScope.cs` |
| 共享数据权限 | `src/shared/Meta.Dow.Shared/.../DataPermission/*` |
| 前端菜单 API | `vue-demo/apps/web-antd/src/api/core/menu.ts` |
| 前端 access | `.../router/access.ts`、`preferences.ts`（`accessMode: backend`） |
| 用户/角色/OU 页 | `vue-demo/.../views/abp/identity/*`、`views/iam/*` |

---

## 14. 总结

Meta.Dow 采用与 **ABP Commercial 一致的 Permission 中心化模型**：

- **用户 / 角色 / 组织机构** → Identity  
- **谁能做什么** → PermissionManagement  
- **侧栏显示什么** → 菜单绑定权限名 + 服务端过滤  
- **能看哪些数据** → 数据权限（OU + RoleDataScope）  

后续增强应优先补齐 **商业版级授权 UI 与按钮级权限**，而不是引入第二套菜单授权体系。
