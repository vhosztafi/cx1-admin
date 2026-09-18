using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingTermsService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private readonly SqlCommandBoundary commands=new(factory,time);

    public Task<CommandOutcome> PrepareAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid ratingId,Guid templateId,
        byte[] version,Guid lease,string key,Guid correlation,CancellationToken token=default)
    {
        if(new[]{draftId,cycleId,ratingId,templateId,lease}.Contains(Guid.Empty) || version is null || version.Length!=8)
            throw new QuoteOperationException(400,"servicing-terms-input-invalid");
        ServicingDecisionContext? held=null;TemplateVersion? template=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/terms/prepare",key,correlation),
            new{draftId,cycleId,ratingId,templateId,version=Convert.ToBase64String(version),lease},"servicing.terms-prepared",
            async(db,ct)=>
            {
                var now=time.GetUtcNow();held=await ServicingDecisionContext.Hold(db,actor,draftId,"policy-draft-write",now,ct,cycleId);
                if(held.Rating.Id!=ratingId)throw new QuoteOperationException(412,"servicing-rating-stale");
                template=await Template(db,held,templateId,now,ct);
                // Proof/authority are live even for an exact receipt replay.
                await Ready(db,held,now,false,ct);
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);var now=time.GetUtcNow();
                var payload=await Payload(db,held,template!,ct);var hash=Hash(payload);
                if(held.Cycle.CurrentTermsVersionId is {} current)
                {
                    var prior=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==current,ct);
                    if(prior.TermsHash==hash && prior.TemplateVersionId==templateId)return await held.Receipt(db,prior.Id,201,now,ct);
                }
                var row=new ServicingTermsVersion{DraftId=draftId,CycleId=cycleId,RevisionId=held.Cycle.RevisionId,BaseVersionId=held.Cycle.BaseVersionId,
                    RatingId=ratingId,TemplateVersionId=templateId,Sequence=checked((await db.Set<ServicingTermsVersion>().Where(x=>x.CycleId==cycleId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),
                    TermsJson=payload,TermsHash=hash,AssuranceHashAtPreparation=await Assurance(db,held,ct),PreparedAt=now,PreparedBy=actor.UserId,CreatedAt=now,CreatedBy=actor.UserId};
                db.Add(row);await db.SaveChangesAsync(ct);held.Cycle.CurrentAcceptanceId=null;held.Cycle.CurrentDeliveryId=null;held.Cycle.CurrentTermsVersionId=row.Id;
                return await held.Receipt(db,row.Id,201,now,ct);
            },token);
    }

    internal async Task Ready(BackOfficeDbContext db,ServicingDecisionContext held,DateTimeOffset now,bool signing,CancellationToken token)
    {
        if(held.Scope.Draft.Kind=="renewal")await RenewalReady(db,held,now,token);
        if(held.Rating.ExpiresAt<=now)throw new QuoteOperationException(409,"servicing-rating-expired");
        var requirements=await ServicingEvidenceProjection.RequirementsAsync(db,held,token);
        var proofs=await (from a in db.Set<ServicingEvidenceAssociation>().AsNoTracking()
            join f in db.Set<ServicingEvidenceFile>() on a.FileId equals f.Id
            join e in db.Set<ServicingEvidenceEvent>() on a.LatestReviewId equals e.Id
            where a.DraftId==held.Cycle.DraftId && a.CycleId==held.Cycle.Id && e.Kind=="review"
            select new ServicingReviewedProof(a.DraftId,a.CycleId,a.RevisionId,a.RatingId,a.RequirementCode,a.RiskItemId,a.InputFingerprint,
                f.ScreeningState,e.Outcome!,a.WithdrawnEventId!=null,a.CapacitySubmissionId,a.TermsVersionId)).ToArrayAsync(token);
        if(requirements.Any(r=>r.Code is not("capacity-response" or "acceptance-proof") && (signing || r.Code!="signed-statement") &&
            !proofs.Any(p=>ServicingEvidenceRules.Satisfied(r.Context,r,p))))throw new QuoteOperationException(409,"servicing-proof-review-required");
        if(signing && !requirements.Any(r=>r.Code=="signed-statement" && r.TermsVersionId==held.Cycle.CurrentTermsVersionId &&
            proofs.Any(p=>ServicingEvidenceRules.Satisfied(r.Context,r,p))))throw new QuoteOperationException(409,"servicing-signature-required");
        await new ServicingReferralService(factory,time).RequireTermsReady(db,held,now,signing,token);
    }

    internal static async Task<TemplateVersion> Template(BackOfficeDbContext db,ServicingDecisionContext held,Guid id,DateTimeOffset now,CancellationToken token)
    {
        var row=await db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK) WHERE Id={id} AND ProductId={held.Cycle.ProductId}").AsNoTracking().SingleOrDefaultAsync(token);
        var renewal=held.Scope.Draft.Kind=="renewal";
        if(renewal && row?.Kind!="renewal-invitation")throw new QuoteOperationException(409,"renewal-invitation-required");
        if(row is null || row.Kind!=(renewal?"renewal-invitation":"servicing-terms") || row.State!="published" || row.EffectiveFrom>now || row.EffectiveTo<=now)
            throw new QuoteOperationException(409,"servicing-template-unavailable");
        try
        {
            using var parsed=JsonDocument.Parse(row.ContentJson);var root=parsed.RootElement;
            if(root.EnumerateObject().Count()!=3 || root.GetProperty("format").GetString()!=(renewal?"renewal-template-1":"servicing-template-1") ||
                string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()) || root.GetProperty("title").GetString()!.Length>300 ||
                string.IsNullOrWhiteSpace(root.GetProperty("notice").GetString()) || root.GetProperty("notice").GetString()!.Length>8000)
                throw new QuoteOperationException(503,"servicing-template-invalid");
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException)
        {throw new QuoteOperationException(503,"servicing-template-invalid");}
        return row;
    }

    internal static string Hash(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static async Task<ServicingTermsVersion> CurrentTerms(BackOfficeDbContext db,ServicingDecisionContext held,Guid id,DateTimeOffset now,CancellationToken token)
    {
        var row=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id && x.DraftId==held.Cycle.DraftId && x.CycleId==held.Cycle.Id,token)
            ??throw new QuoteOperationException(404,"servicing-terms-not-found");
        if(held.Cycle.CurrentTermsVersionId!=id || row.RatingId!=held.Rating.Id || row.RevisionId!=held.Cycle.RevisionId || row.BaseVersionId!=held.Cycle.BaseVersionId)
            throw new QuoteOperationException(409,"servicing-terms-stale");
        var template=await Template(db,held,row.TemplateVersionId,now,token);
        if(Hash(await Payload(db,held,template,token))!=row.TermsHash)throw new QuoteOperationException(409,"servicing-terms-stale");
        return row;
    }
}
