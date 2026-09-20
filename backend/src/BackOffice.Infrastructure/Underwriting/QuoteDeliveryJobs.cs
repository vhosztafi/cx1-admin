using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed class QuoteDeliveryJobs(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    public async Task<(OutboxWork Work, bool RetryAllowed)> ReadAsync(ActorContext actor, Guid jobId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var delivery = await Find(db, jobId, token); var owned = await QuoteScope.ForQuoteAsync(db, actor, delivery.QuoteId, QuoteAccess.Read, token);
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == jobId && x.Kind == QuoteTermsService.WorkKind && x.SubjectRecordId == delivery.Id, token);
        var allowed = false;
        if (owned.Scope.Actor.HasCapability("integration-retry") && owned.Scope.Actor.HasCapability("quote-terms") && owned.Scope.Agency.State == "active" &&
            JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit) is not null)
        {
            try
            {
                var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == delivery.CycleId, token);
                var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId, token);
                var input = StoredRatingInput.ReadMotorTrade(cycle);
                var eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Input.Term, time.GetUtcNow(), token);
                var rating = await db.Set<QuoteRatingResult>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == cycle.CurrentRatingId, token);
                var held = new UnderwritingDecisionContext(owned, cycle, revision, rating, input, eligible, []);
                held.Current(owned.Quote.RowVersion, time.GetUtcNow(), currentPrice: true); await Validate(db, held, delivery, time.GetUtcNow(), token); allowed = true;
            }
            catch (QuoteOperationException) { /* Scoped history remains visible. */ }
        }
        await tx.CommitAsync(token); return (work, allowed);
    }
    public Task<CommandOutcome> RetryAsync(ActorContext actor, Guid jobId, byte[] version, string reason, string key, Guid correlationId, CancellationToken token)
    {
        reason = QuoteRatingService.Reason(reason, 1000); UnderwritingDecisionContext? held = null; QuoteTermsDelivery? delivery = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/jobs/{jobId:D}/retry", key, correlationId), new { jobId, version = Convert.ToBase64String(version), reason }, "quote.delivery-retry",
            async (db, ct) => {
                if (!actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "underwriting-access-denied");
                delivery = await Find(db, jobId, ct); held = await UnderwritingDecisionContext.Hold(db, actor, delivery.QuoteId, delivery.CycleId, "quote-terms", time.GetUtcNow(), false, ct);
                if (!held.Owned.Scope.Actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "underwriting-access-denied");
            },
            async (db, ct) => {
                var now = time.GetUtcNow(); held!.Current(held.Owned.Quote.RowVersion, now, currentPrice: true);
                var work = await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK) WHERE Id={jobId}").SingleAsync(ct);
                UnderwritingDecisionContext.CheckVersion(work.RowVersion, version, "stale-job");
                var expanded = JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit) ?? throw new QuoteOperationException(409, "job-not-retryable");
                var row = await db.Set<QuoteTermsDelivery>().SingleAsync(x => x.Id == delivery!.Id, ct); await Validate(db, held, row, now, ct);
                row.State = "queued"; row.CompletedAt = null; row.OutcomeCode = null;
                work.State = "pending"; work.AttemptLimit = expanded; work.NextAttemptAt = now; work.CompletedAt = null; work.ErrorCode = null; work.LeaseToken = null; work.LeaseExpiresAt = null;
                await db.SaveChangesAsync(ct);
                return new(work.Id, 202, JsonSerializer.Serialize(new { id = work.Id, kind = work.Kind, state = work.State, attempts = work.Attempts, nextAttemptAt = work.NextAttemptAt, attemptLimit = work.AttemptLimit, retryAllowed = false }), Etag: UnderwritingDecisionContext.Etag(work.RowVersion));
            }, token);
    }
    private static async Task Validate(BackOfficeDbContext db, UnderwritingDecisionContext held, QuoteTermsDelivery row, DateTimeOffset now, CancellationToken token)
    {
        if (row.State != "failed" || held.Cycle.CurrentDeliveryId != row.Id) throw new QuoteOperationException(409, "quote-delivery-superseded");
        await QuoteTermsService.CurrentTerms(db, held, row.TermsVersionId, now, token); await QuoteTermsService.Ready(db, held, now, true, token);
        var recipients = JsonSerializer.Deserialize<QuoteTermsRecipient[]>(row.RecipientSnapshotJson, QuoteRatingService.Json)!;
        var current = await QuoteTermsService.Recipients(db, held, recipients.Select(x => x.Id).ToArray(), token);
        if (!recipients.SequenceEqual(current) || row.AssuranceHashAtSend != await UnderwritingEvidenceService.Assurance(db, held.Cycle, held.Revision, token)) throw new QuoteOperationException(409, "quote-delivery-context-stale");
    }
    private static async Task<QuoteTermsDelivery> Find(BackOfficeDbContext db, Guid jobId, CancellationToken token) =>
        await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == jobId, token) ?? throw new QuoteOperationException(404, "job-not-found");
}
