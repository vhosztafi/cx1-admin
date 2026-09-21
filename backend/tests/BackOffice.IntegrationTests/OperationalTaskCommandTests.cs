using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalTaskCommandTests
{
    [Fact]
    public async Task RealSqlTaskCommandsReplayRollbackAndRevokedIdentity()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync();
            var user = new StaffUser { Email = "task-command@cover.example", NormalizedEmail = "TASK-COMMAND@COVER.EXAMPLE", DisplayName = "Task command actor" };
            var role = new Role { Code = "agency-admin", Scope = "internal" };
            var team = new Team { Name = "Fictional task reviewers" }; user.TeamId = team.Id;
            var agency = new Agency { Reference = "AG-OPS-COMMAND", LegalName = "Fictional Task Agency" };
            db.AddRange(user, role, agency, team); await db.SaveChangesAsync(); db.Add(new UserRole { UserId = user.Id, RoleId = role.Id }); await db.SaveChangesAsync();
            var actor = new ActorContext(user.Id, null, null, new HashSet<string> { "agency-admin" });
            var factory = new Factory(options); var service = new TaskService(factory, new SqlCommandBoundary(factory, TimeProvider.System), TimeProvider.System);
            var subject = await service.Register(actor, new("agency", agency.Id), "subject", default);
            Assert.Equal(subject.ResourceId, (await service.Register(actor, new("agency", agency.Id), "subject-other-key", default)).ResourceId);
            var input = new TaskWrite("complaint", "Review fictional complaint", "normal", new("unassigned"), null);
            var created = await service.Create(actor, subject.ResourceId, input, "create", default);
            Assert.True((await service.Create(actor, subject.ResourceId, input, "create", default)).Replayed);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => service.Create(actor, subject.ResourceId, input with { Title = "Changed" }, "create", default));
            var completed = await service.Transition(actor, created.ResourceId, created.Etag!, "complete", "completed", "Review complete", default);
            Assert.Equal(412, (await Assert.ThrowsAsync<OperationalAccessException>(() => service.Transition(actor, created.ResourceId, created.Etag!, "stale", "open", "New evidence", default))).Status);
            var reopened = await service.Transition(actor, created.ResourceId, completed.Etag!, "reopen", "open", "New evidence", default);
            var second = await service.Create(actor, subject.ResourceId, input with { Title = "Second task" }, "second", default);
            await Assert.ThrowsAsync<OperationalAccessException>(() => service.BulkComplete(actor, [new(created.ResourceId, reopened.Etag!), new(second.ResourceId, "\"stale\"")], "batch-bad", "Reviewed together", default));
            Assert.All(await db.Set<OperationalTask>().AsNoTracking().ToArrayAsync(), row => Assert.Equal("open", row.State));
            Assert.Equal(4, await db.Set<OperationalTaskEvent>().CountAsync());
            var batch = await service.BulkComplete(actor, [new(created.ResourceId, reopened.Etag!), new(second.ResourceId, second.Etag!)], "batch-good", "Reviewed together", default);
            Assert.True((await service.BulkComplete(actor, [new(created.ResourceId, reopened.Etag!), new(second.ResourceId, second.Etag!)], "batch-good", "Reviewed together", default)).Replayed);
            Assert.Equal(6, await db.Set<OperationalTaskEvent>().CountAsync());
            var fresh = await service.Read(actor, second.ResourceId, default);
            var openSecond = await service.Transition(actor, second.ResourceId, fresh.Etag!, "reopen-second", "open", "Additional review", default);
            var edited = await service.Update(actor, second.ResourceId, openSecond.Etag!, "edit-second", input with { Title = "Assigned review", Assignment = new("user", user.Id) }, default);
            await Assert.ThrowsAsync<OperationalAccessException>(() => service.Update(actor, second.ResourceId, edited.Etag!, "bad-owner", input with { Assignment = new("user", Guid.NewGuid()) }, default));
            var item = new OperationalTaskChecklist { TaskId = second.ResourceId, Label = "Review evidence", Required = true, CreatedBy = user.Id };
            db.Add(item); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<TaskRuleException>(() => service.Transition(actor, second.ResourceId, edited.Etag!, "incomplete", "completed", "Not ready", default));
            var checkedTask = await service.Checklist(actor, second.ResourceId, edited.Etag!, "check-evidence", [new(item.Id, true)], "Evidence checked", default);
            var comment = await service.Comment(actor, second.ResourceId, checkedTask.Etag!, "comment", "Fictional review observation", default);
            Assert.True((await service.Comment(actor, second.ResourceId, checkedTask.Etag!, "comment", "Fictional review observation", default)).Replayed);
            Assert.Single(await db.Set<OperationalTaskComment>().AsNoTracking().ToArrayAsync());
            var eventSnapshot = await db.Set<OperationalTaskEvent>().AsNoTracking().Where(x => x.TaskId == second.ResourceId && x.Kind == "task.checklist-changed").Select(x => x.SnapshotJson).SingleAsync();
            using (var snapshot = System.Text.Json.JsonDocument.Parse(eventSnapshot)) Assert.True(snapshot.RootElement.GetProperty("checklist")[0].GetProperty("completed").GetBoolean());
            await service.BulkDue(actor, [new(second.ResourceId, comment.Etag!)], "due", new DateOnly(2026, 10, 1), "Agreed date", default);
            var dueRead = await service.Read(actor, second.ResourceId, default);
            await service.BulkAssign(actor, [new(second.ResourceId, dueRead.Etag!)], "team", new("team", TeamId: team.Id), "Team review", default);
            Assert.Equal(team.Id, await db.Set<OperationalTask>().Where(x => x.Id == second.ResourceId).Select(x => x.TeamId).SingleAsync());
            var teamRead = await service.Read(actor, second.ResourceId, default);
            await service.BulkAssign(actor, [new(second.ResourceId, teamRead.Etag!)], "unassign", new("unassigned"), "Queue review", default);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={user.Id}");
            Assert.Equal(403, (await Assert.ThrowsAsync<OperationalAccessException>(() => service.Create(actor, subject.ResourceId, input, "create", default))).Status);
            Assert.Equal(2, await db.Set<OperationalTask>().CountAsync());
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(options);
    }
}
