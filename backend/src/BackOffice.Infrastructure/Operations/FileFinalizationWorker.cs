using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

// Local bounded disk work uses a held SQL file/work lock instead of a remote
// provider lease. Losing the SQL transaction rolls back publication; a rename
// may already exist and is verified on the next durable pending-work attempt.
public sealed class FileFinalizationWorker(IDbContextFactory<BackOfficeDbContext> factory, IOperationalFileStore store, TimeProvider time)
{
    public async Task<bool> Process(Guid workId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var hint = await db.Set<FileObject>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == workId && x.StorageKind == "local", token);
        if (hint is null) return false;
        var user = await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == hint.CreatedBy, token)
            ?? throw new OperationalAccessException(403, "file-originator-unavailable");
        var roles = await (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId == user.Id select role.Code).ToListAsync(token);
        var actor = new ActorContext(user.Id, user.TeamId, user.AgencyId, roles.ToHashSet(StringComparer.Ordinal));
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var row = await FileService.HoldFile(db, actor, hint.Id, "document-upload", token);
        var work = await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={workId}").SingleAsync(token);
        if (row.WorkId != work.Id || work.Kind != "file-finalization" || work.SubjectRecordId != row.SubjectId || work.CreatedBy != row.CreatedBy || work.OperationKey != $"file/{row.Id:N}")
            throw new OperationalFileStoreException("file-work-mismatch");
        using (var payload = JsonDocument.Parse(work.Payload))
            if (!payload.RootElement.TryGetProperty("fileId", out var id) || !id.TryGetGuid(out var parsed) || parsed != row.Id)
                throw new OperationalFileStoreException("file-work-mismatch");
        var now = time.GetUtcNow();
        if (row.State != "pending" || work.State != "pending" || work.NextAttemptAt > now) { await transaction.CommitAsync(token); return false; }
        if (work.Attempts >= work.AttemptLimit) throw new OperationalFileStoreException("file-retry-budget-invalid");
        work.Attempts++;
        var attempt = new AdapterAttempt { WorkId = work.Id, AttemptNumber = work.Attempts, StartedAt = now, CreatedBy = row.CreatedBy,
            Request = JsonSerializer.Serialize(new { kind = work.Kind, operationKey = work.OperationKey }) };
        db.Add(attempt);
        try
        {
            await store.Finalize(row.Id, row.ByteLength, row.Sha256, token);
            now = time.GetUtcNow(); row.State = "ready"; row.VerifiedAt = now;
            work.State = "succeeded"; work.CompletedAt = now; work.ErrorCode = null; work.Result = "{\"state\":\"ready\"}";
            attempt.EndedAt = now; attempt.Outcome = "succeeded";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            now = time.GetUtcNow(); var code = error is OperationalFileStoreException stored ? stored.Code : "file-storage-unavailable";
            var terminal = code is "file-content-mismatch" or "file-path-invalid" or "file-metadata-invalid" || work.Attempts >= work.AttemptLimit;
            work.ErrorCode = code; attempt.EndedAt = now; attempt.ErrorCode = code; attempt.Outcome = terminal ? "rejected" : "transient-failure";
            if (terminal)
            {
                row.State = "quarantined"; row.FailureCode = code; work.State = "failed"; work.CompletedAt = now;
                db.Add(new JobException { WorkId = work.Id, Code = code, OccurredAt = now });
            }
            else work.NextAttemptAt = now.AddMinutes(1);
        }
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return true;
    }

    public async Task<IReadOnlyList<Guid>> Pending(CancellationToken token, int offset = 0)
    {
        await using var db = await factory.CreateDbContextAsync(token); var now = time.GetUtcNow();
        return await db.Set<OutboxWork>().AsNoTracking().Where(x => x.Kind == "file-finalization" && x.State == "pending" && x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.Id).Select(x => x.Id).Skip(offset).Take(32).ToArrayAsync(token);
    }
}
