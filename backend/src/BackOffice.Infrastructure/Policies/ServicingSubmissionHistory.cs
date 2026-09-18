using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingSubmissionView(Guid Id,Guid CycleId,Guid RevisionId,Guid RatingId,string InputHash,
    string Reason,Guid SubmittedBy,DateTimeOffset SubmittedAt,bool Applicable);
public sealed record ServicingSubmissionPage(Guid DraftId,string DraftEtag,DateTimeOffset AssessedAt,
    Guid? CurrentCycleId,ServicingSubmissionView? Current,IReadOnlyList<ServicingSubmissionView> Items,Guid? NextBeforeId);

public sealed partial class ServicingSubmissionService
{
    public async Task<ServicingSubmissionPage> ReadAsync(ActorContext actor,Guid draftId,Guid? beforeId=null,int pageSize=25,CancellationToken token=default)
    {
        if(draftId==Guid.Empty || beforeId==Guid.Empty || pageSize is <1 or >50)
            throw new QuoteOperationException(400,"invalid-query");
        // Reuse the complete rating applicability assessment, including current
        // configuration and base version. Fence the subsequent history read.
        var rating=await new ServicingRatingReadModel(factory,time).ReadAsync(actor,draftId,pageSize:1,token:token);
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var draft=await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var etag="\""+Convert.ToBase64String(draft.RowVersion)+"\"";
        if(etag!=rating.DraftEtag)throw new QuoteOperationException(409,"servicing-version-conflict");
        var query=db.Set<ServicingUnderwritingSubmission>().AsNoTracking().Where(x=>x.DraftId==draftId);
        var current=draft.CurrentCycleId is {} cycle?await query.SingleOrDefaultAsync(x=>x.CycleId==cycle,token):null;
        if(beforeId is {} before)
        {
            var cursor=await query.Where(x=>x.Id==before).Select(x=>new{x.Id,x.SubmittedAt}).SingleOrDefaultAsync(token)
                ??throw new QuoteOperationException(404,"servicing-submission-cursor-not-found");
            query=query.Where(x=>x.SubmittedAt<cursor.SubmittedAt || x.SubmittedAt==cursor.SubmittedAt && x.Id.CompareTo(cursor.Id)<0);
        }
        var rows=await query.OrderByDescending(x=>x.SubmittedAt).ThenByDescending(x=>x.Id).Take(pageSize+1).ToArrayAsync(token);
        ServicingSubmissionView Project(ServicingUnderwritingSubmission row)=>new(row.Id,row.CycleId,row.RevisionId,row.RatingId,
            Convert.ToHexStringLower(row.InputHash),row.Reason,row.SubmittedBy,row.SubmittedAt,
            rating.Current is {Applicable:true,Result:{} result} live && live.Id==row.CycleId && live.RevisionId==row.RevisionId &&
            result.Id==row.RatingId && live.InputHash==Convert.ToHexStringLower(row.InputHash) && result.ExpiresAt>time.GetUtcNow());
        var items=rows.Take(pageSize).Select(Project).ToArray();
        var response=new ServicingSubmissionPage(draftId,etag,rating.AssessedAt,draft.CurrentCycleId,current is null?null:Project(current),
            items,rows.Length>pageSize?items[^1].Id:null);
        await tx.CommitAsync(token);return response;
    }
}
