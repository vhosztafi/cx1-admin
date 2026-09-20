using System.Text.Json;
using BackOffice.Domain;

namespace BackOffice.Application.Underwriting;

public sealed record CommercialCapacityExtension(string Dimension, decimal MaximumAmount, Guid? RiskItemId = null);
public sealed record CommercialCapacityContext(Guid QuoteId, Guid CycleId, Guid SubmissionId, string SubmissionHash,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Dimension, decimal RequestedAmount, Guid? RiskItemId = null);
public sealed record CommercialCapacityDecision(Guid QuoteId, Guid CycleId, Guid SubmissionId, string SubmissionHash,
    string Outcome, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, IReadOnlyList<CommercialCapacityExtension> Extensions);

// Explicit CC extent arithmetic. The caller still holds current provider,
// grant, submission, proof, internal decision and carrier-condition authority.
// District book capacity and outside-appetite source rules cannot be extended.
public static class CommercialCapacityRules
{
    public static bool LocationDimension(string dimension) => dimension is "single-location" or "maximum-estimated-loss";
    public static bool SupportedDimension(string dimension) => LocationDimension(dimension) || dimension is
        "premium-limit" or "employers-liability" or "public-liability" or "products-liability" or "business-interruption" or "contract-works";

    public static CommercialCapacityExtension Extension(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("dimension", out var field) || field.ValueKind != JsonValueKind.String) throw Invalid();
        var dimension = field.GetString()!;
        if (!SupportedDimension(dimension)) throw Invalid();
        var expected = LocationDimension(dimension) ? new[] { "dimension", "maximumAmount", "riskItemId" } : ["dimension", "maximumAmount"];
        var names = value.EnumerateObject().Select(x => x.Name).ToArray();
        if (names.Length != expected.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length || expected.Except(names).Any()) throw Invalid();
        Guid? id = null;
        if (LocationDimension(dimension))
        {
            var target = value.GetProperty("riskItemId");
            if (target.ValueKind != JsonValueKind.String || !Guid.TryParseExact(target.GetString(), "D", out var parsed) || parsed == Guid.Empty) throw Invalid();
            id = parsed;
        }
        var amount = value.GetProperty("maximumAmount");
        if (amount.ValueKind != JsonValueKind.String) throw Invalid();
        try
        {
            var money = Money.Parse(amount.GetString()!);
            if (money.Pence <= 0 || money.Pence / 100m > QuoteRatingRules.MaximumMoney) throw Invalid();
            return new(dimension, money.Pence / 100m, id);
        }
        catch (Exception error) when (error is FormatException or OverflowException) { throw Invalid(); }
    }

    public static bool Applies(CommercialCapacityDecision decision, CommercialCapacityContext current, DateTimeOffset now)
    {
        if (current.QuoteId == Guid.Empty || current.CycleId == Guid.Empty || current.SubmissionId == Guid.Empty ||
            current.StartsAt >= current.EndsAt || !ReferralRules.Hash(current.SubmissionHash) ||
            decision.QuoteId != current.QuoteId || decision.CycleId != current.CycleId || decision.SubmissionId != current.SubmissionId ||
            decision.SubmissionHash != current.SubmissionHash || decision.Outcome is not ("approve" or "approve-with-conditions") ||
            decision.ValidFrom is null || decision.ValidTo is null || decision.ValidFrom > now || now >= decision.ValidTo ||
            decision.ValidFrom > current.StartsAt || decision.ValidTo < current.EndsAt || !SupportedDimension(current.Dimension) ||
            current.RequestedAmount < 0 || current.RequestedAmount > QuoteRatingRules.MaximumMoney || decimal.Round(current.RequestedAmount, 2) != current.RequestedAmount ||
            (LocationDimension(current.Dimension) ? current.RiskItemId is null || current.RiskItemId == Guid.Empty : current.RiskItemId is not null)) return false;
        return decision.Extensions.Any(x => x.Dimension == current.Dimension && x.RiskItemId == current.RiskItemId &&
            x.MaximumAmount > 0 && x.MaximumAmount <= QuoteRatingRules.MaximumMoney && decimal.Round(x.MaximumAmount, 2) == x.MaximumAmount && x.MaximumAmount >= current.RequestedAmount);
    }

    private static ArgumentException Invalid() => new("A closed Commercial Combined extent with an exact subject is required.");
}
