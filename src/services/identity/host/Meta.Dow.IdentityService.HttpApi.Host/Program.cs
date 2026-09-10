using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Meta.Dow.IdentityService;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        MetaDowLogging.Initialize();

        try
        {
            Log.Information("Starting web host.");

            var builder = WebApplication.CreateBuilder(args);
            builder.AddServiceDefaults();
            builder.AddSharedEndpoints();

            
            
            

            builder.Host.AddAppSettingsSecretsJson().UseAutofac().UseSerilog();
            await builder.AddApplicationAsync<IdentityServiceHttpApiHostModule>().ConfigureAwait(false);
            var app = builder.Build();
            await app.InitializeApplicationAsync().ConfigureAwait(false);
            await app.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }
}
