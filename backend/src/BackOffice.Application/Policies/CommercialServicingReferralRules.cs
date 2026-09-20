using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record CommercialServicingAuthoritySlice(DateTimeOffset EffectiveAt, JsonElement Proposal, decimal AnnualPremium);

public static class CommercialServicingReferralRules
{
    public static IReadOnlyList<ServicingReferralNeed> Assess(ResolvedQuoteTerm term,
        IReadOnlyList<CommercialServicingAuthoritySlice> slices, JsonElement binder, JsonElement authority)
    {
        ArgumentNullException.ThrowIfNull(term); ArgumentNullException.ThrowIfNull(slices);
        _ = QuoteRatingRules.CivilDuration(term);
        if (slices.Count is < 1 or > 100 || !CommercialUnderwritingConfiguration.Valid(binder, "binder") ||
            !CommercialUnderwritingConfiguration.Valid(authority, "authority")) throw Invalid();
        var groups = new Dictionary<(string Rule, string Dimension, Guid? Target), List<ServicingReferralTrigger>>();
        DateTimeOffset? previous = null; var count = 0;
        foreach (var slice in slices)
        {
            if (slice is null || slice.EffectiveAt.Offset != TimeSpan.Zero || slice.EffectiveAt < term.StartsAt || slice.EffectiveAt >= term.EndsAt ||
                previous is not null && slice.EffectiveAt <= previous) throw Invalid();
            CommercialCaptureRules.ValidateShapeAndIdentity(slice.Proposal);
            previous = slice.EffectiveAt;
            void Add(string source, IEnumerable<UnderwritingRequirement> requirements)
            {
                foreach (var requirement in requirements)
                {
                    if (++count > 50000) throw Invalid();
                    var key = (requirement.RuleCode, requirement.Dimension, requirement.TargetId);
                    if (!groups.TryGetValue(key, out var triggers))
                    { if (groups.Count >= 5000) throw Invalid(); groups.Add(key, triggers = []); }
                    triggers.Add(new(slice.EffectiveAt, source, requirement));
                }
            }
            Add("binder", CommercialReferralRules.AssessAuthority(binder, slice.Proposal, slice.AnnualPremium));
            Add("authority", CommercialReferralRules.AssessAuthority(authority, slice.Proposal, slice.AnnualPremium));
            Add("source", CommercialReferralRules.SourceReferrals(slice.Proposal).Select(x => x.Requirement));
        }
        return groups.OrderBy(x => x.Key.Rule, StringComparer.Ordinal).ThenBy(x => x.Key.Dimension, StringComparer.Ordinal).ThenBy(x => x.Key.Target)
            .Select(x => new ServicingReferralNeed(x.Key.Rule, x.Key.Dimension, x.Key.Target, x.Value.AsReadOnly())).ToArray();
    }
    private static ArgumentException Invalid() => new("A bounded ordered commercial servicing schedule and commercial authority are required.");
}
