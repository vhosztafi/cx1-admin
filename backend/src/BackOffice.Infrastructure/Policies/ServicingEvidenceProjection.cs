using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal static class ServicingEvidenceProjection
{
    internal static async Task<IReadOnlyList<ServicingProofRequirement>> RequirementsAsync(BackOfficeDbContext db,ServicingDecisionContext held,CancellationToken token)
    {
        var slices=Slices(held);
        var conditions=await (from c in db.Set<ServicingCondition>().AsNoTracking() join r in db.Set<ServicingReferral>() on c.ReferralId equals r.Id
            where c.DraftId==held.Scope.Draft.Id && c.CycleId==held.Cycle.Id && c.DecisionId==r.LatestDecisionId &&
                (r.State=="conditional" || r.State=="queried") && (c.Code=="provide-trading-history" || c.Kind=="warranty")
            select new {c.Id,c.Code,c.Kind,c.DefinitionJson,c.EffectiveDatesJson}).Take(101).ToArrayAsync(token);
        var carrier=await (from c in db.Set<ServicingCapacityCondition>().AsNoTracking()
            join k in db.Set<ServicingCapacityCase>() on c.CaseId equals k.Id
            where c.DraftId==held.Scope.Draft.Id && c.CycleId==held.Cycle.Id && c.ResponseId==k.CurrentResponseId &&
                k.State=="conditional" && c.SubmissionId==k.CurrentSubmissionId && (c.Code=="provide-trading-history" || c.Kind=="warranty")
            select new {c.Id,c.Code,c.Kind,c.DefinitionJson,c.EffectiveDatesJson}).Take(101).ToArrayAsync(token);
        conditions=conditions.Concat(carrier).ToArray();
        if(conditions.Length>100) throw new QuoteOperationException(409,"servicing-condition-limit");
        var requested=new SortedSet<DateTimeOffset>();
        foreach(var condition in conditions.Where(x=>x.Code=="provide-trading-history"))
        {
            var parsed=ServicingConditionRules.Parse(JsonSerializer.Deserialize<JsonElement>(condition.DefinitionJson),slices,
                JsonSerializer.Deserialize<DateTimeOffset[]>(condition.EffectiveDatesJson)!);
            foreach(var row in parsed) requested.Add(row.EffectiveAt);
        }
        var context=new ServicingProofContext(held.Scope.Draft.Id,held.Cycle.Id,held.Scope.Revision.Id,held.Rating.Id,Convert.ToHexStringLower(held.Cycle.InputHash),held.Scope.Eligible.Capture.Pins);
        var core=held.Input.IsCommercial && requested.Count==0
            ? held.CommercialBaseProofs??=ServicingEvidenceRules.Requirements(context,slices)
            : ServicingEvidenceRules.Requirements(context,slices,requested.ToArray());
        var requirements=core.ToList();
        var warranty=conditions.Any(x=>x.Kind=="warranty")?ServicingWarrantyRules.Requirement(context,slices,conditions.Where(x=>x.Kind=="warranty")
            .Select(x=>new ServicingWarrantyInput(x.Id,JsonSerializer.Deserialize<JsonElement>(x.DefinitionJson),JsonSerializer.Deserialize<DateTimeOffset[]>(x.EffectiveDatesJson)!)).ToArray()):null;
        if(warranty is not null) requirements.Add(warranty);
        var submissions=await (from k in db.Set<ServicingCapacityCase>().AsNoTracking()
            join s in db.Set<ServicingCapacitySubmission>().AsNoTracking() on k.CurrentSubmissionId equals s.Id
            where k.DraftId==held.Scope.Draft.Id && k.CycleId==held.Cycle.Id && k.State!="draft" && k.State!="superseded"
            select new {s.Id,s.ContextHash}).Take(101).ToArrayAsync(token);
        if(submissions.Length>100) throw new QuoteOperationException(409,"servicing-capacity-purpose-limit");
        foreach(var submission in submissions)
            requirements.Add(ServicingCapacityProofRules.Requirement(context,submission.Id,Convert.ToHexStringLower(submission.ContextHash),
                slices.Select(x=>x.EffectiveAt).ToArray()));
        if(held.Cycle.CurrentTermsVersionId is {} termsId)
        {
            ServicingTermsVersion? terms=null;
            try {terms=await ServicingTermsService.CurrentTerms(db,held,termsId,held.AssessedAt,token);}
            catch(QuoteOperationException e) when(e.Code is "servicing-terms-stale" or "servicing-template-unavailable") { }
            if(terms is not null)
                foreach(var code in new[]{"signed-statement","acceptance-proof"})
                    requirements.Add(ServicingTermsProofRules.Requirement(context,new(terms.DraftId,terms.CycleId,terms.RevisionId,terms.BaseVersionId,
                        terms.RatingId,terms.Id,terms.TermsHash),code,slices.Select(x=>x.EffectiveAt).ToArray()));
        }
        return requirements;
    }

    internal static IReadOnlyList<ServicingEvidenceSlice> Slices(ServicingDecisionContext held)
        => held.EvidenceSlices??=ProjectSlices(held);

    private static IReadOnlyList<ServicingEvidenceSlice> ProjectSlices(ServicingDecisionContext held)
    {
        var scope=held.Scope;var input=held.Input;
        if (Convert.ToHexStringLower(scope.Base.ContentHash)!=input.BaseContentHash || Convert.ToHexStringLower(scope.Revision.ContentHash)!=input.RevisionContentHash)
            throw new QuoteOperationException(409,"servicing-proof-source-stale");
        // Reconstruct at the retained rating instant. Current authority is held
        // separately; time passing must not change the approved risk projection.
        var context = input.Renewal is null
            ? new ServicingProposalContext(scope.Draft.PolicyId,scope.Draft.BaseVersionId,scope.Term.StartsAt,scope.Term.EndsAt,scope.Base.EffectiveAt,input.RequestedAt,true)
            : new ServicingProposalContext(scope.Draft.PolicyId,scope.Draft.BaseVersionId,input.Term.StartsAt,input.Term.EndsAt,input.Term.StartsAt,input.RequestedAt,false,input.Term);
        var assessment=ServicingProposalRules.Assess(scope.Base.SnapshotJson,scope.Revision.ProposalJson,context);
        if (input.Renewal is not null)
        {
            if (assessment.ReadinessIssues.Count != 0 || input.Slices.Count != 1 || assessment.Slices.Any(x=>x.EffectiveAt!=input.Term.StartsAt))
                throw new QuoteOperationException(409,"servicing-proof-source-stale");
            if(input.IsCommercial)
            {
                var commercial=CommercialUnderwritingInput.Project(assessment.Proposed,scope.Eligible.Capture.Pins,input.RatingDefinition,DateOnly.FromDateTime(input.RequestedAt.UtcDateTime));
                if(!assessment.Slices.SelectMany(x=>x.ChangeIds).Order().SequenceEqual(input.Slices[0].ChangeIds.Order())||input.Slices[0].Input is not null||
                    !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(commercial,ServicingRatingService.Json),JsonSerializer.SerializeToNode(input.Slices[0].Commercial,ServicingRatingService.Json)))
                    throw new QuoteOperationException(409,"servicing-proof-source-stale");
                return [new(input.Term.StartsAt,assessment.Proposed,null)];
            }
            var projection=QuoteUnderwritingInput.Project(assessment.Proposed,input.RatingDefinition);
            if (!assessment.Slices.SelectMany(x=>x.ChangeIds).Order().SequenceEqual(input.Slices[0].ChangeIds.Order()) ||
                !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(projection,ServicingRatingService.Json),JsonSerializer.SerializeToNode(input.Slices[0].Input,ServicingRatingService.Json)))
                throw new QuoteOperationException(409,"servicing-proof-source-stale");
            return [new(input.Term.StartsAt,assessment.Proposed,projection.TradingYears)];
        }
        if (assessment.ReadinessIssues.Count!=0 || assessment.Slices.Count!=input.Slices.Count)
            throw new QuoteOperationException(409,"servicing-proof-source-stale");
        var slices=new List<ServicingEvidenceSlice>();
        for(var i=0;i<assessment.Slices.Count;i++)
        {
            var actual=assessment.Slices[i];var saved=input.Slices[i];
            if (input.IsCommercial)
            {
                var commercial = CommercialUnderwritingInput.Project(actual.Proposed, scope.Eligible.Capture.Pins, input.RatingDefinition,
                    DateOnly.FromDateTime(input.RequestedAt.UtcDateTime));
                if (actual.EffectiveAt != saved.EffectiveAt || saved.Input is not null ||
                    !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(commercial, ServicingRatingService.Json), JsonSerializer.SerializeToNode(saved.Commercial, ServicingRatingService.Json)))
                    throw new QuoteOperationException(409, "servicing-proof-source-stale");
                slices.Add(new(actual.EffectiveAt, actual.Proposed, null));
                continue;
            }
            var projected=QuoteUnderwritingInput.Project(actual.Proposed,input.RatingDefinition);
            if (actual.EffectiveAt!=saved.EffectiveAt || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(projected,ServicingRatingService.Json),JsonSerializer.SerializeToNode(saved.Input,ServicingRatingService.Json)))
                throw new QuoteOperationException(409,"servicing-proof-source-stale");
            slices.Add(new(actual.EffectiveAt,actual.Proposed,projected.TradingYears));
        }
        return slices;
    }
}
