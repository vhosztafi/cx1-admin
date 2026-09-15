using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteServiceRollsBackLateFailuresAndSerializesCompetingCommands()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync();
            }
            var userId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(userId, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var service = new QuoteService(new QuoteFactory(options), new QuoteTime());
            const string proposal = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\",\"risk\":{\"vehicles\":[{\"id\":\"bbbbbbbb-0000-4000-8000-000000000001\",\"registration\":\"DEMO03\"}]}}";
            // Fixed test-owned table names. Fail after business writes and again at
            // receipt insertion to verify the actual command transaction rolls back.
            foreach (var table in new[] { "QuoteActivity", "IdempotencyRecord" })
            {
                await db.Database.ExecuteSqlRawAsync(table == "QuoteActivity"
                    ? "CREATE TRIGGER TR_QuoteServiceTestFail ON QuoteActivity AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51077, 'Injected quote test failure.', 1; END;"
                    : "CREATE TRIGGER TR_QuoteServiceTestFail ON IdempotencyRecord AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51077, 'Injected quote test failure.', 1; END;");
                try
                {
                    var error = await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "fail-" + table, Guid.NewGuid()));
                    Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Errors.Cast<SqlError>(), x => x.Number == 51077);
                    Assert.Equal(0, await db.Set<Quote>().CountAsync()); Assert.Equal(0, await db.Set<QuoteRevision>().CountAsync());
                    Assert.Equal(0, await db.Set<QuoteRegistration>().CountAsync()); Assert.Equal(0, await db.Set<QuoteActivity>().CountAsync());
                    Assert.Equal(0, await db.Set<ClientActivity>().CountAsync(x => x.RecordKind == "quote"));
                    Assert.Equal(0, await db.Set<IdempotencyRecord>().CountAsync());
                    Assert.Equal(0, await db.Set<AuditEvent>().CountAsync(x => x.EventType.StartsWith("quote.")));
                }
                finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_QuoteServiceTestFail"); }
            }
            var creates = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "concurrent-create", Guid.NewGuid())));
            Assert.Single(creates, x => x.Replayed); Assert.Single(creates.Select(x => x.ResourceId).Distinct());
            var initial = await service.GetAsync(actor, creates[0].ResourceId);
            var revised = proposal.Replace("DEMO03", "DEMO04");
            var saves = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => service.SaveAsync(actor, initial.Quote.Id, initial.Quote.RowVersion, revised, null, "concurrent-save", Guid.NewGuid())));
            Assert.Single(saves, x => x.Replayed); Assert.Equal(2, await db.Set<QuoteRevision>().CountAsync());
            var current = await service.GetAsync(actor, initial.Quote.Id);
            async Task<int> Compete(string registration)
            {
                try { return (await service.SaveAsync(actor, current.Quote.Id, current.Quote.RowVersion, proposal.Replace("DEMO03", registration), null, registration, Guid.NewGuid())).Status; }
                catch (QuoteOperationException error) { return error.Status; }
            }
            Assert.Equal(new[] { 200, 412 }, (await Task.WhenAll(Compete("DEMO05"), Compete("DEMO06"))).Order().ToArray());
            Assert.Equal(1, await db.Set<Quote>().CountAsync()); Assert.Equal(3, await db.Set<QuoteRevision>().CountAsync());
            Assert.Equal(3, await db.Set<QuoteActivity>().CountAsync()); Assert.Equal(3, await db.Set<IdempotencyRecord>().CountAsync());
            Assert.Single(await db.Set<QuoteRegistration>().ToListAsync());
        });
    }

    [Fact]
    public async Task RealSqlQuoteServiceCreatesSavesReplaysAndRetainsHistory()
    {
        await WithDatabase(async (db, password) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync();
            }
            var userId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(userId, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var service = new QuoteService(new QuoteFactory(options), new QuoteTime());
            var correlation = Guid.NewGuid();
            var create = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "create-one", correlation);
            Assert.Equal(201, create.Status); Assert.False(create.Replayed);
            Assert.Equal(create.ResourceId, JsonDocument.Parse(create.Body).RootElement.GetProperty("id").GetGuid());
            var replay = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "create-one", Guid.NewGuid());
            Assert.True(replay.Replayed); Assert.Equal(create.ResourceId, replay.ResourceId); Assert.Equal(create.Etag, replay.Etag);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, "{}", "create-one", correlation));
            var first = await service.GetAsync(actor, create.ResourceId);
            Assert.Equal(1, first.Revision.Number); Assert.Equal(fixture.Terms, first.Revision.AgencyTermsVersionId);
            await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveAsync(actor, create.ResourceId, first.Quote.RowVersion, null!, null, "null-save", correlation));
            var unchanged = await service.SaveAsync(actor, create.ResourceId, first.Quote.RowVersion, first.Revision.ProposalJson, null, "unchanged", correlation);
            Assert.Equal(create.Etag, unchanged.Etag); Assert.Equal(1, await db.Set<QuoteRevision>().CountAsync());
            Assert.Equal(1, await db.Set<QuoteActivity>().CountAsync());
            const string proposal = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\",\"termIntent\":{\"localStartDate\":\"2026-10-01\"},\"risk\":{\"vehicles\":[{\"id\":\"bbbbbbbb-0000-4000-8000-000000000001\",\"registration\":\"DEMO 02\"}]}}";
            var save = await service.SaveAsync(actor, create.ResourceId, first.Quote.RowVersion, proposal, "Fictional revision", "save-one", correlation);
            Assert.NotEqual(create.Etag, save.Etag);
            Assert.True((await service.SaveAsync(actor, create.ResourceId, first.Quote.RowVersion, proposal, "Fictional revision", "save-one", correlation)).Replayed);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SaveAsync(actor, create.ResourceId, first.Quote.RowVersion, proposal, null, "stale", correlation))).Status);
            var second = await service.GetAsync(actor, create.ResourceId);
            Assert.Equal(2, second.Revision.Number); Assert.Equal("DEMO02", (await db.Set<QuoteRegistration>().SingleAsync()).NormalizedRegistration);
            Assert.Equal(first.Revision.ProposalJson, (await db.Set<QuoteRevision>().SingleAsync(x => x.Id == first.Revision.Id)).ProposalJson);
            Assert.Equal(2, await db.Set<ClientActivity>().CountAsync(x => x.RecordKind == "quote" && x.RecordId == create.ResourceId));
            Assert.Equal(3, await db.Set<IdempotencyRecord>().CountAsync(x => x.ActorScope == userId.ToString("N") && x.Route.StartsWith("/api/v1/quotes")));
            // Removing a current vehicle removes its projection but preserves history.
            await service.SaveAsync(actor, create.ResourceId, second.Quote.RowVersion, first.Revision.ProposalJson, "Remove fictional vehicle", "remove", correlation);
            Assert.Empty(await db.Set<QuoteRegistration>().ToListAsync()); Assert.Equal(3, await db.Set<QuoteRevision>().CountAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "create-one", correlation))).Status);
            Assert.Equal(3, (await service.GetAsync(actor, create.ResourceId)).Revision.Number);
            await Assert.ThrowsAsync<QuoteOperationException>(() => service.GetAsync(actor with { Roles = new HashSet<string> { "system-admin" } }, create.ResourceId));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET CaptureClosedAt={DateTimeOffset.UtcNow},CaptureClosedReason=N'Progressed fixture' WHERE Id={create.ResourceId}");
            var closed = await service.GetAsync(actor, create.ResourceId);
            Assert.Equal("quote-capture-closed", (await Assert.ThrowsAsync<QuoteInputException>(() => service.SaveAsync(actor, create.ResourceId, closed.Quote.RowVersion, proposal, null, "closed", correlation))).Code);
            Assert.Equal(3, await db.Set<QuoteRevision>().CountAsync());
            var keyPath = Path.GetFullPath(Path.Combine(".local", "quote-activity-test-keys", db.Database.GetDbConnection().Database));
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:DataProtectionPath", keyPath));
            using var browser = host.CreateClient();
            var csrf = (await browser.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "underwriter@cover.example", password }) };
            login.Headers.Add("X-CSRF-Token", csrf);
            using var loggedIn = await browser.SendAsync(login); loggedIn.EnsureSuccessStatusCode();
            var activity = await browser.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{fixture.Client}/activity");
            var quoteEvents = activity.GetProperty("items").EnumerateArray().Where(x => x.GetProperty("eventType").GetString()!.StartsWith("quote.", StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, quoteEvents.Length);
            Assert.Contains(quoteEvents, x => x.GetProperty("summary").GetString() == "Quote created.");
            Assert.Contains(quoteEvents, x => x.GetProperty("summary").GetString() == "Quote saved.");
            Assert.All(quoteEvents, x =>
            {
                Assert.False(x.TryGetProperty("proposal", out _));
                Assert.False(x.TryGetProperty("recordId", out var id) && id.ValueKind != JsonValueKind.Null); // Quote linking is still unavailable.
            });
            using var agencyReader = host.CreateClient();
            var agencyCsrf = (await agencyReader.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var agencyLogin = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = "agency-admin@cover.example", password }) };
            agencyLogin.Headers.Add("X-CSRF-Token", agencyCsrf);
            using var agencyLoggedIn = await agencyReader.SendAsync(agencyLogin); agencyLoggedIn.EnsureSuccessStatusCode();
            var restrictedActivity = await agencyReader.GetFromJsonAsync<JsonElement>($"/api/v1/clients/{fixture.Client}/activity");
            Assert.DoesNotContain(restrictedActivity.GetProperty("items").EnumerateArray(), x => x.GetProperty("eventType").GetString()!.StartsWith("quote.", StringComparison.Ordinal));
            Assert.Equal(0, restrictedActivity.GetProperty("totalCount").GetInt32());
        });
    }

    private sealed class QuoteFactory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(options);
    }
    private sealed class QuoteTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    }

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

            var readerId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var reader = new ActorContext(readerId, null, null, new HashSet<string> { "underwriter" });
            await using (var readTransaction = await db.Database.BeginTransactionAsync())
            {
                var owned = await QuoteScope.ForQuoteAsync(db, reader, quote.Id, QuoteAccess.Read);
                Assert.Equal(second.Id, owned.Quote.CurrentRevisionId);
                Assert.Equal(fixture.Client, owned.Scope.Client.Id);
                Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => QuoteScope.ForQuoteAsync(db, reader, other.Id, QuoteAccess.Read))).Status);
                Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => QuoteScope.ForQuoteAsync(db, reader, quote.Id, QuoteAccess.Capture))).Status);
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var captureTransaction = await db.Database.BeginTransactionAsync())
            {
                Assert.Equal(quote.Id, (await QuoteScope.ForQuoteAsync(db, reader, quote.Id, QuoteAccess.Capture)).Quote.Id);
            }

            var asOf = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
            async Task<EligibleQuoteCapture> Eligible(Guid? retainedTerms = null)
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                var scope = await QuoteScope.ForQuoteAsync(db, reader, quote.Id, QuoteAccess.Capture);
                return await QuoteCaptureEligibility.ResolveAsync(db, scope.Scope, fixture.ProductVersion, asOf, retainedTerms);
            }
            Assert.Equal(503, (await Assert.ThrowsAsync<QuoteOperationException>(() => Eligible())).Status);
            var captureJson = JsonSerializer.Serialize(new { demo = true, kind = "quote-capture", products = new[] {
                new { productVersionId = fixture.ProductVersion, schemaVersion = "1.0", questionSetVersion = QuoteCatalogueIdentity.Version, referenceVersion = QuoteCatalogueIdentity.Version } } });
            async Task Configure(int number, string json, DateTimeOffset? effective = null)
            {
                db.Add(new SettingVersion { Scope = "quote-capture", Version = number, EffectiveFrom = effective ?? asOf.AddDays(-1), Values = json });
                await db.SaveChangesAsync();
            }
            await Configure(1, captureJson);
            var selection = await Eligible();
            Assert.Equal(fixture.Terms, selection.Pins.AgencyTermsVersionId);
            Assert.Equal(QuoteCatalogueIdentity.Version, selection.Pins.QuestionSetVersion);
            Assert.Equal("demo-1", selection.ProductVersion.QuestionSetVersion); // Capture pins do not relabel rating metadata.
            Assert.Contains("\"ratingAvailable\":false", selection.ProductVersion.Definition);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Eligible(otherAgency.Terms))).Status);
            await Configure(2, "{}", asOf.AddDays(1)); Assert.Equal(fixture.Terms, (await Eligible()).Terms.Id);
            await Configure(3, "{}"); Assert.Equal(503, (await Assert.ThrowsAsync<QuoteOperationException>(() => Eligible())).Status);
            await Configure(4, "{\"demo\":true,\"kind\":\"quote-capture\",\"products\":[]}");
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Eligible())).Status);
            await Configure(5, captureJson);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityProvider SET State=N'inactive' WHERE Id={selection.ProductVersion.ProviderId}");
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Eligible())).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityProvider SET State=N'active' WHERE Id={selection.ProductVersion.ProviderId}");
            Assert.Equal(selection.Pins, (await Eligible(fixture.Terms)).Pins);
            await using (var eligibilityTransaction = await db.Database.BeginTransactionAsync())
            {
                var scope = await QuoteScope.ForQuoteAsync(db, reader, quote.Id, QuoteAccess.Capture);
                await QuoteCaptureEligibility.ResolveAsync(db, scope.Scope, fixture.ProductVersion, asOf);
                var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
                await using var writer = new BackOfficeDbContext(options);
                Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(() => writer.Database.ExecuteSqlInterpolatedAsync(
                    $"SET LOCK_TIMEOUT 150; UPDATE CapacityProvider SET State=N'inactive' WHERE Id={selection.ProductVersion.ProviderId}"))).Number);
                var insertedId = Guid.NewGuid();
                Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(() => writer.Database.ExecuteSqlInterpolatedAsync(
                    $"SET LOCK_TIMEOUT 150; INSERT INTO SettingVersion (Id,Scope,Version,EffectiveFrom,[Values],CreatedAt) VALUES ({insertedId},N'quote-capture',6,{asOf},N'{{}}',{DateTimeOffset.UtcNow})"))).Number);
            }

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
