using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingAuthoritySlice(DateTimeOffset EffectiveAt, UnderwritingRisk Risk);
public sealed record ServicingReferralTrigger(DateTimeOffset EffectiveAt, string Source, UnderwritingRequirement Requirement);
public sealed record ServicingReferralNeed(string RuleCode, string Dimension, Guid? RiskItemId, IReadOnlyList<ServicingReferralTrigger> Triggers);

public static class ServicingReferralRules
{
    // The caller supplies immutable annual risk for every rated date. Premium
    // movements and the final risk alone cannot establish servicing authority.
    // Current identity, effective grants, source ownership, decisions and proof
    // remain independent service checks; this predicate grants none of them.
    public static IReadOnlyList<ServicingReferralNeed> Assess(ResolvedQuoteTerm term, IReadOnlyList<ServicingAuthoritySlice> slices,
        JsonElement binder, JsonElement authority, int minimumTradingYears=5)
    {
        ArgumentNullException.ThrowIfNull(term);ArgumentNullException.ThrowIfNull(slices);
        _=QuoteRatingRules.CivilDuration(term);
        if (slices.Count is <1 or >100 || minimumTradingYears is <0 or >100 || !UnderwritingConfiguration.Valid(binder,"binder") ||
            !UnderwritingConfiguration.Valid(authority,"authority") || binder.GetProperty("productCode").GetString()!=authority.GetProperty("productCode").GetString())
            throw Invalid();
        var groups=new Dictionary<(string Rule,string Dimension,Guid? Target),List<ServicingReferralTrigger>>();
        DateTimeOffset? previous=null;var triggerCount=0;
        foreach(var slice in slices)
        {
            if (slice is null || slice.Risk is null || slice.EffectiveAt.Offset!=TimeSpan.Zero || slice.EffectiveAt<term.StartsAt || slice.EffectiveAt>=term.EndsAt ||
                previous is not null && slice.EffectiveAt<=previous || slice.Risk.Drivers is null || slice.Risk.Drivers.Count>1000 ||
                slice.Risk.TradeValues is null || slice.Risk.TradeValues.Count>1000 || slice.Risk.CoverLimits is null || slice.Risk.CoverLimits.Count>4) throw Invalid();
            previous=slice.EffectiveAt;
            void Add(string source,IReadOnlyList<UnderwritingRequirement> requirements)
            {
                foreach(var required in requirements)
                {
                    if (++triggerCount>50000) throw Invalid();
                    var key=(required.RuleCode,required.Dimension,required.TargetId);
                    if (!groups.TryGetValue(key,out var triggers))
                    { if (groups.Count>=5000) throw Invalid();triggers=[];groups.Add(key,triggers); }
                    triggers.Add(new(slice.EffectiveAt,source,required));
                }
            }
            Add("binder",UnderwritingRules.AssessAuthority(binder,slice.Risk,minimumTradingYears));
            Add("authority",UnderwritingRules.AssessAuthority(authority,slice.Risk,minimumTradingYears));
            Add("source",UnderwritingRules.SourceReferrals(slice.Risk,minimumTradingYears));
        }
        return groups.OrderBy(x=>x.Key.Rule,StringComparer.Ordinal).ThenBy(x=>x.Key.Dimension,StringComparer.Ordinal).ThenBy(x=>x.Key.Target)
            .Select(x=>new ServicingReferralNeed(x.Key.Rule,x.Key.Dimension,x.Key.Target,x.Value.AsReadOnly())).ToArray();
    }

    public static bool AuthorityAllows(ResolvedQuoteTerm term, IReadOnlyList<ServicingAuthoritySlice> slices,
        JsonElement binder, JsonElement grant, int minimumTradingYears=5)
    {
        // Assess the complete schedule before returning, including when an
        // earlier failure exists. Invalid later facts must still fail closed.
        var needs=Assess(term,slices,binder,grant,minimumTradingYears);
        return UnderwritingConfiguration.WithinBinder(grant,binder) && needs.All(x=>x.Triggers.All(t=>t.Source=="source"));
    }

    private static ArgumentException Invalid()=>new("A bounded ordered servicing risk schedule and same-product authority are required.");
}
