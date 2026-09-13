using System.Diagnostics;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class DiagnosticDispatcherTests
{
    [Fact]
    public async Task RealApiProcessRestartReconcilesCommittedProviderOutcome()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName = "CoverMGA_Test_" + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = ownedName; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var scenarios = new Dictionary<string, Guid>();
        Process? worker = null;
        try
        {
            Guid timeoutId;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();
                foreach (var scenario in new[] { "success", "reject", "fail-once", "timeout-after-success" })
                {
                    var setting = new SettingVersion { Scope = "diagnostic-probe/" + scenario, Version = 1, EffectiveFrom = DateTimeOffset.UtcNow,
                        Values = JsonSerializer.Serialize(new { kind = SqlJobLeases.DiagnosticKind, scenario }) };
                    db.Add(setting); scenarios[scenario] = setting.Id;
                }
                var job = Job("timeout-after-success", scenarios);
                timeoutId = job.Id; db.Add(job); await db.SaveChangesAsync();
            }
            worker = StartWorker(connection.ConnectionString, ownedName);
            await UntilAsync(options, worker, db => db.Set<OutboxWork>().AnyAsync(x => x.Id == timeoutId && x.State == "pending" && x.Attempts == 1));
            StopWorker(worker); worker = null;
            Guid providerId;
            string providerResult;
            await using (var db = new BackOfficeDbContext(options))
            {
                var result = await db.Set<DemoProviderOperation>().SingleAsync();
                Assert.Equal("succeeded", result.State);
                providerId = result.Id; providerResult = result.Result!;
                Assert.Empty(await db.Set<DiagnosticReceipt>().ToListAsync());
                Assert.Empty(await db.Set<AdapterInbox>().ToListAsync());
                foreach (var scenario in new[] { "success", "reject", "fail-once" }) db.Add(Job(scenario, scenarios));
                await db.SaveChangesAsync();
            }
            worker = StartWorker(connection.ConnectionString, ownedName);
            await UntilAsync(options, worker, async db => await db.Set<OutboxWork>().CountAsync(x => x.State == "succeeded" || x.State == "failed") == 4);
            StopWorker(worker); worker = null;
            await using (var db = new BackOfficeDbContext(options))
            {
                var result = await db.Set<DemoProviderOperation>().SingleAsync(x => x.Id == providerId);
                Assert.Equal(providerResult, result.Result);
                Assert.Equal(4, await db.Set<DemoProviderOperation>().CountAsync());
                Assert.Equal(4, await db.Set<AdapterInbox>().CountAsync());
                Assert.Equal(3, await db.Set<DiagnosticReceipt>().CountAsync());
                Assert.Equal(6, await db.Set<AdapterAttempt>().CountAsync());
                Assert.Equal("provider-rejected", (await db.Set<JobException>().SingleAsync()).Code);
                Assert.Equal(2, (await db.Set<OutboxWork>().SingleAsync(x => x.Id == timeoutId)).Attempts);
                Assert.Equal(3, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "diagnostic.completed"));
                Assert.Equal(1, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "diagnostic.rejected"));
            }
        }
        finally
        {
            if (worker is not null) StopWorker(worker);
            if (connection.InitialCatalog != ownedName) throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static OutboxWork Job(string scenario, Dictionary<string, Guid> scenarios) => new()
    {
        Kind = SqlJobLeases.DiagnosticKind, OperationKey = "hosted-" + scenario, ScenarioVersionId = scenarios[scenario],
        Payload = "{\"probe\":\"foundation\"}", NextAttemptAt = DateTimeOffset.UtcNow
    };

    private static Process StartWorker(string connection, string ownedName)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--urls"); start.ArgumentList.Add("http://127.0.0.1:0");
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Cover__SqlConnection"] = connection;
        start.Environment["Cover__DiagnosticWorkerEnabled"] = "true";
        start.Environment["Cover__DataProtectionPath"] = Path.Combine(AppContext.BaseDirectory, ".local", "dispatcher-keys", ownedName);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Worker process did not start.");
        // Drain redirected pipes without emitting SQL configuration or arbitrary exception text.
        process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }

    private static void StopWorker(Process process)
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(10000); }
        process.Dispose();
    }

    private static async Task UntilAsync(DbContextOptions<BackOfficeDbContext> options, Process process, Func<BackOfficeDbContext, Task<bool>> predicate)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(45))
        {
            Assert.False(process.HasExited, "Diagnostic API process exited before completing work.");
            await using var db = new BackOfficeDbContext(options);
            if (await predicate(db)) return;
            await Task.Delay(100);
        }
        Assert.Fail("Diagnostic API process did not reach the expected persisted state within 45 seconds.");
    }
}
