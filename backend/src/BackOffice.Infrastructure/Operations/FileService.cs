using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public enum FileCommandFault { BeforeMetadataCommit, AfterMetadataCommit }
public sealed record OperationalFileDownload(Stream Content, string Name, string MediaType, long ByteLength, string Sha256) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed partial class FileService(IDbContextFactory<BackOfficeDbContext> factory, SqlCommandBoundary commands,
    IOperationalFileStore store, TimeProvider time, Action<FileCommandFault>? fault = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CommandOutcome> Upload(ActorContext actor, Guid subjectId, string name, string mediaType,
        Stream content, string key, CancellationToken token)
    {
        // Reject revoked/foreign actors before consuming an untrusted body.
        await using (var db = await factory.CreateDbContextAsync(token))
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await OperationalScope.HoldSubjects(db, actor, [subjectId], "document-upload", token);
            await transaction.CommitAsync(token);
        }
        var staged = await store.Stage(name, mediaType, content, FileRules.MaximumFileBytes, token);
        // Staging is outside the retryable SQL command. A rollback or replay can
        // leave an orphan, never a SQL row falsely claiming downloadable bytes.
        var outcome = await commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/records/{subjectId}/file-uploads", key, Guid.NewGuid()),
            new { subjectId, name = staged.Description.Name, mediaType = staged.Description.MediaType, length = staged.Description.ByteLength, staged.Sha256 }, "file.upload-staged",
            async (db, ct) => { await OperationalScope.HoldSubjects(db, actor, [subjectId], "document-upload", ct); },
            async (db, ct) =>
            {
                await Lock(db, $"CoverMGA.File.{staged.Id:N}", ct);
                var now = time.GetUtcNow();
                var work = new OutboxWork { Kind = "file-finalization", SubjectRecordId = subjectId, OperationKey = $"file/{staged.Id:N}",
                    Payload = JsonSerializer.Serialize(new { fileId = staged.Id }, Json), CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now, NextAttemptAt = now };
                var row = new FileObject { Id = staged.Id, SubjectId = subjectId, WorkId = work.Id, FileName = staged.Description.Name,
                    MediaType = staged.Description.MediaType, ByteLength = staged.Description.ByteLength, Sha256 = staged.Sha256,
                    CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.AddRange(work, row); await db.SaveChangesAsync(ct);
                fault?.Invoke(FileCommandFault.BeforeMetadataCommit);
                // The upload ID is the separate work identity. Storage IDs and
                // generated disk names are never returned in the public DTO.
                return UploadOutcome(row, 202);
            }, token);
        fault?.Invoke(FileCommandFault.AfterMetadataCommit);
        return outcome;
    }

    public async Task<CommandOutcome> ReadUpload(ActorContext actor, Guid uploadId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var row = await HoldUpload(db, actor, uploadId, "document-read", token);
        var result = UploadOutcome(row, 200); await transaction.CommitAsync(token); return result;
    }

    public Task<OperationalFileDownload> DownloadUpload(ActorContext actor, Guid uploadId, CancellationToken token) => Download(actor, uploadId, true, token);

    private async Task<OperationalFileDownload> Download(ActorContext actor, Guid id, bool upload, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var row = upload ? await HoldUpload(db, actor, id, "document-download", token) : await HoldFile(db, actor, id, "document-download", token);
        OperationalFileDownload result;
        try { result = await Open(db, row, token); }
        catch (OperationalFileStoreException error) when (error.Code is "file-content-mismatch" or "file-path-invalid" or "file-bytes-missing")
        {
            row.State = "quarantined"; row.FailureCode = error.Code;
            db.Add(new AuditEvent { ActorId = actor.UserId, CreatedBy = actor.UserId, SubjectRecordId = row.SubjectId, EventType = "file.integrity-rejected", OccurredAt = time.GetUtcNow(),
                CorrelationId = Guid.NewGuid(), After = JsonSerializer.Serialize(new { fileId = row.Id, code = error.Code }, Json) });
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token); throw;
        }
        try { await transaction.CommitAsync(token); return result; }
        catch { await result.DisposeAsync(); throw; }
    }

    // Document owners compose these internal identities after authorizing their
    // document/version and audience; the original file parent is checked too.
    internal static async Task<FileObject> HoldFile(BackOfficeDbContext db, ActorContext actor, Guid fileId, string capability, CancellationToken token)
    {
        var hint = await db.Set<FileObject>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId, token) ?? throw Missing();
        await OperationalScope.HoldSubjects(db, actor, [hint.SubjectId], capability, token);
        var row = await db.Set<FileObject>().FromSqlInterpolated($"SELECT * FROM FileObject WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={fileId}").SingleOrDefaultAsync(token) ?? throw Missing();
        if (row.SubjectId != hint.SubjectId) throw Missing();
        return row;
    }

    private static async Task<FileObject> HoldUpload(BackOfficeDbContext db, ActorContext actor, Guid uploadId, string capability, CancellationToken token)
    {
        var id = await db.Set<FileObject>().Where(x => x.WorkId == uploadId && x.StorageKind == "local").Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token) ?? throw Missing();
        var row = await HoldFile(db, actor, id, capability, token);
        if (row.WorkId != uploadId) throw Missing(); return row;
    }

    internal async Task<OperationalFileDownload> Open(BackOfficeDbContext db, FileObject row, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("File reads require current held authorization.");
        if (row.State != "ready") throw new OperationalAccessException(409, "file-not-ready");
        Stream content;
        if (row.StorageKind == "local") content = await store.OpenReady(row.Id, row.ByteLength, row.Sha256, token);
        else
        {
            var bytes = await LegacyBytes(db, row, token);
            if (bytes.LongLength != row.ByteLength || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(row.Sha256)))
                throw new OperationalFileStoreException("file-content-mismatch");
            content = new MemoryStream(bytes, writable: false);
        }
        return new(content, row.FileName, row.MediaType, row.ByteLength, row.Sha256);
    }

    private static Task<byte[]> LegacyBytes(BackOfficeDbContext db, FileObject row, CancellationToken token) => row.StorageKind switch
    {
        "agency-evidence" => db.Set<AgencyEvidenceFile>().Where(x => x.Id == row.AgencyEvidenceFileId && x.ScreeningState == "demo-cleared").Select(x => x.Content).SingleAsync(token),
        "quote-evidence" => db.Set<QuoteEvidenceFile>().Where(x => x.Id == row.QuoteEvidenceFileId && x.ScreeningState == "accepted").Select(x => x.Content).SingleAsync(token),
        "servicing-evidence" => db.Set<ServicingEvidenceFile>().Where(x => x.Id == row.ServicingEvidenceFileId && x.ScreeningState == "accepted").Select(x => x.Content).SingleAsync(token),
        _ => throw new OperationalFileStoreException("file-storage-invalid")
    };

    private static CommandOutcome UploadOutcome(FileObject row, int status) => new(row.WorkId!.Value, status, JsonSerializer.Serialize(new
    {
        id = row.WorkId.Value, subjectRecordId = row.SubjectId, name = row.FileName, mediaType = row.MediaType,
        byteLength = row.ByteLength, sha256 = row.Sha256, state = row.State, createdAt = row.CreatedAt,
        verifiedAt = row.VerifiedAt, failureCode = row.FailureCode
    }, Json));
    private static OperationalAccessException Missing() => new(404, "file-upload-not-found");
}
