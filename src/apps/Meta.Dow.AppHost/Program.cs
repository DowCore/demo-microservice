using Microsoft.Extensions.Hosting;
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
}
