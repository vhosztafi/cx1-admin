using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class RenewalPreparationService
{
    private static Task<bool> IsCommercial(BackOfficeDbContext db,ServicingDraft draft,CancellationToken token)=>
        (from policy in db.Set<Policy>() join product in db.Set<Product>() on policy.ProductId equals product.Id
         where policy.Id==draft.PolicyId&&product.Code==CommercialCaptureRules.ProductCode select policy.Id).AnyAsync(token);

    internal async Task<CommercialRenewalSubjects?> CurrentCommercialSubjects(BackOfficeDbContext db,ActorContext actor,ServicingDraft draft,CancellationToken token)
    {
        if(!await IsCommercial(db,draft,token))return null;
        var preparation=await db.Set<RenewalPreparationVersion>().AsNoTracking().Where(x=>x.DraftId==draft.Id).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
        if(preparation is null)return null;
        var revision=await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x=>x.Id==draft.CurrentRevisionId&&x.DraftId==draft.Id,token);
        var assessment=await new ServicingDraftService(factory,time).Assess(db,actor,draft,ServicingProposalInput.Parse(revision.ProposalJson,draft.BaseVersionId),token);
        if(assessment.ReadinessIssues.Count!=0)throw new QuoteOperationException(409,"commercial-renewal-experience-risk-unavailable");
        try{return CommercialRenewalExperienceRules.Subjects(draft.BaseVersionId,revision.Id,assessment.Proposed,
            new(preparation.ProductVersionId,preparation.AgencyTermsVersionId,"1.0",CommercialCaptureRules.QuestionVersion,CommercialCaptureRules.ReferenceVersion));}
        catch(ArgumentException){throw new QuoteOperationException(409,"commercial-renewal-experience-risk-unavailable");}
    }

    internal async Task DemandCommercialExperience(BackOfficeDbContext db,ActorContext actor,ServicingDraft draft,RenewalExperienceVersion experience,CancellationToken token)
    {
        if(!await IsCommercial(db,draft,token))
        {
            if(experience.CommercialRevisionId is not null||experience.CommercialSubjectsJson is not null)
                throw new QuoteOperationException(409,"renewal-experience-product-mismatch");
            return;
        }
        CommercialRenewalSubjects? stored;
        try{stored=experience.CommercialSubjectsJson is null?null:JsonSerializer.Deserialize<CommercialRenewalSubjects>(experience.CommercialSubjectsJson,Json);}
        catch(JsonException){throw new QuoteOperationException(409,"commercial-renewal-experience-stale");}
        var current=await CurrentCommercialSubjects(db,actor,draft,token);
        if(current is null||experience.CommercialRevisionId!=draft.CurrentRevisionId||!CommercialRenewalExperienceRules.Matches(stored,current))
            throw new QuoteOperationException(409,"commercial-renewal-experience-stale");
    }
}
