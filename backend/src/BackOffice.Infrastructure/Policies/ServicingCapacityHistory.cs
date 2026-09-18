using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityReadModel
{
    public async Task<ServicingCapacityHistoryPage<ServicingCapacitySubmissionView>> SubmissionsAsync(ActorContext actor,Guid draftId,Guid caseId,
        int afterSequence=0,int pageSize=25,CancellationToken token=default)
    {
        Page(afterSequence,pageSize);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await Own(db,actor,draftId,caseId,token);
        var rows=await db.Set<ServicingCapacitySubmission>().AsNoTracking().Where(x=>x.CaseId==caseId && x.Sequence>afterSequence)
            .OrderBy(x=>x.Sequence).Take(pageSize+1).ToArrayAsync(token);
        var items=await SubmissionViews(db,rows.Take(pageSize).ToArray(),token);
        await tx.CommitAsync(token);return new(items,rows.Length>pageSize?items[^1].Sequence:null);
    }

    public async Task<ServicingCapacityHistoryPage<ServicingCapacityMessageView>> MessagesAsync(ActorContext actor,Guid draftId,Guid caseId,
        int afterSequence=0,int pageSize=25,CancellationToken token=default)
    {
        Page(afterSequence,pageSize);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await Own(db,actor,draftId,caseId,token);
        var rows=await (from m in db.Set<ServicingCapacityMessage>().AsNoTracking() join u in db.Set<StaffUser>() on m.RecordedBy equals u.Id
            where m.CaseId==caseId && m.Sequence>afterSequence orderby m.Sequence
            select new ServicingCapacityMessageView(m.Id,m.Sequence,m.SubmissionId,m.Kind,m.Body,m.RecordedBy,u.DisplayName,m.RecordedAt)).Take(pageSize+1).ToArrayAsync(token);
        var items=rows.Take(pageSize).ToArray();await tx.CommitAsync(token);return new(items,rows.Length>pageSize?items[^1].Sequence:null);
    }

    public async Task<ServicingCapacityHistoryPage<ServicingCapacityResponseView>> ResponsesAsync(ActorContext actor,Guid draftId,Guid caseId,
        int afterSequence=0,int pageSize=25,CancellationToken token=default)
    {
        Page(afterSequence,pageSize);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await Own(db,actor,draftId,caseId,token);
        var rows=await db.Set<ServicingCapacityResponseRecord>().AsNoTracking().Where(x=>x.CaseId==caseId && x.Sequence>afterSequence)
            .OrderBy(x=>x.Sequence).Take(pageSize+1).ToArrayAsync(token);
        var items=rows.Take(pageSize).Select(ResponseView).ToArray();await tx.CommitAsync(token);return new(items,rows.Length>pageSize?items[^1].Sequence:null);
    }

    public async Task<ServicingCapacityHistoryPage<ServicingCarrierResolutionView>> ResolutionsAsync(ActorContext actor,Guid draftId,Guid caseId,Guid conditionId,
        int afterSequence=0,int pageSize=25,CancellationToken token=default)
    {
        Page(afterSequence,pageSize);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await Own(db,actor,draftId,caseId,token);
        if(!await db.Set<ServicingCapacityCondition>().AnyAsync(x=>x.Id==conditionId && x.CaseId==caseId && x.DraftId==draftId,token))
            throw new QuoteOperationException(404,"servicing-carrier-condition-not-found");
        var rows=await db.Set<ServicingCapacityConditionResolution>().AsNoTracking().Where(x=>x.ConditionId==conditionId && x.Sequence>afterSequence)
            .OrderBy(x=>x.Sequence).Take(pageSize+1).Select(x=>new ServicingCarrierResolutionView(x.Id,x.Sequence,x.AssociationId,x.ReviewId,x.Outcome,x.Reason,
                x.ActorId,x.AuthorityVersionId,x.GrantId,x.RecordedAt)).ToArrayAsync(token);
        var items=rows.Take(pageSize).ToArray();await tx.CommitAsync(token);return new(items,rows.Length>pageSize?items[^1].Sequence:null);
    }

    private static async Task Own(BackOfficeDbContext db,ActorContext actor,Guid draftId,Guid caseId,CancellationToken token)
    {
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);await Case(db,draftId,caseId,token);
    }
    private static void Page(int after,int size)
    {
        if(after<0 || size is <1 or >50) throw new QuoteOperationException(422,"servicing-capacity-page-invalid");
    }

    private static async Task<ServicingCapacitySubmissionView[]> SubmissionViews(BackOfficeDbContext db,ServicingCapacitySubmission[] rows,CancellationToken token)
    {
        var ids=rows.Select(x=>x.Id).ToArray();var workIds=rows.Select(x=>x.WorkId).ToArray();
        var works=await db.Set<OutboxWork>().AsNoTracking().Where(x=>workIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
        var selections=await db.Set<ServicingCapacitySubmissionEvidence>().AsNoTracking().Where(x=>ids.Contains(x.SubmissionId))
            .Select(x=>new{x.SubmissionId,x.AssociationId}).ToArrayAsync(token);
        return rows.Select(x=>new ServicingCapacitySubmissionView(x.Id,x.Sequence,x.Body,x.Reason,Convert.ToHexStringLower(x.ContextHash),x.WorkId,
            works[x.WorkId].State,works[x.WorkId].Attempts,works[x.WorkId].ErrorCode,x.ScenarioVersionId,x.SubmittedAt,x.ResponseDueAt,
            selections.Where(s=>s.SubmissionId==x.Id).Select(s=>s.AssociationId).Order().ToArray())).ToArray();
    }

    private static ServicingCapacityResponseView ResponseView(ServicingCapacityResponseRecord row)=>new(row.Id,row.Sequence,row.SubmissionId,row.Provenance,
        row.Outcome,row.Body,row.ProviderUnderwriter,row.ProviderReference,row.ApplicationState,row.EvidenceAssociationId,row.EvidenceReviewId,row.ReceivedAt,row.RecordedAt,
        JsonSerializer.Deserialize<ServicingCapacityResponseDefinition>(row.DefinitionJson,ServicingRatingService.Json)
            ??throw new QuoteOperationException(409,"servicing-capacity-response-unavailable"));
}
