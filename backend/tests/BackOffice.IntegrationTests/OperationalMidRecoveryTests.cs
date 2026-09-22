using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalMidDefinitiveRejectionRetainsReasonsAndOneException()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        db.Add(new SettingVersion{Scope=OperationalMidSeed.Scope,Version=2,EffectiveFrom=f.Clock.Current,Values="{\"demo\":true,\"kind\":\"operational-mid\",\"schemaVersion\":\"2\",\"scenario\":\"reject\"}"});await db.SaveChangesAsync();
        var intent=await db.Set<PolicyMidIntent>().AsNoTracking().SingleAsync();var id=await new MidSubmissionRegistration(f.Factory,f.Clock).Register(intent.WorkId);
        var leases=new SqlJobLeases(f.Factory,f.Clock);var worker=new MidSubmissionWorker(f.Factory,f.Clock);var lease=await leases.ClaimWorkAsync("mid-update",intent.WorkId);Assert.NotNull(lease);var outcome=await worker.ExecuteProvider(lease);Assert.NotNull(outcome);Assert.Equal("rejected",outcome.State);Assert.Equal(new[]{"demo-provider-rejected"},outcome.ReasonCodes);
        Assert.Equal(InboxApplication.Applied,await worker.Apply(lease,outcome));Assert.Equal(InboxApplication.Duplicate,await worker.Apply(lease,outcome));
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==intent.WorkId);Assert.Equal("provider-rejected",work.ErrorCode);Assert.Equal("failed",work.State);Assert.Equal(1,await db.Set<MidResult>().CountAsync());Assert.Equal(1,await db.Set<JobException>().CountAsync(x=>x.WorkId==work.Id));
        var service=new MidSubmissionService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock);
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Retry(f.Underwriter,id,TaskService.Etag(work.RowVersion),"Do not repeat a definitive rejection",Guid.NewGuid().ToString(),default));
        var page=await service.List(f.Underwriter,intent.VersionId,null,25,f.Clock.Current,default);var row=JsonSerializer.SerializeToElement(page.Items[0]);Assert.Equal("rejected",row.GetProperty("state").GetString());Assert.False(row.GetProperty("retryAllowed").GetBoolean());
    });
    [Theory][InlineData(false)][InlineData(true)]
    public Task RealSqlOperationalMidRevocationFencesProviderAndLateApplication(bool afterEffect)=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        var intent=await db.Set<PolicyMidIntent>().AsNoTracking().SingleAsync();await new MidSubmissionRegistration(f.Factory,f.Clock).Register(intent.WorkId);
        var leases=new SqlJobLeases(f.Factory,f.Clock);var worker=new MidSubmissionWorker(f.Factory,f.Clock);var lease=await leases.ClaimWorkAsync("mid-update",intent.WorkId);Assert.NotNull(lease);
        var outcome=afterEffect?await worker.ExecuteProvider(lease):null;
        await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,"suspended"));
        if(afterEffect){Assert.NotNull(outcome);Assert.Equal(InboxApplication.Applied,await worker.Apply(lease,outcome));}
        else{var error=await Assert.ThrowsAsync<MidWorkerException>(()=>worker.ExecuteProvider(lease));Assert.Equal(JobFailure.Superseded,error.Failure);Assert.True(await leases.FailAsync(lease,error.Failure));}
        Assert.Empty(await db.Set<MidResult>().ToArrayAsync());Assert.Equal(afterEffect?1:0,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind=="mid-update"));Assert.Equal("mid-context-unavailable",await db.Set<OutboxWork>().Where(x=>x.Id==intent.WorkId).Select(x=>x.ErrorCode).SingleAsync());Assert.Equal(1,await db.Set<JobException>().CountAsync(x=>x.WorkId==intent.WorkId));
    });
    [Fact]
    public Task RealSqlOperationalMidProviderRetainsOriginalOperationAndRejectsRevokedRetry()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var intent=await db.Set<PolicyMidIntent>().AsNoTracking().SingleAsync();
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        db.Add(new SettingVersion{Scope=OperationalMidSeed.Scope,Version=2,EffectiveFrom=f.Clock.GetUtcNow(),Values="{\"demo\":true,\"kind\":\"operational-mid\",\"schemaVersion\":\"2\",\"scenario\":\"timeout-after-success\"}"});await db.SaveChangesAsync();
        var registration=new MidSubmissionRegistration(f.Factory,f.Clock);var id=await registration.Register(intent.WorkId);var submission=await db.Set<MidSubmission>().AsNoTracking().SingleAsync();
        using var request=JsonDocument.Parse(submission.RequestJson);Assert.NotEmpty(request.RootElement.GetProperty("items").EnumerateArray());Assert.All(request.RootElement.GetProperty("items").EnumerateArray(),x=>Assert.Equal("add",x.GetProperty("action").GetString()));Assert.Equal(version.Id,request.RootElement.GetProperty("versionId").GetGuid());
        var leases=new SqlJobLeases(f.Factory,f.Clock);var worker=new MidSubmissionWorker(f.Factory,f.Clock);var lease=await leases.ClaimWorkAsync("mid-update",intent.WorkId);Assert.NotNull(lease);
        Assert.Equal(JobFailure.ProviderTimeout,(await Assert.ThrowsAsync<MidWorkerException>(()=>worker.ExecuteProvider(lease))).Failure);
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind=="mid-update"));Assert.Empty(await db.Set<MidResult>().ToArrayAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET LeaseExpiresAt={f.Clock.Current.AddSeconds(-1)} WHERE Id={intent.WorkId}");
        var recovered=await leases.ClaimWorkAsync("mid-update",intent.WorkId);Assert.NotNull(recovered);worker=new MidSubmissionWorker(f.Factory,f.Clock);var outcome=await worker.ExecuteProvider(recovered);Assert.NotNull(outcome);
        Assert.Equal(InboxApplication.StaleLease,await worker.Apply(lease,outcome));Assert.Equal(InboxApplication.Applied,await worker.Apply(recovered,outcome));Assert.Equal(InboxApplication.Duplicate,await worker.Apply(recovered,outcome));
        Assert.Equal(InboxApplication.Quarantined,await worker.Apply(recovered,outcome with{State="rejected"}));Assert.Equal(1,await db.Set<MidResult>().CountAsync());Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind=="mid-update"));
        Assert.Equal(version.SnapshotJson,await db.Set<PolicyVersion>().Where(x=>x.Id==version.Id).Select(x=>x.SnapshotJson).SingleAsync());
        Assert.Equal(52020,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MidSubmission SET RequestJson=N'{{}}' WHERE Id={id}"))).Number);
        Assert.Equal(52023,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET Payload=N'{{}}' WHERE Id={intent.WorkId}"))).Number);
        Assert.Equal(52024,(await Assert.ThrowsAsync<SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20260922125221_OperationalClaims"))).Number);
        var service=new MidSubmissionService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock);var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==intent.WorkId);
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Retry(f.Underwriter,id,TaskService.Etag(work.RowVersion),"Already accepted",Guid.NewGuid().ToString(),default));
        var agencyActor=f.Underwriter with{AgencyId=Guid.NewGuid()};await Assert.ThrowsAsync<OperationalAccessException>(()=>service.List(agencyActor,version.Id,null,25,f.Clock.Current,default));
        await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,"suspended"));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>service.List(f.Underwriter,version.Id,null,25,f.Clock.Current,default));await Assert.ThrowsAsync<OperationalAccessException>(()=>service.Retry(f.Underwriter,id,TaskService.Etag(work.RowVersion),"A revoked user cannot retry",Guid.NewGuid().ToString(),default));
    });
}
