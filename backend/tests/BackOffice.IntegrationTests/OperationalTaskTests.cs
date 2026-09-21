using BackOffice.Infrastructure.Persistence;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalTaskTests
{
    [Fact]
    public async Task RealSqlTypedSubjectTaskHistoryConstraintsAndReload()
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
            var user = new StaffUser { Email = "operations-storage@cover.example", NormalizedEmail = "OPERATIONS-STORAGE@COVER.EXAMPLE", DisplayName = "Operations storage" };
            var role = new Role { Code = "agency-admin", Scope = "internal" };
            var agency = new Agency { Reference = "AG-OPS-STORAGE", LegalName = "Fictional Operations" };
            var other = new Agency { Reference = "AG-OPS-OTHER", LegalName = "Fictional Other" };
            db.AddRange(user, role, agency, other); await db.SaveChangesAsync();
            db.Add(new UserRole { UserId = user.Id, RoleId = role.Id }); await db.SaveChangesAsync();
            async Task Rejected(StoredRecord row)
            {
                db.Add(row); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            }
            await Rejected(new OperationalSubject { Kind = "agency", CreatedBy = user.Id });
            await Rejected(new OperationalSubject { Kind = "policy", AgencyId = agency.Id, CreatedBy = user.Id });
            await Rejected(new OperationalSubject { Kind = "agency", AgencyId = agency.Id, QuoteId = Guid.NewGuid(), CreatedBy = user.Id });
            await Rejected(new OperationalSubject { Kind = "agency", AgencyId = Guid.NewGuid(), CreatedBy = user.Id });
            var subject = new OperationalSubject { Kind = "agency", AgencyId = agency.Id, CreatedBy = user.Id };
            db.Add(subject); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var actor = new ActorContext(user.Id, null, null, new HashSet<string> { "agency-admin" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => OperationalScope.HoldSubjects(db, actor, [subject.Id], "task-read"));
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var held = await OperationalScope.HoldSubjects(db, actor, [subject.Id], "task-read");
                Assert.Equal(user.Id, held.Actor.UserId); Assert.Equal(subject.Id, Assert.Single(held.Subjects).Id);
                Assert.Equal(404, (await Assert.ThrowsAsync<OperationalAccessException>(() => OperationalScope.HoldSubjects(db, actor, [Guid.NewGuid()], "task-read"))).Status);
                Assert.Equal(403, (await Assert.ThrowsAsync<OperationalAccessException>(() => OperationalScope.HoldSubjects(db, actor with { AgencyId = other.Id }, [subject.Id], "task-read"))).Status);
                Assert.Equal(403, (await Assert.ThrowsAsync<OperationalAccessException>(() => OperationalScope.HoldSubjects(db, actor with { Roles = new HashSet<string> { "system-admin" } }, [subject.Id], "task-read"))).Status);
                await transaction.CommitAsync();
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={user.Id}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                Assert.Equal(403, (await Assert.ThrowsAsync<OperationalAccessException>(() => OperationalScope.HoldSubjects(db, actor, [subject.Id], "task-read"))).Status);
                await transaction.CommitAsync();
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={user.Id}");
            await Rejected(new OperationalSubject { Kind = "agency", AgencyId = agency.Id, CreatedBy = user.Id });
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalSubject SET AgencyId={other.Id} WHERE Id={subject.Id}"));
            var task = new OperationalTask { SubjectId = subject.Id, Reference = "TK-STORAGE", TypeCode = "complaint", Title = "Review fictional evidence", EventSequence = 1, CreatedBy = user.Id };
            db.Add(task); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var history = new OperationalTaskEvent { TaskId = task.Id, Sequence = 1, Kind = "created", ActorLabel = user.DisplayName, CreatedBy = user.Id };
            var comment = new OperationalTaskComment { TaskId = task.Id, Body = "Fictional recorded observation", AuthorLabel = user.DisplayName, CreatedBy = user.Id };
            var item = new OperationalTaskChecklist { TaskId = task.Id, Ordinal = 0, Label = "Evidence reviewed", Required = true, CreatedBy = user.Id };
            db.AddRange(history, comment, item); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await Rejected(new OperationalTaskEvent { TaskId = task.Id, Sequence = 1, Kind = "duplicate", ActorLabel = user.DisplayName, CreatedBy = user.Id });
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalTaskEvent SET Reason=N'Changed' WHERE Id={history.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OperationalTaskComment WHERE Id={comment.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalTaskChecklist SET Label=N'Changed' WHERE Id={item.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalTask SET State=N'completed' WHERE Id={task.Id}"));
            await using var reload = new BackOfficeDbContext(options);
            Assert.Equal(agency.Id, (await reload.Set<OperationalSubject>().SingleAsync()).AgencyId);
            Assert.Equal("open", (await reload.Set<OperationalTask>().SingleAsync()).State);
            Assert.Single(await reload.Set<OperationalTaskEvent>().ToListAsync());
            Assert.Equal(comment.Body, (await reload.Set<OperationalTaskComment>().SingleAsync()).Body);
            Assert.False((await reload.Set<OperationalTaskChecklist>().SingleAsync()).Completed);
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
