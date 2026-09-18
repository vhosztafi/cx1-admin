using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingSubmissionService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private readonly SqlCommandBoundary commands=new(factory,time);

    public Task<CommandOutcome> SubmitAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid revisionId,
        byte[] version,Guid lease,string reason,string key,Guid correlation,CancellationToken token=default)
    {
        try { reason=ServicingSubmissionRules.Validate(draftId,cycleId,revisionId,version,lease,reason); }
        catch(ArgumentException) { throw new QuoteOperationException(422,"servicing-submission-input-invalid"); }
        version=version.ToArray();
        ServicingDecisionContext? held=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/submit",key,correlation),
            new{draftId,cycleId,revisionId,version=Convert.ToBase64String(version),lease,reason},"servicing.submitted",
            async(db,ct)=>
            {
                held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-draft-write",time.GetUtcNow(),ct,cycleId);
                if(held.Cycle.RevisionId!=revisionId) throw new QuoteOperationException(412,"servicing-version-conflict");
                if(held.Rating.ExpiresAt<=time.GetUtcNow()) throw new QuoteOperationException(409,"servicing-rating-expired");
                var drafts=new ServicingDraftService(factory,time);
                // The submitted version may change as a consequence of success;
                // current identity/scope and lease must still hold before replay.
                await drafts.DemandLease(db,draftId,held.Scope.Source.Scope.Actor.UserId,lease,ct);
                var proposal=ServicingProposalInput.Parse(held.Scope.Revision.ProposalJson,held.Scope.Draft.BaseVersionId);
                var assessment=await drafts.Assess(db,held.Scope.Source.Scope.Actor,held.Scope.Draft,proposal,ct);
                if(assessment.ReadinessIssues.Count!=0) throw new QuoteValidationException(assessment.ReadinessIssues);
                _=ServicingEvidenceProjection.Slices(held);
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);
                if(await db.Set<ServicingUnderwritingSubmission>().AnyAsync(x=>x.CycleId==cycleId,ct))
                    throw new QuoteOperationException(409,"servicing-cycle-already-submitted");
                var now=time.GetUtcNow();
                var row=new ServicingUnderwritingSubmission {DraftId=draftId,CycleId=cycleId,RevisionId=revisionId,
                    RatingId=held.Rating.Id,InputHash=held.Cycle.InputHash.ToArray(),Reason=reason,
                    SubmittedBy=held.Scope.Source.Scope.Actor.UserId,SubmittedAt=now,CreatedBy=held.Scope.Source.Scope.Actor.UserId,CreatedAt=now};
                db.Add(row);
                return await held.Receipt(db,row.Id,201,now,ct);
            },token);
    }
}
