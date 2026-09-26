using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public sealed class FinancePaymentDispatcher(IServiceScopeFactory scopes,
    ILogger<FinancePaymentDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var leases = scope.ServiceProvider.GetRequiredService<SqlJobLeases>();
                var lease = await leases.ClaimKindAsync(FinancePaymentWorker.WorkKind, stoppingToken);
                if (lease is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }
                var worker = scope.ServiceProvider.GetRequiredService<FinancePaymentWorker>();
                try
                {
                    var outcome = await worker.ExecuteProviderAsync(lease, stoppingToken);
                    if (outcome is not null) await worker.ApplyAsync(lease, outcome, stoppingToken);
                }
                catch (FinancePaymentWorkerException failure)
                {
                    await leases.FailAsync(lease, failure.Failure, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Demo refund payment interrupted; the durable operation remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<FinancePaymentWorker>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) &&
            builder.Configuration.GetValue("Cover:FinancePaymentWorkerEnabled", true))
            builder.Services.AddHostedService<FinancePaymentDispatcher>();
    }
}
