using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingTermsService
{
    private static async Task RenewalReady(BackOfficeDbContext db,ServicingDecisionContext held,DateTimeOffset now,CancellationToken token)
    {
        if(!RenewalLifecycleRules.WithinIssueWindow(now,held.Input.Term.StartsAt))throw new QuoteOperationException(409,"renewal-late-issue-unsupported");
        var renewal=held.Input.Renewal??throw new QuoteOperationException(409,"renewal-preparation-required");
        if(!RenewalPreparationRules.Experience(renewal.Experience,renewal.EvidenceAccepted,now,renewal.ThresholdBasisPoints,renewal.LoadingBasisPoints).InformationComplete)
            throw new QuoteOperationException(409,"renewal-reviewed-experience-required");
        if(renewal.FairValueAssessmentId is not {} id || !await (from assessment in db.Set<FairValueAssessmentVersion>()
            join file in db.Set<ProductEvidenceFileVersion>() on assessment.EvidenceFileVersionId equals file.Id
            where assessment.Id==id && assessment.ProductVersionId==held.Cycle.ProductVersionId && assessment.BinderVersionId==held.Cycle.BinderVersionId &&
                assessment.Outcome=="pass" && assessment.ApprovedAt<=now && assessment.ValidFrom<=held.Input.Term.StartsAt && held.Input.Term.StartsAt<assessment.ValidTo && file.ScreeningState=="accepted"
            select assessment.Id).AnyAsync(token))throw new QuoteOperationException(409,"renewal-fair-value-required");
    }
}
