using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalWorkflowTests
{
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
