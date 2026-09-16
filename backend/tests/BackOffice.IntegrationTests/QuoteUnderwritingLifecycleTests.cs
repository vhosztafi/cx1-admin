using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlUnderwritingRefreshAdoptsExplicitPublishedVersionAndTermsWithoutChangingOldRevisions()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
            var f = await Fixture(db, productVersion: 1);
            var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
            var quotes = new QuoteService(factory, new RatingClock()); var lifecycle = new QuoteUnderwritingLifecycle(factory, new RatingClock());
            var owner = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.Quote);
            using var stream = typeof(UnderwritingSeed).Assembly.GetManifestResourceStream("UnderwritingDemo.Definitions")!;
            using var example = JsonDocument.Parse(stream);
            var fixtureProposal = JsonNode.Parse(example.RootElement.GetProperty("proposals").GetProperty("motor-trade-road-risks").GetRawText())!;
            fixtureProposal["termIntent"]!["localStartDate"] = "2026-10-01";
            var proposal = JsonSerializer.SerializeToElement(fixtureProposal);
            var created = await quotes.CreateAsync(f.Actor, owner.RelationshipId, f.ProductVersion, proposal.GetRawText(), Guid.NewGuid().ToString(), Guid.NewGuid());
            var before = await quotes.GetAsync(f.Actor, created.ResourceId);
            var target = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == owner.ProductId && x.Version == 2);
            // Distribution alone cannot manufacture the agency's independent agreement.
            Assert.Equal("quote-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => lifecycle.RefreshAsync(f.Actor, created.ResourceId,
                before.Revision.Id, target.Id, f.Terms, before.Quote.RowVersion, "Adopt published underwriting", Guid.NewGuid().ToString(), Guid.NewGuid()))).Code);
            var originalTerms = await db.Set<AgencyTermsVersion>().SingleAsync(x => x.Id == f.Terms);
            var snapshot = JsonNode.Parse(originalTerms.Snapshot)!;
            snapshot["effectiveFrom"] = "2026-09-16"; snapshot["commercialTerms"]!["effectiveFrom"] = "2026-09-16";
            snapshot["products"]![0]!["productVersionId"] = target.Id; snapshot["products"]![0]!["effectiveFrom"] = "2026-09-16";
            snapshot["products"]![0]!["brokerCommissionBasisPoints"] = 1500;
            var admin = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
            var requester = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example");
            var agency = await db.Set<Agency>().AsNoTracking().SingleAsync(x => x.Id == owner.AgencyId);
            var request = new AgencyTermsRequest { AgencyId = agency.Id, BaseVersion = agency.RowVersion, EffectiveFrom = new(2026, 9, 16),
                ProposedSnapshot = snapshot.ToJsonString(), ProposedInputFingerprint = new string('b', 64), RequestedBy = requester.Id, CreatedBy = requester.Id, CreatedAt = Now, RequestReason = "Adopt fictional underwriting product" };
            db.Add(request); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyTermsRequest SET State=N'applied',DecisionBy={admin.Id},DecisionReason=N'Independent fictional approval',DecidedAt={Now} WHERE Id={request.Id}");
            var terms = new AgencyTermsVersion { AgencyId = agency.Id, Version = 2, EffectiveFrom = request.EffectiveFrom, ApprovedTermsRequestId = request.Id, Snapshot = request.ProposedSnapshot, CreatedBy = admin.Id };
            db.Add(terms); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var refreshAssessment = await new QuoteUnderwritingReadModel(factory, new RatingClock()).AssessmentAsync(f.Actor, created.ResourceId);
            var options = JsonSerializer.SerializeToElement(refreshAssessment["refreshOptions"]);
            var option = Assert.Single(options.EnumerateArray());
            Assert.Equal(target.Id, option.GetProperty("productVersionId").GetGuid());
            Assert.Equal(terms.Id, option.GetProperty("agencyTermsVersionId").GetGuid());
            var key = Guid.NewGuid().ToString();
            await lifecycle.RefreshAsync(f.Actor, created.ResourceId, before.Revision.Id, target.Id, terms.Id, before.Quote.RowVersion,
                "Adopt the independently approved underwriting version", key, Guid.NewGuid());
            var after = await quotes.GetAsync(f.Actor, created.ResourceId); Assert.True(after.CanSave);
            Assert.Equal(target.Id, after.Revision.ProductVersionId); Assert.Equal(terms.Id, after.Revision.AgencyTermsVersionId);
            Assert.Equal(before.Revision.Number + 1, after.Revision.Number); Assert.NotEqual(before.Revision.Id, after.Revision.Id);
            using var fresh = JsonDocument.Parse(after.Revision.ProposalJson);
            Assert.True(JsonElement.DeepEquals(proposal.GetProperty("cover").GetProperty("requestedSections"), fresh.RootElement.GetProperty("cover").GetProperty("requestedSections")));
            var old = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == before.Revision.Id);
            Assert.Equal(before.Revision.ProposalJson, old.ProposalJson); Assert.Equal(before.Revision.ContentHash, old.ContentHash); Assert.Equal(f.Terms, old.AgencyTermsVersionId);
            Assert.True((await lifecycle.RefreshAsync(f.Actor, created.ResourceId, before.Revision.Id, target.Id, terms.Id, before.Quote.RowVersion,
                "Adopt the independently approved underwriting version", key, Guid.NewGuid())).Replayed);
            Assert.Empty(await db.Set<UnderwritingCycle>().ToArrayAsync());
        });
    }
}
