using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record QuoteDeliveryOutcome(Guid OperationId, string State, DateTimeOffset CompletedAt);
public sealed class QuoteDeliveryException(JobFailure failure) : Exception("Fictional quote delivery did not complete.")
{ public JobFailure Failure { get; } = failure; }

public sealed class QuoteDeliveryWorker(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<QuoteDeliveryOutcome> ExecuteProviderAsync(JobLease lease, CancellationToken token = default)
    {
        if (lease.Kind != QuoteTermsService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        if (delivery.ScenarioVersionId != lease.ScenarioVersionId || lease.OperationKey != $"quote-delivery/{delivery.Id:N}") throw Failure(JobFailure.ProviderConflict);
        var scenario = QuoteTermsSeed.Scenario(await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == delivery.ScenarioVersionId, token)) ?? throw Failure(JobFailure.InvalidPayload);
        var hash = Convert.FromHexString(delivery.PayloadHash);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var operation = await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed = operation is not null;
        if (operation is not null && (operation.ScenarioVersionId != lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, hash))) throw Failure(JobFailure.ProviderConflict);
        if (operation?.Result is not null)
        { var retained = JsonSerializer.Deserialize<QuoteDeliveryOutcome>(operation.Result, QuoteRatingService.Json)!; await tx.CommitAsync(token); return retained; }
        if (operation is null)
        { operation = new DemoProviderOperation { Kind = lease.Kind, OperationKey = lease.OperationKey, RequestHash = hash, ScenarioVersionId = lease.ScenarioVersionId, CreatedAt = time.GetUtcNow() }; db.Add(operation); }
        if (!existed && scenario == "transient-once")
        { operation.State = "transient-failed"; await db.SaveChangesAsync(token); await tx.CommitAsync(token); throw Failure(JobFailure.ProviderUnavailable); }
        var outcome = new QuoteDeliveryOutcome(operation.Id, scenario == "reject" ? "rejected" : "delivered", time.GetUtcNow());
        operation.State = outcome.State == "delivered" ? "succeeded" : "rejected"; operation.CompletedAt = outcome.CompletedAt; operation.Result = JsonSerializer.Serialize(outcome, QuoteRatingService.Json);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        if (!existed && scenario == "timeout-after-success") throw Failure(JobFailure.ProviderTimeout);
        return outcome;
    }

    public async Task<bool> ApplyAsync(JobLease lease, QuoteDeliveryOutcome outcome, CancellationToken token = default)
    {
        if (lease.Kind != QuoteTermsService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var hint = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        var cycleHint = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == hint.CycleId, token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={cycleHint.AgencyId}").AsNoTracking().SingleAsync(token);
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(hint.SentBy, null), token);
        var quote = await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={hint.QuoteId}").AsNoTracking().SingleAsync(token);
        var now = time.GetUtcNow(); var work = await SqlJobLeases.OwnedAsync(db, lease, now, token);
        if (work is null) return false;
        var operation = await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == outcome.OperationId && x.Kind == lease.Kind && x.OperationKey == lease.OperationKey, token);
        if (work.SubjectRecordId != hint.Id || operation is null || operation.Result != JsonSerializer.Serialize(outcome, QuoteRatingService.Json) ||
            operation.ScenarioVersionId != hint.ScenarioVersionId || lease.ScenarioVersionId != hint.ScenarioVersionId || lease.OperationKey != $"quote-delivery/{hint.Id:N}" ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash, Convert.FromHexString(hint.PayloadHash))) throw Failure(JobFailure.ProviderConflict);
        var delivery = await db.Set<QuoteTermsDelivery>().SingleAsync(x => x.Id == hint.Id, token);
        UnderwritingDecisionContext? held = null;
        if (identity is not null && delivery.State == "queued" && quote.CurrentUnderwritingCycleId == delivery.CycleId)
        {
            try
            {
                var actor = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
                held = await UnderwritingDecisionContext.Hold(db, actor, quote.Id, delivery.CycleId, "quote-terms", now, false, token);
                held.Current(quote.RowVersion, now, currentPrice: true);
                if (held.Cycle.CurrentDeliveryId != delivery.Id) throw new QuoteOperationException(409, "quote-delivery-superseded");
                _ = await QuoteTermsService.CurrentTerms(db, held, delivery.TermsVersionId, now, token);
                await QuoteTermsService.Ready(db, held, now, signing: true, token);
                var recipients = JsonSerializer.Deserialize<QuoteTermsRecipient[]>(delivery.RecipientSnapshotJson, QuoteRatingService.Json)!;
                var current = await QuoteTermsService.Recipients(db, held, recipients.Select(x => x.Id).ToArray(), token);
                if (!recipients.SequenceEqual(current) || delivery.AssuranceHashAtSend != await UnderwritingEvidenceService.Assurance(db, held.Cycle, held.Revision, token))
                    throw new QuoteOperationException(409, "quote-delivery-context-stale");
            }
            catch (QuoteOperationException) { held = null; }
        }
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id && x.AttemptNumber == lease.Attempt, token);
        delivery.State = held is null ? "superseded" : outcome.State == "delivered" ? "delivered" : "failed";
        delivery.CompletedAt = now; delivery.ProviderOperationId = operation.Id; delivery.AttemptId = attempt.Id;
        delivery.OutcomeCode = held is null ? "quote-delivery-context-stale" : outcome.State == "delivered" ? null : "provider-rejected";
        if (held is not null && delivery.State == "delivered")
        {
            var row = held.Owned.Quote; if (db.Entry(row).State == EntityState.Detached) db.Attach(row); row.State = "sent"; row.UpdatedAt = now;
            db.Add(new QuoteActivity { QuoteId = row.Id, RevisionId = held.Revision.Id, ActorId = delivery.SentBy, CreatedBy = delivery.SentBy, CreatedAt = now, OccurredAt = now, EventType = "quote.terms-delivered" });
        }
        attempt.EndedAt = now; attempt.Outcome = held is null ? "superseded" : outcome.State == "delivered" ? "succeeded" : "rejected";
        attempt.Response = JsonSerializer.Serialize(new { deliveryId = delivery.Id, state = delivery.State });
        work.State = "succeeded"; work.CompletedAt = now; work.Result = attempt.Response; work.LeaseToken = null; work.LeaseExpiresAt = null; work.ErrorCode = null;
        if (outcome.State == "rejected") await SqlJobLeases.MarkTerminalAsync(db, work, "provider-rejected", now, token);
        db.Add(new AuditEvent { ActorId = delivery.SentBy, EventType = "quote.delivery-completed", SubjectRecordId = quote.Id, CorrelationId = work.CorrelationId, OccurredAt = now, After = attempt.Response });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return true;
    }
    private static QuoteDeliveryException Failure(JobFailure failure) => new(failure);
}
