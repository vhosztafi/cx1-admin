using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed class CapacityJobs(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    public async Task<(OutboxWork Work, bool RetryAllowed)> ReadAsync(ActorContext actor, Guid jobId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var submission = await Submission(db, jobId, token);
        var owned = await QuoteScope.ForQuoteAsync(db, actor, submission.QuoteId, QuoteAccess.Read, token);
        if (!owned.Scope.Actor.HasCapability("underwriting-read")) throw new QuoteOperationException(403, "underwriting-access-denied");
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == jobId && x.Kind == CapacityService.WorkKind && x.SubjectRecordId == submission.Id, token);
        var allowed = false;
        if (owned.Scope.Actor.HasCapability("integration-retry") && JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit) is not null)
        {
            try
            {
                var now = time.GetUtcNow();
                var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == submission.CycleId, token);
                var input = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json)!;
                var eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Input.Term, now, token);
                var grants = await QuoteUnderwritingScope.GrantsAsync(db, owned, cycle.ProductVersionId, eligible.BinderVersion, eligible.Capture.Product.Code, input.Input.Term, now, token);
                var escalation = await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == submission.EscalationId, token);
                allowed = owned.Scope.Actor.HasCapability("underwriting-escalate") && owned.Scope.Agency.State == "active" && grants.Count > 0 &&
                    owned.Quote.CurrentUnderwritingCycleId == cycle.Id && owned.Quote.CurrentRevisionId == cycle.QuoteRevisionId && cycle.State == "rated" && owned.Quote.State is not ("draft" or "bound" or "withdrawn") &&
                    eligible.RatingVersion.Id == cycle.RatingRuleVersionId && eligible.BinderVersion.Id == cycle.BinderVersionId && eligible.AuthorityVersion.Id == cycle.AuthorityVersionId &&
                    escalation.CurrentSubmissionId == submission.Id && escalation.CurrentResponseId is null && escalation.State == "failed" &&
                    await db.Set<QuoteRatingResult>().AnyAsync(x => x.Id == cycle.CurrentRatingId && x.Outcome == "rated" && x.ExpiresAt > now, token) &&
                    await db.Set<CapacityProvider>().AnyAsync(x => x.Id == escalation.ProviderId && x.State == "active", token);
            }
            catch (QuoteOperationException) { allowed = false; }
        }
        await tx.CommitAsync(token); return (work, allowed);
    }
    public Task<CommandOutcome> RetryAsync(ActorContext actor, Guid jobId, byte[] version, string reason, string key, Guid correlationId, CancellationToken token)
    {
        reason = QuoteRatingService.Reason(reason, 1000); UnderwritingDecisionContext? held = null; CapacitySubmission? submission = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/jobs/{jobId:D}/retry", key, correlationId),
            new { jobId, version = Convert.ToBase64String(version), reason }, "capacity.retry-requested",
            async (db, ct) =>
            {
                if (!actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "underwriting-access-denied");
                submission = await Submission(db, jobId, ct);
                held = await UnderwritingDecisionContext.Hold(db, actor, submission.QuoteId, submission.CycleId, "underwriting-escalate", time.GetUtcNow(), true, ct);
                if (!held.Owned.Scope.Actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "underwriting-access-denied");
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(held.Owned.Quote.RowVersion, now, currentPrice: true);
                // Work precedes escalation, matching worker and terminal handling.
                var work = await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK) WHERE Id={jobId}").SingleAsync(ct);
                if (work.Kind != CapacityService.WorkKind || work.SubjectRecordId != submission!.Id || work.ScenarioVersionId != submission.ScenarioVersionId) throw new QuoteOperationException(404, "job-not-found");
                UnderwritingDecisionContext.CheckVersion(work.RowVersion, version, "stale-job");
                var expanded = JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit) ?? throw new QuoteOperationException(409, "job-not-retryable");
                var escalation = await CapacityService.HoldEscalation(db, held, submission.EscalationId, ct);
                if (escalation.CurrentSubmissionId != submission.Id || escalation.CurrentResponseId is not null || escalation.State != "failed") throw new QuoteOperationException(409, "capacity-submission-superseded");
                escalation.State = "queued"; escalation.UpdatedAt = now;
                work.State = "pending"; work.AttemptLimit = expanded; work.NextAttemptAt = now; work.CompletedAt = null; work.ErrorCode = null; work.LeaseToken = null; work.LeaseExpiresAt = null;
                await db.SaveChangesAsync(ct);
                return new(work.Id, 202, JsonSerializer.Serialize(new { id = work.Id, kind = work.Kind, state = work.State, attempts = work.Attempts,
                    nextAttemptAt = work.NextAttemptAt, attemptLimit = work.AttemptLimit, retryAllowed = false }), Etag: UnderwritingDecisionContext.Etag(work.RowVersion));
            }, token);
    }
    private static async Task<CapacitySubmission> Submission(BackOfficeDbContext db, Guid jobId, CancellationToken token) =>
        await db.Set<CapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == jobId, token) ?? throw new QuoteOperationException(404, "job-not-found");
}
