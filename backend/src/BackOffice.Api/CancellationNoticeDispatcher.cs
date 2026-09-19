using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public sealed class CancellationNoticeDispatcher(SqlJobLeases leases,CancellationNoticeWorker worker,TimeProvider time,ILogger<CancellationNoticeDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease=await leases.ClaimKindAsync(CancellationNoticeWorker.Kind,stoppingToken);
                if(lease is null){await Task.Delay(TimeSpan.FromSeconds(1),time,stoppingToken);continue;}
                try{if(await worker.Deliver(lease,stoppingToken) is{} receipt)await worker.Apply(lease,receipt,stoppingToken);}
                catch(CancellationNoticeException error){await leases.FailAsync(lease,error.Failure,stoppingToken);}
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception)
            {
                logger.LogWarning("Cancellation notice processing was interrupted; persisted work remains recoverable.");
                try{await Task.Delay(TimeSpan.FromSeconds(5),time,stoppingToken);}
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<CancellationNoticeWorker>();
        if(builder.Environment.IsDevelopment()&&builder.Configuration.GetValue("Cover:CancellationNoticeWorkerEnabled",false))
            builder.Services.AddHostedService<CancellationNoticeDispatcher>();
    }
}
