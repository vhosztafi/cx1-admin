using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalAcceptancePolicyTasksIncludeOwnedServicingAndExcludeOtherSubjects()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var drafts=new ServicingDraftService(f.Factory,f.Clock);
        var listed=await drafts.ListAsync(f.Servicing,version.TermId);
        var draft=await drafts.CreateAsync(f.Servicing,version.TermId,Convert.FromBase64String(listed.Etag.Trim('"')),
            new("adjustment",version.Id,JsonSerializer.SerializeToElement(new{localDate="2026-10-01",localTime="00:00",timeZone="Europe/London"}),"Fictional policy task discovery"),Guid.NewGuid().ToString(),Guid.NewGuid());
        var tasks=new TaskService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock);
        async Task<Guid> Create(string kind,Guid parent,string title)
        {
            var subject=await tasks.Register(f.Underwriter,new(kind,parent),Guid.NewGuid().ToString(),default);
            return(await tasks.Create(f.Underwriter,subject.ResourceId,new("servicing",title,"normal",new("unassigned"),null),Guid.NewGuid().ToString(),default)).ResourceId;
        }
        var direct=await Create("policy",version.PolicyId,"Direct policy task");
        var child=await Create("servicing-draft",draft.ResourceId,"Servicing referral follow-up");
        var quote=await Create("quote",f.QuoteId,"Separate source quote task");
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","record-task-keys",db.Database.GetDbConnection().Database)))
            .UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:WorkflowTaskWorkerEnabled","false")
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        using var client=host.CreateClient();var route=$"/api/v1/tasks?policyId={version.PolicyId}&pageSize=1";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(route)).StatusCode);
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email,password})})
        {login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        using var response=await client.GetAsync(route);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var first=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(2,first.GetProperty("totalCount").GetInt32());
        var ids=new List<Guid>{Assert.Single(first.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid()};
        var second=await client.GetFromJsonAsync<JsonElement>(route+"&cursor="+Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!));
        ids.Add(Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(new[]{direct,child}.Order(),ids.Order());Assert.DoesNotContain(quote,ids);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/tasks?policyId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/v1/tasks?policyId=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(route+$"&policyId={version.PolicyId}")).StatusCode);
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==version.PolicyId);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={policy.RelationshipId}");
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(route)).StatusCode);
    });
}
