using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class FileService
{
    // Internal bridge for document/evidence owners; no copying or mutation of
    // the retained original bytes, IDs, reviews or attestations.
    public async Task<Guid> BridgeLegacy(ActorContext actor, Guid subjectId, string kind, Guid evidenceFileId, string key, CancellationToken token)
    {
        LegacyFile? source = null;
        var result = await commands.ExecuteAuthorizedAsync(new(actor.UserId, "/internal/files/legacy-bridge", key, Guid.NewGuid()),
            new { subjectId, kind, evidenceFileId }, "file.legacy-linked",
            async (db, ct) =>
            {
                var held = await OperationalScope.HoldSubjects(db, actor, [subjectId], "document-upload", ct);
                source = await ReadLegacy(db, kind, evidenceFileId, ct);
                if (OperationalScope.Parent(held.Subjects.Single()) != source.Parent) throw new OperationalAccessException(404, "evidence-file-not-found");
                if (source.Bytes.LongLength != source.Length || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(source.Bytes), Convert.FromHexString(source.Hash)))
                    throw new OperationalFileStoreException("file-content-mismatch");
            }, async (db, ct) =>
            {
                await Lock(db, $"CoverMGA.LegacyFile.{kind}.{evidenceFileId:N}", ct);
                var existing = await db.Set<FileObject>().SingleOrDefaultAsync(x => x.StorageKind == kind && (x.AgencyEvidenceFileId == evidenceFileId || x.QuoteEvidenceFileId == evidenceFileId || x.ServicingEvidenceFileId == evidenceFileId), ct);
                if (existing is not null)
                {
                    if (existing.SubjectId != subjectId || existing.StorageKind != kind) throw new OperationalAccessException(409, "evidence-bridge-conflict");
                    return new(existing.Id, 200, JsonSerializer.Serialize(new { id = existing.Id }, Json));
                }
                var now = time.GetUtcNow();
                var row = new FileObject { SubjectId = subjectId, StorageKind = kind, FileName = source!.Name, MediaType = source.Type, ByteLength = source.Length, Sha256 = source.Hash,
                    State = "ready", VerifiedAt = now, CreatedAt = now, UpdatedAt = now, CreatedBy = actor.UserId,
                    AgencyEvidenceFileId = kind == "agency-evidence" ? evidenceFileId : null,
                    QuoteEvidenceFileId = kind == "quote-evidence" ? evidenceFileId : null,
                    ServicingEvidenceFileId = kind == "servicing-evidence" ? evidenceFileId : null };
                db.Add(row); await db.SaveChangesAsync(ct); return new(row.Id, 201, JsonSerializer.Serialize(new { id = row.Id }, Json));
            }, token);
        return result.ResourceId;
    }

    public Task<OperationalFileDownload> DownloadFile(ActorContext actor, Guid fileId, CancellationToken token) => Download(actor, fileId, false, token);

    private static async Task<LegacyFile> ReadLegacy(BackOfficeDbContext db, string kind, Guid id, CancellationToken token)
    {
        switch (kind)
        {
            case "agency-evidence":
                var agency = await db.Set<AgencyEvidenceFile>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScreeningState == "demo-cleared", token) ?? throw Missing();
                return new(new("agency", agency.AgencyId), agency.FileName, agency.ContentType, agency.ByteLength, agency.Sha256, agency.Content);
            case "quote-evidence":
                var quote = await db.Set<QuoteEvidenceFile>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScreeningState == "accepted", token) ?? throw Missing();
                return new(new("quote", quote.QuoteId), quote.FileName, quote.ContentType, quote.ByteLength, quote.Sha256, quote.Content);
            case "servicing-evidence":
                var servicing = await db.Set<ServicingEvidenceFile>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScreeningState == "accepted", token) ?? throw Missing();
                return new(new("servicing-draft", servicing.DraftId), servicing.FileName, servicing.ContentType, servicing.ByteLength, servicing.Sha256, servicing.Content);
            default: throw new OperationalAccessException(422, "unsupported-evidence-source");
        }
    }

    internal static async Task Lock(BackOfficeDbContext db, string resource, CancellationToken token)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = resource; command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) < 0) throw new CommandBusyException();
    }
    private sealed record LegacyFile(OperationalParent Parent, string Name, string Type, long Length, string Hash, byte[] Bytes);
}
