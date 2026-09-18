using System.Text.Json;
using BackOffice.Domain;

namespace BackOffice.Application.Underwriting;

public sealed record CapacityContext(Guid QuoteId, Guid CycleId, Guid SubmissionId, string SubmissionHash,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Dimension, decimal? RequestedAmount = null,
    int? MinimumAge = null, int? MaximumAge = null, string? QuestionId = null);
public sealed record CapacityExtension(string Dimension, decimal? MaximumAmount = null, int? MinimumAge = null,
    int? MaximumAge = null, string? QuestionId = null, bool? Permitted = null);
public sealed record CapacityDecision(Guid QuoteId, Guid CycleId, Guid SubmissionId, string SubmissionHash,
    string Outcome, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, IReadOnlyList<CapacityExtension> Extensions,
    IReadOnlyList<ReferralCondition> Conditions);

public static class CapacityRules
{
    private static readonly HashSet<string> MoneyDimensions = ["premium-limit", "stock-limit", "vehicle-limit", "tools-limit", "premises-limit"];

    public static CapacityExtension Extension(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("dimension", out var dimensionValue) || dimensionValue.ValueKind != JsonValueKind.String) throw Invalid();
        var dimension = dimensionValue.GetString()!;
        var fields = MoneyDimensions.Contains(dimension) ? new[] { "dimension", "maximumAmount" }
            : dimension == "driver-age" ? ["dimension", "minimumAge", "maximumAge"]
            : dimension == "trade-restriction" ? ["dimension", "questionId", "permitted"] : throw Invalid();
        var keys = value.EnumerateObject().Select(x => x.Name).ToArray();
        if (keys.Length != fields.Length || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || fields.Except(keys).Any()) throw Invalid();
        if (MoneyDimensions.Contains(dimension))
        {
            var amount = value.GetProperty("maximumAmount"); if (amount.ValueKind != JsonValueKind.String) throw Invalid();
            try { var money = Money.Parse(amount.GetString()!); if (money.Pence <= 0) throw Invalid(); return new(dimension, money.Pence / 100m); }
            catch (Exception error) when (error is FormatException or OverflowException) { throw Invalid(); }
        }
        if (dimension == "driver-age")
        {
            var minimum = value.GetProperty("minimumAge"); var maximum = value.GetProperty("maximumAge");
            if (minimum.ValueKind != JsonValueKind.Number || maximum.ValueKind != JsonValueKind.Number || !minimum.TryGetInt32(out var min) || !maximum.TryGetInt32(out var max) || min is < 16 or > 100 || max < min || max > 100) throw Invalid();
            return new(dimension, MinimumAge: min, MaximumAge: max);
        }
        var question = value.GetProperty("questionId");
        if (question.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(question.GetString()) || question.GetString()!.Length > 100 || question.GetString()!.Any(char.IsControl) || value.GetProperty("permitted").ValueKind != JsonValueKind.True) throw Invalid();
        return new(dimension, QuestionId: question.GetString(), Permitted: true);
    }

    // The two stock checks concern the same financial exposure, but their
    // referral decisions remain separate. Other cover dimensions never inherit it.
    public static string Dimension(string ruleCode, string dimension) => ruleCode switch {
        "cover-stock-custody" => "stock-limit", "cover-road-risks" => "vehicle-limit",
        "cover-tools-equipment" => "tools-limit", "cover-premises" => "premises-limit", _ => dimension
    };

    // Extent only: callers must also require current provider/submission pointers,
    // reviewed supplied proof, all carrier conditions, current actor authority and
    // unrelated referral/proof readiness. Conditional extent does not waive proof.
    public static bool Applies(CapacityDecision decision, CapacityContext current, DateTimeOffset now)
    {
        if (current.QuoteId == Guid.Empty || current.CycleId == Guid.Empty || current.SubmissionId == Guid.Empty ||
            current.StartsAt >= current.EndsAt || !ReferralRules.Hash(current.SubmissionHash) ||
            decision.QuoteId != current.QuoteId || decision.CycleId != current.CycleId || decision.SubmissionId != current.SubmissionId ||
            !ReferralRules.Hash(decision.SubmissionHash) || decision.SubmissionHash != current.SubmissionHash ||
            decision.Outcome is not ("approve" or "approve-with-conditions") || decision.ValidFrom is null || decision.ValidTo is null ||
            decision.ValidFrom > now || now >= decision.ValidTo || decision.ValidFrom > current.StartsAt || decision.ValidTo < current.EndsAt)
            return false;
        return decision.Extensions.Any(extension => Covers(extension, current.Dimension, current.RequestedAmount,
            current.MinimumAge, current.MaximumAge, current.QuestionId));
    }

    // Subject-neutral extent arithmetic shared by quote and servicing graphs.
    // Each caller must verify its own provenance, current authority and proof.
    public static bool Covers(CapacityExtension extension, string dimension, decimal? requestedAmount = null,
        int? minimumAge = null, int? maximumAge = null, string? questionId = null) =>
        extension.Dimension == dimension &&
            (MoneyDimensions.Contains(extension.Dimension)
                ? requestedAmount is >= 0 && extension.MaximumAmount is > 0 && extension.MaximumAmount >= requestedAmount
                : extension.Dimension == "driver-age"
                    ? minimumAge is >= 16 && maximumAge is <= 100 && minimumAge <= maximumAge && extension.MinimumAge >= 16 && extension.MaximumAge <= 100 && extension.MinimumAge <= minimumAge && extension.MaximumAge >= maximumAge
                    : extension.Dimension == "trade-restriction" && extension.Permitted == true && questionId is not null && extension.QuestionId == questionId);
    public static DateTimeOffset ResponseDue(DateTimeOffset submittedAt, int workingDays)
    {
        if (workingDays is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(workingDays));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var due = TimeZoneInfo.ConvertTime(submittedAt, zone).DateTime;
        for (var added = 0; added < workingDays;)
        {
            due = due.AddDays(1);
            if (due.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) added++;
        }
        // The demo calendar excludes weekends. A bank-holiday calendar is a
        // separate configuration concern; never silently claim one was consulted.
        return new DateTimeOffset(due, zone.GetUtcOffset(due)).ToUniversalTime();
    }
    private static ArgumentException Invalid() => new("A closed, dimension-specific capacity extension is required.");
}
