using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingTermsHistoryItem
{
    public Guid Id {get;init;}
    public Guid CycleId {get;init;}
    public Guid RevisionId {get;init;}
    public Guid RatingId {get;init;}
    public Guid TermsVersionId {get;init;}
    public Guid? DeliveryId {get;init;}
    public DateTimeOffset RecordedAt {get;init;}
    public required string State {get;init;}
    public required string Label {get;init;}
}
public sealed record ServicingTermsHistoryPage(IReadOnlyList<ServicingTermsHistoryItem> Items,Guid? NextBeforeId);

public sealed partial class ServicingTermsService
{
    public async Task<ServicingTermsHistoryPage> HistoryAsync(ActorContext actor,Guid draftId,string kind,Guid? beforeId,int pageSize,CancellationToken token=default)
    {
        if(pageSize is <1 or >50 || beforeId==Guid.Empty)throw new QuoteOperationException(400,"invalid-query");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        IQueryable<ServicingTermsHistoryItem> query=kind switch{
            "terms"=>db.Set<ServicingTermsVersion>().AsNoTracking().Where(x=>x.DraftId==draftId)
                .Select(x=>new ServicingTermsHistoryItem{Id=x.Id,CycleId=x.CycleId,RevisionId=x.RevisionId,RatingId=x.RatingId,TermsVersionId=x.Id,RecordedAt=x.PreparedAt,State="prepared",Label="Prepared contract"}),
            "deliveries"=>db.Set<ServicingTermsDelivery>().AsNoTracking().Where(x=>x.DraftId==draftId)
                .Select(x=>new ServicingTermsHistoryItem{Id=x.Id,CycleId=x.CycleId,RevisionId=x.RevisionId,RatingId=x.RatingId,TermsVersionId=x.TermsVersionId,DeliveryId=x.Id,RecordedAt=x.CreatedAt,State=x.State,Label="Demo delivery"}),
            "acceptances"=>db.Set<ServicingAcceptance>().AsNoTracking().Where(x=>x.DraftId==draftId)
                .Select(x=>new ServicingTermsHistoryItem{Id=x.Id,CycleId=x.CycleId,RevisionId=x.RevisionId,RatingId=x.RatingId,TermsVersionId=x.TermsVersionId,DeliveryId=x.DeliveryId,RecordedAt=x.RecordedAt,State="recorded",Label=x.AccepterLabel}),
            _=>throw new QuoteOperationException(404,"servicing-history-not-found")};
        if(beforeId is {} id)
        {
            var cursor=await query.SingleOrDefaultAsync(x=>x.Id==id,token)??throw new QuoteOperationException(404,"servicing-history-cursor-not-found");
            query=query.Where(x=>x.RecordedAt<cursor.RecordedAt || x.RecordedAt==cursor.RecordedAt && x.Id.CompareTo(cursor.Id)<0);
        }
        var rows=await query.OrderByDescending(x=>x.RecordedAt).ThenByDescending(x=>x.Id).Take(pageSize+1).ToArrayAsync(token);
        var items=rows.Take(pageSize).ToArray();await tx.CommitAsync(token);return new(items,rows.Length>pageSize?items[^1].Id:null);
    }

    public async Task<ServicingTermsSnapshot> RetainedAsync(ActorContext actor,Guid draftId,Guid termsId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var row=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==termsId && x.DraftId==draftId,token)
            ??throw new QuoteOperationException(404,"servicing-terms-not-found");
        // Historical reads never imply current issue eligibility.
        var result=new ServicingTermsSnapshot(row.Id,row.Sequence,row.RatingId,row.TemplateVersionId,row.TermsHash,row.PreparedAt,JsonSerializer.Deserialize<JsonElement>(row.TermsJson),false);
        await tx.CommitAsync(token);return result;
    }
}
