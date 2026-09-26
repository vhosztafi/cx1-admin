using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public sealed class RenewalLifecycleDispatcher(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time,
    RenewalLifecycleService lifecycle,SqlJobLeases leases,RenewalLapseNotificationWorker worker,ILogger<RenewalLifecycleDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextScan=DateTimeOffset.MinValue;
        var scanOffset=0;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now=time.GetUtcNow();
                if(now>=nextScan)
                {
                    await using var db=await factory.CreateDbContextAsync(stoppingToken);
                    var ids=await db.Set<PolicyTerm>().AsNoTracking().Where(t=>t.EndsAt<=now &&
                        !db.Set<RenewalLapseEvent>().Any(x=>x.TermId==t.Id) &&
                        !db.Set<PolicyTerm>().Any(x=>x.PolicyId==t.PolicyId && x.Id!=t.Id && x.StartsAt==t.EndsAt) &&
                        !db.Set<PolicyVersion>().Any(v=>v.Id==t.CurrentVersionId && db.Set<PolicyTransaction>().Any(x=>x.Id==v.TransactionId && x.Kind=="cancellation")) &&
                        !db.Set<ServicingDraft>().Any(d=>d.BaseTermId==t.Id && d.Kind=="renewal" && d.State=="draft" &&
                            db.Set<ServicingCycle>().Any(c=>c.Id==d.CurrentCycleId && c.State=="rated" && c.CurrentAcceptanceId!=null)))
                        .OrderBy(t=>t.EndsAt).ThenBy(t=>t.Id).Select(t=>t.Id).Skip(scanOffset).Take(64).ToArrayAsync(stoppingToken);
                    // Revisit the queue in bounded passes. Configuration errors
                    // in the oldest terms must not starve later due renewals.
                    // Successful removals may defer a term until the next pass;
                    // ownership and eligibility are always rechecked on apply.
                    scanOffset=ids.Length<64 || scanOffset>int.MaxValue-64?0:scanOffset+64;
                    foreach(var id in ids)
                    {
                        try{await lifecycle.LapseDueAsync(id,stoppingToken);}
                        catch(QuoteOperationException){logger.LogWarning("A renewal deadline requires configuration review; no lapse was applied.");}
                    }
                    nextScan=now.AddSeconds(30);
                }
                var lease=await leases.ClaimKindAsync(RenewalLifecycleService.NotificationKind,stoppingToken);
                if(lease is not null)
                {
                    try{if(await worker.Deliver(lease,stoppingToken) is {} receipt)await worker.Apply(lease,receipt,stoppingToken);}
                    catch(RenewalLapseNotificationException error){await leases.FailAsync(lease,error.Failure,stoppingToken);}
                }
                else await Task.Delay(TimeSpan.FromSeconds(1),time,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception)
            {
                logger.LogWarning("Renewal lifecycle processing was interrupted; persisted work remains recoverable.");
                try{await Task.Delay(TimeSpan.FromSeconds(5),time,stoppingToken);}
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            }
        }
    }
    public static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<RenewalLifecycleService>();builder.Services.AddSingleton<RenewalLapseNotificationWorker>();
        // Explicit local demo opt-in prevents a clock worker running against
        // arbitrary developer/test databases before migration and configuration.
        if (DeploymentBoundary.DemoWorkersEnabled(builder) && builder.Configuration.GetValue("Cover:RenewalLifecycleWorkerEnabled",false))
            builder.Services.AddHostedService<RenewalLifecycleDispatcher>();
    }
}
