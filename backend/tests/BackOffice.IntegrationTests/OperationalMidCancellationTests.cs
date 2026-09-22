using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public Task RealSqlOperationalMidCancellationPreservesOriginalWorkAndWaitsForEffectiveTime()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var drafts=new ServicingDraftService(f.Factory,f.Clock);var review=new CancellationReviewService(f.Factory,f.Clock);
        static byte[] V(string value)=>Convert.FromBase64String(value.Trim('"'));static string K()=>Guid.NewGuid().ToString();
        var created=await drafts.CreateAsync(f.Underwriter,basis.TermId,V((await drafts.ListAsync(f.Underwriter,basis.TermId)).Etag),new("cancellation",basis.Id,JsonSerializer.SerializeToElement(new{localDate="2026-10-15",localTime="00:00",timeZone="Europe/London"}),"Fictional MID cancellation check"),K(),Guid.NewGuid());
        var leased=await drafts.LeaseAsync(f.Underwriter,created.ResourceId,V(created.Etag!),"acquire",null,null,K(),Guid.NewGuid());var body=JsonNode.Parse(leased.Body)!;var fence=body["lease"]!["leaseToken"]!.GetValue<Guid>();body["proposal"]!["cancellationReasonCode"]="insured-request";
        var saved=await drafts.SaveAsync(f.Underwriter,created.ResourceId,V(leased.Etag!),fence,body["proposal"]!.ToJsonString(),K(),Guid.NewGuid());
        var uploaded=await review.UploadAsync(f.Underwriter,created.ResourceId,V(saved.Etag!),fence,"cancellation-request",null,"request.txt","text/plain",Encoding.UTF8.GetBytes("Fictional insured request for MID cancellation verification."),K(),Guid.NewGuid());
        var evidence=await review.ReviewEvidenceAsync(f.Underwriter,created.ResourceId,uploaded.ResourceId,V(uploaded.Etag!),fence,"accepted","Verified fictional request",K(),Guid.NewGuid());var view=await review.ReadAsync(f.Underwriter,created.ResourceId);Assert.Empty(view.Blockers);
        var prepared=await review.PrepareAsync(f.Underwriter,created.ResourceId,V(evidence.Etag!),fence,view.PreviewHash,K(),Guid.NewGuid());var approved=await review.ApproveAsync(f.Underwriter,created.ResourceId,V(prepared.Etag!),fence,prepared.ResourceId,view.PreviewHash,"Approve fictional cancellation",K(),Guid.NewGuid());
        await review.IssueAsync(f.Underwriter,created.ResourceId,V(approved.Etag!),fence,new(prepared.ResourceId,approved.ResourceId,view.PreviewHash,"Issue fictional cancellation"),K(),Guid.NewGuid());
        var intent=await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x=>x.Kind=="mid-removal");var original=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==intent.WorkId);
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        var registration=new MidSubmissionRegistration(f.Factory,f.Clock);var id=await registration.Register(intent.WorkId);Assert.Equal(id,await registration.Register(intent.WorkId));var sub=await db.Set<MidSubmission>().AsNoTracking().SingleAsync();Assert.Equal(intent.Id,sub.CancellationConsequenceId);Assert.Null(sub.PolicyMidIntentId);Assert.Equal(basis.Id,sub.BaseVersionId);
        using var request=JsonDocument.Parse(sub.RequestJson);var effective=request.RootElement.GetProperty("effectiveAt").GetDateTimeOffset();Assert.All(request.RootElement.GetProperty("items").EnumerateArray(),x=>{Assert.Equal("remove",x.GetProperty("action").GetString());Assert.Equal(effective,x.GetProperty("effectiveAt").GetDateTimeOffset());});Assert.NotEmpty(request.RootElement.GetProperty("items").EnumerateArray());
        var retained=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==intent.WorkId);Assert.Equal(original.OperationKey,retained.OperationKey);Assert.Equal(original.Payload,retained.Payload);Assert.Equal(original.ScenarioVersionId,retained.ScenarioVersionId);Assert.NotEqual(retained.ScenarioVersionId,sub.ScenarioVersionId);
        var leases=new SqlJobLeases(f.Factory,f.Clock);Assert.Null(await leases.ClaimWorkAsync("cancellation-mid-removal",intent.WorkId));f.Clock.Current=effective;var lease=await leases.ClaimWorkAsync("cancellation-mid-removal",intent.WorkId);Assert.NotNull(lease);
        var worker=new MidSubmissionWorker(f.Factory,f.Clock);var outcome=await worker.ExecuteProvider(lease);Assert.NotNull(outcome);Assert.Equal(InboxApplication.Applied,await worker.Apply(lease,outcome));Assert.Equal(intent.VersionId,outcome.PolicyVersionId);Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind=="cancellation-mid-removal"));
    });
    [Fact]
    public Task RealSqlOperationalMidCommercialVersionCannotEnterMotorReporting()=>CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();Assert.Empty(await db.Set<PolicyMidIntent>().ToArrayAsync());Assert.Empty(await db.Set<MidSubmission>().ToArrayAsync());
        var clock=new RatingClock{Current=now};var service=new MidSubmissionService(source.Factory,new SqlCommandBoundary(source.Factory,clock),clock);
        Assert.Equal(404,(await Assert.ThrowsAsync<OperationalAccessException>(()=>service.List(source.Actor,version.Id,null,25,now,default))).Status);
        await Assert.ThrowsAsync<OperationalAccessException>(()=>new MidSubmissionRegistration(source.Factory,clock).Register(Guid.NewGuid()));
    },stopAfterAccepted:true);
}
