using System.Globalization;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingTermsService
{
    internal static async Task<string> Payload(BackOfficeDbContext db,ServicingDecisionContext held,TemplateVersion template,CancellationToken token)
    {
        var internalConditions=await (from c in db.Set<ServicingCondition>().AsNoTracking()
            join r in db.Set<ServicingReferral>() on c.ReferralId equals r.Id
            where c.CycleId==held.Cycle.Id && c.DecisionId==r.LatestDecisionId && r.State=="conditional" && c.Kind!="documentary"
            orderby c.Id select new{c.Id,c.Code,c.Kind,c.DefinitionJson,c.EffectiveDatesJson}).Take(101).ToArrayAsync(token);
        var carrierConditions=await (from c in db.Set<ServicingCapacityCondition>().AsNoTracking()
            join k in db.Set<ServicingCapacityCase>() on c.CaseId equals k.Id
            where c.CycleId==held.Cycle.Id && c.ResponseId==k.CurrentResponseId && c.SubmissionId==k.CurrentSubmissionId && k.State=="conditional" && c.Kind!="documentary"
            orderby c.Id select new{c.Id,c.Code,c.Kind,c.DefinitionJson,c.EffectiveDatesJson}).Take(101).ToArrayAsync(token);
        if(internalConditions.Length+carrierConditions.Length>100)throw new QuoteOperationException(409,"servicing-condition-limit");
        var slices=ServicingEvidenceProjection.Slices(held);
        var rating=held.Rating;var cycle=held.Cycle;
        string Money(decimal value)=>value.ToString("0.00",CultureInfo.InvariantCulture);
        var payload=JsonSerializer.Serialize(new{format=held.Scope.Draft.Kind=="renewal"?"renewal-contract-1":"servicing-contract-1",draftId=cycle.DraftId,cycleId=cycle.Id,revisionId=cycle.RevisionId,
            baseVersionId=cycle.BaseVersionId,baseTermId=cycle.BaseTermId,policyId=cycle.PolicyId,ratingId=rating.Id,templateVersionId=template.Id,
            inputHash=Convert.ToHexStringLower(cycle.InputHash),ratingHash=Convert.ToHexStringLower(rating.ResultHash),
            effectiveDates=slices.Select(x=>x.EffectiveAt).ToArray(),slices=slices.Select(x=>new{x.EffectiveAt,proposal=x.Proposal}).ToArray(),
            template=JsonSerializer.Deserialize<JsonElement>(template.ContentJson),
            ratingInput=JsonSerializer.Deserialize<JsonElement>(cycle.InputJson),rating=JsonSerializer.Deserialize<JsonElement>(rating.ResultJson),
            commercialTerms=JsonSerializer.Deserialize<JsonElement>(held.Scope.Eligible.Capture.Terms.Snapshot),
            conditions=internalConditions.Concat(carrierConditions).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Code,x.Kind,
                definition=JsonSerializer.Deserialize<JsonElement>(x.DefinitionJson),effectiveDates=JsonSerializer.Deserialize<DateTimeOffset[]>(x.EffectiveDatesJson)}).ToArray(),
            expiresAt=rating.ExpiresAt,price=new{currency="GBP",premium=Money(rating.Premium),tax=Money(rating.Tax),fee=Money(rating.Fee),
                brokerCommission=Money(rating.BrokerCommission),grossPayable=Money(rating.GrossPayable),netDue=Money(rating.NetDue)},
            documentState="structured-payload"},ServicingRatingService.Json);
        if(System.Text.Encoding.UTF8.GetByteCount(payload)>8388608)throw new QuoteOperationException(422,"servicing-terms-too-large");
        return payload;
    }

    internal static async Task<string> Assurance(BackOfficeDbContext db,ServicingDecisionContext held,CancellationToken token)
    {
        var cycle=held.Cycle;
        var proof=await (from a in db.Set<ServicingEvidenceAssociation>().AsNoTracking()
            join f in db.Set<ServicingEvidenceFile>() on a.FileId equals f.Id
            where a.CycleId==cycle.Id orderby a.Id
            select new{a.Id,a.FileId,a.InputFingerprint,a.LatestReviewId,a.WithdrawnEventId,a.TermsVersionId,a.CapacitySubmissionId,f.Sha256,f.ScreeningState}).ToArrayAsync(token);
        var referrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).OrderBy(x=>x.Id)
            .Select(x=>new{x.Id,x.State,x.LatestDecisionId,x.RequiredAuthorityJson}).ToArrayAsync(token);
        var resolutions=await db.Set<ServicingConditionResolution>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).OrderBy(x=>x.Id)
            .Select(x=>new{x.Id,x.ConditionId,x.Sequence,x.Outcome,x.AssociationId,x.ReviewId,x.InputFingerprint,x.GrantId}).ToArrayAsync(token);
        var carrier=await db.Set<ServicingCapacityCase>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).OrderBy(x=>x.Id)
            .Select(x=>new{x.Id,x.State,x.CurrentSubmissionId,x.CurrentResponseId}).ToArrayAsync(token);
        var carrierResolutions=await db.Set<ServicingCapacityConditionResolution>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).OrderBy(x=>x.Id)
            .Select(x=>new{x.Id,x.ConditionId,x.Sequence,x.Outcome,x.AssociationId,x.ReviewId,x.InputFingerprint,x.GrantId}).ToArrayAsync(token);
        return Hash(JsonSerializer.Serialize(new{format="servicing-assurance-1",cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.BaseVersionId,
            ratingId=held.Rating.Id,inputHash=Convert.ToHexStringLower(cycle.InputHash),proof,referrals,resolutions,carrier,carrierResolutions},ServicingRatingService.Json));
    }
}
