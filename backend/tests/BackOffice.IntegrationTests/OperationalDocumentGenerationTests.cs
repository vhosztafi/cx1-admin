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
    [InlineData(DocumentGenerationFault.AfterRender)]
    [InlineData(DocumentGenerationFault.AfterContentCommit)]
    [InlineData(DocumentGenerationFault.AfterFinalize)]
    public Task RealSqlOperationalDocumentGenerationRecoversContentCommitAndKeepsOriginalBytes(DocumentGenerationFault crash) => WithDatabase(async (db,password)=>
    {
        var setup=await AcceptedIssue(db,password); var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var request=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Kind=="policy-schedule");
        var root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"CoverMGA_DocumentGeneration",Guid.NewGuid().ToString("N")));
        var store=new OperationalFileStore(root,[]);
        var renderer=new PolicyDocumentRenderService(f.Factory,new PolicyDocumentRenderer());
        var boundary=new SqlCommandBoundary(f.Factory,f.Clock);
        var service=new DocumentService(f.Factory,boundary,renderer,f.Clock,new FileService(f.Factory,boundary,store,f.Clock));
        var result=await service.RegisterRetainedRequest(f.Underwriter,request.Id,"register-document",default);
        Assert.True((await service.RegisterRetainedRequest(f.Underwriter,request.Id,"register-document",default)).Replayed);
        Assert.Equal(result.ResourceId,(await service.RegisterRetainedRequest(f.Underwriter,request.Id,"same-original-document",default)).ResourceId);
        var worker=new DocumentGenerationWorker(f.Factory,renderer,store,f.Clock,point=>{if(point==crash)throw new IOException("Simulated process loss at "+crash);});
        await Assert.ThrowsAsync<IOException>(()=>worker.Process(request.WorkId,default));
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync();
        var before=await db.Set<DocumentVersionContent>().AsNoTracking().SingleOrDefaultAsync();
        if(crash==DocumentGenerationFault.AfterRender) Assert.Null(before); else Assert.NotNull(before);
        Assert.Equal(result.ResourceId,version.Id);
        worker=new DocumentGenerationWorker(f.Factory,renderer,store,f.Clock);
        var recovered=await Task.WhenAll(worker.Process(request.WorkId,default),worker.Process(request.WorkId,default));
        Assert.Contains(true,recovered);
        var binding=await db.Set<DocumentVersionContent>().AsNoTracking().SingleAsync();
        if(before is not null) Assert.Equal(before.FileObjectId,binding.FileObjectId);
        var file=await db.Set<FileObject>().AsNoTracking().SingleAsync(x=>x.Id==binding.FileObjectId);
        Assert.Equal("ready",file.State);
        await using var first=await store.OpenReady(file.Id,file.ByteLength,file.Sha256,default);
        using var saved=new MemoryStream(); await first.CopyToAsync(saved);
        Assert.StartsWith("%PDF-",System.Text.Encoding.ASCII.GetString(saved.ToArray()[..8]));
        Assert.False(await worker.Process(request.WorkId,default));
        Assert.Equal(1,await db.Set<DocumentVersion>().CountAsync()); Assert.Equal(1,await db.Set<DocumentVersionContent>().CountAsync());
        Assert.Equal(binding.FileObjectId,(await db.Set<DocumentVersionContent>().SingleAsync()).FileObjectId);
        Assert.Equal("succeeded",(await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==request.WorkId)).State);
        Assert.Equal(request.PayloadJson,(await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Id==request.Id)).PayloadJson);
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);user.State="suspended";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.RegisterRetainedRequest(f.Underwriter,request.Id,"register-document",default));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>worker.Process(request.WorkId,default));
    });
}
