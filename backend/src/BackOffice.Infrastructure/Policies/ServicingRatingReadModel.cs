using System.Globalization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingRatingPage(Guid DraftId, Guid? RevisionId, string DraftState, string DraftEtag, DateTimeOffset AssessedAt,
    Guid? CurrentCycleId, ServicingRatingCycleView? Current, IReadOnlyList<ServicingRatingCycleView> Items,
    int? NextBeforeSequence, IReadOnlyList<string> Blockers);
public sealed record ServicingRatingCycleView(Guid Id, int Sequence, Guid RevisionId, Guid BaseVersionId, DateTimeOffset RequestedAt,
    string State, string StoredState, bool IsCurrent, bool Applicable, Guid WorkId, string JobState, int Attempts,
    int AttemptLimit, string? ErrorCode, string JobEtag, DateTimeOffset? NextAttemptAt, string InputHash,
    Guid RuleVersionId, Guid AgencyTermsVersionId, Guid? ServicingSettingVersionId, DateTimeOffset? SupersededAt,
    string? SupersededReason, ServicingRatingResultView? Result);
public sealed record ServicingRatingResultView(Guid Id, string Outcome, DateTimeOffset CompletedAt, DateTimeOffset ExpiresAt,
    string ResultHash, string Currency, string BaseAnnualPremium, string Premium, string Tax, string BrokerCommission,
    string Fee, string GrossPayable, string NetDue, bool DetailsAvailable,
    IReadOnlyList<Guid> ChangeIds, IReadOnlyList<ServicingRatingSliceView> Slices);
public sealed record ServicingRatingSliceView(DateTimeOffset EffectiveAt, DateTimeOffset CoverageEndsAt, IReadOnlyList<Guid> ChangeIds,
    string AnnualPremium, string AnnualDelta, int RemainingDays, int AnnualDays, string Premium, string Tax, string BrokerCommission);

public sealed class ServicingRatingReadModel(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<ServicingRatingPage> ReadAsync(ActorContext actor, Guid draftId, int? beforeSequence = null, int pageSize = 25,
        CancellationToken token = default)
    {
        if (beforeSequence is <= 0 || pageSize is < 1 or > 50) throw new QuoteOperationException(400, "invalid-query");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var draft = await ServicingDraftService.HoldDraft(db, actor, draftId, false, token);
        var now = time.GetUtcNow();
        var rows = await db.Set<ServicingCycle>().AsNoTracking().Where(x => x.DraftId == draftId && (beforeSequence == null || x.Sequence < beforeSequence))
            .OrderByDescending(x => x.Sequence).Take(pageSize + 1).ToArrayAsync(token);
        var page = rows.Take(pageSize).ToArray();
        ServicingCycle? current = draft.CurrentCycleId is { } currentId
            ? page.SingleOrDefault(x => x.Id == currentId) ?? await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == currentId && x.DraftId == draftId, token)
            : null;
        var selected = current is null ? page : page.Append(current).DistinctBy(x => x.Id).ToArray();
        var ids = selected.Select(x => x.Id).ToArray(); var workIds = selected.Select(x => x.WorkId).ToArray();
        // Bounded batches retain the current cycle separately from a historical
        // page. Amounts and results always belong to the selected cycle/work.
        var works = await db.Set<OutboxWork>().AsNoTracking().Where(x => workIds.Contains(x.Id) && x.Kind == ServicingRatingService.WorkKind)
            .ToDictionaryAsync(x => x.Id, token);
        var results = await db.Set<ServicingRatingResult>().AsNoTracking().Where(x => x.DraftId == draftId && ids.Contains(x.CycleId))
            .ToDictionaryAsync(x => x.CycleId, token);
        var blockers = new List<string>(); var currentPins = false;
        if (current is not null && draft.State == "draft")
        {
            try
            {
                var held = await ServicingRatingScope.HoldAsync(db, actor, draftId, now, token, write: false);
                var latest = await db.Set<PolicyVersion>().Where(x => x.PolicyId == draft.PolicyId && x.TermId == draft.BaseTermId)
                    .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Sequence).Select(x => x.Id).FirstAsync(token);
                currentPins = (held.Renewal is not null || latest == current.BaseVersionId) && ServicingRatingScope.Matches(held, current, ServicingRatingInput.Read(current.InputJson, current.InputHash));
                if (!currentPins) blockers.Add("servicing-rating-cycle-stale");
            }
            catch (QuoteOperationException error) { blockers.Add(error.Code); }
            catch (ArgumentException) { blockers.Add("servicing-rating-input-unavailable"); }
        }
        ServicingRatingCycleView Project(ServicingCycle cycle)
        {
            if (!works.TryGetValue(cycle.WorkId, out var work) || work.SubjectRecordId != cycle.Id)
                throw new QuoteOperationException(409, "servicing-rating-job-unavailable");
            results.TryGetValue(cycle.Id, out var result);
            var isCurrent = draft.State == "draft" && draft.CurrentCycleId == cycle.Id && draft.CurrentRevisionId == cycle.RevisionId;
            var resultView = result is null ? null : Result(result);
            var applicable = isCurrent && currentPins && cycle.State == "rated" && result is { Outcome: "rated" } &&
                cycle.CurrentRatingId == result.Id && result.ExpiresAt > now && resultView!.DetailsAvailable;
            var state = !isCurrent || cycle.State == "superseded" ? "superseded"
                : cycle.State == "failed" ? "failed"
                : result is { Outcome: "rated" } && result.ExpiresAt <= now ? "expired"
                : !currentPins ? "stale" : cycle.State;
            if (isCurrent && state is "failed" or "expired") blockers.Add("servicing-rating-" + state);
            if (isCurrent && resultView is { DetailsAvailable: false }) blockers.Add("servicing-rating-result-unavailable");
            return new(cycle.Id, cycle.Sequence, cycle.RevisionId, cycle.BaseVersionId, cycle.CreatedAt, state, cycle.State, isCurrent, applicable,
                work.Id, work.State, work.Attempts, work.AttemptLimit, work.ErrorCode, Etag(work.RowVersion),
                work.State == "pending" ? work.NextAttemptAt : null, Convert.ToHexStringLower(cycle.InputHash), cycle.RatingRuleVersionId,
                cycle.AgencyTermsVersionId, cycle.ServicingSettingVersionId, cycle.SupersededAt, cycle.SupersededReason, resultView);
        }
        var views = selected.ToDictionary(x => x.Id, Project);
        var response = new ServicingRatingPage(draftId, draft.CurrentRevisionId, draft.State, Etag(draft.RowVersion), now,
            draft.CurrentCycleId, current is null ? null : views[current.Id], page.Select(x => views[x.Id]).ToArray(),
            rows.Length > pageSize ? page[^1].Sequence : null, blockers.Distinct(StringComparer.Ordinal).ToArray());
        await tx.CommitAsync(token); return response;
    }

    private static ServicingRatingResultView Result(ServicingRatingResult result)
    {
        ServicingRatingOutcome? outcome;
        try { outcome = JsonSerializer.Deserialize<ServicingRatingOutcome>(result.ResultJson, ServicingRatingService.Json); }
        catch (JsonException) { outcome = null; }
        var detailsAvailable = outcome?.Format == "servicing-rating-result-1" &&
            (result.Outcome == "rejected" || outcome.Rating?.Slices is { Count: > 0 });
        var slices = outcome?.Rating?.Slices?.Select(x => new ServicingRatingSliceView(x.EffectiveAt, x.CoverageEndsAt, x.ChangeIds,
            Money(x.AnnualPremium), Money(x.AnnualDelta), x.RemainingDays, x.AnnualDays, Money(x.Premium), Money(x.Tax), Money(x.BrokerCommission))).ToArray() ?? [];
        return new(result.Id, result.Outcome, result.CompletedAt, result.ExpiresAt, Convert.ToHexStringLower(result.ResultHash), "GBP",
            Money(result.BaseAnnualPremium), Money(result.Premium), Money(result.Tax), Money(result.BrokerCommission), Money(result.Fee),
            Money(result.GrossPayable), Money(result.NetDue), detailsAvailable, slices.SelectMany(x => x.ChangeIds).Distinct().Order().ToArray(), slices);
    }
    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Etag(byte[] version) => "\"" + Convert.ToBase64String(version) + "\"";
}
