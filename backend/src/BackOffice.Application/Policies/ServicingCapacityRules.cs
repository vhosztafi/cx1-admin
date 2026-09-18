using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingCapacitySubject(Guid DraftId, Guid RevisionId, Guid CycleId, Guid RatingId,
    Guid CaseId, Guid ReferralId, Guid ProviderId, Guid SubmissionId, string SubmissionHash);
public sealed record ServicingCapacityExposure(string Dimension, Guid? TargetId, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, decimal? RequestedAmount = null, int? MinimumAge = null, int? MaximumAge = null,
    string? QuestionId = null);
public sealed record ServicingCapacityResponse(Guid Id, ServicingCapacitySubject Subject, string Outcome,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, IReadOnlyList<CapacityExtension> Extensions);

public static class ServicingCapacityRules
{
    private static readonly HashSet<string> States = ["draft", "queued", "sent", "queried", "approved", "conditional", "declined", "failed", "superseded"];

    // Exact retained response extent only. This grants neither actor authority
    // nor proof readiness. Callers must hold current scope, rating and provider,
    // require current selected evidence and resolve all current carrier conditions.
    // Check every dated exposure; the final schedule slice alone is insufficient.
    public static bool ExtentApplies(ServicingCapacityResponse response, ServicingCapacitySubject current,
        Guid? currentResponseId, string state, Guid? referralTargetId, ServicingCapacityExposure exposure,
        DateTimeOffset now)
    {
        if (!Valid(current) || response.Subject != current || response.Id == Guid.Empty || response.Id != currentResponseId ||
            !((state == "approved" && response.Outcome == "approve") ||
              (state == "conditional" && response.Outcome == "approve-with-conditions")) ||
            exposure.StartsAt >= exposure.EndsAt || exposure.TargetId == Guid.Empty || referralTargetId == Guid.Empty ||
            exposure.TargetId != referralTargetId || response.ValidFrom is null || response.ValidTo is null ||
            response.ValidFrom > now || now >= response.ValidTo || response.ValidFrom > exposure.StartsAt ||
            response.ValidTo < exposure.EndsAt || response.Extensions is null || response.Extensions.Count is < 1 or > 20 ||
            response.Extensions.Any(x => x is null)) return false;
        return response.Extensions.Any(extension => CapacityRules.Covers(extension, exposure.Dimension,
            exposure.RequestedAmount, exposure.MinimumAge, exposure.MaximumAge, exposure.QuestionId));
    }

    // Consume late/duplicate results into history, but never let them replace a
    // newer response or reactivate a withdrawn/reopened case. Current identity,
    // rating, base, provider and evidence must still be checked by the worker.
    public static bool CanApplyResponse(ServicingCapacitySubject incoming, ServicingCapacitySubject current,
        string state, Guid? currentResponseId) => Valid(current) && incoming == current &&
        state is "queued" or "sent" && currentResponseId is null;

    // Only the state changes. Retained submission/response pointers and immutable
    // histories survive withdraw/reopen; only a new submission can advance them.
    // Assignment is routing and requires an eligible current senior in the service.
    public static string ActionState(string state, string action)
    {
        if (!States.Contains(state) || state == "superseded") throw Invalid();
        return action switch
        {
            "assign" => state,
            "withdraw" when state is "queued" or "sent" or "queried" or "failed" => "draft",
            "reopen" when state is "approved" or "conditional" or "declined" => "draft",
            _ => throw Invalid()
        };
    }

    private static bool Valid(ServicingCapacitySubject subject) => subject.DraftId != Guid.Empty &&
        subject.RevisionId != Guid.Empty && subject.CycleId != Guid.Empty && subject.RatingId != Guid.Empty &&
        subject.CaseId != Guid.Empty && subject.ReferralId != Guid.Empty && subject.ProviderId != Guid.Empty &&
        subject.SubmissionId != Guid.Empty && ReferralRules.Hash(subject.SubmissionHash);
    private static ArgumentException Invalid() => new("The capacity action is unavailable in the current servicing case state.");
}
