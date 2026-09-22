using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalAcceptanceTaskContextKeepsOwnedAgencyAndInsuredLinks() => WithDatabase(async (db, password) =>
    {
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync();
        var drafts = new ServicingDraftService(f.Factory, f.Clock);
        var listed = await drafts.ListAsync(f.Servicing, version.TermId);
        var draft = await drafts.CreateAsync(f.Servicing, version.TermId, Convert.FromBase64String(listed.Etag.Trim('"')),
            new("adjustment", version.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional task context"), Guid.NewGuid().ToString(), Guid.NewGuid());
        var tasks = new TaskService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
        var saved = new List<Guid>();
        foreach (var parent in new[] { new OperationalParent("quote", f.QuoteId), new("policy", policy.Id), new("servicing-draft", draft.ResourceId) })
        {
            var subject = await tasks.Register(f.Underwriter, parent, Guid.NewGuid().ToString(), default);
            var task = await tasks.Create(f.Underwriter, subject.ResourceId, new("servicing", "Fictional " + parent.Kind + " context", "normal", new("unassigned"), null), Guid.NewGuid().ToString(), default);
            saved.Add(task.ResourceId);
            var read = await tasks.Read(f.Underwriter, task.ResourceId, default);
            using var json = JsonDocument.Parse(read.Body);
            Assert.Equal(parent.ParentId, json.RootElement.GetProperty("subject").GetProperty("parentId").GetGuid());
            Assert.True(json.RootElement.TryGetProperty("relatedRecords", out var related), "Tasks need saved related agency/insured context.");
            var links = related.EnumerateArray().ToArray();
            var agency = Assert.Single(links, x => x.GetProperty("kind").GetString() == "agency");
            Assert.Equal(policy.AgencyId, agency.GetProperty("id").GetGuid());
            Assert.Equal($"/agents/{policy.AgencyId}", agency.GetProperty("href").GetString());
            var insured = Assert.Single(links, x => x.GetProperty("kind").GetString() == "client");
            Assert.Equal(policy.ClientId, insured.GetProperty("id").GetGuid());
            Assert.Equal($"/clients/{policy.ClientId}", insured.GetProperty("href").GetString());
            Assert.All(links, x => Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("label").GetString())));
            if (parent.Kind == "servicing-draft")
            {
                var policyLink = Assert.Single(links, x => x.GetProperty("kind").GetString() == "policy");
                Assert.Equal(policy.Id, policyLink.GetProperty("id").GetGuid());
                Assert.Equal($"/policies/{policy.Id}", policyLink.GetProperty("href").GetString());
            }
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={policy.RelationshipId}");
        foreach (var id in saved)
        {
            var denied = await Assert.ThrowsAsync<OperationalAccessException>(() => tasks.Read(f.Underwriter, id, default));
            Assert.Equal(404, denied.Status);
        }
    });
}
