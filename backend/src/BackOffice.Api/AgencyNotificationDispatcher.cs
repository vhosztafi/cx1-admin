using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public sealed class AgencyNotificationDispatcher(SqlJobLeases leases,AgencyNotificationWorker worker,ILogger<AgencyNotificationDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease=await leases.ClaimKindAsync(AgencyNotificationService.Kind,stoppingToken);
                if(lease is null){await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);continue;}
                try
                {
                    if(await worker.Deliver(lease,stoppingToken) is Guid receiptId)await worker.Apply(lease,receiptId,stoppingToken);
                }
                catch(AgencyNotificationProviderException failure){await leases.FailAsync(lease,failure.Failure,stoppingToken);}
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception)
            {
                logger.LogWarning("Demo agency delivery interrupted; persisted work remains recoverable.");
                try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AgencyNotificationPayload>();
        builder.Services.AddSingleton<AgencyNotificationService>();
        builder.Services.AddSingleton<AgencyNotificationWorker>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) &&builder.Configuration.GetValue("Cover:AgencyNotificationWorkerEnabled",true))
            builder.Services.AddHostedService<AgencyNotificationDispatcher>();
    }
}
