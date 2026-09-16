using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteLookupCandidate(Guid Id, string Label, JsonElement Patch);
public sealed record QuoteLookupOutcome(Guid OperationId, string State, string Source, DateTimeOffset AsOf, QuoteLookupCandidate[] Candidates);
public sealed class QuoteLookupProviderException(JobFailure failure) : Exception("The demo lookup did not complete.")
{ public JobFailure Failure { get; } = failure; }

// No network calls. Provider completion commits independently of local application.
public sealed class QuoteLookupWorker(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<QuoteLookupOutcome> ExecuteProviderAsync(JobLease lease, CancellationToken token = default)
    {
        if (lease.Kind != QuoteLookupService.WorkKind) throw new QuoteLookupProviderException(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var lookup = await db.Set<QuoteLookup>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token)
            ?? throw new QuoteLookupProviderException(JobFailure.InvalidPayload);
        if (lookup.ScenarioVersionId != lease.ScenarioVersionId || lease.OperationKey != $"quote-lookup/{lookup.Id:N}")
            throw new QuoteLookupProviderException(JobFailure.ProviderConflict);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var operation = await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed = operation is not null;
        if (operation is not null && (operation.ScenarioVersionId != lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, lookup.RequestHash)))
            throw new QuoteLookupProviderException(JobFailure.ProviderConflict);
        if (operation?.Result is not null)
        {
            var previous = JsonSerializer.Deserialize<QuoteLookupOutcome>(operation.Result, Json)!;
            await transaction.CommitAsync(token); return previous;
        }
        if (operation is null)
        {
            operation = new DemoProviderOperation { Kind = lease.Kind, OperationKey = lease.OperationKey,
                RequestHash = lookup.RequestHash, ScenarioVersionId = lease.ScenarioVersionId, CreatedAt = time.GetUtcNow() };
            db.Add(operation);
        }
        if (!existed && lookup.Scenario == "fail-once")
        {
            operation.State = "transient-failed"; await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            throw new QuoteLookupProviderException(JobFailure.ProviderUnavailable);
        }
        var state = lookup.Scenario == "reject" ? "rejected" : lookup.Scenario == "no-match" ? "no-match" : "succeeded";
        var candidates = state == "succeeded" ? Enumerable.Range(1, lookup.Scenario == "multiple" ? 2 : 1).Select(index => Candidate(lookup, index)).ToArray() : [];
        var outcome = new QuoteLookupOutcome(operation.Id, state, "deterministic-demo-v1", time.GetUtcNow(), candidates);
        operation.State = state == "rejected" ? "rejected" : "succeeded"; operation.CompletedAt = outcome.AsOf;
        operation.Result = JsonSerializer.Serialize(outcome, Json);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        if (!existed && lookup.Scenario == "timeout-after-success") throw new QuoteLookupProviderException(JobFailure.ProviderTimeout);
        return outcome;
    }

    public async Task<bool> ApplyAsync(JobLease lease, QuoteLookupOutcome outcome, CancellationToken token = default)
    {
        if (lease.Kind != QuoteLookupService.WorkKind) throw new QuoteLookupProviderException(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var now = time.GetUtcNow(); var work = await SqlJobLeases.OwnedAsync(db, lease, now, token);
        if (work is null) return false;
        if (work.Kind != lease.Kind || work.OperationKey != lease.OperationKey || work.ScenarioVersionId != lease.ScenarioVersionId)
            throw new QuoteLookupProviderException(JobFailure.ProviderConflict);
        var lookup = await db.Set<QuoteLookup>().SingleAsync(x => x.WorkId == work.Id, token);
        var operation = await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == outcome.OperationId && x.Kind == work.Kind && x.OperationKey == work.OperationKey, token);
        var serialized = JsonSerializer.Serialize(outcome, Json);
        if (operation?.Result != serialized || operation.ScenarioVersionId != lookup.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, lookup.RequestHash))
            throw new QuoteLookupProviderException(JobFailure.ProviderConflict);
        if (lookup.State != "pending") throw new QuoteLookupProviderException(JobFailure.ProviderConflict);
        lookup.State = outcome.State; lookup.ResultJson = serialized; lookup.CompletedAt = now;
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id && x.AttemptNumber == lease.Attempt, token);
        attempt.EndedAt = now; attempt.Outcome = outcome.State == "rejected" ? "rejected" : "succeeded";
        // Generic operations carry opaque identities only, never addresses/licence details/candidates.
        attempt.Response = JsonSerializer.Serialize(new { lookupId = lookup.Id });
        if (outcome.State == "rejected") await SqlJobLeases.MarkTerminalAsync(db, work, "provider-rejected", now, token);
        else { work.State = "succeeded"; work.CompletedAt = now; work.LeaseToken = null; work.LeaseExpiresAt = null; work.ErrorCode = null; }
        work.Result = JsonSerializer.Serialize(new { lookupId = lookup.Id });
        db.Add(new AuditEvent { ActorId = lookup.CreatedBy, CreatedBy = lookup.CreatedBy, EventType = "quote.lookup-completed", OccurredAt = now,
            CorrelationId = work.CorrelationId, After = JsonSerializer.Serialize(new { lookupId = lookup.Id }) });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return true;
    }

    private static QuoteLookupCandidate Candidate(QuoteLookup lookup, int index)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"quote-lookup-demo-1/{lookup.Id:N}/{index}"));
        var id = new Guid(bytes.AsSpan(0, 16));
        object patch = lookup.Kind switch
        {
            "address" => new { address = new { houseNumber = index.ToString(System.Globalization.CultureInfo.InvariantCulture), street = "Fictional Demo Street", town = "Demo Town", county = "Demo County", postcode = lookup.Query.Length > 3 ? lookup.Query.Insert(lookup.Query.Length - 3, " ") : lookup.Query } },
            "vehicle" => new { make = "Demo Motors", model = $"Demonstrator {index}" },
            _ => new { }
        };
        return new(id, $"Fictional {lookup.Kind} match {index} (demo)", JsonSerializer.SerializeToElement(patch, Json));
    }
}
