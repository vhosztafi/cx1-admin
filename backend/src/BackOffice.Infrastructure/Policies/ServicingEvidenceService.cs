using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingEvidenceService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory,time);

    public Task<CommandOutcome> UploadAsync(ActorContext actor, Guid draftId, byte[] version, Guid leaseToken,
        string fileName, string contentType, byte[] content, string key, Guid correlation, CancellationToken token = default)
    {
        if (version.Length != 8 || leaseToken == Guid.Empty) throw new QuoteOperationException(400,"servicing-evidence-input-invalid");
        var file = QuoteEvidenceRules.File(fileName,contentType,content,maximumNameLength:200);
        ServicingDecisionContext? held=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/evidence/uploads",key,correlation),
            new { draftId, version=Convert.ToBase64String(version), leaseToken, file.FileName, file.ContentType, file.Sha256, length=file.Content.Length },
            "servicing.evidence-file-uploaded",
            async (db,ct) => { held=await ServicingDecisionContext.Hold(db,actor,draftId,"underwriting-evidence-write",time.GetUtcNow(),ct); },
            async (db,ct) =>
            {
                var now=time.GetUtcNow();
                await held!.Current(db,factory,time,version,leaseToken,ct);
                var row=new ServicingEvidenceFile { DraftId=draftId, FileName=file.FileName, ContentType=file.ContentType,
                    Content=file.Content, ByteLength=file.Content.Length, Sha256=file.Sha256, CreatedBy=held.Scope.Source.Scope.Actor.UserId, CreatedAt=now };
                db.Add(row);
                return await held.Receipt(db,row.Id,201,now,ct);
            },token);
    }

    public async Task<ServicingEvidenceFile> DownloadAsync(ActorContext actor, Guid draftId, Guid fileId, CancellationToken token = default)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        await using var tx=await db.Database.BeginTransactionAsync(token);
        // Historical file access needs current policy read permission, not a
        // current price or an editing lease. File IDs alone grant no access.
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var file=await db.Set<ServicingEvidenceFile>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==fileId && x.DraftId==draftId && x.ScreeningState=="accepted",token)
            ?? throw new QuoteOperationException(404,"servicing-evidence-file-not-found");
        await tx.CommitAsync(token);return file;
    }
}
