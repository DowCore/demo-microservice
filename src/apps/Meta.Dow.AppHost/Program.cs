using Microsoft.Extensions.Hosting;
using Projects;

namespace Meta.Dow.AppHost;

internal class Program
{
    private static void Main(string[] args)
    {
        // VS F5 时 Aspire 用 run_session 拉起 AddProject；本机 IDE 调试桥经常超时，
        // Gateway 等会卡满 300s 后以空参数 fallback 失败。清空会话变量，强制进程启动。
        DisableAspireIdeRunSession();

        const string LaunchProfileName = "Aspire";
        var builder = DistributedApplication.CreateBuilder(args);

        // 仅 Mongo 挂卷，保留业务库；RabbitMQ/Redis/Seq 用临时容器加快启动
        var mongo = builder.AddMongoDB(MetaDowNames.MongoDb).WithDataVolume().WithMongoExpress();
        var rabbitMq = builder.AddRabbitMQ(MetaDowNames.RabbitMq).WithManagementPlugin();
        var redis = builder.AddRedis(MetaDowNames.Redis).WithRedisCommander();
        var seq = builder.AddSeq(MetaDowNames.Seq);

        var adminDb = mongo.AddDatabase(MetaDowNames.AdministrationDb);
        var identityDb = mongo.AddDatabase(MetaDowNames.IdentityServiceDb);
        var projectsDb = mongo.AddDatabase(MetaDowNames.ProjectsDb);
        var saasDb = mongo.AddDatabase(MetaDowNames.SaaSDb);

        var repoRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".."));
        var mongoRestore = builder
            .AddExecutable(
                "mongo-restore",
                "powershell",
                repoRoot,
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine("tools", "mongo-restore.ps1")
            )
            .WaitFor(mongo);

        // IDE run_session 已禁用，DbMigrator 走进程启动；仍 WaitForCompletion 后再起业务服务
        var migrator = builder
            .AddProject<Meta_Dow_DbMigrator>(MetaDowNames.DbMigrator, launchProfileName: LaunchProfileName)
            .WithReference(adminDb)
            .WithReference(identityDb)
            .WithReference(projectsDb)
            .WithReference(saasDb)
            .WithReference(seq)
            .WaitFor(mongo)
            .WaitForCompletion(mongoRestore);

        var admin = builder
            .AddProject<Meta_Dow_Administration_HttpApi_Host>(
                MetaDowNames.AdministrationApi,
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
            .WithReference(identityDb)
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
            .WaitForCompletion(migrator);

        builder
            .AddProject<Meta_Dow_AuthServer>(MetaDowNames.AuthServer, launchProfileName: LaunchProfileName)
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

    private static void DisableAspireIdeRunSession()
    {
        string[] keys =
        [
            "DEBUG_SESSION_PORT",
            "DEBUG_SESSION_ID",
            "DEBUG_SESSION_INFO",
            "DEBUG_SESSION_RUN_MODE",
            "DEBUG_SESSION_SERVER_CERTIFICATE",
        ];

        foreach (var key in keys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
