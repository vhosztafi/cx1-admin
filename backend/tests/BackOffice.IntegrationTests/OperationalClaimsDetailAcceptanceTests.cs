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
    public Task RealSqlOperationalAcceptanceClaimsDetailsRetainProviderValuesAndAdministratorIdentity() => WithDatabase(async (db, password) =>
    {
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(); f.Clock.Current = term.EndsAt.AddDays(2);
        await using (var tx = await db.Database.BeginTransactionAsync()) { await OperationalClaimsSeed.Seed(db); await tx.CommitAsync(); }
        db.Add(new SettingVersion { Scope = ClaimsHandoffService.WorkKind, Version = 2, EffectiveFrom = f.Clock.GetUtcNow(),
            Values = "{\"demo\":true,\"kind\":\"operational-claims\",\"schemaVersion\":\"2\",\"scenario\":\"summary-details\"}" });
        await db.SaveChangesAsync();
        var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
        var resolver = new IncidentOccurrenceResolver(f.Factory, f.Clock);
        var incidents = new IncidentService(f.Factory, boundary, resolver, f.Clock);
        var handoffs = new ClaimsHandoffService(boundary, resolver, f.Clock);
        var summaries = new ClaimsSummaryService(f.Factory, boundary, handoffs, resolver, f.Clock);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1);
        var draft = JsonSerializer.SerializeToElement(new { policyId = version.PolicyId, productCode = "motor-trade-road-risks",
            occurrence = new { occurredOn = day.ToString("yyyy-MM-dd"), timeZone = "Europe/London", precision = "date" },
            kind = "other", thirdPartyInvolvement = "unknown", reportedBy = "Fictional reporter", reportingRoute = "agency",
            bestContactDescription = "01632 960001", description = "Fictional property damage for administrator detail acceptance.",
            motorSubject = new { kind = "third-party-only", itemDescription = "Fictional boundary wall" } });
        var created = await incidents.Create(f.Underwriter, draft, Guid.NewGuid().ToString(), default);
        var resolved = await incidents.Resolve(f.Underwriter, created.ResourceId, created.Etag!, true, Guid.NewGuid().ToString(), default);
        var incident = await db.Set<OperationalIncident>().AsNoTracking().SingleAsync();
        var queued = await handoffs.Handoff(f.Underwriter, incident.Id, resolved.Etag!, incident.CurrentRevisionId!.Value,
            incident.CurrentResolutionId!.Value, OperationalClaimsSeed.AdministratorId, false, Guid.NewGuid().ToString(), default);
        var files = new FileService(f.Factory, boundary, new OperationalFileStore(Path.GetFullPath(Path.Combine(".local", "claims-detail-files", db.Database.GetDbConnection().Database)), []), f.Clock);
        var worker = new ClaimsHandoffWorker(f.Factory, resolver, files, f.Clock);
        var leases = new SqlJobLeases(f.Factory, f.Clock);
        var lease = await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind, queued.ResourceId); Assert.NotNull(lease);
        var outcome = await worker.ExecuteProvider(lease); Assert.NotNull(outcome);
        Assert.Equal(InboxApplication.Applied, await worker.Apply(lease, outcome));
        Assert.Equal(InboxApplication.Duplicate, await worker.Apply(lease, outcome));
        var saved = await db.Set<ClaimsSummary>().AsNoTracking().SingleAsync();
        var details = new Dictionary<string, string> { ["liability"] = "Not yet determined by administrator", ["incurred"] = "6500.00",
            ["recoveryExpected"] = "Recovery enquiries pending", ["excessApplied"] = "750.00", ["movementNote"] = "Security footage requested" };
        void AssertDetails(JsonElement value) { foreach (var pair in details) Assert.Equal(pair.Value, value.GetProperty(pair.Key).GetString()); }
        using (var stored = JsonDocument.Parse(saved.SummaryJson)) AssertDetails(stored.RootElement);
        var page = await summaries.Summaries(f.Underwriter, incident.Id, null, 10, f.Clock.GetUtcNow(), default);
        AssertDetails(JsonSerializer.SerializeToElement(Assert.Single(page.Items), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        // Historical identity belongs to the submitted handoff, not today's renamed directory entry.
        await db.Set<ClaimsAdministrator>().Where(x => x.Id == OperationalClaimsSeed.AdministratorId).ExecuteUpdateAsync(x => x.SetProperty(y => y.Name, "Renamed fictional administrator"));
        var read = await incidents.Read(f.Underwriter, incident.Id, default);
        using (var current = JsonDocument.Parse(read.Body))
        {
            AssertDetails(current.RootElement.GetProperty("administratorSummary"));
            Assert.Equal("Cover Demo Claims Administrator", current.RootElement.GetProperty("administratorName").GetString());
        }
        var after = await db.Set<ClaimsSummary>().AsNoTracking().SingleAsync();
        Assert.Equal(saved.SummaryJson, after.SummaryJson); Assert.Equal(saved.ContentHash, after.ContentHash);
        var relationshipId = await db.Set<Policy>().Where(x => x.Id == version.PolicyId).Select(x => x.RelationshipId).SingleAsync();
        await db.Set<ClientAgencyRelationship>().Where(x => x.Id == relationshipId).ExecuteUpdateAsync(x => x.SetProperty(y => y.State, "inactive"));
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationalAccessException>(() => summaries.Summaries(f.Underwriter, incident.Id, null, 10, f.Clock.GetUtcNow(), default))).Status);
    });
}
