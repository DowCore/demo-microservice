# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

An **ABP Framework**-based microservices solution orchestrated with **.NET Aspire**. It contains four
business services (Administration, Identity, Projects, SaaS), a **YARP**-based API gateway, an
**OpenIddict** authorization server, and a centralized MongoDB migrator.

The web UI is **not** in this repository. The frontend is a separate **Vue 3 + Vben Admin** monorepo
(vue-vben-admin) that talks to this solution through the gateway.

| Item | Actual value |
| --- | --- |
| ABP Framework | **10.6.0** |
| Target framework | **net10.0** |
| .NET SDK | **10.0.401** (pinned via `global.json`, `rollForward: latestFeature`) |
| .NET Aspire | **13.5.3** |
| Database | **MongoDB** (`MongoDB.Driver` 3.4.0) — no EF Core, no SQL |
| Solution file | **`Meta.Dow.slnx`** (new-style XML solution) |
| Package management | **Central Package Management (CPM)** via `Directory.Packages.props` |
| Messaging | RabbitMQ (ABP distributed event bus + background jobs) |
| Cache / locks | Redis (`Volo.Abp.Caching.StackExchangeRedis`, `IAbpDistributedLock` via Medallion) |
| Tests | `tests/Meta.Dow.Orchestration.Tests` (xUnit) |
| CI | `.github/workflows/ci.yml` (restore → build → test on ubuntu-latest) |

> There is **no Makefile**, **no Blazor WebAssembly app**, **no EntityFrameworkCore projects**, and
> **no PostgreSQL**. Older revisions of this document described those; they do not exist here.

## Build, Test, Run

All commands are run from the repository root (`src/` in the original layout — the solution lives at
the repo root, with sources under `src/`).

```bash
# Restore + build the whole solution
dotnet restore Meta.Dow.slnx
dotnet build Meta.Dow.slnx -c Release

# Run all tests
dotnet test Meta.Dow.slnx -c Release

# Run only the orchestration unit tests
dotnet test tests/Meta.Dow.Orchestration.Tests/Meta.Dow.Orchestration.Tests.csproj
dotnet test tests/Meta.Dow.Orchestration.Tests --filter "FullyQualifiedName~WorkflowDefinitionValidatorTests"

# Build a single service
dotnet build src/services/saas/host/Meta.Dow.SaaS.HttpApi.Host/Meta.Dow.SaaS.HttpApi.Host.csproj
```

### Running the full stack (Aspire)

```bash
cd src/apps/Meta.Dow.AppHost
dotnet run
```

Aspire starts and wires, in order:

1. **MongoDB** (`mongodb`, with Mongo Express) — the only container with a persistent data volume.
2. **mongo-restore** — `tools/mongo-restore.ps1`, restores seed data, waits for Mongo.
3. **DbMigrator** (`MetaDowDbMigrator`) — waits for Mongo + mongo-restore, then seeds/migrates.
4. **RabbitMQ** (`rabbitmq`, management plugin), **Redis** (`redis`, Redis Commander), **Seq** (`seq`).
5. All five services + Gateway + AuthServer, each `WaitForCompletion(migrator)`.

RabbitMQ / Redis / Seq run as **temporary containers** (no volumes) for fast startup; only Mongo is
persisted. The Aspire dashboard URL is printed to the console.

> `Program.cs` clears the `DEBUG_SESSION_*` environment variables on startup. This is deliberate: the
> local IDE Aspire debug bridge frequently times out, causing child processes to launch with empty
> arguments and fail after a 300 s timeout.

### Running a single service without Aspire

```bash
cd src/services/saas/host/Meta.Dow.SaaS.HttpApi.Host
dotnet run
```

You must supply your own Mongo/RabbitMQ/Redis and point the connection strings at them.

### Service endpoints (launch profiles)

| Component | URL |
| --- | --- |
| Aspire AppHost (dashboard) | `https://localhost:17166` (http `15124`) |
| Gateway | `https://localhost:7500` (http `7501`) |
| AuthServer | `https://localhost:7600` |
| Administration | `https://localhost:7001` |
| IdentityService | `https://localhost:7002` |
| SaaS | `https://localhost:7003` |
| Projects | `https://localhost:7004` |

Every host now exposes **`/health`** and **`/alive`** in all environments (mapped by
`MapDefaultEndpoints()` from `Meta.Dow.ServiceDefaults`), returning a JSON payload.

## Configuration & Secrets

- Plaintext secrets (`SwaggerClientSecret`, `DefaultPassPhrase`, OpenIddict signing/encryption keys,
  etc.) are **not** committed. Each host loads an optional, git-ignored
  **`appsettings.secrets.json`** next to `appsettings.json`.
- Commit only the tracked template: **`appsettings.secrets.json.example`**. Copy it to
  `appsettings.secrets.json` and fill in real values for local development.
- `.gitignore` covers `**/appsettings.secrets.json`, `tempkey.jwk`, and `tempkey.rsa`. If you add a new
  host, follow the same pattern — never re-introduce a plaintext secret into `appsettings.json`.
- The AuthServer registers OpenIddict **signing and encryption certificates** from
  `OpenIddict:Certificate:Path` (dev cert generated on first run if absent).
- **NEVER put a JSON array in `appsettings.secrets.json`.** A later config source does **not** replace or
  append arrays — it merges them **by index**, so a partial array silently rewrites the `ClientId` of the
  same-index entry in `appsettings.json` while keeping that entry's other fields. This "Frankenstein merge"
  is what deleted the `MetaDow_Vue` OpenIddict client and broke login with
  `invalid_request / ID2052 (client_id is invalid)` on 2026-10-10.
  For per-key overrides use an **object**, e.g. `"ClientSecrets": { "Administration_API": "<secret>" }`.
  `SecretsConfigSafetyTests` enforces this across the repo (including `*.example` templates).
- `Clients` in the DbMigrator is the single source of truth for OpenIddict clients, and the seeder is
  **config-authoritative**: it clears and rebuilds permissions/redirect URIs on every run, so re-running the
  migrator converges the database back to config. Destructive pruning of unlisted clients only happens when
  `PruneObsoleteClients` is explicitly `true`, and every removal is logged at Warning level.

## Architecture

### Layout

```
src/
├── apps/
│   ├── Meta.Dow.AppHost/          # Aspire orchestration (Program.cs = startup graph)
│   └── Meta.Dow.AuthServer/       # OpenIddict OAuth2/OIDC server
├── gateway/Meta.Dow.Gateway/      # YARP reverse proxy + JWT + rate limiting
├── services/{administration,identity,projects,saas}/
│   ├── host/{Service}.HttpApi.Host/       # API host (entry point)
│   └── src/
│       ├── {Service}.Domain.Shared/       # Constants, enums, shared schema types
│       ├── {Service}.Domain/              # Entities, aggregates, domain services
│       ├── {Service}.Application.Contracts/  # DTOs, service interfaces
│       ├── {Service}.Application/         # Application services (business logic)
│       ├── {Service}.MongoDB/             # MongoDbContext, collections, index setup
│       ├── {Service}.HttpApi/             # Controllers / endpoints
│       └── {Service}.HttpApi.Client/      # Client proxies for remote calls
└── shared/
    ├── Meta.Dow.Shared/                   # MetaDowNames, data-permission, multi-tenancy consts
    ├── Meta.Dow.ServiceDefaults/          # OpenTelemetry, health endpoints, resilience
    ├── Meta.Dow.Hosting.Shared/           # Redis cache, RabbitMQ, distributed locking, localization
    ├── Meta.Dow.Microservice.Shared/      # JWT/CORS/Swagger config shared by services
    └── Meta.Dow.DbMigrator/               # Centralized Mongo migration + seeding
tests/
└── Meta.Dow.Orchestration.Tests/          # xUnit unit tests
```

Note the persistence layer is **`{Service}.MongoDB`** (there is no `{Service}.EntityFrameworkCore`).

### Services

1. **Administration** (7001) — permissions, settings, audit logging, feature management. Db: `MetaDowAdministrationDb`.
2. **IdentityService** (7002) — users/roles on top of ABP Identity; also serves `/api/account/*`. Db: `MetaDowIdentityServiceDb`.
3. **SaaS** (7003) — multi-tenancy, tenants, feature subscriptions, and the **low-code orchestration /
   OA workflow / form library** module under `/api/orchestration/*` and `/api/logic/*`. Db: `MetaDowSaaSDb`.
4. **Projects** (7004) — business domain for project management. Db: `MetaDowProjectsDb`.

Shared contexts are intentional: Administration, Identity, SaaS and AuthServer each reference
`MetaDowAdministrationDb`, `MetaDowIdentityServiceDb`, `MetaDowSaaSDb` for cross-cutting
permissions/tenancy. This is a deliberate trade-off, not a boundary violation.

Service/database names are centralized in
`src/shared/Meta.Dow.Shared/Microsoft/Extensions/Hosting/MetaDowNames.cs`. Always use these constants.

### Gateway

`src/gateway/Meta.Dow.Gateway` is a YARP proxy that also enforces auth, applies rate limiting, and
normalizes error responses.

- **JWT bearer** validation against `AuthServer:Authority` / `AuthServer:Audience`; the default policy
  is "authenticated".
- **Rate limiting**: ~300 requests/minute, partitioned per client.
- **Unified error bodies** for `401` and `429`.
- Cluster HTTP request timeout is `00:00:30`.

Route table (`appsettings.json` → `ReverseProxy.Routes`):

| Path | Cluster | Auth |
| --- | --- | --- |
| `/api/administration/{*any}` | administration | — |
| `/api/abp/{*any}` | administration | — |
| `/api/setting-management/{*any}` | administration | — |
| `/api/identity/{*any}` | identity | — |
| `/api/account/{*any}` | account (→ IdentityService) | — |
| `/api/multi-tenancy/{*any}` | saas | — |
| `/api/orchestration/{*any}` | saas | — |
| `/api/logic/{*any}` | saas | — |
| `/api/feature-management/{*any}` | saas | — |
| `/api/projects/{*any}` | projects | — |
| `{**catch-all}` | administration | **Default (authenticated)** |

The catch-all is deliberately **not** `/api/administration/{**catch-all}` and **requires
authentication** — unknown paths can no longer reach Administration anonymously.

### AuthServer

OpenIddict-based OAuth2/OIDC (port 7600). Issues tokens for all services, seeds clients/resources via
`OpenIddictDataSeeder`, and uses configured signing/encryption certificates. Flows: Authorization Code,
Client Credentials, Resource Owner Password.

### Distributed infrastructure

- **Redis** — distributed cache (per-service key prefixes), data-protection keys, distributed locks.
- **RabbitMQ** — distributed events (`IDistributedEventHandler<TEto>`) and background jobs.
- **Seq** — centralized structured logging (Serilog).
- **OpenTelemetry** — traces/metrics (ASP.NET Core, HTTP client, runtime), exported via OTLP.
- **Service discovery** — `Microsoft.Extensions.ServiceDiscovery`; services resolve each other by the
  `MetaDowNames` constants; Aspire provides name resolution in development.

Distributed locking is configured via the public `ConfigureDistributedLocking(...)` extension in
`Meta.Dow.Hosting.Shared` and is a no-op when no Redis connection is available. Background/scheduled
work (e.g. `FlowScheduleHostedService`) must acquire `IAbpDistributedLock` before acting so multiple
hosts do not double-fire.

## ABP Conventions

### Modules

Every component is an `AbpModule` with explicit `[DependsOn(...)]`:

```csharp
[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(YourDomainModule),
    typeof(YourApplicationModule),
    typeof(YourMongoDbModule)
)]
public class YourHttpApiHostModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        // ...
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        app.UseCorrelationId();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAbpRequestLocalization();
        app.UseAuthorization();
        app.UseSwagger();
        app.UseAbpSwaggerUI();
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}
```

Mongo index creation (unique + query indexes) is done in
`OnApplicationInitializationAsync`/`EnsureIndexesAsync` inside the service's `MongoDbModule` — not via
migrations. When adding a collection that needs a unique key, register the index there.

### Repositories

```csharp
private readonly IRepository<YourEntity, Guid> _repository;

var entity = await _repository.GetAsync(id);
await _repository.InsertAsync(entity);
await _repository.UpdateAsync(entity);

var query = await _repository.GetQueryableAsync();
var list = await AsyncExecuter.ToListAsync(query.Where(x => x.Name.Contains("test")));
```

### Application services & DTOs

```csharp
public class YourAppService : ApplicationService, IYourAppService
{
    private readonly IRepository<YourEntity, Guid> _repository;

    public YourAppService(IRepository<YourEntity, Guid> repository) => _repository = repository;

    public async Task<YourDto> GetAsync(Guid id)
        => ObjectMapper.Map<YourEntity, YourDto>(await _repository.GetAsync(id));
}
```

### Unit of work behavior

`AbpUnitOfWorkDefaultOptions.TransactionBehavior` is set to **`Disabled`**. MongoDB has no ambient
transaction that spans services/collections the way EF Core does, so do not rely on rollback semantics
from the ABP UoW — write compensating logic where needed.

### Domain events

```csharp
await LocalEventBus.PublishAsync(new YourDomainEvent { /* ... */ });

public class YourEventHandler : ILocalEventHandler<YourDomainEvent>
{
    public Task HandleEventAsync(YourDomainEvent eventData) => Task.CompletedTask;
}
```

For cross-service async messaging use `IDistributedEventHandler<TEto>` with ETos declared in
`Application.Contracts` and published over RabbitMQ via `DistributedEventBus`.

## Multi-Tenancy

Enabled via `src/shared/Meta.Dow.Shared/Meta.Dow/MultiTenancy/MultiTenancyConsts.cs`. Tenants are
resolved from the `__tenant` header, the `{tenantName}.yourdomain.com` subdomain, or `?__tenant={id}`.

```csharp
var tenantId = _currentTenant.Id;        // null ⇒ host
var isAvailable = _currentTenant.IsAvailable;
```

Tenant-scoped entities implement `IMultiTenant`. Unique indexes on tenant-scoped collections must
include `TenantId` (e.g. unique `TenantId + Code` on form/workflow/flow definitions) — otherwise two
tenants collide on the same code.

## Inter-Service Communication

Use ABP client proxies (`*.HttpApi.Client`): add the project reference and inject the application
service interface. The client module auto-configures service discovery, JWT propagation, and resilience
(retry/circuit breaker/timeout).

Avoid adding a compile-time `ProjectReference` from one service's `Application` layer to another
service's `Application`/`Domain` project. If you only need an ABP type (e.g. `IdentityUserManager`),
reference the corresponding **ABP package** (e.g. `Volo.Abp.Identity.Domain`) instead of the other
service's assembly. This keeps service boundaries honest and buildable in isolation.

## Database (MongoDB)

- Connection strings are injected by Aspire via project references; locally they live in
  `appsettings.json` / `appsettings.secrets.json`.
- The centralized `Meta.Dow.DbMigrator` depends on every service's MongoDb module, runs seeding, then
  all services wait for its completion.
- There are **no migrations**. Schema evolution is handled by the Mongo driver, explicit index setup,
  and application-level data fixes.

## Testing

xUnit, in `tests/Meta.Dow.Orchestration.Tests`. Tests cover the orchestration domain directly (pure
unit tests — no Mongo/Redis/RabbitMQ needed): workflow definition validation, form schema
flattening, and record-patch filtering. Some application-service members are `internal` and exposed via
`InternalsVisibleTo`, so the test project can exercise them.

```csharp
public class YourService_Tests
{
    [Fact]
    public void Should_Do_Something()
    {
        // Arrange / Act / Assert
    }
}
```

## Code Quality

- **CPM**: add new packages to `Directory.Packages.props` (`<PackageVersion>`), and reference them in
  project files **without** a `Version` attribute. Never hard-code a version in a `.csproj`.
- **Package sources**: `NuGet.config` at the repo root is the single source of truth (`<clear />` +
  `nuget.org` + `<packageSourceMapping>` pinning `*` to `nuget.org`). Do **not** add per-directory
  `NuGet.Config` files — they merge into the effective source list and break CPM (NU1507).
- **Analyzer/bootstrap settings**: `Directory.Build.props`.
- **`.editorconfig`** and **`global.json`** are at the repo root and are authoritative for style/SDK.
- Analyzers (StyleCop, SonarAnalyzer, Roslynator, NetAnalyzers, etc.) are enabled via
  `Directory.Build.props`; run with `/p:AnalysisMode=All /p:EnforceCodeStyleInBuild=true` for strict mode.

## Common Tasks

### Add an entity to a service

1. Entity in `{Service}.Domain/…`.
2. Add the collection to `{Service}MongoDbContext` in `{Service}.MongoDB/`.
3. Configure mapping/indexes in the `{Service}.MongoDB` module (or a collection config).
4. DTO in `{Service}.Application.Contracts/…`.
5. Map in `{Service}ApplicationAutoMapperProfile`.
6. Application service in `{Service}.Application/…`.
7. Controller in `{Service}.HttpApi/…` (or rely on ABP conventions).

### Add a microservice

1. Create `src/services/your-service/` mirroring an existing service (e.g. `projects/`).
2. Rename namespaces/projects; use `{Service}.MongoDB` for persistence.
3. Add projects to `Meta.Dow.slnx`.
4. Add constants to `MetaDowNames.cs`.
5. Register in `src/apps/Meta.Dow.AppHost/Program.cs` with its database + `WaitFor`/`WaitForCompletion`.
6. Add gateway routes in `src/gateway/Meta.Dow.Gateway/appsettings.json`.
7. Add the Mongo context to `Meta.Dow.DbMigrator`.

### Authentication config (per service)

```json
{
  "AuthServer": {
    "Authority": "https://localhost:7600",
    "RequireHttpsMetadata": "false",
    "SwaggerClientId": "YourService_Swagger",
    "SwaggerClientSecret": ""   // ← put the real value in appsettings.secrets.json
  }
}
```

## Troubleshooting

- **Service fails to start**: confirm Aspire started Mongo/RabbitMQ/Redis and that DbMigrator completed;
  check `appsettings.secrets.json` exists where required.
- **Auth errors**: AuthServer (7600) must be up; verify `Authority` in the service config matches the
  gateway's `AuthServer:Authority`.
- **`invalid_request / ID2052` ("The specified 'client_id' is invalid") on the login redirect**: the
  OpenIddict client is missing from the DB — usually because a `Clients` array got index-merged in a secrets
  file (see *Configuration & Secrets*). Inspect and repair:
  `docker exec mongodb-<id> mongosh -u admin -p <pw> --authenticationDatabase admin` →
  `db.getSiblingDB("MetaDowIdentityServiceDb").OpenIddictApplications.find({}, {ClientId:1, IsDeleted:1})`.
  OpenIddict applications/scopes/tokens live in the **Identity** database (the `AbpOpenIddict` connection is
  mapped to `MetaDowIdentityServiceDb`). A soft-deleted client (`IsDeleted: true`) is invisible to
  `FindByClientIdAsync` — restore it, or fix the config and re-run the DbMigrator.
- **`ERR_INVALID_PACKAGE_CONFIG` / package restore oddities on Windows**: see the machine-specific
  notes about nvm/pnpm ACLs in your user memory — not a repository issue.
- **Aspire children start with empty args / 300 s timeout**: expected on some local IDE setups; the
  `DisableAspireIdeRunSession()` workaround in `AppHost/Program.cs` handles it.
