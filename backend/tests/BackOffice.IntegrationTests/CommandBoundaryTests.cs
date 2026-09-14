using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class CommandBoundaryTests
{
    [Fact]
    public async Task RealSqlAtomicRollbackConcurrentReplayAndDurableReceipts()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");
        connection.InitialCatalog=ownedName; connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql => sql.UseCompatibilityLevel(160)).Options;
        var factory=new ContextFactory(options);
        var time=new AdjustableTime();
        var boundary=new SqlCommandBoundary(factory,time);
        try
        {
            Guid actorId;
            await using (var seed=new BackOfficeDbContext(options))
            {
                await seed.Database.MigrateAsync();
                await DemoDatabase.SeedAsync(seed,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                actorId=await seed.Set<StaffUser>().Where(x => x.Email=="system-admin@cover.example").Select(x => x.Id).SingleAsync();
            }
            var failed=new CommandIdentity(actorId,"/internal/foundation-probe","rollback",Guid.NewGuid());
            await Assert.ThrowsAsync<DbUpdateException>(() => boundary.ExecuteAsync(failed,new {name="Rolled back"},"foundation.probe",(db,token) =>
            {
                var team=new Team {Name="Rolled back"}; db.Add(team);
                db.Add(new OutboxWork {Kind="foundation-probe",OperationKey=failed.Key,Payload="invalid JSON",NextAttemptAt=time.GetUtcNow()});
                return Task.FromResult(Outcome(team));
            }));
            await using (var check=new BackOfficeDbContext(options))
            {
                Assert.False(await check.Set<Team>().AnyAsync(x => x.Name=="Rolled back"));
                Assert.False(await check.Set<OutboxWork>().AnyAsync(x => x.OperationKey==failed.Key));
                Assert.False(await check.Set<AuditEvent>().AnyAsync(x => x.CorrelationId==failed.CorrelationId));
                Assert.False(await check.Set<IdempotencyRecord>().AnyAsync(x => x.Key==failed.Key));
            }
            // A handler can flush SQL within the transaction; a crash still rolls it all back.
            var crashed=failed with {Key="crash",CorrelationId=Guid.NewGuid()};
            await Assert.ThrowsAsync<InjectedCrashException>(() => boundary.ExecuteAsync(crashed,new {name="Crash before commit"},"foundation.probe",async (db,token) =>
            {
                db.Add(new Team {Name="Crash before commit"}); await db.SaveChangesAsync(token); throw new InjectedCrashException();
            }));
            await using (var check=new BackOfficeDbContext(options)) Assert.False(await check.Set<Team>().AnyAsync(x => x.Name=="Crash before commit"));

            var command=failed with {Key="concurrent",CorrelationId=Guid.NewGuid()};
            var calls=0;
            var results=await Task.WhenAll(Enumerable.Range(0,3).Select(_ => boundary.ExecuteAsync(command,new {name="Committed once"},"foundation.probe",async (db,token) =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(50,token);
                var team=new Team {Name="Committed once"}; db.Add(team);
                db.Add(new OutboxWork {Kind="foundation-probe",OperationKey=command.Key,Payload="{}",NextAttemptAt=time.GetUtcNow()});
                return Outcome(team);
            })));
            Assert.Equal(1,calls); Assert.Equal(2,results.Count(x => x.Replayed));
            Assert.Single(results.Select(x => x.ResourceId).Distinct());
            time.Now=time.Now.AddDays(2);
            var restarted=new SqlCommandBoundary(new ContextFactory(options),time);
            var replay=await restarted.ExecuteAsync(command,new {name="Committed once"},"foundation.probe",(_,_) => throw new DbUpdateConcurrencyException("Stale edit version must not run before replay."));
            Assert.True(replay.Replayed); Assert.Equal(results[0].Body,replay.Body);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => restarted.ExecuteAsync(command,new {name="Different intent"},"foundation.probe",(_,_) => throw new InvalidOperationException("Must not execute.")));

            foreach (var (key,name) in new[] {("CaseKey","Upper key effect"),("casekey","Lower key effect")})
                await boundary.ExecuteAsync(command with {Key=key},new {name},"foundation.probe",(db,_) => {var team=new Team {Name=name};db.Add(team);return Task.FromResult(Outcome(team));});
            await using (var check=new BackOfficeDbContext(options))
            {
                Assert.Equal(1,await check.Set<OutboxWork>().CountAsync(x => x.OperationKey==command.Key));
                Assert.Equal(1,await check.Set<Team>().CountAsync(x => x.Name=="Committed once"));
                Assert.Equal(3,await check.Set<IdempotencyRecord>().CountAsync());
                Assert.Equal(3,await check.Set<AuditEvent>().CountAsync(x => x.EventType=="foundation.probe"));
                var receipt=await check.Set<IdempotencyRecord>().SingleAsync(x => x.Key==command.Key);
                Assert.True(receipt.ExpiresAt<time.GetUtcNow());
                var update=await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlRawAsync("UPDATE [AuditEvent] SET [EventType]='tampered' WHERE [EventType]='foundation.probe'"));
                Assert.Equal(51002,update.Number);
                var delete=await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlRawAsync("DELETE FROM [IdempotencyRecord]"));
                Assert.Equal(51003,delete.Number);
            }

            var versioned=command with {Key="versioned",CorrelationId=Guid.NewGuid()};
            var original=await boundary.ExecuteAsync(versioned,new {name="Versioned original"},"foundation.probe",async (db,token) =>
            {
                var team=new Team {Name="Versioned original"};db.Add(team);
                await db.SaveChangesAsync(token); // Generate rowversion without committing the command transaction.
                return Outcome(team) with {Etag="\""+Convert.ToBase64String(team.RowVersion)+"\""};
            });
            Assert.NotNull(original.Etag);
            await using (var changed=new BackOfficeDbContext(options))
            {
                var team=await changed.Set<Team>().SingleAsync(x => x.Id==original.ResourceId);
                team.Name="Later authorized version";await changed.SaveChangesAsync();
                Assert.NotEqual(original.Etag,"\""+Convert.ToBase64String(team.RowVersion)+"\"");
            }
            var originalReplay=await restarted.ExecuteAsync(versioned,new {name="Versioned original"},"foundation.probe",(_,_) => throw new DbUpdateConcurrencyException());
            Assert.True(originalReplay.Replayed);Assert.Equal(original.Body,originalReplay.Body);
            Assert.Equal(original.Etag,originalReplay.Etag);Assert.Equal(original.Status,originalReplay.Status);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => restarted.ExecuteAsync(versioned,new {name="Changed intent"},"foundation.probe",(_,_) => throw new InvalidOperationException()));

            foreach (var invalidTag in new[] {"W/\"weak\"","\"bad\r\nheader\"","\"space token\"","\"inner\"quote\"",new string('x',101)})
            {
                var invalidCommand=command with {Key="invalid-tag-"+Guid.NewGuid().ToString("N"),CorrelationId=Guid.NewGuid()};
                await Assert.ThrowsAsync<InvalidOperationException>(() => boundary.ExecuteAsync(invalidCommand,new {name="Rollback invalid ETag"},"foundation.probe",async (db,token) =>
                {
                    var team=new Team {Name="Rollback invalid ETag"};db.Add(team);await db.SaveChangesAsync(token);
                    return Outcome(team) with {Etag=invalidTag};
                }));
                await using var inspect=new BackOfficeDbContext(options);
                Assert.False(await inspect.Set<Team>().AnyAsync(x => x.Name=="Rollback invalid ETag"));
                Assert.False(await inspect.Set<IdempotencyRecord>().AnyAsync(x => x.Key==invalidCommand.Key));
                Assert.False(await inspect.Set<AuditEvent>().AnyAsync(x => x.CorrelationId==invalidCommand.CorrelationId));
            }
            // Explicit old JSON shape verifies compatibility, rather than serializing the new null field.
            var legacy=command with {Key="legacy-response"};var legacyBody="{\"legacy\":true}";
            await using (var insertLegacy=new BackOfficeDbContext(options))
            {
                insertLegacy.Add(new IdempotencyRecord {ActorScope=actorId.ToString("N"),Route=legacy.Route,Key=legacy.Key,
                    RequestHash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {legacy=true})),ResultStatus=200,
                    ResultBody=JsonSerializer.Serialize(new {ResourceId=original.ResourceId,Body=legacyBody}),ExpiresAt=time.GetUtcNow().AddDays(1)});
                await insertLegacy.SaveChangesAsync();
            }
            var legacyReplay=await restarted.ExecuteAsync(legacy,new {legacy=true},"foundation.probe",(_,_) => throw new InvalidOperationException());
            Assert.True(legacyReplay.Replayed);Assert.Null(legacyReplay.Etag);Assert.Equal(legacyBody,legacyReplay.Body);
        }
        finally
        {
            if (connection.InitialCatalog!=ownedName) throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static CommandOutcome Outcome(Team team) => new(team.Id,201,JsonSerializer.Serialize(new {id=team.Id,name=team.Name}));
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
}
