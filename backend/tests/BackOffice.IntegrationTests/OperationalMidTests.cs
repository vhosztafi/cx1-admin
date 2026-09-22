using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalMidApiHasScopedSavedVersionHistory()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var intent=await db.Set<PolicyMidIntent>().AsNoTracking().SingleAsync();Assert.Equal("new-business",intent.Purpose);
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        var registration=new MidSubmissionRegistration(f.Factory,f.Clock);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_MidSubmission_TestFailure ON MidSubmission AFTER INSERT AS THROW 52099,'Owned MID rollback probe.',1;");
        try{var failure=await Assert.ThrowsAsync<DbUpdateException>(()=>registration.Register(intent.WorkId));Assert.Equal(52099,Assert.IsType<SqlException>(failure.InnerException).Number);}
        finally{await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_MidSubmission_TestFailure;");}
        Assert.Empty(await db.Set<MidSubmission>().ToArrayAsync());Assert.Null(await db.Set<OutboxWork>().Where(x=>x.Id==intent.WorkId).Select(x=>x.ScenarioVersionId).SingleAsync());
        var bridgeKey=Guid.NewGuid().ToString();var bridged=await registration.RegisterMissingInitial(f.Underwriter,version.Id,"Retain existing initial demo intent",bridgeKey);Assert.Equal(intent.WorkId,bridged.ResourceId);Assert.True((await registration.RegisterMissingInitial(f.Underwriter,version.Id,"Retain existing initial demo intent",bridgeKey)).Replayed);Assert.Equal(1,await db.Set<PolicyMidIntent>().CountAsync());
        var id=await registration.Register(intent.WorkId);Assert.Equal(id,await registration.Register(intent.WorkId));
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:OperationalMidWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false")
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","mid-api-keys",db.Database.GetDbConnection().Database)))
            .ConfigureServices(s=>s.AddSingleton<TimeProvider>(f.Clock)));
        using var client=host.CreateClient();var path=$"/api/v1/versions/{version.Id}/mid-submissions";
        using(var anonymous=await client.GetAsync(path))Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})}){login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        using var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("no-store",response.Headers.CacheControl!.ToString());
        var page=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(id,page.GetProperty("items")[0].GetProperty("id").GetGuid());Assert.Equal(version.Id,page.GetProperty("items")[0].GetProperty("policyVersionId").GetGuid());
        using(var missing=await client.GetAsync($"/api/v1/versions/{Guid.NewGuid()}/mid-submissions"))Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        using(var missingCsrf=await client.PostAsJsonAsync($"/api/v1/mid-submissions/{id}/retry",new{reason="Retry fictional submission"}))Assert.Equal(HttpStatusCode.Forbidden,missingCsrf.StatusCode);
    });
}
