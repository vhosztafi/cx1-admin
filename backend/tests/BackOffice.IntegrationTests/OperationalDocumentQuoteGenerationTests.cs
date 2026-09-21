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
    public Task RealSqlOperationalDocumentQuoteGenerationPinsSavedRevisionAndTerms(string product)=>WithDatabase(async(db,password)=>
    {
        var setup=await SignedTerms(db,password,product:product);var f=setup.Fixture;
        var terms=await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==setup.TermsId);
        await VerifyQuoteDocumentGeneration(db,f.Factory,f.Clock,f.Underwriter,terms);
    });

    [Fact]
    public Task RealSqlOperationalDocumentQuoteGenerationCommercial()=>CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var factory=new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(),x=>x.UseCompatibilityLevel(160)).Options);
        var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==actorId);
        var actor=new ActorContext(user.Id,user.TeamId,null,new HashSet<string>{"senior-underwriter"});
        var terms=await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x=>x.CycleId==cycle.Id);
        await VerifyQuoteDocumentGeneration(db,factory,new DocumentTestTime(now),actor,terms);
    },stopAfterAccepted:true);

    private sealed class DocumentTestTime(DateTimeOffset now):TimeProvider { public override DateTimeOffset GetUtcNow()=>now; }

    private static async Task VerifyQuoteDocumentGeneration(BackOfficeDbContext db,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider clock,ActorContext actor,QuoteTermsVersion terms)
    {
        var boundary=new SqlCommandBoundary(factory,clock);
        var store=new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","document-quote",db.Database.GetDbConnection().Database)),[]);
        var sources=new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer());
        var service=new DocumentService(factory,boundary,sources,clock,new FileService(factory,boundary,store,clock));
        var subject=await new TaskService(factory,boundary,clock).Register(actor,new("quote",terms.QuoteId),"quote-document-subject",default);
        var cycle=await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x=>x.Id==terms.CycleId);
        var input=new DocumentGenerateInput("quotation",new("quote-revision",QuoteRevisionId:cycle.QuoteRevisionId,QuoteTermsVersionId:terms.Id),terms.TemplateVersionId,"internal","Render saved approved demonstration quotation");
        var result=await service.Generate(actor,subject.ResourceId,input,"quote-document",default);
        Assert.True((await service.Generate(actor,subject.ResourceId,input,"quote-document",default)).Replayed);
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==result.ResourceId);
        Assert.Equal(cycle.QuoteRevisionId,version.QuoteRevisionId);Assert.Equal(terms.Id,version.QuoteTermsVersionId);Assert.Equal(terms.TermsHash,version.TermsHash);
        Assert.True(await new DocumentGenerationWorker(factory,sources,store,clock).Process(version.WorkId,default));
        await using(var download=await service.DownloadVersion(actor,version.Id,default))
        {using var memory=new MemoryStream();await download.Content.CopyToAsync(memory);Assert.StartsWith("%PDF-",System.Text.Encoding.ASCII.GetString(memory.ToArray(),0,8));}
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Generate(actor,subject.ResourceId,input with{Source=input.Source with{QuoteTermsVersionId=Guid.NewGuid()}},"foreign-terms",default));
        Assert.Equal(1,await db.Set<DocumentVersion>().CountAsync());
        foreach(var retainTerms in new[]{true,false})
        {
            var statement=input with{Kind="statement-of-fact",Source=input.Source with{QuoteTermsVersionId=retainTerms?terms.Id:null}};
            var saved=await service.Generate(actor,subject.ResourceId,statement,"statement-"+retainTerms,default);
            var row=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==saved.ResourceId);
            Assert.Equal(retainTerms?terms.TermsHash:null,row.TermsHash);
            Assert.True(await new DocumentGenerationWorker(factory,sources,store,clock).Process(row.WorkId,default));
            await using var download=await service.DownloadVersion(actor,row.Id,default);
            Assert.True(download.Content.Length>5000);
        }
        Assert.Equal(terms.TermsJson,(await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==terms.Id)).TermsJson);
    }
}
