using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record CommercialExposureLocation(Guid RiskItemId, string District, decimal SumInsured);
public sealed record CommercialExposureSlice(Guid BookId, Guid PolicyId, Guid TermId, Guid VersionId,
    DateTimeOffset TermStartsAt, DateTimeOffset TermEndsAt, DateTimeOffset EffectiveAt, DateTimeOffset ProcessedAt,
    int TransactionSequence, int SliceOrdinal, string TransactionKind, IReadOnlyList<CommercialExposureLocation> Locations);
public sealed record CommercialExposureLimit(Guid Id, Guid BookId, string District, int Version, decimal Amount,
    DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveTo, DateTimeOffset PublishedAt, string ContentHash, Guid? SupersedesLimitId = null);
public sealed record CommercialExposurePosition(string District, decimal PropertySum, int PolicyCount);
public sealed record CommercialExposureInterval(DateTimeOffset From, DateTimeOffset To, string District,
    decimal ProposedPropertySum, decimal OtherPropertySum, decimal ResultingPropertySum, int PolicyCount,
    Guid? LimitVersionId, string? LimitHash, decimal? Limit, decimal? Headroom, string? Blocker);
public sealed record CommercialExposureAssessment(IReadOnlyList<CommercialExposureInterval> Intervals)
{
    public bool Allowed => Intervals.Count > 0 && Intervals.All(x => x.Blocker is null);
}

// Pure property-only book arithmetic. Callers supply complete immutable source
// projections under the shared writer fence; these values grant no reservation.
public static class CommercialExposureRules
{
    public const decimal MaximumLocationProperty = 3m * QuoteRatingRules.MaximumMoney;

    public static string? NormalizeDistrict(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var district = input.Trim().ToUpperInvariant();
        if (district == "GIR") return district;
        var postcode = CommercialCaptureRules.NormalizePostcode(district + " 1AA");
        return postcode?.District == district ? district : null;
    }

    public static IReadOnlyList<CommercialExposurePosition> Snapshot(IEnumerable<CommercialExposureSlice> source,
        Guid bookId, DateTimeOffset effectiveAt, DateTimeOffset knownAt)
    {
        var rows = Visible(source, bookId, knownAt);
        return Positions(rows, effectiveAt);
    }

    public static CommercialExposureAssessment Assess(IEnumerable<CommercialExposureSlice> existing,
        IReadOnlyList<CommercialExposureSlice> proposed, IEnumerable<CommercialExposureLimit> publishedLimits,
        Guid bookId, Guid policyId, DateTimeOffset from, DateTimeOffset to, DateTimeOffset knownAt)
    {
        if (bookId == Guid.Empty || policyId == Guid.Empty || from >= to || proposed.Count == 0 ||
            proposed.Any(x => x.BookId != bookId || x.PolicyId != policyId || x.ProcessedAt > knownAt)) throw Invalid();
        ValidateSlices(proposed);
        var known = Visible(existing, bookId, knownAt);
        var limits = publishedLimits.Where(x => x.BookId == bookId).ToArray(); ValidateLimits(limits);
        var visibleLimits = limits.Where(x => x.PublishedAt <= knownAt).ToArray();
        var own = known.Where(x => x.PolicyId == policyId).ToArray();
        var other = known.Where(x => x.PolicyId != policyId).ToArray();
        // A full replacement must explicitly carry forward every known future
        // source change. Omitting one must never release future headroom.
        if (own.Where(x => from < x.EffectiveAt && x.EffectiveAt < to && x.TermStartsAt < to && from < x.TermEndsAt)
            .Any(x => !proposed.Any(p => p.TermId == x.TermId && p.EffectiveAt == x.EffectiveAt)))
            throw new ArgumentException("The proposed exposure timeline omits a known future own-policy slice.");
        var districts = proposed.Concat(own.Where(x => x.EffectiveAt < to && from < x.TermEndsAt && x.TermStartsAt < to))
            .SelectMany(x => x.Locations.Select(l => l.District)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (districts.Length == 0) districts = ["*"];
        var points = new SortedSet<DateTimeOffset> { from, to };
        void Point(DateTimeOffset value) { if (from < value && value < to) points.Add(value); }
        foreach (var row in known.Concat(proposed)) { Point(row.TermStartsAt); Point(row.TermEndsAt); Point(row.EffectiveAt); }
        foreach (var limit in visibleLimits.Where(x => x.District == "*" || districts.Contains(x.District))) { Point(limit.EffectiveFrom); Point(limit.EffectiveTo); }
        var breaks = points.ToArray(); var intervals = new List<CommercialExposureInterval>();
        for (var index = 0; index < breaks.Length - 1; index++)
        {
            var instant = breaks[index];
            if (!Winners(proposed, instant).Any()) throw new ArgumentException("The proposed exposure timeline has an uncovered interval.");
            var proposedPosition = Positions(proposed, instant).ToDictionary(x => x.District);
            var otherPosition = Positions(other, instant).ToDictionary(x => x.District);
            foreach (var district in districts)
            {
                proposedPosition.TryGetValue(district, out var ownValue); otherPosition.TryGetValue(district, out var otherValue);
                var ownAmount = ownValue?.PropertySum ?? 0m; var otherAmount = otherValue?.PropertySum ?? 0m;
                var total = checked(ownAmount + otherAmount); var count = (ownValue?.PolicyCount ?? 0) + (otherValue?.PolicyCount ?? 0);
                var candidates = ApplicableLimits(visibleLimits, district, instant);
                var limit = candidates.Length == 1 ? candidates[0] : null;
                var blocker = candidates.Length == 0 ? "commercial-exposure-limit-missing" : candidates.Length > 1 ? "commercial-exposure-limit-ambiguous"
                    : total > limit!.Amount ? "commercial-district-capacity-exceeded" : null;
                intervals.Add(new(instant, breaks[index + 1], district, ownAmount, otherAmount, total, count,
                    limit?.Id, limit?.ContentHash, limit?.Amount, limit is null ? null : limit.Amount - total, blocker));
            }
        }
        return new(intervals);
    }

    private static CommercialExposureSlice[] Visible(IEnumerable<CommercialExposureSlice> source, Guid bookId, DateTimeOffset knownAt)
    {
        if (bookId == Guid.Empty) throw Invalid();
        var rows = source.Where(x => x.BookId == bookId && x.ProcessedAt <= knownAt).ToArray(); ValidateSlices(rows); return rows;
    }

    private static IReadOnlyList<CommercialExposurePosition> Positions(IEnumerable<CommercialExposureSlice> rows, DateTimeOffset at) =>
        Winners(rows, at).Where(x => x.TransactionKind != "cancellation")
            .SelectMany(x => x.Locations.Where(l => l.SumInsured > 0).Select(l => new { x.PolicyId, l.District, l.SumInsured }))
            .GroupBy(x => x.District, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new CommercialExposurePosition(x.Key, x.Sum(l => l.SumInsured), x.Select(l => l.PolicyId).Distinct().Count())).ToArray();

    private static IEnumerable<CommercialExposureSlice> Winners(IEnumerable<CommercialExposureSlice> rows, DateTimeOffset at)
    {
        foreach (var policy in rows.GroupBy(x => x.PolicyId))
        {
            // Only active-term winners contribute. PolicyTemporalSelector's
            // scheduled/expired display fallbacks deliberately contribute zero.
            var term = policy.Where(x => x.TermStartsAt <= at && at < x.TermEndsAt).GroupBy(x => x.TermId)
                .OrderByDescending(x => x.First().TermStartsAt).ThenBy(x => x.Key).FirstOrDefault();
            var winner = term?.Where(x => x.EffectiveAt <= at).OrderByDescending(x => x.EffectiveAt)
                .ThenByDescending(x => x.TransactionSequence).ThenByDescending(x => x.SliceOrdinal).ThenBy(x => x.VersionId).FirstOrDefault();
            if (winner is not null) yield return winner;
        }
    }

    private static CommercialExposureLimit[] ApplicableLimits(CommercialExposureLimit[] visible, string district, DateTimeOffset at)
    {
        var applicable = visible.Where(x => x.EffectiveFrom <= at && at < x.EffectiveTo).ToArray();
        var candidates = applicable.Where(x => x.District == district).ToArray();
        if (candidates.Length == 0) candidates = applicable.Where(x => x.District == "*").ToArray();
        var byId = visible.ToDictionary(x => x.Id); var replaced = new HashSet<Guid>();
        foreach (var candidate in candidates)
            for (var ancestor = candidate.SupersedesLimitId; ancestor is Guid id; ancestor = byId[id].SupersedesLimitId) replaced.Add(id);
        return candidates.Where(x => !replaced.Contains(x.Id)).ToArray();
    }

    private static void ValidateSlices(IReadOnlyCollection<CommercialExposureSlice> rows)
    {
        if (rows.Select(x => x.VersionId).Distinct().Count() != rows.Count ||
            rows.GroupBy(x => x.TermId).Any(group => group.Select(x => (x.BookId, x.PolicyId, x.TermStartsAt, x.TermEndsAt)).Distinct().Count() != 1) ||
            rows.GroupBy(x => (x.TermId, x.TransactionSequence, x.SliceOrdinal)).Any(x => x.Count() > 1)) throw Invalid();
        foreach (var row in rows)
        {
            if (row.BookId == Guid.Empty || row.PolicyId == Guid.Empty || row.TermId == Guid.Empty || row.VersionId == Guid.Empty || row.TermStartsAt >= row.TermEndsAt ||
                row.EffectiveAt < row.TermStartsAt || row.EffectiveAt >= row.TermEndsAt || row.TransactionSequence < 1 || row.SliceOrdinal < 1 ||
                row.TransactionKind is not ("new-business" or "adjustment" or "renewal" or "cancellation") || row.Locations.Count > 100 ||
                row.TransactionKind == "cancellation" && row.Locations.Count != 0 || row.Locations.Select(x => x.RiskItemId).Distinct().Count() != row.Locations.Count) throw Invalid();
            foreach (var location in row.Locations)
                if (location.RiskItemId == Guid.Empty || NormalizeDistrict(location.District) != location.District || location.SumInsured < 0 ||
                    location.SumInsured > MaximumLocationProperty || decimal.Round(location.SumInsured, 2) != location.SumInsured) throw Invalid();
        }
    }

    private static void ValidateLimits(CommercialExposureLimit[] limits)
    {
        if (limits.Select(x => x.Id).Distinct().Count() != limits.Length) throw Invalid();
        var byId = limits.ToDictionary(x => x.Id);
        foreach (var limit in limits)
        {
            if (limit.Id == Guid.Empty || limit.BookId == Guid.Empty || limit.Version < 1 || limit.EffectiveFrom >= limit.EffectiveTo ||
                limit.District != "*" && NormalizeDistrict(limit.District) != limit.District || !ReferralRules.Hash(limit.ContentHash) ||
                limit.Amount < 0 || limit.Amount > QuoteRatingRules.MaximumMoney || decimal.Round(limit.Amount, 2) != limit.Amount) throw Invalid();
            if (limit.SupersedesLimitId is Guid parent && (!byId.TryGetValue(parent, out var previous) || previous.BookId != limit.BookId || previous.District != limit.District ||
                previous.Version >= limit.Version || previous.PublishedAt > limit.PublishedAt)) throw Invalid();
        }
    }

    private static ArgumentException Invalid() => new("Complete, normalized and internally consistent commercial exposure metadata is required.");
}
