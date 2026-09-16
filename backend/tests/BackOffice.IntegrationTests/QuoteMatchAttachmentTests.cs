using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteIntakeAttachmentAndAutomaticReviewRemainAtomicAndScoped()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            var candidate = await CreateFixture(db, "-MATCH-CANDIDATE");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteCaptureDemoSeed.SeedAsync(db); await MatchDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var client = await db.Set<ClientAccount>().SingleAsync(x => x.Id == fixture.Client);
            client.Address = "{\"line1\":\"1 Fictional Lane\",\"town\":\"Sheffield\",\"postcode\":\"S1 1AA\",\"country\":\"GB\"}";
            client.EntityType = "sole-trader"; await db.SaveChangesAsync();
            var actorId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(actorId, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options;
            var service = new QuoteService(new QuoteFactory(options), TimeProvider.System);
            var created = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "automatic-match", Guid.NewGuid());
            var intake = await db.Set<MatchSubmission>().AsNoTracking().SingleAsync(x => x.QuoteId == created.ResourceId);
            var review = await db.Set<MatchReview>().AsNoTracking().SingleAsync(x => x.SubmissionId == intake.Id);
            Assert.Equal(candidate.Client, review.CandidateClientId); Assert.Equal("pending", review.State);
            Assert.Equal(fixture.Client, intake.LinkedClientId); Assert.Equal(fixture.Agency, intake.AgencyId);
            Assert.Equal(MatchDemoSeed.RuleId, review.RuleVersionId);
            var read = await service.GetAsync(actor, created.ResourceId);
            Assert.Equal("quote-match-review-required", read.MatchingCode); Assert.Equal(review.Id, read.MatchReviewId);
            Assert.True((await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "automatic-match", Guid.NewGuid())).Replayed);
            Assert.Single(await db.Set<MatchSubmission>().Where(x => x.QuoteId == created.ResourceId).ToListAsync());
            var duplicate = await Assert.ThrowsAsync<QuoteOperationException>(() => service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "already-attached", Guid.NewGuid(), matchSubmissionId: intake.Id));
            Assert.Equal("quote-match-already-attached", duplicate.Code);
            Assert.Equal(1, await db.Set<Quote>().CountAsync());

            var manual = new MatchSubmission { Reference = "MI-EXISTING-ATTACHMENT", AgencyId = fixture.Agency, LinkedClientId = fixture.Client, LinkedRelationshipId = fixture.Relationship,
                IdentitySnapshot = JsonSerializer.Serialize(new ClientWrite(client.LegalName, client.EntityType, new("1 Fictional Lane", "Sheffield", "S1 1AA", "GB")), new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            var pinned = await db.Set<SettingVersion>().SingleAsync(x => x.Id == MatchDemoSeed.RuleId);
            var manualReview = new MatchReview { SubmissionId = manual.Id, CandidateClientId = candidate.Client, CandidateRelationshipId = candidate.Relationship,
                RuleVersionId = pinned.Id, RuleSnapshot = pinned.Values, Signals = review.Signals, State = "queried" };
            db.AddRange(manual, manualReview); await db.SaveChangesAsync();
            var foreign = await Assert.ThrowsAsync<QuoteOperationException>(() => service.CreateAsync(actor, candidate.Relationship, candidate.ProductVersion, null, "foreign-attachment", Guid.NewGuid(), matchSubmissionId: manual.Id));
            Assert.Contains(foreign.Code, new[] { "agency-unavailable", "quote-match-context-not-found" });
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_QuoteAttachRollback ON IdempotencyRecord AFTER INSERT AS BEGIN THROW 51085, 'Injected attachment rollback.', 1; END;");
            try { await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "attachment-rollback", Guid.NewGuid(), matchSubmissionId: manual.Id)); }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_QuoteAttachRollback"); }
            Assert.Null(await db.Set<MatchSubmission>().AsNoTracking().Where(x => x.Id == manual.Id).Select(x => x.QuoteId).SingleAsync());
            Assert.Equal(1, await db.Set<Quote>().CountAsync());
            var attached = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "attach-existing", Guid.NewGuid(), matchSubmissionId: manual.Id);
            Assert.Equal(attached.ResourceId, await db.Set<MatchSubmission>().AsNoTracking().Where(x => x.Id == manual.Id).Select(x => x.QuoteId).SingleAsync());
            Assert.True((await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "attach-existing", Guid.NewGuid(), matchSubmissionId: manual.Id)).Replayed);
            Assert.Equal("quote-match-review-required", (await service.GetAsync(actor, attached.ResourceId)).MatchingCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            Assert.Equal("agency-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, null, "attach-existing", Guid.NewGuid(), matchSubmissionId: manual.Id))).Code);
        });
    }
}
