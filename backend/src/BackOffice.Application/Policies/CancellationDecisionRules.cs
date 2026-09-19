namespace BackOffice.Application.Policies;

public sealed record CancellationReasonRule(string Code, int MinimumNoticeDays, bool RequiresSenior,
    bool RequiresDistinctApprover, IReadOnlyList<string> EvidencePurposes);
public sealed record CancellationNoticeEvidence(Guid AssociationId, string Purpose, bool Accepted, DateTimeOffset? DeliveredAt);
public sealed record CancellationDecisionAssessment(IReadOnlyList<string> Blockers, DateTimeOffset SupportedFrom,
    DateOnly? NoticeEffectiveFrom);

public static class CancellationDecisionRules
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    public static IReadOnlyList<CancellationReasonRule> Reasons { get; } = Array.AsReadOnly(new[] {
        new CancellationReasonRule("insured-request",0,false,false,Array.AsReadOnly(new[]{"cancellation-request"})),
        new CancellationReasonRule("non-payment",7,true,true,Array.AsReadOnly(new[]{"cancellation-notice"})),
        new CancellationReasonRule("non-disclosure",7,true,true,Array.AsReadOnly(new[]{"cancellation-notice","cancellation-reason"})),
        new CancellationReasonRule("trade-ceased",0,false,false,Array.AsReadOnly(new[]{"cancellation-request"})),
        new CancellationReasonRule("insurer-instruction",0,true,true,Array.AsReadOnly(new[]{"insurer-instruction"})) });

    public static CancellationDecisionAssessment Assess(string reasonCode, DateTimeOffset termStart, DateTimeOffset termEnd,
        DateTimeOffset latestIssuedEffective, DateTimeOffset effective, DateTimeOffset now, bool laterTermIssued,
        bool canBackdate, IReadOnlyList<CancellationNoticeEvidence> evidence)
    {
        var rule = Reasons.SingleOrDefault(x => x.Code == reasonCode) ?? throw new ArgumentException("Unknown cancellation reason.");
        if (termStart >= termEnd || latestIssuedEffective < termStart || latestIssuedEffective >= termEnd || evidence is null || evidence.Count > 100 ||
            new[] { termStart, termEnd, latestIssuedEffective, effective, now }.Any(x => x.Offset != TimeSpan.Zero) ||
            evidence.Any(x => x is null || x.AssociationId == Guid.Empty || !rule.EvidencePurposes.Contains(x.Purpose) || x.DeliveredAt?.Offset != null && x.DeliveredAt.Value.Offset != TimeSpan.Zero) ||
            evidence.Select(x => x.AssociationId).Distinct().Count() != evidence.Count)
            throw new ArgumentException("Cancellation requires bounded, valid dates and uniquely associated evidence.");
        var blockers = new List<string>();
        if (laterTermIssued) blockers.Add("later-term-issued");
        if (effective < termStart || effective >= termEnd) blockers.Add("outside-term");
        if (effective < latestIssuedEffective) blockers.Add("before-latest-issued-slice");
        if (effective < now && !canBackdate) blockers.Add("cancellation-backdate-authority-required");
        foreach (var purpose in rule.EvidencePurposes)
            if (!evidence.Any(x => x.Purpose == purpose && x.Accepted)) blockers.Add("evidence-required:" + purpose);
        DateOnly? minimum = null;
        if (rule.MinimumNoticeDays > 0)
        {
            // An accepted upload is not delivery. Only an accepted notice with
            // an actual nonfuture delivery timestamp starts the notice period.
            var delivered = evidence.Where(x => x.Purpose == "cancellation-notice" && x.Accepted && x.DeliveredAt <= now)
                .Select(x => x.DeliveredAt).OfType<DateTimeOffset>().Order().ToArray();
            if (delivered.Length == 0) blockers.Add("cancellation-notice-delivery-required");
            else
            {
                var on = LocalDay(delivered[0]);
                if (on.DayNumber > DateOnly.MaxValue.DayNumber - rule.MinimumNoticeDays) blockers.Add("notice-period-incomplete");
                else
                {
                    minimum = on.AddDays(rule.MinimumNoticeDays);
                    if (LocalDay(effective) < minimum) blockers.Add("notice-period-incomplete");
                }
            }
        }
        return new(blockers.AsReadOnly(), latestIssuedEffective, minimum);
    }

    // Current grant resolution is performed under SQL locks by the caller.
    // A role without a current, cancellation-enabled grant cannot approve.
    public static bool CanApprove(string reasonCode, Guid requesterId, Guid approverId, bool currentCancellationGrant, bool seniorGrant)
    {
        var rule = Reasons.SingleOrDefault(x => x.Code == reasonCode) ?? throw new ArgumentException("Unknown cancellation reason.");
        return requesterId != Guid.Empty && approverId != Guid.Empty && currentCancellationGrant &&
            (!rule.RequiresSenior || seniorGrant) && (!rule.RequiresDistinctApprover || requesterId != approverId);
    }

    private static DateOnly LocalDay(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, London).DateTime);
}
