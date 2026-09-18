using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityService
{
    public Task<CommandOutcome> ResolveConditionAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid conditionId,
        byte[] version,byte[] conditionVersion,Guid lease,Guid associationId,string outcome,string reason,string key,
        Guid correlation,CancellationToken token=default)
    {
        if(draftId==Guid.Empty || cycleId==Guid.Empty || conditionId==Guid.Empty || associationId==Guid.Empty || lease==Guid.Empty ||
            version is null || version.Length!=8 || conditionVersion is null || conditionVersion.Length!=8 ||
            outcome is not ("satisfied" or "rejected") || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length<10)
            throw new QuoteOperationException(422,"servicing-carrier-resolution-invalid");
        reason=QuoteRatingService.Reason(reason);version=version.ToArray();conditionVersion=conditionVersion.ToArray();
        ServicingDecisionContext? held=null;ServicingCapacityCondition? condition=null;EffectiveUnderwritingGrant? grant=null;
        ServicingEvidenceAssociation? proof=null;ServicingProofRequirement? required=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/capacity-conditions/{conditionId:D}/resolutions",key,correlation),
            new{draftId,cycleId,conditionId,version=Convert.ToBase64String(version),conditionVersion=Convert.ToBase64String(conditionVersion),
                lease,associationId,outcome,reason},"servicing.carrier-condition-resolved",
            async(db,ct)=>
            {
                held=await HoldEscalationAuthority(db,actor,draftId,cycleId,lease,ct);
                if(!held.Scope.Source.Scope.Actor.HasCapability("underwriting-decide-within-authority") || !actor.HasCapability("underwriting-decide-within-authority"))
                    throw new QuoteOperationException(403,"servicing-carrier-resolution-denied");
                condition=await db.Set<ServicingCapacityCondition>().FromSqlInterpolated($"SELECT * FROM ServicingCapacityCondition WITH(UPDLOCK,HOLDLOCK) WHERE Id={conditionId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-carrier-condition-not-found");
                if(!await CarrierConditionActive(db,condition,ct) || !await CarrierConditionSourceCurrent(db,condition,ct))
                    throw new QuoteOperationException(409,"servicing-carrier-condition-superseded");
                var remaining=held.Input.Term with{Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt};
                var grants=await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,held.Cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
                    held.Scope.Eligible.Capture.Product.Code,remaining,time.GetUtcNow(),ct);
                // Resolving proof does not approve risk or extend a grant. Issue
                // eligibility separately checks the actor's complete authority.
                grant=grants.FirstOrDefault(x=>condition.Code!="provide-trading-history" || x.Definition.GetProperty("limits").GetProperty("reviewTradingHistory").GetBoolean());
                if(grant is null) throw new QuoteOperationException(403,"servicing-carrier-resolution-authority-required");
                proof=await db.Set<ServicingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM ServicingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={associationId} AND DraftId={draftId} AND CycleId={cycleId}").AsNoTracking().SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-evidence-not-found");
                required=await CarrierConditionRequirement(db,held,condition,ct);
                if(required is null || proof.WithdrawnEventId is not null || proof.LatestReviewId is null ||
                    proof.RequirementCode!=required.Code || proof.RiskItemId!=required.RiskItemId || proof.InputFingerprint!=required.InputFingerprint ||
                    proof.CapacitySubmissionId!=required.CapacitySubmissionId ||
                    !await db.Set<ServicingEvidenceFile>().AnyAsync(x=>x.Id==proof.FileId && x.ScreeningState=="accepted",ct) ||
                    !await db.Set<ServicingEvidenceEvent>().AnyAsync(x=>x.Id==proof.LatestReviewId && x.AssociationId==associationId && x.Kind=="review" &&
                        (outcome=="rejected" || x.Outcome=="accepted"),ct))
                    throw new QuoteOperationException(409,"servicing-carrier-condition-proof-required");
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);
                if(!CryptographicOperations.FixedTimeEquals(condition!.RowVersion,conditionVersion)) throw new QuoteOperationException(412,"servicing-carrier-condition-stale");
                var now=time.GetUtcNow();
                var resolution=new ServicingCapacityConditionResolution{ConditionId=conditionId,ResponseId=condition.ResponseId,SubmissionId=condition.SubmissionId,
                    CaseId=condition.CaseId,DraftId=draftId,CycleId=cycleId,RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,
                    AssociationId=associationId,ReviewId=proof!.LatestReviewId!.Value,InputFingerprint=required!.InputFingerprint,
                    Sequence=checked((await db.Set<ServicingCapacityConditionResolution>().Where(x=>x.ConditionId==conditionId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),
                    Outcome=outcome,Reason=reason,ActorId=actor.UserId,AuthorityVersionId=grant!.Version.Id,GrantId=grant.Grant.Id,
                    RecordedAt=now,CreatedAt=now,CreatedBy=actor.UserId};
                db.Add(resolution);return await held.Receipt(db,resolution.Id,200,now,ct);
            },token);
    }

    public async Task<bool> ConditionSatisfiedAsync(ActorContext actor,Guid draftId,Guid conditionId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-read",time.GetUtcNow(),token);
        var condition=await db.Set<ServicingCapacityCondition>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==conditionId && x.DraftId==draftId && x.CycleId==held.Cycle.Id,token)
            ??throw new QuoteOperationException(404,"servicing-carrier-condition-not-found");
        var result=held.Rating.ExpiresAt>time.GetUtcNow() && await CarrierConditionSatisfied(db,held,condition,token);
        await tx.CommitAsync(token);return result;
    }

    internal static async Task<bool> CarrierConditionSatisfied(BackOfficeDbContext db,ServicingDecisionContext held,ServicingCapacityCondition condition,CancellationToken token)
    {
        if(!await CarrierConditionActive(db,condition,token) || !await CarrierConditionSourceCurrent(db,condition,token)) return false;
        var resolution=await db.Set<ServicingCapacityConditionResolution>().AsNoTracking().Where(x=>x.ConditionId==condition.Id).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
        var required=await CarrierConditionRequirement(db,held,condition,token);
        if(resolution?.Outcome!="satisfied" || required is null || resolution.InputFingerprint!=required.InputFingerprint) return false;
        var proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==resolution.AssociationId,token);
        return proof.LatestReviewId==resolution.ReviewId && proof.RequirementCode==required.Code && proof.RiskItemId==required.RiskItemId &&
            proof.CapacitySubmissionId==required.CapacitySubmissionId && await ReviewedProofCurrent(db,proof,token);
    }

    private static Task<bool> CarrierConditionActive(BackOfficeDbContext db,ServicingCapacityCondition condition,CancellationToken token)=>
        db.Set<ServicingCapacityCase>().AnyAsync(x=>x.Id==condition.CaseId && x.State=="conditional" && x.CurrentResponseId==condition.ResponseId && x.CurrentSubmissionId==condition.SubmissionId,token);

    private static async Task<bool> CarrierConditionSourceCurrent(BackOfficeDbContext db,ServicingCapacityCondition condition,CancellationToken token)
    {
        var response=await db.Set<ServicingCapacityResponseRecord>().AsNoTracking().SingleAsync(x=>x.Id==condition.ResponseId,token);
        if(response.ApplicationState!="applied") return false;
        if(response.Provenance=="supplied-response")
        {
            var proof=await db.Set<ServicingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM ServicingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={response.EvidenceAssociationId}").AsNoTracking().SingleAsync(token);
            if(proof.LatestReviewId!=response.EvidenceReviewId || proof.CapacitySubmissionId!=condition.SubmissionId || !await ReviewedProofCurrent(db,proof,token)) return false;
        }
        try { await RequireSelectedProof(db,condition.SubmissionId,token); }
        catch(QuoteOperationException e) when(e.Code=="servicing-capacity-evidence-unavailable") { return false; }
        return true;
    }

    private static async Task<ServicingProofRequirement?> CarrierConditionRequirement(BackOfficeDbContext db,ServicingDecisionContext held,ServicingCapacityCondition condition,CancellationToken token)
    {
        var parsed=ServicingConditionRules.Parse(JsonSerializer.Deserialize<JsonElement>(condition.DefinitionJson),ServicingEvidenceProjection.Slices(held),
            JsonSerializer.Deserialize<DateTimeOffset[]>(condition.EffectiveDatesJson)!);
        var definition=parsed[0].Condition;
        if(!ReferralRules.CanResolveWithEvidence(definition) || definition.TermsVersionId is not null || definition.Kind!="warranty" && definition.TargetIds.Count>1) return null;
        var target=definition.Kind=="warranty" || definition.TargetIds.Count==0?(Guid?)null:definition.TargetIds[0];
        return (await ServicingEvidenceProjection.RequirementsAsync(db,held,token)).SingleOrDefault(x=>x.Code==definition.RequirementCode && x.RiskItemId==target &&
            parsed.All(p=>x.EffectiveDates.Contains(p.EffectiveAt)));
    }
}
