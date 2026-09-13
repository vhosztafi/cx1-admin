using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class DiagnosticInboxTests
{
    [Fact]
    public async Task RealSqlCompletionIsAtomicFencedAndChangedDuplicatesAreQuarantined()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog=ownedName; connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql => sql.UseCompatibilityLevel(160)).Options;
        var factory=new ContextFactory(options);
        var time=new AdjustableTime();
        try
        {
            Guid jobId;
            await using (var seed=new BackOfficeDbContext(options))
            {
                await seed.Database.MigrateAsync();
                var setting=new SettingVersion {Scope="diagnostic-probe/timeout-after-success",Version=1,EffectiveFrom=time.Now,
                    Values=JsonSerializer.Serialize(new {kind=SqlJobLeases.DiagnosticKind,scenario="timeout-after-success"})};
                var job=new OutboxWork {Kind=SqlJobLeases.DiagnosticKind,OperationKey="inbox-recovery",ScenarioVersionId=setting.Id,Payload="{\"probe\":\"foundation\"}",NextAttemptAt=time.Now};
                seed.AddRange(setting,job); await seed.SaveChangesAsync(); jobId=job.Id;
            }
            var leases=new SqlJobLeases(factory,time);
            var first=Assert.IsType<JobLease>(await leases.ClaimAsync());
            var provider=new DiagnosticDemoProvider(factory,time);
            Assert.Equal(JobFailure.ProviderTimeout,(await Assert.ThrowsAsync<DiagnosticProviderException>(() => provider.ExecuteAsync(first))).Failure);
            var reply=await new DiagnosticDemoProvider(new ContextFactory(options),time).ExecuteAsync(first);
            var inbox=new DiagnosticInbox(factory,time);
            await Assert.ThrowsAsync<DiagnosticProviderException>(() => inbox.ApplyAsync(first,reply with {Reference="altered-before-first-application"}));
            time.Now+=SqlJobLeases.LeaseDuration+TimeSpan.FromSeconds(1);
            Assert.Equal(InboxApplication.StaleLease,await inbox.ApplyAsync(first,reply));
            var current=Assert.IsType<JobLease>(await new SqlJobLeases(new ContextFactory(options),time).ClaimAsync());
            var faultOptions=new DbContextOptionsBuilder<BackOfficeDbContext>(options).AddInterceptors(new CrashAfterSqlWrites()).Options;
            await Assert.ThrowsAsync<InjectedCrashException>(() => new DiagnosticInbox(new ContextFactory(faultOptions),time).ApplyAsync(current,reply));
            await using (var inspect=new BackOfficeDbContext(options))
            {
                Assert.Empty(await inspect.Set<AdapterInbox>().ToListAsync());
                Assert.Empty(await inspect.Set<DiagnosticReceipt>().ToListAsync());
                Assert.Empty(await inspect.Set<AuditEvent>().ToListAsync());
                Assert.Equal("leased",(await inspect.Set<OutboxWork>().SingleAsync(x => x.Id==jobId)).State);
                Assert.Equal("succeeded",(await inspect.Set<DemoProviderOperation>().SingleAsync()).State);
            }
            var applications=await Task.WhenAll(inbox.ApplyAsync(current,reply),new DiagnosticInbox(new ContextFactory(options),time).ApplyAsync(current,reply));
            Assert.Single(applications,x => x==InboxApplication.Applied);
            Assert.Single(applications,x => x==InboxApplication.Duplicate);
            Assert.Equal(InboxApplication.Duplicate,await inbox.ApplyAsync(first,reply));
            var changed=reply with {Reference="changed-duplicate"};
            Assert.Equal(InboxApplication.Quarantined,await inbox.ApplyAsync(current,changed));
            Assert.Equal(InboxApplication.Quarantined,await inbox.ApplyAsync(current,changed));
            await using (var inspect=new BackOfficeDbContext(options))
            {
                var job=await inspect.Set<OutboxWork>().SingleAsync(x => x.Id==jobId);
                Assert.Equal("succeeded",job.State); Assert.Null(job.LeaseToken);
                var receipt=Assert.Single(await inspect.Set<DiagnosticReceipt>().ToListAsync());
                Assert.Equal(reply.Reference,receipt.Reference); Assert.Equal(reply.OperationId,receipt.ProviderOperationId);
                Assert.Equal("applied",(await inspect.Set<AdapterInbox>().SingleAsync()).State);
                Assert.Equal(1,await inspect.Set<AdapterQuarantine>().CountAsync());
                Assert.Equal(1,await inspect.Set<AuditEvent>().CountAsync(x => x.EventType=="diagnostic.completed"));
                Assert.Equal(1,await inspect.Set<AuditEvent>().CountAsync(x => x.EventType=="diagnostic.callback-quarantined"));
                Assert.Equal("succeeded",(await inspect.Set<AdapterAttempt>().SingleAsync(x => x.WorkId==jobId && x.AttemptNumber==2)).Outcome);
                var rejectionSetting=new SettingVersion {Scope="diagnostic-probe/reject",Version=1,EffectiveFrom=time.Now,
                    Values=JsonSerializer.Serialize(new {kind=SqlJobLeases.DiagnosticKind,scenario="reject"})};
                inspect.Add(rejectionSetting);
                inspect.Add(new OutboxWork {Kind=SqlJobLeases.DiagnosticKind,OperationKey="reject-inbox",ScenarioVersionId=rejectionSetting.Id,
                    Payload="{\"probe\":\"foundation\"}",NextAttemptAt=time.Now});
                await inspect.SaveChangesAsync();
            }
            var rejectedLease=Assert.IsType<JobLease>(await leases.ClaimAsync());
            var rejection=await provider.ExecuteAsync(rejectedLease);
            Assert.False(rejection.Accepted);
            Assert.Equal(InboxApplication.Applied,await inbox.ApplyAsync(rejectedLease,rejection));
            Assert.Equal(InboxApplication.Duplicate,await inbox.ApplyAsync(rejectedLease,rejection));
            await using (var inspect=new BackOfficeDbContext(options))
            {
                Assert.Equal("failed",(await inspect.Set<OutboxWork>().SingleAsync(x => x.Id==rejectedLease.WorkId)).State);
                Assert.Equal(1,await inspect.Set<JobException>().CountAsync(x => x.WorkId==rejectedLease.WorkId));
                Assert.Equal(1,await inspect.Set<DiagnosticReceipt>().CountAsync()); // Rejection creates no successful effect.
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
    private sealed class InjectedCrashException : Exception;
    // Test DI only. There is no HTTP fault-injection endpoint or production setting.
    private sealed class CrashAfterSqlWrites : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,int result,CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<DiagnosticReceipt>().Any()) throw new InjectedCrashException();
            return ValueTask.FromResult(result);
        }
    }
}
