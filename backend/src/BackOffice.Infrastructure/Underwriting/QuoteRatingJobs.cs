using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed class QuoteRatingJobs(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    public async Task<(OutboxWork Work, bool RetryAllowed)> ReadAsync(ActorContext actor, Guid jobId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == jobId, token) ?? throw new QuoteOperationException(404, "job-not-found");
        var owned = await QuoteScope.ForQuoteAsync(db, actor, cycle.QuoteId, QuoteAccess.Read, token);
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == jobId && x.Kind == QuoteRatingService.WorkKind && x.SubjectRecordId == cycle.Id, token);
        var canRetry = owned.Scope.Actor.HasCapability("integration-retry") && owned.Scope.Actor.HasCapability("quote-rate") &&
            owned.Quote.CurrentUnderwritingCycleId == cycle.Id && owned.Quote.CurrentRevisionId == cycle.QuoteRevisionId && owned.Quote.State == "rating-pending" &&
            owned.Scope.Agency.State == "active" && owned.Scope.Client.IdentityState == "active" && owned.Scope.Relationship.State == "active" &&
            JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit) is not null;
        if (canRetry)
        {
            try
            {
                var input = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json)!;
                var eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Input.Term, time.GetUtcNow(), token);
                canRetry = eligible.RatingVersion.Id == cycle.RatingRuleVersionId && eligible.BinderVersion.Id == cycle.BinderVersionId && eligible.AuthorityVersion.Id == cycle.AuthorityVersionId &&
                    eligible.RuntimeVersion.Id == input.RuntimeVersionId && eligible.ScenarioVersion.Id == input.ScenarioVersionId &&
                    (await QuoteMatching.AssessAsync(db, owned.Quote, time.GetUtcNow(), token)).Code is null;
            }
            catch (QuoteOperationException) { canRetry = false; }
        }
        await tx.CommitAsync(token); return (work, canRetry);
    }

    public Task<CommandOutcome> RetryAsync(ActorContext actor, Guid jobId, byte[] version, string reason, string key, Guid correlationId, CancellationToken token)
    {
        if (version.Length != 8) throw new QuoteOperationException(400, "invalid-version");
        reason = QuoteRatingService.Reason(reason, 1000); UnderwritingCycle? cycle = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/jobs/{jobId:D}/retry", key, correlationId),
            new { jobId, version = Convert.ToBase64String(version), reason }, "quote.rating-retry-requested",
            async (db, ct) =>
            {
                if (!actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "underwriting-access-denied");
                cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == jobId, ct) ?? throw new QuoteOperationException(404, "job-not-found");
                var owned = await QuoteUnderwritingScope.HoldAsync(db, actor, cycle.QuoteId, "quote-rate", ct);
                if (!owned.Scope.Actor.HasCapability("integration-retry")) throw new QuoteOperationException(403, "underwriting-access-denied");
                if (owned.Quote.CurrentUnderwritingCycleId != cycle.Id || owned.Quote.CurrentRevisionId != cycle.QuoteRevisionId || owned.Quote.State != "rating-pending")
                    throw new QuoteOperationException(409, "underwriting-cycle-stale");
                var input = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json)!;
                var eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Input.Term, time.GetUtcNow(), ct);
                if (eligible.RatingVersion.Id != cycle.RatingRuleVersionId || eligible.BinderVersion.Id != cycle.BinderVersionId || eligible.AuthorityVersion.Id != cycle.AuthorityVersionId ||
                    eligible.RuntimeVersion.Id != input.RuntimeVersionId || eligible.ScenarioVersion.Id != input.ScenarioVersionId ||
                    (await QuoteMatching.AssessAsync(db, owned.Quote, time.GetUtcNow(), ct)).Code is not null) throw new QuoteOperationException(409, "underwriting-cycle-stale");
            },
            async (db, ct) =>
            {
                var work = await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK) WHERE Id={jobId}").SingleAsync(ct);
                if (work.Kind != QuoteRatingService.WorkKind || work.SubjectRecordId != cycle!.Id) throw new QuoteOperationException(404, "job-not-found");
                if (!CryptographicOperations.FixedTimeEquals(work.RowVersion, version)) throw new QuoteOperationException(412, "stale-job");
                var expanded = JobRetryBudget.ExpandedLimit(work.State, work.ErrorCode, work.Attempts, work.AttemptLimit) ?? throw new QuoteOperationException(409, "job-not-retryable");
                var current = await db.Set<UnderwritingCycle>().SingleAsync(x => x.Id == cycle.Id, ct);
                if (current.State != "failed" || current.CurrentRatingId is not null) throw new QuoteOperationException(409, "job-not-retryable");
                var now = time.GetUtcNow(); current.State = "rating-pending"; current.UpdatedAt = now;
                work.State = "pending"; work.AttemptLimit = expanded; work.NextAttemptAt = now; work.CompletedAt = null; work.ErrorCode = null; work.LeaseToken = null; work.LeaseExpiresAt = null;
                await db.SaveChangesAsync(ct);
                return new CommandOutcome(work.Id, 202, JsonSerializer.Serialize(new { id = work.Id, kind = work.Kind, state = work.State, attempts = work.Attempts,
                    nextAttemptAt = work.NextAttemptAt, attemptLimit = work.AttemptLimit, retryAllowed = false }), Etag: "\"" + Convert.ToBase64String(work.RowVersion) + "\"");
            }, token);
    }
}
