using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class ServicingRatingJobs(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    public async Task<(OutboxWork Work, bool RetryAllowed)> ReadAsync(ActorContext actor, Guid jobId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var cycle = await db.Set<ServicingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == jobId, token)
            ?? throw new QuoteOperationException(404, "job-not-found");
        await ServicingDraftService.HoldDraft(db, actor, cycle.DraftId, false, token);
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == jobId && x.Kind == ServicingRatingService.WorkKind && x.SubjectRecordId == cycle.Id, token);
        var allowed = false;
        if (cycle.State == "failed" && cycle.CurrentRatingId is null &&
            JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit) is not null &&
            !await db.Set<ServicingRatingResult>().AnyAsync(x => x.WorkId == jobId, token))
        {
            try
            {
                var held = await ServicingRatingScope.HoldAsync(db, actor, cycle.DraftId, time.GetUtcNow(), token, write: false);
                var currentActor = held.Source.Scope.Actor;
                var latest = await db.Set<PolicyVersion>().Where(x => x.PolicyId == cycle.PolicyId && x.TermId == cycle.BaseTermId)
                    .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Sequence).Select(x => x.Id).FirstAsync(token);
                allowed = currentActor.HasCapability("integration-retry") && currentActor.HasCapability("quote-rate") &&
                    currentActor.HasCapability("policy-draft-write") && currentActor.HasCapability("policy-draft-rate") && (held.Renewal is not null || latest == cycle.BaseVersionId) &&
                    ServicingRatingScope.Matches(held, cycle, ServicingRatingInput.Read(cycle.InputJson, cycle.InputHash));
            }
            catch (QuoteOperationException) { allowed = false; }
        }
        await tx.CommitAsync(token); return (work, allowed);
    }

    public Task<CommandOutcome> RetryAsync(ActorContext actor, Guid jobId, byte[] version, string reason, string key,
        Guid correlation, CancellationToken token = default)
    {
        if (version.Length != 8) throw new QuoteOperationException(400, "invalid-version");
        reason = QuoteRatingService.Reason(reason, 1000); ServicingCycle? hint = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/jobs/{jobId:D}/retry", key, correlation),
            new { jobId, version = Convert.ToBase64String(version), reason }, "servicing.rating-retry-requested",
            async (db, ct) =>
            {
                if (!actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "servicing-rating-retry-denied");
                hint = await db.Set<ServicingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == jobId, ct)
                    ?? throw new QuoteOperationException(404, "job-not-found");
                var held = await ServicingRatingScope.HoldAsync(db, actor, hint.DraftId, time.GetUtcNow(), ct);
                if (!held.Source.Scope.Actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "servicing-rating-retry-denied");
                var input = ServicingRatingInput.Read(hint.InputJson, hint.InputHash);
                var latest = await db.Set<PolicyVersion>().Where(x => x.PolicyId == hint.PolicyId && x.TermId == hint.BaseTermId)
                    .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Sequence).Select(x => x.Id).FirstAsync(ct);
                // Both current configuration and exact draft applicability are
                // authorization preconditions, including for a stored receipt.
                if (held.Renewal is null && latest != hint.BaseVersionId || !ServicingRatingScope.Matches(held, hint, input))
                    throw new QuoteOperationException(409, "servicing-rating-cycle-stale");
            },
            async (db, ct) =>
            {
                var work = await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK) WHERE Id={jobId}").SingleAsync(ct);
                if (work.Kind != ServicingRatingService.WorkKind || work.SubjectRecordId != hint!.Id) throw new QuoteOperationException(404, "job-not-found");
                if (!CryptographicOperations.FixedTimeEquals(work.RowVersion, version)) throw new QuoteOperationException(412, "stale-job");
                var expanded = JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit)
                    ?? throw new QuoteOperationException(409, "job-not-retryable");
                var cycle = await db.Set<ServicingCycle>().FromSqlInterpolated($"SELECT * FROM ServicingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.Id}").SingleAsync(ct);
                if (cycle.State != "failed" || cycle.CurrentRatingId is not null || await db.Set<ServicingRatingResult>().AnyAsync(x => x.WorkId == jobId, ct))
                    throw new QuoteOperationException(409, "job-not-retryable");
                var now = time.GetUtcNow(); cycle.State = "rating-pending"; cycle.UpdatedAt = now;
                work.State = "pending"; work.AttemptLimit = expanded; work.NextAttemptAt = now; work.CompletedAt = null;
                work.ErrorCode = null; work.LeaseToken = null; work.LeaseExpiresAt = null;
                db.Add(new AuditEvent { ActorId = actor.UserId, SubjectRecordId = cycle.DraftId, EventType = "servicing.rating-retry-reason",
                    OccurredAt = now, CorrelationId = correlation, After = JsonSerializer.Serialize(new { jobId, reason, attemptLimit = expanded }, ServicingRatingService.Json) });
                await db.SaveChangesAsync(ct);
                return new CommandOutcome(work.Id, 202, JsonSerializer.Serialize(new { id = work.Id, kind = work.Kind, state = work.State,
                    attempts = work.Attempts, nextAttemptAt = work.NextAttemptAt, attemptLimit = work.AttemptLimit, retryAllowed = false }, ServicingRatingService.Json),
                    Etag: "\"" + Convert.ToBase64String(work.RowVersion) + "\"");
            }, token);
    }
}
