using System.Globalization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record RenewalPreparationSelection(Guid Id,int Sequence,int TermMonths,ResolvedQuoteTerm Term,Guid ProductVersionId,
    Guid BinderVersionId,Guid AgencyTermsVersionId,Guid RuleSettingVersionId,Guid? FairValueAssessmentId,DateTimeOffset PreparedAt);
public sealed record RenewalPreparationWorkspace(Guid DraftId,string DraftEtag,DateTimeOffset AssessedAt,
    RenewalPreparationSelection? Preparation,RenewalPreparationPreview? Eligibility,IReadOnlyList<int> AllowedTermMonths,
    int? DefaultTermMonths,string ExpiringAnnualPremium,DateTimeOffset ExpiringStartsAt,DateTimeOffset ExpiringEndsAt,
    bool Current,IReadOnlyList<string> Blockers);

public sealed partial class RenewalPreparationService
{
    public async Task<RenewalPreparationWorkspace> ReadPreparationAsync(ActorContext actor,Guid draftId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var quoteId=await (from draftRow in db.Set<ServicingDraft>() join policy in db.Set<Policy>() on draftRow.PolicyId equals policy.Id
            where draftRow.Id==draftId && draftRow.Kind=="renewal" select (Guid?)policy.SourceQuoteId).SingleOrDefaultAsync(token)
            ??throw new QuoteOperationException(404,"renewal-draft-not-found");
        var source=await QuoteScope.ForQuoteAsync(db,actor,quoteId,QuoteAccess.Read,token);
        var draft=await ServicingDraftService.HoldDraft(db,source.Scope.Actor,draftId,false,token);
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==draft.BaseTermId,token);
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==draft.BaseVersionId && x.PolicyId==draft.PolicyId,token);
        using var snapshot=JsonDocument.Parse(basis.SnapshotJson);
        if(!PolicySnapshotShape.Valid(snapshot.RootElement))throw new QuoteOperationException(409,"servicing-base-format-unavailable");
        var annual=decimal.Parse(snapshot.RootElement.GetProperty("premium").GetProperty("annualPremium").GetString()!,CultureInfo.InvariantCulture);
        var preparation=await db.Set<RenewalPreparationVersion>().AsNoTracking().Where(x=>x.DraftId==draftId).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
        RenewalPreparationSelection? selected=null;RenewalPreparationPreview? eligibility=null;
        var blockers=new List<string>();var now=time.GetUtcNow();IReadOnlyList<int> allowed=[];int? defaultMonths=null;
        if(preparation is not null)
        {
            using var intent=JsonDocument.Parse(preparation.TermIntentJson);
            var coverage=QuoteTerm.Assess(intent.RootElement).Term;
            if(coverage is null || coverage.StartsAt!=preparation.StartsAt || coverage.EndsAt!=preparation.EndsAt)
                throw new QuoteOperationException(409,"renewal-term-unavailable");
            selected=new(preparation.Id,preparation.Sequence,preparation.TermMonths,coverage,preparation.ProductVersionId,
                preparation.BinderVersionId,preparation.AgencyTermsVersionId,preparation.RuleSettingVersionId,preparation.FairValueAssessmentId,preparation.CreatedAt);
        }
        else blockers.Add("renewal-preparation-required");
        try
        {
            var held=await ResolveEligibility(db,source,term.Id,preparation?.TermMonths,preparation?.EndUtcOffsetMinutes,now,token);
            allowed=held.Settings.AllowedTermMonths.ToArray();defaultMonths=held.Settings.DefaultTermMonths;
            eligibility=Preview(held);
            if(preparation is not null)await ServicingRatingScope.HoldAsync(db,source.Scope.Actor,draftId,now,token,write:false);
        }
        catch(QuoteOperationException error) when(error.Status is 409 or 422 or 503){blockers.Add(error.Code);}
        if(draft.State!="draft")blockers.Add("renewal-draft-closed");
        var result=new RenewalPreparationWorkspace(draftId,"\""+Convert.ToBase64String(draft.RowVersion)+"\"",now,selected,eligibility,
            allowed,defaultMonths,annual.ToString("0.00",CultureInfo.InvariantCulture),term.StartsAt,term.EndsAt,blockers.Count==0,blockers);
        await tx.CommitAsync(token);return result;
    }

    private static RenewalPreparationPreview Preview(HeldRenewalEligibility held)=>new(held.ExpiringTerm.PolicyId,held.ExpiringTerm.Id,held.Basis.Id,
        "\""+Convert.ToBase64String(held.ExpiringTerm.RowVersion)+"\"",held.Prepared.Term,held.Prepared.Intent,
        held.Eligible.Capture.ProductVersion.Id,held.Eligible.BinderVersion.Id,held.Eligible.Capture.Terms.Id,
        held.Setting.Id,held.Settings.RuleVersion,held.FairValue?.Id,held.FairValue?.EvidenceFileVersionId,held.FairValueSatisfied,
        held.FairValue?.Outcome??"unavailable","unavailable");
}
