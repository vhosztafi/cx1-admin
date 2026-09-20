using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

// Product-level configuration metadata only. Quote rating still checks the
// actor, agency terms, requested policy term and current captured risk.
public static class AgencyCatalogRating
{
    public static async Task<HashSet<Guid>> ReadyAsync(BackOfficeDbContext db, DateTimeOffset now, CancellationToken token)
    {
        var result = new HashSet<Guid>();
        var setting = await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope=="underwriting-runtime" && x.EffectiveFrom<=now)
            .OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);
        var runtime = setting is null ? null : UnderwritingRuntimeConfiguration.Parse(setting.Values);
        if(runtime is null) return result;
        var scenario = await db.Set<SettingVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==runtime.ScenarioVersionId,token);
        var scenarioName = scenario is null ? null : UnderwritingRuntimeConfiguration.Scenario(scenario.Values);
        if(scenario is null || scenarioName is null || scenario.EffectiveFrom>now || scenario.Scope!="quote-rating/"+scenarioName ||
            !await db.Set<Team>().AnyAsync(x=>x.Id==runtime.RoutingTeamId,token)) return result;
        foreach(var pin in runtime.Products.Values)
        {
            var rows = await (from v in db.Set<ProductVersion>() join p in db.Set<Product>() on v.ProductId equals p.Id
                join c in db.Set<CapacityProvider>() on v.ProviderId equals c.Id
                join r in db.Set<RatingRuleVersion>() on v.ProductId equals r.ProductId
                join b in db.Set<BinderVersion>() on v.ProductId equals b.ProductId
                join a in db.Set<AuthorityVersion>() on v.Id equals a.ProductVersionId
                where v.Id==pin.ProductVersionId && r.Id==pin.RatingRuleVersionId && b.Id==pin.BinderVersionId && a.Id==pin.AuthorityVersionId
                    && c.State=="active" && v.State=="published" && r.State=="published" && b.State=="published" && a.State=="published"
                    && b.ProviderId==v.ProviderId && a.ProductId==v.ProductId && a.BinderVersionId==b.Id
                    && v.EffectiveFrom<=now && (v.EffectiveTo==null || now<v.EffectiveTo)
                    && r.EffectiveFrom<=now && now<r.EffectiveTo && b.EffectiveFrom<=now && now<b.EffectiveTo && a.EffectiveFrom<=now && now<a.EffectiveTo
                select new {v.Id,p.Code,v.Definition,Rating=r.DefinitionJson,Binder=b.DefinitionJson,Authority=a.DefinitionJson,b.ProviderId}).AsNoTracking().ToArrayAsync(token);
            foreach(var row in rows)
            {
                if(!QuoteRatingEligibility.PublishedProduct(row.Definition,row.Code))continue;
                try
                {
                    using var rating=JsonDocument.Parse(row.Rating);using var binder=JsonDocument.Parse(row.Binder);using var authority=JsonDocument.Parse(row.Authority);
                    bool Current(JsonElement value,string kind) => (row.Code==CommercialCaptureRules.ProductCode
                        ? CommercialUnderwritingConfiguration.Valid(value,kind) : UnderwritingConfiguration.Valid(value,kind))
                        && value.GetProperty("productCode").GetString()==row.Code
                        && value.GetProperty("effectiveFrom").GetDateTimeOffset()<=now && now<value.GetProperty("effectiveTo").GetDateTimeOffset();
                    if(Current(rating.RootElement,"rating") && Current(binder.RootElement,"binder") && Current(authority.RootElement,"authority")
                        && binder.RootElement.GetProperty("providerId").GetGuid()==row.ProviderId
                        && (row.Code==CommercialCaptureRules.ProductCode ? CommercialUnderwritingConfiguration.WithinBinder(authority.RootElement,binder.RootElement)
                            : UnderwritingConfiguration.WithinBinder(authority.RootElement,binder.RootElement)))result.Add(row.Id);
                }
                catch(Exception error) when(error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException) { }
            }
        }
        return result;
    }
}
