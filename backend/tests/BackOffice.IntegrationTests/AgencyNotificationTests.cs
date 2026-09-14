using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyNotificationTests
{
    [Fact]
    public void AgencyNotificationPayloadBindsCiphertextToAgencyMessageAndPurpose()
    {
        var provider=new EphemeralDataProtectionProvider();var payload=new AgencyNotificationPayload(provider);
        var agency=Guid.NewGuid();var message=Guid.NewGuid();var envelope=Envelope();
        var cipher=payload.Protect(agency,message,envelope);
        Assert.DoesNotContain(envelope.Content,cipher);Assert.DoesNotContain(envelope.Content,envelope.ToString());
        Assert.Equal(envelope.Content,payload.Unprotect(agency,message,cipher).Content);
        Assert.Throws<CryptographicException>(()=>payload.Unprotect(Guid.NewGuid(),message,cipher));
        Assert.Throws<CryptographicException>(()=>payload.Unprotect(agency,Guid.NewGuid(),cipher));
        Assert.Throws<CryptographicException>(()=>provider.CreateProtector("CoverMGA.SqlSessionTicket.v1").Unprotect(cipher));
        Assert.Throws<CryptographicException>(()=>payload.Unprotect(agency,message,"invalid"));
        Assert.Throws<ArgumentException>(()=>payload.Protect(agency,message,new(){Recipient="bad\naddress",Template="agency-activated",Content="x"}));
    }

    [Fact]
    public async Task RealSqlAgencyNotificationEnqueueIsProtectedAtomicScopedAndImmutable()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var keys=new DirectoryInfo(Path.GetFullPath(Path.Combine(".local","notification-test-keys",owned)));
        var envelope=Envelope();Guid agencyId,otherId,actorId,settingId,notificationId;var operationId=Guid.NewGuid();
        var service=new AgencyNotificationService(new AgencyNotificationPayload(DataProtectionProvider.Create(keys)),TimeProvider.System);
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();Assert.False(db.Database.HasPendingModelChanges());
                await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                actorId=(await db.Set<StaffUser>().FirstAsync()).Id;
                var agency=new Agency{Reference="AG-NOTIFY-A",LegalName="Fictional Notification Agency",State="active"};
                var other=new Agency{Reference="AG-NOTIFY-B",LegalName="Fictional Other Agency",State="active"};
                var setting=new SettingVersion{Scope="agency-notification",Version=1,Values="{\"demo\":true,\"scenario\":\"pass\"}",EffectiveFrom=DateTimeOffset.UtcNow};
                db.AddRange(agency,other,setting);await db.SaveChangesAsync();agencyId=agency.Id;otherId=other.Id;settingId=setting.Id;
                await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EnqueueActivation(db,agencyId,operationId,settingId,actorId,envelope));
                await using var tx=await db.Database.BeginTransactionAsync();notificationId=await service.EnqueueActivation(db,agencyId,operationId,settingId,actorId,envelope);await tx.CommitAsync();
            }
            // A fresh protection provider using persisted keys can read the committed envelope.
            var restarted=new AgencyNotificationPayload(DataProtectionProvider.Create(keys));
            await using(var db=new BackOfficeDbContext(options))
            {
                var notification=await db.Set<AgencyNotification>().SingleAsync();var job=await db.Set<OutboxWork>().SingleAsync(x=>x.Id==notification.WorkId);
                Assert.Equal(envelope.Content,restarted.Unprotect(agencyId,notificationId,notification.ProtectedPayload).Content);
                Assert.Equal(notificationId,JsonDocument.Parse(job.Payload).RootElement.GetProperty("notificationId").GetGuid());
                Assert.Single(JsonDocument.Parse(job.Payload).RootElement.EnumerateObject());Assert.Null(job.Result);
                Assert.DoesNotContain(envelope.Content,job.Payload);Assert.DoesNotContain(envelope.Recipient,job.Payload);
                Assert.Equal("pending",job.State);Assert.Empty(await db.Set<AgencyNotificationReceipt>().ToListAsync());
                Assert.DoesNotContain(await db.Set<AuditEvent>().ToListAsync(),x=>(x.After??"").Contains(envelope.Content));
                await using(var tx=await db.Database.BeginTransactionAsync())
                {
                    Assert.Equal(notificationId,await service.EnqueueActivation(db,agencyId,operationId,settingId,actorId,envelope));
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EnqueueActivation(db,agencyId,operationId,settingId,actorId,Envelope("different")));
                    await Assert.ThrowsAsync<InvalidOperationException>(()=>service.EnqueueActivation(db,otherId,operationId,settingId,actorId,envelope));
                    await tx.CommitAsync();
                }
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [AgencyNotification] SET [Purpose]='agency-invitation' WHERE [Id]={notificationId}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AgencyNotification] WHERE [Id]={notificationId}"));
                db.Add(new AgencyNotificationReceipt{AgencyId=otherId,NotificationId=notificationId,Accepted=true,ResultCode="demo-delivered",CompletedAt=DateTimeOffset.UtcNow});
                await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
                await using(var tx=await db.Database.BeginTransactionAsync())
                {await service.EnqueueActivation(db,agencyId,Guid.NewGuid(),settingId,actorId,envelope);await tx.RollbackAsync();}
            }
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Single(await db.Set<AgencyNotification>().ToListAsync());
                Assert.Single(await db.Set<OutboxWork>().Where(x=>x.Kind==AgencyNotificationService.Kind).ToListAsync());
                Assert.Single(await db.Set<AgencyActivity>().Where(x=>x.Action=="agency.notification-queued").ToListAsync());
                var receipt=new AgencyNotificationReceipt{AgencyId=agencyId,NotificationId=notificationId,Accepted=true,ResultCode="demo-delivered",CompletedAt=DateTimeOffset.UtcNow};
                db.Add(receipt);await db.SaveChangesAsync();
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AgencyNotificationReceipt] WHERE [Id]={receipt.Id}"));
            }
        }
        finally
        {
            if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static AgencyDeliveryEnvelope Envelope(string? content=null)=>new(){Recipient="fictional@cover.example",Template="agency-activated",Content=content??"Fictional delivery secret "+Guid.NewGuid().ToString("N")};

    [Fact]
    public async Task RealSqlAgencyNotificationRecoversReceiptsFencesStaleWorkersAndBoundsFailures()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var clock=new NotificationClock();
        var keys=new DirectoryInfo(Path.GetFullPath(Path.Combine(".local","notification-test-keys",owned)));
        var protection=new AgencyNotificationPayload(DataProtectionProvider.Create(keys));
        var service=new AgencyNotificationService(protection,clock);var leases=new SqlJobLeases(factory,clock);
        var worker=new AgencyNotificationWorker(factory,protection,clock);
        try
        {
            Guid agencyId,actorId;var version=0;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                actorId=(await db.Set<StaffUser>().FirstAsync()).Id;
                var agency=new Agency{Reference="AG-WORKER",LegalName="Fictional Worker Agency",State="active"};db.Add(agency);await db.SaveChangesAsync();agencyId=agency.Id;
            }
            foreach(var scenario in new[]{"pass","reject","transient","unavailable","timeout-after-success"})
            {
                Guid notificationId;
                await using(var db=new BackOfficeDbContext(options))
                {
                    var setting=new SettingVersion{Scope="agency-notification",Version=++version,Values=JsonSerializer.Serialize(new{demo=true,scenario}),EffectiveFrom=clock.GetUtcNow()};db.Add(setting);await db.SaveChangesAsync();
                    await using var tx=await db.Database.BeginTransactionAsync();notificationId=await service.EnqueueActivation(db,agencyId,Guid.NewGuid(),setting.Id,actorId,Envelope());await tx.CommitAsync();
                }
                Assert.Null(await leases.ClaimAsync()); // Existing diagnostic worker cannot consume agency work.
                var lease=Assert.IsType<JobLease>(await leases.ClaimKindAsync(AgencyNotificationService.Kind));
                if(scenario=="unavailable")
                {
                    for(var i=1;i<=6;i++)
                    {
                        Assert.Equal(JobFailure.ProviderUnavailable,(await Assert.ThrowsAsync<AgencyNotificationProviderException>(()=>worker.Deliver(lease))).Failure);
                        Assert.True(await leases.FailAsync(lease,JobFailure.ProviderUnavailable));clock.Advance();
                        if(i<6)lease=Assert.IsType<JobLease>(await leases.ClaimKindAsync(AgencyNotificationService.Kind));
                    }
                    Assert.Null(await leases.ClaimKindAsync(AgencyNotificationService.Kind));
                }
                else
                {
                    if(scenario is "transient" or "timeout-after-success")
                    {
                        var failure=await Assert.ThrowsAsync<AgencyNotificationProviderException>(()=>worker.Deliver(lease));
                        Assert.Equal(scenario=="transient"?JobFailure.ProviderUnavailable:JobFailure.ProviderTimeout,failure.Failure);
                        Assert.True(await leases.FailAsync(lease,failure.Failure));clock.Advance();
                        lease=Assert.IsType<JobLease>(await leases.ClaimKindAsync(AgencyNotificationService.Kind));
                    }
                    var receipt=Assert.IsType<Guid>(await worker.Deliver(lease));
                    // Provider receipt survives a discarded worker and an expired lease before local completion.
                    var old=lease;clock.Advance();lease=Assert.IsType<JobLease>(await leases.ClaimKindAsync(AgencyNotificationService.Kind));
                    var restarted=new AgencyNotificationWorker(new PooledDbContextFactory<BackOfficeDbContext>(options),new AgencyNotificationPayload(DataProtectionProvider.Create(keys)),clock);
                    Assert.Equal(receipt,await restarted.Deliver(lease));Assert.False(await restarted.Apply(old,receipt));
                    Assert.False(await leases.FailAsync(old,JobFailure.ProviderUnavailable));
                    Assert.True(await restarted.Apply(lease,receipt));Assert.False(await restarted.Apply(lease,receipt));
                }
                await using(var db=new BackOfficeDbContext(options))
                {
                    var row=await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==notificationId);var job=await db.Set<OutboxWork>().SingleAsync(x=>x.Id==row.WorkId);
                    Assert.Equal(scenario is "reject" or "unavailable"?"failed":"succeeded",job.State);
                    Assert.Equal(scenario=="unavailable"?0:1,await db.Set<AgencyNotificationReceipt>().CountAsync(x=>x.NotificationId==row.Id));
                    if(scenario=="unavailable")Assert.Equal(6,job.Attempts);
                    Assert.DoesNotContain(await db.Set<AdapterAttempt>().Where(x=>x.WorkId==job.Id).ToListAsync(),x=>x.Request.Contains("Fictional delivery secret")||(x.Response??"").Contains("Fictional delivery secret"));
                }
            }
            // Actual API process kill/restart after independently committed provider success.
            var hostedPayload=new AgencyNotificationPayload(DataProtectionProvider.Create(keys,configuration=>configuration.SetApplicationName("CoverMGA.BackOffice.v1")));
            var hostedService=new AgencyNotificationService(hostedPayload,TimeProvider.System);Guid hostedWork;
            await using(var db=new BackOfficeDbContext(options))
            {
                var setting=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-notification"&&x.Version==5);
                await using var tx=await db.Database.BeginTransactionAsync();var id=await hostedService.EnqueueActivation(db,agencyId,Guid.NewGuid(),setting.Id,actorId,Envelope());
                hostedWork=(await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==id)).WorkId;await tx.CommitAsync();
            }
            Process? process=null;
            try
            {
                process=StartNotificationProcess(connection.ConnectionString,keys.FullName);
                await Until(options,process,db=>db.Set<OutboxWork>().AnyAsync(x=>x.Id==hostedWork&&x.State=="pending"&&x.Attempts==1));
                Stop(process);process=null;Guid receiptId;
                await using(var db=new BackOfficeDbContext(options))
                {
                    var notification=await db.Set<AgencyNotification>().SingleAsync(x=>x.WorkId==hostedWork);
                    receiptId=(await db.Set<AgencyNotificationReceipt>().SingleAsync(x=>x.NotificationId==notification.Id)).Id;
                    Assert.Null((await db.Set<OutboxWork>().SingleAsync(x=>x.Id==hostedWork)).Result);
                }
                process=StartNotificationProcess(connection.ConnectionString,keys.FullName);
                await Until(options,process,db=>db.Set<OutboxWork>().AnyAsync(x=>x.Id==hostedWork&&x.State=="succeeded"));
                Stop(process);process=null;
                await using(var db=new BackOfficeDbContext(options))
                {
                    var job=await db.Set<OutboxWork>().SingleAsync(x=>x.Id==hostedWork);Assert.Equal(2,job.Attempts);
                    Assert.Equal(receiptId,JsonDocument.Parse(job.Result!).RootElement.GetProperty("receiptId").GetGuid());
                    var notification=await db.Set<AgencyNotification>().SingleAsync(x=>x.WorkId==hostedWork);
                    Assert.Single(await db.Set<AgencyNotificationReceipt>().Where(x=>x.NotificationId==notification.Id).ToListAsync());
                }
            }
            finally{if(process is not null)Stop(process);}
        }
        finally
        {
            if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private sealed class NotificationClock:TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance()=>now=now.AddHours(1);
    }

    private static Process StartNotificationProcess(string connection,string keys)
    {
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add(typeof(Program).Assembly.Location);start.ArgumentList.Add("--urls");start.ArgumentList.Add("http://127.0.0.1:0");
        start.Environment["DOTNET_ENVIRONMENT"]="Development";start.Environment["ASPNETCORE_ENVIRONMENT"]="Development";
        start.Environment["Cover__SqlConnection"]=connection;start.Environment["Cover__DataProtectionPath"]=keys;
        start.Environment["Cover__DiagnosticWorkerEnabled"]="true";start.Environment["Cover__AgencyNotificationWorkerEnabled"]="true";
        var process=Process.Start(start)??throw new InvalidOperationException("Demo notification process did not start.");
        process.OutputDataReceived+=(_,_)=>{};process.ErrorDataReceived+=(_,_)=>{};process.BeginOutputReadLine();process.BeginErrorReadLine();return process;
    }
    private static void Stop(Process process)
    {if(!process.HasExited){process.Kill(entireProcessTree:true);process.WaitForExit(10000);}process.Dispose();}
    private static async Task Until(DbContextOptions<BackOfficeDbContext> options,Process process,Func<BackOfficeDbContext,Task<bool>> predicate)
    {
        var elapsed=Stopwatch.StartNew();while(elapsed.Elapsed<TimeSpan.FromSeconds(45))
        {Assert.False(process.HasExited,"Demo notification process exited early.");await using var db=new BackOfficeDbContext(options);if(await predicate(db))return;await Task.Delay(100);}
        Assert.Fail("Demo notification process did not reach expected persisted state.");
    }
}
