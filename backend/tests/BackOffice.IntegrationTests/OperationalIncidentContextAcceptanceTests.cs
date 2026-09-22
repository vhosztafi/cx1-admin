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
    public Task RealSqlOperationalAcceptanceIncidentContextUsesExactOccurrenceSource() => WithDatabase(async (db, password) =>
    {
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync();
        var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(); f.Clock.Current = term.EndsAt.AddDays(2);
        var resolver = new IncidentOccurrenceResolver(f.Factory, f.Clock);
        var incidents = new IncidentService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), resolver, f.Clock);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1);
        var draft = JsonSerializer.SerializeToElement(new { policyId = policy.Id, productCode = "motor-trade-road-risks",
            occurrence = new { occurredOn = day.ToString("yyyy-MM-dd"), timeZone = "Europe/London", precision = "date" } });
        var created = await incidents.Create(f.Underwriter, draft, Guid.NewGuid().ToString(), default);
        await incidents.Resolve(f.Underwriter, created.ResourceId, created.Etag!, false, Guid.NewGuid().ToString(), default);
        var options = await incidents.SubjectOptions(f.Underwriter, created.ResourceId, version.Id, default);
        using var json = JsonDocument.Parse(options.Body);
        using var source = JsonDocument.Parse(version.SnapshotJson);
        Assert.True(json.RootElement.TryGetProperty("policyContext", out var context), "Incident choices must expose their exact historical policy context.");
        Assert.Equal(policy.Reference, context.GetProperty("reference").GetString());
        var insured = source.RootElement.GetProperty("insured");
        var expectedName = insured.TryGetProperty("legalName", out var company) ? company.GetString() : insured.GetProperty("firstName").GetString() + " " + insured.GetProperty("surname").GetString();
        Assert.Equal(expectedName, context.GetProperty("insuredName").GetString());
        Assert.Equal($"/policies/{policy.Id}?termId={term.Id}&versionId={version.Id}&tab=Transactions", context.GetProperty("href").GetString());
        Assert.Equal(source.RootElement.GetProperty("cover").GetProperty("sections").GetArrayLength(), context.GetProperty("sections").GetArrayLength());
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationalAccessException>(() => incidents.SubjectOptions(f.Underwriter, created.ResourceId, Guid.NewGuid(), default))).Status);
        await db.Set<ClientAgencyRelationship>().Where(x => x.Id == policy.RelationshipId).ExecuteUpdateAsync(x => x.SetProperty(y => y.State, "inactive"));
        Assert.Equal(404, (await Assert.ThrowsAsync<OperationalAccessException>(() => incidents.SubjectOptions(f.Underwriter, created.ResourceId, version.Id, default))).Status);
    });
}
