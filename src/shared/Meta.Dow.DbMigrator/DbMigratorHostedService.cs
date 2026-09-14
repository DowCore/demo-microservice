using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Volo.Abp;

namespace Meta.Dow.DbMigrator;

public class DbMigratorHostedService(IHostApplicationLifetime hostApplicationLifetime, IConfiguration configuration)
    : IHostedService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly IHostApplicationLifetime _hostApplicationLifetime = hostApplicationLifetime;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var application = await AbpApplicationFactory
                .CreateAsync<MetaDowDbMigratorModule>(options =>
                {
                    options.Services.ReplaceConfiguration(_configuration);
                    options.UseAutofac();
                    options.Services.AddLogging(c => c.AddSerilog());
                })
                .ConfigureAwait(false);

            await application.InitializeAsync().ConfigureAwait(false);

            await application
                .ServiceProvider.GetRequiredService<MetaDowDbMigrationService>()
                .MigrateAsync(cancellationToken)
                .ConfigureAwait(false);

            await application.ShutdownAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DbMigrator failed");
            // 明确非 0，便于 Aspire 识别；避免宿主层出现难读的 0x80008081
            Environment.ExitCode = 1;
            throw;
        }
        finally
        {
            _hostApplicationLifetime.StopApplication();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
