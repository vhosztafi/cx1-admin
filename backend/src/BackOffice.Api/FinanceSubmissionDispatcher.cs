using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public sealed class FinanceSubmissionDispatcher(IServiceScopeFactory scopes,
    ILogger<FinanceSubmissionDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var leases = scope.ServiceProvider.GetRequiredService<SqlJobLeases>();
                var lease = await leases.ClaimKindAsync(FinanceSubmissionWorker.WorkKind, stoppingToken);
                if (lease is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }
                var worker = scope.ServiceProvider.GetRequiredService<FinanceSubmissionWorker>();
                try
                {
                    var outcome = await worker.ExecuteProviderAsync(lease, stoppingToken);
                    if (outcome is not null) await worker.ApplyAsync(lease, outcome, stoppingToken);
                }
                catch (FinanceSubmissionWorkerException failure)
                {
                    await leases.FailAsync(lease, failure.Failure, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("Demo bordereau submission interrupted; durable work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<FinanceSubmissionWorker>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) &&
            builder.Configuration.GetValue("Cover:FinanceSubmissionWorkerEnabled", true))
            builder.Services.AddHostedService<FinanceSubmissionDispatcher>();
    }
}
