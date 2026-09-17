using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public sealed class ServicingRatingDispatcher(SqlJobLeases leases, ServicingRatingWorker worker, ILogger<ServicingRatingDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await leases.ClaimKindAsync(ServicingRatingService.WorkKind, stoppingToken);
                if (lease is null) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                try { await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease, stoppingToken), stoppingToken); }
                catch (QuoteRatingProviderException failure) { await leases.FailAsync(lease, failure.Failure, stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Demo servicing rating interrupted; persisted work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ServicingRatingService>();
        builder.Services.AddScoped<ServicingRatingReadModel>();
        builder.Services.AddScoped<ServicingRatingJobs>();
        builder.Services.AddSingleton<ServicingRatingWorker>();
        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Cover:ServicingRatingWorkerEnabled", true))
            builder.Services.AddHostedService<ServicingRatingDispatcher>();
    }
}
