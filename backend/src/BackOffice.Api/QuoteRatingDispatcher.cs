using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public sealed class QuoteRatingDispatcher(SqlJobLeases leases, QuoteRatingWorker worker, ILogger<QuoteRatingDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await leases.ClaimKindAsync(QuoteRatingService.WorkKind, stoppingToken);
                if (lease is null) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                try { await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease, stoppingToken), stoppingToken); }
                catch (QuoteRatingProviderException failure) { await leases.FailAsync(lease, failure.Failure, stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Demo quote rating interrupted; persisted work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<QuoteRatingService>(); builder.Services.AddScoped<QuoteUnderwritingLifecycle>(); builder.Services.AddScoped<QuoteUnderwritingReadModel>();
        builder.Services.AddSingleton<QuoteRatingWorker>();
        builder.Services.AddScoped<QuoteRatingJobs>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) && builder.Configuration.GetValue("Cover:QuoteRatingWorkerEnabled", true)) builder.Services.AddHostedService<QuoteRatingDispatcher>();
    }
}
