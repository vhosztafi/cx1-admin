using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Underwriting;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public sealed class ServicingCapacityDispatcher(SqlJobLeases leases, ServicingCapacityWorker worker, ILogger<ServicingCapacityDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await leases.ClaimKindAsync(ServicingCapacityService.WorkKind, stoppingToken);
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
        builder.Services.AddScoped<ServicingCapacityService>(); builder.Services.AddScoped<ServicingCapacityReadModel>(); builder.Services.AddSingleton<ServicingCapacityWorker>();

        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Cover:ServicingCapacityWorkerEnabled", true)) builder.Services.AddHostedService<ServicingCapacityDispatcher>();
    }
}
