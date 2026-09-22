using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RealSqlOperationalDemoLapseCorrespondenceRetainsLegacyReceiptAndCover(bool automatic)=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        var lifecycle=new RenewalLifecycleService(f.Factory,f.Clock);var before=await lifecycle.ReadAsync(f.Underwriter,term.Id);
        Guid lapseId;
        if(automatic){f.Clock.Current=before.Timeline.AutoLapseAt;lapseId=(await lifecycle.LapseDueAsync(term.Id))!.Value;}
        else lapseId=(await lifecycle.LapseAsync(f.Underwriter,term.Id,Convert.FromBase64String(before.Etag.Trim('"')),"Fictional insured declined renewal",Guid.NewGuid().ToString(),Guid.NewGuid())).ResourceId;
        var lapse=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleAsync();
        var leases=new SqlJobLeases(f.Factory,f.Clock);var lease=Assert.IsType<JobLease>(await leases.ClaimWorkAsync(RenewalLifecycleService.NotificationKind,lapse.WorkId));
        var legacy=new RenewalLapseNotificationWorker(f.Factory,f.Clock);var receiptId=(await legacy.Deliver(lease))!.Value;Assert.True(await legacy.Apply(lease,receiptId));
        var receipt=await db.Set<RenewalLapseNotificationReceipt>().AsNoTracking().SingleAsync();
        var originalWork=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==lapse.WorkId);
        var commands=new SqlCommandBoundary(f.Factory,f.Clock);var bridge=new LegacyOperationalBridge(f.Factory,new TaskService(f.Factory,commands,f.Clock),new ThreadService(f.Factory,commands,f.Clock),f.Clock);
        var messageId=await bridge.LapseNotice(f.Underwriter,lapseId);
        var draft=await db.Set<OperationalMessageDraft>().SingleAsync(x=>x.Id==messageId);Assert.Contains(lapse.Reason,draft.Body);Assert.Equal("draft",draft.State);
        draft.Body="Staff retained and annotated the historical lapse notice.";draft.UpdatedAt=f.Clock.GetUtcNow();await db.SaveChangesAsync();
        Assert.Equal(messageId,await bridge.LapseNotice(f.Underwriter,lapseId));db.ChangeTracker.Clear();
        Assert.Equal(messageId,(await lifecycle.ReadAsync(f.Underwriter,term.Id)).CorrespondenceMessageId);
        var seed=new OperationalDemoSeed(f.Factory,bridge);Assert.Equal(1,(await seed.InitializeRenewals(f.Underwriter)).RenewalNotices);
        Assert.Equal(draft.Body,await db.Set<OperationalMessageDraft>().Where(x=>x.Id==messageId).Select(x=>x.Body).SingleAsync());
        var retainedReceipt=await db.Set<RenewalLapseNotificationReceipt>().AsNoTracking().SingleAsync();Assert.Equal(receipt.Id,retainedReceipt.Id);Assert.Equal(receipt.PayloadHash,retainedReceipt.PayloadHash);
        var retainedWork=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==lapse.WorkId);Assert.Equal(originalWork.Payload,retainedWork.Payload);Assert.Equal(originalWork.Attempts,retainedWork.Attempts);
        Assert.Equal(term.EndsAt,await db.Set<PolicyTerm>().Where(x=>x.Id==term.Id).Select(x=>x.EndsAt).SingleAsync());Assert.Empty(await db.Set<OperationalDelivery>().ToListAsync());
        var immutable=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RenewalLapseCorrespondence SET CreatedAt=CreatedAt WHERE LapseEventId={lapseId}"));Assert.Equal(52045,immutable.Number);
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","operational-lapse-keys",db.Database.GetDbConnection().Database)))
            .UseSetting("Cover:DocumentWorkerEnabled","false").UseSetting("Cover:OperationalDeliveryWorkerEnabled","false").UseSetting("Cover:OperationalCancellationWorkerEnabled","false")
            .UseSetting("Cover:WorkflowTaskWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:LegacyOperationalWorkerEnabled","false")
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        host.UseKestrel(0);using var client=host.CreateClient();
        await OperationalDemoTests.RunMatchBrowser(host,db,password,Guid.Empty,messageId,automatic?"lapse-automatic":"lapse-manual",term.PolicyId,term.Id);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={f.Underwriter.UserId}");
        await Assert.ThrowsAsync<OperationalAccessException>(()=>bridge.LapseNotice(f.Underwriter,lapseId));
    });
}
