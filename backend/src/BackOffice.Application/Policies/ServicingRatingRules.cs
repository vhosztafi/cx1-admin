using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingAnnualSlice(DateTimeOffset EffectiveAt, decimal AnnualPremium, IReadOnlyList<Guid> ChangeIds);
public sealed record ServicingRiskSlice(DateTimeOffset EffectiveAt, IReadOnlyList<Guid> ChangeIds, RatingFacts Facts);
public sealed record ServicingRatedSlice(DateTimeOffset EffectiveAt, DateTimeOffset CoverageEndsAt, IReadOnlyList<Guid> ChangeIds,
    decimal AnnualPremium, decimal AnnualDelta, int RemainingDays, int AnnualDays, decimal Premium, decimal Tax, decimal BrokerCommission);
public sealed record CalculatedServicingRating(decimal BaseAnnualPremium, IReadOnlyList<ServicingRatedSlice> Slices,
    decimal Premium, decimal Tax, decimal BrokerCommission, decimal Fee, decimal GrossPayable, decimal NetDue);

// Trusted, already scoped cumulative risks only; never accepts browser-provided
// prices as authority. Calendar-day earning is the servicing financial contract.
// Exact instants still determine ordering and whether an interval is in term.
public static class ServicingRatingRules
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static CalculatedServicingRating Calculate(ResolvedQuoteTerm term, decimal baseAnnualPremium,
        IReadOnlyList<ServicingAnnualSlice> slices, int taxBasisPoints, int commissionBasisPoints, decimal fee)
    {
        ArgumentNullException.ThrowIfNull(term); ArgumentNullException.ThrowIfNull(slices);
        _ = QuoteRatingRules.CivilDuration(term);
        Money(baseAnnualPremium, positive: true); Money(fee);
        if (fee < 0 || taxBasisPoints is < 0 or > 10000 || commissionBasisPoints is < 0 or > 10000 || slices.Count is < 1 or > 100)
            throw new ArgumentException("Invalid servicing components or schedule size.");
        var start = LocalDay(term.StartsAt); var end = LocalDay(term.EndsAt);
        var annualDays = start.AddYears(1).DayNumber - start.DayNumber;
        var rated = new List<ServicingRatedSlice>(); var priorIds = new HashSet<Guid>();
        var previousAnnual = baseAnnualPremium; DateTimeOffset? previousDate = null;
        foreach (var slice in slices)
        {
            ArgumentNullException.ThrowIfNull(slice); ArgumentNullException.ThrowIfNull(slice.ChangeIds);
            Money(slice.AnnualPremium, positive: true);
            var ids = slice.ChangeIds.ToHashSet();
            if (slice.EffectiveAt < term.StartsAt || slice.EffectiveAt >= term.EndsAt ||
                previousDate is { } previous && slice.EffectiveAt <= previous ||
                ids.Count is < 1 or > 100 || ids.Contains(Guid.Empty) || ids.Count != slice.ChangeIds.Count ||
                !ids.IsProperSupersetOf(priorIds))
                throw new ArgumentException("Each in-term slice must add distinct changes to the complete cumulative schedule.");
            var effective = LocalDay(slice.EffectiveAt);
            var remainingDays = end.DayNumber - effective.DayNumber;
            // The annual delta is against the preceding full risk, never the
            // original price again. Each movement earns until the term end.
            var delta = slice.AnnualPremium - previousAnnual;
            var premium = Round(delta * remainingDays / annualDays);
            var tax = Round(premium * taxBasisPoints / 10000m);
            var commission = Round(premium * commissionBasisPoints / 10000m);
            Money(delta); Money(premium); Money(tax); Money(commission);
            rated.Add(new(slice.EffectiveAt, term.EndsAt, Array.AsReadOnly(ids.Order().ToArray()),
                slice.AnnualPremium, delta, remainingDays, annualDays, premium, tax, commission));
            previousAnnual = slice.AnnualPremium; previousDate = slice.EffectiveAt; priorIds = ids;
        }
        var premiums = rated.Select(x => x.Premium).ToArray();
        var taxes = rated.Select(x => x.Tax).ToArray();
        var commissions = rated.Select(x => x.BrokerCommission).ToArray();
        var total = ServicingRules.TotalMovement(premiums, taxes, commissions, fee);
        var premiumTotal = premiums.Sum(); var taxTotal = taxes.Sum(); var commissionTotal = commissions.Sum();
        Money(premiumTotal); Money(taxTotal); Money(commissionTotal);
        return new(baseAnnualPremium, rated.AsReadOnly(), premiumTotal, taxTotal, commissionTotal, fee, total.Gross, total.Net);
    }

    public static CalculatedServicingRating Rate(JsonElement config, ResolvedQuoteTerm term, decimal baseAnnualPremium,
        IReadOnlyList<ServicingRiskSlice> slices, int commissionBasisPoints, decimal fee, decimal? minimumPremium = null)
    {
        ArgumentNullException.ThrowIfNull(slices);
        if (slices.Count is < 1 or > 100 || !UnderwritingConfiguration.Valid(config, "rating"))
            throw new ArgumentException("A pinned rating definition and bounded cumulative risks are required.");
        var annual = slices.Select(slice =>
        {
            ArgumentNullException.ThrowIfNull(slice); ArgumentNullException.ThrowIfNull(slice.Facts);
            var fullRisk = QuoteRatingRules.Calculate(config, slice.Facts, term, commissionBasisPoints, minimumPremium);
            return new ServicingAnnualSlice(slice.EffectiveAt, fullRisk.AnnualPremium, slice.ChangeIds);
        }).ToArray();
        // Ignore the new-business fee emitted by the shared full-risk calculator.
        // The separately pinned servicing fee is charged once by Calculate.
        return Calculate(term, baseAnnualPremium, annual, config.GetProperty("taxRateBps").GetInt32(), commissionBasisPoints, fee);
    }

    private static DateOnly LocalDay(DateTimeOffset value) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, London).DateTime);
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static void Money(decimal value, bool positive = false)
    {
        if (value < -QuoteRatingRules.MaximumMoney || value > QuoteRatingRules.MaximumMoney || Round(value) != value || positive && value <= 0)
            throw new ArgumentException("Servicing amounts require bounded exact pennies.");
    }
}
