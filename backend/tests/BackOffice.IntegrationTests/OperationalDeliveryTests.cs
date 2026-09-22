using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalDeliveryQueuesFrozenMessageAtomically()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        await using(var seed=await db.Database.BeginTransactionAsync()){await OperationalDeliverySeed.Seed(db);await seed.CommitAsync();}
        var boundary=new SqlCommandBoundary(f.Factory,f.Clock);
        var subject=await new TaskService(f.Factory,boundary,f.Clock).Register(f.Underwriter,new("policy",policy.Id),"delivery-subject",default);
        var threads=new ThreadService(f.Factory,boundary,f.Clock);
        var thread=await threads.Create(f.Underwriter,subject.ResourceId,new("agency","Fictional agency update",policy.RelationshipId),"delivery-thread",default);
        var contact=await db.Set<Contact>().AsNoTracking().SingleAsync(x=>x.RelationshipId==policy.RelationshipId&&x.EndedAt==null);
        var draft=await threads.CreateDraft(f.Underwriter,thread.ResourceId,new("Frozen fictional message",[contact.Id],[]),"delivery-draft",default);
        var owned=db.Database.GetDbConnection().Database;
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:OperationalDeliveryWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false")
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","delivery-test-keys",owned)))
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        using var client=host.CreateClient();
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
        {login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var key=Guid.NewGuid().ToString();
        Task<HttpResponseMessage> Send(string etag)
        {
            var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/messages/{draft.ResourceId}/send"){Content=JsonContent.Create(new{})};
            request.Headers.Add("X-CSRF-TOKEN",csrf);request.Headers.Add("Idempotency-Key",key);request.Headers.Add("If-Match",etag);return client.SendAsync(request);
        }
        var response=await Send(draft.Etag!);Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        var receipt=await response.Content.ReadAsStringAsync();Assert.Equal(receipt,await(await Send(draft.Etag!)).Content.ReadAsStringAsync());
        var delivery=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync();var version=await db.Set<OperationalMessageVersion>().AsNoTracking().SingleAsync();
        Assert.Equal("queued",delivery.State);Assert.Equal(version.Id,delivery.MessageVersionId);Assert.Equal(version.ContentJson,delivery.ContentJson);Assert.Equal(version.ContentHash,delivery.ContentHash);
        Assert.Contains("Frozen fictional message",version.ContentJson);Assert.Contains(contact.Email!,version.ContentJson);
        Assert.Equal(contact.Id,(await db.Set<OperationalDeliveryRecipient>().SingleAsync()).ContactId);
        var job=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==delivery.WorkId);Assert.Equal("pending",job.State);Assert.Equal("operational-delivery",job.Kind);Assert.Equal(delivery.Id,job.SubjectRecordId);
        Assert.Equal("queued",await db.Set<OperationalMessageDraft>().Where(x=>x.Id==draft.ResourceId).Select(x=>x.State).SingleAsync());
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalMessageVersion SET ContentJson=N'{{}}' WHERE Id={version.Id}"))).Number);
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OperationalDeliveryRecipient WHERE DeliveryId={delivery.Id}"))).Number);
        var leases=new SqlJobLeases(f.Factory,f.Clock);
        var worker=new MessageDeliveryWorker(f.Factory,new FileService(f.Factory,boundary,new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","delivery-files",owned)),[]),f.Clock),f.Clock);
        var first=await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,job.Id);Assert.NotNull(first);
        var outcome=await worker.ExecuteProvider(first);Assert.NotNull(outcome);
        Assert.Equal(outcome,await worker.ExecuteProvider(first));
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==MessageDeliveryService.WorkKind));
        using(var jobRead=await client.GetAsync($"/api/v1/jobs/{job.Id}")){Assert.Equal(HttpStatusCode.OK,jobRead.StatusCode);Assert.Contains("no-store",jobRead.Headers.CacheControl!.ToString());}
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET LeaseExpiresAt={f.Clock.GetUtcNow().AddSeconds(-1)} WHERE Id={job.Id}");
        Assert.Equal(InboxApplication.StaleLease,await worker.Apply(first,outcome));
        var recovery=await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,job.Id);Assert.NotNull(recovery);
        Assert.Equal(outcome,await worker.ExecuteProvider(recovery));
        Assert.Equal(InboxApplication.Applied,await worker.Apply(recovery,outcome));
        Assert.Equal(InboxApplication.Duplicate,await worker.Apply(recovery,outcome));
        Assert.Equal(InboxApplication.Quarantined,await worker.Apply(recovery,outcome with{State="rejected"}));
        Assert.Equal("delivered",await db.Set<OperationalDelivery>().Where(x=>x.Id==delivery.Id).Select(x=>x.State).SingleAsync());
        Assert.Equal("sent",await db.Set<OperationalMessageDraft>().Where(x=>x.Id==draft.ResourceId).Select(x=>x.State).SingleAsync());
        Assert.Single(await db.Set<AdapterQuarantine>().ToArrayAsync());
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==MessageDeliveryService.WorkKind));
        using var history=await client.GetAsync($"/api/v1/message-deliveries/{delivery.Id}");Assert.Equal(HttpStatusCode.OK,history.StatusCode);
        var details=await history.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("delivered",details.GetProperty("state").GetString());Assert.Equal("Frozen fictional message",details.GetProperty("body").GetString());
        using var resend=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/message-deliveries/{delivery.Id}/resend"){Content=JsonContent.Create(new{reason="Fictional explicit resend"})};
        resend.Headers.Add("X-CSRF-TOKEN",csrf);resend.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());resend.Headers.Add("If-Match",history.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Accepted,(await client.SendAsync(resend)).StatusCode);
        var newDelivery=await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x=>x.ResendOfId==delivery.Id);
        Assert.NotEqual(job.Id,newDelivery.WorkId);Assert.Equal(delivery.ContentJson,newDelivery.ContentJson);Assert.Equal(delivery.ContentHash,newDelivery.ContentHash);
        Assert.Equal("delivered",await db.Set<OperationalDelivery>().Where(x=>x.Id==delivery.Id).Select(x=>x.State).SingleAsync());
        Assert.Equal("sent",await db.Set<OperationalMessageDraft>().Where(x=>x.Id==draft.ResourceId).Select(x=>x.State).SingleAsync());
        Assert.Equal("Fictional explicit resend",await db.Set<AuditEvent>().Where(x=>x.EventType=="communication.delivery-recovery-reason").Select(x=>x.Reason).SingleAsync());
    });
}
