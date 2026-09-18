using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingDocumentHistoryItem(Guid TermsVersionId,string Title,int Version,DateTimeOffset PreparedAt,
    Guid? DeliveryId,string? DeliveryState,DateTimeOffset? SentAt,IReadOnlyList<ServicingTermsRecipient> Recipients);
public sealed record ServicingDocumentHistoryPage(IReadOnlyList<ServicingDocumentHistoryItem> Items,Guid? NextBeforeId);

public sealed partial class ServicingTermsService
{
    // Document history is independent of current rating/issue eligibility. The
    // existing signed history cursor and scope checks still fence each page.
    public async Task<ServicingDocumentHistoryPage> DocumentsAsync(ActorContext actor,Guid draftId,Guid? beforeId,int pageSize,CancellationToken token=default)
    {
        var history=await HistoryAsync(actor,draftId,"terms",beforeId,pageSize,token);
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var ids=history.Items.Select(x=>x.TermsVersionId).ToArray();
        var terms=await db.Set<ServicingTermsVersion>().AsNoTracking().Where(x=>x.DraftId==draftId && ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
        // A later failed attempt cannot undo a previously delivered document.
        // All attempts remain separately available in delivery history.
        var deliveries=await db.Set<ServicingTermsDelivery>().AsNoTracking().Where(x=>x.DraftId==draftId && ids.Contains(x.TermsVersionId))
            .GroupBy(x=>x.TermsVersionId).Select(g=>g.OrderByDescending(x=>x.State=="delivered").ThenByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).First()).ToArrayAsync(token);
        var byTerms=deliveries.ToDictionary(x=>x.TermsVersionId);
        var result=history.Items.Select(item=>
        {
            var saved=terms[item.TermsVersionId];using var json=JsonDocument.Parse(saved.TermsJson);byTerms.TryGetValue(saved.Id,out var delivery);
            return new ServicingDocumentHistoryItem(saved.Id,json.RootElement.GetProperty("template").GetProperty("title").GetString()!,saved.Sequence,saved.PreparedAt,
                delivery?.Id,delivery?.State,delivery?.State=="delivered"?delivery.CompletedAt:null,
                delivery is null?[]:JsonSerializer.Deserialize<ServicingTermsRecipient[]>(delivery.RecipientSnapshotJson,ServicingRatingService.Json)!);
        }).ToArray();
        await tx.CommitAsync(token);return new(result,history.NextBeforeId);
    }
}
