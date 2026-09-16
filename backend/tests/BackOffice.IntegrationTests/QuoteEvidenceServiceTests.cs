using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteEvidenceCommandsScopeFilesRevisionsRollbackAndReauthorizeReplay()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(user, null, null, new HashSet<string> { "underwriter" });
            var factory = new QuoteFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options);
            var clock = new QuoteTime(); var quotes = new QuoteService(factory, clock); var service = new QuoteEvidenceService(factory, clock);
            const string proposal = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\",\"risk\":{\"business\":{\"description\":\"Fictional trade\"}}}";
            var created = await quotes.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "evidence-create", Guid.NewGuid());
            var other = await quotes.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "evidence-other", Guid.NewGuid());
            var saved = await quotes.GetAsync(actor, created.ResourceId); var otherSaved = await quotes.GetAsync(actor, other.ResourceId);
            var bytes = Encoding.UTF8.GetBytes("Fictional trade evidence\r\n");
            Assert.Equal("missing", (await service.ReadAsync(actor, created.ResourceId)).Requirements.Single().State);
            var upload = await service.UploadAsync(actor, created.ResourceId, saved.Quote.RowVersion, "trade.txt", "text/plain", bytes, "evidence-upload", Guid.NewGuid());
            Assert.Single(JsonDocument.Parse(upload.Body).RootElement.EnumerateObject());
            Assert.True((await service.UploadAsync(actor, created.ResourceId, saved.Quote.RowVersion, "trade.txt", "text/plain", bytes, "evidence-upload", Guid.NewGuid())).Replayed);
            Assert.Equal(1, await db.Set<QuoteEvidenceFile>().CountAsync());
            var files = await service.FilesAsync(actor, created.ResourceId); Assert.Single(files);
            Assert.DoesNotContain(Convert.ToBase64String(bytes), JsonSerializer.Serialize(files));
            Assert.Equal(bytes, (await new QuoteEvidenceService(factory, clock).DownloadAsync(actor, created.ResourceId, upload.ResourceId)).Content);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.DownloadAsync(actor, other.ResourceId, upload.ResourceId))).Status);
            using var document = JsonDocument.Parse(saved.Revision.ProposalJson);
            var fingerprint = QuoteEvidenceRules.Prepare(document.RootElement, "motor-trader-proof", null, saved.VersionPins).InputFingerprint;
            Task<BackOffice.Infrastructure.Platform.CommandOutcome> Attach(string key, Guid? file = null, Guid? revision = null, string? hash = null) =>
                service.AttachAsync(actor, created.ResourceId, saved.Quote.RowVersion, revision ?? saved.Revision.Id, "motor-trader-proof", null,
                    file ?? upload.ResourceId, hash ?? fingerprint, "Reviewed fictional evidence", key, Guid.NewGuid());
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Attach("wrong-revision", revision: otherSaved.Revision.Id))).Status);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Attach("wrong-input", hash: new string('a', 64)))).Status);
            var foreignFile = await service.UploadAsync(actor, other.ResourceId, otherSaved.Quote.RowVersion, "other.txt", "text/plain", bytes, "foreign-upload", Guid.NewGuid());
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Attach("foreign-file", file: foreignFile.ResourceId))).Status);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_EvidenceCommandTestFail ON QuoteActivity AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51089, 'Injected evidence failure.', 1; END;");
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => Attach("attach-proof"));
                Assert.Equal(0, await db.Set<QuoteCaptureEvidence>().CountAsync());
                await Assert.ThrowsAsync<DbUpdateException>(() => service.UploadAsync(actor, created.ResourceId, saved.Quote.RowVersion, "rollback.txt", "text/plain", bytes, "rollback-upload", Guid.NewGuid()));
                Assert.Equal(2, await db.Set<QuoteEvidenceFile>().CountAsync());
            }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_EvidenceCommandTestFail"); }
            var attached = await Attach("attach-proof");
            Assert.Equal(201, attached.Status); Assert.Equal(1, await db.Set<QuoteCaptureEvidence>().CountAsync());
            var attachedView = await service.ReadAsync(actor, created.ResourceId);
            Assert.Equal("current", attachedView.Requirements.Single().State); Assert.Equal(attached.ResourceId, attachedView.Requirements.Single().EvidenceId);
            Assert.Equal(attached.Etag, attachedView.Items.Single().Etag);
            Assert.DoesNotContain(Convert.ToBase64String(bytes), JsonSerializer.Serialize(attachedView));
            var unrelated = JsonNode.Parse(proposal)!; unrelated["risk"]!["materialFacts"] = "Unrelated note";
            await quotes.SaveAsync(actor, created.ResourceId, saved.Quote.RowVersion, unrelated.ToJsonString(), null, "edit-unrelated", Guid.NewGuid());
            Assert.Equal("current", (await service.ReadAsync(actor, created.ResourceId)).Requirements.Single().State);
            var latest = await quotes.GetAsync(actor, created.ResourceId);
            await quotes.SaveAsync(actor, created.ResourceId, latest.Quote.RowVersion, proposal.Replace("Fictional trade", "Changed trade"), null, "edit-proof-subject", Guid.NewGuid());
            var stale = await service.ReadAsync(actor, created.ResourceId);
            Assert.Equal("stale", stale.Items.Single().State); Assert.Equal("missing", stale.Requirements.Single().State);
            Assert.Equal("current", (await service.ReadAsync(actor, created.ResourceId, saved.Revision.Id)).Items.Single().State);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReadAsync(actor, created.ResourceId, otherSaved.Revision.Id))).Status);
            Assert.True((await Attach("attach-proof")).Replayed);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Attach("stale-proof"))).Status);
            var evidence = await db.Set<QuoteCaptureEvidence>().AsNoTracking().SingleAsync();
            var wrongVersion = evidence.RowVersion.ToArray(); wrongVersion[0] ^= 1;
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.WithdrawAsync(actor, created.ResourceId, evidence.Id,
                wrongVersion, "Old document", "withdraw-wrong-version", Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_EvidenceWithdrawalTestFail ON QuoteActivity AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51089, 'Injected withdrawal failure.', 1; END;");
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => service.WithdrawAsync(actor, created.ResourceId, evidence.Id, evidence.RowVersion, "Old document", "withdraw-proof", Guid.NewGuid()));
                Assert.Equal(0, await db.Set<QuoteEvidenceWithdrawal>().CountAsync());
            }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_EvidenceWithdrawalTestFail"); }
            var withdrawn = await service.WithdrawAsync(actor, created.ResourceId, evidence.Id, evidence.RowVersion, "Old document", "withdraw-proof", Guid.NewGuid());
            Assert.Equal(evidence.Id, withdrawn.ResourceId);
            Assert.True((await service.WithdrawAsync(actor, created.ResourceId, evidence.Id, evidence.RowVersion, "Old document", "withdraw-proof", Guid.NewGuid())).Replayed);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.WithdrawAsync(actor, created.ResourceId, evidence.Id,
                evidence.RowVersion, "Repeated withdrawal", "withdraw-again", Guid.NewGuid()))).Status);
            Assert.Equal(bytes, (await service.DownloadAsync(actor, created.ResourceId, upload.ResourceId)).Content);
            Assert.Equal("withdrawn", (await service.ReadAsync(actor, created.ResourceId)).Items.Single().State);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            await Assert.ThrowsAsync<QuoteOperationException>(() => Attach("attach-proof"));
            await Assert.ThrowsAsync<QuoteOperationException>(() => service.WithdrawAsync(actor, created.ResourceId, evidence.Id, evidence.RowVersion, "Old document", "withdraw-proof", Guid.NewGuid()));
            await Assert.ThrowsAsync<QuoteOperationException>(() => service.UploadAsync(actor, created.ResourceId, saved.Quote.RowVersion, "trade.txt", "text/plain", bytes, "evidence-upload", Guid.NewGuid()));
        });
    }
}
