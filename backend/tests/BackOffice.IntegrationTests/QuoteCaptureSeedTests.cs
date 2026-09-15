using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class QuoteCaptureSeedTests
{
    [Fact]
    public async Task RealSqlCaptureInitializationPreservesProductsAndLaterOperatorSettings()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync();
            var password = "Demo!" + Guid.NewGuid().ToString("N") + "a1";
            await DemoDatabase.SeedAsync(db, password);
            Assert.Empty(await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").ToListAsync());
            var products = await db.Set<ProductVersion>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.QuestionSetVersion, x.Definition, x.RowVersion }).ToListAsync();
            var distribution = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Scope == "agency-distribution");
            await Assert.ThrowsAsync<InvalidOperationException>(() => QuoteCaptureDemoSeed.SeedAsync(db));
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true);
            var seeded = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Scope == "quote-capture");
            var pins = QuoteCaptureConfiguration.Parse(seeded.Values)!; Assert.Equal(2, pins.Count);
            var allowed = await (from version in db.Set<ProductVersion>() join product in db.Set<Product>() on version.ProductId equals product.Id
                where product.Code == "motor-trade-road-risks" || product.Code == "motor-trade-combined" select version.Id).ToListAsync();
            Assert.True(allowed.ToHashSet().SetEquals(pins.Keys));
            Assert.All(pins.Values, x => Assert.Equal(QuoteCatalogueIdentity.Version, x.QuestionSetVersion));
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true);
            Assert.Equal(seeded.Id, (await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "quote-capture")).Id);
            const string revoked = "{\"demo\":true,\"kind\":\"quote-capture\",\"products\":[]}";
            var withdrawal = new SettingVersion { Scope = "quote-capture", Version = 2, EffectiveFrom = seeded.EffectiveFrom, Values = revoked };
            db.Add(withdrawal); await db.SaveChangesAsync();
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true);
            Assert.Equal(2, await db.Set<SettingVersion>().CountAsync(x => x.Scope == "quote-capture"));
            Assert.Equal(revoked, (await db.Set<SettingVersion>().SingleAsync(x => x.Id == withdrawal.Id)).Values);
            db.Add(new SettingVersion { Scope = "quote-capture", Version = 3, EffectiveFrom = seeded.EffectiveFrom, Values = "{}" });
            await db.SaveChangesAsync(); await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true);
            Assert.Equal(3, await db.Set<SettingVersion>().CountAsync(x => x.Scope == "quote-capture"));
            Assert.Equal("{}", (await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "quote-capture" && x.Version == 3)).Values);
            var retained = await db.Set<ProductVersion>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            for (var i = 0; i < products.Count; i++)
            {
                Assert.Equal(products[i].Id, retained[i].Id); Assert.Equal(products[i].Definition, retained[i].Definition);
                Assert.Equal(products[i].QuestionSetVersion, retained[i].QuestionSetVersion); Assert.Equal(products[i].RowVersion, retained[i].RowVersion);
            }
            Assert.Equal(distribution.Values, (await db.Set<SettingVersion>().SingleAsync(x => x.Id == distribution.Id)).Values);
            Assert.Empty(await db.Set<Quote>().ToListAsync()); // Configuration alone does not invent demonstration quotes.
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
