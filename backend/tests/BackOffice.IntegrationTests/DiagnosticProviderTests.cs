using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class DiagnosticProviderTests
{
    [Fact]
    public async Task RealSqlProviderOutcomeSurvivesTimeoutAndIndependentRestart()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog=ownedName; connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql => sql.UseCompatibilityLevel(160)).Options;
        var factory=new ContextFactory(options);
        var provider=new DiagnosticDemoProvider(factory,TimeProvider.System);
        var scenarios=new Dictionary<string,Guid>();
        try
        {
            await using (var seed=new BackOfficeDbContext(options))
            {
                await seed.Database.MigrateAsync();
                foreach (var name in new[] {"success","reject","fail-once","timeout-after-success"})
                {
                    var setting=new SettingVersion {Scope="diagnostic-probe/"+name,Version=1,EffectiveFrom=DateTimeOffset.UtcNow,
                        Values=JsonSerializer.Serialize(new {kind=SqlJobLeases.DiagnosticKind,scenario=name})};
                    seed.Add(setting); scenarios[name]=setting.Id;
                }
                await seed.SaveChangesAsync();
            }
            var timeout=Lease("timeout",scenarios["timeout-after-success"]);
            var failure=await Assert.ThrowsAsync<DiagnosticProviderException>(() => provider.ExecuteAsync(timeout));
            Assert.Equal(JobFailure.ProviderTimeout,failure.Failure);
            Guid originalId;
            string originalResult;
            await using (var inspect=new BackOfficeDbContext(options))
            {
                var outcome=await inspect.Set<DemoProviderOperation>().SingleAsync(x => x.OperationKey=="timeout");
                Assert.Equal("succeeded",outcome.State); Assert.NotNull(outcome.CompletedAt);
                originalId=outcome.Id; originalResult=outcome.Result!;
                Assert.Empty(await inspect.Set<AdapterInbox>().ToListAsync()); // No local application took place.
            }
            var restarted=new DiagnosticDemoProvider(new ContextFactory(options),TimeProvider.System);
            var recovered=await restarted.ExecuteAsync(timeout with {Token=Guid.NewGuid(),Attempt=2});
            Assert.Equal(originalId,recovered.OperationId); Assert.True(recovered.Accepted);
            Assert.Equal(recovered,JsonSerializer.Deserialize<DiagnosticReply>(originalResult,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal(recovered,await restarted.ExecuteAsync(timeout));
            var conflict=await Assert.ThrowsAsync<DiagnosticProviderException>(() => restarted.ExecuteAsync(timeout with {ScenarioVersionId=scenarios["success"]}));
            Assert.Equal(JobFailure.ProviderConflict,conflict.Failure);
            var invalid=await Assert.ThrowsAsync<DiagnosticProviderException>(() => restarted.ExecuteAsync(timeout with {Payload="{\"probe\":\"foundation\",\"password\":\"not-allowed\"}"}));
            Assert.Equal(JobFailure.InvalidPayload,invalid.Failure);

            var once=Lease("fail-once",scenarios["fail-once"]);
            Assert.Equal(JobFailure.ProviderUnavailable,(await Assert.ThrowsAsync<DiagnosticProviderException>(() => provider.ExecuteAsync(once))).Failure);
            Assert.True((await new DiagnosticDemoProvider(factory,TimeProvider.System).ExecuteAsync(once)).Accepted);
            var rejected=await provider.ExecuteAsync(Lease("reject",scenarios["reject"]));
            Assert.False(rejected.Accepted);
            Assert.Equal(rejected,await restarted.ExecuteAsync(Lease("reject",scenarios["reject"])));
            var concurrent=Lease("concurrent",scenarios["success"]);
            var replies=await Task.WhenAll(provider.ExecuteAsync(concurrent),restarted.ExecuteAsync(concurrent));
            Assert.Equal(replies[0],replies[1]);
            await using (var inspect=new BackOfficeDbContext(options))
            {
                Assert.Equal(4,await inspect.Set<DemoProviderOperation>().CountAsync());
                Assert.Equal(1,await inspect.Set<DemoProviderOperation>().CountAsync(x => x.OperationKey=="timeout"));
                Assert.Equal(1,await inspect.Set<DemoProviderOperation>().CountAsync(x => x.OperationKey=="concurrent"));
            }
        }
        finally
        {
            if (connection.InitialCatalog!=ownedName) throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static JobLease Lease(string key,Guid scenario) => new(Guid.NewGuid(),Guid.NewGuid(),1,SqlJobLeases.DiagnosticKind,key,scenario,"{\"probe\":\"foundation\"}");
    private sealed class ContextFactory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(options);
    }
}
