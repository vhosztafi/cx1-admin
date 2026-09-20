using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class CommercialUnderwritingCancellationSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Commercial cancellation seed requires held initialization.");
        if(await db.Set<SettingVersion>().AnyAsync(x=>x.Scope==CancellationConfiguration.CommercialScope,token))return;
        var actor=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example",token);
        if(actor.State!="active")throw new InvalidOperationException("Commercial cancellation initialization requires the active demo administrator.");
        var clock=await db.Set<DemoClock>().SingleAsync(token);var now=clock.FrozenAt??DateTimeOffset.UtcNow;
        if(CancellationConfiguration.Parse(CancellationConfiguration.CommercialDemoJson,CancellationConfiguration.CommercialScope) is null)
            throw new InvalidOperationException("Bundled commercial cancellation settings are invalid.");
        db.Add(new SettingVersion{Scope=CancellationConfiguration.CommercialScope,Version=1,EffectiveFrom=now,
            Values=CancellationConfiguration.CommercialDemoJson,CreatedAt=now,CreatedBy=actor.Id});
        await db.SaveChangesAsync(token);
    }
}
