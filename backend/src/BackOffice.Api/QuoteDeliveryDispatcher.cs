using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public sealed class QuoteDeliveryDispatcher(SqlJobLeases leases, QuoteDeliveryWorker worker, ILogger<QuoteDeliveryDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await leases.ClaimKindAsync(QuoteTermsService.WorkKind, stoppingToken);
                if (lease is null) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                try { await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease, stoppingToken), stoppingToken); }
                catch (QuoteDeliveryException error) { await leases.FailAsync(lease, error.Failure, stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Demo quote delivery interrupted; persisted work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<QuoteTermsService>(); builder.Services.AddScoped<QuoteAcceptanceService>(); builder.Services.AddSingleton<QuoteDeliveryWorker>();
        builder.Services.AddScoped<QuoteTermsReadModel>();
        builder.Services.AddScoped<QuoteDeliveryJobs>();
        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Cover:QuoteDeliveryWorkerEnabled", true)) builder.Services.AddHostedService<QuoteDeliveryDispatcher>();
    }
}
