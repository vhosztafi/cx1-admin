using System.Security.Cryptography;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlFinanceBordereauPinsPostedSourceAndSealsSuccessorValidationBytes()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var journal = await db.Set<Journal>().AsNoTracking().SingleAsync();
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var providerId = obligation.ProviderId;
            var actor = await FinanceLedgerActor(db);
            var clock = new StatementClock(f.Clock.GetUtcNow().AddSeconds(2));
            var service = new FinanceBordereauService(f.Factory, new SqlCommandBoundary(f.Factory, clock), clock);
            var generated = await service.GenerateAsync(actor, providerId, journal.AccountingPeriodId!.Value,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var first = await service.DetailAsync(actor, generated.ResourceId);
            var member = Assert.Single(first.Members);
            Assert.Equal(journal.Id, member.SourceJournalId);
            Assert.Equal(obligation.Premium.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), member.Premium);
            Assert.Equal(obligation.Commission.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), member.Commission);
            Assert.Equal(journal.PostingDate, member.PostingDate);
            Assert.Equal("unvalidated", first.State);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DownloadAsync(actor, first.BatchId, first.Id))).Status);
            Assert.Equal(1, (await service.ListAsync(actor, providerId)).Total);
            var validated = await service.ValidateAsync(actor, first.BatchId, first.Id,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var valid = await service.DetailAsync(actor, first.BatchId);
            Assert.Equal("valid", valid.State);
            Assert.Empty(valid.Validation);
            Assert.Equal(first.SourceHash, valid.SourceHash);
            var file = await service.DownloadAsync(actor, first.BatchId, valid.Id);
            Assert.Equal(valid.ContentHash, Convert.ToHexString(SHA256.HashData(file.Bytes)));
            var savedBytes = file.Bytes.ToArray();
            var correction = await service.CorrectAsync(actor, first.BatchId, valid.Id, journal.Id,
                "=SUM(A1:A2)", null, null, "Fictional provider mapping correction", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var edited = await service.DetailAsync(actor, first.BatchId);
            Assert.Equal(valid.Id, edited.ParentVersionId);
            Assert.Equal("unvalidated", edited.State);
            Assert.Equal(first.SourceHash, edited.SourceHash);
            Assert.NotEqual(valid.MembersHash, edited.MembersHash);
            Assert.Equal(member.Premium, Assert.Single(edited.Members).Premium);
            Assert.Equal(actor.UserId, Assert.Single(edited.Members).CorrectionActorId);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DownloadAsync(actor, first.BatchId, valid.Id))).Status);
            var stillSaved = await db.Set<FinanceBordereauVersion>().AsNoTracking().SingleAsync(x => x.Id == valid.Id);
            Assert.Equal(savedBytes, stillSaved.ContentBytes);
            Assert.Equal(SHA256.HashData(savedBytes), stillSaved.ContentHash);
            var productVersion = await db.Set<ProductVersion>().AsNoTracking().SingleAsync(x => x.Id == member.ProductVersionId);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Product SET Code=N'LATER-PRODUCT' WHERE Id={productVersion.ProductId}");
            Assert.Equal(member.ProviderProductCode, Assert.Single((await service.DetailAsync(actor, first.BatchId)).Members).ProviderProductCode);
            var regenerated = await service.GenerateAsync(actor, providerId, journal.AccountingPeriodId!.Value,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var drifted = await service.DetailAsync(actor, regenerated.ResourceId);
            Assert.Equal("LATER-PRODUCT", Assert.Single(drifted.Members).ProviderProductCode);
            Assert.NotEqual(first.SourceHash, drifted.SourceHash);
            await service.ValidateAsync(actor, first.BatchId, edited.Id, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var corrected = await service.DetailAsync(actor, first.BatchId);
            Assert.Equal("valid", corrected.State);
            var csv = System.Text.Encoding.UTF8.GetString((await service.DownloadAsync(actor, first.BatchId, corrected.Id)).Bytes);
            Assert.Contains("'=SUM(A1:A2)", csv);
            Assert.Contains(member.NetDue, csv);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DetailAsync(f.Underwriter, first.BatchId))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DownloadAsync(f.Underwriter, first.BatchId, corrected.Id))).Status);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FinanceBordereauVersion SET State=N'invalid' WHERE Id={valid.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FinanceBordereauMember SET Premium=999 WHERE VersionId={valid.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FinanceBordereauBatch SET CurrentVersionId={valid.Id} WHERE Id={first.BatchId}"));
            await using (var injection = await db.Database.BeginTransactionAsync())
            {
                var forged = new FinanceBordereauVersion
                {
                    BatchId = first.BatchId, ParentVersionId = corrected.Id, Number = corrected.Number + 1,
                    SourceCutoff = corrected.SourceCutoff, SourceHash = Convert.FromHexString(corrected.SourceHash),
                    MembersHash = Convert.FromHexString(corrected.MembersHash), SchemaVersion = corrected.SchemaVersion,
                    CreatedAt = clock.GetUtcNow(), CreatedBy = actor.UserId
                };
                db.Add(forged);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                db.Add(new FinanceBordereauMember
                {
                    VersionId = forged.Id, SourceJournalId = member.SourceJournalId, PolicyId = member.PolicyId,
                    TransactionId = member.TransactionId, AgencyId = member.AgencyId,
                    ProductVersionId = member.ProductVersionId, AgencyTermsVersionId = member.AgencyTermsVersionId,
                    PostingDate = member.PostingDate, PostedAt = member.PostedAt,
                    Premium = obligation.Premium + 1m, Tax = obligation.Tax, Fee = obligation.Fee,
                    Commission = obligation.Commission, NetDue = obligation.NetDue,
                    PolicyReference = member.PolicyReference, ProviderProductCode = member.ProviderProductCode,
                    AgencyReference = member.AgencyReference, CreatedAt = clock.GetUtcNow(), CreatedBy = actor.UserId
                });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                db.ChangeTracker.Clear();
                await injection.RollbackAsync();
            }
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinanceBordereauInvalidRowsAndAuditedExclusionRequireNewValidation()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var journal = await db.Set<Journal>().AsNoTracking().SingleAsync();
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var actor = await FinanceLedgerActor(db);
            var clock = new StatementClock(f.Clock.GetUtcNow().AddSeconds(2));
            var service = new FinanceBordereauService(f.Factory, new SqlCommandBoundary(f.Factory, clock), clock);
            var generated = await service.GenerateAsync(actor, obligation.ProviderId, journal.AccountingPeriodId!.Value,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var first = await service.DetailAsync(actor, generated.ResourceId);
            await service.CorrectAsync(actor, first.BatchId, first.Id, journal.Id, "", "", "",
                "Fictional deliberate missing mappings", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var blank = await service.DetailAsync(actor, first.BatchId);
            await service.ValidateAsync(actor, first.BatchId, blank.Id, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var invalid = await service.DetailAsync(actor, first.BatchId);
            Assert.Equal("invalid", invalid.State);
            Assert.Equal(3, invalid.Validation.Count);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DownloadAsync(actor, first.BatchId, invalid.Id))).Status);
            await service.ExcludeAsync(actor, first.BatchId, invalid.Id, journal.Id,
                "Fictional source rejected by provider", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var excluded = await service.DetailAsync(actor, first.BatchId);
            Assert.Equal(actor.UserId, Assert.Single(excluded.Members).ExclusionActorId);
            Assert.Equal("unvalidated", excluded.State);
            await service.ValidateAsync(actor, first.BatchId, excluded.Id, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var empty = await service.DetailAsync(actor, first.BatchId);
            Assert.Equal("invalid", empty.State);
            Assert.Contains(empty.Validation, x => x.Code == "no-included-rows");
            Assert.Null(empty.ContentHash);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.ValidateAsync(actor, first.BatchId, excluded.Id, Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
        });
    }
}
