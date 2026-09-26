using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public sealed class QuoteLookupDispatcher(SqlJobLeases leases, QuoteLookupWorker worker,
    ILogger<QuoteLookupDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await leases.ClaimKindAsync(QuoteLookupService.WorkKind, stoppingToken);
                if (lease is null) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                try
                {
                    var result = await worker.ExecuteProviderAsync(lease, stoppingToken);
                    await worker.ApplyAsync(lease, result, stoppingToken);
                }
                catch (QuoteLookupProviderException failure)
                { await leases.FailAsync(lease, failure.Failure, stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Do not log query, provider payload or arbitrary exception messages.
                logger.LogWarning("Demo quote lookup interrupted; persisted work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<QuoteLookupService>();
        builder.Services.AddSingleton<QuoteLookupWorker>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) && builder.Configuration.GetValue("Cover:QuoteLookupWorkerEnabled", true))
            builder.Services.AddHostedService<QuoteLookupDispatcher>();
    }
}
