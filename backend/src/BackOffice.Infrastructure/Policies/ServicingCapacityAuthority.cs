using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal static class ServicingCapacityAuthority
{
    private sealed record CurrentResponse(ServicingCapacityCase Case,ServicingCapacitySubmission Submission,
        ServicingCapacityResponseRecord Record,ServicingReferral Referral,ServicingCapacityResponse Response,ServicingParsedCapacityResponse Parsed);

    internal static async Task<bool> Allows(BackOfficeDbContext db,ServicingDecisionContext held,JsonElement grant,
        IReadOnlyList<ServicingAuthoritySlice> risks,IReadOnlyList<ServicingConditionSlice> internalConditions,DateTimeOffset now,CancellationToken token)
    {
        if (held.Input.IsCommercial) return await AllowsCommercial(db, held, grant, internalConditions, now, token);
        var minimum=held.Input.RatingDefinition.GetProperty("minimumTradingYears").GetInt32();
        // Validate the complete dated schedule even when an earlier slice fails.
        _=ServicingReferralRules.AuthorityAllows(held.Input.Term,risks,held.Scope.Eligible.Binder,grant,internalConditions,minimum);
        if(!UnderwritingConfiguration.WithinBinder(grant,held.Scope.Eligible.Binder)) return false;
        var responses=await Current(db,held,now,token);
        for(var i=0;i<risks.Count;i++)
        {
            var slice=risks[i];var end=i+1<risks.Count?risks[i+1].EffectiveAt:held.Input.Term.EndsAt;
            var carrierConditions=responses.Where(x=>x.Response.ValidFrom<=slice.EffectiveAt && x.Response.ValidTo>=end)
                .SelectMany(x=>x.Parsed.Conditions).SelectMany(x=>x.Slices).Where(x=>x.EffectiveAt==slice.EffectiveAt).Select(x=>x.Condition);
            var conditions=(internalConditions.SingleOrDefault(x=>x.EffectiveAt==slice.EffectiveAt)?.Conditions??[]).Concat(carrierConditions).ToArray();
            var blockers=ReferralRules.AuthorityBlockers(grant,held.Scope.Eligible.Binder,slice.Risk,conditions,minimum);
            foreach(var blocker in blockers)
            {
                var exposure=Exposure(blocker,slice.Risk,slice.EffectiveAt,end);
                if(!responses.Any(x=>ServicingCapacityRules.ExtentApplies(x.Response,x.Response.Subject,x.Case.CurrentResponseId,x.Case.State,
                    x.Referral.RiskItemId,exposure,now))) return false;
            }
        }
        return true;
    }

    private static async Task<bool> AllowsCommercial(BackOfficeDbContext db, ServicingDecisionContext held, JsonElement grant,
        IReadOnlyList<ServicingConditionSlice> conditions, DateTimeOffset now, CancellationToken token)
    {
        var slices = ServicingEvidenceProjection.Slices(held);
        var rating = JsonSerializer.Deserialize<ServicingRatingOutcome>(held.Rating.ResultJson, ServicingRatingService.Json)?.Rating
            ?? throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
        if (rating.Slices.Count != slices.Count) throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
        // Current CC conditions are documentary: none can waive appetite or
        // alter a published numerical limit. Their proof is checked separately.
        if (conditions.Any(x => !slices.Any(s => s.EffectiveAt == x.EffectiveAt) || x.Conditions.Any(c => c.Kind != "documentary"))) return false;
        if (!CommercialUnderwritingConfiguration.WithinBinder(grant, held.Scope.Eligible.Binder)) return false;
        var responses = await Current(db, held, now, token);
        for (var i=0; i<slices.Count; i++)
        {
            var slice=slices[i]; var priced=rating.Slices[i];
            if (slice.EffectiveAt != priced.EffectiveAt) throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
            var end=i+1<slices.Count?slices[i+1].EffectiveAt:held.Input.Term.EndsAt;
            var blockers=CommercialReferralRules.AssessAuthority(grant,slice.Proposal,priced.AnnualPremium)
                .Concat(CommercialReferralRules.AssessAuthority(held.Scope.Eligible.Binder,slice.Proposal,priced.AnnualPremium)).Distinct();
            foreach(var blocker in blockers)
            {
                if (!CommercialCapacityRules.SupportedDimension(blocker.Dimension) || blocker.RequestedAmount is null) return false;
                var exposure=new ServicingCapacityExposure(blocker.Dimension,blocker.TargetId,slice.EffectiveAt,end,blocker.RequestedAmount);
                if (!responses.Any(x=>ServicingCapacityRules.ExtentApplies(x.Response,x.Response.Subject,x.Case.CurrentResponseId,x.Case.State,
                    x.Referral.RiskItemId,exposure,now))) return false;
            }
        }
        return true;
    }

    internal static async Task<bool> HasBlockingRequest(BackOfficeDbContext db,ServicingDecisionContext held,Guid referralId,DateTimeOffset now,CancellationToken token)
    {
        if(!await db.Set<ServicingCapacityCase>().AnyAsync(x=>x.ReferralId==referralId && x.CycleId==held.Cycle.Id,token)) return false;
        var response=(await Current(db,held,now,token)).SingleOrDefault(x=>x.Referral.Id==referralId);
        if(response is null) return true;
        using var required=JsonDocument.Parse(response.Referral.RequiredAuthorityJson);
        var triggers=JsonSerializer.Deserialize<ServicingReferralTrigger[]>(required.RootElement.GetProperty("triggers"),ServicingRatingService.Json)!;
        var rating=JsonSerializer.Deserialize<ServicingRatingOutcome>(held.Rating.ResultJson,ServicingRatingService.Json)?.Rating
            ??throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
        foreach(var trigger in triggers)
        {
            var index=held.Input.Slices.Select((slice,i)=>(slice,i)).Single(x=>x.slice.EffectiveAt==trigger.EffectiveAt).i;
            var slice=held.Input.Slices[index];var end=index+1<held.Input.Slices.Count?held.Input.Slices[index+1].EffectiveAt:held.Input.Term.EndsAt;
            if(rating.Slices[index].EffectiveAt!=slice.EffectiveAt) throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
            var exposure=held.Input.IsCommercial
                ? new ServicingCapacityExposure(trigger.Requirement.Dimension,trigger.Requirement.TargetId,slice.EffectiveAt,end,trigger.Requirement.RequestedAmount)
                : Exposure(trigger.Requirement,slice.Input.RiskForPremium(rating.Slices[index].AnnualPremium),slice.EffectiveAt,end);
            if(!ServicingCapacityRules.ExtentApplies(response.Response,response.Response.Subject,response.Case.CurrentResponseId,response.Case.State,
                response.Referral.RiskItemId,exposure,now)) return true;
        }
        var conditions=await db.Set<ServicingCapacityCondition>().AsNoTracking().Where(x=>x.ResponseId==response.Record.Id).OrderBy(x=>x.Sequence).ToArrayAsync(token);
        foreach(var condition in conditions)
            if(!await ServicingCapacityService.CarrierConditionSatisfied(db,held,condition,token)) return true;
        return false;
    }

    private static ServicingCapacityExposure Exposure(UnderwritingRequirement blocker,UnderwritingRisk risk,DateTimeOffset start,DateTimeOffset end)
    {
        var dimension=CapacityRules.Dimension(blocker.RuleCode,blocker.Dimension);
        var minimum=dimension=="driver-age" && blocker.TargetId is Guid driver?risk.Drivers.Single(x=>x.Id==driver).Age:risk.AnyDriverMinimumAge;
        var maximum=blocker.TargetId is not null?minimum:risk.AnyDriverMaximumAge;
        return new(dimension,blocker.TargetId,start,end,blocker.RequestedAmount,minimum,maximum,dimension=="trade-restriction"?blocker.RuleCode:null);
    }

    private static async Task<IReadOnlyList<CurrentResponse>> Current(BackOfficeDbContext db,ServicingDecisionContext held,DateTimeOffset now,CancellationToken token)
    {
        if(held.Rating.ExpiresAt<=now) return [];
        var rows=await (from c in db.Set<ServicingCapacityCase>().AsNoTracking()
            join s in db.Set<ServicingCapacitySubmission>() on c.CurrentSubmissionId equals s.Id
            join r in db.Set<ServicingCapacityResponseRecord>() on c.CurrentResponseId equals r.Id
            join f in db.Set<ServicingReferral>() on c.ReferralId equals f.Id
            join p in db.Set<CapacityProvider>() on c.ProviderId equals p.Id
            where c.DraftId==held.Scope.Draft.Id && c.CycleId==held.Cycle.Id && c.RevisionId==held.Cycle.RevisionId && c.RatingId==held.Rating.Id &&
                c.BinderVersionId==held.Cycle.BinderVersionId && c.ProviderId==held.Scope.Eligible.BinderVersion.ProviderId && p.State=="active" &&
                (c.State=="approved" || c.State=="conditional") && r.SubmissionId==s.Id && r.ApplicationState=="applied" &&
                (r.Outcome=="approve" || r.Outcome=="approve-with-conditions") && f.State!="declined" && f.State!="superseded"
            select new{Case=c,Submission=s,Record=r,Referral=f}).Take(101).ToArrayAsync(token);
        if(rows.Length>100) throw new QuoteOperationException(409,"servicing-capacity-limit");
        var result=new List<CurrentResponse>();var slices=ServicingEvidenceProjection.Slices(held);
        foreach(var row in rows)
        {
            try { await ServicingCapacityService.RequireSelectedProof(db,row.Submission.Id,token); }
            catch(QuoteOperationException e) when(e.Code=="servicing-capacity-evidence-unavailable") { continue; }
            if(row.Record.Provenance=="supplied-response" && !await (
                from a in db.Set<ServicingEvidenceAssociation>() join e in db.Set<ServicingEvidenceEvent>() on a.LatestReviewId equals e.Id
                join f in db.Set<ServicingEvidenceFile>() on a.FileId equals f.Id
                where a.Id==row.Record.EvidenceAssociationId && a.CapacitySubmissionId==row.Submission.Id && a.WithdrawnEventId==null &&
                    e.Id==row.Record.EvidenceReviewId && e.Kind=="review" && e.Outcome=="accepted" && f.ScreeningState=="accepted"
                select a.Id).AnyAsync(token)) continue;
            var definition=JsonSerializer.Deserialize<ServicingCapacityResponseDefinition>(row.Record.DefinitionJson,ServicingRatingService.Json)!;
            if(definition.ValidFrom is null || definition.ValidTo is null || definition.ValidFrom>now || definition.ValidTo<=now) continue;
            var parseKey=(row.Record.Id,now);
            if(!held.ParsedCapacityResponses.TryGetValue(parseKey,out var parsed))
            {
                parsed=ServicingCapacityResponseRules.Parse(definition,row.Referral.RuleCode,row.Referral.Dimension,row.Submission.SubmittedAt,row.Record.ReceivedAt,now,slices);
                held.ParsedCapacityResponses.Add(parseKey,parsed);
            }
            var subject=new ServicingCapacitySubject(row.Case.DraftId,row.Case.RevisionId,row.Case.CycleId,row.Case.RatingId,row.Case.Id,
                row.Case.ReferralId,row.Case.ProviderId,row.Submission.Id,Convert.ToHexStringLower(row.Submission.ContextHash));
            result.Add(new(row.Case,row.Submission,row.Record,row.Referral,new(row.Record.Id,subject,definition.Outcome,definition.ValidFrom,definition.ValidTo,parsed.Extensions){CommercialExtensions=parsed.CommercialExtensions},parsed));
        }
        return result;
    }
}
