using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialPolicyReadUsesExactEffectiveKnowledgeSourceAndWholeBook() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var f = await CommercialIssueCommand(db, cycle, acceptance, actorId);
        var issued = await f.Service.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var model = new CommercialExposureReadModel(f.Factory, new RatingClock());
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var before = Json(await model.ReadAsync(f.Actor, "policies", issued.ResourceId, cycle.StartsAt, now.AddTicks(-1)));
        Assert.Equal("not-covered", before.GetProperty("coverageState").GetString()); Assert.Empty(before.GetProperty("districts").EnumerateArray());
        var scheduled = Json(await model.ReadAsync(f.Actor, "policies", issued.ResourceId, now, now));
        Assert.Equal("scheduled", scheduled.GetProperty("coverageState").GetString());
        Assert.All(scheduled.GetProperty("districts").EnumerateArray(), x => Assert.Equal("0.00", x.GetProperty("ownProposedSumInsured").GetString()));
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var own = Json(await model.ReadAsync(f.Actor, "policies", issued.ResourceId, cycle.StartsAt, now));
        Assert.Equal(version.Id, own.GetProperty("source").GetProperty("id").GetGuid());
        Assert.Equal(Convert.ToHexStringLower(version.ContentHash), own.GetProperty("source").GetProperty("hash").GetString());
        Assert.All(own.GetProperty("districts").EnumerateArray(), x => Assert.Equal(x.GetProperty("ownProposedSumInsured").GetString(), x.GetProperty("bookSumInsured").GetString()));
        await CommercialTermsScenario(async (_, secondCycle, secondAcceptance, secondActor, _) =>
        {
            var second = await CommercialIssueCommand(db, secondCycle, secondAcceptance, secondActor);
            await second.Service.IssueAsync(second.Actor, second.Quote.Id, second.Quote.RowVersion, second.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        }, stopAfterAccepted: true, existingDb: db);
        var whole = Json(await model.ReadAsync(f.Actor, "policies", issued.ResourceId, cycle.StartsAt, now));
        var rows = whole.GetProperty("districts").EnumerateArray().ToArray(); Assert.All(rows, x => Assert.Equal(2, x.GetProperty("policyCount").GetInt32()));
        Assert.All(rows, x => Assert.Equal(2 * decimal.Parse(x.GetProperty("ownProposedSumInsured").GetString()!, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(x.GetProperty("bookSumInsured").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        var boundQuote = Json(await model.ReadAsync(f.Actor, "quotes", f.Quote.Id, cycle.StartsAt, now));
        Assert.True(JsonElement.DeepEquals(whole.GetProperty("districts"), boundQuote.GetProperty("districts")));
        var expired = Json(await model.ReadAsync(f.Actor, "policies", issued.ResourceId, cycle.EndsAt, now));
        Assert.Equal("expired", expired.GetProperty("coverageState").GetString());
        Assert.All(expired.GetProperty("districts").EnumerateArray(), x => Assert.Equal("0.00", x.GetProperty("ownProposedSumInsured").GetString()));
    }, stopAfterAccepted: true);

    [Fact]
    public Task RealSqlCommercialPolicyReadAgencyProjectionDeniesForeignSubjectsAndCurrentRevocation() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var f = await CommercialIssueCommand(db, cycle, acceptance, actorId);
        var issued = await f.Service.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var user = new StaffUser { AgencyId = f.Quote.AgencyId, State = "invited", Email = "commercial-reader@example.invalid", NormalizedEmail = "COMMERCIAL-READER@EXAMPLE.INVALID", DisplayName = "Fictional commercial reader" };
        db.Add(user); await db.SaveChangesAsync(); db.Add(new UserRole { UserId = user.Id, RoleId = await db.Set<Role>().Where(x => x.Code == "broker-readonly").Select(x => x.Id).SingleAsync() });
        await db.SaveChangesAsync(); user.State = "active"; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var actor = new ActorContext(user.Id, null, f.Quote.AgencyId, new HashSet<string> { "broker-readonly" });
        var model = new CommercialExposureReadModel(f.Factory, new RatingClock());
        var safe = JsonSerializer.SerializeToElement(await model.ReadAsync(actor, "policies", issued.ResourceId, cycle.StartsAt, now));
        Assert.Equal("agency", safe.GetProperty("audience").GetString());
        foreach (var row in safe.GetProperty("districts").EnumerateArray())
        foreach (var field in new[] { "bookSumInsured", "policyCount", "limit", "headroom", "bookId", "limitVersionId", "limitHash", "policyIds" }) Assert.False(row.TryGetProperty(field, out _), field);
        await CommercialTermsScenario(async (_, otherCycle, otherAcceptance, otherActor, _) =>
        {
            var other = await CommercialIssueCommand(db, otherCycle, otherAcceptance, otherActor);
            var otherPolicy = await other.Service.IssueAsync(other.Actor, other.Quote.Id, other.Quote.RowVersion, other.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            foreach (var subject in new[] { ("quotes", other.Quote.Id), ("policies", otherPolicy.ResourceId), ("drafts", Guid.NewGuid()) })
                Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => model.ReadAsync(actor, subject.Item1, subject.Item2, cycle.StartsAt, now))).Status);
        }, stopAfterAccepted: true, existingDb: db);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={user.Id}");
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => model.ReadAsync(actor, "policies", issued.ResourceId, cycle.StartsAt, now))).Status);
    }, stopAfterAccepted: true);
}
