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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSqlQuoteTermsStorageRetainsUpgradeHistoryAndRejectsForeignTermsOwnership(bool upgrade)
    {
        await WithDatabase(async db =>
        {
            if (upgrade) await db.GetService<IMigrator>().MigrateAsync("20260916231026_CapacityEscalationStorage");
            await Seed(db); var f = await Fixture(db, published: true);
            var quote = await InsertQuote(db, f); var other = await InsertQuote(db, f);
            var cycle = await InsertCycle(db, quote, f, legacy: upgrade); var foreign = await InsertCycle(db, other, f, legacy: upgrade);
            var rating = await InsertRating(db, cycle);
            var retained = await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ProposalJson, x.ContentHash }).ToArrayAsync();
            if (upgrade) { await db.Database.MigrateAsync(); db.ChangeTracker.Clear(); }
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(retained.Select(x => x.ProposalJson), await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync());
            foreach (var original in retained) Assert.Equal(original.ContentHash, await db.Set<QuoteRevision>().Where(x => x.Id == original.Id).Select(x => x.ContentHash).SingleAsync());
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteTermsSeed.SeedAsync(db); await tx.CommitAsync(); }
            var template = await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x => x.ProductId == f.Product);
            var now = DateTimeOffset.UtcNow;
            var terms = new QuoteTermsVersion { QuoteId = quote.Id, CycleId = cycle.Id, RatingId = rating.Id, TemplateVersionId = template.Id, Number = 1,
                TermsHash = new string('a', 64), AssuranceHashAtPreparation = new string('b', 64), PreparedAt = now, CreatedAt = now, PreparedBy = f.Actor, CreatedBy = f.Actor };
            db.Add(terms); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UnderwritingCycle SET CurrentTermsVersionId={terms.Id} WHERE Id={cycle.Id}");
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UnderwritingCycle SET CurrentTermsVersionId={terms.Id} WHERE Id={foreign.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteTermsVersion SET TermsHash={new string('c', 64)} WHERE Id={terms.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE TemplateVersion WHERE Id={template.Id}"));
            var retainedTemplate = template.ContentJson;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TemplateVersion SET State=N'retired' WHERE Id={template.Id}");
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteTermsSeed.SeedAsync(db); await tx.CommitAsync(); }
            Assert.Equal("retired", await db.Set<TemplateVersion>().AsNoTracking().Where(x => x.Id == template.Id).Select(x => x.State).SingleAsync());
            Assert.Equal(retainedTemplate, await db.Set<TemplateVersion>().AsNoTracking().Where(x => x.Id == template.Id).Select(x => x.ContentJson).SingleAsync());
        });
    }
}
