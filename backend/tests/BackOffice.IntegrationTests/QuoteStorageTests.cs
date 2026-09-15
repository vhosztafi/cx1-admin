using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteStorageEnforcesOwnershipImmutableHistoryAndCaptureBounds()
    {
        await WithDatabase(async (db, password) =>
        {
            Assert.False(db.Database.HasPendingModelChanges());
            var fixture = await CreateFixture(db);
            var otherAgency = await CreateFixture(db, "-OTHER");
            var otherProductVersion = await db.Set<ProductVersion>().FirstAsync(x => x.ProductId != fixture.Product);
            var quote = await InsertQuote(db, fixture);
            var other = await InsertQuote(db, fixture);
            Assert.Matches("^QT-MT-[0-9]{10}$", quote.Reference);
            Assert.NotEqual(quote.Reference, other.Reference);
            var first = Revision(quote, fixture);
            db.Add(first); await db.SaveChangesAsync();
            quote.CurrentRevisionId = first.Id; await db.SaveChangesAsync();
            var second = Revision(quote, fixture, 2);
            db.Add(second); await db.SaveChangesAsync();
            quote.CurrentRevisionId = second.Id; await db.SaveChangesAsync();
            var activity = new QuoteActivity { QuoteId = quote.Id, RevisionId = second.Id, ActorId = fixture.Actor,
                CreatedBy = fixture.Actor, EventType = "quote.saved" };
            db.Add(activity);
            db.Add(new QuoteRegistration { QuoteId = quote.Id, VehicleId = Guid.NewGuid(), NormalizedRegistration = "DEMO01" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            async Task Denied(FormattableString sql) => await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
            await Denied($"UPDATE QuoteRevision SET Reason=N'Changed history' WHERE Id={first.Id}");
            await Denied($"DELETE FROM QuoteRevision WHERE Id={first.Id}");
            await Denied($"UPDATE QuoteActivity SET EventType=N'Changed' WHERE Id={activity.Id}");
            await Denied($"DELETE FROM QuoteActivity WHERE Id={activity.Id}");
            await Denied($"UPDATE Quote SET Number=9999999998 WHERE Id={quote.Id}");
            await Denied($"UPDATE Quote SET CurrentRevisionId={first.Id} WHERE Id={other.Id}");
            await Denied($"UPDATE Quote SET ClientId={Guid.NewGuid()} WHERE Id={quote.Id}");
            await Denied($"UPDATE Quote SET ClientId={otherAgency.Client} WHERE Id={quote.Id}");
            await Denied($"UPDATE Quote SET AgencyId={otherAgency.Agency} WHERE Id={quote.Id}");
            await Denied($"UPDATE QuoteRegistration SET NormalizedRegistration=N'DEMO 01' WHERE QuoteId={quote.Id}");
            await Denied($"UPDATE QuoteRegistration SET NormalizedRegistration=N'demo01' WHERE QuoteId={quote.Id}");

            async Task InvalidRevision(Action<QuoteRevision> change)
            {
                var invalid = Revision(quote, fixture, 3); change(invalid); db.Add(invalid);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
                Assert.Equal(2, await db.Set<QuoteRevision>().CountAsync());
            }
            await InvalidRevision(x => x.Number = 2);
            await InvalidRevision(x => x.AgencyId = Guid.NewGuid());
            await InvalidRevision(x => x.ProductVersionId = Guid.NewGuid());
            await InvalidRevision(x => x.AgencyTermsVersionId = Guid.NewGuid());
            await InvalidRevision(x => x.AgencyTermsVersionId = otherAgency.Terms);
            await InvalidRevision(x => x.ProductVersionId = otherProductVersion.Id);
            await InvalidRevision(x => x.ContentHash = new byte[32]);
            await InvalidRevision(x => x.CreatedBy = null);
            await InvalidRevision(x => x.ProposalJson = "[]");
            await InvalidRevision(x => x.ProposalJson = "{\"schemaVersion\":\"2.0\",\"productCode\":\"motor-trade-road-risks\"}");
            await InvalidRevision(x => x.ProposalJson = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-combined\"}");
            // UTF-8 bytes, not .NET character count, determine the storage bound.
            await InvalidRevision(x => x.TermIntentJson = "{\"text\":\"" + new string('\u20ac', 350000) + "\"}");
            await InvalidRevision(x => x.ReferenceVersionsJson = "[]");

            var wrongActivity = new QuoteActivity { QuoteId = other.Id, RevisionId = first.Id, ActorId = fixture.Actor,
                CreatedBy = fixture.Actor, EventType = "quote.saved" };
            db.Add(wrongActivity); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET State=N'withdrawn' WHERE Id={quote.Id}");
            await InvalidRevision(_ => { });
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET State=N'draft',CaptureClosedAt={DateTimeOffset.UtcNow},CaptureClosedReason=N'Closed fixture' WHERE Id={quote.Id}");
            await InvalidRevision(_ => { });
            Assert.Equal(new[] { other.Id }, await QuoteStorageIntegrity.MissingCurrentRevisionsAsync(db));
            await DemoDatabase.SeedAsync(db, password);
            Assert.Equal(2, await db.Set<Quote>().CountAsync());
            var retained = await db.Set<QuoteRevision>().SingleAsync(x => x.Id == first.Id);
            Assert.Equal(first.ProposalJson, retained.ProposalJson); Assert.Equal(first.ContentHash, retained.ContentHash);
            Assert.Equal(quote.Reference, (await db.Set<Quote>().SingleAsync(x => x.Id == quote.Id)).Reference);
        });
    }

    [Fact]
    public async Task RealSqlQuoteStorageUpgradeAndRollbackRetainExistingData()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            for (var failureStage = 1; failureStage <= 3; failureStage++)
            {
                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    var quote = await InsertQuote(db, fixture);
                    var revision = Revision(quote, fixture); db.Add(revision); await db.SaveChangesAsync();
                    if (failureStage >= 2)
                    {
                        db.Add(new QuoteRegistration { QuoteId = quote.Id, VehicleId = Guid.NewGuid(), NormalizedRegistration = "DEMO02" });
                        await db.SaveChangesAsync();
                    }
                    if (failureStage >= 3)
                    {
                        quote.CurrentRevisionId = revision.Id;
                        db.Add(new QuoteActivity { QuoteId = quote.Id, RevisionId = revision.Id, ActorId = fixture.Actor,
                            CreatedBy = fixture.Actor, EventType = "quote.created" });
                        await db.SaveChangesAsync();
                    }
                    await transaction.RollbackAsync();
                }
                db.ChangeTracker.Clear();
                Assert.Equal(0, await db.Set<Quote>().CountAsync()); Assert.Equal(0, await db.Set<QuoteRevision>().CountAsync());
                Assert.Equal(0, await db.Set<QuoteRegistration>().CountAsync()); Assert.Equal(0, await db.Set<QuoteActivity>().CountAsync());
                Assert.Empty(await QuoteStorageIntegrity.MissingCurrentRevisionsAsync(db));
            }
        }, upgrade: true);
    }

    private sealed record Fixture(Guid Agency, Guid Client, Guid Relationship, Guid Product, Guid ProductVersion, Guid Terms, Guid Actor);

    private static async Task<Fixture> CreateFixture(BackOfficeDbContext db, string suffix = "")
    {
        var requester = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example");
        var actor = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
        var product = await db.Set<Product>().SingleAsync(x => x.Code == "motor-trade-road-risks");
        var version = await db.Set<ProductVersion>().FirstAsync(x => x.ProductId == product.Id);
        var agency = new Agency { Reference = "AG-QUOTE-STORAGE" + suffix, LegalName = "Fictional quote storage" };
        var client = new ClientAccount { Reference = "CL-QUOTE-STORAGE" + suffix, LegalName = "Fictional quote client", NormalizedName = "FICTIONAL QUOTE CLIENT" };
        db.AddRange(agency, client); await db.SaveChangesAsync();
        var relationship = new ClientAgencyRelationship { AgencyId = agency.Id, ClientId = client.Id };
        var request = new AgencyStateRequest { AgencyId = agency.Id, BaseVersion = agency.RowVersion,
            ProposedInputFingerprint = new string('a', 64), RequestedBy = requester.Id, CreatedBy = requester.Id, RequestReason = "Fictional activation" };
        db.AddRange(relationship, request); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={actor.Id},DecisionReason=N'Fictional approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={request.Id}");
        var snapshot = JsonSerializer.Serialize(new { effectiveFrom = "2026-09-15", commercialTerms = new { commissionBasis = "per-product" }, creditLimit = "0.00",
            products = new[] { new { productVersionId = version.Id, effectiveFrom = "2026-09-15", brokerCommissionBasisPoints = 1250 } } });
        var terms = new AgencyTermsVersion { AgencyId = agency.Id, Version = 1, EffectiveFrom = new(2026, 9, 15),
            ApprovedStateRequestId = request.Id, CreatedBy = actor.Id, Snapshot = snapshot };
        db.Add(terms); await db.SaveChangesAsync();
        return new(agency.Id, client.Id, relationship.Id, product.Id, version.Id, terms.Id, actor.Id);
    }

    private static async Task<Quote> InsertQuote(BackOfficeDbContext db, Fixture fixture)
    {
        var quote = new Quote { AgencyId = fixture.Agency, ClientId = fixture.Client, RelationshipId = fixture.Relationship,
            ProductId = fixture.Product, CreatedBy = fixture.Actor };
        db.Add(quote); await db.SaveChangesAsync(); return quote;
    }

    private static QuoteRevision Revision(Quote quote, Fixture fixture, int number = 1)
    {
        const string proposal = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\"}";
        var now = DateTimeOffset.UtcNow;
        return new QuoteRevision { QuoteId = quote.Id, AgencyId = fixture.Agency, ProductId = fixture.Product,
            ProductVersionId = fixture.ProductVersion, AgencyTermsVersionId = fixture.Terms, Number = number,
            QuestionSetVersion = QuoteCatalogueIdentity.Version, ProposalJson = proposal,
            ContentHash = SHA256.HashData(Encoding.UTF8.GetBytes(proposal)), CreatedBy = fixture.Actor, SavedBy = fixture.Actor,
            CreatedAt = now, SavedAt = now };
    }

    private static async Task WithDatabase(Func<BackOfficeDbContext, string, Task> test, bool upgrade = false)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options);
            var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
            if (upgrade)
            {
                await db.GetService<IMigrator>().MigrateAsync("20260915005955_AgencyPermissionProvenance");
                await DemoDatabase.SeedAsync(db, password);
                var before = await db.Set<StaffUser>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
                await db.Database.MigrateAsync();
                Assert.Equal(before, await db.Set<StaffUser>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToListAsync());
            }
            else { await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password); }
            await db.Database.MigrateAsync();
            await test(db, password);
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
