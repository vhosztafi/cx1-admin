using BackOffice.Infrastructure.Operations;

namespace BackOffice.Api;

public sealed class WorkflowTaskDispatcher(IServiceScopeFactory scopes, TimeProvider time, ILogger<WorkflowTaskDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<WorkflowTaskScanner>().Scan(offsets, stoppingToken);
                foreach (var issue in result.Issues)
                    logger.LogWarning("Workflow rule {RuleVersionId}, source {SourceKind}/{SourceEventId}, requires review: {Code}.",
                        issue.RuleVersionId, issue.SourceKind, issue.SourceEventId, issue.Code);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Workflow task scan was interrupted; persisted source events remain recoverable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), time, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<WorkflowTaskService>(); builder.Services.AddScoped<WorkflowTaskScanner>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) && builder.Configuration.GetValue("Cover:WorkflowTaskWorkerEnabled", false))
            builder.Services.AddHostedService<WorkflowTaskDispatcher>();
    }
}
