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

public sealed partial class ServicingReferralService
{
    public Task<CommandOutcome> ResolveAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid conditionId,byte[] version,Guid lease,
        byte[] conditionVersion,Guid associationId,string outcome,string reason,string key,Guid correlation,CancellationToken token=default)
    {
        reason=QuoteRatingService.Reason(reason);
        if(version is null || version.Length!=8 || conditionVersion is null || conditionVersion.Length!=8 || lease==Guid.Empty ||
            conditionId==Guid.Empty || associationId==Guid.Empty || cycleId==Guid.Empty || reason.Length<10 || outcome is not("satisfied" or "rejected"))
            throw new QuoteOperationException(422,"servicing-resolution-invalid");
        ServicingDecisionContext? held=null;ServicingCondition? condition=null;EffectiveUnderwritingGrant? grant=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/conditions/{conditionId:D}/resolutions",key,correlation),
            new{draftId,cycleId,conditionId,version=Convert.ToBase64String(version),lease,conditionVersion=Convert.ToBase64String(conditionVersion),associationId,outcome,reason},
            "servicing.condition-resolved",
            async(db,ct)=>
            {
                var now=time.GetUtcNow();held=await ServicingDecisionContext.Hold(db,actor,draftId,"underwriting-decide-within-authority",now,ct,cycleId);
                condition=await db.Set<ServicingCondition>().FromSqlInterpolated($"SELECT * FROM ServicingCondition WITH(UPDLOCK,HOLDLOCK) WHERE Id={conditionId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-condition-not-found");
                var remaining=held.Input.Term with{Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt};
                var grants=await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,held.Cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
                    held.Scope.Eligible.Capture.Product.Code,remaining,now,ct);
                var decision=await db.Set<ServicingReferralDecision>().AsNoTracking().SingleAsync(x=>x.Id==condition.DecisionId,ct);
                foreach(var candidate in grants)
                    if((condition.Code!="provide-trading-history" || candidate.Definition.GetProperty("limits").GetProperty("reviewTradingHistory").GetBoolean()) &&
                        (decision.Outcome=="query" || await ResolutionAuthority(db,held,candidate.Definition,time.GetUtcNow(),ct))) {grant=candidate;break;}
                if(grant is null) throw new QuoteOperationException(403,"servicing-resolution-authority-required");
                if(!await db.Set<ServicingEvidenceAssociation>().AnyAsync(x=>x.Id==associationId && x.DraftId==draftId && x.CycleId==cycleId,ct))
                    throw new QuoteOperationException(404,"servicing-evidence-not-found");
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);
                if(!CryptographicOperations.FixedTimeEquals(condition!.RowVersion,conditionVersion)) throw new QuoteOperationException(412,"servicing-condition-stale");
                if(!await ConditionActive(db,condition,ct)) throw new QuoteOperationException(409,"servicing-condition-superseded");
                var required=await ConditionRequirement(db,held,condition,ct)??throw new QuoteOperationException(409,"servicing-condition-proof-unavailable");
                var association=await db.Set<ServicingEvidenceAssociation>().SingleAsync(x=>x.Id==associationId,ct);
                if(association.WithdrawnEventId is not null || association.LatestReviewId is null || association.RequirementCode!=required.Code ||
                    association.RiskItemId!=required.RiskItemId || association.InputFingerprint!=required.InputFingerprint)
                    throw new QuoteOperationException(409,"servicing-condition-proof-required");
                var review=await db.Set<ServicingEvidenceEvent>().AsNoTracking().SingleAsync(x=>x.Id==association.LatestReviewId && x.AssociationId==association.Id,ct);
                if(review.Kind!="review" || outcome=="satisfied" && review.Outcome!="accepted") throw new QuoteOperationException(409,"servicing-condition-proof-review-required");
                var now=time.GetUtcNow();var resolution=new ServicingConditionResolution{ConditionId=conditionId,ReferralId=condition.ReferralId,DraftId=draftId,CycleId=cycleId,
                    RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,AssociationId=associationId,ReviewId=review.Id,InputFingerprint=required.InputFingerprint,
                    Sequence=checked((await db.Set<ServicingConditionResolution>().Where(x=>x.ConditionId==conditionId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),
                    Outcome=outcome,Reason=reason,ActorId=actor.UserId,AuthorityVersionId=grant!.Version.Id,GrantId=grant.Grant.Id,RecordedAt=now,CreatedAt=now,CreatedBy=actor.UserId};
                db.Add(resolution);return await held.Receipt(db,resolution.Id,200,now,ct);
            },token);
    }

    public async Task<bool> ConditionSatisfiedAsync(ActorContext actor,Guid draftId,Guid conditionId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-read",time.GetUtcNow(),token);
        var condition=await db.Set<ServicingCondition>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==conditionId && x.DraftId==draftId && x.CycleId==held.Cycle.Id,token)
            ??throw new QuoteOperationException(404,"servicing-condition-not-found");
        var result=await ConditionSatisfied(db,held,condition,token);
        await tx.CommitAsync(token);return result;
    }

    private async Task<bool> ConditionSatisfied(BackOfficeDbContext db,ServicingDecisionContext held,ServicingCondition condition,CancellationToken token)
    {
        var satisfied=false;
        if(held.Rating.ExpiresAt>time.GetUtcNow() && await ConditionActive(db,condition,token))
        {
            var resolution=await db.Set<ServicingConditionResolution>().AsNoTracking().Where(x=>x.ConditionId==condition.Id).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
            var required=await ConditionRequirement(db,held,condition,token);
            if(resolution?.Outcome=="satisfied" && required is not null && resolution.InputFingerprint==required.InputFingerprint)
            {
                var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==resolution.AssociationId,token);
                var review=await db.Set<ServicingEvidenceEvent>().AsNoTracking().SingleAsync(x=>x.Id==resolution.ReviewId,token);
                var file=await db.Set<ServicingEvidenceFile>().AsNoTracking().SingleAsync(x=>x.Id==association.FileId,token);
                satisfied=association.LatestReviewId==review.Id && ServicingEvidenceRules.Satisfied(required.Context,required,
                    new(association.DraftId,association.CycleId,association.RevisionId,association.RatingId,association.RequirementCode,association.RiskItemId,
                        association.InputFingerprint,file.ScreeningState,review.Outcome??"unreviewed",association.WithdrawnEventId is not null));
            }
        }
        return satisfied;
    }

    private static Task<bool> ConditionActive(BackOfficeDbContext db,ServicingCondition condition,CancellationToken token)=>
        db.Set<ServicingReferral>().AnyAsync(x=>x.Id==condition.ReferralId && x.LatestDecisionId==condition.DecisionId && (x.State=="conditional" || x.State=="queried"),token);

    private static async Task<ServicingProofRequirement?> ConditionRequirement(BackOfficeDbContext db,ServicingDecisionContext held,ServicingCondition condition,CancellationToken token)
    {
        var parsed=ServicingConditionRules.Parse(JsonSerializer.Deserialize<JsonElement>(condition.DefinitionJson),ServicingEvidenceProjection.Slices(held),
            JsonSerializer.Deserialize<DateTimeOffset[]>(condition.EffectiveDatesJson)!);
        var definition=parsed[0].Condition;
        if(!ReferralRules.CanResolveWithEvidence(definition) || definition.TermsVersionId is not null || definition.Kind!="warranty" && definition.TargetIds.Count>1) return null;
        var target=definition.Kind=="warranty" || definition.TargetIds.Count==0?(Guid?)null:definition.TargetIds[0];
        return (await ServicingEvidenceProjection.RequirementsAsync(db,held,token)).SingleOrDefault(x=>x.Code==definition.RequirementCode && x.RiskItemId==target &&
            parsed.All(p=>x.EffectiveDates.Contains(p.EffectiveAt)));
    }

    private static async Task<bool> ResolutionAuthority(BackOfficeDbContext db,ServicingDecisionContext held,JsonElement grant,DateTimeOffset now,CancellationToken token)
    {
        var proposals=ServicingEvidenceProjection.Slices(held);
        var active=await ActiveConditions(db,held.Cycle.Id,token);
        var conditions=active.SelectMany(c=>ServicingConditionRules.Parse(JsonSerializer.Deserialize<JsonElement>(c.DefinitionJson),proposals,
            JsonSerializer.Deserialize<DateTimeOffset[]>(c.EffectiveDatesJson)!)).GroupBy(x=>x.EffectiveAt)
            .Select(x=>new ServicingConditionSlice(x.Key,x.Select(c=>c.Condition).ToArray())).ToArray();
        var outcome=JsonSerializer.Deserialize<ServicingRatingOutcome>(held.Rating.ResultJson,ServicingRatingService.Json);
        if(outcome?.Rating is not {} rating || rating.Slices.Count!=held.Input.Slices.Count) throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
        var risks=held.Input.Slices.Select((x,i)=>
        {
            if(x.EffectiveAt!=rating.Slices[i].EffectiveAt || !x.ChangeIds.Order().SequenceEqual(rating.Slices[i].ChangeIds.Order())) throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
            return new ServicingAuthoritySlice(x.EffectiveAt,x.Input.RiskForPremium(rating.Slices[i].AnnualPremium));
        }).ToArray();
        return await ServicingCapacityAuthority.Allows(db,held,grant,risks,conditions,now,token);
    }
}
