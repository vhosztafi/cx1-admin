using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private sealed record DecisionFixture(RatingFactory Factory, RatingClock Clock, ActorContext Servicing, ActorContext Underwriter, Guid QuoteId, Guid CycleId);
    private static async Task<DecisionFixture> ReadyUnderwriting(BackOfficeDbContext db, string password, Action<JsonNode>? amend = null, string productCode = "motor-trade-road-risks")
    {
        await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
        await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteLookupDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
        var f = await Fixture(db, productCode: productCode); var clock = new RatingClock();
        var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
        var quotes = new QuoteService(factory, clock);
        using var stream = typeof(UnderwritingSeed).Assembly.GetManifestResourceStream("UnderwritingDemo.Definitions")!;
        using var examples = JsonDocument.Parse(stream); var draft = JsonNode.Parse(examples.RootElement.GetProperty("proposals").GetProperty(productCode).GetRawText())!;
        amend?.Invoke(draft); var proposal = JsonSerializer.SerializeToElement(draft);
        var relationship = await db.Set<Quote>().Where(x => x.Id == f.Quote).Select(x => x.RelationshipId).SingleAsync();
        var created = await quotes.CreateAsync(f.Actor, relationship, f.ProductVersion, proposal.GetRawText(), Guid.NewGuid().ToString(), Guid.NewGuid());
        var saved = await quotes.GetAsync(f.Actor, created.ResourceId);
        var lookupService = new QuoteLookupService(factory, clock); var worker = new QuoteLookupWorker(factory, clock); var leases = new SqlJobLeases(factory, clock);
        foreach (var vehicle in proposal.GetProperty("risk").GetProperty("vehicles").EnumerateArray())
        {
            var request = await lookupService.RequestAsync(f.Actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.Id, new("vehicle", "vehicle", vehicle.GetProperty("id").GetGuid()), "no-match", Guid.NewGuid().ToString(), Guid.NewGuid());
            var lookup = await lookupService.GetAsync(f.Actor, created.ResourceId, request.ResourceId);
            var lease = (await leases.ClaimWorkAsync(QuoteLookupService.WorkKind, lookup.WorkId))!;
            Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            await lookupService.SelectAsync(f.Actor, created.ResourceId, lookup.Id, saved.Quote.RowVersion, saved.Revision.Id, lookup.InputFingerprint, null, "Fictional manual check", Guid.NewGuid().ToString(), Guid.NewGuid());
            saved = await quotes.GetAsync(f.Actor, created.ResourceId);
        }
        var requested = await new QuoteRatingService(factory, clock).RateAsync(f.Actor, created.ResourceId, saved.Revision.Id, saved.Quote.RowVersion, "Fictional review fixture", Guid.NewGuid().ToString(), Guid.NewGuid());
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == requested.ResourceId);
        var ratingWorker = new QuoteRatingWorker(factory, clock); var ratingLease = (await leases.ClaimWorkAsync(QuoteRatingService.WorkKind, cycle.WorkId))!;
        Assert.True(await ratingWorker.ApplyAsync(ratingLease, await ratingWorker.ExecuteProviderAsync(ratingLease)));
        var uw = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "underwriter@cover.example");
        return new(factory, clock, f.Actor, new(uw.Id, uw.TeamId, null, new HashSet<string> { "underwriter" }), created.ResourceId, cycle.Id);
    }

    [Fact]
    public async Task RealSqlQuoteEvidenceReviewRequiresCurrentIndependentDecisionAndWithdrawnProofInvalidatesAssurance()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await ReadyUnderwriting(db, password); var service = new UnderwritingEvidenceService(f.Factory, f.Clock);
            var quotes = new QuoteService(f.Factory, f.Clock);
            var before = await quotes.GetAsync(f.Servicing, f.QuoteId);
            var required = (await service.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "motor-trader-proof");
            await Assert.ThrowsAsync<QuoteInputException>(() => new QuoteEvidenceService(f.Factory, f.Clock).UploadAsync(f.Servicing, f.QuoteId, before.Quote.RowVersion, "proof.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional proof"), Guid.NewGuid().ToString(), Guid.NewGuid()));
            var upload = await service.UploadAsync(f.Servicing, f.QuoteId, before.Quote.RowVersion, "proof.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional proof"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var current = await quotes.GetAsync(f.Servicing, f.QuoteId);
            var storedFile = await db.Set<QuoteEvidenceFile>().AsNoTracking().SingleAsync(x => x.Id == upload.ResourceId);
            var foreignQuote = await db.Set<Quote>().Where(x => x.Id != f.QuoteId).Select(x => x.Id).SingleAsync();
            var foreignFile = new QuoteEvidenceFile { QuoteId = foreignQuote, FileName = storedFile.FileName, ContentType = storedFile.ContentType, Content = storedFile.Content,
                Sha256 = storedFile.Sha256, ByteLength = storedFile.ByteLength, CreatedBy = f.Servicing.UserId };
            db.Add(foreignFile); await db.SaveChangesAsync();
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, current.Quote.RowVersion,
                foreignFile.Id, required.Code, null, null, null, required.InputFingerprint, "Foreign file must not attach", Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            var attached = await service.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, current.Quote.RowVersion, upload.ResourceId, required.Code, null, null, null,
                required.InputFingerprint, "Actual supplied evidence", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.False((await service.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == required.Code).Satisfied);
            current = await quotes.GetAsync(f.Servicing, f.QuoteId);
            var row = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReviewAsync(f.Servicing, f.QuoteId, f.CycleId, row.Id, current.Quote.RowVersion, row.RowVersion, "accepted", required.InputFingerprint, "No review authority", Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, current.Quote.RowVersion, row.RowVersion, "accepted", new string('b', 64), "Wrong proof", Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            var key = Guid.NewGuid().ToString();
            await service.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, current.Quote.RowVersion, row.RowVersion, "accepted", required.InputFingerprint, "Reviewed actual proof", key, Guid.NewGuid());
            Assert.True((await service.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == required.Code).Satisfied);
            Assert.True((await service.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, current.Quote.RowVersion, row.RowVersion, "accepted", required.InputFingerprint, "Reviewed actual proof", key, Guid.NewGuid())).Replayed);
            var assessment = new QuoteUnderwritingReadModel(f.Factory, f.Clock); var acceptedHash = (await assessment.AssessmentAsync(f.Servicing, f.QuoteId))["assuranceHash"];
            var withdrawnQuote = await quotes.GetAsync(f.Servicing, f.QuoteId); row = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == row.Id);
            await service.WithdrawAsync(f.Servicing, f.QuoteId, f.CycleId, row.Id, withdrawnQuote.Quote.RowVersion, row.RowVersion, "Evidence corrected", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.False((await service.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == required.Code).Satisfied);
            Assert.NotEqual(acceptedHash, (await assessment.AssessmentAsync(f.Servicing, f.QuoteId))["assuranceHash"]);
            Assert.Equal(2, await db.Set<UnderwritingEvidenceEvent>().CountAsync(x => x.AssociationId == row.Id));
            var after = await quotes.GetAsync(f.Servicing, f.QuoteId); Assert.Equal(before.Revision.ProposalJson, after.Revision.ProposalJson);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Authority withdrawn' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, current.Quote.RowVersion, row.RowVersion, "accepted", required.InputFingerprint, "Reviewed actual proof", key, Guid.NewGuid()))).Status);
            var progressed = await quotes.GetAsync(f.Servicing, f.QuoteId);
            await new QuoteUnderwritingLifecycle(f.Factory, f.Clock).ReturnToDraftAsync(f.Servicing, f.QuoteId, f.CycleId, progressed.Quote.RowVersion, "Revise risk explicitly", Guid.NewGuid().ToString(), Guid.NewGuid());
            var draft = await quotes.GetAsync(f.Servicing, f.QuoteId);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, draft.Quote.RowVersion,
                upload.ResourceId, required.Code, null, null, null, required.InputFingerprint, "Old cycle cannot acquire proof", Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
        });
    }
}
