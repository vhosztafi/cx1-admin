using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlOperationalWorkflowMatchRequiresOwnedParentAndUnambiguousCurrentRequest()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == "underwriter@cover.example");
            var now = DateTimeOffset.UtcNow;
            var matching = new SettingVersion { Scope = "matching-rule", Version = 1, EffectiveFrom = now.AddDays(-1) };
            matching.Values = JsonSerializer.Serialize(new { id = matching.Id, version = 1 });
            var definition = new WorkflowTaskDefinition("workflow-task-1", "published", "match-information", "missing-information", "servicing", "Request matching information", "normal", "awaiting-information", 3, 0, []);
            var rule = new SettingVersion { Scope = "workflow-task/match-information", Version = 1, EffectiveFrom = now.AddDays(-1), Values = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            db.AddRange(matching, rule); await db.SaveChangesAsync();
            var intake = new MatchSubmission { Reference = "MI-WORKFLOW-OWNED", AgencyId = fixture.Agency };
            var review = new MatchReview { SubmissionId = intake.Id, CandidateClientId = fixture.Client, CandidateRelationshipId = fixture.Relationship,
                RuleVersionId = matching.Id, RuleSnapshot = matching.Values, State = "queried",
                Signals = "[{\"code\":\"legal-name\",\"description\":\"Fictional name comparison\",\"strength\":\"strong\",\"result\":\"near-match\"}]" };
            db.AddRange(intake, review); await db.SaveChangesAsync();
            var request = new MatchInformationRequest { MatchId = review.Id, ActorId = user.Id, Description = "Confirm fictional trading identity", RecordedAt = now, CreatedAt = now, CreatedBy = user.Id };
            db.Add(request); await db.SaveChangesAsync();
            db.Add(new MatchDecision { MatchId = review.Id, Outcome = "query", Reason = request.Description, ActorId = user.Id, InformationRequestId = request.Id, OccurredAt = now, CreatedAt = now, CreatedBy = user.Id }); await db.SaveChangesAsync();
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var factory = new QuoteFactory(options); var tasks = new TaskService(factory, new SqlCommandBoundary(factory, TimeProvider.System), TimeProvider.System);
            var workflows = new WorkflowTaskService(factory, tasks, TimeProvider.System);
            Assert.Equal("workflow-source-parent-unavailable", (await Assert.ThrowsAsync<OperationalAccessException>(() => workflows.Reconcile(rule.Id, "match-information-request", request.Id, default))).Code);
            Assert.Empty(await db.Set<OperationalTask>().ToArrayAsync());
            intake.LinkedClientId = fixture.Client; intake.LinkedRelationshipId = fixture.Relationship; await db.SaveChangesAsync();
            var taskId = Assert.IsType<Guid>(await workflows.Reconcile(rule.Id, "match-information-request", request.Id, default));
            Assert.Equal(fixture.Relationship, (await db.Set<OperationalSubject>().SingleAsync()).RelationshipId);
            Assert.Equal(taskId, await workflows.Reconcile(rule.Id, "match-information-request", request.Id, default));
            var tiedRequest = new MatchInformationRequest { MatchId = review.Id, ActorId = user.Id, Description = "Another fictional identity request", RecordedAt = now, CreatedAt = now, CreatedBy = user.Id };
            db.Add(tiedRequest); await db.SaveChangesAsync();
            db.Add(new MatchDecision { MatchId = review.Id, Outcome = "query", Reason = tiedRequest.Description, ActorId = user.Id, InformationRequestId = tiedRequest.Id, OccurredAt = now, CreatedAt = now, CreatedBy = user.Id }); await db.SaveChangesAsync();
            Assert.Equal("workflow-match-history-ambiguous", (await Assert.ThrowsAsync<OperationalAccessException>(() => workflows.Reconcile(rule.Id, "match-information-request", tiedRequest.Id, default))).Code);
            var actor = new ActorContext(user.Id, user.TeamId, null, new HashSet<string> { "underwriter" });
            using var view = JsonDocument.Parse((await tasks.Read(actor, taskId, default)).Body);
            Assert.Equal("unavailable", view.RootElement.GetProperty("workflow").GetProperty("sourceCondition").GetString());
            Assert.Equal("awaiting-information", view.RootElement.GetProperty("state").GetString());
            Assert.Single(await db.Set<WorkflowTaskBinding>().ToArrayAsync());
        });
    }
}
