using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

// Only the persisted fictional diagnostic adapter is dispatched by this worker.
public sealed class DiagnosticDispatcher(
    SqlJobLeases leases, DiagnosticDemoProvider provider, DiagnosticInbox inbox,
    ILogger<DiagnosticDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = await leases.ClaimAsync(stoppingToken);
                if (lease is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }
                try
                {
                    var reply = await provider.ExecuteAsync(lease, stoppingToken);
                    await inbox.ApplyAsync(lease, reply, stoppingToken);
                }
                catch (DiagnosticProviderException failure)
                {
                    await leases.FailAsync(lease, failure.Failure, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // A committed lease is reclaimed after expiry by the next worker.
            }
            catch (Exception)
            {
                // Unknown failures retain their lease for reconciliation. Never log payloads,
                // connection strings or arbitrary provider exception messages.
                logger.LogWarning("Diagnostic dispatcher interrupted; persisted work remains recoverable.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<SqlCommandBoundary>();
        builder.Services.AddSingleton<OperationalPaging>();
        builder.Services.AddSingleton<SqlJobLeases>();
        builder.Services.AddSingleton<DiagnosticDemoProvider>();
        builder.Services.AddSingleton<DiagnosticInbox>();
        // Demo fault scenarios are never dispatched by a production host.
        if (DeploymentBoundary.DemoWorkersEnabled(builder) && builder.Configuration.GetValue("Cover:DiagnosticWorkerEnabled", true))
            builder.Services.AddHostedService<DiagnosticDispatcher>();
    }
}
