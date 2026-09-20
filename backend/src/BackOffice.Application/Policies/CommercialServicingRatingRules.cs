using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record CommercialServicingRiskSlice(DateTimeOffset EffectiveAt, IReadOnlyList<Guid> ChangeIds, CommercialRatingFacts Facts);

// The owning service supplies held, complete commercial projections and exact
// published configuration pins. Full-risk rates establish annual prices only;
// the shared servicing calculator earns incremental movements and one fee.
public static class CommercialServicingRatingRules
{
    public static CalculatedServicingRating Rate(JsonElement config, ResolvedQuoteTerm term, decimal baseAnnualPremium,
        IReadOnlyList<CommercialServicingRiskSlice> slices, int commissionBasisPoints, decimal fee, decimal? minimumPremium = null)
    {
        ArgumentNullException.ThrowIfNull(slices);
        if (slices.Count is < 1 or > 100 || !CommercialUnderwritingConfiguration.Valid(config,"rating"))
            throw new ArgumentException("Published commercial rating configuration and bounded cumulative slices are required.");
        var annual = slices.Select(slice =>
        {
            ArgumentNullException.ThrowIfNull(slice); ArgumentNullException.ThrowIfNull(slice.Facts);
            var fullRisk = CommercialRatingRules.Calculate(config,slice.Facts,term,commissionBasisPoints,minimumPremium);
            return new ServicingAnnualSlice(slice.EffectiveAt,fullRisk.AnnualPremium,slice.ChangeIds);
        }).ToArray();
        return ServicingRatingRules.Calculate(term,baseAnnualPremium,annual,config.GetProperty("taxRateBps").GetInt32(),commissionBasisPoints,fee);
    }
}
