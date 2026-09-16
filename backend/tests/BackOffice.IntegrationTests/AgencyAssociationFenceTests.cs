using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyAssociationFenceTests
{
    [Theory]
    [InlineData("link")]
    [InlineData("separate")]
    [InlineData("relationship")]
    public async Task RealSqlAssociationWaitsForAgencyBeforeChildrenAndObservesSuspension(string outcome)
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Guid.NewGuid().ToString("N")+"a1";
        try
        {
            ActorContext actor;byte[] version;int clients,relationships,decisions;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();
                await DemoDatabase.SeedAsync(db,password,includeMatches:true);
                actor=new(await db.Set<StaffUser>().Where(x=>x.Email=="underwriter@cover.example").Select(x=>x.Id).SingleAsync(),null,null,new HashSet<string>{"underwriter"});
                version=await db.Set<MatchReview>().Where(x=>x.Id==MatchDemoSeed.ReviewId(1)).Select(x=>x.RowVersion).SingleAsync();
                clients=await db.Set<ClientAccount>().CountAsync();relationships=await db.Set<ClientAgencyRelationship>().CountAsync();decisions=await db.Set<MatchDecision>().CountAsync();
            }
            var observer=new AgencyLockObserver();
            var waitingOptions=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString).AddInterceptors(observer).Options;
            await using var blocker=new BackOfficeDbContext(options);
            await using var suspension=await blocker.Database.BeginTransactionAsync();
            var agencyId=PartyDemoSeed.SecondAgencyId;
            var agency=await blocker.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={agencyId}").SingleAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var pending=Attempt();
            try
            {
                await observer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
                // These locks must remain available while the association waits for its agency.
                await using(var probe=new BackOfficeDbContext(options))
                {
                    probe.Database.SetCommandTimeout(3);
                    await using var transaction=await probe.Database.BeginTransactionAsync();
                    var intakeId=MatchDemoSeed.SubmissionId(1);var clientId=PartyDemoSeed.ClientId(outcome=="relationship" ? 4 : 3);
                    await probe.Set<MatchSubmission>().FromSqlInterpolated($"SELECT * FROM [MatchSubmission] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={intakeId}").SingleAsync();
                    await probe.Set<ClientAccount>().FromSqlInterpolated($"SELECT * FROM [ClientAccount] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={clientId}").SingleAsync();
                    await transaction.RollbackAsync();
                }
                Assert.False(pending.IsCompleted);
                agency.State="suspended";await blocker.SaveChangesAsync();await suspension.CommitAsync();
                var failure=await pending;Assert.Equal(409,failure.Status);Assert.Equal("agency-unavailable",failure.Code);
            }
            finally
            {
                if(blocker.Database.CurrentTransaction is not null)await suspension.RollbackAsync();
                await timeout.CancelAsync();
                try{await pending;}catch(OperationCanceledException){}
            }
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(clients,await db.Set<ClientAccount>().CountAsync());Assert.Equal(relationships,await db.Set<ClientAgencyRelationship>().CountAsync());Assert.Equal(decisions,await db.Set<MatchDecision>().CountAsync());
                var intake=await db.Set<MatchSubmission>().SingleAsync(x=>x.Id==MatchDemoSeed.SubmissionId(1));
                Assert.Null(intake.LinkedClientId);Assert.Null(intake.LinkedRelationshipId);Assert.Null(intake.SeparateClientId);
                Assert.Equal("pending",await db.Set<MatchReview>().Where(x=>x.Id==MatchDemoSeed.ReviewId(1)).Select(x=>x.State).SingleAsync());
                // Historical review remains available after suspension without creating an association.
                await using var transaction=await db.Database.BeginTransactionAsync();
                await MatchService.DecideAsync(db,actor,MatchDemoSeed.ReviewId(1),version,MatchRules.Validate(new("query","Fictional historical clarification")),DateTimeOffset.UtcNow);
                await transaction.CommitAsync();
            }
            async Task<MatchOperationException> Attempt()
            {
                if(outcome=="relationship")
                {
                    using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
                        .UseSetting("Cover:SqlConnection",connection.ConnectionString)
                        .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","association-test-keys",ownedName)))
                        .ConfigureServices(services=>services.AddDbContextFactory<BackOfficeDbContext>(builder=>builder.AddInterceptors(observer))));
                    using var http=host.CreateClient();
                    async Task<string> Csrf()=> (await http.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf",timeout.Token)).GetProperty("requestToken").GetString()!;
                    using var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new {email="servicing@cover.example",password})};
                    login.Headers.Add("X-CSRF-Token",await Csrf());
                    using var loggedIn=await http.SendAsync(login,timeout.Token);loggedIn.EnsureSuccessStatusCode();
                    var route="/api/v1/clients/"+PartyDemoSeed.ClientId(4);
                    using var client=await http.GetAsync(route,timeout.Token);client.EnsureSuccessStatusCode();
                    using var request=new HttpRequestMessage(HttpMethod.Post,route+"/relationships"){Content=JsonContent.Create(new {agencyId})};
                    request.Headers.Add("X-CSRF-Token",await Csrf());request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));request.Headers.Add("If-Match",client.Headers.ETag!.ToString());
                    using var response=await http.SendAsync(request,timeout.Token);
                    Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
                    Assert.Contains("agency-unavailable",await response.Content.ReadAsStringAsync(timeout.Token));
                    return new MatchOperationException(409,"agency-unavailable");
                }
                await using var db=new BackOfficeDbContext(waitingOptions);
                await using var transaction=await db.Database.BeginTransactionAsync(timeout.Token);
                return await Assert.ThrowsAsync<MatchOperationException>(()=>MatchService.DecideAsync(db,actor,MatchDemoSeed.ReviewId(1),version,MatchRules.Validate(new(outcome,"Fictional association racing suspension")),DateTimeOffset.UtcNow,timeout.Token));
            }
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");
            await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class AgencyLockObserver:DbCommandInterceptor
    {
        public TaskCompletionSource Reached {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {
            if(command.CommandText.Contains("[Agency] WITH (UPDLOCK,ROWLOCK)",StringComparison.Ordinal) ||
                command.CommandText.Contains("Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK)",StringComparison.Ordinal))Reached.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
