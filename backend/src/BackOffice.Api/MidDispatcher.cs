using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Api;
public sealed class MidDispatcher(IServiceScopeFactory scopes,ILogger<MidDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>();
                await using var db=await factory.CreateDbContextAsync(stoppingToken);
                var unregistered=await db.Set<OutboxWork>().AsNoTracking().Where(x=>(x.Kind=="mid-update"||x.Kind=="cancellation-mid-removal")&&x.State=="pending"&&!db.Set<MidSubmission>().Any(s=>s.WorkId==x.Id)).OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(stoppingToken);
                if(unregistered is{} id)
                {
                    try{await scope.ServiceProvider.GetRequiredService<MidSubmissionRegistration>().Register(id,stoppingToken);}
                    catch(Exception error)when(error is OperationalAccessException or BackOffice.Infrastructure.Quotes.QuoteOperationException)
                    {
                        // A failed original source or revoked actor becomes one visible exception,
                        // not an endless unregistered queue loop. No provider effect is attempted.
                        await scope.ServiceProvider.GetRequiredService<MidSubmissionRegistration>().RecordUnavailable(id,stoppingToken);
                    }
                }
                var leases=scope.ServiceProvider.GetRequiredService<SqlJobLeases>();var lease=await leases.ClaimKindAsync("mid-update",stoppingToken)??await leases.ClaimKindAsync("cancellation-mid-removal",stoppingToken);
                if(lease is null){await Task.Delay(TimeSpan.FromSeconds(1),stoppingToken);continue;}
                var worker=scope.ServiceProvider.GetRequiredService<MidSubmissionWorker>();
                try{var result=await worker.ExecuteProvider(lease,stoppingToken);if(result is not null)await worker.Apply(lease,result,stoppingToken);}
                catch(MidWorkerException e){await leases.FailAsync(lease,e.Failure,stoppingToken);}
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception)
            {
                logger.LogWarning("Demo MID operation interrupted; durable work remains recoverable.");
                try{await Task.Delay(TimeSpan.FromSeconds(5),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<MidSubmissionRegistration>();builder.Services.AddScoped<MidSubmissionService>();builder.Services.AddScoped<MidSubmissionWorker>();
        if (DeploymentBoundary.DemoWorkersEnabled(builder) &&builder.Configuration.GetValue("Cover:OperationalMidWorkerEnabled",true))builder.Services.AddHostedService<MidDispatcher>();
    }
}
