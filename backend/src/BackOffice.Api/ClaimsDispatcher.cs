using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public sealed class ClaimsDispatcher(IServiceScopeFactory scopes,ILogger<ClaimsDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var leases=scope.ServiceProvider.GetRequiredService<SqlJobLeases>();
                var lease=await leases.ClaimKindAsync(ClaimsHandoffService.WorkKind,stoppingToken);
                if(lease is null){await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);continue;}
                var worker=scope.ServiceProvider.GetRequiredService<ClaimsHandoffWorker>();
                try{var outcome=await worker.ExecuteProvider(lease,stoppingToken);if(outcome is not null)await worker.Apply(lease,outcome,stoppingToken);}
                catch(ClaimsWorkerException failure){await leases.FailAsync(lease,failure.Failure,stoppingToken);}
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception)
            {
                logger.LogWarning("Demo claims operation interrupted; durable work remains recoverable.");
                try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ClaimsHandoffWorker>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) &&builder.Configuration.GetValue("Cover:OperationalClaimsWorkerEnabled",true))builder.Services.AddHostedService<ClaimsDispatcher>();
    }
}
