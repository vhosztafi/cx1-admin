using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record FinanceSubmissionProviderOutcome(Guid OperationId, string EventId, Guid SubmissionId,
    Guid VersionId, string ContentHash, string State, string? ProviderReference, DateTimeOffset CompletedAt);
public sealed class FinanceSubmissionWorkerException(JobFailure failure) : Exception("Demo insurer operation did not complete.")
{
    public JobFailure Failure { get; } = failure;
}

public sealed class FinanceSubmissionWorker(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public const string WorkKind = "finance-bordereau-submit";
    private const string InboxProvider = "finance-bordereau-demo";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string Scenario(SettingVersion setting)
    {
        if (setting.Scope != "finance-bordereau-submission-demo") throw Failure(JobFailure.InvalidPayload);
        try
        {
            using var document = JsonDocument.Parse(setting.Values);
            var value = document.RootElement.GetProperty("scenario").GetString();
            if (value is "success" or "reject" or "fail-once" or "timeout-after-success") return value;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException) { }
        throw Failure(JobFailure.InvalidPayload);
    }

    public async Task<FinanceSubmissionProviderOutcome?> ExecuteProviderAsync(JobLease lease,
        CancellationToken token = default)
    {
        if (lease.Kind != WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var submission = await db.Set<FinanceBordereauSubmission>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        var owned = await SqlJobLeases.OwnedAsync(db, lease, time.GetUtcNow(), token);
        if (owned is null) return null;
        if (owned.OperationKey != submission.OperationKey || owned.ScenarioVersionId != submission.ScenarioVersionId)
            throw Failure(JobFailure.ProviderConflict);
        var batch = await db.Set<FinanceBordereauBatch>().AsNoTracking()
            .SingleAsync(x => x.Id == submission.BatchId, token);
        var version = await db.Set<FinanceBordereauVersion>().AsNoTracking()
            .SingleAsync(x => x.Id == submission.VersionId, token);
        var priorOperation = await db.Set<DemoProviderOperation>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Kind == WorkKind && x.OperationKey == submission.OperationKey, token);
        var recoverable = priorOperation?.Result is not null &&
            priorOperation.ScenarioVersionId == submission.ScenarioVersionId &&
            CryptographicOperations.FixedTimeEquals(priorOperation.RequestHash, submission.RequestHash);
        if ((batch.CurrentVersionId != version.Id && !recoverable) || version.BatchId != batch.Id || version.State != "valid" ||
            version.ContentBytes is null || version.ContentHash is null ||
            !CryptographicOperations.FixedTimeEquals(version.ContentHash, submission.ContentHash) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(version.ContentBytes), submission.ContentHash))
            throw Failure(JobFailure.Superseded);
        if (!recoverable)
        {
            var identity = await IdentitySnapshot.Lock(db,
                new IdentityReference(submission.CreatedBy!.Value, null), token);
            if (identity is null || identity.User.AgencyId is not null ||
                !identity.Roles.Any(x => x.Code == "finance")) throw Failure(JobFailure.Superseded);
        }
        var setting = await db.Set<SettingVersion>().AsNoTracking()
            .SingleAsync(x => x.Id == submission.ScenarioVersionId, token);
        var scenario = Scenario(setting);
        await tx.CommitAsync(token);
        return await ProviderAsync(submission, lease, scenario, token);
    }

    private async Task<FinanceSubmissionProviderOutcome> ProviderAsync(FinanceBordereauSubmission submission,
        JobLease lease, string scenario, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var previouslySaved = await db.Set<DemoProviderOperation>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Kind == WorkKind && x.OperationKey == lease.OperationKey, token);
        if (previouslySaved?.Result is null)
        {
            var identity = await IdentitySnapshot.Lock(db,
                new IdentityReference(submission.CreatedBy!.Value, null), token);
            if (identity is null || identity.User.AgencyId is not null ||
                !identity.Roles.Any(x => x.Code == "finance")) throw Failure(JobFailure.Superseded);
        }
        var batch = await db.Set<FinanceBordereauBatch>()
            .FromSqlInterpolated($"SELECT * FROM FinanceBordereauBatch WITH(UPDLOCK,HOLDLOCK) WHERE Id={submission.BatchId}")
            .AsNoTracking().SingleAsync(token);
        var operation = await db.Set<DemoProviderOperation>()
            .FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={WorkKind} AND OperationKey={lease.OperationKey}")
            .SingleOrDefaultAsync(token);
        var existed = operation is not null;
        if (operation is not null && (operation.ScenarioVersionId != submission.ScenarioVersionId ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash, submission.RequestHash)))
            throw Failure(JobFailure.ProviderConflict);
        if (operation?.Result is not null)
        {
            var saved = Read(operation.Result);
            Validate(operation, submission, saved);
            await tx.CommitAsync(token);
            return saved;
        }
        if (batch.CurrentVersionId != submission.VersionId) throw Failure(JobFailure.Superseded);
        if (operation is null)
        {
            operation = new DemoProviderOperation { Kind = WorkKind, OperationKey = lease.OperationKey,
                RequestHash = submission.RequestHash, ScenarioVersionId = submission.ScenarioVersionId,
                CreatedAt = time.GetUtcNow(), UpdatedAt = time.GetUtcNow() };
            db.Add(operation);
        }
        if (!existed && scenario == "fail-once")
        {
            operation.State = "transient-failed";
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            throw Failure(JobFailure.ProviderUnavailable);
        }
        var now = time.GetUtcNow();
        var state = scenario == "reject" ? "rejected" : "accepted";
        var outcome = new FinanceSubmissionProviderOutcome(operation.Id,
            $"finance-bordereau-demo/{operation.Id:N}", submission.Id, submission.VersionId,
            Convert.ToHexString(submission.ContentHash), state,
            state == "accepted" ? $"INS-DEMO-{submission.Id:N}" : null, now);
        operation.State = state; operation.CompletedAt = now;
        operation.Result = JsonSerializer.Serialize(outcome, Json);
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        if (!existed && scenario == "timeout-after-success") throw Failure(JobFailure.ProviderTimeout);
        return outcome;
    }

    public async Task<InboxApplication> ApplyAsync(JobLease lease, FinanceSubmissionProviderOutcome outcome,
        CancellationToken token = default)
    {
        if (lease.Kind != WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var submission = await db.Set<FinanceBordereauSubmission>().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token)
            ?? throw Failure(JobFailure.InvalidPayload);
        var work = await db.Set<OutboxWork>()
            .FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={lease.WorkId}")
            .SingleAsync(token);
        if (work.Kind != WorkKind || work.OperationKey != submission.OperationKey ||
            work.ScenarioVersionId != submission.ScenarioVersionId) throw Failure(JobFailure.ProviderConflict);
        var operation = await db.Set<DemoProviderOperation>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == outcome.OperationId, token) ?? throw Failure(JobFailure.ProviderConflict);
        if (operation.Kind != WorkKind || operation.OperationKey != submission.OperationKey ||
            operation.ScenarioVersionId != submission.ScenarioVersionId || operation.Result is null ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash, submission.RequestHash))
            throw Failure(JobFailure.ProviderConflict);
        var saved = Read(operation.Result);
        Validate(operation, submission, saved);
        if (outcome.EventId != saved.EventId || outcome.OperationId != saved.OperationId ||
            outcome.SubmissionId != submission.Id || outcome.VersionId != submission.VersionId)
            throw Failure(JobFailure.ProviderConflict);
        var result = JsonSerializer.Serialize(outcome, Json);
        var responseHash = SHA256.HashData(Encoding.UTF8.GetBytes(result));
        var inbox = await db.Set<AdapterInbox>().SingleOrDefaultAsync(x => x.Provider == InboxProvider &&
            x.EventId == outcome.EventId, token);
        if (inbox is not null)
        {
            if (inbox.WorkId != work.Id) throw Failure(JobFailure.ProviderConflict);
            if (CryptographicOperations.FixedTimeEquals(inbox.ContentHash, responseHash))
            {
                await tx.CommitAsync(token); return InboxApplication.Duplicate;
            }
            if (!await db.Set<AdapterQuarantine>().AnyAsync(x => x.InboxId == inbox.Id &&
                x.ObservedHash == responseHash, token))
                db.Add(new AdapterQuarantine { InboxId = inbox.Id, ObservedHash = responseHash,
                    ReceivedAt = time.GetUtcNow(), Reason = "Changed duplicate demo insurer outcome." });
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token); return InboxApplication.Quarantined;
        }
        if (result != operation.Result) throw Failure(JobFailure.ProviderConflict);
        var now = time.GetUtcNow();
        if (work.State != "leased" || work.LeaseToken != lease.Token || work.Attempts != lease.Attempt ||
            work.LeaseExpiresAt is null || work.LeaseExpiresAt <= now) return InboxApplication.StaleLease;
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id &&
            x.AttemptNumber == lease.Attempt, token);
        attempt.EndedAt = now; attempt.Outcome = saved.State == "accepted" ? "succeeded" : "rejected";
        attempt.Response = JsonSerializer.Serialize(new { submissionId = submission.Id,
            providerOutcome = saved.State, providerReference = saved.ProviderReference }, Json);
        submission.ProviderState = saved.State;
        submission.ProviderOperationId = operation.Id;
        submission.ProviderEventId = saved.EventId;
        submission.AppliedAt = now;
        submission.State = saved.State == "accepted" ? "submitted" : "rejected";
        if (saved.State == "accepted")
        {
            work.State = "succeeded"; work.CompletedAt = now; work.ErrorCode = null;
            work.LeaseToken = null; work.LeaseExpiresAt = null;
            work.Result = JsonSerializer.Serialize(new { submissionId = submission.Id }, Json);
        }
        else
        {
            attempt.ErrorCode = "provider-rejected";
            await SqlJobLeases.MarkTerminalAsync(db, work, "provider-rejected", now, token);
        }
        db.Add(new AdapterInbox { Provider = InboxProvider, EventId = saved.EventId,
            ContentHash = responseHash, WorkId = work.Id, State = "applied", AppliedAt = now,
            CreatedAt = now, UpdatedAt = now });
        db.Add(new AuditEvent { ActorId = submission.CreatedBy, SubjectRecordId = submission.BatchId,
            EventType = "finance.bordereau.submission-applied", OccurredAt = now,
            CorrelationId = work.CorrelationId, After = JsonSerializer.Serialize(new
                { submissionId = submission.Id, versionId = submission.VersionId, state = submission.State }, Json) });
        await db.SaveChangesAsync(token);
        await tx.CommitAsync(token);
        return InboxApplication.Applied;
    }

    private static FinanceSubmissionProviderOutcome Read(string json)
    {
        try { return JsonSerializer.Deserialize<FinanceSubmissionProviderOutcome>(json, Json) ??
            throw Failure(JobFailure.ProviderConflict); }
        catch (JsonException) { throw Failure(JobFailure.ProviderConflict); }
    }

    private static void Validate(DemoProviderOperation operation, FinanceBordereauSubmission submission,
        FinanceSubmissionProviderOutcome value)
    {
        if (value.OperationId != operation.Id || value.EventId != $"finance-bordereau-demo/{operation.Id:N}" ||
            value.SubmissionId != submission.Id || value.VersionId != submission.VersionId ||
            value.ContentHash != Convert.ToHexString(submission.ContentHash) ||
            value.CompletedAt != operation.CompletedAt || value.State is not ("accepted" or "rejected") ||
            operation.State != value.State || value.ProviderReference !=
            (value.State == "accepted" ? $"INS-DEMO-{submission.Id:N}" : null))
            throw Failure(JobFailure.ProviderConflict);
    }

    private static FinanceSubmissionWorkerException Failure(JobFailure failure) => new(failure);
}
