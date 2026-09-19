using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingAcceptanceInput(Guid CycleId,Guid RatingId,Guid TermsVersionId,Guid DeliveryId,string TermsHash,string AssuranceHash,
    string AccepterLabel,DateTimeOffset AcceptedAt,string Channel,Guid EvidenceAssociationId);

public sealed partial class ServicingTermsService
{
    public Task<CommandOutcome> AcceptAsync(ActorContext actor,Guid draftId,byte[] version,Guid lease,ServicingAcceptanceInput input,
        string key,Guid correlation,CancellationToken token=default)
    {
        if(input is null || new[]{draftId,lease,input.CycleId,input.RatingId,input.TermsVersionId,input.DeliveryId,input.EvidenceAssociationId}.Contains(Guid.Empty) ||
            version is null || version.Length!=8 || !ReferralRules.Hash(input.TermsHash) || !ReferralRules.Hash(input.AssuranceHash) ||
            input.AcceptedAt.Offset!=TimeSpan.Zero || !QuoteTermsRules.ValidAcceptanceIdentity(input.AccepterLabel,input.Channel))
            throw new QuoteOperationException(422,"servicing-acceptance-input-invalid");
        input=input with{AccepterLabel=input.AccepterLabel.Trim()};ServicingDecisionContext? held=null;Guid reviewId=Guid.Empty;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/acceptances",key,correlation),
            new{draftId,version=Convert.ToBase64String(version),lease,input},"servicing.acceptance-recorded",
            async(db,ct)=>
            {
                var now=time.GetUtcNow();held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-draft-write",now,ct,input.CycleId);
                var terms=await CurrentTerms(db,held,input.TermsVersionId,now,ct);
                if(input.RatingId!=held.Rating.Id || input.TermsHash!=terms.TermsHash)throw new QuoteOperationException(409,"servicing-acceptance-stale");
                var delivery=await Delivered(db,held,input.DeliveryId,terms.Id,ct);
                await Ready(db,held,now,true,ct);await CurrentRecipients(db,held,delivery,ct);
                var assurance=await Assurance(db,held,ct);
                var supplied=new ServicingTermsSubject(draftId,input.CycleId,held.Cycle.RevisionId,held.Cycle.BaseVersionId,input.RatingId,input.TermsVersionId,input.TermsHash);
                var current=new ServicingTermsSubject(draftId,held.Cycle.Id,terms.RevisionId,terms.BaseVersionId,terms.RatingId,terms.Id,terms.TermsHash);
                if(!ServicingTermsRules.CanAccept(supplied,current,delivery.Id,held.Cycle.CurrentDeliveryId,delivery.State,delivery.CompletedAt,input.AcceptedAt,now,
                    held.Rating.ExpiresAt,input.AssuranceHash,assurance,input.AccepterLabel,input.Channel))throw new QuoteOperationException(409,"servicing-acceptance-stale");
                reviewId=await AcceptanceProof(db,held,terms.Id,input.EvidenceAssociationId,ct);
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);var now=time.GetUtcNow();
                // System time can move backwards after authorisation (for example
                // Windows time synchronisation). Refuse an impossible chronology
                // before SQL, rather than violating immutable provenance checks.
                if(now<held.AssessedAt || now<input.AcceptedAt || now>=held.Rating.ExpiresAt)
                    throw new QuoteOperationException(409,"servicing-acceptance-stale");
                var row=new ServicingAcceptance{DraftId=draftId,CycleId=held.Cycle.Id,RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,
                    TermsVersionId=input.TermsVersionId,DeliveryId=input.DeliveryId,TermsHash=input.TermsHash,AssuranceHash=input.AssuranceHash,
                    AccepterLabel=input.AccepterLabel,AcceptedAt=input.AcceptedAt,Channel=input.Channel,EvidenceAssociationId=input.EvidenceAssociationId,
                    EvidenceReviewId=reviewId,RecordedBy=actor.UserId,RecordedAt=now,CreatedAt=now,CreatedBy=actor.UserId};
                db.Add(row);await db.SaveChangesAsync(ct);held.Cycle.CurrentAcceptanceId=row.Id;
                return await held.Receipt(db,row.Id,201,now,ct);
            },token);
    }

    private static async Task<ServicingTermsDelivery> Delivered(BackOfficeDbContext db,ServicingDecisionContext held,Guid deliveryId,Guid termsId,CancellationToken token)
    {
        var delivery=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deliveryId && x.DraftId==held.Cycle.DraftId &&
            x.CycleId==held.Cycle.Id && x.TermsVersionId==termsId && x.RatingId==held.Rating.Id,token);
        if(delivery is null || held.Cycle.CurrentDeliveryId!=delivery.Id || delivery.State!="delivered" || delivery.CompletedAt is null)
            throw new QuoteOperationException(409,"servicing-delivery-required");
        return delivery;
    }

    private static async Task CurrentRecipients(BackOfficeDbContext db,ServicingDecisionContext held,ServicingTermsDelivery delivery,CancellationToken token)
    {
        var saved=System.Text.Json.JsonSerializer.Deserialize<ServicingTermsRecipient[]>(delivery.RecipientSnapshotJson,ServicingRatingService.Json)!;
        var current=await Recipients(db,held,saved.Select(x=>x.Id).ToArray(),token);
        if(!saved.SequenceEqual(current))throw new QuoteOperationException(409,"servicing-recipient-stale");
    }

    private static async Task<Guid> AcceptanceProof(BackOfficeDbContext db,ServicingDecisionContext held,Guid termsId,Guid associationId,CancellationToken token)
    {
        var requirement=(await ServicingEvidenceProjection.RequirementsAsync(db,held,token)).SingleOrDefault(x=>x.Code=="acceptance-proof" && x.TermsVersionId==termsId);
        if(requirement is null)throw new QuoteOperationException(409,"servicing-acceptance-proof-required");
        var review=await (from a in db.Set<ServicingEvidenceAssociation>().AsNoTracking()
            join f in db.Set<ServicingEvidenceFile>() on a.FileId equals f.Id join e in db.Set<ServicingEvidenceEvent>() on a.LatestReviewId equals e.Id
            where a.Id==associationId && a.DraftId==held.Cycle.DraftId && a.CycleId==held.Cycle.Id && a.RevisionId==held.Cycle.RevisionId &&
                a.RatingId==held.Rating.Id && a.TermsVersionId==termsId && a.RequirementCode=="acceptance-proof" && a.RiskItemId==null && a.CapacitySubmissionId==null &&
                a.InputFingerprint==requirement.InputFingerprint && a.WithdrawnEventId==null && f.ScreeningState=="accepted" && e.Kind=="review" && e.Outcome=="accepted"
            select (Guid?)e.Id).SingleOrDefaultAsync(token);
        return review??throw new QuoteOperationException(409,"servicing-acceptance-proof-required");
    }

    internal async Task<bool> AcceptanceCurrent(BackOfficeDbContext db,ServicingDecisionContext held,ServicingAcceptance acceptance,string assurance,DateTimeOffset now,CancellationToken token)
    {
        try
        {
            if(held.Cycle.CurrentAcceptanceId!=acceptance.Id || acceptance.AssuranceHash!=assurance)return false;
            var terms=await CurrentTerms(db,held,acceptance.TermsVersionId,now,token);
            var delivery=await Delivered(db,held,acceptance.DeliveryId,terms.Id,token);
            await Ready(db,held,now,true,token);await CurrentRecipients(db,held,delivery,token);
            var saved=new ServicingTermsSubject(acceptance.DraftId,acceptance.CycleId,acceptance.RevisionId,terms.BaseVersionId,acceptance.RatingId,acceptance.TermsVersionId,acceptance.TermsHash);
            var current=new ServicingTermsSubject(held.Cycle.DraftId,held.Cycle.Id,held.Cycle.RevisionId,held.Cycle.BaseVersionId,held.Rating.Id,terms.Id,terms.TermsHash);
            return ServicingTermsRules.CanAccept(saved,current,delivery.Id,held.Cycle.CurrentDeliveryId,delivery.State,delivery.CompletedAt,acceptance.AcceptedAt,now,
                held.Rating.ExpiresAt,acceptance.AssuranceHash,assurance,acceptance.AccepterLabel,acceptance.Channel) &&
                acceptance.EvidenceReviewId==await AcceptanceProof(db,held,terms.Id,acceptance.EvidenceAssociationId,token);
        }
        catch(QuoteOperationException){return false;}
    }
}
