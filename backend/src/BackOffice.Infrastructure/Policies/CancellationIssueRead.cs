using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record CancellationFollowUp(Guid Id,string Kind,string State,string? NoticeOutcome);
public sealed record CancellationIssuedView(Guid DraftId,string DraftEtag,Guid PolicyId,string PolicyReference,
    Guid TermId,Guid TransactionId,Guid VersionId,Guid DecisionId,Guid ApprovalId,Guid PreviewId,
    DateTimeOffset EffectiveAt,DateTimeOffset ProcessedAt,string CoverageState,string NetAmount,string CashPaid,
    IReadOnlyList<CancellationFollowUp> Consequences);

public sealed partial class CancellationReviewService
{
    public async Task<CancellationIssuedView> ReadIssueAsync(ActorContext actor,Guid draftId,CancellationToken token=default)
    {
        if(!actor.HasCapability("policy-read"))throw new QuoteOperationException(403,"cancellation-access-denied");
        await using var db=await factory.CreateDbContextAsync(token);
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token);
        var policy=await (from d in db.Set<ServicingDraft>() join p in db.Set<Policy>() on d.PolicyId equals p.Id
            where d.Id==draftId select p).AsNoTracking().SingleOrDefaultAsync(token)
            ??throw new QuoteOperationException(404,"servicing-draft-not-found");
        var source=await QuoteScope.ForQuoteAsync(db,actor,policy.SourceQuoteId,QuoteAccess.Read,token);
        if(!source.Scope.Actor.HasCapability("policy-read"))throw new QuoteOperationException(403,"cancellation-access-denied");
        var draft=await ServicingDraftService.HoldDraft(db,source.Scope.Actor,draftId,false,token);
        if(draft.Kind!="cancellation" || draft.State!="issued")throw new QuoteOperationException(409,"cancellation-not-issued");
        var decision=await db.Set<CancellationIssueDecision>().AsNoTracking().SingleAsync(x=>x.DraftId==draftId,token);
        var transaction=await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x=>x.Id==draft.IssuedTransactionId,token);
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.TransactionId==transaction.Id,token);
        var financial=await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync(x=>x.TransactionId==transaction.Id,token);
        var consequences=await (from c in db.Set<CancellationConsequence>() join w in db.Set<OutboxWork>() on c.WorkId equals w.Id
            join receipt in db.Set<CancellationNoticeReceipt>() on c.Id equals receipt.ConsequenceId into receipts
            from receipt in receipts.DefaultIfEmpty() where c.TransactionId==transaction.Id orderby c.Kind
            select new CancellationFollowUp(c.Id,w.Kind,w.State,receipt==null?null:receipt.Outcome)).ToArrayAsync(token);
        var result=new CancellationIssuedView(draftId,"\""+Convert.ToBase64String(draft.RowVersion)+"\"",policy.Id,policy.Reference,
            decision.BaseTermId,transaction.Id,version.Id,decision.Id,decision.ApprovalId,decision.PreviewId,
            decision.EffectiveAt,decision.CreatedAt,time.GetUtcNow()<decision.EffectiveAt?"cancellation-scheduled":"cancelled",
            PolicyIssueWriter.Money(financial.InvoiceDue),"0.00",consequences);
        await tx.CommitAsync(token);return result;
    }
}
