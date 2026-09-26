using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public sealed class CapacityDispatcher(SqlJobLeases leases, CapacityWorker worker, ILogger<CapacityDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await leases.ClaimKindAsync(CapacityService.WorkKind, stoppingToken);
                if (lease is null) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                try { await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease, stoppingToken), stoppingToken); }
                catch (CapacityProviderException error) { await leases.FailAsync(lease, error.Failure, stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Demo capacity processing interrupted; persisted work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<CapacityService>(); builder.Services.AddScoped<CapacityReadModel>(); builder.Services.AddSingleton<CapacityWorker>();
        builder.Services.AddScoped<CapacityJobs>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) && builder.Configuration.GetValue("Cover:CapacityWorkerEnabled", true)) builder.Services.AddHostedService<CapacityDispatcher>();
    }
}
