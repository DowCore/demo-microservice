using Serilog;

namespace Meta.Dow.DbMigrator;

internal static class Program
{
    private static Task Main(string[] args)
    {
        MetaDowLogging.Initialize();

        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();

        
        
        
        

        builder.Configuration.AddAppSettingsSecretsJson();

        builder.Logging.AddSerilog();

        builder.Services.AddHostedService<DbMigratorHostedService>();

        var host = builder.Build();

        return host.RunAsync();
    }
}
