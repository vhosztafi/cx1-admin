using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteDemoCreatesBothProductsAndPreservesEditsOnRepeatedSeed()
    {
        await WithDatabase(async (db, _) =>
        {
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var factory = new QuoteFactory(options); var demo = new QuoteDemo(factory, new QuoteTime());
            var result = await demo.SeedAsync(); Assert.Equal(8, result.Created); Assert.Equal(8, result.QuoteIds.Distinct().Count());
            Assert.Equal(8, await db.Set<Quote>().CountAsync()); Assert.Equal(8, await db.Set<QuoteRevision>().CountAsync());
            Assert.Equal(8, await db.Set<IdempotencyRecord>().CountAsync());
            Assert.Equal(2, await db.Set<AgencyProduct>().CountAsync(x => x.AgencyTermsVersionId == db.Set<AgencyTermsVersion>().Where(t => t.AgencyId == QuoteDemo.AgencyId).Select(t => t.Id).Single()));
            var underwriter = await db.Set<StaffUser>().SingleAsync(x => x.Email == "underwriter@cover.example");
            var actor = new ActorContext(underwriter.Id, underwriter.TeamId, null, new HashSet<string> { "underwriter" });
            var service = new QuoteService(factory, new QuoteTime());
            var views = await Task.WhenAll(result.QuoteIds.Select(id => service.GetAsync(actor, id)));
            Assert.Equal(2, views.Count(x => x.TermAssessment.Term is null)); Assert.Equal(6, views.Count(x => x.TermAssessment.Term is not null));
            Assert.Equal(4, views.Count(x => x.ProductCode == "motor-trade-road-risks")); Assert.Equal(4, views.Count(x => x.ProductCode == "motor-trade-combined"));
            var draft = views.First(x => x.TermAssessment.Term is null);
            var edited = draft.Revision.ProposalJson.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.0\",\"termIntent\":{\"localStartDate\":\"2026-11-01\"}");
            await service.SaveAsync(actor, draft.Quote.Id, draft.Quote.RowVersion, edited, "Preserve fictional edit", "quote-demo-user-edit", Guid.NewGuid());
            var replay = await demo.SeedAsync(); Assert.Equal(0, replay.Created); Assert.Equal(result.QuoteIds, replay.QuoteIds);
            Assert.Equal(9, await db.Set<QuoteRevision>().CountAsync());
            Assert.Contains("2026-11-01", (await service.GetAsync(actor, draft.Quote.Id)).Revision.ProposalJson);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={QuoteDemo.AgencyId}");
            Assert.Equal("quote-demo-products-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => demo.SeedAsync())).Code);
            Assert.Equal("suspended", await db.Set<Agency>().Where(x => x.Id == QuoteDemo.AgencyId).Select(x => x.State).SingleAsync());
            Assert.Equal(8, await db.Set<Quote>().CountAsync()); Assert.Equal(9, await db.Set<QuoteRevision>().CountAsync());
            Assert.Equal("draft", await db.Set<Agency>().Where(x => x.Id == PartyDemoSeed.FirstAgencyId).Select(x => x.State).SingleAsync());
            Assert.Equal("draft", await db.Set<Agency>().Where(x => x.Id == PartyDemoSeed.SecondAgencyId).Select(x => x.State).SingleAsync());
        });
    }
}
