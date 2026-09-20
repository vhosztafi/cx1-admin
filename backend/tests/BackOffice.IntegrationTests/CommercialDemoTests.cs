using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("version")]
    [InlineData("snapshot")]
    [InlineData("projection")]
    [InlineData("unknown")]
    [InlineData("template")]
    [InlineData("request")]
    public Task RealSqlCommercialDemoDocumentGuardRejectsSubstitutionAndRollsBack(string change) => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var f = await CommercialIssueCommand(db, cycle, acceptance, actorId); var key = Guid.NewGuid().ToString();
        var fault = new CommercialDocumentFault(change);
        var poisoned = new QuoteIssueService(new CommercialDocumentFaultFactory(db.Database.GetConnectionString()!, fault), new RatingClock());
        var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => poisoned.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, key, Guid.NewGuid()));
        Assert.True(fault.Applied); Assert.Equal(51970, Assert.IsType<SqlException>(rejected.InnerException).Number);
        Assert.False(await db.Set<Policy>().AnyAsync()); Assert.False(await db.Set<Journal>().AnyAsync()); Assert.False(await db.Set<PolicyDocumentRequest>().AnyAsync());
        Assert.False(await db.Set<CommercialExposureVersion>().AnyAsync());
        Assert.Equal(201, (await f.Service.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, key, Guid.NewGuid())).Status);
    }, stopAfterAccepted: true);

    private sealed class CommercialDocumentFaultFactory(string connection, CommercialDocumentFault fault) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection, x => x.UseCompatibilityLevel(160)).AddInterceptors(fault).Options);
        public Task<BackOfficeDbContext> CreateDbContextAsync(CancellationToken token = default) => Task.FromResult(CreateDbContext());
    }
    private sealed class CommercialDocumentFault(string change) : SaveChangesInterceptor
    {
        private string? replacement; public bool Applied { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Applied) return ValueTask.FromResult(result);
            var work = data.Context!.ChangeTracker.Entries<OutboxWork>().FirstOrDefault(x => x.State == EntityState.Added && x.Entity.Kind == "policy-document")?.Entity;
            if (work is not null)
            {
                var payload = JsonNode.Parse(work.Payload)!;
                if (change == "version") payload["commercial"]!["versionId"] = Guid.NewGuid();
                if (change == "snapshot") payload["snapshot"]!["insured"]!["legalName"] = "Foreign snapshot";
                if (change == "projection") payload["commercial"]!["insured"]!["legalName"] = "Foreign projection";
                if (change == "unknown") payload["commercial"]!["vehicles"] = new JsonArray();
                if (change == "template") payload["template"] = new JsonObject { ["foreign"] = true };
                if (change == "request") payload["requestId"] = Guid.NewGuid();
                replacement = payload.ToJsonString(); work.Payload = replacement;
            }
            var request = data.Context.ChangeTracker.Entries<PolicyDocumentRequest>().FirstOrDefault(x => x.State == EntityState.Added)?.Entity;
            if (request is not null && replacement is not null)
            {
                request.PayloadJson = replacement; request.PayloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(replacement)); Applied = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public Task RealSqlCommercialDemoRepeatPreservesEditsAndChecksAccessBeforeDiscovery() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var f = await CommercialIssueCommand(db, cycle, acceptance, actorId);
        var seed = new CommercialDemoSeed(f.Factory, new RatingClock());
        var beforeVersions = await db.Set<SettingVersion>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.Values).ToArrayAsync();
        var beforeGrants = await db.Set<UserAuthorityGrant>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var first = await seed.SeedAsync(f.Actor, f.Quote.RelationshipId, cycle.ProductVersionId);
        Assert.Equal(5, first.Count);
        var quotes = new QuoteService(f.Factory, new RatingClock());
        var editable = await quotes.GetAsync(f.Actor, first[0].QuoteId);
        var changed = JsonNode.Parse(editable.Revision.ProposalJson)!; changed["risk"]!["materialFacts"] = "Business user's retained demonstration notes";
        await quotes.SaveAsync(f.Actor, editable.Quote.Id, editable.Quote.RowVersion, changed.ToJsonString(), "Owned edit", Guid.NewGuid().ToString(), Guid.NewGuid());
        var beforeRevisions = await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync();
        var beforeReceipts = await db.Set<IdempotencyRecord>().CountAsync();
        Assert.Equal(first, await seed.SeedAsync(f.Actor, f.Quote.RelationshipId, cycle.ProductVersionId));
        Assert.Equal(beforeRevisions, await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync());
        Assert.Equal(beforeVersions, await db.Set<SettingVersion>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.Values).ToArrayAsync());
        Assert.Equal(beforeGrants, await db.Set<UserAuthorityGrant>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        Assert.Equal(beforeReceipts, await db.Set<IdempotencyRecord>().CountAsync());
        foreach (var demo in first)
        {
            var saved = await quotes.GetAsync(f.Actor, demo.QuoteId);
            using var proposal = JsonDocument.Parse(saved.Revision.ProposalJson);
            Assert.Equal(2, proposal.RootElement.GetProperty("risk").GetProperty("locations").GetArrayLength());
            var referrals = BackOffice.Application.Underwriting.CommercialReferralRules.SourceReferrals(proposal.RootElement);
            if (demo.Scenario == "flood-referral") Assert.Contains(referrals, x => x.RuleCode == "PR-05");
            if (demo.Scenario == "outside-appetite") Assert.Contains(referrals, x => x.Disposition == "outside-appetite");
            if (demo.Scenario is "conditional-capacity" or "capacity-contender") Assert.Contains(referrals, x => x.Disposition == "carrier-required");
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={f.Quote.AgencyId}");
        var denied = await Assert.ThrowsAsync<QuoteOperationException>(() => seed.SeedAsync(f.Actor, f.Quote.RelationshipId, cycle.ProductVersionId));
        Assert.Contains(denied.Status, new[] { 403, 409 });
        Assert.Equal(beforeReceipts, await db.Set<IdempotencyRecord>().CountAsync());
        Assert.Equal(beforeRevisions, await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync());
    }, stopAfterAccepted: true);

    [Fact]
    public Task RealSqlCommercialDemoDocumentRequestsRetainExactSourceOnReplay() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var f = await CommercialIssueCommand(db, cycle, acceptance, actorId); var key = Guid.NewGuid().ToString();
        var issued = await f.Service.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, key, Guid.NewGuid());
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.PolicyId == issued.ResourceId);
        var source = new CommercialPayloadSource(version.PolicyId, version.Id, Convert.ToHexStringLower(version.ContentHash), version.SnapshotJson, version.EffectiveAt, cycle.EndsAt);
        var documents = await db.Set<PolicyDocumentRequest>().AsNoTracking().Where(x => x.VersionId == version.Id).OrderBy(x => x.Kind).ToArrayAsync();
        Assert.Equal(3, documents.Length);
        foreach (var request in documents)
        {
            using var payload = JsonDocument.Parse(request.PayloadJson);
            Assert.True(CommercialDocumentPayload.Valid(payload.RootElement.GetProperty("commercial"), source, request.Kind));
            using var original = JsonDocument.Parse(version.SnapshotJson);
            Assert.True(JsonElement.DeepEquals(original.RootElement, payload.RootElement.GetProperty("snapshot")));
            Assert.Equal(request.PayloadJson, (await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == request.WorkId)).Payload);
            Assert.Equal("requested", request.State);
        }
        Assert.True((await f.Service.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, key, Guid.NewGuid())).Replayed);
        Assert.Equal(documents.Select(x => x.PayloadJson), await db.Set<PolicyDocumentRequest>().AsNoTracking().Where(x => x.VersionId == version.Id).OrderBy(x => x.Kind).Select(x => x.PayloadJson).ToArrayAsync());
        Assert.Equal(1, await db.Set<Journal>().CountAsync());
    }, stopAfterAccepted: true);
}
