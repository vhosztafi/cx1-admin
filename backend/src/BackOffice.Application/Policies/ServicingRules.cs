using BackOffice.Domain;

namespace BackOffice.Application.Policies;

public sealed record ServicingVersionCandidate(Guid Id, DateTimeOffset EffectiveAt, DateTimeOffset ProcessedAt,
    int TransactionSequence, int SliceOrdinal, bool Issued);
public sealed record ServicingScheduledChange(Guid ChangeId, Guid RiskItemId, string Kind, DateTimeOffset EffectiveAt);
public sealed record ServicingScheduleSlice(DateTimeOffset EffectiveAt, IReadOnlyList<Guid> ChangeIds);

// Pure rules only. Callers must authorize and restrict candidates to the same
// policy/term, validate typed changes, and hold the issue boundary themselves.
public static class ServicingRules
{
    private const decimal MaximumAmount = 9999999999999.99m;

    public static ServicingVersionCandidate? SelectVersion(IEnumerable<ServicingVersionCandidate> candidates,
        DateTimeOffset effectiveCutoff, DateTimeOffset processingCutoff)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Where(x => x.Issued && x.EffectiveAt <= effectiveCutoff && x.ProcessedAt <= processingCutoff)
            .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.TransactionSequence)
            .ThenByDescending(x => x.SliceOrdinal).ThenBy(x => x.Id).FirstOrDefault();
    }

    public static IReadOnlyList<ServicingScheduleSlice> BuildSchedule(DateTimeOffset termStart, DateTimeOffset termEnd,
        DateTimeOffset latestIssuedEffective, DateTimeOffset commonEffective, DateTimeOffset now,
        bool backdatingAuthorized, IReadOnlyList<ServicingScheduledChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (termStart >= termEnd || commonEffective < termStart || commonEffective >= termEnd ||
            commonEffective < latestIssuedEffective || changes.Count is < 1 or > 100)
            throw new ArgumentException("Schedule requires in-term, in-sequence changes and a common effective date.");
        if (commonEffective < now && !backdatingAuthorized)
            throw new ArgumentException("Backdating requires current explicit authority.");
        var ids = new HashSet<Guid>();
        var targets = new HashSet<(Guid, string, DateTimeOffset)>();
        foreach (var change in changes)
        {
            if (change.ChangeId == Guid.Empty || change.RiskItemId == Guid.Empty || !ids.Add(change.ChangeId) ||
                change.Kind is not ("driver" or "vehicle" or "premises" or "business" or "cover" or "policyholder") ||
                !targets.Add((change.RiskItemId, change.Kind, change.EffectiveAt)))
                throw new ArgumentException("Each change requires a unique identity and unambiguous typed target.");
            if (change.EffectiveAt < commonEffective || change.EffectiveAt >= termEnd ||
                change.Kind != "cover" && change.EffectiveAt != commonEffective)
                throw new ArgumentException("Only cover changes permit a later in-term date.");
            if (change.Kind == "driver" && change.EffectiveAt < now)
                throw new ArgumentException("Driver changes cannot be backdated.");
        }
        var cumulative = new List<Guid>();
        var result = new List<ServicingScheduleSlice>();
        foreach (var group in changes.GroupBy(x => x.EffectiveAt).OrderBy(x => x.Key))
        {
            cumulative.AddRange(group.OrderBy(x => x.ChangeId).Select(x => x.ChangeId));
            result.Add(new(group.Key, Array.AsReadOnly(cumulative.ToArray())));
        }
        return result.AsReadOnly();
    }

    public static decimal ProrateAnnualDelta(decimal annualDelta, DateOnly termStart, DateOnly effective, DateOnly termEnd)
    {
        ValidateMoney(annualDelta);
        if (termStart.Year == 9999) throw new ArgumentException("Term anniversary is out of range.");
        var anniversary = termStart.AddYears(1);
        if (effective < termStart || effective >= termEnd || termEnd > anniversary)
            throw new ArgumentException("Proration requires an ordered interval within the annual term basis.");
        return Round(annualDelta * (termEnd.DayNumber - effective.DayNumber) / (anniversary.DayNumber - termStart.DayNumber));
    }

    // Returns the signed opposite of the unearned part of this exact posted
    // component. The caller selects unreturned original components under lock.
    // Retained fees must not be passed to this earned-premium operation.
    public static decimal ReverseUnearned(decimal postedAmount, DateOnly coverageStart, DateOnly coverageEnd, DateOnly cancellation)
    {
        ValidateMoney(postedAmount);
        if (coverageStart >= coverageEnd) throw new ArgumentException("Component requires a positive coverage interval.");
        if (cancellation >= coverageEnd) return 0m;
        if (cancellation <= coverageStart) return -postedAmount;
        return Round(-postedAmount * (coverageEnd.DayNumber - cancellation.DayNumber) / (coverageEnd.DayNumber - coverageStart.DayNumber));
    }

    public static string AssessExperience(decimal? paid, decimal? outstanding, decimal? earnedPremium, bool evidenceAccepted)
    {
        foreach (var value in new[] { paid, outstanding, earnedPremium })
            if (value is { } amount)
            {
                ValidateMoney(amount);
                if (amount < 0) throw new ArgumentException("Experience cannot contain negative amounts.");
            }
        if (!evidenceAccepted || paid is null || outstanding is null || earnedPremium is null or 0m)
            return "information-required";
        return paid.Value + outstanding.Value > earnedPremium.Value * 0.50m ? "senior-referral" : "within-threshold";
    }

    // Delivered/effective dates must be derived from the corresponding instants
    // in Europe/London by the caller. Evidence and approval checks remain separate.
    public static string? CancellationDateBlock(DateTimeOffset termStart, DateTimeOffset termEnd,
        DateTimeOffset latestIssuedEffective, DateTimeOffset effective, bool laterTermIssued,
        DateOnly noticeDeliveredOn, DateOnly effectiveOn, int minimumNoticeDays)
    {
        if (termStart >= termEnd || minimumNoticeDays is < 0 or > 365)
            throw new ArgumentException("Cancellation requires valid term and notice configuration.");
        if (laterTermIssued) return "later-term-issued";
        if (effective < termStart || effective >= termEnd) return "outside-term";
        if (effective < latestIssuedEffective) return "before-latest-issued-slice";
        return effectiveOn.DayNumber - noticeDeliveredOn.DayNumber < minimumNoticeDays ? "notice-period-incomplete" : null;
    }

    public static (decimal Gross, decimal Net) TotalMovement(IReadOnlyList<decimal> premiums,
        IReadOnlyList<decimal> taxes, IReadOnlyList<decimal> commissions, decimal fee)
    {
        ArgumentNullException.ThrowIfNull(premiums);
        ArgumentNullException.ThrowIfNull(taxes);
        ArgumentNullException.ThrowIfNull(commissions);
        if (premiums.Count is < 1 or > 100 || taxes.Count != premiums.Count || commissions.Count != premiums.Count)
            throw new ArgumentException("Each slice requires premium, tax and commission components.");
        ValidateMoney(fee);
        if (fee < 0) throw new ArgumentException("The single adjustment fee cannot be negative.");
        foreach (var value in premiums.Concat(taxes).Concat(commissions)) ValidateMoney(value);
        var gross = premiums.Sum() + taxes.Sum() + fee;
        var net = gross - commissions.Sum();
        ValidateMoney(gross); ValidateMoney(net);
        return (gross, net);
    }

    private static decimal Round(decimal value) => Money.Round(value).Pence / 100m;
    private static void ValidateMoney(decimal value)
    {
        if (value < -MaximumAmount || value > MaximumAmount || decimal.Round(value, 2) != value)
            throw new ArgumentException("Component amount requires bounded signed exact pennies.");
    }
}
