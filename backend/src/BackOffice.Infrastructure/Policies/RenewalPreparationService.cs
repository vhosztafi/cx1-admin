using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record RenewalExperienceView(Guid DraftId,string Etag,RenewalExperienceVersion? Experience,RenewalExperienceReview? Review);

public sealed class RenewalPreparationService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private readonly SqlCommandBoundary commands=new(factory,time);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    public async Task<RenewalExperienceView> ReadExperienceAsync(ActorContext actor,Guid draftId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var draft=await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);Renewal(draft);
        var experience=await db.Set<RenewalExperienceVersion>().AsNoTracking().Where(x=>x.DraftId==draftId).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
        var review=experience is null?null:await db.Set<RenewalExperienceReview>().AsNoTracking().Where(x=>x.ExperienceVersionId==experience.Id && x.DraftId==draftId)
            .OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
        await tx.CommitAsync(token);return new(draftId,Etag(draft),experience,review);
    }

    public Task<CommandOutcome> UploadExperienceAsync(ActorContext actor,Guid draftId,byte[] version,Guid leaseToken,
        string fileName,string contentType,byte[] content,string key,Guid correlation,CancellationToken token=default)
    {
        var file=QuoteEvidenceRules.File(fileName,contentType,content,maximumNameLength:200);
        return Mutate(actor,draftId,version,leaseToken,"experience/uploads",new{file.FileName,file.ContentType,file.Sha256,length=file.Content.Length},
            key,correlation,async(db,draft,current,ct)=>
            {
                var now=time.GetUtcNow();var stored=new ServicingEvidenceFile{DraftId=draftId,FileName=file.FileName,ContentType=file.ContentType,
                    Content=file.Content,ByteLength=file.Content.Length,Sha256=file.Sha256,CreatedBy=current.UserId,CreatedAt=now};
                db.Add(stored);await db.SaveChangesAsync(ct);
                var association=new RenewalExperienceEvidence{DraftId=draftId,FileId=stored.Id,CreatedBy=current.UserId,CreatedAt=now};
                db.Add(association);return association.Id;
            },token);
    }

    public Task<CommandOutcome> SaveExperienceAsync(ActorContext actor,Guid draftId,byte[] version,Guid leaseToken,
        RenewalExperienceFacts input,string key,Guid correlation,CancellationToken token=default)
    {
        try{RenewalPreparationRules.ValidateExperience(input,time.GetUtcNow());}
        catch(ArgumentException){throw new QuoteOperationException(422,"renewal-experience-invalid");}
        return Mutate(actor,draftId,version,leaseToken,"experience",input,key,correlation,async(db,draft,current,ct)=>
        {
            if(!await db.Set<RenewalExperienceEvidence>().AnyAsync(x=>x.Id==input.EvidenceAssociationId && x.DraftId==draftId,ct))
                throw new QuoteOperationException(404,"renewal-experience-evidence-not-found");
            var sequence=checked((await db.Set<RenewalExperienceVersion>().Where(x=>x.DraftId==draftId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1);
            var row=new RenewalExperienceVersion{DraftId=draftId,Sequence=sequence,ObservationStartsOn=input.ObservationStartsOn,
                ObservationEndsOn=input.ObservationEndsOn,ClaimCount=input.ClaimCount,Paid=input.Paid,Outstanding=input.Outstanding,
                EarnedPremium=input.EarnedPremium,SourceCode=input.SourceCode,SourceReference=input.SourceReference,
                EvidenceAssociationId=input.EvidenceAssociationId,CreatedAt=time.GetUtcNow(),CreatedBy=current.UserId};
            await ServicingRatingService.InvalidateAsync(db,draft,"Supplied renewal experience changed",time.GetUtcNow(),ct);
            db.Add(row);return row.Id;
        },token);
    }

    public Task<CommandOutcome> ReviewExperienceAsync(ActorContext actor,Guid draftId,Guid experienceId,byte[] version,Guid leaseToken,
        string outcome,string reason,string key,Guid correlation,CancellationToken token=default)
    {
        if(experienceId==Guid.Empty || outcome is not("accepted" or "rejected") || string.IsNullOrWhiteSpace(reason) ||
            reason.Trim().Length<10 || reason.Length>2000 || reason.Any(char.IsControl))throw new QuoteOperationException(422,"renewal-experience-review-invalid");
        reason=reason.Trim();EffectiveUnderwritingGrant? grant=null;RenewalExperienceVersion? experience=null;
        return Mutate(actor,draftId,version,leaseToken,$"experience/{experienceId:D}/reviews",new{experienceId,outcome,reason},key,correlation,
            async(db,draft,current,ct)=>
            {
                if(await db.Set<RenewalExperienceVersion>().AnyAsync(x=>x.DraftId==draftId && x.Sequence>experience!.Sequence,ct))
                    throw new QuoteOperationException(412,"renewal-experience-version-conflict");
                var row=new RenewalExperienceReview{DraftId=draftId,ExperienceVersionId=experienceId,
                    Sequence=checked((await db.Set<RenewalExperienceReview>().Where(x=>x.ExperienceVersionId==experienceId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),
                    Outcome=outcome,Reason=reason,AuthorityVersionId=grant!.Version.Id,AuthorityGrantId=grant.Grant.Id,CreatedBy=current.UserId,CreatedAt=time.GetUtcNow()};
                await ServicingRatingService.InvalidateAsync(db,draft,"Renewal experience evidence review changed",time.GetUtcNow(),ct);
                db.Add(row);return row.Id;
            },token,async(db,source,ct)=>
            {
                experience=await db.Set<RenewalExperienceVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==experienceId && x.DraftId==draftId,ct)
                    ??throw new QuoteOperationException(404,"renewal-experience-not-found");
                var term=await (from d in db.Set<ServicingDraft>() join t in db.Set<PolicyTerm>() on d.BaseTermId equals t.Id where d.Id==draftId select t).SingleAsync(ct);
                var product=await db.Set<Product>().AsNoTracking().SingleAsync(x=>x.Id==term.ProductId,ct);
                // This review checks evidence about the expiring insured risk;
                // approval to price or issue the future term is a separate grant.
                using var intent=JsonDocument.Parse(term.LocalTermIntentJson);
                var resolved=QuoteTerm.Assess(intent.RootElement).Term??throw new QuoteOperationException(409,"servicing-term-unavailable");
                var authorities=await db.Set<AuthorityVersion>().FromSqlInterpolated($"SELECT * FROM AuthorityVersion WITH(HOLDLOCK) WHERE ProductVersionId={term.ProductVersionId}").AsNoTracking().ToArrayAsync(ct);
                foreach(var binderId in authorities.Select(x=>x.BinderVersionId).Distinct())
                {
                    var binder=await db.Set<BinderVersion>().FromSqlInterpolated($"SELECT * FROM BinderVersion WITH(HOLDLOCK) WHERE Id={binderId}").AsNoTracking().SingleAsync(ct);
                    grant=(await QuoteUnderwritingScope.GrantsAsync(db,source,term.ProductVersionId,binder,product.Code,resolved,time.GetUtcNow(),ct)).FirstOrDefault();
                    if(grant is not null)break;
                }
                if(grant is null)throw new QuoteOperationException(403,"renewal-experience-review-authority-required");
            },"underwriting-evidence-review");
    }

    private Task<CommandOutcome> Mutate<T>(ActorContext actor,Guid draftId,byte[] version,Guid leaseToken,string action,T input,
        string key,Guid correlation,Func<BackOfficeDbContext,ServicingDraft,ActorContext,CancellationToken,Task<Guid>> handler,CancellationToken token,
        Func<BackOfficeDbContext,OwnedQuoteScope,CancellationToken,Task>? authorize=null,string capability="underwriting-evidence-write")
    {
        if(version.Length!=8 || leaseToken==Guid.Empty)throw new QuoteOperationException(400,"renewal-command-invalid");
        ServicingDraft? held=null;ActorContext? current=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/renewal/{action}",key,correlation),
            new{draftId,version=Convert.ToBase64String(version),leaseToken,input},"renewal."+action.Replace('/','-'),
            async(db,ct)=>
            {
                var quoteId=await (from d in db.Set<ServicingDraft>() join p in db.Set<Policy>() on d.PolicyId equals p.Id
                    where d.Id==draftId select (Guid?)p.SourceQuoteId).SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-draft-not-found");
                var source=await QuoteUnderwritingScope.HoldAsync(db,actor,quoteId,capability,ct);
                current=source.Scope.Actor;held=await ServicingDraftService.HoldDraft(db,current,draftId,true,ct);Renewal(held);
                if(authorize is not null)await authorize(db,source,ct);
            },async(db,ct)=>
            {
                if(held!.State!="draft")throw new QuoteOperationException(409,"servicing-draft-closed");
                if(!held.RowVersion.SequenceEqual(version))throw new QuoteOperationException(412,"servicing-version-conflict");
                await new ServicingDraftService(factory,time).DemandLease(db,draftId,current!.UserId,leaseToken,ct);
                var resource=await handler(db,held,current,ct);
                held.UpdatedAt=time.GetUtcNow();db.Entry(held).Property(x=>x.UpdatedAt).IsModified=true;await db.SaveChangesAsync(ct);
                return new(resource,201,JsonSerializer.Serialize(new{draftId,resourceId=resource,held.Kind,held.UpdatedAt},Json),Etag:Etag(held));
            },token);
    }

    private static void Renewal(ServicingDraft draft)
    {if(draft.Kind!="renewal")throw new QuoteOperationException(409,"renewal-draft-required");}
    private static string Etag(ServicingDraft draft)=>"\""+Convert.ToBase64String(draft.RowVersion)+"\"";
}
