using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingAuthoritySlice(DateTimeOffset EffectiveAt, UnderwritingRisk Risk);
public sealed record ServicingReferralTrigger(DateTimeOffset EffectiveAt, string Source, UnderwritingRequirement Requirement);
public sealed record ServicingReferralNeed(string RuleCode, string Dimension, Guid? RiskItemId, IReadOnlyList<ServicingReferralTrigger> Triggers);
public sealed record ServicingConditionSlice(DateTimeOffset EffectiveAt, IReadOnlyList<ReferralCondition> Conditions);

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

    public static bool AuthorityAllows(ResolvedQuoteTerm term, IReadOnlyList<ServicingAuthoritySlice> slices,
        JsonElement binder, JsonElement grant, IReadOnlyList<ServicingConditionSlice> conditions, int minimumTradingYears=5)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        // Validate the entire risk schedule even when authority already fails.
        // Conditions must be parsed with ReferralRules.Condition against their
        // dated proposals by the caller, after current decision ownership checks.
        // This predicate does not accept evidence or resolve source referrals.
        _=Assess(term,slices,binder,grant,minimumTradingYears);
        if (conditions.Count>100) throw Invalid();
        var dates=slices.Select(x=>x.EffectiveAt).ToHashSet();
        // Up to 100 active conditions may apply at each of the 100 risk dates.
        // A single condition repeated across dates is not a new decision.
        var byDate=new Dictionary<DateTimeOffset,IReadOnlyList<ReferralCondition>>();
        foreach(var row in conditions)
        {
            if (row is null || row.EffectiveAt.Offset!=TimeSpan.Zero || !dates.Contains(row.EffectiveAt) ||
                row.Conditions is null || row.Conditions.Count>100 || row.Conditions.Any(x=>x is null) ||
                !byDate.TryAdd(row.EffectiveAt,row.Conditions)) throw Invalid();
        }
        var allowed=UnderwritingConfiguration.WithinBinder(grant,binder);
        foreach(var slice in slices)
            allowed &= ReferralRules.AuthorityBlockers(grant,binder,slice.Risk,
                byDate.GetValueOrDefault(slice.EffectiveAt)??[],minimumTradingYears).Count==0;
        return allowed;
    }
}
