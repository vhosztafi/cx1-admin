using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlAdministrationAuthorityRequiresIndependentCurrentApprovalAndPinsNewConsumption()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
            var f = await Fixture(db);
            var factory = new AdministrationFactory(db.Database.GetConnectionString()!);
            var clock = new AdministrationClock(Now);
            var service = new AuthorityAdministration(factory, new SqlCommandBoundary(factory, clock), clock);
            var admin = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
            var actor = new ActorContext(admin.Id, admin.TeamId, null, new HashSet<string> { "system-admin" });
            var role = await db.Set<Role>().SingleAsync(x => x.Code == "system-admin");
            var second = new StaffUser { Email = "authority-reviewer@cover.example", NormalizedEmail = "AUTHORITY-REVIEWER@COVER.EXAMPLE", DisplayName = "Independent authority reviewer", TeamId = admin.TeamId };
            db.Add(second); await db.SaveChangesAsync(); db.Add(new UserRole { UserId = second.Id, RoleId = role.Id }); await db.SaveChangesAsync();
            var reviewer = new ActorContext(second.Id, second.TeamId, null, new HashSet<string> { "system-admin" });
            var grantee = new StaffUser { Email = "new-authority@cover.example", NormalizedEmail = "NEW-AUTHORITY@COVER.EXAMPLE", DisplayName = "New authority holder", TeamId = admin.TeamId };
            db.Add(grantee); await db.SaveChangesAsync();
            db.Add(new UserRole { UserId = grantee.Id, RoleId = (await db.Set<Role>().SingleAsync(x => x.Code == "underwriter")).Id }); await db.SaveChangesAsync();
            var source = await db.Set<AuthorityVersion>().AsNoTracking().SingleAsync(x => x.ProductVersionId == f.ProductVersion && x.Version == "demo-senior-1");
            using var definition = JsonDocument.Parse(source.DefinitionJson);
            var input = new AuthorityProposal(source.Id, AdminAccess.Etag(source.RowVersion), f.ProductVersion, Now, source.EffectiveTo,
                definition.RootElement.GetProperty("limits").Clone(), admin.TeamId!.Value, [grantee.Id], "Reviewed fictional authority successor");
            var excessive = System.Text.Json.Nodes.JsonNode.Parse(input.Limits.GetRawText())!;
            excessive["annualPremiumLimit"] = "99999999999.99";
            Assert.Equal("authority-outside-binder", (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ProposeAsync(actor,
                input with { Limits = JsonSerializer.SerializeToElement(excessive) }, Guid.NewGuid().ToString("N")))).Code);
            var key = Guid.NewGuid().ToString("N");
            var result = await service.ProposeAsync(actor, input, key);
            Assert.Equal(result.ResourceId, (await service.ProposeAsync(actor, input, key)).ResourceId);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => service.ProposeAsync(actor, input with { Reason = "Different intent" }, key));
            var request = JsonSerializer.Deserialize<AdministrationRequest>(result.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var spareResult = await service.ProposeAsync(actor, input with { UserIds = [] }, Guid.NewGuid().ToString("N"));
            var spare = JsonSerializer.Deserialize<AdministrationRequest>(spareResult.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("independent-approval-required", (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DecideAsync(actor, request.Id, request.Etag, true, "Cannot self approve", Guid.NewGuid().ToString("N")))).Code);
            var approved = await service.DecideAsync(reviewer, request.Id, request.Etag, true, "Independently checked", Guid.NewGuid().ToString("N"));
            Assert.Equal("approved", JsonSerializer.Deserialize<AdministrationRequest>(approved.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.State);
            db.ChangeTracker.Clear();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                var owned = await QuoteUnderwritingScope.HoldAsync(db, f.Actor, f.Quote, "quote-rate");
                var current = await QuoteRatingEligibility.ResolveAsync(db, owned, f.ProductVersion, f.Terms, Term, Now);
                Assert.NotEqual(source.Id, current.AuthorityVersion.Id);
                Assert.Equal(input.RoutingTeamId, current.Runtime.RoutingTeamId);
                var grantActor = new ActorContext(grantee.Id, grantee.TeamId, null, new HashSet<string> { "underwriter" });
                var grantedScope = await QuoteUnderwritingScope.HoldAsync(db, grantActor, f.Quote, "quote-rate");
                Assert.Single(await QuoteUnderwritingScope.GrantsAsync(db, grantedScope, f.ProductVersion, current.BinderVersion, current.Capture.Product.Code, Term, Now));
                await tx.CommitAsync();
            }
            Assert.Equal(source.DefinitionJson, (await db.Set<AuthorityVersion>().AsNoTracking().SingleAsync(x => x.Id == source.Id)).DefinitionJson);
            Assert.True(await db.Set<AuditEvent>().AnyAsync(x => x.EventType == "administration.authority-published"));
            Assert.Equal("authority-grant-overlap", (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ProposeAsync(actor, input, Guid.NewGuid().ToString("N")))).Code);
            Assert.Equal("authority-request-stale", (await Assert.ThrowsAsync<QuoteOperationException>(() => service.DecideAsync(reviewer, spare.Id,
                spare.Etag, true, "Outdated base", Guid.NewGuid().ToString("N")))).Code);
            var grant = await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x => x.UserId == grantee.Id);
            await service.RevokeAsync(actor, grant.Id, AdminAccess.Etag(grant.RowVersion), "Explicit grant withdrawal", Guid.NewGuid().ToString("N"));
            Assert.NotNull((await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x => x.Id == grant.Id)).RevokedAt);
            var pending = await service.ProposeAsync(actor, input with { UserIds = [] }, Guid.NewGuid().ToString("N"));
            var next = JsonSerializer.Deserialize<AdministrationRequest>(pending.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.DecideAsync(reviewer,
                next.Id, next.Etag, true, "Requester access revoked", Guid.NewGuid().ToString("N")))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ProposeAsync(actor, input, key))).Status);
        });
    }

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
