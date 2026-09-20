using System.Text;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialServicingTermsTemplateIsPublishedAndIdempotent()=>WithDatabase(async(db,_)=>
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        await ServicingTermsSeed.SeedAsync(db);await ServicingTermsSeed.SeedAsync(db);
        var product=await db.Set<Product>().SingleAsync(x=>x.Code=="commercial-combined");
        var template=Assert.Single(await db.Set<TemplateVersion>().Where(x=>x.ProductId==product.Id && x.Kind=="servicing-terms").ToArrayAsync());
        Assert.Equal("published",template.State);Assert.Contains(product.Name,template.ContentJson,StringComparison.Ordinal);
        await tx.CommitAsync();
    });

    private static async Task<(ServicingAcceptance Acceptance,string Etag)> VerifyCommercialServicingTerms(BackOfficeDbContext db,
        IDbContextFactory<BackOfficeDbContext> factory, ActorContext actor, RatingClock clock, ServicingCycle cycle, Guid lease, string etag)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        await using(var tx=await db.Database.BeginTransactionAsync())
        {await ServicingTermsSeed.SeedAsync(db);await tx.CommitAsync();}
        var terms=new ServicingTermsService(factory,clock);var evidence=new ServicingEvidenceService(factory,clock);
        var view=await terms.ReadAsync(actor,cycle.DraftId);
        Assert.True(view.CanPrepare,view.BlockingCode);var template=Assert.Single(view.Templates);
        var ratingId=(await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==cycle.Id)).CurrentRatingId!.Value;
        var prepared=await terms.PrepareAsync(actor,cycle.DraftId,cycle.Id,ratingId,template.Id,Version(etag),lease,Key(),Guid.NewGuid());
        etag=prepared.Etag!;
        var contract=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==prepared.ResourceId);
        async Task<Guid> Proof(string code)
        {
            var requirement=Assert.Single((await evidence.RequirementsAsync(actor,cycle.DraftId)).Requirements,x=>x.Requirement.Code==code).Requirement;
            var upload=await evidence.UploadAsync(actor,cycle.DraftId,Version(etag),lease,"fictional-"+code+".txt","text/plain",
                Encoding.UTF8.GetBytes("Fictional commercial policy adjustment acknowledgement: "+code),Key(),Guid.NewGuid());
            var attach=await evidence.AttachAsync(actor,cycle.DraftId,cycle.Id,Version(upload.Etag!),lease,upload.ResourceId,code,null,
                requirement.InputFingerprint,"Attach exact fictional commercial acceptance proof",Key(),Guid.NewGuid());
            var row=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attach.ResourceId);
            var review=await evidence.ReviewAsync(actor,cycle.DraftId,cycle.Id,row.Id,Version(attach.Etag!),lease,row.RowVersion,"accepted",
                requirement.InputFingerprint,"Review exact fictional commercial acceptance proof",Key(),Guid.NewGuid());
            etag=review.Etag!;return row.Id;
        }
        await Proof("signed-statement");
        view=await terms.ReadAsync(actor,cycle.DraftId);Assert.True(view.CanSend,view.SendBlockingCode);
        var sent=await terms.SendAsync(actor,cycle.DraftId,cycle.Id,contract.Id,[view.RecipientOptions.First().Id],Version(etag),lease,Key(),Guid.NewGuid());
        var delivery=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleAsync(x=>x.Id==sent.ResourceId);
        var job=Assert.IsType<JobLease>(await new SqlJobLeases(factory,clock).ClaimWorkAsync(ServicingTermsService.WorkKind,delivery.WorkId));
        var worker=new ServicingDeliveryWorker(factory,clock);Assert.True(await worker.ApplyAsync(job,await worker.ExecuteProviderAsync(job)));
        etag="\""+Convert.ToBase64String(await db.Set<ServicingDraft>().AsNoTracking().Where(x=>x.Id==cycle.DraftId).Select(x=>x.RowVersion).SingleAsync())+"\"";
        var proof=await Proof("acceptance-proof");view=await terms.ReadAsync(actor,cycle.DraftId);
        var input=new ServicingAcceptanceInput(cycle.Id,ratingId,contract.Id,delivery.Id,contract.TermsHash,view.AssuranceHash,
            "Fictional commercial customer",clock.GetUtcNow(),"email",proof);
        foreach(var bad in new[]{input with {TermsHash=new string('f',64)},input with {AssuranceHash=new string('b',64)},input with {EvidenceAssociationId=Guid.NewGuid()}})
            await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.AcceptAsync(actor,cycle.DraftId,Version(etag),lease,bad,Key(),Guid.NewGuid()));
        var key=Key();var accepted=await terms.AcceptAsync(actor,cycle.DraftId,Version(etag),lease,input,key,Guid.NewGuid());
        Assert.Equal(201,accepted.Status);Assert.True((await terms.AcceptAsync(actor,cycle.DraftId,Version(etag),lease,input,key,Guid.NewGuid())).Replayed);
        Assert.True((await terms.ReadAsync(actor,cycle.DraftId)).AcceptanceApplicable);
        return(await db.Set<ServicingAcceptance>().AsNoTracking().SingleAsync(x=>x.Id==accepted.ResourceId),accepted.Etag!);
    }
}
