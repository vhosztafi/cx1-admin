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
    [Fact]
    public Task RealSqlOperationalDocumentExplicitRegenerationPreservesEarlierFileAndVersion()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var request=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Kind=="policy-schedule");
        var root=Path.GetFullPath(Path.Combine(".local","document-regeneration",db.Database.GetDbConnection().Database));
        var store=new OperationalFileStore(root,[]);var boundary=new SqlCommandBoundary(f.Factory,f.Clock);
        var sources=new PolicyDocumentRenderService(f.Factory,new PolicyDocumentRenderer());var files=new FileService(f.Factory,boundary,store,f.Clock);
        var service=new DocumentService(f.Factory,boundary,sources,f.Clock,files);var worker=new DocumentGenerationWorker(f.Factory,sources,store,f.Clock);
        var registered=await service.RegisterRetainedRequest(f.Underwriter,request.Id,"register-original",default);
        await worker.Process(request.WorkId,default);
        var first=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync();var head=await db.Set<OperationalDocument>().AsNoTracking().SingleAsync();
        byte[] original;
        await using(var download=await service.DownloadVersion(f.Underwriter,first.Id,default)){using var memory=new MemoryStream();await download.Content.CopyToAsync(memory);original=memory.ToArray();}
        var originalTemplate=await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x=>x.Id==request.TemplateVersionId);
        var replacement=new TemplateVersion{ProductId=originalTemplate.ProductId,Code="test-document-regeneration",Version=1,Kind=originalTemplate.Kind,
            EffectiveFrom=f.Clock.GetUtcNow().AddDays(-1),EffectiveTo=f.Clock.GetUtcNow().AddYears(1),ContentJson=System.Text.Json.JsonSerializer.Serialize(new{
                format="document-template-1",productCode="motor-trade-road-risks",kind="policy-schedule",title="Reissued demonstration schedule",notice="New template, same explicitly retained policy source."})};
        db.Add(replacement);await db.SaveChangesAsync();
        var input=new DocumentGenerateInput("policy-schedule",new("policy-version",PolicyVersionId:request.VersionId),replacement.Id,"internal","Regenerate an explicitly selected saved source",DocumentId:head.Id);
        var generated=await service.Generate(f.Underwriter,head.SubjectId,input,"explicit-regeneration",default);
        Assert.True((await service.Generate(f.Underwriter,head.SubjectId,input,"explicit-regeneration",default)).Replayed);
        var second=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==generated.ResourceId);Assert.Equal(2,second.Number);Assert.Equal(head.Id,second.DocumentId);
        Assert.NotEqual(first.WorkId,second.WorkId);Assert.Null(second.PolicyDocumentRequestId);
        await worker.Process(second.WorkId,default);
        Assert.Equal(replacement.Id,second.TemplateVersionId);Assert.NotEqual(first.TemplateHash,second.TemplateHash);
        await using(var download=await service.DownloadVersion(f.Underwriter,second.Id,default)){using var memory=new MemoryStream();await download.Content.CopyToAsync(memory);Assert.False(original.SequenceEqual(memory.ToArray()));}
        await using(var download=await service.DownloadVersion(f.Underwriter,first.Id,default)){using var memory=new MemoryStream();await download.Content.CopyToAsync(memory);Assert.Equal(original,memory.ToArray());}
        Assert.Equal(2,await db.Set<DocumentVersionContent>().CountAsync());
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Generate(f.Underwriter,head.SubjectId,input with{DocumentId=Guid.NewGuid()},"foreign-document",default));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Generate(f.Underwriter,head.SubjectId,input with{Source=new("policy-version",PolicyVersionId:Guid.NewGuid())},"foreign-source",default));
        await Assert.ThrowsAsync<DocumentRuleException>(()=>service.Generate(f.Underwriter,head.SubjectId,input with{Visibility="insurer"},"changed-audience",default));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TemplateVersion SET State='retired' WHERE Id={replacement.Id}");
        Assert.True((await service.Generate(f.Underwriter,head.SubjectId,input,"explicit-regeneration",default)).Replayed);
        await Assert.ThrowsAsync<DocumentRuleException>(()=>service.Generate(f.Underwriter,head.SubjectId,input,"retired-template-new-command",default));
        Assert.Equal(2,await db.Set<DocumentVersion>().CountAsync());
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);user.State="suspended";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Generate(f.Underwriter,head.SubjectId,input,"explicit-regeneration",default));
        Assert.Equal(request.PayloadJson,(await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Id==request.Id)).PayloadJson);
    });
}
