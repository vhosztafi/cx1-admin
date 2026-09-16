using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteEvidenceStorageBindsFilesRevisionsTargetsAndImmutableWithdrawal()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db); var quote = await InsertQuote(db, fixture); var other = await InsertQuote(db, fixture);
            var driver = Guid.NewGuid(); var revision = Revision(quote, fixture);
            revision.ProposalJson = JsonSerializer.Serialize(new { schemaVersion = "1.0", productCode = "motor-trade-road-risks", risk = new { drivers = new[] { new { id = driver } } } });
            var foreign = Revision(other, fixture); db.AddRange(revision, foreign); await db.SaveChangesAsync();
            var content = Encoding.UTF8.GetBytes("Fictional driver evidence");
            QuoteEvidenceFile File(Guid owner) => new() { QuoteId = owner, CreatedBy = fixture.Actor, FileName = "proof.txt", ContentType = "text/plain",
                Content = content.ToArray(), ByteLength = content.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) };
            var file = File(quote.Id); var otherFile = File(other.Id); db.AddRange(file, otherFile); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var invalidFile = File(quote.Id); invalidFile.Sha256 = new string('a', 64); db.Add(invalidFile);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            invalidFile = File(quote.Id); invalidFile.ByteLength++; db.Add(invalidFile);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            QuoteCaptureEvidence Evidence() => new() { QuoteId = quote.Id, RevisionId = revision.Id, FileId = file.Id,
                RequirementCode = "photocard-both-sides", RiskItemId = driver, InputFingerprint = new string('b', 64),
                Reason = "Fictional reviewed proof", ActorId = fixture.Actor, CreatedBy = fixture.Actor };
            foreach (var change in new Action<QuoteCaptureEvidence>[] {
                e => e.FileId = otherFile.Id, e => e.RevisionId = foreign.Id, e => e.RiskItemId = Guid.NewGuid(),
                e => e.RiskItemId = null, e => e.RequirementCode = "motor-trader-proof", e => e.InputFingerprint = "invalid", e => e.Reason = " " })
            {
                var invalid = Evidence(); change(invalid); db.Add(invalid);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            }
            var evidence = Evidence(); db.Add(evidence); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            Assert.Equal(8, evidence.RowVersion.Length);
            QuoteEvidenceWithdrawal Withdrawal() => new() { QuoteId = quote.Id, EvidenceId = evidence.Id, Reason = "Replaced fictional proof", ActorId = fixture.Actor, CreatedBy = fixture.Actor };
            var wrong = Withdrawal(); wrong.QuoteId = other.Id; db.Add(wrong);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            wrong = Withdrawal(); wrong.CreatedAt = evidence.CreatedAt.AddMinutes(-1); db.Add(wrong);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var withdrawal = Withdrawal(); db.Add(withdrawal); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            db.Add(Withdrawal()); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteEvidenceFile SET FileName=N'changed.txt' WHERE Id={file.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteCaptureEvidence SET Reason=N'changed' WHERE Id={evidence.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM QuoteEvidenceWithdrawal WHERE Id={withdrawal.Id}"));
            var restarted = new QuoteFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options);
            await using var read = restarted.CreateDbContext();
            var retained = await read.Set<QuoteEvidenceFile>().SingleAsync(x => x.Id == file.Id);
            Assert.Equal(content, retained.Content); Assert.Equal(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(retained.Content)));
            Assert.Equal(1, await read.Set<QuoteCaptureEvidence>().CountAsync()); Assert.Equal(1, await read.Set<QuoteEvidenceWithdrawal>().CountAsync());
        });
    }
}
