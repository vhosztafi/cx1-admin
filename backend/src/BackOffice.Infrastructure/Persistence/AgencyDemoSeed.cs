using System.Text.Json;
using BackOffice.Application.Parties;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class AgencyDemoSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Agency seed requires transaction.");
        foreach(var agency in await db.Set<Agency>().ToListAsync(token))
            if(!await db.Set<AgencyOnboarding>().AnyAsync(x=>x.AgencyId==agency.Id,token))
            {
                agency.NormalizedName=ClientIdentity.NormalizeName(agency.LegalName);
                db.Add(new AgencyOnboarding{AgencyId=agency.Id,Details=JsonSerializer.Serialize(new{legalName=agency.LegalName})});
            }
        if(!await db.Set<SettingVersion>().AnyAsync(x=>x.Scope=="agency-onboarding"&&x.Version==1,token))
            db.Add(new SettingVersion{Scope="agency-onboarding",Version=1,EffectiveFrom=new(2026,9,1,0,0,0,TimeSpan.Zero),Values="{\"demo\":true,\"evidenceAvailable\":false,\"activationAvailable\":false}"});
        const string complianceRules="""{"demo":true,"minimumPi":"1700000.00","tobaVersion":"2026.1","checkValidityDays":90,"scenarios":{"fca":"pass","financial-check":"pass","sanctions":"pass","ownership":"pass"}}""";
        var initialCompliance=await db.Set<SettingVersion>().SingleOrDefaultAsync(x=>x.Scope=="agency-compliance"&&x.Version==1,token);
        if(initialCompliance is null)
            db.Add(new SettingVersion{Scope="agency-compliance",Version=1,EffectiveFrom=new(2026,9,1,0,0,0,TimeSpan.Zero),Values=complianceRules});
        else if(initialCompliance.Values==complianceRules.Replace("1700000.00","1300000.00")&&!await db.Set<SettingVersion>().AnyAsync(x=>x.Scope=="agency-compliance"&&x.Version>1,token))
            // Preserve the earlier immutable demo rule and its evidence. The
            // source prototype uses £1.7m; a new version invalidates old checks.
            db.Add(new SettingVersion{Scope="agency-compliance",Version=2,EffectiveFrom=new(2026,9,1,0,0,0,TimeSpan.Zero),Values=complianceRules});
        await db.SaveChangesAsync(token);
    }
}
