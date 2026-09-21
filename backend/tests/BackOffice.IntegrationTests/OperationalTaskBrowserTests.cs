using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalTaskBrowserTests
{
    [Fact]
    public async Task RealSqlOperationalTaskBrowser()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "package.json"))) root = root.Parent;
        Assert.NotNull(root);
        const string dist = ".local/next-phase9-03-browser";
        Assert.True(File.Exists(Path.Combine(root.FullName, "apps/backoffice", dist, "BUILD_ID")), "Build the isolated task browser bundle first.");
        var output = Path.Combine(root.FullName, ".local/browser-evidence/tasks", owned); Directory.CreateDirectory(output);
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password);
            var agency = new Agency { Reference = "AG-TASK-BROWSER", LegalName = "Fictional browser task agency" }; db.Add(agency); await db.SaveChangesAsync();
            async Task<ActorContext> Actor(string email)
            {
                var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == email);
                var roles = await (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId == user.Id select role.Code).ToArrayAsync();
                return new(user.Id, user.TeamId, null, roles.ToHashSet(StringComparer.Ordinal));
            }
            var actor = await Actor("agency-admin@cover.example"); var other = await Actor("underwriter@cover.example");
            var contexts = new Factory(options); var service = new TaskService(contexts, new SqlCommandBoundary(contexts, TimeProvider.System), TimeProvider.System);
            var subject = await service.Register(actor, new("agency", agency.Id), "browser-subject", default);
            var write = new TaskWrite("complaint", "Required checklist fixture", "high", new("user", actor.UserId), DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
            var required = await service.Create(actor, subject.ResourceId, write, "browser-required", default);
            var another = await service.Create(other, subject.ResourceId, write with { Title = "Owned here, created elsewhere" }, "browser-other", default);
            // Explicit browser fixture for the workflow checklist added in09-04.
            db.Add(new OperationalTaskChecklist { TaskId = required.ResourceId, Label = "Review fictional evidence", Required = true, Ordinal = 0, CreatedBy = actor.UserId }); await db.SaveChangesAsync();
            var definition = new WorkflowTaskDefinition("workflow-task-1", "published", "browser-failed-job", "job-exception", "data-exception", "Review processing exception", "high", "open", 1, 0, []);
            var workflowRule = new SettingVersion { Scope = "workflow-task/browser-failed-job", Version = 1, EffectiveFrom = DateTimeOffset.UtcNow.AddDays(-1), Values = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            var notification = new AgencyNotification { AgencyId = agency.Id, ProtectedPayload = "Fictional browser fixture", ContentHash = new byte[32], CreatedBy = other.UserId };
            var work = new OutboxWork { Kind = "agency-notification", SubjectRecordId = agency.Id, Payload = JsonSerializer.Serialize(new { notificationId = notification.Id }), OperationKey = "browser-workflow", State = "failed", CreatedBy = other.UserId, NextAttemptAt = DateTimeOffset.UtcNow };
            db.AddRange(workflowRule, work); await db.SaveChangesAsync(); notification.WorkId = work.Id;
            var failure = new JobException { WorkId = work.Id, Code = "demo-failure", OccurredAt = DateTimeOffset.UtcNow };
            db.AddRange(notification, failure); await db.SaveChangesAsync();
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0").UseSetting("Cover:SqlConnection", connection.ConnectionString)
                .UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:WorkflowTaskWorkerEnabled", "true").UseSetting("Cover:DataProtectionPath", Path.Combine(output, "keys")));
            host.UseKestrel(0); using var client = host.CreateClient();
            var api = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(); Assert.True(new Uri(api).IsLoopback); Assert.NotEqual(5000, new Uri(api).Port);
            Guid? generatedTask = null;
            for (var attempt = 0; attempt < 80 && generatedTask is null; attempt++)
            {
                generatedTask = await db.Set<WorkflowTaskBinding>().AsNoTracking().Where(x => x.RuleVersionId == workflowRule.Id && x.JobExceptionId == failure.Id).Select(x => (Guid?)x.TaskId).SingleOrDefaultAsync();
                if (generatedTask is null) await Task.Delay(250);
            }
            var workflowId = Assert.IsType<Guid>(generatedTask);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET State=N'succeeded',CompletedAt={DateTimeOffset.UtcNow} WHERE Id={work.Id}");
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var webOrigin = $"http://127.0.0.1:{port}";
            Process Start(IEnumerable<string> args, Dictionary<string, string> env)
            {
                var info = new ProcessStartInfo("node") { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in args) info.ArgumentList.Add(arg); foreach (var (key, value) in env) info.Environment[key] = value;
                return Process.Start(info) ?? throw new InvalidOperationException("Task browser process could not start.");
            }
            using var web = Start(["apps/backoffice/node_modules/next/dist/bin/next", "start", "apps/backoffice", "--hostname", "127.0.0.1", "--port", port.ToString()], new() { ["BACKOFFICE_API_ORIGIN"] = api, ["COVER_NEXT_DIST_DIR"] = dist });
            var webOut = web.StandardOutput.ReadToEndAsync(); var webErr = web.StandardError.ReadToEndAsync(); Process? browser = null;
            try
            {
                using var probe = new HttpClient(); var serving = false;
                for (var attempt = 0; attempt < 80 && !web.HasExited; attempt++) { try { using var response = await probe.GetAsync(webOrigin + "/login"); if (response.IsSuccessStatusCode) { serving = true; break; } } catch (HttpRequestException) { } await Task.Delay(250); }
                Assert.True(serving, "Isolated task web preview did not start.");
                var fixture = JsonSerializer.Serialize(new { apiOrigin = api, webOrigin, output, agencyId = agency.Id, actorId = actor.UserId, requiredId = required.ResourceId, otherId = another.ResourceId, workflowId });
                browser = Start(["scripts/verify-task-browser.mjs", "--worker"], new() { ["COVER_TASK_BROWSER_FIXTURE"] = fixture, ["COVER_TASK_BROWSER_PASSWORD"] = password });
                var browserOut = browser.StandardOutput.ReadToEndAsync(); var browserErr = browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(8));
                var errors = await browserErr; await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), await browserOut + errors); Assert.True(browser.ExitCode == 0, errors);
                var created = await db.Set<OperationalTask>().AsNoTracking().SingleAsync(x => x.Title == "Browser persisted task");
                Assert.Equal("open", created.State); Assert.Equal(actor.UserId, created.CreatedBy);
                Assert.True(await db.Set<OperationalTaskComment>().AnyAsync(x => x.TaskId == created.Id && x.Body == "Browser saved internal comment"));
                Assert.True(await db.Set<OperationalTaskChecklist>().Where(x => x.TaskId == required.ResourceId).AllAsync(x => x.Completed));
                Assert.Contains(await db.Set<OperationalTaskEvent>().Where(x => x.TaskId == created.Id).ToArrayAsync(), x => x.Reason == "Browser reopened for follow-up");
                Assert.Equal("open", (await db.Set<OperationalTask>().AsNoTracking().SingleAsync(x => x.Id == workflowId)).State);
                Assert.Equal(failure.Id, (await db.Set<WorkflowTaskBinding>().AsNoTracking().SingleAsync(x => x.TaskId == workflowId)).JobExceptionId);
                await File.WriteAllTextAsync(Path.Combine(output, "sql-readback.json"), JsonSerializer.Serialize(new { passed = true, created.Id, tasks = await db.Set<OperationalTask>().CountAsync(), events = await db.Set<OperationalTaskEvent>().CountAsync() }));
                Directory.CreateDirectory(Path.Combine(root.FullName, ".local/phase9-03-browser"));
                await File.WriteAllTextAsync(Path.Combine(root.FullName, ".local/phase9-03-browser/latest.json"), JsonSerializer.Serialize(new { output, passed = true, verifiedAt = DateTimeOffset.UtcNow }));
            }
            finally
            {
                if (browser is not null) { if (!browser.HasExited) browser.Kill(true); browser.Dispose(); }
                if (!web.HasExited) web.Kill(true); await web.WaitForExitAsync(); await File.WriteAllTextAsync(Path.Combine(output, "web.log"), await webOut + await webErr);
            }
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext> { public BackOfficeDbContext CreateDbContext() => new(options); }
}
