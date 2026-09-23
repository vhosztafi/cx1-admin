using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCommercialIssueMigrationRetainsMotorPolicyAndMidHistory()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            var outcome = await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var before = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var reference = await db.Set<Policy>().Where(x => x.Id == outcome.ResourceId).Select(x => x.Reference).SingleAsync();
            db.ChangeTracker.Clear();
            // Current Motor Trade issue owns initial MID work. Its immutable
            // history must refuse the downgrade before commercial issue storage.
            await VerifyRetainedTemplateDowngradeProtection(db, "20260920045506_CommercialExposureStorage", requiresTemplateGuard: false);
            var after = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            Assert.Equal(before.SnapshotJson, after.SnapshotJson); Assert.Equal(before.ContentHash, after.ContentHash);
            Assert.Equal(reference, await db.Set<Policy>().Where(x => x.Id == outcome.ResourceId).Select(x => x.Reference).SingleAsync());
            Assert.StartsWith("PL-MT-", reference); Assert.Equal(3, await db.Set<PolicyDocumentRequest>().CountAsync());
            Assert.Equal(1, await db.Set<Journal>().CountAsync()); Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RealSqlCommercialIssueRejectsFutureCollisionAcrossPublishedBinderVersions(bool replaceBinder) => CommercialTermsScenario(async (db, futureCycle, acceptance, actorId, now) =>
    {
        var future = await CommercialIssueCommand(db, futureCycle, acceptance, actorId);
        var issued = await future.Service.IssueAsync(future.Actor, future.Quote.Id, future.Quote.RowVersion, future.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        if (replaceBinder)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await CommercialExposureLock.AcquireAsync(db);
            var previous = await db.Set<BinderVersion>().AsNoTracking().SingleAsync(x => x.Id == futureCycle.BinderVersionId);
            var oldAuthority = await db.Set<AuthorityVersion>().AsNoTracking().SingleAsync(x => x.Id == futureCycle.AuthorityVersionId);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE BinderVersion SET State=N'retired' WHERE Id={previous.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AuthorityVersion SET State=N'retired' WHERE Id={oldAuthority.Id}");
            var binderJson = JsonNode.Parse(previous.DefinitionJson)!; binderJson["version"] = "commercial-demo-binder-next";
            var binder = new BinderVersion { ProductId = previous.ProductId, ProviderId = previous.ProviderId, Version = "commercial-demo-binder-next",
                EffectiveFrom = previous.EffectiveFrom, EffectiveTo = previous.EffectiveTo, DefinitionJson = binderJson.ToJsonString(), CreatedBy = actorId };
            db.Add(binder); await db.SaveChangesAsync();
            var authorityJson = JsonNode.Parse(oldAuthority.DefinitionJson)!; authorityJson["version"] = "commercial-demo-authority-next";
            var authority = new AuthorityVersion { ProductId = oldAuthority.ProductId, ProductVersionId = oldAuthority.ProductVersionId, BinderVersionId = binder.Id,
                Version = "commercial-demo-authority-next", EffectiveFrom = oldAuthority.EffectiveFrom, EffectiveTo = oldAuthority.EffectiveTo,
                DefinitionJson = authorityJson.ToJsonString(), CreatedBy = actorId };
            db.Add(authority); await db.SaveChangesAsync();
            var setting = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == "underwriting-runtime").OrderByDescending(x => x.Version).FirstAsync();
            var runtime = JsonNode.Parse(setting.Values)!;
            var entry = runtime["products"]!.AsArray().Single(x => x!["productVersionId"]!.GetValue<string>() == oldAuthority.ProductVersionId.ToString());
            entry!["binderVersionId"] = binder.Id; entry["authorityVersionId"] = authority.Id;
            db.Add(new SettingVersion { Scope = setting.Scope, Version = setting.Version + 1, EffectiveFrom = setting.EffectiveFrom, Values = runtime.ToJsonString(), CreatedBy = actorId });
            await db.SaveChangesAsync(); await CommercialExposureSeed.SeedAsync(db); await transaction.CommitAsync(); db.ChangeTracker.Clear();
        }
        await PublishCommercialTestLimit(db, actorId, now, 5_000_000m);
        await CommercialTermsScenario(async (_, currentCycle, currentAcceptance, currentActor, _) =>
        {
            var current = await CommercialIssueCommand(db, currentCycle, currentAcceptance, currentActor);
            Assert.True(currentCycle.StartsAt < futureCycle.StartsAt && currentCycle.EndsAt > futureCycle.StartsAt);
            Assert.Equal(replaceBinder, currentCycle.BinderVersionId != futureCycle.BinderVersionId);
            var denied = await Assert.ThrowsAsync<CommercialExposureConflictException>(() => current.Service.IssueAsync(current.Actor, current.Quote.Id, current.Quote.RowVersion, current.Input, Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Contains(denied.Assessment.Intervals, x => x.From == futureCycle.StartsAt && x.Blocker == "commercial-district-capacity-exceeded");
            var policy = Assert.Single(await db.Set<Policy>().AsNoTracking().ToArrayAsync()); Assert.Equal(issued.ResourceId, policy.Id);
            Assert.Equal(1, await db.Set<CommercialExposureVersion>().CountAsync()); Assert.Equal(1, await db.Set<Journal>().CountAsync());
        }, stopAfterAccepted: true, existingDb: db);
    }, stopAfterAccepted: true, configureProposal: proposal => proposal["termIntent"]!["localStartDate"] = "2027-03-01");

    private static async Task<(QuoteIssueService Service, ActorContext Actor, Quote Quote, QuoteIssueInput Input, RatingFactory Factory)> CommercialIssueCommand(BackOfficeDbContext db, UnderwritingCycle cycle, Guid acceptanceId, Guid actorId)
    {
        await using (var held = await db.Database.BeginTransactionAsync())
        { await CommercialExposureSeed.SeedAsync(db); await PolicyTemplateSeed.SeedAsync(db); await held.CommitAsync(); }
        db.ChangeTracker.Clear();
        var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
        var staff = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Id == actorId);
        var actor = new ActorContext(staff.Id, staff.TeamId, null, new HashSet<string> { "senior-underwriter" });
        var acceptance = await db.Set<QuoteAcceptance>().AsNoTracking().SingleAsync(x => x.Id == acceptanceId);
        var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteId);
        return (new QuoteIssueService(factory, new RatingClock()), actor, quote,
            new(cycle.Id, acceptance.RatingId, acceptance.Id, acceptance.TermsHash, acceptance.AssuranceHash, "Fictional atomic issue test"), factory);
    }

    private static async Task PublishCommercialTestLimit(BackOfficeDbContext db, Guid actorId, DateTimeOffset now, decimal amount)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await CommercialExposureLock.AcquireAsync(db);
        var previous = await db.Set<CommercialExposureLimitVersion>().AsNoTracking().OrderByDescending(x => x.Version).FirstAsync();
        var next = new CommercialExposureLimitVersion { BookId = previous.BookId, District = previous.District, Version = previous.Version + 1,
            Amount = amount, EffectiveFrom = previous.EffectiveFrom, EffectiveTo = previous.EffectiveTo, PublishedAt = now,
            SupersedesLimitId = previous.Id, CreatedBy = actorId };
        CommercialExposureSeed.SetPublication(next, "Owned test capacity publication"); db.Add(next); await db.SaveChangesAsync();
        await transaction.CommitAsync(); db.ChangeTracker.Clear();
    }

    [Fact]
    public Task RealSqlCommercialIssueLockTimeoutAndLateWriterFailureRollBackThenSameKeySucceeds() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var f = await CommercialIssueCommand(db, cycle, acceptance, actorId); var key = Guid.NewGuid().ToString();
        Task<CommandOutcome> Issue() => f.Service.IssueAsync(f.Actor, f.Quote.Id, f.Quote.RowVersion, f.Input, key, Guid.NewGuid());
        await using (var blocker = f.Factory.CreateDbContext())
        await using (var transaction = await blocker.Database.BeginTransactionAsync())
        {
            await CommercialExposureLock.AcquireAsync(blocker);
            Assert.Equal("commercial-exposure-busy", (await Assert.ThrowsAsync<QuoteOperationException>(Issue)).Code);
        }
        var beforeOutbox = await db.Set<OutboxWork>().CountAsync();
        var beforeReceipts = await db.Set<IdempotencyRecord>().CountAsync();
        // This fires after projection, immutable decision, finance and document
        // writes, at the final quote transition inside the command transaction.
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_Test_CommercialIssueFailure ON Quote AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE BoundPolicyId IS NOT NULL) THROW 51999,'Injected late issue failure',1; END;");
        try { await Assert.ThrowsAsync<DbUpdateException>(Issue); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_Test_CommercialIssueFailure;"); }
        db.ChangeTracker.Clear();
        Assert.False(await db.Set<Policy>().AnyAsync()); Assert.False(await db.Set<PolicyTerm>().AnyAsync());
        Assert.False(await db.Set<PolicyVersion>().AnyAsync()); Assert.False(await db.Set<CommercialExposureVersion>().AnyAsync());
        Assert.False(await db.Set<CommercialExposureIssueDecision>().AnyAsync()); Assert.False(await db.Set<IssueFinancialObligation>().AnyAsync());
        Assert.False(await db.Set<Journal>().AnyAsync()); Assert.False(await db.Set<PolicyDocumentRequest>().AnyAsync());
        Assert.Equal(beforeOutbox, await db.Set<OutboxWork>().CountAsync()); Assert.Equal(beforeReceipts, await db.Set<IdempotencyRecord>().CountAsync());
        Assert.Null((await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.Quote.Id)).BoundPolicyId);
        Assert.Equal(201, (await Issue()).Status); Assert.True((await Issue()).Replayed);
    }, stopAfterAccepted: true);

    [Fact]
    public Task RealSqlCommercialIssueIndependentScopesRaceForOneRemainingDistrictPlace() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var first = await CommercialIssueCommand(db, cycle, acceptance, actorId);
        await CommercialTermsScenario(async (_, secondCycle, secondAcceptance, secondActor, _) =>
        {
            var second = await CommercialIssueCommand(db, secondCycle, secondAcceptance, secondActor);
            Assert.NotEqual(first.Quote.AgencyId, second.Quote.AgencyId); Assert.NotEqual(first.Quote.ClientId, second.Quote.ClientId);
            await PublishCommercialTestLimit(db, actorId, now, 5_000_000m);
            async Task<object> TryIssue(QuoteIssueService service, ActorContext actor, Quote quote, QuoteIssueInput input)
            { try { return await service.IssueAsync(actor, quote.Id, quote.RowVersion, input, Guid.NewGuid().ToString(), Guid.NewGuid()); } catch (CommercialExposureConflictException conflict) { return conflict; } }
            var results = await Task.WhenAll(TryIssue(first.Service, first.Actor, first.Quote, first.Input), TryIssue(second.Service, second.Actor, second.Quote, second.Input));
            Assert.Single(results, x => x is CommandOutcome { Status: 201 });
            var denied = Assert.IsType<CommercialExposureConflictException>(Assert.Single(results, x => x is CommercialExposureConflictException));
            Assert.Contains(denied.Assessment.Intervals, x => x.Blocker == "commercial-district-capacity-exceeded" && x.LimitVersionId != null);
            Assert.Equal(1, await db.Set<Policy>().CountAsync()); Assert.Equal(1, await db.Set<PolicyTerm>().CountAsync());
            Assert.Equal(1, await db.Set<PolicyVersion>().CountAsync()); Assert.Equal(1, await db.Set<CommercialExposureVersion>().CountAsync());
            Assert.Equal(1, await db.Set<CommercialExposureIssueDecision>().CountAsync()); Assert.Equal(1, await db.Set<Journal>().CountAsync());
            Assert.Equal(3, await db.Set<PolicyDocumentRequest>().CountAsync());
            Assert.Equal(1, await db.Set<Quote>().CountAsync(x => x.BoundPolicyId != null));
            Assert.Equal(1, await db.Set<IdempotencyRecord>().CountAsync(x => x.Route.EndsWith("/issue")));
        }, stopAfterAccepted: true, existingDb: db);
    }, stopAfterAccepted: true);

    [Fact]
    public Task RealSqlCommercialIssueReplayAfterOtherPolicyConsumptionStillRequiresCurrentAuthority() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var first = await CommercialIssueCommand(db, cycle, acceptance, actorId); var key = Guid.NewGuid().ToString();
        Task<CommandOutcome> Issue() => first.Service.IssueAsync(first.Actor, first.Quote.Id, first.Quote.RowVersion, first.Input, key, Guid.NewGuid());
        var issued = await Issue();
        await CommercialTermsScenario(async (_, secondCycle, secondAcceptance, secondActor, _) =>
        {
            var second = await CommercialIssueCommand(db, secondCycle, secondAcceptance, secondActor);
            await second.Service.IssueAsync(second.Actor, second.Quote.Id, second.Quote.RowVersion, second.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        }, stopAfterAccepted: true, existingDb: db);
        await PublishCommercialTestLimit(db, actorId, now, 1m);
        var replay = await Issue(); Assert.True(replay.Replayed); Assert.Equal(issued.Body, replay.Body);
        Assert.Equal(2, await db.Set<Policy>().CountAsync()); Assert.Equal(2, await db.Set<CommercialExposureVersion>().CountAsync());
        var admin = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={now},RevokedBy={admin.Id},RevocationReason=N'Owned replay authorization test' WHERE UserId={actorId} AND AuthorityVersionId={cycle.AuthorityVersionId} AND RevokedAt IS NULL");
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(Issue)).Status);
        Assert.Equal(2, await db.Set<Policy>().CountAsync());
    }, stopAfterAccepted: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RealSqlCommercialIssuePersistsExactSourceExposureFinanceAndSelectedDocuments(bool employersSelected) => CommercialTermsScenario(async (db, oldCycle, acceptanceId, actorId, now) =>
    {
        await using (var held = await db.Database.BeginTransactionAsync())
        { await CommercialExposureSeed.SeedAsync(db); await PolicyTemplateSeed.SeedAsync(db); await held.CommitAsync(); }
        var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
        var clock = new RatingClock(); var staff = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Id == actorId);
        var actor = new ActorContext(staff.Id, staff.TeamId, null, new HashSet<string> { "senior-underwriter" });
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == oldCycle.Id);
        var accepted = await db.Set<QuoteAcceptance>().AsNoTracking().SingleAsync(x => x.Id == acceptanceId);
        var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteId);
        var input = new QuoteIssueInput(cycle.Id, accepted.RatingId, accepted.Id, accepted.TermsHash, accepted.AssuranceHash, "Issue fictional Commercial Combined cover");
        var service = new QuoteIssueService(factory, clock); var key = Guid.NewGuid().ToString();
        var issued = await service.IssueAsync(actor, quote.Id, quote.RowVersion, input, key, Guid.NewGuid());
        Assert.Equal(201, issued.Status); Assert.False(issued.Replayed);
        var replay = await service.IssueAsync(actor, quote.Id, quote.RowVersion, input, key, Guid.NewGuid());
        Assert.True(replay.Replayed); Assert.Equal(issued.Body, replay.Body);
        db.ChangeTracker.Clear();
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.SourceQuoteId == quote.Id);
        Assert.StartsWith("PL-CC-", policy.Reference); Assert.Equal(issued.ResourceId, policy.Id);
        var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.PolicyId == policy.Id);
        using var snapshot = JsonDocument.Parse(version.SnapshotJson);
        Assert.Empty(PolicySnapshotShape.Errors(snapshot.RootElement)); Assert.Equal("issued-commercial-1", snapshot.RootElement.GetProperty("snapshotFormat").GetString());
        Assert.False(snapshot.RootElement.GetProperty("risk").TryGetProperty("vehicles", out _));
        var exposure = await db.Set<CommercialExposureVersion>().AsNoTracking().SingleAsync(x => x.VersionId == version.Id);
        var decision = await db.Set<CommercialExposureIssueDecision>().AsNoTracking().SingleAsync(x => x.ExposureVersionId == exposure.Id);
        Assert.Equal(now, decision.AssessedAt); Assert.Equal(version.ContentHash, exposure.SourceHash);
        using var receipt = JsonDocument.Parse(issued.Body);
        Assert.Equal(decision.Id, receipt.RootElement.GetProperty("commercialExposureDecisionId").GetGuid());
        var locations = await db.Set<CommercialExposureLocationRecord>().AsNoTracking().Where(x => x.ExposureVersionId == exposure.Id).ToArrayAsync();
        Assert.Equal(snapshot.RootElement.GetProperty("risk").GetProperty("locations").GetArrayLength(), locations.Length);
        var documents = await db.Set<PolicyDocumentRequest>().AsNoTracking().Where(x => x.VersionId == version.Id).ToArrayAsync();
        Assert.Equal(CommercialDocumentSelection.Kinds(employersSelected).Order(), documents.Select(x => x.Kind).Order());
        foreach (var document in documents)
        {
            using var payload = JsonDocument.Parse(document.PayloadJson);
            Assert.Equal(version.Id, payload.RootElement.GetProperty("versionId").GetGuid());
            Assert.Equal("commercial-combined", payload.RootElement.GetProperty("snapshot").GetProperty("productCode").GetString());
            var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == document.WorkId);
            Assert.Equal(document.PayloadJson, work.Payload); Assert.Equal("requested", document.State);
            if (document.Kind == "policy-certificate") Assert.Contains("Employers' liability", payload.RootElement.GetProperty("template").GetProperty("title").GetString());
        }
        var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync(x => x.PolicyId == policy.Id);
        var journal = await db.Set<Journal>().AsNoTracking().SingleAsync(x => x.ObligationId == obligation.Id);
        var lines = await db.Set<JournalLine>().AsNoTracking().Where(x => x.JournalId == journal.Id).ToArrayAsync();
        Assert.NotNull(journal.PostedAt); Assert.Equal(lines.Sum(x => x.Debit), lines.Sum(x => x.Credit));
        Assert.Equal(policy.Id, (await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == quote.Id)).BoundPolicyId);
        var readback = await new PolicyReadService(factory, clock).ReadAtAsync(actor, policy.Id, cycle.StartsAt, now);
        Assert.Equal(version.Id, readback["versionId"]); Assert.Equal("active", readback["coverageState"]);
        Assert.Equal("commercial-combined", ((JsonElement)readback["snapshot"]).GetProperty("productCode").GetString());
        Assert.False(db.Database.HasPendingModelChanges());
    }, stopAfterAccepted: true, configureProposal: proposal =>
    {
        var selected = proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote.36ef01068295");
        selected!["value"] = employersSelected;
        if (!employersSelected)
        {
            proposal["risk"]!["liability"]!.AsObject().Remove("employersLimit");
            proposal["risk"]!["liability"]!.AsObject().Remove("employersReferenceNumber");
        }
    });
}
