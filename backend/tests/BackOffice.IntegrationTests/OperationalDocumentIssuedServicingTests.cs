using System.Text;
using BackOffice.Application;
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
    [InlineData("motor-trade-road-risks",false)]
    [InlineData("motor-trade-combined",false)]
    [InlineData("motor-trade-road-risks",true)]
    [InlineData("motor-trade-combined",true)]
    public Task RealSqlOperationalDocumentIssuedServicing(string product,bool renewal)=>renewal
        ?VerifyRenewalLifecycle(product,false,false,onAccepted:IssueForDocumentVerification)
        :RunServicingRatingRequests(product,"terms-prepare",onAccepted:IssueForDocumentVerification);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RealSqlOperationalDocumentIssuedCommercialServicing(bool renewal)=>renewal
        ?CommercialRenewalScenario(true,onIssued:VerifyIssuedDocumentRequests)
        :CommercialServicingIssueScenario(true,onIssued:VerifyIssuedDocumentRequests);

    private static async Task IssueForDocumentVerification(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,
        ServicingAcceptance acceptance,Guid fence,string etag)
    {
        var input=new ServicingIssueInput(cycle.Id,acceptance.RatingId,acceptance.TermsVersionId,acceptance.Id,
            acceptance.TermsHash,acceptance.AssuranceHash,"Issue fictional cover for exact document provenance verification");
        var issued=await new ServicingIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,cycle.DraftId,
            Convert.FromBase64String(etag.Trim('"')),fence,input,Guid.NewGuid().ToString(),Guid.NewGuid());
        Assert.Equal(201,issued.Status);
        await VerifyIssuedDocumentRequests(db,f.Factory,f.Underwriter,f.Clock);
    }

    private static async Task VerifyIssuedDocumentRequests(BackOfficeDbContext db,IDbContextFactory<BackOfficeDbContext> factory,ActorContext actor,TimeProvider clock)
    {
        var requests=await db.Set<PolicyDocumentRequest>().AsNoTracking().OrderBy(r=>r.CreatedAt).ThenBy(r=>r.Id).ToArrayAsync();
        Assert.True(requests.Length>=6);Assert.True(requests.Select(r=>r.VersionId).Distinct().Count()>=2);
        var boundary=new SqlCommandBoundary(factory,clock);
        var store=new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","document-issued-servicing",db.Database.GetDbConnection().Database)),[]);
        var sources=new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer());
        var service=new DocumentService(factory,boundary,sources,clock,new FileService(factory,boundary,store,clock));
        var worker=new DocumentGenerationWorker(factory,sources,store,clock);
        foreach(var request in requests)
        {
            var registered=await service.RegisterRetainedRequest(actor,request.Id,"issued-request/"+request.Id.ToString("N"),default);
            Assert.True(await worker.Process(request.WorkId,default));
            var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(v=>v.Id==registered.ResourceId);
            Assert.Equal(request.VersionId,version.PolicyVersionId);Assert.Equal(request.TemplateVersionId,version.TemplateVersionId);
            Assert.Equal(request.WorkId,version.WorkId);
            await using var download=await service.DownloadVersion(actor,version.Id,default);
            using var bytes=new MemoryStream();await download.Content.CopyToAsync(bytes);
            Assert.StartsWith("%PDF-",Encoding.ASCII.GetString(bytes.ToArray(),0,8));Assert.True(bytes.Length>5000);
            Assert.Equal(request.PayloadJson,(await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(r=>r.Id==request.Id)).PayloadJson);
            Assert.Equal(request.PayloadJson,(await db.Set<OutboxWork>().AsNoTracking().SingleAsync(w=>w.Id==request.WorkId)).Payload);
        }
        Assert.Equal(requests.Length,await db.Set<DocumentVersion>().CountAsync());
        Assert.Equal(requests.Length,await db.Set<DocumentVersionContent>().CountAsync());
    }
}
