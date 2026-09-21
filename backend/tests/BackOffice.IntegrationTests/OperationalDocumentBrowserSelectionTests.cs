using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public Task RealSqlOperationalDocumentBrowserSelectionPinsOwnedSourceAndApplicableTemplates(string product)=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await VerifyDocumentSelections(db,f.Factory,f.Clock,f.Underwriter,f.QuoteId,setup.Input.AcceptanceId,product);
    });

    [Fact]
    public Task RealSqlOperationalDocumentBrowserSelectionCommercial()=>CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);
        await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await VerifyDocumentSelections(db,source.Factory,new DocumentTestTime(now),source.Actor,source.Quote.Id,acceptance,"commercial-combined");
    },stopAfterAccepted:true);

    private static async Task VerifyDocumentSelections(BackOfficeDbContext db,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider clock,ActorContext actor,
        Guid quoteId,Guid acceptanceId,string product)
    {
        await using(var tx=await db.Database.BeginTransactionAsync()){await DocumentTemplateSeed.SeedAsync(db);await tx.CommitAsync();}
        var boundary=new SqlCommandBoundary(factory,clock);var store=new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","document-options",db.Database.GetDbConnection().Database)),[]);
        var service=new DocumentService(factory,boundary,new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer()),clock,new FileService(factory,boundary,store,clock));
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var subject=await new TaskService(factory,boundary,clock).Register(actor,new("policy",version.PolicyId),"options-policy-subject",default);
        var source=new DocumentSourceInput("policy-version",PolicyVersionId:version.Id);
        var choices=new List<DocumentGenerationChoice>();Guid? after=null;var pages=0;
        do
        {
            var page=await service.GenerationOptions(actor,subject.ResourceId,source,after,1,clock.GetUtcNow(),default);
            Assert.Equal(product,page.ProductCode);Assert.Equal(version.Id,page.SourceVersionId);Assert.Equal(version.EffectiveAt,page.SourceDate);
            Assert.Contains("version",page.SourceLabel,StringComparison.OrdinalIgnoreCase);
            choices.AddRange(page.Items);after=page.NextTemplateId;Assert.True(++pages<30);
        }while(after is not null);
        Assert.Contains(choices,c=>c.Kind=="policy-schedule");Assert.Contains(choices,c=>c.Kind=="policy-certificate");
        Assert.Contains(choices,c=>c.Kind=="statement-of-fact");Assert.Contains(choices,c=>c.Kind=="endorsement");
        Assert.DoesNotContain(choices,c=>c.Kind=="cancellation-notice");
        Assert.Equal(choices.Count,choices.Select(c=>(c.Kind,c.TemplateVersionId)).Distinct().Count());
        if(product=="commercial-combined")Assert.All(choices,c=>Assert.DoesNotContain("motor",c.TemplateLabel,StringComparison.OrdinalIgnoreCase));
        var selected=choices.First(c=>c.Kind=="policy-schedule");
        var generated=await service.Generate(actor,subject.ResourceId,new(selected.Kind,selected.Source,selected.TemplateVersionId,"internal","Generate the selected option"),"generate-selected-option",default);
        Assert.Equal(version.Id,(await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(v=>v.Id==generated.ResourceId)).PolicyVersionId);
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.GenerationOptions(actor,subject.ResourceId,source with{PolicyVersionId=Guid.NewGuid()},null,25,clock.GetUtcNow(),default));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.GenerationOptions(actor,subject.ResourceId,source,Guid.NewGuid(),25,clock.GetUtcNow(),default));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TemplateVersion SET State='retired' WHERE Id={selected.TemplateVersionId}");
        Assert.DoesNotContain((await service.GenerationOptions(actor,subject.ResourceId,source,null,100,clock.GetUtcNow(),default)).Items,c=>c.TemplateVersionId==selected.TemplateVersionId);
        var quote=await db.Set<Quote>().AsNoTracking().SingleAsync(q=>q.Id==quoteId);
        var accepted=await db.Set<QuoteAcceptance>().AsNoTracking().SingleAsync(a=>a.Id==acceptanceId);
        var quoteSubject=await new TaskService(factory,boundary,clock).Register(actor,new("quote",quoteId),"options-quote-subject",default);
        var quoteSource=new DocumentSourceInput("quote-revision",QuoteRevisionId:quote.CurrentRevisionId,QuoteTermsVersionId:accepted.TermsVersionId);
        var quotation=await service.GenerationOptions(actor,quoteSubject.ResourceId,quoteSource,null,100,clock.GetUtcNow(),default);
        Assert.Contains(quotation.Items,c=>c.Kind=="quotation");Assert.Contains(quotation.Items,c=>c.Kind=="statement-of-fact");
        Assert.All(quotation.Items,c=>Assert.Equal(accepted.TermsVersionId,c.Source.QuoteTermsVersionId));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.GenerationOptions(actor,quoteSubject.ResourceId,quoteSource with{QuoteTermsVersionId=Guid.NewGuid()},null,100,clock.GetUtcNow(),default));
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==actor.UserId);user.State="suspended";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.GenerationOptions(actor,subject.ResourceId,source,null,25,clock.GetUtcNow(),default));
    }
}
