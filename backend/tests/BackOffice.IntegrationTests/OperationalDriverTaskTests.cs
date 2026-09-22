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

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalAcceptanceDriverReferralTasksRetainExactRiskScope()=>RunServicingRatingRequests("motor-trade-road-risks","referral-generation",onRated:async(db,password,f,cycle)=>
    {
        using var input=JsonDocument.Parse(cycle.InputJson);var slice=input.RootElement.GetProperty("slices")[0];
        var driverId=slice.GetProperty("input").GetProperty("drivers")[0].GetProperty("id").GetGuid();
        var referral=new ServicingReferral{DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId!.Value,
            Sequence=(await db.Set<ServicingReferral>().Where(x=>x.CycleId==cycle.Id).MaxAsync(x=>(int?)x.Sequence)??0)+1,
            RuleCode="driver-age",Dimension="driver-age",RiskItemId=driverId,TargetKey=driverId,Reason="Synthetic driver task discovery boundary",
            RequiredAuthorityJson=JsonSerializer.Serialize(new{triggers=new[]{new{effectiveAt=slice.GetProperty("effectiveAt").GetDateTimeOffset(),source="authority",requirement=new{ruleCode="driver-age",dimension="driver-age",targetId=driverId}}}}),
            CreatedBy=f.Servicing.UserId,CreatedAt=f.Clock.GetUtcNow(),UpdatedAt=f.Clock.GetUtcNow()};
        db.Add(referral);await db.SaveChangesAsync();
        var rule=await WorkflowRule(db,f.Clock.GetUtcNow(),"driver-context-proof","referral","underwriting-referral");
        var tasks=new TaskService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock);
        var workflows=new WorkflowTaskService(f.Factory,tasks,f.Clock);
        var expected=Assert.IsType<Guid>(await workflows.Reconcile(rule.Id,"servicing-referral",referral.Id,default));
        var other=await db.Set<ServicingReferral>().AsNoTracking().FirstAsync(x=>x.CycleId==cycle.Id&&x.RiskItemId==null);
        var unrelated=Assert.IsType<Guid>(await workflows.Reconcile(rule.Id,"servicing-referral",other.Id,default));
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","driver-task-keys",db.Database.GetDbConnection().Database)))
            .UseSetting("Cover:WorkflowTaskWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false")
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        host.UseKestrel(0);using var client=host.CreateClient();var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email,password})})
        {login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        var route=$"/api/v1/tasks?policyId={policy.Id}&riskItemId={driverId}";
        using var response=await client.GetAsync(route);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var page=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(1,page.GetProperty("totalCount").GetInt32());
        var row=Assert.Single(page.GetProperty("items").EnumerateArray());Assert.Equal(expected,row.GetProperty("id").GetGuid());Assert.NotEqual(unrelated,row.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/api/v1/tasks/{expected}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/v1/tasks?riskItemId={driverId}")).StatusCode);
        Assert.Equal(0,(await client.GetFromJsonAsync<JsonElement>($"/api/v1/tasks?policyId={policy.Id}&riskItemId={Guid.NewGuid()}")).GetProperty("totalCount").GetInt32());
        await DriverTaskBrowser(host,db,email,password,policy.Id,driverId,expected,cycle.DraftId);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={policy.RelationshipId}");
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(route)).StatusCode);
    });
}
