using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;
using BackOffice.Application.Operations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalIncidentCommercialRetainsOriginalLocationAndUnsentLog()=>CommercialTermsScenario(stopAfterAccepted:true,inspectAccepted:async(db,password)=>
    {
        var cycle=await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");
        var f=await CommercialIssueCommand(db,cycle,cycle.CurrentAcceptanceId!.Value,user.Id);
        await f.Service.IssueAsync(f.Actor,f.Quote.Id,f.Quote.RowVersion,f.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        var clock=new RatingClock{Current=term.EndsAt.AddDays(1)};var service=new IncidentService(f.Factory,new SqlCommandBoundary(f.Factory,clock),new IncidentOccurrenceResolver(f.Factory,clock),clock);
        using var snapshot=JsonDocument.Parse(version.SnapshotJson);var location=snapshot.RootElement.GetProperty("risk").GetProperty("locations")[0].GetProperty("id").GetGuid();
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1);
        var input=JsonSerializer.SerializeToElement(new{policyId=version.PolicyId,productCode="commercial-combined",occurrence=new{occurredOn=day.ToString("yyyy-MM-dd"),timeZone="Europe/London",precision="date"},kind="property-damage",thirdPartyInvolvement="no",reportedBy="Fictional agency",reportingRoute="agency",bestContactDescription="01632 960002",description="Fictional storm damage to the insured building roof.",commercialSubject=new{kind="property",locationId=location,coverCode="buildings",owner="insured",estimatedValueAtRisk="2500.00"}});
        var created=await service.Create(f.Actor,input,Guid.NewGuid().ToString(),default);
        var logged=await service.Resolve(f.Actor,created.ResourceId,created.Etag!,true,Guid.NewGuid().ToString(),default);
        using var result=JsonDocument.Parse(logged.Body);Assert.Equal("logged",result.RootElement.GetProperty("state").GetString());Assert.Empty(result.RootElement.GetProperty("missing").EnumerateArray());
        var source=await db.Set<IncidentResolutionSource>().AsNoTracking().SingleAsync();Assert.Equal(version.Id,source.VersionId);Assert.Equal(Convert.ToHexStringLower(version.ContentHash),source.SourceHash);
        var invalid=JsonNode.Parse(input.GetRawText())!;invalid["commercialSubject"]!["locationId"]=Guid.NewGuid();
        await Assert.ThrowsAsync<IncidentRuleException>(()=>service.Update(f.Actor,created.ResourceId,logged.Etag!,JsonSerializer.SerializeToElement(invalid),Guid.NewGuid().ToString(),default));
        Assert.Equal(1,await db.Set<IncidentRevision>().CountAsync());Assert.Equal(0,await db.Set<OutboxWork>().CountAsync(x=>x.Kind=="incident-handoff"));
        var changed=await service.SaveDescription(f.Actor,created.ResourceId,logged.Etag!,"A corrected fictional description retains the prior report.",Guid.NewGuid().ToString(),default);
        using var corrected=JsonDocument.Parse(changed.Body);Assert.Equal("draft",corrected.RootElement.GetProperty("state").GetString());Assert.False(corrected.RootElement.TryGetProperty("resolution",out _));
        Assert.Equal(2,await db.Set<IncidentRevision>().CountAsync());Assert.Equal(1,await db.Set<IncidentOccurrenceRecord>().CountAsync());
    });
    [Fact]
    public Task RealSqlOperationalIncidentApiPersistsIncompleteDraftWithImmutableHistory()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();f.Clock.Current=term.EndsAt.AddDays(2);
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:OperationalDeliveryWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false")
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","incident-api-keys",db.Database.GetDbConnection().Database)))
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        using var client=host.CreateClient();
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
        {login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/incidents"){Content=JsonContent.Create(new{policyId=policy.Id,productCode="motor-trade-road-risks"})};
        request.Headers.Add("X-CSRF-TOKEN",csrf);request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());
        using var response=await client.SendAsync(request);Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var saved=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("draft",saved.GetProperty("state").GetString());
        var incident=await db.Set<OperationalIncident>().AsNoTracking().SingleAsync();var revision=await db.Set<IncidentRevision>().AsNoTracking().SingleAsync();
        Assert.Equal(incident.Id,saved.GetProperty("id").GetGuid());Assert.Equal(revision.Id,incident.CurrentRevisionId);Assert.Equal(policy.Id,incident.PolicyId);
        Assert.Contains("historical-cover",saved.GetProperty("missing").EnumerateArray().Select(x=>x.GetString()));
        Assert.Equal(0,await db.Set<OutboxWork>().CountAsync(x=>x.Kind=="incident-handoff"));
        var path=$"/api/v1/incidents/{incident.Id}";var originalEtag=response.Headers.ETag!.ToString();
        async Task<HttpResponseMessage> Write(HttpMethod method,string url,object body,string? etag=null,string? key=null,bool withCsrf=true)
        {
            using var command=new HttpRequestMessage(method,url){Content=JsonContent.Create(body)};
            if(withCsrf)command.Headers.Add("X-CSRF-TOKEN",csrf);
            command.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());if(etag is not null)command.Headers.Add("If-Match",etag);
            return await client.SendAsync(command);
        }
        using(var denied=await Write(HttpMethod.Put,path,saved.GetProperty("draft"),originalEtag,withCsrf:false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var notReady=await Write(HttpMethod.Post,path+"/log",new{},originalEtag))Assert.Equal(HttpStatusCode.UnprocessableEntity,notReady.StatusCode);
        Assert.Empty(await db.Set<IncidentOccurrenceRecord>().ToArrayAsync());
        var description=new{description="A fictional customer reported damage while moving a vehicle."};var descriptionKey=Guid.NewGuid().ToString();
        using var described=await Write(HttpMethod.Put,path+"/description",description,originalEtag,descriptionKey);Assert.Equal(HttpStatusCode.OK,described.StatusCode);
        var describedBody=await described.Content.ReadAsStringAsync();var describedEtag=described.Headers.ETag!.ToString();
        using(var replay=await Write(HttpMethod.Put,path+"/description",description,originalEtag,descriptionKey)){Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(describedBody,await replay.Content.ReadAsStringAsync());}
        using(var changed=await Write(HttpMethod.Put,path+"/description",new{description="Changed request with the original key"},originalEtag,descriptionKey))Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
        using(var stale=await Write(HttpMethod.Put,path+"/description",description,originalEtag))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        Assert.Equal(2,await db.Set<IncidentRevision>().CountAsync());
        var complete=JsonNode.Parse(describedBody)!["draft"]!;
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1);
        complete["occurrence"]=JsonSerializer.SerializeToNode(new{occurredOn=day.ToString("yyyy-MM-dd"),timeZone="Europe/London",precision="approximate",approximateLocalTime="12:30"});
        complete["kind"]="other";complete["thirdPartyInvolvement"]="unknown";complete["reportedBy"]="Fictional reporter";complete["reportingRoute"]="agency";complete["bestContactDescription"]="01632 960001";
        complete["motorSubject"]=JsonSerializer.SerializeToNode(new{kind="registered-vehicle",vehicleId=Guid.NewGuid()});
        using(var foreign=await Write(HttpMethod.Put,path,complete,describedEtag))Assert.Equal(HttpStatusCode.UnprocessableEntity,foreign.StatusCode);
        complete["motorSubject"]=JsonSerializer.SerializeToNode(new{kind="third-party-only",itemDescription="Customer's boundary wall"});
        using var updated=await Write(HttpMethod.Put,path,complete,describedEtag);Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
        using var resolved=await Write(HttpMethod.Post,path+"/occurrence-resolution",new{},updated.Headers.ETag!.ToString());Assert.Equal(HttpStatusCode.OK,resolved.StatusCode);
        var resolution=await resolved.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("resolved",resolution.GetProperty("state").GetString());
        Assert.Equal(version.Id,resolution.GetProperty("candidates")[0].GetProperty("versionId").GetGuid());
        var options=await client.GetFromJsonAsync<JsonElement>(path+$"/subject-options?versionId={version.Id}");Assert.Equal(version.Id,options.GetProperty("versionId").GetGuid());
        using(var foreignOptions=await client.GetAsync(path+$"/subject-options?versionId={Guid.NewGuid()}"))Assert.Equal(HttpStatusCode.NotFound,foreignOptions.StatusCode);
        using var logged=await Write(HttpMethod.Post,path+"/log",new{},resolved.Headers.ETag!.ToString());Assert.Equal(HttpStatusCode.OK,logged.StatusCode);
        Assert.Equal("logged",(await logged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        using var loaded=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,loaded.StatusCode);Assert.Contains("no-store",loaded.Headers.CacheControl!.ToString());
        var history=await client.GetFromJsonAsync<JsonElement>(path+"/revisions?pageSize=2");Assert.Equal(3,history.GetProperty("totalCount").GetInt32());Assert.Equal(2,history.GetProperty("items").GetArrayLength());
        var older=await client.GetFromJsonAsync<JsonElement>(path+"/revisions?pageSize=2&cursor="+Uri.EscapeDataString(history.GetProperty("nextCursor").GetString()!));Assert.Equal(1,older.GetProperty("items")[0].GetProperty("number").GetInt32());
        var list=await client.GetFromJsonAsync<JsonElement>($"/api/v1/incidents?policyId={policy.Id}");Assert.Equal(1,list.GetProperty("totalCount").GetInt32());
        Assert.Equal(2,await db.Set<IncidentOccurrenceRecord>().CountAsync());Assert.Equal(2,await db.Set<IncidentResolutionSource>().CountAsync());
        var checks=await client.GetFromJsonAsync<JsonElement>(path+"/occurrence-resolutions?pageSize=1");Assert.Equal(2,checks.GetProperty("totalCount").GetInt32());Assert.Single(checks.GetProperty("items").EnumerateArray());
        var priorCheck=await client.GetFromJsonAsync<JsonElement>(path+"/occurrence-resolutions?pageSize=1&cursor="+Uri.EscapeDataString(checks.GetProperty("nextCursor").GetString()!));
        Assert.NotEqual(checks.GetProperty("items")[0].GetProperty("id").GetGuid(),priorCheck.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(0,await db.Set<OutboxWork>().CountAsync(x=>x.Kind=="incident-handoff"));
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE IncidentRevision SET Reason='Changed' WHERE Id={revision.Id}"))).Number);
        // The retained revision FK prevents deleting the head before its AFTER trigger runs.
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Incident WHERE Id={incident.Id}"))).Number);
        var direct=new IncidentService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),new IncidentOccurrenceResolver(f.Factory,f.Clock),f.Clock);
        var receiptCount=await db.Set<IdempotencyRecord>().CountAsync();var auditCount=await db.Set<AuditEvent>().CountAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_IncidentRevision_TestFailure ON IncidentRevision AFTER INSERT AS THROW 52099,'Owned incident rollback probe.',1;");
        try
        {
            var failure=await Assert.ThrowsAsync<DbUpdateException>(()=>direct.SaveDescription(f.Underwriter,incident.Id,logged.Headers.ETag!.ToString(),"A fictional change rejected by the owned rollback probe.",Guid.NewGuid().ToString(),default));
            Assert.Equal(52099,Assert.IsType<SqlException>(failure.InnerException).Number);
        }
        finally{await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_IncidentRevision_TestFailure;");}
        Assert.Equal(3,await db.Set<IncidentRevision>().CountAsync());Assert.Equal(receiptCount,await db.Set<IdempotencyRecord>().CountAsync());Assert.Equal(auditCount,await db.Set<AuditEvent>().CountAsync());
        Assert.Equal("logged",await db.Set<OperationalIncident>().AsNoTracking().Where(x=>x.Id==incident.Id).Select(x=>x.State).SingleAsync());
        var retainedIncident = await db.Set<OperationalIncident>().AsNoTracking().SingleAsync(x=>x.Id==incident.Id);
        var retainedRevisions = await db.Set<IncidentRevision>().AsNoTracking().Where(x=>x.IncidentId==incident.Id).OrderBy(x=>x.Id).ToArrayAsync();
        await AssertRetainedMidDowngradeRefused(db, "20260922094628_OperationalDelivery");
        Assert.Equal(retainedIncident.CurrentRevisionId,(await db.Set<OperationalIncident>().AsNoTracking().SingleAsync(x=>x.Id==incident.Id)).CurrentRevisionId);
        Assert.Equal(retainedRevisions.Select(x=>x.Id),await db.Set<IncidentRevision>().AsNoTracking().Where(x=>x.IncidentId==incident.Id).OrderBy(x=>x.Id).Select(x=>x.Id).ToArrayAsync());
        var suspended=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);suspended.State="suspended";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalAccessException>(()=>direct.SaveDescription(f.Underwriter,incident.Id,originalEtag,description.description,descriptionKey,default));
        Assert.Equal(3,await db.Set<IncidentRevision>().CountAsync());
    });
}
