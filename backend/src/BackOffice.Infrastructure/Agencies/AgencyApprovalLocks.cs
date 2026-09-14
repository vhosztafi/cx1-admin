using System.Security.Cryptography;
using System.Text;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Agencies;

// Shared transaction prerequisites for independent state and terms decisions.
internal static class AgencyApprovalLocks
{
    internal static async Task Authority(BackOfficeDbContext db,Guid actor,CancellationToken token)
    {
        // Hold current identity and role membership until commit, including revocation races.
        var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK) WHERE Id={actor}").SingleOrDefaultAsync(token);
        var links=await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={actor}").ToListAsync(token);
        var allowed=false;
        foreach(var link in links.OrderBy(x=>x.RoleId))
        {
            var role=await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").SingleAsync(token);
            allowed|=role.Scope=="internal"&&role.Code is "agency-admin" or "system-admin";
        }
        if(user is null||user.State!="active"||user.AgencyId is not null||!allowed)throw new AgencyCommandException(403,"agency-access-denied");
    }
    internal static async Task<string> Eligibility(BackOfficeDbContext db,ValidatedAgencyTerms terms,DateTimeOffset now,CancellationToken token)
    {
        // Range lock includes future and newly inserted rule versions. Publication
        // must not race configuration withdrawal after the final eligibility check.
        var settings=await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'agency-distribution'").AsNoTracking().ToListAsync(token);
        var setting=settings.Where(x=>x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefault();
        var eligible=setting is null?null:AgencyDistributionRules.Parse(setting.Values);
        if(eligible is null)throw new AgencyCommandException(503,"agency-distribution-unavailable");
        var families=new HashSet<Guid>();
        foreach(var selected in terms.Products.OrderBy(x=>x.ProductVersionId))
        {
            if(!eligible.Contains(selected.ProductVersionId))throw new AgencyCommandException(422,"agency-product-ineligible");
            var version=await db.Set<ProductVersion>().FromSqlInterpolated($"SELECT * FROM ProductVersion WITH(HOLDLOCK) WHERE Id={selected.ProductVersionId}").AsNoTracking().SingleOrDefaultAsync(token)??throw new AgencyCommandException(422,"agency-product-ineligible");
            var product=await db.Set<Product>().FromSqlInterpolated($"SELECT * FROM Product WITH(HOLDLOCK) WHERE Id={version.ProductId}").AsNoTracking().SingleAsync(token);
            var provider=await db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={version.ProviderId}").AsNoTracking().SingleAsync(token);
            var local=selected.EffectiveFrom.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);
            var effective=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")));
            if(effective<now)effective=now;
            if(!families.Add(product.Id)||product.Code is not ("motor-trade-road-risks" or "motor-trade-combined" or "commercial-combined")||provider.State!="active"||version.EffectiveFrom>effective||version.EffectiveTo is DateTimeOffset end&&end<=effective)throw new AgencyCommandException(422,"agency-product-ineligible");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(setting!.Id.ToString("D")+":"+terms.Fingerprint))).ToLowerInvariant();
    }
}
