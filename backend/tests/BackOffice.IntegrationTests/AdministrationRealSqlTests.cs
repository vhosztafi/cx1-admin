using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlAdministrationCataloguePinsSuccessorAndRejectsOverlapStaleWriteAndRevokedReplay()
    {
        await WithDatabase(async (db, _) =>
        {
            var factory = new AdministrationFactory(db.Database.GetConnectionString()!);
            var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
            var actor = new ActorContext(user.Id, user.TeamId, null, new HashSet<string> { "system-admin" });
            var service = new ProductAdministration(factory, new SqlCommandBoundary(factory, TimeProvider.System), TimeProvider.System);
            var product = await db.Set<Product>().SingleAsync(x => x.Code == "motor-trade-road-risks");
            var source = await db.Set<ProductVersion>().AsNoTracking().SingleAsync(x => x.ProductId == product.Id && x.Version == 1);
            var sourceJson = source.Definition;
            var historic = await Fixture(db, 1);
            var historicRevision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.QuoteId == historic.Quote);
            var input = new ProductVersionEdit(source.ProviderId, new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new(2032, 1, 1, 0, 0, 0, TimeSpan.Zero), ["road-risks"], "Reviewed fictional successor");
            var key = Guid.NewGuid().ToString("N");
            Assert.Equal("referenced-version-immutable", (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.SaveAsync(actor, source.Id, AdminAccess.Etag(source.RowVersion), input, Guid.NewGuid().ToString("N")))).Code);
            Assert.Equal(400, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CloneAsync(actor, source.Id,
                AdminAccess.Etag(source.RowVersion), input with { CoverSections = ["invented-cover"] }, Guid.NewGuid().ToString("N")))).Status);
            var clone = await service.CloneAsync(actor, source.Id, AdminAccess.Etag(source.RowVersion), input, key);
            Assert.Equal(clone.ResourceId, (await service.CloneAsync(actor, source.Id, AdminAccess.Etag(source.RowVersion), input, key)).ResourceId);
            var draft = JsonSerializer.Deserialize<ProductVersionView>(clone.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var saved = await service.SaveAsync(actor, draft.Id, draft.Etag, input with { Reason = "Reviewed draft details" }, Guid.NewGuid().ToString("N"));
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.SaveAsync(actor, draft.Id, draft.Etag, input, Guid.NewGuid().ToString("N")))).Status);
            draft = JsonSerializer.Deserialize<ProductVersionView>(saved.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var published = await service.PublishAsync(actor, draft.Id, draft.Etag, "Publish reviewed version", Guid.NewGuid().ToString("N"));
            var current = JsonSerializer.Deserialize<ProductVersionView>(published.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("published", current.State);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.SaveAsync(actor, current.Id, current.Etag, input, Guid.NewGuid().ToString("N")))).Status);
            var overlap = await service.CloneAsync(actor, source.Id, AdminAccess.Etag(source.RowVersion), input, Guid.NewGuid().ToString("N"));
            var other = JsonSerializer.Deserialize<ProductVersionView>(overlap.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("product-period-overlap", (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.PublishAsync(actor, other.Id, other.Etag, "Overlap attempt", Guid.NewGuid().ToString("N")))).Code);
            db.ChangeTracker.Clear();
            Assert.Equal(sourceJson, (await db.Set<ProductVersion>().AsNoTracking().SingleAsync(x => x.Id == source.Id)).Definition);
            Assert.False(await db.Set<AgencyProduct>().AnyAsync(x => x.ProductVersionId == current.Id));
            var capture = await db.Set<SettingVersion>().Where(x => x.Scope == "quote-capture").OrderByDescending(x => x.Version).FirstAsync();
            Assert.Contains(current.Id, QuoteCaptureConfiguration.Parse(capture.Values)!.Keys);
            Assert.True(await db.Set<AuditEvent>().AnyAsync(x => x.SubjectRecordId == current.Id && x.EventType == "administration.product-publication"));
            // Use a separately approved agency terms snapshot for the successor.
            var future = await Fixture(db, current.Version);
            var futureQuote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == future.Quote);
            var clock = new AdministrationClock(new(2031, 6, 1, 0, 0, 0, TimeSpan.Zero));
            var offer = await new QuoteProducts(factory, clock).ListAsync(future.Actor, futureQuote.RelationshipId);
            Assert.True(offer.Single(x => x.ProductVersionId == current.Id).CaptureEligible);
            var quotes = new QuoteService(factory, clock);
            var created = await quotes.CreateAsync(future.Actor, futureQuote.RelationshipId, current.Id, null,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.QuoteId == created.ResourceId);
            Assert.Equal(current.Id, revision.ProductVersionId);
            var disallowed = JsonSerializer.Serialize(new { schemaVersion = "1.0", productCode = product.Code,
                cover = new { requestedSections = new[] { new { id = Guid.NewGuid(), code = "tools-equipment", selected = true, limit = "1000.00", excess = "100.00" } } } });
            var rejected = await Assert.ThrowsAsync<QuoteValidationException>(() => quotes.CreateAsync(future.Actor,
                futureQuote.RelationshipId, current.Id, disallowed, Guid.NewGuid().ToString("N"), Guid.NewGuid()));
            Assert.Contains(rejected.Issues, x => x.Code == "section-not-offered");
            var keptRevision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == historicRevision.Id);
            Assert.Equal(source.Id, keptRevision.ProductVersionId);
            Assert.Equal(historicRevision.ProposalJson, keptRevision.ProposalJson);
            Assert.Equal(historicRevision.ContentHash, keptRevision.ContentHash);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.CloneAsync(actor, source.Id, AdminAccess.Etag(source.RowVersion), input, key))).Status);
        });
    }

    private sealed class AdministrationFactory(string connection) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(new DbContextOptionsBuilder<BackOfficeDbContext>()
            .UseSqlServer(connection, sql => sql.UseCompatibilityLevel(160)).Options);
    }

    private sealed class AdministrationClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }
}
