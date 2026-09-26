using BackOffice.Infrastructure.Operations;

namespace BackOffice.Api;

public sealed class FileFinalizationDispatcher(IServiceScopeFactory scopes, TimeProvider time, ILogger<FileFinalizationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<FileFinalizationWorker>();
                var pending = await worker.Pending(stoppingToken, offset);
                // Revisit the queue after each bounded sweep, including rows
                // whose originator currently lacks authority.
                offset = pending.Count == 0 ? 0 : offset + pending.Count;
                foreach (var id in pending)
                {
                    try { await worker.Process(id, stoppingToken); }
                    catch (OperationalAccessException) { logger.LogWarning("File finalization {WorkId} requires current source authority.", id); }
                }
                await scope.ServiceProvider.GetRequiredService<FileService>().CleanupExpiredTemporary(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("File finalization was interrupted; pending files remain recoverable."); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), time, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        var configured = builder.Configuration["Cover:FileStoragePath"];
        builder.Services.AddSingleton<IOperationalFileStore>(_ =>
        {
            if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Configure a persistent private file volume.");
            var root = configured ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, ".local", "operational-files"));
            return new OperationalFileStore(root, [builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot")]);
        });
        builder.Services.AddScoped<FileService>(); builder.Services.AddScoped<FileFinalizationWorker>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) && builder.Configuration.GetValue("Cover:FileWorkerEnabled", false))
            builder.Services.AddHostedService<FileFinalizationDispatcher>();
    }
}
