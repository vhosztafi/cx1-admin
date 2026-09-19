using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record CancellationIssueInput(Guid PreviewId,Guid ApprovalId,string PreviewHash,string Reason);

public sealed partial class CancellationReviewService
{
    public Task<CommandOutcome> IssueAsync(ActorContext actor,Guid draftId,byte[] version,Guid lease,CancellationIssueInput input,
        string key,Guid correlation,CancellationToken token=default)
    {
        if(input is null || draftId==Guid.Empty || lease==Guid.Empty || input.PreviewId==Guid.Empty || input.ApprovalId==Guid.Empty || version is null || version.Length!=8)
            throw new QuoteOperationException(422,"cancellation-issue-input-invalid");
        DemandHash(input.PreviewHash);input=input with{Reason=Reason(input.Reason)};
        HeldCancellation? held=null;Guid grantId=Guid.Empty,authorityId=Guid.Empty;bool issued=false;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/cancellation-issue",key,correlation),
            new{draftId,version=Convert.ToBase64String(version),lease,input},"cancellation.issued",
            async(db,ct)=>
            {
                held=await Hold(db,actor,draftId,"policy-issue-within-authority",true,ct);
                issued=held.Draft.State=="issued";
                if(issued)
                {
                    var decision=await db.Set<CancellationIssueDecision>().AsNoTracking().SingleAsync(x=>x.DraftId==draftId,ct);
                    if(!held.Grants.Any(x=>x.Grant.Id==decision.AuthorityGrantId && x.Version.Id==decision.AuthorityVersionId))
                        throw new QuoteOperationException(403,"cancellation-issue-authority-required");
                    return;
                }
                var grant=held.Grants.OrderBy(x=>x.Grant.Id).FirstOrDefault()
                    ??throw new QuoteOperationException(403,"cancellation-issue-authority-required");
                grantId=grant.Grant.Id;authorityId=grant.Version.Id;
            },
            async(db,ct)=>
            {
                if(issued || held!.Draft.State!="draft")throw new QuoteOperationException(409,"cancellation-already-closed");
                if(!held.Draft.RowVersion.SequenceEqual(version))throw new QuoteOperationException(412,"servicing-version-conflict");
                await new ServicingDraftService(factory,time).DemandLease(db,draftId,held.Source.Scope.Actor.UserId,lease,ct);
                var assessed=await Assess(db,held,ct);DemandPreview(assessed.View,input.PreviewHash);
                if(assessed.View.PreviewId!=input.PreviewId || assessed.View.ApprovalId!=input.ApprovalId)
                    throw new QuoteOperationException(409,"cancellation-approval-stale");
                var now=time.GetUtcNow();
                var decision=new CancellationIssueDecision{DraftId=draftId,PolicyId=held.Draft.PolicyId,BaseTermId=held.Term.Id,BaseVersionId=held.Draft.BaseVersionId,
                    RevisionId=held.Draft.CurrentRevisionId!.Value,PreviewId=input.PreviewId,ApprovalId=input.ApprovalId,PreviewHash=Convert.FromHexString(input.PreviewHash),
                    AuthorityGrantId=grantId,AuthorityVersionId=authorityId,ActorId=held.Source.Scope.Actor.UserId,EffectiveAt=assessed.View.EffectiveAt,
                    Reason=input.Reason,CreatedAt=now,CreatedBy=held.Source.Scope.Actor.UserId};
                db.Add(decision);await db.SaveChangesAsync(ct);
                return await CancellationIssueWriter.Write(db,held,decision,assessed.View,now,correlation,ct);
            },token);
    }
}
