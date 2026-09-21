using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public Task RealSqlOperationalDocumentCancellationPreservesOriginalNotice(string product)=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await VerifyCancellationDocument(db,f.Factory,f.Clock,f.Underwriter,product=="motor-trade-road-risks");
    });

    [Fact]
    public Task RealSqlOperationalDocumentCancellationCommercial()=>CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);
        await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await using(var transaction=await db.Database.BeginTransactionAsync())
        {await CommercialUnderwritingCancellationSeed.SeedAsync(db);await transaction.CommitAsync();}
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        await VerifyCancellationDocument(db,source.Factory,new RatingClock{Current=term.StartsAt.AddDays(1)},source.Actor);
    },stopAfterAccepted:true);

    private static async Task VerifyCancellationDocument(BackOfficeDbContext db,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider clock,ActorContext actor,bool hosted=false)
    {
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var drafts=new ServicingDraftService(factory,clock);var review=new CancellationReviewService(factory,clock);
        static byte[] V(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string K()=>Guid.NewGuid().ToString();
        var created=await drafts.CreateAsync(actor,basis.TermId,V((await drafts.ListAsync(actor,basis.TermId)).Etag),
            new("cancellation",basis.Id,JsonSerializer.SerializeToElement(new{localDate="2026-10-15",localTime="00:00",timeZone="Europe/London"}),"Fictional cancellation document check"),K(),Guid.NewGuid());
        var leased=await drafts.LeaseAsync(actor,created.ResourceId,V(created.Etag!),"acquire",null,null,K(),Guid.NewGuid());
        var body=JsonNode.Parse(leased.Body)!;var fence=body["lease"]!["leaseToken"]!.GetValue<Guid>();
        body["proposal"]!["cancellationReasonCode"]="insured-request";
        var saved=await drafts.SaveAsync(actor,created.ResourceId,V(leased.Etag!),fence,body["proposal"]!.ToJsonString(),K(),Guid.NewGuid());
        var uploaded=await review.UploadAsync(actor,created.ResourceId,V(saved.Etag!),fence,"cancellation-request",null,"request.txt","text/plain",
            Encoding.UTF8.GetBytes("Fictional insured cancellation request for document verification."),K(),Guid.NewGuid());
        var evidence=await review.ReviewEvidenceAsync(actor,created.ResourceId,uploaded.ResourceId,V(uploaded.Etag!),fence,"accepted","Verified fictional request",K(),Guid.NewGuid());
        var view=await review.ReadAsync(actor,created.ResourceId);Assert.Empty(view.Blockers);
        var prepared=await review.PrepareAsync(actor,created.ResourceId,V(evidence.Etag!),fence,view.PreviewHash,K(),Guid.NewGuid());
        var approved=await review.ApproveAsync(actor,created.ResourceId,V(prepared.Etag!),fence,prepared.ResourceId,view.PreviewHash,"Approve fictional cancellation",K(),Guid.NewGuid());
        await review.IssueAsync(actor,created.ResourceId,V(approved.Etag!),fence,new(prepared.ResourceId,approved.ResourceId,view.PreviewHash,"Issue fictional cancellation"),K(),Guid.NewGuid());
        var notice=await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x=>x.Kind=="notice");
        var lease=(await new SqlJobLeases(factory,clock).ClaimWorkAsync("cancellation-notice",notice.WorkId))!;
        var notices=new CancellationNoticeWorker(factory,clock);var receiptId=(await notices.Deliver(lease))!.Value;Assert.True(await notices.Apply(lease,receiptId));
        var originalWork=JsonSerializer.Serialize(await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==notice.WorkId));
        var originalReceipt=JsonSerializer.Serialize(await db.Set<CancellationNoticeReceipt>().AsNoTracking().SingleAsync(x=>x.Id==receiptId));
        await using(var transaction=await db.Database.BeginTransactionAsync()){await DocumentTemplateSeed.SeedAsync(db);await transaction.CommitAsync();}
        var root=Path.GetFullPath(Path.Combine(".local","document-cancellation",db.Database.GetDbConnection().Database));
        var store=new OperationalFileStore(Path.Combine(root,"files"),[]);
        var boundary=new SqlCommandBoundary(factory,clock);var sources=new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer());
        var documents=new DocumentService(factory,boundary,sources,clock,new FileService(factory,boundary,store,clock));
        Assert.Contains(notice.Id,await documents.UnregisteredCancellationNotices(0,default));
        if(hosted)
        {
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:FileStoragePath",Path.Combine(root,"files"))
                .UseSetting("Cover:DataProtectionPath",Path.Combine(root,"keys")).UseSetting("Cover:DocumentWorkerEnabled","true")
                .UseSetting("Cover:FileWorkerEnabled","false").ConfigureServices(s=>s.AddSingleton(clock)));
            using var client=host.CreateClient();var complete=false;
            for(var poll=0;poll<120&&!complete;poll++)
            {
                complete=await(from v in db.Set<DocumentVersion>() join w in db.Set<OutboxWork>() on v.WorkId equals w.Id
                    where v.CancellationConsequenceId==notice.Id&&w.State=="succeeded" select v.Id).AnyAsync();
                if(!complete)await Task.Delay(250);
            }
            Assert.True(complete,"Hosted worker did not generate the PDF for an already delivered cancellation notice.");
        }
        var registered=await documents.RegisterCancellationNotice(actor,notice.Id,"cancellation-document",default);
        Assert.True((await documents.RegisterCancellationNotice(actor,notice.Id,"cancellation-document",default)).Replayed);
        Assert.Equal(registered.ResourceId,(await documents.RegisterCancellationNotice(actor,notice.Id,"cancellation-other-key",default)).ResourceId);
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.CancellationConsequenceId==notice.Id);
        Assert.NotEqual(notice.WorkId,version.WorkId);Assert.Equal(notice.VersionId,version.PolicyVersionId);
        if(!hosted)Assert.True(await new DocumentGenerationWorker(factory,sources,store,clock).Process(version.WorkId,default));
        Assert.DoesNotContain(notice.Id,await documents.UnregisteredCancellationNotices(0,default));
        await using(var download=await documents.DownloadVersion(actor,version.Id,default))
        {using var bytes=new MemoryStream();await download.Content.CopyToAsync(bytes);Assert.StartsWith("%PDF-",Encoding.ASCII.GetString(bytes.ToArray(),0,8));Assert.True(bytes.Length>5000);}
        Assert.Equal(originalWork,JsonSerializer.Serialize(await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==notice.WorkId)));
        Assert.Equal(originalReceipt,JsonSerializer.Serialize(await db.Set<CancellationNoticeReceipt>().AsNoTracking().SingleAsync(x=>x.Id==receiptId)));
        Assert.Equal(notice.PayloadJson,(await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x=>x.Id==notice.Id)).PayloadJson);
        Assert.Single(await db.Set<DocumentVersion>().Where(x=>x.CancellationConsequenceId==notice.Id).ToArrayAsync());
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==actor.UserId);user.State="suspended";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalAccessException>(()=>documents.RegisterCancellationNotice(actor,notice.Id,"cancellation-document",default));
    }
}
