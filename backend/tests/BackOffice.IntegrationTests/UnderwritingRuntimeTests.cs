using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly ResolvedQuoteTerm Term = new("annual", new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new(2027, 10, 1, 0, 0, 0, TimeSpan.Zero), "Europe/London");

    [Fact]
    public async Task RealSqlRuntimeSeedAddsVersionsWithoutRewritingHistoryOrRevivingRevokedSettings()
    {
        await WithDatabase(async (db, password) =>
        {
            var original = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == "quote-capture" || x.Scope == "agency-distribution").ToArrayAsync();
            var credentials = await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToArrayAsync();
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
            var runtime = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "underwriting-runtime");
            Assert.Equal(2, UnderwritingRuntimeConfiguration.Parse(runtime.Values)!.Products.Count);
            var capture = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").OrderByDescending(x => x.Version).FirstAsync();
            Assert.Equal(4, QuoteCaptureConfiguration.Parse(capture.Values)!.Count);
            var distribution = await db.Set<SettingVersion>().Where(x => x.Scope == "agency-distribution").OrderByDescending(x => x.Version).FirstAsync();
            Assert.Equal(5, AgencyDistributionRules.Parse(distribution.Values)!.Count);
            foreach (var kept in original) Assert.Equal(kept.Values, (await db.Set<SettingVersion>().SingleAsync(x => x.Id == kept.Id)).Values);
            var disabled = JsonNode.Parse(runtime.Values)!; disabled["products"] = new JsonArray();
            db.Add(new SettingVersion { Scope = runtime.Scope, Version = 2, EffectiveFrom = Now, Values = disabled.ToJsonString() });
            db.Add(new SettingVersion { Scope = capture.Scope, Version = 3, EffectiveFrom = Now, Values = "{\"demo\":true,\"kind\":\"quote-capture\",\"products\":[]}" });
            await db.SaveChangesAsync();
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
            Assert.Equal(2, await db.Set<SettingVersion>().CountAsync(x => x.Scope == runtime.Scope));
            Assert.Equal(3, await db.Set<SettingVersion>().CountAsync(x => x.Scope == capture.Scope));
            Assert.Empty(UnderwritingRuntimeConfiguration.Parse((await db.Set<SettingVersion>().SingleAsync(x => x.Scope == runtime.Scope && x.Version == 2)).Values)!.Products);
            Assert.Equal(credentials, await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToArrayAsync());
            Assert.Empty(await db.Set<AgencyTermsVersion>().ToArrayAsync());
        });
    }

    [Fact]
    public async Task RealSqlRatingEligibilityChecksPublishedOwnershipWholeTermCurrentSettingsAndStoredRoles()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
            var f = await Fixture(db);
            async Task<EligibleQuoteRating> Resolve(ResolvedQuoteTerm? term = null)
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync();
                var owned = await QuoteUnderwritingScope.HoldAsync(db, f.Actor, f.Quote, "quote-rate");
                var result = await QuoteRatingEligibility.ResolveAsync(db, owned, f.ProductVersion, f.Terms, term ?? Term, Now);
                Assert.Empty(await QuoteUnderwritingScope.GrantsAsync(db, owned, f.ProductVersion, result.BinderVersion, result.Capture.Product.Code, term ?? Term, Now));
                await tx.CommitAsync(); return result;
            }
            var rated = await Resolve(); Assert.Equal(1250, rated.CommissionBasisPoints); Assert.Null(rated.MinimumPremium);
            Assert.Equal("success", rated.Scenario); Assert.Equal("demo-senior-1", rated.AuthorityVersion.Version);
            Assert.Equal("underwriting-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve(Term with { StartsAt = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero) }))).Code);
            Assert.Equal("underwriting-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve(Term with { EndsAt = new(2030, 1, 2, 0, 0, 0, TimeSpan.Zero) }))).Code);
            var runtime = JsonNode.Parse(rated.RuntimeVersion.Values)!;
            var other = runtime["products"]!.AsArray().Single(x => x!["productVersionId"]!.GetValue<Guid>() != f.ProductVersion)!;
            var current = runtime["products"]!.AsArray().Single(x => x!["productVersionId"]!.GetValue<Guid>() == f.ProductVersion)!;
            current["binderVersionId"] = other["binderVersionId"]!.DeepClone();
            db.Add(new SettingVersion { Scope = "underwriting-runtime", Version = 2, EffectiveFrom = Now, Values = runtime.ToJsonString() }); await db.SaveChangesAsync();
            Assert.Equal("underwriting-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve())).Code);
            db.Add(new SettingVersion { Scope = "underwriting-runtime", Version = 3, EffectiveFrom = Now, Values = "{}" }); await db.SaveChangesAsync();
            Assert.Equal("underwriting-configuration-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve())).Code);
            db.Add(new SettingVersion { Scope = "underwriting-runtime", Version = 4, EffectiveFrom = Now, Values = rated.RuntimeVersion.Values }); await db.SaveChangesAsync();
            await Resolve();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RatingRuleVersion SET State=N'retired' WHERE Id={rated.RatingVersion.Id}");
            Assert.Equal("underwriting-product-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve())).Code);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Resolve())).Status);
        });
    }

    private sealed record RatingFixture(Guid Quote, Guid ProductVersion, Guid Terms, ActorContext Actor);
    private static async Task<RatingFixture> Fixture(BackOfficeDbContext db, int productVersion = 2, string productCode = "motor-trade-road-risks")
    {
        var admin = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
        var requester = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example");
        var servicing = await db.Set<StaffUser>().SingleAsync(x => x.Email == "servicing@cover.example");
        var product = await db.Set<Product>().SingleAsync(x => x.Code == productCode);
        var version = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == product.Id && x.Version == productVersion);
        var agency = new Agency { Reference = "AG-UW-RUNTIME", LegalName = "Fictional underwriting agency" };
        var client = new ClientAccount { Reference = "CL-UW-RUNTIME", LegalName = "Fictional underwriting client", NormalizedName = "FICTIONAL UNDERWRITING CLIENT", EntityType = "sole-trader",
            Address = "{\"line1\":\"1 Fictional Lane\",\"town\":\"Sheffield\",\"postcode\":\"S1 1AA\",\"country\":\"GB\"}" };
        db.AddRange(agency, client); await db.SaveChangesAsync();
        var relationship = new ClientAgencyRelationship { AgencyId = agency.Id, ClientId = client.Id };
        var request = new AgencyStateRequest { AgencyId = agency.Id, BaseVersion = agency.RowVersion, ProposedInputFingerprint = new string('a', 64),
            RequestedBy = requester.Id, CreatedBy = requester.Id, CreatedAt = Now, RequestReason = "Fictional activation" };
        db.AddRange(relationship, request); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={admin.Id},DecisionReason=N'Fictional approval',DecidedAt={Now} WHERE Id={request.Id}");
        var snapshot = JsonSerializer.Serialize(new { effectiveFrom = "2026-09-15", commercialTerms = new { effectiveFrom = "2026-09-15", commissionBasis = "per-product",
            feeSharing = "none", volumeCommitmentMode = "none", minimumPremiumOverrideMode = "none", referralRouting = "standard-internal-underwriting" },
            settlement = new { statementCycle = "monthly", method = "bank-transfer", premiumCollection = "agency", commissionSettlement = "net-remittance" },
            paymentTermsDays = 30, creditLimit = "0.00", products = new[] { new { productVersionId = version.Id, effectiveFrom = "2026-09-15", brokerCommissionBasisPoints = 1250 } } });
        var terms = new AgencyTermsVersion { AgencyId = agency.Id, Version = 1, EffectiveFrom = new(2026, 9, 15), ApprovedStateRequestId = request.Id, CreatedBy = admin.Id, Snapshot = snapshot };
        db.Add(terms); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agency.Id}");
        var quote = new Quote { AgencyId = agency.Id, ClientId = client.Id, RelationshipId = relationship.Id, ProductId = product.Id, CreatedBy = servicing.Id };
        db.Add(quote); await db.SaveChangesAsync();
        var proposal = JsonSerializer.Serialize(new { schemaVersion = "1.0", productCode });
        var revision = new QuoteRevision { QuoteId = quote.Id, AgencyId = agency.Id, ClientId = client.Id, RelationshipId = relationship.Id,
            ProductId = product.Id, ProductVersionId = version.Id, AgencyTermsVersionId = terms.Id, Number = 1,
            QuestionSetVersion = QuoteCatalogueIdentity.Version, ProposalJson = proposal, ContentHash = SHA256.HashData(Encoding.UTF8.GetBytes(proposal)), CreatedBy = servicing.Id, CreatedAt = Now, SavedBy = servicing.Id, SavedAt = Now };
        db.Add(revision); await db.SaveChangesAsync(); quote.CurrentRevisionId = revision.Id; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return new(quote.Id, version.Id, terms.Id, new(servicing.Id, servicing.TeamId, null, new HashSet<string> { "servicing" }));
    }

    private static async Task WithDatabase(Func<BackOfficeDbContext, string, Task> test)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync();
            var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true); await test(db, password);
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
