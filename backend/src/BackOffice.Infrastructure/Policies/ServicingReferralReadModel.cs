using BackOffice.Application;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingConditionView(Guid Id,string Code,string Kind,bool Satisfied,string Etag);
public sealed record ServicingReferralView(Guid Id,int Sequence,string RuleCode,string Dimension,Guid? RiskItemId,string State,
    string Etag,Guid? DecisionId,bool DecisionReady,IReadOnlyList<ServicingConditionView> Conditions);
public sealed record ServicingReferralPage(Guid DraftId,Guid CycleId,string DraftEtag,bool Applicable,IReadOnlyList<ServicingReferralView> Items,int? NextAfterSequence);

public sealed partial class ServicingReferralService
{
    public async Task<ServicingReferralPage> ReadReferralsAsync(ActorContext actor,Guid draftId,int afterSequence=0,int pageSize=50,CancellationToken token=default)
    {
        if(afterSequence<0 || pageSize is <1 or >50) throw new QuoteOperationException(422,"servicing-referral-page-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var now=time.GetUtcNow();var held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-read",now,token);
        var rows=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.DraftId==draftId && x.CycleId==held.Cycle.Id && x.Sequence>afterSequence)
            .OrderBy(x=>x.Sequence).Take(pageSize+1).ToArrayAsync(token);
        var page=rows.Take(pageSize).ToArray();var decisionIds=page.Where(x=>x.LatestDecisionId is not null).Select(x=>x.LatestDecisionId!.Value).ToArray();
        var conditions=await db.Set<ServicingCondition>().AsNoTracking().Where(x=>x.DraftId==draftId && x.CycleId==held.Cycle.Id && decisionIds.Contains(x.DecisionId))
            .OrderBy(x=>x.Sequence).Take(101).ToArrayAsync(token);
        if(conditions.Length>100) throw new QuoteOperationException(409,"servicing-condition-limit");
        var applicable=held.Rating.ExpiresAt>now;var items=new List<ServicingReferralView>();
        foreach(var row in page)
        {
            var conditionViews=new List<ServicingConditionView>();
            foreach(var condition in conditions.Where(x=>x.DecisionId==row.LatestDecisionId))
                conditionViews.Add(new(condition.Id,condition.Code,condition.Kind,applicable && await ConditionSatisfied(db,held,condition,token),Etag(condition.RowVersion)));
            // This is referral-decision readiness only, not overall issue
            // readiness. Base proof, capacity, terms and acceptance are separate.
            var ready=applicable && (row.State=="approved" || row.State=="conditional" && conditionViews.Count>0 && conditionViews.All(x=>x.Satisfied));
            if(ready && row.LatestDecisionId is {} decisionId)
            {
                var decision=await db.Set<ServicingReferralDecision>().AsNoTracking().SingleAsync(x=>x.Id==decisionId,token);
                var grant=await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x=>x.Id==decision.GrantId,token);
                var authority=await db.Set<AuthorityVersion>().AsNoTracking().SingleAsync(x=>x.Id==decision.AuthorityVersionId,token);
                ready=grant.RevokedAt is null && grant.EffectiveFrom<=now && grant.EffectiveTo>now &&
                    grant.EffectiveFrom<=held.Input.Slices[0].EffectiveAt && grant.EffectiveTo>=held.Input.Term.EndsAt &&
                    authority.State=="published" && authority.EffectiveFrom<=now && authority.EffectiveTo>now &&
                    authority.EffectiveFrom<=held.Input.Slices[0].EffectiveAt && authority.EffectiveTo>=held.Input.Term.EndsAt;
                if(ready) ready=await ResolutionAuthority(db,held,JsonSerializer.Deserialize<JsonElement>(authority.DefinitionJson),token);
                if(ready && row.RuleCode=="UW-22") ready=await TradingHistorySatisfied(db,held,token);
            }
            else ready=false;
            items.Add(new(row.Id,row.Sequence,row.RuleCode,row.Dimension,row.RiskItemId,row.State,Etag(row.RowVersion),row.LatestDecisionId,ready,conditionViews));
        }
        var result=new ServicingReferralPage(draftId,held.Cycle.Id,Etag(held.Scope.Draft.RowVersion),applicable,items,rows.Length>pageSize?page[^1].Sequence:null);
        await tx.CommitAsync(token);return result;
    }

    private static string Etag(byte[] version)=>"\""+Convert.ToBase64String(version)+"\"";

    private async Task<bool> TradingHistorySatisfied(BackOfficeDbContext db,ServicingDecisionContext held,CancellationToken token)
    {
        if(held.Rating.ExpiresAt<=time.GetUtcNow()) return false;
        var requirement=(await ServicingEvidenceProjection.RequirementsAsync(db,held,token))
            .SingleOrDefault(x=>x.Code=="trading-history" && x.RiskItemId is null);
        if(requirement is null) return false;
        // Exact current ownership and full-schedule fingerprint; only the
        // latest accepted review of a screened, non-withdrawn file counts.
        return await (from association in db.Set<ServicingEvidenceAssociation>().AsNoTracking()
            join file in db.Set<ServicingEvidenceFile>() on association.FileId equals file.Id
            join review in db.Set<ServicingEvidenceEvent>() on association.LatestReviewId equals review.Id
            where association.DraftId==held.Scope.Draft.Id && association.CycleId==held.Cycle.Id &&
                association.RevisionId==held.Scope.Revision.Id && association.RatingId==held.Rating.Id &&
                association.RequirementCode==requirement.Code && association.RiskItemId==null &&
                association.InputFingerprint==requirement.InputFingerprint && association.WithdrawnEventId==null &&
                file.ScreeningState=="accepted" && review.Kind=="review" && review.Outcome=="accepted"
            select association.Id).AnyAsync(token);
    }
}
