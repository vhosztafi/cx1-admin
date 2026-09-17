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

public sealed class ServicingReferralService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private readonly SqlCommandBoundary commands=new(factory,time);

    public Task<CommandOutcome> DecideAsync(ActorContext actor,Guid draftId,Guid cycleId,byte[] version,Guid lease,
        IReadOnlyList<ReferralDecisionInput> decisions,string key,Guid correlation,CancellationToken token=default)
    {
        if(version.Length!=8 || lease==Guid.Empty || cycleId==Guid.Empty || decisions.Count is <1 or >50 ||
            decisions.Select(x=>x.ReferralId).Distinct().Count()!=decisions.Count || decisions.Any(x=>x.ReferralId==Guid.Empty || x.Version.Length!=8 ||
                x.Outcome is not("approve" or "approve-with-conditions" or "query" or "decline" or "reopen"))) throw new QuoteOperationException(422,"servicing-decision-invalid");
        var normalized=decisions.Select(x=>x with{Reason=QuoteRatingService.Reason(x.Reason),Question=x.Question is null?null:QuoteRatingService.Reason(x.Question),
            Conditions=x.Conditions.Select(c=>JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(c))).ToArray()}).ToArray();
        foreach(var item in normalized)
            if((item.Outcome is "approve-with-conditions" or "query"?item.Conditions.Count is <1 or >20:item.Conditions.Count!=0) ||
                (item.Outcome=="query")!=(item.Question is not null)) throw new QuoteOperationException(422,"servicing-decision-conditions-required");
        ServicingDecisionContext? held=null;
        var selected=new Dictionary<Guid,(ServicingReferral Row,EffectiveUnderwritingGrant Grant,IReadOnlyList<ServicingParsedCondition>[] Conditions)>();
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/referral-decisions",key,correlation),
            new{draftId,cycleId,version=Convert.ToBase64String(version),lease,decisions=normalized},"servicing.referrals-decided",
            async(db,ct)=>
            {
                var now=time.GetUtcNow();held=await ServicingDecisionContext.Hold(db,actor,draftId,"underwriting-decide-within-authority",now,ct,cycleId);
                var remaining=held.Input.Term with{Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt};
                var grants=await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,held.Cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
                    held.Scope.Eligible.Capture.Product.Code,remaining,now,ct);
                if(grants.Count==0) throw new QuoteOperationException(403,"servicing-decision-authority-required");
                var proposals=ServicingEvidenceProjection.Slices(held);
                var outcome=JsonSerializer.Deserialize<ServicingRatingOutcome>(held.Rating.ResultJson,ServicingRatingService.Json);
                if(outcome?.Rating is not {} price || price.Slices.Count!=held.Input.Slices.Count) throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
                var risks=held.Input.Slices.Select((x,i)=>
                {
                    if(x.EffectiveAt!=price.Slices[i].EffectiveAt || !x.ChangeIds.Order().SequenceEqual(price.Slices[i].ChangeIds.Order())) throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
                    return new ServicingAuthoritySlice(x.EffectiveAt,x.Input.RiskForPremium(price.Slices[i].AnnualPremium));
                }).ToArray();
                var active=await ActiveConditions(db,cycleId,ct);
                var retained=active.Where(x=>!normalized.Any(n=>n.ReferralId==x.ReferralId)).ToArray();
                if(retained.Length+normalized.Sum(x=>x.Conditions.Count)>100) throw new QuoteOperationException(422,"servicing-condition-limit");
                var retainedParsed=retained.SelectMany(x=>ServicingConditionRules.Parse(JsonSerializer.Deserialize<JsonElement>(x.DefinitionJson),proposals,
                    JsonSerializer.Deserialize<DateTimeOffset[]>(x.EffectiveDatesJson)!)).ToArray();
                var newConditions=normalized.ToDictionary(x=>x.ReferralId,x=>x.Conditions.Select(c=>Project(c,proposals)).ToArray());
                var all=retainedParsed.Concat(newConditions.Values.SelectMany(x=>x).SelectMany(x=>x)).GroupBy(x=>x.EffectiveAt)
                    .Select(x=>new ServicingConditionSlice(x.Key,x.Select(c=>c.Condition).ToArray())).ToArray();
                foreach(var item in normalized.OrderBy(x=>x.ReferralId))
                {
                    var row=await db.Set<ServicingReferral>().FromSqlInterpolated($"SELECT * FROM ServicingReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={item.ReferralId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                        ??throw new QuoteOperationException(404,"servicing-referral-not-found");
                    var parsed=newConditions[item.ReferralId];
                    if(item.Outcome=="query" && parsed.Any(x=>x[0].Condition.Kind!="documentary")) throw new QuoteOperationException(422,"servicing-query-documentary-required");
                    if(item.Conditions.Where((value,index)=>item.Conditions.Take(index).Any(previous=>JsonElement.DeepEquals(previous,value))).Any())
                        throw new QuoteOperationException(422,"servicing-condition-duplicate");
                    var approval=item.Outcome is "approve" or "approve-with-conditions";
                    var revise=parsed.Any(x=>x[0].Condition.Kind=="risk-change");
                    var grant=grants.FirstOrDefault(g=>!approval || revise || ServicingReferralRules.AuthorityAllows(held.Input.Term,risks,held.Scope.Eligible.Binder,g.Definition,all,
                        held.Input.RatingDefinition.GetProperty("minimumTradingYears").GetInt32())) ??throw new QuoteOperationException(403,"servicing-dimension-authority-required");
                    selected.Add(row.Id,(row,grant,parsed));
                }
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);var now=time.GetUtcNow();
                var active=await ActiveConditions(db,cycleId,ct);
                foreach(var item in normalized)
                {
                    var row=selected[item.ReferralId].Row;
                    if(!CryptographicOperations.FixedTimeEquals(row.RowVersion,item.Version)) throw new QuoteOperationException(412,"servicing-referral-stale");
                    if(row.State=="superseded" || row.State=="declined" && item.Outcome!="reopen") throw new QuoteOperationException(409,"servicing-referral-reopen-required");
                    // Until condition resolution commands and live proof checks
                    // are wired, an outstanding condition cannot become approval.
                    if(item.Outcome=="approve" && active.Any(x=>x.ReferralId==row.Id)) throw new QuoteOperationException(409,"servicing-condition-outstanding");
                    if(item.Outcome=="approve" && row.RuleCode=="UW-22") throw new QuoteOperationException(409,"servicing-trading-history-review-required");
                }
                Guid first=Guid.Empty;
                foreach(var item in normalized)
                {
                    var chosen=selected[item.ReferralId];
                    var decision=new ServicingReferralDecision{DraftId=draftId,CycleId=cycleId,RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,ReferralId=item.ReferralId,
                        Sequence=checked((await db.Set<ServicingReferralDecision>().Where(x=>x.ReferralId==item.ReferralId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),Outcome=item.Outcome,
                        Reason=item.Reason,Question=item.Question,ConditionsJson=JsonSerializer.Serialize(item.Conditions),ActorId=actor.UserId,AuthorityVersionId=chosen.Grant.Version.Id,
                        GrantId=chosen.Grant.Grant.Id,DecidedAt=now,CreatedBy=actor.UserId,CreatedAt=now};
                    db.Add(decision);await db.SaveChangesAsync(ct);if(first==Guid.Empty)first=decision.Id;
                    var sequence=0;
                    foreach(var condition in chosen.Conditions)
                        db.Add(new ServicingCondition{DraftId=draftId,CycleId=cycleId,RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,ReferralId=item.ReferralId,DecisionId=decision.Id,
                            Sequence=++sequence,Code=condition[0].Condition.Code,Kind=condition[0].Condition.Kind,DefinitionJson=condition[0].Condition.DefinitionJson,
                            EffectiveDatesJson=JsonSerializer.Serialize(condition.Select(x=>x.EffectiveAt)),CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now});
                    await db.SaveChangesAsync(ct);chosen.Row.LatestDecisionId=decision.Id;chosen.Row.UpdatedAt=now;
                    chosen.Row.State=item.Outcome switch{"approve"=>"approved","approve-with-conditions"=>"conditional","query"=>"queried","decline"=>"declined",_=>"open"};
                }
                return await held.Receipt(db,first,200,now,ct);
            },token);
    }

    private static Task<ServicingCondition[]> ActiveConditions(BackOfficeDbContext db,Guid cycle,CancellationToken token)=>
        (from c in db.Set<ServicingCondition>().AsNoTracking() join r in db.Set<ServicingReferral>() on c.ReferralId equals r.Id
         where c.CycleId==cycle && c.DecisionId==r.LatestDecisionId && r.State!="superseded" select c).ToArrayAsync(token);

    private static IReadOnlyList<ServicingParsedCondition> Project(JsonElement definition,IReadOnlyList<ServicingEvidenceSlice> proposals)
    {
        ReferralCondition? sample=null;
        foreach(var proposal in proposals)
            try{sample=ReferralRules.Condition(definition,proposal.Proposal);break;}catch(ArgumentException){}
        if(sample is null || sample.TermsVersionId is not null) throw new QuoteOperationException(422,"servicing-condition-invalid");
        var collection=sample.Code switch{"provide-driver-proof" or "named-drivers-only"=>"drivers","revise-vehicle-limit"=>"vehicles",_=>"premises"};
        var dates=proposals.Where(x=>sample.TargetIds.Count==0 || x.Proposal.GetProperty("risk").TryGetProperty(collection,out var items) &&
            sample.TargetIds.All(id=>items.EnumerateArray().Any(item=>item.GetProperty("id").GetGuid()==id))).Select(x=>x.EffectiveAt).ToArray();
        try{return ServicingConditionRules.Parse(definition,proposals,dates);}catch(ArgumentException){throw new QuoteOperationException(422,"servicing-condition-invalid");}
    }
}
