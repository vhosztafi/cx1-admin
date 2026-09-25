using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Data.SqlClient;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlAgencyResponseRequiresDeliveredPublicMessageAndRetainsClosure()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        var quote=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==f.QuoteId);
        await using(var seed=await db.Database.BeginTransactionAsync()){await OperationalDeliverySeed.Seed(db);await AgencyDemoSeed.SeedAsync(db);await seed.CommitAsync();}
        var boundary=new SqlCommandBoundary(f.Factory,f.Clock);
        var subject=await new TaskService(f.Factory,boundary,f.Clock).Register(f.Underwriter,new("quote",quote.Id),"response-subject",default);
        var threads=new ThreadService(f.Factory,boundary,f.Clock);
        var thread=await threads.Create(f.Underwriter,subject.ResourceId,new("agency","Licence details requested",quote.RelationshipId),"response-thread",default);
        var contact=await db.Set<Contact>().AsNoTracking().SingleAsync(x=>x.RelationshipId==quote.RelationshipId&&x.EndedAt==null);
        const string instruction="Please supply the driver's licence number and date of test.";
        var draft=await threads.CreateDraft(f.Underwriter,thread.ResourceId,new(instruction,[contact.Id],[]),"response-draft",default);
        var queued=await new MessageDeliveryService(boundary,f.Clock).Send(f.Underwriter,draft.ResourceId,draft.Etag!,"response-send",default);
        var owned=db.Database.GetDbConnection().Database;
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:OperationalDeliveryWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false")
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","agency-response-keys",owned)))
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        using var client=host.CreateClient();
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        async Task<HttpResponseMessage> Post(string path,object body,string? key=null,string? etag=null)
        {
            var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};
            request.Headers.Add("X-CSRF-TOKEN",csrf);request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());
            if(etag is not null)request.Headers.Add("If-Match",etag);return await client.SendAsync(request);
        }
        (await Post("/api/v1/auth/login",new{email="underwriter@cover.example",password})).EnsureSuccessStatusCode();
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var path=$"/api/v1/messages/{draft.ResourceId}/agency-response";
        var body=new{reason="Track the agency response to this delivered request"};
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(path,body)).StatusCode);
        var leases=new SqlJobLeases(f.Factory,f.Clock);
        var worker=new MessageDeliveryWorker(f.Factory,new FileService(f.Factory,boundary,new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","agency-response-files",owned)),[]),f.Clock),f.Clock);
        var lease=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,queued.ResourceId))!;
        var effect=await worker.ExecuteProvider(lease);Assert.NotNull(effect);Assert.Equal(InboxApplication.Applied,await worker.Apply(lease,effect));
        var key=Guid.NewGuid().ToString();var created=await Post(path,body,key);Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var receipt=await created.Content.ReadAsStringAsync();Assert.Equal(receipt,await(await Post(path,body,key)).Content.ReadAsStringAsync());
        var request=JsonSerializer.Deserialize<JsonElement>(receipt);var id=request.GetProperty("id").GetGuid();
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyResponseRequest SET Instruction='Changed public instruction' WHERE Id={id}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AgencyResponseRequest WHERE Id={id}"));
        Assert.Equal("awaiting-response",request.GetProperty("state").GetString());Assert.Equal(instruction,request.GetProperty("instruction").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(path,body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(path,new{reason="Different request using a duplicate key"},key)).StatusCode);
        var page=await client.GetFromJsonAsync<JsonElement>($"/api/v1/agencies/{quote.AgencyId}/sharing/open-items?q=Licence");
        var agencyRead=await client.GetFromJsonAsync<JsonElement>($"/api/v1/agencies/{quote.AgencyId}");
        Assert.Equal("statements",Assert.Single(agencyRead.GetProperty("unavailableSections").EnumerateArray()).GetProperty("kind").GetString());
        var item=Assert.Single(page.GetProperty("items").EnumerateArray());Assert.Equal(id,item.GetProperty("id").GetGuid());
        Assert.Equal(instruction,item.GetProperty("instruction").GetString());
        Assert.Empty((await AgencySharingService.PreviewOpenItems(db,f.Underwriter,quote.AgencyId,new(Search:body.reason,At:f.Clock.GetUtcNow()))).Items);
        var foreignAgency=await db.Set<Agency>().AsNoTracking().Where(x=>x.Id!=quote.AgencyId).Select(x=>x.Id).FirstAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State='active' WHERE Id={foreignAgency}");
        Assert.DoesNotContain((await AgencySharingService.PreviewOpenItems(db,f.Underwriter,foreignAgency,new(At:f.Clock.GetUtcNow()))).Items,x=>x.Id==id);
        var brokerUser=new StaffUser{State="invited",AgencyId=quote.AgencyId,Email="response-broker@example.test",NormalizedEmail="RESPONSE-BROKER@EXAMPLE.TEST",DisplayName="Fictional response broker"};
        db.Add(brokerUser);await db.SaveChangesAsync();
        db.Add(new UserRole{UserId=brokerUser.Id,RoleId=await db.Set<Role>().Where(x=>x.Code=="broker-readonly").Select(x=>x.Id).SingleAsync()});await db.SaveChangesAsync();
        brokerUser.State="active";await db.SaveChangesAsync();
        var broker=new ActorContext(brokerUser.Id,null,quote.AgencyId,new HashSet<string>{"broker-readonly"});
        Assert.Equal(instruction,Assert.Single((await AgencySharingService.OpenItems(db,broker,quote.AgencyId,new(Search:"licence",At:f.Clock.GetUtcNow()))).Items).Instruction);
        Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>AgencySharingService.OpenItems(db,broker,foreignAgency,new(At:f.Clock.GetUtcNow())))).Status);
        var secondThread=await threads.Create(f.Underwriter,subject.ResourceId,new("agency","Other response requested",quote.RelationshipId),"response-thread-two",default);
        var secondDraft=await threads.CreateDraft(f.Underwriter,secondThread.ResourceId,new("Please supply the remaining information.",[contact.Id],[]),"response-draft-two",default);
        var secondSend=await new MessageDeliveryService(boundary,f.Clock).Send(f.Underwriter,secondDraft.ResourceId,secondDraft.Etag!,"response-send-two",default);
        var secondLease=(await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,secondSend.ResourceId))!;
        Assert.Equal(InboxApplication.Applied,await worker.Apply(secondLease,(await worker.ExecuteProvider(secondLease))!));
        var secondRequest=await new AgencyResponseService(f.Factory,boundary,f.Clock).Track(f.Underwriter,secondDraft.ResourceId,"Track the second delivered request","response-track-two",default);
        var firstPage=await client.GetFromJsonAsync<JsonElement>($"/api/v1/agencies/{quote.AgencyId}/sharing/open-items?pageSize=1");
        var next=$"/api/v1/agencies/{quote.AgencyId}/sharing/open-items?pageSize=1&cursor="+Uri.EscapeDataString(firstPage.GetProperty("nextCursor").GetString()!);
        foreach(var hidden in new[]{"reason","recipientContactIds","email","authority","referral"})Assert.False(item.TryGetProperty(hidden,out _));
        using var saved=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
        var etag=saved.Headers.ETag!.Tag;
        Assert.Equal(HttpStatusCode.PreconditionFailed,(await Post($"/api/v1/agency-responses/{id}/resolve",new{outcome="response-received",reason="Agency supplied the requested licence details"},etag:"\"AAAAAAAAAAA=\"")).StatusCode);
        var closeKey=Guid.NewGuid().ToString();var closeBody=new{outcome="response-received",reason="Agency supplied the requested licence details"};
        var closed=await Post($"/api/v1/agency-responses/{id}/resolve",closeBody,closeKey,etag);Assert.Equal(HttpStatusCode.OK,closed.StatusCode);
        Assert.Equal(await closed.Content.ReadAsStringAsync(),await(await Post($"/api/v1/agency-responses/{id}/resolve",closeBody,closeKey,etag)).Content.ReadAsStringAsync());
        var after=await client.GetFromJsonAsync<JsonElement>($"/api/v1/agencies/{quote.AgencyId}/sharing/open-items?q=Licence");Assert.Equal(0,after.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(next)).StatusCode);
        var retained=await client.GetFromJsonAsync<JsonElement>(path);Assert.Equal("response-received",retained.GetProperty("state").GetString());
        Assert.Equal(instruction,retained.GetProperty("instruction").GetString());
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyResponseRequest SET ResolutionReason='Changed retained closure' WHERE Id={id}"));
        Assert.Equal(2,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM AgencyResponseRequest").SingleAsync());
        var migrations=(await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var responseMigration=Array.IndexOf(migrations,"20260922212339_AgencyResponseTracking");
        Assert.True(responseMigration>0);
        var downgrade=await Assert.ThrowsAsync<SqlException>(()=>
            db.GetService<IMigrator>().MigrateAsync(migrations[responseMigration-1]));
        Assert.Equal(52000,downgrade.Number);
        Assert.Contains("Retained agency response requests prevent downgrade",downgrade.Message);
        Assert.Equal(2,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM AgencyResponseRequest").SingleAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State='inactive' WHERE Id={quote.RelationshipId}");
        Assert.Empty((await AgencySharingService.OpenItems(db,broker,quote.AgencyId,new(At:f.Clock.GetUtcNow()))).Items);
        Assert.False((await Post(path,body,key)).IsSuccessStatusCode);
        Assert.False((await Post($"/api/v1/agency-responses/{id}/resolve",closeBody,closeKey,etag)).IsSuccessStatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State='suspended' WHERE Id={brokerUser.Id}");
        Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>AgencySharingService.OpenItems(db,broker,quote.AgencyId,new(At:f.Clock.GetUtcNow())))).Status);
    });
}
