#!/usr/bin/env python3
"""Copy Tasky microservice skeleton and convert to Meta.Dow + MongoDB."""
from __future__ import annotations

import re
import shutil
from pathlib import Path

SRC = Path(r"D:\C#\abp-microservice\src")
DST = Path(r"D:\C#\demo-microservice\src")

SKIP_DIRS = {
    "bin",
    "obj",
    "test",
    "angular",
    "Tasky.WebApp",
    "Migrations",
    ".claude",
    ".template.config",
    "Logs",
}

SKIP_FILES = {
    "Tasky.sln",
    "FodyWeavers.xml",
    "FodyWeavers.xsd",
    "stylecop.json",
    "Directory.Build.props",
}

IDENTIFIERS = [
    ("TaskyNames", "MetaDowNames"),
    ("TaskyHostingModule", "MetaDowHostingModule"),
    ("TaskyMicroserviceModule", "MetaDowMicroserviceModule"),
    ("TaskySharedModule", "MetaDowSharedModule"),
    ("TaskyServiceDefaultsModule", "MetaDowServiceDefaultsModule"),
    ("TaskyAuthServerModule", "MetaDowAuthServerModule"),
    ("TaskyDbMigratorModule", "MetaDowDbMigratorModule"),
    ("TaskyDbMigrationService", "MetaDowDbMigrationService"),
    ("TaskyBrandingProvider", "MetaDowBrandingProvider"),
    ("TaskyLogging", "MetaDowLogging"),
]

VOLO_NS = [
    ("Volo.Abp.Identity.EntityFrameworkCore", "Volo.Abp.Identity.MongoDB"),
    ("Volo.Abp.OpenIddict.EntityFrameworkCore", "Volo.Abp.OpenIddict.MongoDB"),
    ("Volo.Abp.AuditLogging.EntityFrameworkCore", "Volo.Abp.AuditLogging.MongoDB"),
    ("Volo.Abp.FeatureManagement.EntityFrameworkCore", "Volo.Abp.FeatureManagement.MongoDB"),
    ("Volo.Abp.PermissionManagement.EntityFrameworkCore", "Volo.Abp.PermissionManagement.MongoDB"),
    ("Volo.Abp.SettingManagement.EntityFrameworkCore", "Volo.Abp.SettingManagement.MongoDB"),
    ("Volo.Abp.TenantManagement.EntityFrameworkCore", "Volo.Abp.TenantManagement.MongoDB"),
]

VOLO_TYPES = [
    ("AbpIdentityEntityFrameworkCoreModule", "AbpIdentityMongoDbModule"),
    ("AbpOpenIddictEntityFrameworkCoreModule", "AbpOpenIddictMongoDbModule"),
    ("AbpAuditLoggingEntityFrameworkCoreModule", "AbpAuditLoggingMongoDbModule"),
    ("AbpFeatureManagementEntityFrameworkCoreModule", "AbpFeatureManagementMongoDbModule"),
    ("AbpPermissionManagementEntityFrameworkCoreModule", "AbpPermissionManagementMongoDbModule"),
    ("AbpSettingManagementEntityFrameworkCoreModule", "AbpSettingManagementMongoDbModule"),
    ("AbpTenantManagementEntityFrameworkCoreModule", "AbpTenantManagementMongoDbModule"),
    ("AbpEntityFrameworkCorePostgreSqlModule", "AbpMongoDbModule"),
    ("IIdentityDbContext", "IIdentityMongoDbContext"),
    ("IOpenIddictDbContext", "IOpenIddictMongoDbContext"),
    ("IAuditLoggingDbContext", "IAuditLoggingMongoDbContext"),
    ("IFeatureManagementDbContext", "IFeatureManagementMongoDbContext"),
    ("IPermissionManagementDbContext", "IPermissionManagementMongoDbContext"),
    ("ISettingManagementDbContext", "ISettingManagementMongoDbContext"),
    ("ITenantManagementDbContext", "ITenantManagementMongoDbContext"),
]

LOCAL_EF = [
    ("AdministrationEntityFrameworkCoreModule", "AdministrationMongoDbModule"),
    ("IdentityServiceEntityFrameworkCoreModule", "IdentityServiceMongoDbModule"),
    ("SaaSEntityFrameworkCoreModule", "SaaSMongoDbModule"),
    ("ProjectsEntityFrameworkCoreModule", "ProjectsMongoDbModule"),
    ("Tasky.Administration.EntityFrameworkCore", "Meta.Dow.Administration.MongoDB"),
    ("Tasky.IdentityService.EntityFrameworkCore", "Meta.Dow.IdentityService.MongoDB"),
    ("Tasky.SaaS.EntityFrameworkCore", "Meta.Dow.SaaS.MongoDB"),
    ("Tasky.Projects.EntityFrameworkCore", "Meta.Dow.Projects.MongoDB"),
]

PKG = [
    ("Volo.Abp.Identity.EntityFrameworkCore", "Volo.Abp.Identity.MongoDB"),
    ("Volo.Abp.OpenIddict.EntityFrameworkCore", "Volo.Abp.OpenIddict.MongoDB"),
    ("Volo.Abp.AuditLogging.EntityFrameworkCore", "Volo.Abp.AuditLogging.MongoDB"),
    ("Volo.Abp.FeatureManagement.EntityFrameworkCore", "Volo.Abp.FeatureManagement.MongoDB"),
    ("Volo.Abp.PermissionManagement.EntityFrameworkCore", "Volo.Abp.PermissionManagement.MongoDB"),
    ("Volo.Abp.SettingManagement.EntityFrameworkCore", "Volo.Abp.SettingManagement.MongoDB"),
    ("Volo.Abp.TenantManagement.EntityFrameworkCore", "Volo.Abp.TenantManagement.MongoDB"),
    ("Volo.Abp.EntityFrameworkCore.PostgreSql", "Volo.Abp.MongoDB"),
    ("Volo.Abp.EntityFrameworkCore", "Volo.Abp.MongoDB"),
    ("Aspire.Hosting.PostgreSQL", "Aspire.Hosting.MongoDB"),
]


def should_skip_dir(name: str) -> bool:
    return name in SKIP_DIRS or name.endswith(".WebApp")


def copy_tree() -> None:
    if DST.exists():
        shutil.rmtree(DST)
    for path in SRC.rglob("*"):
        rel = path.relative_to(SRC)
        if any(should_skip_dir(p) for p in rel.parts):
            continue
        if path.is_dir():
            continue
        if path.name in SKIP_FILES:
            continue
        dest = DST / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, dest)


def transform_text(text: str) -> str:
    for a, b in IDENTIFIERS + VOLO_NS + VOLO_TYPES + LOCAL_EF + PKG:
        text = text.replace(a, b)
    text = text.replace("Tasky.EntityFrameworkCore", "Meta.Dow.MongoDB")
    text = text.replace("Tasky.", "Meta.Dow.")
    text = text.replace("Tasky_", "MetaDow_")
    text = text.replace('"Tasky"', '"Meta.Dow"')
    text = text.replace("Tasky:", "Meta.Dow:")
    text = text.replace("Starting Tasky", "Starting Meta.Dow")
    text = text.replace("Tasky.AuthServer", "Meta.Dow.AuthServer")
    text = text.replace("AddAudiences(\"Tasky\")", "AddAudiences(\"Meta.Dow\")")
    text = text.replace("AddAudiences(\"Meta.Dow\")", "AddAudiences(\"Meta.Dow\")")
    # leftover Tasky tokens
    text = text.replace("Tasky", "MetaDow")
    text = re.sub(
        r"builder\.AddNpgsqlDbContext<[^>]+>\([\s\S]*?\);",
        "",
        text,
    )
    text = text.replace("using Volo.Abp.Identity.MongoDB;\n", "")
    text = text.replace("using Volo.Abp.Identity.EntityFrameworkCore;\n", "")
    text = text.replace("using Microsoft.EntityFrameworkCore;\n", "")
    text = text.replace("using Microsoft.EntityFrameworkCore.Infrastructure;\n", "")
    text = text.replace("using Microsoft.EntityFrameworkCore.Storage;\n", "")
    text = text.replace("using Volo.Abp.EntityFrameworkCore;\n", "")
    text = text.replace("using Volo.Abp.EntityFrameworkCore.PostgreSql;\n", "")
    text = text.replace("Aspire.Npgsql.EntityFrameworkCore.PostgreSQL", "REMOVED_NPGSQL")
    return text


def transform_files() -> None:
    for path in DST.rglob("*"):
        if not path.is_file():
            continue
        if path.suffix.lower() not in {".cs", ".csproj", ".json", ".xml", ".props", ".targets", ".md", ".gitignore", ".editorconfig", ".yml"}:
            continue
        raw = path.read_text(encoding="utf-8")
        new = transform_text(raw)
        if new != raw:
            path.write_text(new, encoding="utf-8")


def rename_paths() -> None:
    special = {
        "TaskyNames.cs": "MetaDowNames.cs",
        "TaskyHostingModule.cs": "MetaDowHostingModule.cs",
        "TaskyMicroserviceModule.cs": "MetaDowMicroserviceModule.cs",
        "TaskySharedModule.cs": "MetaDowSharedModule.cs",
        "TaskyServiceDefaultsModule.cs": "MetaDowServiceDefaultsModule.cs",
        "TaskyAuthServerModule.cs": "MetaDowAuthServerModule.cs",
        "TaskyDbMigratorModule.cs": "MetaDowDbMigratorModule.cs",
        "TaskyDbMigrationService.cs": "MetaDowDbMigrationService.cs",
        "TaskyBrandingProvider.cs": "MetaDowBrandingProvider.cs",
        "TaskyLogging.cs": "MetaDowLogging.cs",
    }
    items = sorted(DST.rglob("*"), key=lambda p: len(p.parts), reverse=True)
    for path in items:
        name = path.name
        if name in special:
            path.rename(path.with_name(special[name]))
            continue
        if "Tasky" in name:
            path.rename(path.with_name(name.replace("Tasky", "Meta.Dow")))
        elif "EntityFrameworkCore" in name and path.suffix in {".csproj", ""}:
            path.rename(path.with_name(name.replace("EntityFrameworkCore", "MongoDB")))


def rename_ef_dirs() -> None:
    items = sorted(DST.rglob("*"), key=lambda p: len(p.parts), reverse=True)
    for path in items:
        if path.is_dir() and path.name == "EntityFrameworkCore":
            path.rename(path.with_name("MongoDB"))
        elif path.is_dir() and path.name.endswith(".EntityFrameworkCore"):
            path.rename(path.with_name(path.name.replace("EntityFrameworkCore", "MongoDB")))


ABP = "10.0.2"

IDENTITY_MODULE = f'''using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.Identity.MongoDB;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.OpenIddict.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.IdentityService.MongoDB;

[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(AbpIdentityMongoDbModule))]
[DependsOn(typeof(AbpOpenIddictMongoDbModule))]
[DependsOn(typeof(IdentityServiceDomainModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class IdentityServiceMongoDbModule : AbpModule
{{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {{
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options =>
        {{
            options.Databases.Configure(
                MetaDowNames.IdentityServiceDb,
                db =>
                {{
                    db.MappedConnections.Add("AbpIdentity");
                    db.MappedConnections.Add("AbpOpenIddict");
                }}
            );
        }});
    }}
}}
'''

ADMIN_MODULE = '''using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.AuditLogging.MongoDB;
using Volo.Abp.Data;
using Volo.Abp.FeatureManagement.MongoDB;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.PermissionManagement.MongoDB;
using Volo.Abp.SettingManagement.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.Administration.MongoDB;

[DependsOn(typeof(AbpAuditLoggingMongoDbModule))]
[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(AbpFeatureManagementMongoDbModule))]
[DependsOn(typeof(AbpPermissionManagementMongoDbModule))]
[DependsOn(typeof(AbpSettingManagementMongoDbModule))]
[DependsOn(typeof(AdministrationDomainModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class AdministrationMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options =>
        {
            options.Databases.Configure(
                MetaDowNames.AdministrationDb,
                db =>
                {
                    db.MappedConnections.Add("AbpAuditLogging");
                    db.MappedConnections.Add("AbpFeatureManagement");
                    db.MappedConnections.Add("AbpPermissionManagement");
                    db.MappedConnections.Add("AbpSettingManagement");
                }
            );
        });
    }
}
'''

SAAS_MODULE = '''using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.TenantManagement.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.SaaS.MongoDB;

[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(AbpTenantManagementMongoDbModule))]
[DependsOn(typeof(SaaSDomainModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class SaaSMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options =>
            options.Databases.Configure(MetaDowNames.SaaSDb, db => db.MappedConnections.Add("AbpTenantManagement")));
    }
}
'''

PROJECTS_MODULE = '''using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.Uow;

namespace Meta.Dow.Projects.MongoDB;

[DependsOn(typeof(ProjectsDomainModule))]
[DependsOn(typeof(AbpMongoDbModule))]
[DependsOn(typeof(MetaDowSharedModule))]
public class ProjectsMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Configure<AbpUnitOfWorkDefaultOptions>(options =>
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled);

        Configure<AbpDbConnectionOptions>(options => options.Databases.Configure(MetaDowNames.ProjectsDb, db => { }));

        context.Services.AddMongoDbContext<ProjectsDbContext>(options => options.AddDefaultRepositories(true));
    }
}
'''

PROJECTS_CTX = '''using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Meta.Dow.Projects.MongoDB;

[ConnectionStringName(MetaDowNames.ProjectsDb)]
public class ProjectsDbContext : AbpMongoDbContext, IProjectsDbContext
{
}
'''


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content.replace("\n", "\n"), encoding="utf-8")


def rewrite_data_layers() -> None:
    identity = DST / "services/identity/src/Meta.Dow.IdentityService.MongoDB"
    admin = DST / "services/administration/src/Meta.Dow.Administration.MongoDB"
    saas = DST / "services/saas/src/Meta.Dow.SaaS.MongoDB"
    projects = DST / "services/projects/src/Meta.Dow.Projects.MongoDB"

    for folder in (identity, admin, saas, projects):
        if folder.exists():
            for f in folder.rglob("*.cs"):
                f.unlink()

    write(identity / "IdentityServiceMongoDbModule.cs", IDENTITY_MODULE)
    write(admin / "AdministrationMongoDbModule.cs", ADMIN_MODULE)
    write(saas / "SaaSMongoDbModule.cs", SAAS_MODULE)
    write(projects / "ProjectsMongoDbModule.cs", PROJECTS_MODULE)
    write(
        projects / "IProjectsDbContext.cs",
        """using Microsoft.Extensions.Hosting;
using Volo.Abp.Data;
using Volo.Abp.MongoDB;

namespace Meta.Dow.Projects.MongoDB;

[ConnectionStringName(MetaDowNames.ProjectsDb)]
public interface IProjectsDbContext : IAbpMongoDbContext
{
}
""",
    )
    write(projects / "ProjectsDbContext.cs", PROJECTS_CTX)

    write(
        identity / "Meta.Dow.IdentityService.MongoDB.csproj",
        f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Meta.Dow.IdentityService</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Volo.Abp.MongoDB" Version="{ABP}" />
    <PackageReference Include="Volo.Abp.Identity.MongoDB" Version="{ABP}" />
    <PackageReference Include="Volo.Abp.OpenIddict.MongoDB" Version="{ABP}" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\..\\..\\shared\\Meta.Dow.Shared\\Meta.Dow.Shared.csproj" />
    <ProjectReference Include="..\\Meta.Dow.IdentityService.Domain\\Meta.Dow.IdentityService.Domain.csproj" />
  </ItemGroup>
</Project>
""",
    )
    write(
        admin / "Meta.Dow.Administration.MongoDB.csproj",
        f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Meta.Dow.Administration</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Volo.Abp.MongoDB" Version="{ABP}" />
    <PackageReference Include="Volo.Abp.AuditLogging.MongoDB" Version="{ABP}" />
    <PackageReference Include="Volo.Abp.FeatureManagement.MongoDB" Version="{ABP}" />
    <PackageReference Include="Volo.Abp.PermissionManagement.MongoDB" Version="{ABP}" />
    <PackageReference Include="Volo.Abp.SettingManagement.MongoDB" Version="{ABP}" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\..\\..\\shared\\Meta.Dow.Shared\\Meta.Dow.Shared.csproj" />
    <ProjectReference Include="..\\Meta.Dow.Administration.Domain\\Meta.Dow.Administration.Domain.csproj" />
  </ItemGroup>
</Project>
""",
    )
    write(
        saas / "Meta.Dow.SaaS.MongoDB.csproj",
        f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Meta.Dow.SaaS</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Volo.Abp.MongoDB" Version="{ABP}" />
    <PackageReference Include="Volo.Abp.TenantManagement.MongoDB" Version="{ABP}" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\..\\..\\shared\\Meta.Dow.Shared\\Meta.Dow.Shared.csproj" />
    <ProjectReference Include="..\\Meta.Dow.SaaS.Domain\\Meta.Dow.SaaS.Domain.csproj" />
  </ItemGroup>
</Project>
""",
    )
    write(
        projects / "Meta.Dow.Projects.MongoDB.csproj",
        f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Meta.Dow.Projects</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Volo.Abp.MongoDB" Version="{ABP}" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\..\\..\\shared\\Meta.Dow.Shared\\Meta.Dow.Shared.csproj" />
    <ProjectReference Include="..\\Meta.Dow.Projects.Domain\\Meta.Dow.Projects.Domain.csproj" />
  </ItemGroup>
</Project>
""",
    )


def patch_hosting_csproj() -> None:
    p = DST / "shared/Meta.Dow.Hosting.Shared/Meta.Dow.Hosting.Shared.csproj"
    text = p.read_text(encoding="utf-8")
    text = re.sub(
        r"\s*<PackageReference Include=\"REMOVED_NPGSQL\"[\s\S]*?/>",
        "",
        text,
    )
    text = re.sub(
        r"\s*<PackageReference Include=\"Microsoft\.EntityFrameworkCore\"[\s\S]*?/>",
        "",
        text,
    )
    text = re.sub(
        r"\s*<PackageReference Include=\"Microsoft\.EntityFrameworkCore\.Relational\"[\s\S]*?/>",
        "",
        text,
    )
    text = re.sub(
        r"\s*<PackageReference Include=\"Npgsql\.EntityFrameworkCore\.PostgreSQL\"[\s\S]*?/>",
        "",
        text,
    )
    p.write_text(text, encoding="utf-8")


def patch_microservice_shared() -> None:
    p = DST / "shared/Meta.Dow.Microservice.Shared/Meta.Dow.Microservice.Shared.csproj"
    text = p.read_text(encoding="utf-8")
    text = text.replace("EntityFrameworkCore", "MongoDB")
    p.write_text(text, encoding="utf-8")


def patch_migrator() -> None:
    write(
        DST / "shared/Meta.Dow.DbMigrator/MetaDowDbMigrationService.cs",
        """using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Meta.Dow.DbMigrator;

public class MetaDowDbMigrationService(
    ILogger<MetaDowDbMigrationService> logger,
    ITenantRepository tenantRepository,
    IDataSeeder dataSeeder,
    ICurrentTenant currentTenant
) : ITransientDependency
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding MongoDB host data ...");
        await SeedDataAsync(null).ConfigureAwait(false);

        var tenants = await tenantRepository
            .GetListAsync(includeDetails: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var tenant in tenants)
        {
            using (currentTenant.Change(tenant.Id))
            {
                await SeedDataAsync(tenant).ConfigureAwait(false);
            }
        }

        logger.LogInformation("MongoDB seed completed.");
    }

    private Task SeedDataAsync(Tenant? tenant)
    {
        if (tenant is null)
        {
            logger.LogInformation("Seeding host data ...");
        }
        else
        {
            logger.LogInformation("Seeding tenant data: {Name} ({Id})", tenant.Name, tenant.Id);
        }

        return dataSeeder.SeedAsync(
            new DataSeedContext(tenant?.Id)
                .WithProperty(
                    IdentityDataSeedContributor.AdminEmailPropertyName,
                    IdentityDataSeedContributor.AdminEmailDefaultValue
                )
                .WithProperty(
                    IdentityDataSeedContributor.AdminPasswordPropertyName,
                    IdentityDataSeedContributor.AdminPasswordDefaultValue
                )
        );
    }
}
""",
    )
    p = DST / "shared/Meta.Dow.DbMigrator/Program.cs"
    text = p.read_text(encoding="utf-8")
    text = re.sub(r"using Meta\.Dow\.[^;]+MongoDB;\n", "", text)
    text = re.sub(r"using Volo\.Abp\.Identity\.MongoDB;\n", "", text)
    p.write_text(text, encoding="utf-8")

    csproj = DST / "shared/Meta.Dow.DbMigrator/Meta.Dow.DbMigrator.csproj"
    t = csproj.read_text(encoding="utf-8")
    t = t.replace("EntityFrameworkCore", "MongoDB")
    csproj.write_text(t, encoding="utf-8")


def patch_app_host() -> None:
    write(
        DST / "apps/Meta.Dow.AppHost/Program.cs",
        """using Microsoft.Extensions.Hosting;
using Projects;

namespace Meta.Dow.AppHost;

internal class Program
{
    private static void Main(string[] args)
    {
        const string LaunchProfileName = "Aspire";
        var builder = DistributedApplication.CreateBuilder(args);

        var mongo = builder.AddMongoDB(MetaDowNames.MongoDb).WithMongoExpress();
        var rabbitMq = builder.AddRabbitMQ(MetaDowNames.RabbitMq).WithManagementPlugin();
        var redis = builder.AddRedis(MetaDowNames.Redis).WithRedisCommander();
        var seq = builder.AddSeq(MetaDowNames.Seq);

        var adminDb = mongo.AddDatabase(MetaDowNames.AdministrationDb);
        var identityDb = mongo.AddDatabase(MetaDowNames.IdentityServiceDb);
        var projectsDb = mongo.AddDatabase(MetaDowNames.ProjectsDb);
        var saasDb = mongo.AddDatabase(MetaDowNames.SaaSDb);

        var migrator = builder
            .AddProject<Meta_Dow_DbMigrator>(MetaDowNames.DbMigrator, launchProfileName: LaunchProfileName)
            .WithReference(adminDb)
            .WithReference(identityDb)
            .WithReference(projectsDb)
            .WithReference(saasDb)
            .WithReference(seq)
            .WaitFor(mongo);

        var admin = builder
            .AddProject<Meta_Dow_Administration_HttpApi_Host>(
                MetaDowNames.AdministrationApi,
                launchProfileName: LaunchProfileName
            )
            .WithExternalHttpEndpoints()
            .WithReference(adminDb)
            .WithReference(identityDb)
            .WithReference(rabbitMq)
            .WithReference(redis)
            .WithReference(seq)
            .WaitFor(rabbitMq)
            .WaitFor(redis)
            .WaitForCompletion(migrator);

        var identity = builder
            .AddProject<Meta_Dow_IdentityService_HttpApi_Host>(
                MetaDowNames.IdentityServiceApi,
                launchProfileName: LaunchProfileName
            )
            .WithExternalHttpEndpoints()
            .WithReference(adminDb)
            .WithReference(identityDb)
            .WithReference(saasDb)
            .WithReference(rabbitMq)
            .WithReference(redis)
            .WithReference(seq)
            .WaitFor(rabbitMq)
            .WaitFor(redis)
            .WaitForCompletion(migrator);

        var saas = builder
            .AddProject<Meta_Dow_SaaS_HttpApi_Host>(MetaDowNames.SaaSApi, launchProfileName: LaunchProfileName)
            .WithExternalHttpEndpoints()
            .WithReference(adminDb)
            .WithReference(saasDb)
            .WithReference(rabbitMq)
            .WithReference(redis)
            .WithReference(seq)
            .WaitFor(rabbitMq)
            .WaitFor(redis)
            .WaitForCompletion(migrator);

        builder
            .AddProject<Meta_Dow_Projects_HttpApi_Host>(
                MetaDowNames.ProjectsApi,
                launchProfileName: LaunchProfileName
            )
            .WithExternalHttpEndpoints()
            .WithReference(adminDb)
            .WithReference(projectsDb)
            .WithReference(rabbitMq)
            .WithReference(redis)
            .WithReference(seq)
            .WaitFor(rabbitMq)
            .WaitFor(redis)
            .WaitForCompletion(migrator);

        builder
            .AddProject<Meta_Dow_Gateway>(MetaDowNames.Gateway, launchProfileName: LaunchProfileName)
            .WithExternalHttpEndpoints()
            .WithReference(seq)
            .WaitFor(admin)
            .WaitFor(identity)
            .WaitFor(saas);

        builder
            .AddProject<Meta_Dow_AuthServer>(MetaDowNames.AuthServer, launchProfileName: LaunchProfileName
            .WithExternalHttpEndpoints()
            .WithReference(adminDb)
            .WithReference(identityDb)
            .WithReference(saasDb)
            .WithReference(rabbitMq)
            .WithReference(redis)
            .WithReference(seq)
            .WaitFor(rabbitMq)
            .WaitFor(redis)
            .WaitForCompletion(migrator);

        builder.Build().Run();
    }
}
""",
    )
    names = DST / "shared/Meta.Dow.Shared/Microsoft/Extensions/Hosting/MetaDowNames.cs"
    text = names.read_text(encoding="utf-8")
    text = text.replace('public const string Postgres = "postgres";', 'public const string MongoDb = "mongodb";')
    text = text.replace("TaskyAdministration", "MetaDowAdministration")
    names.write_text(text, encoding="utf-8")

    csproj = DST / "apps/Meta.Dow.AppHost/Meta.Dow.AppHost.csproj"
    t = csproj.read_text(encoding="utf-8")
    t = t.replace("Aspire.Hosting.PostgreSQL", "Aspire.Hosting.MongoDB")
    t = "\n".join(
        line
        for line in t.splitlines()
        if "WebApp" not in line
    ) + "\n"
    csproj.write_text(t, encoding="utf-8")


def patch_auth_server() -> None:
    p = DST / "apps/Meta.Dow.AuthServer/MetaDowAuthServerModule.cs"
    text = p.read_text(encoding="utf-8")
    text = text.replace("using Volo.Abp.MongoDB;\n", "using Volo.Abp.MongoDB;\n")
    text = text.replace(
        "AppContext.SetSwitch(\"Npgsql.EnableLegacyTimestampBehavior\", true);\n\n        ",
        "",
    )
    p.write_text(text, encoding="utf-8")


def write_directory_build() -> None:
    write(
        DST / "Directory.Build.props",
        """<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
""",
    )


def cleanup_csproj_refs() -> None:
    for csproj in DST.rglob("*.csproj"):
        t = csproj.read_text(encoding="utf-8")
        t = t.replace("EntityFrameworkCore", "MongoDB")
        t = t.replace("Microsoft.EntityFrameworkCore.Tools", "REMOVED_EF_TOOLS")
        t = re.sub(
            r"\s*<PackageReference Include=\"REMOVED_EF_TOOLS\"[\s\S]*?</PackageReference>",
            "",
            t,
        )
        t = re.sub(
            r"\s*<PackageReference Include=\"REMOVED_EF_TOOLS\"[\s\S]*?/>",
            "",
            t,
        )
        t = t.replace("OpenIddict.Abstractions", "OpenIddict.Abstractions")
        csproj.write_text(t, encoding="utf-8")


def main() -> None:
    print("copy...")
    copy_tree()
    print("transform...")
    transform_files()
    print("rename...")
    rename_paths()
    rename_ef_dirs()
    print("data layers...")
    rewrite_data_layers()
    patch_hosting_csproj()
    patch_microservice_shared()
    patch_migrator()
    patch_app_host()
    patch_auth_server()
    write_directory_build()
    cleanup_csproj_refs()
    print("done")


if __name__ == "__main__":
    main()
