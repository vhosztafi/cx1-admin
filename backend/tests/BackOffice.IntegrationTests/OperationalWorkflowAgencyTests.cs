using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class OperationalWorkflowTests
{
    [Fact]
    public async Task RealSqlAgencyWorkflowRespectsDueWindowAndRetainsActualActivationProvenance()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow; var clock = new WorkflowClock(now);
            var requester = new StaffUser { Email = "workflow-request@cover.example", NormalizedEmail = "WORKFLOW-REQUEST@COVER.EXAMPLE", DisplayName = "Workflow requester" };
            var approver = new StaffUser { Email = "workflow-approve@cover.example", NormalizedEmail = "WORKFLOW-APPROVE@COVER.EXAMPLE", DisplayName = "Workflow approver" };
            var role = new Role { Code = "agency-admin", Scope = "internal" };
            var agency = new Agency { Reference = "AG-WORKFLOW-REVIEW", LegalName = "Fictional agency review", State = "active" };
            var definition = new WorkflowTaskDefinition("workflow-task-1", "published", "agency-review", "agency-follow-up", "agency-onboarding", "Review agency follow-up", "normal", "open", 0, 7, []);
            var rule = new SettingVersion { Scope = "workflow-task/agency-review", Version = 1, EffectiveFrom = now.AddMinutes(-1), Values = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            db.AddRange(requester, approver, role, agency, rule); await db.SaveChangesAsync();
            db.AddRange(new UserRole { UserId = requester.Id, RoleId = role.Id }, new UserRole { UserId = approver.Id, RoleId = role.Id }); await db.SaveChangesAsync();
            var request = new AgencyStateRequest { AgencyId = agency.Id, BaseVersion = agency.RowVersion, RequestedBy = requester.Id, CreatedBy = requester.Id,
                CreatedAt = now.AddMinutes(-2), ProposedInputFingerprint = new string('a', 64), RequestReason = "Fictional agency activation" };
            db.Add(request); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={approver.Id},DecisionReason=N'Independent fictional approval',DecidedAt={now} WHERE Id={request.Id}");
            var due = DateOnly.FromDateTime(now.UtcDateTime).AddDays(90);
            var followUp = new AgencyFollowUp { AgencyId = agency.Id, ActivationRequestId = request.Id, Purpose = "quarter-review", DueOn = due, CreatedBy = approver.Id, CreatedAt = now };
            db.Add(followUp); await db.SaveChangesAsync();
            var factory = new Factory(options); var tasks = new TaskService(factory, new SqlCommandBoundary(factory, clock), clock); var workflows = new WorkflowTaskService(factory, tasks, clock);
            Assert.Null(await workflows.Reconcile(rule.Id, "agency-follow-up", followUp.Id, default));
            Assert.Empty(await db.Set<OperationalTask>().ToArrayAsync());
            clock.Current = now.AddDays(84);
            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => workflows.Reconcile(rule.Id, "agency-follow-up", followUp.Id, default)));
            var taskId = Assert.IsType<Guid>(results[0]); Assert.All(results, result => Assert.Equal(taskId, result));
            var task = await db.Set<OperationalTask>().AsNoTracking().SingleAsync();
            Assert.Equal(due, task.DueOn); Assert.Equal(approver.Id, task.CreatedBy);
            var binding = await db.Set<WorkflowTaskBinding>().AsNoTracking().SingleAsync();
            Assert.Equal(followUp.Id, binding.AgencyFollowUpId); Assert.Contains(request.Id.ToString(), binding.SourceSnapshotJson);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={agency.Id}");
            Assert.Equal(taskId, await new WorkflowTaskService(factory, tasks, clock).Reconcile(rule.Id, "agency-follow-up", followUp.Id, default));
            Assert.Equal("open", (await db.Set<OperationalTask>().AsNoTracking().SingleAsync()).State);
            Assert.Equal(binding.SourceSnapshotJson, (await db.Set<WorkflowTaskBinding>().AsNoTracking().SingleAsync()).SourceSnapshotJson);
            await using (var seedTransaction = await db.Database.BeginTransactionAsync())
            {
                await WorkflowTaskSeed.SeedAsync(db); await seedTransaction.CommitAsync();
            }
            var seeded = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope.StartsWith("workflow-task/demo-")).ToDictionaryAsync(x => x.Id, x => x.Values);
            Assert.Equal(5, seeded.Count);
            await using (var seedTransaction = await db.Database.BeginTransactionAsync())
            {
                await WorkflowTaskSeed.SeedAsync(db); await seedTransaction.CommitAsync();
            }
            Assert.Equal(seeded.OrderBy(x => x.Key), (await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope.StartsWith("workflow-task/demo-")).ToDictionaryAsync(x => x.Id, x => x.Values)).OrderBy(x => x.Key));
            var scan = await new WorkflowTaskScanner(factory, workflows, clock).Scan(new Dictionary<string, int>(), default);
            Assert.Empty(scan.Issues); Assert.Equal(2, scan.Examined); Assert.Equal(1, scan.Associated);
            Assert.Single(await db.Set<OperationalTask>().ToArrayAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agency.Id}");
            var content = Encoding.UTF8.GetBytes("Fictional indemnity evidence");
            var file = new AgencyEvidenceFile { AgencyId = agency.Id, FileName = "fictional-indemnity.txt", ContentType = "text/plain", Content = content,
                ByteLength = content.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), CreatedBy = approver.Id };
            db.Add(file); await db.SaveChangesAsync();
            AgencyEvidence Evidence(DateOnly expires) => new() { AgencyId = agency.Id, Kind = "professional-indemnity", State = "verified", InputFingerprint = new string('a', 64),
                RuleVersionId = rule.Id, FileId = file.Id, AttestedBy = approver.Id, VerifiedAt = clock.Current, ExpiresOn = expires, Notes = "Fictional verified indemnity", CreatedBy = approver.Id, CreatedAt = clock.Current };
            var pi = Evidence(due); db.Add(pi); await db.SaveChangesAsync();
            var expiry = new AgencyFollowUp { AgencyId = agency.Id, EvidenceId = pi.Id, Purpose = "pi-expiry", DueOn = due, CreatedBy = approver.Id, CreatedAt = clock.Current };
            db.Add(expiry); await db.SaveChangesAsync();
            var expiryTask = Assert.IsType<Guid>(await workflows.Reconcile(rule.Id, "agency-follow-up", expiry.Id, default)); Assert.NotEqual(taskId, expiryTask);
            var replacement = Evidence(due.AddYears(1)); db.Add(replacement); await db.SaveChangesAsync();
            Assert.Equal(expiryTask, await workflows.Reconcile(rule.Id, "agency-follow-up", expiry.Id, default));
            using var view = JsonDocument.Parse((await tasks.Read(new ActorContext(approver.Id, null, null, new HashSet<string> { "agency-admin" }), expiryTask, default)).Body);
            Assert.Equal("resolved", view.RootElement.GetProperty("workflow").GetProperty("sourceCondition").GetString()); Assert.Equal("open", view.RootElement.GetProperty("state").GetString());
            Assert.Equal(content, (await db.Set<AgencyEvidenceFile>().AsNoTracking().SingleAsync()).Content);
            Assert.Equal(pi.Id, (await db.Set<AgencyFollowUp>().AsNoTracking().SingleAsync(x => x.Id == expiry.Id)).EvidenceId);
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class WorkflowClock(DateTimeOffset current) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = current;
        public override DateTimeOffset GetUtcNow() => Current;
    }
}
