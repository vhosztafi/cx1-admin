using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencyDistributionConfiguration(Guid RuleVersionId,IReadOnlySet<Guid> ProductVersionIds);
public static class AgencyDistributionService
{
    public static async Task<AgencyDistributionConfiguration?> Configuration(BackOfficeDbContext db,DateTimeOffset now,CancellationToken token)
    {
        var row=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope=="agency-distribution"&&x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);
        if(row is null)return null;
        var ids=AgencyDistributionRules.Parse(row.Values);
        return ids is null?null:new(row.Id,ids);
    }
    public static async Task<bool?> DraftEligible(BackOfficeDbContext db,Guid agencyId,DateTimeOffset now,CancellationToken token)
    {
        var rule=await Configuration(db,now,token);if(rule is null)return null;
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var selected=await(from grant in db.Set<AgencyDraftProduct>() join version in db.Set<ProductVersion>() on grant.ProductVersionId equals version.Id
            join product in db.Set<Product>() on version.ProductId equals product.Id join provider in db.Set<CapacityProvider>() on version.ProviderId equals provider.Id
            where grant.AgencyId==agencyId select new{version.Id,version.ProductId,version.EffectiveFrom,version.EffectiveTo,Code=product.Code,ProviderState=provider.State,GrantFrom=grant.EffectiveFrom}).ToListAsync(token);
        // Every selection must have an explicit grant and an available provider;
        // at least one must be effective now. A future selection is checked at its start.
        return selected.Count is >=1 and <=3&&selected.Select(x=>x.ProductId).Distinct().Count()==selected.Count
            &&selected.Any(x=>x.GrantFrom<=today)
            &&selected.All(x=>rule.ProductVersionIds.Contains(x.Id)&&x.ProviderState=="active"
                &&x.Code is "motor-trade-road-risks" or "motor-trade-combined" or "commercial-combined"
                &&x.EffectiveFrom<=(x.GrantFrom>today?Start(x.GrantFrom):now)
                &&(x.EffectiveTo is null||x.EffectiveTo>(x.GrantFrom>today?Start(x.GrantFrom):now)));
    }
    private static DateTimeOffset Start(DateOnly date)
    {
        var local=date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")));
    }
}
