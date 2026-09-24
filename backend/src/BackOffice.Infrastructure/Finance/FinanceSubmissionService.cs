using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record FinanceSubmissionAttemptView(int Attempt, string Outcome, string? ErrorCode,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt);
public sealed record FinanceSubmissionView(Guid Id, Guid BatchId, Guid VersionId, string ContentHash,
    Guid WorkId, string State, string? ProviderState, Guid? ProviderOperationId, string? ProviderEventId,
    DateTimeOffset? AppliedAt, IReadOnlyList<FinanceSubmissionAttemptView> Attempts);

public sealed class FinanceSubmissionService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private const string ScenarioScope = "finance-bordereau-submission-demo";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CommandOutcome> QueueAsync(ActorContext actor, Guid batchId, Guid versionId,
        string contentHash, string key, Guid correlationId, CancellationToken token = default)
    {
        if (batchId == Guid.Empty || versionId == Guid.Empty || contentHash is not { Length: 64 } ||
            contentHash.Any(c => !Uri.IsHexDigit(c)))
            throw new QuoteOperationException(400, "bordereau-submission-input-invalid");
        contentHash = contentHash.ToUpperInvariant();
        var expectedHash = Convert.FromHexString(contentHash);
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/bordereaux/{batchId:N}/versions/{versionId:N}/submissions", key, correlationId),
            new { batchId, versionId, contentHash }, "finance.bordereau.submission-queued",
            async (db, ct) =>
            {
                var providerId = await db.Set<FinanceBordereauBatch>().AsNoTracking().Where(x => x.Id == batchId)
                    .Select(x => (Guid?)x.ProviderId).SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "bordereau-not-found");
                await FinanceBordereauService.Authorize(db, actor, providerId, ct, true);
                var batch = await db.Set<FinanceBordereauBatch>()
                    .FromSqlInterpolated($"SELECT * FROM FinanceBordereauBatch WITH(UPDLOCK,HOLDLOCK) WHERE Id={batchId}")
                    .AsNoTracking().SingleAsync(ct);
                var previous = await db.Set<FinanceBordereauSubmission>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.BatchId == batchId, ct);
                if (batch.CurrentVersionId != versionId && previous?.VersionId != versionId)
                    throw new QuoteOperationException(412, "bordereau-version-stale");
                var version = await db.Set<FinanceBordereauVersion>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == versionId && x.BatchId == batchId, ct)
                    ?? throw new QuoteOperationException(404, "bordereau-version-not-found");
                if (version.State != "valid" || version.ContentHash is null || version.ContentBytes is null ||
                    !CryptographicOperations.FixedTimeEquals(version.ContentHash, expectedHash) ||
                    !CryptographicOperations.FixedTimeEquals(SHA256.HashData(version.ContentBytes), expectedHash) ||
                    (JsonSerializer.Deserialize<BordereauValidationIssue[]>(version.ValidationJson, Json)?.Length ?? -1) != 0)
                    throw new QuoteOperationException(409, "batch-invalid");
                if (previous is not null && (!CryptographicOperations.FixedTimeEquals(previous.ContentHash, expectedHash) ||
                    previous.VersionId != versionId)) throw new QuoteOperationException(409, "batch-already-submitted");
            },
            async (db, ct) =>
            {
                if (await db.Set<FinanceBordereauSubmission>().AnyAsync(x => x.BatchId == batchId, ct))
                    throw new QuoteOperationException(409, "batch-already-submitted");
                var scenario = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == ScenarioScope &&
                    x.EffectiveFrom <= time.GetUtcNow()).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("Demo bordereau scenario is unavailable.");
                _ = FinanceSubmissionWorker.Scenario(scenario);
                var operationKey = $"finance-bordereau/{batchId:N}";
                var requestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                    { batchId, versionId, contentHash, scenarioVersionId = scenario.Id }, Json));
                var now = time.GetUtcNow();
                var submission = new FinanceBordereauSubmission { BatchId = batchId, VersionId = versionId,
                    ContentHash = expectedHash, RequestHash = requestHash, ScenarioVersionId = scenario.Id,
                    OperationKey = operationKey, State = "queued", CreatedAt = now, UpdatedAt = now,
                    CreatedBy = actor.UserId };
                var work = new OutboxWork { Kind = FinanceSubmissionWorker.WorkKind, SubjectRecordId = submission.Id,
                    OperationKey = operationKey, Payload = JsonSerializer.Serialize(new
                        { submissionId = submission.Id, batchId, versionId, contentHash }, Json),
                    ScenarioVersionId = scenario.Id, NextAttemptAt = now, CreatedAt = now, UpdatedAt = now,
                    CreatedBy = actor.UserId, CorrelationId = correlationId };
                submission.WorkId = work.Id;
                db.Add(work); db.Add(submission);
                await db.SaveChangesAsync(ct);
                return new CommandOutcome(submission.Id, 202, JsonSerializer.Serialize(new
                    { submissionId = submission.Id, batchId, versionId, state = submission.State }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public async Task<FinanceSubmissionView> DetailAsync(ActorContext actor, Guid batchId, Guid versionId,
        CancellationToken token = default)
    {
        if (batchId == Guid.Empty || versionId == Guid.Empty)
            throw new QuoteOperationException(400, "bordereau-submission-input-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var providerId = await db.Set<FinanceBordereauBatch>().AsNoTracking().Where(x => x.Id == batchId)
            .Select(x => (Guid?)x.ProviderId).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "bordereau-not-found");
        await FinanceBordereauService.Authorize(db, actor, providerId, token);
        var submission = await db.Set<FinanceBordereauSubmission>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.BatchId == batchId && x.VersionId == versionId, token)
            ?? throw new QuoteOperationException(404, "bordereau-submission-not-found");
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == submission.WorkId, token);
        var attempts = await db.Set<AdapterAttempt>().AsNoTracking().Where(x => x.WorkId == work.Id)
            .OrderBy(x => x.AttemptNumber).Select(x => new FinanceSubmissionAttemptView(x.AttemptNumber,
                x.Outcome, x.ErrorCode, x.StartedAt, x.EndedAt)).ToArrayAsync(token);
        await tx.CommitAsync(token);
        return new FinanceSubmissionView(submission.Id, batchId, versionId,
            Convert.ToHexString(submission.ContentHash), work.Id,
            submission.State == "queued" && work.ErrorCode == "provider-timeout" ? "uncertain" : submission.State,
            submission.ProviderState, submission.ProviderOperationId, submission.ProviderEventId,
            submission.AppliedAt, attempts);
    }
}
