using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public sealed class ServicingDeliveryDispatcher(SqlJobLeases leases,ServicingDeliveryWorker worker,ILogger<ServicingDeliveryDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease=await leases.ClaimKindAsync(ServicingTermsService.WorkKind,stoppingToken);
                if(lease is null){await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);continue;}
                try{await worker.ApplyAsync(lease,await worker.ExecuteProviderAsync(lease,stoppingToken),stoppingToken);}
                catch(ServicingDeliveryException e){await leases.FailAsync(lease,e.Failure,stoppingToken);}
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception)
            {
                logger.LogWarning("Demo servicing delivery interrupted; persisted work remains recoverable.");
                try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            }
        }
    }

    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ServicingTermsService>();builder.Services.AddSingleton<ServicingDeliveryWorker>();
        if(builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Cover:ServicingDeliveryWorkerEnabled",true))
            builder.Services.AddHostedService<ServicingDeliveryDispatcher>();
    }
}
