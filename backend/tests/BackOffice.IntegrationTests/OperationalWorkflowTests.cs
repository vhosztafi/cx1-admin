using BackOffice.Infrastructure.Persistence;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class OperationalWorkflowTests
{
    [Fact]
    public async Task RealSqlConcurrentJobWorkflowUsesOwnedSourceAndPreservesHumanCompletion()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var user = new StaffUser { Email = "workflow-actor@cover.example", NormalizedEmail = "WORKFLOW-ACTOR@COVER.EXAMPLE", DisplayName = "Workflow actor" };
            var role = new Role { Code = "agency-admin", Scope = "internal" };
            var agency = new Agency { Reference = "AG-WORKFLOW-OWNER", LegalName = "Fictional workflow owner", State = "active" };
            var other = new Agency { Reference = "AG-WORKFLOW-OTHER", LegalName = "Fictional unrelated agency" };
            var definition = new WorkflowTaskDefinition("workflow-task-1", "published", "failed-delivery", "job-exception", "data-exception", "Review failed delivery", "high", "open", 1, 0, [new("review", "Review the source failure", true)]);
            var rule = new SettingVersion { Scope = "workflow-task/failed-delivery", Version = 1, EffectiveFrom = now.AddMinutes(-1), Values = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            db.AddRange(user, role, agency, other, rule); await db.SaveChangesAsync();
            db.Add(new UserRole { UserId = user.Id, RoleId = role.Id }); await db.SaveChangesAsync();
            async Task<JobException> Failure(string key)
            {
                var notification = new AgencyNotification { AgencyId = agency.Id, ProtectedPayload = "fictional-storage-fixture", ContentHash = new byte[32], CreatedBy = user.Id };
                var work = new OutboxWork { Kind = "agency-notification", SubjectRecordId = agency.Id, Payload = JsonSerializer.Serialize(new { notificationId = notification.Id }), OperationKey = key, CreatedBy = user.Id, NextAttemptAt = now, State = "failed" };
                db.Add(work); await db.SaveChangesAsync();
                notification.WorkId = work.Id; db.Add(notification);
                var failure = new JobException { WorkId = work.Id, Code = "demo-failure", OccurredAt = now }; db.Add(failure); await db.SaveChangesAsync(); return failure;
            }
            var source = await Failure("workflow-first");
            var factory = new Factory(options); var tasks = new TaskService(factory, new SqlCommandBoundary(factory, TimeProvider.System), TimeProvider.System);
            var workflows = new WorkflowTaskService(factory, tasks, TimeProvider.System);
            var unownedWork = new OutboxWork { Kind = "diagnostic-probe", SubjectRecordId = other.Id, ScenarioVersionId = rule.Id, OperationKey = "workflow-unowned", CreatedBy = user.Id, NextAttemptAt = now, State = "failed" };
            db.Add(unownedWork); await db.SaveChangesAsync();
            var unownedFailure = new JobException { WorkId = unownedWork.Id, Code = "demo-failure", OccurredAt = now }; db.Add(unownedFailure); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<OperationalAccessException>(() => workflows.Reconcile(rule.Id, "job-exception", unownedFailure.Id, default));
            Assert.Empty(await db.Set<WorkflowTaskBinding>().ToListAsync());
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => workflows.Reconcile(rule.Id, "job-exception", source.Id, default)));
            var taskId = Assert.IsType<Guid>(results[0]); Assert.All(results, result => Assert.Equal(taskId, result));
            db.ChangeTracker.Clear();
            var binding = await db.Set<WorkflowTaskBinding>().SingleAsync();
            Assert.Equal(agency.Id, (await db.Set<OperationalSubject>().SingleAsync(x => x.Id == binding.SubjectId)).AgencyId);
            var actor = new ActorContext(user.Id, null, null, new HashSet<string> { "agency-admin" });
            var saved = await db.Set<OperationalTask>().AsNoTracking().SingleAsync();
            Assert.Equal("high", saved.Priority); Assert.Equal(user.Id, saved.CreatedBy);
            Assert.Contains("Review the source failure", (await db.Set<OperationalTaskEvent>().SingleAsync()).SnapshotJson);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET State=N'succeeded',CompletedAt={DateTimeOffset.UtcNow} WHERE Id={source.WorkId}");
            Assert.Equal(taskId, await workflows.Reconcile(rule.Id, "job-exception", source.Id, default));
            Assert.Equal("open", (await db.Set<OperationalTask>().AsNoTracking().SingleAsync()).State);
            using (var view = JsonDocument.Parse((await tasks.Read(actor, taskId, default)).Body))
            {
                Assert.True(view.RootElement.GetProperty("sourceChanged").GetBoolean());
                Assert.Equal("resolved", view.RootElement.GetProperty("workflow").GetProperty("sourceCondition").GetString());
                Assert.Equal(1, view.RootElement.GetProperty("workflow").GetProperty("ruleVersion").GetInt32());
            }
            await Assert.ThrowsAsync<TaskRuleException>(() => tasks.Transition(actor, taskId, TaskService.Etag(saved.RowVersion), "premature-complete", "completed", "Reviewed", default));
            // Completion goes through the ordinary human task commands.
            var item = await db.Set<OperationalTaskChecklist>().SingleAsync();
            await tasks.Checklist(actor, taskId, TaskService.Etag(saved.RowVersion), "check", [new(item.Id, true)], "Source failure reviewed", default);
            saved = await db.Set<OperationalTask>().AsNoTracking().SingleAsync();
            await tasks.Transition(actor, taskId, TaskService.Etag(saved.RowVersion), "complete", "completed", "Failure reviewed", default);
            var revised = new SettingVersion { Scope = rule.Scope, Version = 2, EffectiveFrom = now, Values = JsonSerializer.Serialize(definition with { Title = "Revised future task" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            db.Add(revised); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<OperationalAccessException>(() => workflows.Reconcile(rule.Id, "job-exception", source.Id, default));
            Assert.Equal(taskId, await new WorkflowTaskService(factory, tasks, TimeProvider.System).Reconcile(revised.Id, "job-exception", source.Id, default));
            Assert.Equal("completed", (await db.Set<OperationalTask>().AsNoTracking().SingleAsync()).State);
            Assert.Equal(rule.Id, (await db.Set<WorkflowTaskBinding>().AsNoTracking().SingleAsync()).RuleVersionId);
            var newSource = await Failure("workflow-second");
            Assert.NotEqual(taskId, await workflows.Reconcile(revised.Id, "job-exception", newSource.Id, default));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={user.Id}");
            await Assert.ThrowsAsync<OperationalAccessException>(() => workflows.Reconcile(revised.Id, "job-exception", source.Id, default));
            Assert.Equal(2, await db.Set<WorkflowTaskBinding>().CountAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={user.Id}");
            var draftRule = new SettingVersion { Scope = rule.Scope, Version = 3, EffectiveFrom = now, Values = JsonSerializer.Serialize(definition with { Publication = "draft" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            db.Add(draftRule); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<TaskRuleException>(() => workflows.Reconcile(draftRule.Id, "job-exception", newSource.Id, default));
            await Assert.ThrowsAsync<OperationalAccessException>(() => workflows.Reconcile(revised.Id, "job-exception", newSource.Id, default));
            var futureRule = new SettingVersion { Scope = rule.Scope, Version = 4, EffectiveFrom = now.AddYears(1), Values = rule.Values };
            db.Add(futureRule); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<OperationalAccessException>(() => workflows.Reconcile(futureRule.Id, "job-exception", newSource.Id, default));
            Assert.Equal(2, await db.Set<WorkflowTaskBinding>().CountAsync());
            var scan = await new WorkflowTaskScanner(factory, workflows, TimeProvider.System).Scan(new Dictionary<string, int>(), default);
            Assert.Equal(0, scan.Examined); Assert.Equal("invalid-workflow-rule", Assert.Single(scan.Issues).Code);
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext> { public BackOfficeDbContext CreateDbContext() => new(options); }

    [Fact]
    public async Task RealSqlBindingPreservesFirstRuleAndSourceAcrossReloadAndRejectsRebinding()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options);
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            var user = new StaffUser { Email = "workflow-storage@cover.example", NormalizedEmail = "WORKFLOW-STORAGE@COVER.EXAMPLE", DisplayName = "Workflow storage" };
            var agency = new Agency { Reference = "AG-WORKFLOW-STORAGE", LegalName = "Fictional workflow storage" };
            var rule = new SettingVersion { Scope = "workflow-task/failed-work", Version = 1, EffectiveFrom = DateTimeOffset.UtcNow, Values = "{\"publication\":\"published\"}" };
            var revised = new SettingVersion { Scope = rule.Scope, Version = 2, EffectiveFrom = rule.EffectiveFrom.AddSeconds(1), Values = "{\"publication\":\"published\",\"revision\":2}" };
            db.AddRange(user, agency, rule, revised); await db.SaveChangesAsync();
            var subject = new OperationalSubject { Kind = "agency", AgencyId = agency.Id, CreatedBy = user.Id };
            var work = new OutboxWork { Kind = "diagnostic-probe", OperationKey = "workflow-storage-only", ScenarioVersionId = rule.Id, CreatedBy = user.Id, NextAttemptAt = DateTimeOffset.UtcNow };
            db.AddRange(subject, work); await db.SaveChangesAsync();
            // This storage fixture is not a supported workflow source adapter.
            var source = new JobException { WorkId = work.Id, Code = "demo-failure", OccurredAt = DateTimeOffset.UtcNow };
            var first = new OperationalTask { SubjectId = subject.Id, Reference = "TSK-WORKFLOW-1", TypeCode = "data-exception", Title = "Review fictional failure", CreatedBy = user.Id, EventSequence = 1 };
            var second = new OperationalTask { SubjectId = subject.Id, Reference = "TSK-WORKFLOW-2", TypeCode = "data-exception", Title = "Second fictional task", CreatedBy = user.Id, EventSequence = 1 };
            db.AddRange(source, first, second); await db.SaveChangesAsync();
            WorkflowTaskBinding Binding(Guid taskId, Guid versionId) => new()
            {
                TaskId = taskId, SubjectId = subject.Id, RuleVersionId = versionId, RuleCode = "failed-work",
                SourceKind = "job-exception", SourceEventId = source.Id, JobExceptionId = source.Id,
                SourceSnapshotJson = "{\"code\":\"demo-failure\"}", SourceHash = new string('A', 64),
                RuleSnapshotJson = rule.Values, RuleHash = new string('B', 64), CreatedBy = user.Id
            };
            var binding = Binding(first.Id, rule.Id); db.Add(binding); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            async Task Rejected(WorkflowTaskBinding row)
            {
                db.Add(row); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            }
            // A published revision cannot claim the same logical rule/source again.
            await Rejected(Binding(second.Id, revised.Id));
            var wrongSubject = Binding(second.Id, revised.Id); wrongSubject.RuleCode = "another-rule"; wrongSubject.SubjectId = Guid.NewGuid();
            await Rejected(wrongSubject);
            var wrongType = Binding(second.Id, revised.Id); wrongType.RuleCode = "another-rule"; wrongType.SourceKind = "quote-referral";
            await Rejected(wrongType);
            var nonexistentSource = Binding(second.Id, revised.Id); nonexistentSource.SourceEventId = Guid.NewGuid(); nonexistentSource.JobExceptionId = nonexistentSource.SourceEventId;
            await Rejected(nonexistentSource);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE WorkflowTaskBinding SET RuleVersionId={revised.Id} WHERE Id={binding.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM WorkflowTaskBinding WHERE Id={binding.Id}"));
            await using var reload = new BackOfficeDbContext(options);
            var saved = await reload.Set<WorkflowTaskBinding>().SingleAsync();
            Assert.Equal(rule.Id, saved.RuleVersionId); Assert.Equal(rule.Values, saved.RuleSnapshotJson);
            Assert.Equal(source.Id, saved.JobExceptionId); Assert.Equal(first.Id, saved.TaskId);
            Assert.Equal("open", (await reload.Set<OperationalTask>().SingleAsync(x => x.Id == first.Id)).State);
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
