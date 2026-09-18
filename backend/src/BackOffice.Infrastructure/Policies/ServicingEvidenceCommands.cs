using System.Security.Cryptography;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingProofView(ServicingProofRequirement Requirement, bool Satisfied);
public sealed record ServicingEvidenceView(Guid DraftId, Guid CycleId, string DraftEtag, bool Applicable, IReadOnlyList<ServicingProofView> Requirements);

public sealed partial class ServicingEvidenceService
{
    public async Task<ServicingEvidenceView> RequirementsAsync(ActorContext actor, Guid draftId, CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-read",time.GetUtcNow(),token);
        var requirements=await ServicingEvidenceProjection.RequirementsAsync(db,held,token);
        var rows=await (from a in db.Set<ServicingEvidenceAssociation>().AsNoTracking()
            join f in db.Set<ServicingEvidenceFile>().AsNoTracking() on a.FileId equals f.Id
            join e in db.Set<ServicingEvidenceEvent>().AsNoTracking() on a.LatestReviewId equals e.Id into reviews
            from e in reviews.DefaultIfEmpty()
            where a.DraftId==draftId && a.CycleId==held.Cycle.Id
            select new ServicingReviewedProof(a.DraftId,a.CycleId,a.RevisionId,a.RatingId,a.RequirementCode,a.RiskItemId,
                a.InputFingerprint,f.ScreeningState,e==null?"unreviewed":e.Outcome!,a.WithdrawnEventId!=null,a.CapacitySubmissionId)).ToArrayAsync(token);
        var applicable=held.Rating.ExpiresAt>time.GetUtcNow();
        var result=new ServicingEvidenceView(draftId,held.Cycle.Id,"\""+Convert.ToBase64String(held.Scope.Draft.RowVersion)+"\"",applicable,
            requirements.Select(r=>new ServicingProofView(r,applicable && rows.Any(p=>ServicingEvidenceRules.Satisfied(r.Context,r,p)))).ToArray());
        await tx.CommitAsync(token);return result;
    }

    public Task<CommandOutcome> AttachAsync(ActorContext actor, Guid draftId, Guid cycleId, byte[] version, Guid leaseToken,
        Guid fileId, string code, Guid? riskItemId, string fingerprint, string reason, string key, Guid correlation, CancellationToken token=default)
    {
        reason=EvidenceReason(reason);
        if (cycleId==Guid.Empty || fileId==Guid.Empty || riskItemId==Guid.Empty || !ReferralRules.Hash(fingerprint)) throw new QuoteOperationException(422,"servicing-evidence-input-invalid");
        ServicingDecisionContext? held=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/evidence",key,correlation),
            new {draftId,cycleId,version=Convert.ToBase64String(version),leaseToken,fileId,code,riskItemId,fingerprint,reason},"servicing.evidence-attached",
            async (db,ct)=>
            {
                held=await ServicingDecisionContext.Hold(db,actor,draftId,"underwriting-evidence-write",time.GetUtcNow(),ct,cycleId);
                if (!await db.Set<ServicingEvidenceFile>().AnyAsync(x=>x.Id==fileId && x.DraftId==draftId && x.ScreeningState=="accepted",ct))
                    throw new QuoteOperationException(404,"servicing-evidence-file-not-found");
            },
            async (db,ct)=>
            {
                await held!.Current(db,factory,time,version,leaseToken,ct);
                var purposes=(await ServicingEvidenceProjection.RequirementsAsync(db,held,ct)).Where(x=>x.Code==code && x.RiskItemId==riskItemId).ToArray();
                if(purposes.Length==0) throw new QuoteOperationException(422,"servicing-evidence-purpose-inapplicable");
                var required=purposes.SingleOrDefault(x=>x.InputFingerprint==fingerprint)??throw new QuoteOperationException(412,"servicing-evidence-input-stale");
                var now=time.GetUtcNow();var row=new ServicingEvidenceAssociation { DraftId=draftId,CycleId=cycleId,RevisionId=held.Cycle.RevisionId,
                    RatingId=held.Rating.Id,FileId=fileId,RequirementCode=code,RiskItemId=riskItemId,CapacitySubmissionId=required.CapacitySubmissionId,InputFingerprint=fingerprint,Reason=reason,
                    CreatedBy=held.Scope.Source.Scope.Actor.UserId,CreatedAt=now,UpdatedAt=now };
                db.Add(row);return await held.Receipt(db,row.Id,201,now,ct);
            },token);
    }

    public Task<CommandOutcome> ReviewAsync(ActorContext actor, Guid draftId, Guid cycleId, Guid associationId, byte[] version, Guid leaseToken,
        byte[] associationVersion, string outcome, string fingerprint, string reason, string key, Guid correlation, CancellationToken token=default)
        => EvidenceEvent(actor,draftId,cycleId,associationId,version,leaseToken,associationVersion,"review",outcome,fingerprint,reason,key,correlation,token);
    public Task<CommandOutcome> WithdrawAsync(ActorContext actor, Guid draftId, Guid cycleId, Guid associationId, byte[] version, Guid leaseToken,
        byte[] associationVersion, string reason, string key, Guid correlation, CancellationToken token=default)
        => EvidenceEvent(actor,draftId,cycleId,associationId,version,leaseToken,associationVersion,"withdrawal",null,null,reason,key,correlation,token);

    private Task<CommandOutcome> EvidenceEvent(ActorContext actor,Guid draftId,Guid cycleId,Guid associationId,byte[] version,Guid leaseToken,
        byte[] associationVersion,string kind,string? outcome,string? fingerprint,string reason,string key,Guid correlation,CancellationToken token)
    {
        reason=EvidenceReason(reason);
        if (cycleId==Guid.Empty || associationId==Guid.Empty || associationVersion.Length!=8 ||
            kind=="review" && (outcome is not ("accepted" or "rejected") || fingerprint is null || !ReferralRules.Hash(fingerprint)))
            throw new QuoteOperationException(422,"servicing-evidence-review-invalid");
        ServicingDecisionContext? held=null;ServicingEvidenceAssociation? association=null;EffectiveUnderwritingGrant? grant=null;
        var route=kind=="review"?"reviews":"withdraw";
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/evidence/{associationId:D}/{route}",key,correlation),
            new {draftId,cycleId,associationId,version=Convert.ToBase64String(version),leaseToken,associationVersion=Convert.ToBase64String(associationVersion),outcome,fingerprint,reason},"servicing.evidence-"+kind,
            async (db,ct)=>
            {
                held=await ServicingDecisionContext.Hold(db,actor,draftId,kind=="review"?"underwriting-evidence-review":"underwriting-evidence-write",time.GetUtcNow(),ct,cycleId);
                association=await db.Set<ServicingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM ServicingEvidenceAssociation WITH(UPDLOCK,HOLDLOCK) WHERE Id={associationId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404,"servicing-evidence-not-found");
                if (kind=="review")
                {
                    var remaining=held.Input.Term with {Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt};
                    var grants=await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,held.Cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
                        held.Scope.Eligible.Capture.Product.Code,remaining,time.GetUtcNow(),ct);
                    // Proof review does not approve risk or waive referrals. The
                    // trading-history purpose independently requires its grant.
                    grant=grants.FirstOrDefault(x=>association.RequirementCode!="trading-history" || x.Definition.GetProperty("limits").GetProperty("reviewTradingHistory").GetBoolean())
                        ?? throw new QuoteOperationException(403,"servicing-proof-authority-required");
                }
            },
            async (db,ct)=>
            {
                await held!.Current(db,factory,time,version,leaseToken,ct);
                if (!CryptographicOperations.FixedTimeEquals(association!.RowVersion,associationVersion)) throw new QuoteOperationException(412,"servicing-evidence-version-conflict");
                if (association.WithdrawnEventId is not null) throw new QuoteOperationException(409,"servicing-evidence-withdrawn");
                if (kind=="review")
                {
                    var purpose=(await ServicingEvidenceProjection.RequirementsAsync(db,held,ct)).SingleOrDefault(x=>x.Code==association.RequirementCode && x.RiskItemId==association.RiskItemId && x.CapacitySubmissionId==association.CapacitySubmissionId);
                    if (purpose is null || purpose.InputFingerprint!=association.InputFingerprint || fingerprint!=association.InputFingerprint)
                        throw new QuoteOperationException(412,"servicing-evidence-input-stale");
                }
                var now=time.GetUtcNow();var actorId=held.Scope.Source.Scope.Actor.UserId;
                var row=new ServicingEvidenceEvent {AssociationId=associationId,DraftId=draftId,CycleId=cycleId,RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,
                    Sequence=checked((await db.Set<ServicingEvidenceEvent>().Where(x=>x.AssociationId==associationId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),
                    Kind=kind,Outcome=outcome,Reason=reason,ActorId=actorId,AuthorityVersionId=grant?.Version.Id,InputFingerprint=association.InputFingerprint,
                    RecordedAt=now,CreatedBy=actorId,CreatedAt=now};
                db.Add(row);await db.SaveChangesAsync(ct);
                if (kind=="review") association.LatestReviewId=row.Id;else association.WithdrawnEventId=row.Id;
                association.UpdatedAt=now;
                return await held.Receipt(db,row.Id,200,now,ct);
            },token);
    }

    private static string EvidenceReason(string reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length>=10 && reason.Length<=2000 && !reason.Any(char.IsControl)
        ? reason.Trim() : throw new QuoteOperationException(422,"servicing-evidence-reason-required");
}
