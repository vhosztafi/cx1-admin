using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingFileView(Guid Id,string FileName,string ContentType,int ByteLength,string ScreeningState,string ScreeningMethod,DateTimeOffset CreatedAt);
public sealed record ServicingFilePage(IReadOnlyList<ServicingFileView> Items,Guid? NextBeforeId);
public sealed record ServicingAssociationView(Guid Id,Guid CycleId,Guid RevisionId,Guid RatingId,Guid FileId,string FileName,string Code,Guid? RiskItemId,
    string InputFingerprint,string Reason,string Etag,Guid? LatestReviewId,string? ReviewOutcome,bool Withdrawn,DateTimeOffset CreatedAt,Guid? CapacitySubmissionId=null);
public sealed record ServicingAssociationPage(IReadOnlyList<ServicingAssociationView> Items,Guid? NextBeforeId);
public sealed record ServicingReviewView(Guid Id,int Sequence,string Kind,string? Outcome,string Reason,Guid ActorId,Guid? AuthorityVersionId,DateTimeOffset RecordedAt);
public sealed record ServicingReviewPage(IReadOnlyList<ServicingReviewView> Items,int? NextAfterSequence);

public sealed partial class ServicingEvidenceService
{
    public async Task<string> HistoryVersionAsync(ActorContext actor,Guid draftId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var draft=await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var result="\""+Convert.ToBase64String(draft.RowVersion)+"\"";await tx.CommitAsync(token);return result;
    }
    public async Task<ServicingFilePage> FilesAsync(ActorContext actor,Guid draftId,Guid? beforeId=null,int pageSize=50,CancellationToken token=default)
    {
        Page(beforeId,pageSize);
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var query=db.Set<ServicingEvidenceFile>().AsNoTracking().Where(x=>x.DraftId==draftId);
        if(beforeId is {} id)
        {
            var cursor=await query.Where(x=>x.Id==id).Select(x=>new {x.Id,x.CreatedAt}).SingleOrDefaultAsync(token)
                ??throw new QuoteOperationException(404,"servicing-evidence-cursor-not-found");
            query=query.Where(x=>x.CreatedAt<cursor.CreatedAt || x.CreatedAt==cursor.CreatedAt && x.Id.CompareTo(cursor.Id)<0);
        }
        // Never load file bytes for a history page.
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(pageSize+1)
            .Select(x=>new ServicingFileView(x.Id,x.FileName,x.ContentType,x.ByteLength,x.ScreeningState,x.ScreeningMethod,x.CreatedAt)).ToArrayAsync(token);
        var items=rows.Take(pageSize).ToArray();await tx.CommitAsync(token);
        return new(items,rows.Length>pageSize?items[^1].Id:null);
    }

    public async Task<ServicingAssociationPage> AssociationsAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid? beforeId=null,int pageSize=50,CancellationToken token=default)
    {
        Page(beforeId,pageSize);
        if(cycleId==Guid.Empty) throw new QuoteOperationException(422,"servicing-evidence-page-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        if(!await db.Set<ServicingCycle>().AnyAsync(x=>x.Id==cycleId && x.DraftId==draftId,token)) throw new QuoteOperationException(404,"servicing-cycle-not-found");
        var query=db.Set<ServicingEvidenceAssociation>().AsNoTracking().Where(x=>x.DraftId==draftId && x.CycleId==cycleId);
        if(beforeId is {} id)
        {
            var cursor=await query.Where(x=>x.Id==id).Select(x=>new {x.Id,x.CreatedAt}).SingleOrDefaultAsync(token)
                ??throw new QuoteOperationException(404,"servicing-evidence-cursor-not-found");
            query=query.Where(x=>x.CreatedAt<cursor.CreatedAt || x.CreatedAt==cursor.CreatedAt && x.Id.CompareTo(cursor.Id)<0);
        }
        var selected=query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(pageSize+1);
        var rows=await (from a in selected join f in db.Set<ServicingEvidenceFile>() on a.FileId equals f.Id
            join e in db.Set<ServicingEvidenceEvent>() on a.LatestReviewId equals e.Id into reviews from e in reviews.DefaultIfEmpty()
            orderby a.CreatedAt descending,a.Id descending
            select new {a.Id,a.CycleId,a.RevisionId,a.RatingId,a.FileId,f.FileName,a.RequirementCode,a.RiskItemId,a.CapacitySubmissionId,a.InputFingerprint,a.Reason,a.RowVersion,
                a.LatestReviewId,ReviewOutcome=e==null?null:e.Outcome,Withdrawn=a.WithdrawnEventId!=null,a.CreatedAt}).ToArrayAsync(token);
        var items=rows.Take(pageSize).Select(a=>new ServicingAssociationView(a.Id,a.CycleId,a.RevisionId,a.RatingId,a.FileId,a.FileName,a.RequirementCode,a.RiskItemId,
            a.InputFingerprint,a.Reason,"\""+Convert.ToBase64String(a.RowVersion)+"\"",a.LatestReviewId,a.ReviewOutcome,a.Withdrawn,a.CreatedAt,a.CapacitySubmissionId)).ToArray();
        await tx.CommitAsync(token);return new(items,rows.Length>pageSize?items[^1].Id:null);
    }

    public async Task<ServicingReviewPage> ReviewsAsync(ActorContext actor,Guid draftId,Guid associationId,int afterSequence=0,int pageSize=50,CancellationToken token=default)
    {
        Page(null,pageSize);
        if(afterSequence<0 || associationId==Guid.Empty) throw new QuoteOperationException(422,"servicing-evidence-page-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        if(!await db.Set<ServicingEvidenceAssociation>().AnyAsync(x=>x.Id==associationId && x.DraftId==draftId,token)) throw new QuoteOperationException(404,"servicing-evidence-not-found");
        var rows=await db.Set<ServicingEvidenceEvent>().AsNoTracking().Where(x=>x.DraftId==draftId && x.AssociationId==associationId && x.Sequence>afterSequence)
            .OrderBy(x=>x.Sequence).Take(pageSize+1).Select(x=>new ServicingReviewView(x.Id,x.Sequence,x.Kind,x.Outcome,x.Reason,x.ActorId,x.AuthorityVersionId,x.RecordedAt)).ToArrayAsync(token);
        var items=rows.Take(pageSize).ToArray();await tx.CommitAsync(token);return new(items,rows.Length>pageSize?items[^1].Sequence:null);
    }

    private static void Page(Guid? beforeId,int pageSize)
    {
        if(beforeId==Guid.Empty || pageSize is <1 or >50) throw new QuoteOperationException(422,"servicing-evidence-page-invalid");
    }
}
