using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Meta.Dow;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        MetaDowLogging.Initialize();

        try
        {
            Log.Information("Starting Meta.Dow.AuthServer.");

            var builder = WebApplication.CreateBuilder(args);
            builder.AddServiceDefaults();
            builder.AddSharedEndpoints();

            
            
            

            builder.Host.AddAppSettingsSecretsJson().UseAutofac().UseSerilog();

            await builder.AddApplicationAsync<MetaDowAuthServerModule>().ConfigureAwait(false);

            var app = builder.Build();

            await app.InitializeApplicationAsync().ConfigureAwait(false);

            await app.RunAsync().ConfigureAwait(false);

            return 0;
        }
        catch (OperationCanceledException ex)
        {
            Log.Fatal(ex, "Meta.Dow.AuthServer terminated unexpectedly!");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }
}
