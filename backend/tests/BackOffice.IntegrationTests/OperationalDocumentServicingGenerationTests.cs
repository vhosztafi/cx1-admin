using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public Task RealSqlOperationalDocumentServicingGenerationRenewal(string product)=>VerifyRenewalLifecycle(product,false,false,async(db,f,cycle,terms)=>
        await VerifyServicingDocumentGeneration(db,f.Factory,f.Clock,f.Underwriter,terms,"renewal-invitation"));

    [Fact]
    public Task RealSqlOperationalDocumentServicingGenerationAdjustment()=>RunServicingRatingRequests("motor-trade-road-risks","terms-prepare",onPrepared:async(db,f,cycle,terms)=>
        await VerifyServicingDocumentGeneration(db,f.Factory,f.Clock,f.Underwriter,terms,"quotation"));

    [Fact]
    public Task RealSqlOperationalDocumentServicingGenerationCommercialRenewal()=>CommercialRenewalScenario(true,onAccepted:async(db,factory,actor,cycle,terms)=>
        await VerifyServicingDocumentGeneration(db,factory,new DocumentTestTime(terms.PreparedAt),actor,terms,"renewal-invitation"));

    private static async Task VerifyServicingDocumentGeneration(BackOfficeDbContext db,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider clock,
        ActorContext actor,ServicingTermsVersion terms,string kind)
    {
        var boundary=new SqlCommandBoundary(factory,clock);
        var store=new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","document-servicing",db.Database.GetDbConnection().Database)),[]);
        var sources=new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer());
        var service=new DocumentService(factory,boundary,sources,clock,new FileService(factory,boundary,store,clock));
        var subject=await new TaskService(factory,boundary,clock).Register(actor,new("servicing-draft",terms.DraftId),"servicing-document-subject",default);
        var input=new DocumentGenerateInput(kind,new("servicing-terms",TermsVersionId:terms.Id),terms.TemplateVersionId,"internal","Render exact saved servicing terms");
        var generated=await service.Generate(actor,subject.ResourceId,input,"servicing-document",default);
        Assert.True((await service.Generate(actor,subject.ResourceId,input,"servicing-document",default)).Replayed);
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==generated.ResourceId);
        Assert.Equal(terms.Id,version.ServicingTermsVersionId);Assert.Equal(terms.TermsHash,version.SourceHash);
        Assert.True(await new DocumentGenerationWorker(factory,sources,store,clock).Process(version.WorkId,default));
        await using(var download=await service.DownloadVersion(actor,version.Id,default))
        {using var memory=new MemoryStream();await download.Content.CopyToAsync(memory);Assert.StartsWith("%PDF-",System.Text.Encoding.ASCII.GetString(memory.ToArray(),0,8));}
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Generate(actor,subject.ResourceId,input with{Source=new("servicing-terms",TermsVersionId:Guid.NewGuid())},"foreign-servicing-terms",default));
        Assert.Equal(1,await db.Set<DocumentVersion>().CountAsync());
        Assert.Equal(terms.TermsJson,(await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==terms.Id)).TermsJson);
    }
}
