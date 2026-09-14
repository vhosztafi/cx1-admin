using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

// Explicit opt-in fictional browser/demo fixture. Each run adds its own agency and preserves history.
public static class AgencyNotificationDemo
{
    public static async Task<Guid> Create(IDbContextFactory<BackOfficeDbContext> factory,AgencyNotificationPayload payload)
    {
        var clock=new DemoTime();var service=new AgencyNotificationService(payload,clock);var leases=new SqlJobLeases(factory,clock);var worker=new AgencyNotificationWorker(factory,payload,clock);
        Guid agencyId,actorId;var jobs=new List<Guid>();
        await using(var db=await factory.CreateDbContextAsync())
        {
            DemoDatabase.ValidateDemoTarget(db.Database.GetConnectionString()!);
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            actorId=(await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example")).Id;
            var agency=new Agency{Reference=await AgencyDraftService.NextReference(db),LegalName="Fictional notification delivery demo",NormalizedName="FICTIONAL NOTIFICATION DELIVERY DEMO",State="active",CreatedBy=actorId};
            db.Add(agency);db.Add(new AgencyOnboarding{AgencyId=agency.Id,Details="{\"legalName\":\"Fictional notification delivery demo\"}",CreatedBy=actorId});await db.SaveChangesAsync();agencyId=agency.Id;
            var version=await db.Set<SettingVersion>().Where(x=>x.Scope=="agency-notification").Select(x=>(int?)x.Version).MaxAsync()??0;
            foreach(var scenario in new[]{"pass","reject","unavailable"})
            {
                var setting=new SettingVersion{Scope="agency-notification",Version=++version,EffectiveFrom=clock.GetUtcNow(),Values=JsonSerializer.Serialize(new{demo=true,scenario})};db.Add(setting);await db.SaveChangesAsync();
                var id=await service.EnqueueActivation(db,agencyId,Guid.NewGuid(),setting.Id,actorId,new(){Recipient="fictional-notification@cover.example",Template="agency-activated",Content="Fictional demo delivery fixture. No real recipient or activation decision."});
                jobs.Add((await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==id)).WorkId);
            }
            await tx.CommitAsync();
        }
        foreach(var job in jobs)
        {
            for(var i=0;i<6;i++)
            {
                var lease=await leases.ClaimWorkAsync(AgencyNotificationService.Kind,job);if(lease is null)break;
                try{if(await worker.Deliver(lease) is Guid receipt)await worker.Apply(lease,receipt);break;}
                catch(AgencyNotificationProviderException failure){await leases.FailAsync(lease,failure.Failure);clock.Advance();}
            }
        }
        return agencyId;
    }
    private sealed class DemoTime:TimeProvider
    {private DateTimeOffset now=DateTimeOffset.UtcNow.AddDays(-1);public override DateTimeOffset GetUtcNow()=>now;public void Advance()=>now=now.AddHours(1);}
}
