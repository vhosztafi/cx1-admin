using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingStorageTests
{
    [Fact]
    public async Task RealSqlUnderwritingStorageEnforcesOwnershipHistoryAndExactMoney()
    {
        await WithDatabase(async db =>
        {
            Assert.False(db.Database.HasPendingModelChanges());
            await Seed(db);
            var fixture = await Fixture(db, published: true);
            var quote = await InsertQuote(db, fixture); var other = await InsertQuote(db, fixture);
            var first = await InsertCycle(db, quote, fixture); var second = await InsertCycle(db, other, fixture);
            var before = await db.Set<QuoteRevision>().AsNoTracking().Select(x => new { x.Id, x.ProposalJson, x.ContentHash }).ToArrayAsync();
            async Task Reject(FormattableString sql) { await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql)); db.ChangeTracker.Clear(); }
            await Reject($"UPDATE Quote SET CurrentUnderwritingCycleId={second.Id} WHERE Id={quote.Id}");
            await Reject($"UPDATE UnderwritingCycle SET InputJson=N'{{}}' WHERE Id={first.Id}");
            await Reject($"DELETE UnderwritingCycle WHERE Id={first.Id}");
            await Reject($"UPDATE RatingRuleVersion SET DefinitionJson=N'{{}}' WHERE Id={first.RatingRuleVersionId}");
            var rating = await InsertRating(db, first);
            await Reject($"UPDATE UnderwritingCycle SET CurrentRatingId={rating.Id} WHERE Id={second.Id}");
            await Reject($"UPDATE QuoteRatingResult SET AnnualPremium=1 WHERE Id={rating.Id}");
            await Reject($"DELETE QuoteRatingResult WHERE Id={rating.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UnderwritingCycle SET CurrentRatingId={rating.Id},State=N'rated' WHERE Id={first.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET CurrentUnderwritingCycleId={first.Id} WHERE Id={quote.Id}");
            var duplicate = await NewCycle(db, quote, fixture, first.Sequence);
            db.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var invalid = await NewCycle(db, quote, fixture, 2); invalid.EndsAt = invalid.StartsAt;
            db.Add(invalid); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            invalid = await NewCycle(db, quote, fixture, 2); invalid.InputJson = "[]";
            db.Add(invalid); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            invalid = await NewCycle(db, quote, fixture, 2); invalid.ClientId = Guid.NewGuid();
            db.Add(invalid); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            // Failed inserts leave no result; validation is not an update guard artifact.
            var negative = await NewRating(db, second); negative.TermPremium = -1;
            db.Add(negative); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var wrongHash = await NewRating(db, second); wrongHash.InputHash = SHA256.HashData([1, 2, 3]);
            db.Add(wrongHash); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var routing = new SettingVersion { Scope = "underwriting-routing-test", Version = 1, EffectiveFrom = DateTimeOffset.UtcNow, Values = "{}" };
            db.Add(routing); await db.SaveChangesAsync();
            QuoteSubmission Submission(int sequence) => new() { CycleId = first.Id, QuoteId = quote.Id, Sequence = sequence,
                OperationKey = "same-command", SubmittedBy = fixture.Actor, CreatedBy = fixture.Actor, RoutingVersionId = routing.Id,
                AssignedUserId = fixture.Actor, Reason = "Test routing" };
            var submission = Submission(1); db.Add(submission); await db.SaveChangesAsync();
            db.Add(Submission(2)); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            await Reject($"DELETE QuoteSubmission WHERE Id={submission.Id}");
            var grant = await db.Set<UserAuthorityGrant>().FirstAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={fixture.Actor},RevocationReason=N'Operator revocation' WHERE Id={grant.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RatingRuleVersion SET State=N'retired' WHERE Id={first.RatingRuleVersionId}");
            await Seed(db);
            Assert.NotNull(await db.Set<UserAuthorityGrant>().Where(x => x.Id == grant.Id).Select(x => x.RevokedAt).SingleAsync());
            Assert.Equal("retired", await db.Set<RatingRuleVersion>().Where(x => x.Id == first.RatingRuleVersionId).Select(x => x.State).SingleAsync());
            await Reject($"UPDATE UserAuthorityGrant SET RevokedAt=NULL,RevokedBy=NULL,RevocationReason=NULL WHERE Id={grant.Id}");
            await Reject($"UPDATE RatingRuleVersion SET State=N'published' WHERE Id={first.RatingRuleVersionId}");
            var after = await db.Set<QuoteRevision>().AsNoTracking().Select(x => new { x.Id, x.ProposalJson, x.ContentHash }).ToArrayAsync();
            Assert.Equal(before.Select(x => x.Id).Order(), after.Select(x => x.Id).Order());
            foreach (var row in before) { var kept = after.Single(x => x.Id == row.Id); Assert.Equal(row.ProposalJson, kept.ProposalJson); Assert.Equal(row.ContentHash, kept.ContentHash); }
        });
    }

    [Fact]
    public async Task RealSqlUnderwritingMigrationPreservesPhaseFiveQuoteBytesAndCredentials()
    {
        await WithDatabase(async db =>
        {
            var fixture = await Fixture(db, published: false); var quoteId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT Quote(Id,AgencyId,ClientId,RelationshipId,ProductId,State,CreatedAt,CreatedBy,UpdatedAt) VALUES({quoteId},{fixture.Agency},{fixture.Client},{fixture.Relationship},{fixture.Product},N'draft',{now},{fixture.Actor},{now})");
            var revision = Revision(quoteId, fixture); db.Add(revision); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET CurrentRevisionId={revision.Id} WHERE Id={quoteId}");
            var credentials = await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToListAsync();
            await db.Database.MigrateAsync(); await Seed(db);
            var retained = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == revision.Id);
            Assert.Equal(revision.ProposalJson, retained.ProposalJson); Assert.Equal(revision.ContentHash, retained.ContentHash);
            Assert.Equal(credentials, await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToListAsync());
            Assert.Equal(1, await db.Set<Quote>().CountAsync()); Assert.Null(await db.Set<Quote>().Select(x => x.CurrentUnderwritingCycleId).SingleAsync());
            Assert.Equal(2, await db.Set<RatingRuleVersion>().CountAsync()); Assert.Equal(4, await db.Set<UserAuthorityGrant>().CountAsync());
            Assert.Contains("20260916184737_UnderwritingCoreStorage", await db.Database.GetAppliedMigrationsAsync());
        }, upgrade: true);
    }

    private sealed record TestFixture(Guid Agency, Guid Client, Guid Relationship, Guid Product, Guid ProductVersion, Guid Terms, Guid Actor);
    private static async Task<TestFixture> Fixture(BackOfficeDbContext db, bool published)
    {
        var actor = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
        var requester = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example");
        var product = await db.Set<Product>().SingleAsync(x => x.Code == "motor-trade-road-risks");
        var version = await db.Set<ProductVersion>().SingleAsync(x => x.ProductId == product.Id && x.Version == (published ? 2 : 1));
        var agency = new Agency { Reference = "AG-UW-STORAGE", LegalName = "Fictional underwriting storage" };
        var client = new ClientAccount { Reference = "CL-UW-STORAGE", LegalName = "Fictional underwriting client", NormalizedName = "FICTIONAL UNDERWRITING CLIENT" };
        db.AddRange(agency, client); await db.SaveChangesAsync();
        var relationship = new ClientAgencyRelationship { AgencyId = agency.Id, ClientId = client.Id };
        var request = new AgencyStateRequest { AgencyId = agency.Id, BaseVersion = agency.RowVersion, ProposedInputFingerprint = new string('a', 64),
            RequestedBy = requester.Id, CreatedBy = requester.Id, RequestReason = "Fictional activation" };
        db.AddRange(relationship, request); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={actor.Id},DecisionReason=N'Fictional approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={request.Id}");
        var snapshot = JsonSerializer.Serialize(new { effectiveFrom = "2026-09-15", commercialTerms = new { commissionBasis = "per-product" }, creditLimit = "0.00",
            products = new[] { new { productVersionId = version.Id, effectiveFrom = "2026-09-15", brokerCommissionBasisPoints = 1000 } } });
        var terms = new AgencyTermsVersion { AgencyId = agency.Id, Version = 1, EffectiveFrom = new(2026, 9, 15), ApprovedStateRequestId = request.Id, CreatedBy = actor.Id, Snapshot = snapshot };
        db.Add(terms); await db.SaveChangesAsync();
        return new(agency.Id, client.Id, relationship.Id, product.Id, version.Id, terms.Id, actor.Id);
    }
    private static QuoteRevision Revision(Guid quoteId, TestFixture f)
    {
        const string proposal = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\"}"; var now = DateTimeOffset.UtcNow;
        return new() { QuoteId = quoteId, AgencyId = f.Agency, ClientId = f.Client, RelationshipId = f.Relationship, ProductId = f.Product,
            ProductVersionId = f.ProductVersion, AgencyTermsVersionId = f.Terms, Number = 1, QuestionSetVersion = QuoteCatalogueIdentity.Version,
            ProposalJson = proposal, ContentHash = SHA256.HashData(Encoding.UTF8.GetBytes(proposal)), CreatedBy = f.Actor, SavedBy = f.Actor, CreatedAt = now, SavedAt = now };
    }
    private static async Task<Quote> InsertQuote(BackOfficeDbContext db, TestFixture f)
    {
        var quote = new Quote { AgencyId = f.Agency, ClientId = f.Client, RelationshipId = f.Relationship, ProductId = f.Product, CreatedBy = f.Actor };
        db.Add(quote); await db.SaveChangesAsync(); var revision = Revision(quote.Id, f); db.Add(revision); await db.SaveChangesAsync();
        quote.CurrentRevisionId = revision.Id; await db.SaveChangesAsync(); return quote;
    }
    private static async Task<UnderwritingCycle> NewCycle(BackOfficeDbContext db, Quote q, TestFixture f, int sequence)
    {
        var rules = await db.Set<RatingRuleVersion>().SingleAsync(x => x.ProductId == f.Product);
        var authority = await db.Set<AuthorityVersion>().FirstAsync(x => x.ProductVersionId == f.ProductVersion);
        var cycle = new UnderwritingCycle { QuoteId = q.Id, QuoteRevisionId = q.CurrentRevisionId!.Value, AgencyId = f.Agency, ClientId = f.Client,
            RelationshipId = f.Relationship, ProductId = f.Product, ProductVersionId = f.ProductVersion, AgencyTermsVersionId = f.Terms,
            RatingRuleVersionId = rules.Id, AuthorityVersionId = authority.Id, BinderVersionId = authority.BinderVersionId, Sequence = sequence,
            RequestedBy = f.Actor, CreatedBy = f.Actor, PricingInputHash = SHA256.HashData([42]), StartsAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), EndsAt = new(2027, 10, 1, 0, 0, 0, TimeSpan.Zero) };
        var work = new OutboxWork { Kind = "quote-rating", SubjectRecordId = cycle.Id, OperationKey = Guid.NewGuid().ToString(), NextAttemptAt = DateTimeOffset.UtcNow };
        db.Add(work); await db.SaveChangesAsync(); cycle.WorkId = work.Id; return cycle;
    }
    private static async Task<UnderwritingCycle> InsertCycle(BackOfficeDbContext db, Quote q, TestFixture f)
    { var cycle = await NewCycle(db, q, f, 1); db.Add(cycle); await db.SaveChangesAsync(); return cycle; }
    private static async Task<QuoteRatingResult> NewRating(BackOfficeDbContext db, UnderwritingCycle cycle)
    {
        var count = await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == cycle.WorkId);
        var now = DateTimeOffset.UtcNow; var attempt = new AdapterAttempt { WorkId = cycle.WorkId, AttemptNumber = count + 1, StartedAt = now, EndedAt = now, Outcome = "success" };
        db.Add(attempt); await db.SaveChangesAsync();
        return new() { CycleId = cycle.Id, QuoteId = cycle.QuoteId, WorkId = cycle.WorkId, AttemptId = attempt.Id, RuleVersionId = cycle.RatingRuleVersionId,
            InputHash = cycle.PricingInputHash, AnnualPremium = 600, TermPremium = 600, Tax = 72, Fee = 35, GrossPayable = 707, BrokerCommission = 60,
            CreatedAt = now, CompletedAt = now, ExpiresAt = now.AddDays(14) };
    }
    private static async Task<QuoteRatingResult> InsertRating(BackOfficeDbContext db, UnderwritingCycle cycle)
    { var result = await NewRating(db, cycle); db.Add(result); await db.SaveChangesAsync(); return result; }
    private static async Task Seed(BackOfficeDbContext db)
    { await using var tx = await db.Database.BeginTransactionAsync(); await UnderwritingSeed.SeedAsync(db); await tx.CommitAsync(); db.ChangeTracker.Clear(); }
    private static async Task WithDatabase(Func<BackOfficeDbContext, Task> test, bool upgrade = false)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options);
            if (upgrade) await db.GetService<IMigrator>().MigrateAsync("20260916142410_QuoteMatchOwnership"); else await db.Database.MigrateAsync();
            await DemoDatabase.SeedAsync(db, "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1");
            await test(db);
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
