using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public sealed class MessageDeliveryDispatcher(IServiceScopeFactory scopes,ILogger<MessageDeliveryDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var leases=scope.ServiceProvider.GetRequiredService<SqlJobLeases>();
                var lease=await leases.ClaimKindAsync(MessageDeliveryService.WorkKind,stoppingToken);
                if(lease is null){await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);continue;}
                var worker=scope.ServiceProvider.GetRequiredService<MessageDeliveryWorker>();
                try{var outcome=await worker.ExecuteProvider(lease,stoppingToken);if(outcome is not null)await worker.Apply(lease,outcome,stoppingToken);}
                catch(MessageDeliveryException failure){await leases.FailAsync(lease,failure.Failure,stoppingToken);}
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception)
            {
                logger.LogWarning("Demo communication delivery interrupted; durable work remains recoverable.");
                try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<MessageDeliveryWorker>();
        if(builder.Environment.IsDevelopment()&&builder.Configuration.GetValue("Cover:OperationalDeliveryWorkerEnabled",true))builder.Services.AddHostedService<MessageDeliveryDispatcher>();
    }
}
