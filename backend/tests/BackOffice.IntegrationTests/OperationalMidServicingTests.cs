using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Theory][InlineData("motor-trade-road-risks")][InlineData("motor-trade-combined")]
    public Task RealSqlOperationalMidServicingUsesPredecessorAndOldResultCannotRetarget(string product)=>RunServicingRatingRequests(product,"ServicingIssueTests",onAccepted:async(db,f,cycle,acceptance,fence,etag)=>
    {
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var original=await db.Set<PolicyMidIntent>().AsNoTracking().SingleAsync();
        var registration=new MidSubmissionRegistration(f.Factory,f.Clock);await registration.Register(original.WorkId);
        var leases=new SqlJobLeases(f.Factory,f.Clock);var worker=new MidSubmissionWorker(f.Factory,f.Clock);var oldLease=await leases.ClaimWorkAsync("mid-update",original.WorkId);Assert.NotNull(oldLease);var oldResult=await worker.ExecuteProvider(oldLease);Assert.NotNull(oldResult);
        var issued=await new ServicingIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,cycle.DraftId,Convert.FromBase64String(etag.Trim('"')),fence,new(cycle.Id,acceptance.RatingId,acceptance.TermsVersionId,acceptance.Id,acceptance.TermsHash,acceptance.AssuranceHash,"Issue exact MID adjustment"),Guid.NewGuid().ToString(),Guid.NewGuid());
        var versions=await db.Set<PolicyVersion>().AsNoTracking().Where(x=>x.TransactionId==issued.ResourceId).OrderBy(x=>x.SliceOrdinal).ToArrayAsync();Assert.Equal(2,versions.Length);
        var laterHash=versions[^1].ContentHash;var latestId=versions[^1].Id;Assert.Equal(InboxApplication.Applied,await worker.Apply(oldLease,oldResult));Assert.Equal(basis.Id,oldResult.PolicyVersionId);
        var allItems=new List<JsonElement>();
        for(var index=0;index<versions.Length;index++)
        {
            var intent=await db.Set<PolicyMidIntent>().AsNoTracking().SingleAsync(x=>x.VersionId==versions[index].Id);var payload=intent.PayloadJson;var id=await registration.Register(intent.WorkId);
            var sub=await db.Set<MidSubmission>().AsNoTracking().SingleAsync(x=>x.Id==id);Assert.Equal(index==0?basis.Id:versions[index-1].Id,sub.BaseVersionId);Assert.Equal(payload,await db.Set<OutboxWork>().Where(x=>x.Id==intent.WorkId).Select(x=>x.Payload).SingleAsync());
            using var json=JsonDocument.Parse(sub.RequestJson);allItems.AddRange(json.RootElement.GetProperty("items").EnumerateArray().Select(x=>x.Clone()));Assert.Equal(versions[index].EffectiveAt,json.RootElement.GetProperty("effectiveAt").GetDateTimeOffset());
            var lease=await leases.ClaimWorkAsync("mid-update",intent.WorkId);Assert.NotNull(lease);var result=await worker.ExecuteProvider(lease);Assert.NotNull(result);Assert.Equal(InboxApplication.Applied,await worker.Apply(lease,result));Assert.Equal(versions[index].Id,result.PolicyVersionId);
        }
        Assert.NotEmpty(allItems);if(product=="motor-trade-road-risks"){Assert.Contains(allItems,x=>x.GetProperty("action").GetString()=="remove");Assert.Contains(allItems,x=>x.GetProperty("action").GetString()=="add"&&x.GetProperty("registration").GetString()=="ZZ10TST");}
        Assert.Equal(3,await db.Set<MidSubmission>().CountAsync());Assert.Equal(3,await db.Set<MidResult>().CountAsync());Assert.Equal(laterHash,await db.Set<PolicyVersion>().Where(x=>x.Id==latestId).Select(x=>x.ContentHash).SingleAsync());
    });
}
