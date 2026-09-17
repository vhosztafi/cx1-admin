using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Infrastructure.Policies;

internal static class ServicingEvidenceProjection
{
    internal static IReadOnlyList<ServicingProofRequirement> Requirements(ServicingDecisionContext held)
        =>ServicingEvidenceRules.Requirements(new(held.Scope.Draft.Id,held.Cycle.Id,held.Scope.Revision.Id,held.Rating.Id,Convert.ToHexStringLower(held.Cycle.InputHash),
            held.Scope.Eligible.Capture.Pins),Slices(held));

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
