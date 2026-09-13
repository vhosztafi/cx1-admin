using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class JobLeaseTests
{
    [Fact]
    public async Task RealSqlClaimsAreExclusiveAndExpiredOwnersCannotWrite()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog=ownedName; connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql => sql.UseCompatibilityLevel(160)).Options;
        var time=new AdjustableTime();
        var factory=new ContextFactory(options);
        var leases=new SqlJobLeases(factory,time);
        try
        {
            Guid workId;
            Guid scenarioId;
            await using (var seed=new BackOfficeDbContext(options))
            {
                await seed.Database.MigrateAsync();
                var scenario=new SettingVersion {Scope="diagnostic-success",Version=1,Values="{\"scenario\":\"success\"}",EffectiveFrom=time.GetUtcNow()};
                var work=new OutboxWork {Kind=SqlJobLeases.DiagnosticKind,OperationKey="lease-probe",ScenarioVersionId=scenario.Id,Payload="{}",NextAttemptAt=time.GetUtcNow()};
                seed.AddRange(scenario,work); await seed.SaveChangesAsync(); workId=work.Id; scenarioId=scenario.Id;
            }
            var parallel=await Task.WhenAll(leases.ClaimAsync(),leases.ClaimAsync());
            var first=Assert.Single(parallel,x => x is not null)!;
            Assert.Equal(workId,first.WorkId); Assert.Equal(1,first.Attempt); Assert.Equal(scenarioId,first.ScenarioVersionId);
            time.Now+=SqlJobLeases.LeaseDuration+TimeSpan.FromSeconds(1);
            Assert.False(await leases.FailAsync(first,JobFailure.ProviderRejected));
            var restarted=new SqlJobLeases(new ContextFactory(options),time);
            var second=Assert.IsType<JobLease>(await restarted.ClaimAsync());
            Assert.Equal(2,second.Attempt); Assert.NotEqual(first.Token,second.Token);
            Assert.False(await leases.FailAsync(first,JobFailure.InvalidPayload));
            Assert.True(await restarted.FailAsync(second,JobFailure.ProviderUnavailable));
            Assert.Null(await restarted.ClaimAsync());
            await using (var check=new BackOfficeDbContext(options))
            {
                var oldAttempt=await check.Set<AdapterAttempt>().SingleAsync(x => x.WorkId==workId && x.AttemptNumber==1);
                Assert.Equal("lease-expired",oldAttempt.Outcome); Assert.NotNull(oldAttempt.EndedAt);
                var job=await check.Set<OutboxWork>().SingleAsync(x => x.Id==workId);
                Assert.Equal("pending",job.State); Assert.Null(job.LeaseToken);
                time.Now=job.NextAttemptAt;
            }
            var current=Assert.IsType<JobLease>(await restarted.ClaimAsync());
            while (current.Attempt<RetrySchedule.MaximumAttempts)
            {
                Assert.True(await restarted.FailAsync(current,JobFailure.ProviderUnavailable));
                await using var check=new BackOfficeDbContext(options);
                time.Now=(await check.Set<OutboxWork>().SingleAsync(x => x.Id==workId)).NextAttemptAt;
                current=Assert.IsType<JobLease>(await restarted.ClaimAsync());
            }
            // The last owner disappears: reclaim must terminally fail instead of leasing forever.
            time.Now+=SqlJobLeases.LeaseDuration+TimeSpan.FromSeconds(1);
            Assert.Null(await restarted.ClaimAsync());
            Assert.Null(await restarted.ClaimAsync());
            Assert.False(await restarted.FailAsync(current,JobFailure.ProviderUnavailable));
            await using (var check=new BackOfficeDbContext(options))
            {
                var job=await check.Set<OutboxWork>().SingleAsync(x => x.Id==workId);
                Assert.Equal("failed",job.State); Assert.Equal("attempts-exhausted",job.ErrorCode); Assert.NotNull(job.CompletedAt);
                Assert.Equal(6,await check.Set<AdapterAttempt>().CountAsync(x => x.WorkId==workId));
                Assert.Equal(1,await check.Set<JobException>().CountAsync(x => x.WorkId==workId));
                var rejection=new OutboxWork {Kind=SqlJobLeases.DiagnosticKind,OperationKey="reject-probe",ScenarioVersionId=scenarioId,NextAttemptAt=time.GetUtcNow()};
                check.Add(rejection); await check.SaveChangesAsync();
            }
            var rejected=Assert.IsType<JobLease>(await restarted.ClaimAsync());
            Assert.True(await restarted.FailAsync(rejected,JobFailure.ProviderRejected));
            Assert.False(await restarted.FailAsync(rejected,JobFailure.ProviderRejected));
            await using (var check=new BackOfficeDbContext(options)) Assert.Equal(1,await check.Set<JobException>().CountAsync(x => x.WorkId==rejected.WorkId));
            await using (var check=new BackOfficeDbContext(options))
            {
                var job=await check.Set<OutboxWork>().SingleAsync(x => x.Id==workId);
                job.AttemptLimit=JobRetryBudget.ExpandedLimit(job.State,job.ErrorCode,job.Attempts,job.AttemptLimit)!.Value;
                job.State="pending"; job.CompletedAt=null; job.ErrorCode=null; job.NextAttemptAt=time.Now;
                await check.SaveChangesAsync();
            }
            for (var number=7; number<=12; number++)
            {
                var recovery=Assert.IsType<JobLease>(await restarted.ClaimAsync());
                Assert.Equal(number,recovery.Attempt); Assert.Equal("lease-probe",recovery.OperationKey);
                Assert.True(await restarted.FailAsync(recovery,JobFailure.ProviderUnavailable));
                await using var check=new BackOfficeDbContext(options);
                var job=await check.Set<OutboxWork>().SingleAsync(x => x.Id==workId);
                if (number<12)
                {
                    Assert.Equal("pending",job.State);
                    Assert.Equal(JobRetryBudget.Delay(number,job.OperationKey),job.NextAttemptAt-time.Now);
                    time.Now=job.NextAttemptAt;
                }
                else Assert.Equal("failed",job.State);
            }
            await using (var check=new BackOfficeDbContext(options))
            {
                Assert.Equal(12,await check.Set<AdapterAttempt>().CountAsync(x => x.WorkId==workId));
                Assert.Equal(1,await check.Set<JobException>().CountAsync(x => x.WorkId==workId));
            }
        }
        finally
        {
            if (connection.InitialCatalog!=ownedName) throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class ContextFactory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(options);
    }
    private sealed class AdjustableTime : TimeProvider
    {
        public DateTimeOffset Now {get;set;}=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
