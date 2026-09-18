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
        var requirements=ServicingEvidenceRules.Requirements(context,slices,requested.ToArray()).ToList();
        var warranty=ServicingWarrantyRules.Requirement(context,slices,conditions.Where(x=>x.Kind=="warranty")
            .Select(x=>new ServicingWarrantyInput(x.Id,JsonSerializer.Deserialize<JsonElement>(x.DefinitionJson),JsonSerializer.Deserialize<DateTimeOffset[]>(x.EffectiveDatesJson)!)).ToArray());
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
    {
        var scope=held.Scope;var input=held.Input;
        if (Convert.ToHexStringLower(scope.Base.ContentHash)!=input.BaseContentHash || Convert.ToHexStringLower(scope.Revision.ContentHash)!=input.RevisionContentHash)
            throw new QuoteOperationException(409,"servicing-proof-source-stale");
        // Reconstruct at the retained rating instant. Current authority is held
        // separately; time passing must not change the approved risk projection.
        var assessment=ServicingProposalRules.Assess(scope.Base.SnapshotJson,scope.Revision.ProposalJson,
            new(scope.Draft.PolicyId,scope.Draft.BaseVersionId,scope.Term.StartsAt,scope.Term.EndsAt,scope.Base.EffectiveAt,input.RequestedAt,true));
        if (assessment.ReadinessIssues.Count!=0 || assessment.Slices.Count!=input.Slices.Count)
            throw new QuoteOperationException(409,"servicing-proof-source-stale");
        var slices=new List<ServicingEvidenceSlice>();
        for(var i=0;i<assessment.Slices.Count;i++)
        {
            var actual=assessment.Slices[i];var saved=input.Slices[i];
            var projected=QuoteUnderwritingInput.Project(actual.Proposed,input.RatingDefinition);
            if (actual.EffectiveAt!=saved.EffectiveAt || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(projected,ServicingRatingService.Json),JsonSerializer.SerializeToNode(saved.Input,ServicingRatingService.Json)))
                throw new QuoteOperationException(409,"servicing-proof-source-stale");
            slices.Add(new(actual.EffectiveAt,actual.Proposed,projected.TradingYears));
        }
        return slices;
    }
}
