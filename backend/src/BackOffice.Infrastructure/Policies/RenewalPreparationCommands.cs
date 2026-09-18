using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class RenewalPreparationService
{
    public Task<CommandOutcome> PrepareAsync(ActorContext actor,Guid draftId,byte[] version,Guid leaseToken,int months,int? endOffsetMinutes,
        string key,Guid correlation,CancellationToken token=default)
        =>Mutate(actor,draftId,version,leaseToken,"preparation",new{months,endOffsetMinutes},key,correlation,async(db,draft,current,ct)=>
        {
            var quoteId=await db.Set<Policy>().Where(x=>x.Id==draft.PolicyId).Select(x=>x.SourceQuoteId).SingleAsync(ct);
            var source=await QuoteScope.ForQuoteAsync(db,current,quoteId,QuoteAccess.Read,ct);
            var held=await ResolveEligibility(db,source,draft.BaseTermId,months,endOffsetMinutes,time.GetUtcNow(),ct);
            if(held.Basis.Id!=draft.BaseVersionId)throw new QuoteOperationException(409,"renewal-base-stale");
            var row=new RenewalPreparationVersion{DraftId=draftId,PolicyId=draft.PolicyId,BaseTermId=draft.BaseTermId,BaseVersionId=draft.BaseVersionId,
                Sequence=checked((await db.Set<RenewalPreparationVersion>().Where(x=>x.DraftId==draftId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),
                TermMonths=months,EndUtcOffsetMinutes=endOffsetMinutes,StartsAt=held.Prepared.Term.StartsAt,EndsAt=held.Prepared.Term.EndsAt,
                TermIntentJson=held.Prepared.Intent.GetRawText(),ProductId=held.Eligible.Capture.Product.Id,ProductVersionId=held.Eligible.Capture.ProductVersion.Id,
                BinderVersionId=held.Eligible.BinderVersion.Id,AgencyTermsVersionId=held.Eligible.Capture.Terms.Id,RuleSettingVersionId=held.Setting.Id,
                FairValueAssessmentId=held.FairValue?.Id,CreatedBy=current.UserId,CreatedAt=time.GetUtcNow()};
            var revision=await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x=>x.Id==draft.CurrentRevisionId && x.DraftId==draftId,ct);
            var proposal=JsonNode.Parse(revision.ProposalJson)!.AsObject();var intent=held.Prepared.Intent;
            proposal["commonEffectiveIntent"]=JsonSerializer.SerializeToNode(new{localDate=intent.GetProperty("localStartDate").GetString(),
                localTime=intent.GetProperty("localStartTime").GetString(),timeZone="Europe/London",utcOffsetMinutes=intent.GetProperty("utcOffsetMinutes").GetInt32()});
            var canonical=ServicingProposalInput.Parse(proposal.ToJsonString(),draft.BaseVersionId);
            await ServicingRatingService.InvalidateAsync(db,draft,"Renewal preparation choices changed",time.GetUtcNow(),ct);
            await new ServicingDraftService(factory,time).Append(db,draft,canonical,current.UserId,ct);
            db.Add(row);return row.Id;
        },token,capability:"quote-rate");
}
