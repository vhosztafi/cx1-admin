using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
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
    private sealed class StatementClock(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    [Fact]
    public async Task RealSqlFinanceStatementSealsPostedSourceReconciliationBytesAndCurrentScope()
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
            var date = journal.PostingDate!.Value;
            var actor = await FinanceLedgerActor(db);
            var initialCutoff = f.Clock.GetUtcNow().AddSeconds(2);
            var clock = new StatementClock(initialCutoff);
            var service = new FinanceStatementService(f.Factory, new SqlCommandBoundary(f.Factory, clock), clock);
            var key = Guid.NewGuid().ToString("N");
            var end = date.AddDays(32);
            var generated = await service.GenerateAsync(actor, obligation.AgencyId, date, end, key, Guid.NewGuid());
            Assert.Equal(201, generated.Status);
            var replay = await service.GenerateAsync(actor, obligation.AgencyId, date, end, key, Guid.NewGuid());
            Assert.True(replay.Replayed);
            Assert.Equal(generated.ResourceId, replay.ResourceId);
            var detail = await service.DetailAsync(actor, generated.ResourceId);
            var source = Assert.Single(detail.Sources);
            Assert.Equal("insurance", source.SourceKind);
            Assert.Equal(obligation.AgencyTermsVersionId, source.AgencyTermsVersionId);
            Assert.Equal(journal.PostedAt, source.PostedAt);
            Assert.Equal(date, source.PostingDate);
            var due = FinanceLedgerMath.LegacyPostingDate(journal.PostedAt!.Value).AddDays(30);
            Assert.Equal(due.ToString("yyyy-MM-dd"), source.DueDate);
            Assert.True(source.PastDueAtEnd);
            Assert.True(source.AgeDaysAtEnd > 0);
            Assert.Equal("0.00", detail.Opening);
            Assert.Equal(obligation.InvoiceDue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), detail.Closing);
            var line = Assert.Single(detail.Rows);
            Assert.Equal(detail.Closing, line.RunningBalance);
            Assert.Equal(source.DueDate, line.DueDate);
            Assert.True(line.Overdue);
            var empty = await service.GenerateAsync(actor, obligation.AgencyId, date.AddDays(1), end,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var openingOnly = await service.DetailAsync(actor, empty.ResourceId);
            Assert.Empty(openingOnly.Rows);
            Assert.Equal(detail.Closing, openingOnly.Opening);
            Assert.Equal(detail.Closing, openingOnly.Closing);
            Assert.True(Assert.Single(openingOnly.Sources).PastDueAtEnd);
            Assert.Equal(2, (await service.ListAsync(actor, obligation.AgencyId)).Total);
            var download = await service.DownloadAsync(actor, generated.ResourceId);
            Assert.Equal(detail.ContentHash, Convert.ToHexString(SHA256.HashData(download.Bytes)));
            Assert.Contains(detail.Closing, Encoding.UTF8.GetString(download.Bytes));
            var saved = await db.Set<FinanceStatementVersion>().AsNoTracking().SingleAsync(x => x.Id == generated.ResourceId);
            Assert.Equal(detail.SourceHash, Convert.ToHexString(saved.SourceHash));
            Assert.Equal(detail.ContentHash, Convert.ToHexString(saved.ContentHash));
            Assert.Equal(download.Bytes, saved.ContentBytes);
            var termsRequester = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "agency-admin@cover.example");
            var termsReviewer = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "system-admin@cover.example");
            var currentTerms = await db.Set<AgencyTermsVersion>().AsNoTracking()
                .SingleAsync(x => x.Id == obligation.AgencyTermsVersionId);
            var futureDate = date.AddDays(1);
            var changedTerms = JsonNode.Parse(currentTerms.Snapshot)!;
            changedTerms["effectiveFrom"] = futureDate.ToString("yyyy-MM-dd");
            changedTerms["commercialTerms"]!["effectiveFrom"] = futureDate.ToString("yyyy-MM-dd");
            changedTerms["paymentTermsDays"] = 7;
            foreach (var product in changedTerms["products"]!.AsArray())
                product!["effectiveFrom"] = futureDate.ToString("yyyy-MM-dd");
            var baseVersion = await db.Set<Agency>().AsNoTracking().Where(x => x.Id == obligation.AgencyId)
                .Select(x => x.RowVersion).SingleAsync();
            var termsRequest = new AgencyTermsRequest
            {
                AgencyId = obligation.AgencyId, BaseVersion = baseVersion, EffectiveFrom = futureDate,
                ProposedSnapshot = changedTerms.ToJsonString(), ProposedInputFingerprint = new string('b', 64),
                RequestedBy = termsRequester.Id, CreatedBy = termsRequester.Id,
                RequestReason = "Fictional later payment terms"
            };
            db.Add(termsRequest); await db.SaveChangesAsync();
            var reviewedAt = DateTimeOffset.UtcNow.AddSeconds(1);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyTermsRequest SET State=N'applied',DecisionBy={termsReviewer.Id},DecisionReason=N'Fictional independent terms review',DecidedAt={reviewedAt} WHERE Id={termsRequest.Id}");
            db.Add(new AgencyTermsVersion
            {
                AgencyId = obligation.AgencyId, Version = 2, EffectiveFrom = futureDate,
                ApprovedTermsRequestId = termsRequest.Id, CreatedBy = termsReviewer.Id, CreatedAt = reviewedAt.AddSeconds(1),
                Snapshot = changedTerms.ToJsonString()
            });
            await db.SaveChangesAsync();
            Assert.Equal(due.ToString("yyyy-MM-dd"), Assert.Single((await service.DetailAsync(actor, saved.Id)).Sources).DueDate);
            var later = initialCutoff.AddSeconds(1);
            var laterClock = new StatementClock(later);
            var receiptService = new FinanceReceiptService(f.Factory,
                new SqlCommandBoundary(f.Factory, laterClock), laterClock);
            var recorded = await receiptService.RecordAsync(actor, obligation.AgencyId, "1.00", "GBP", date,
                "Fictional late cash receipt", "manual", Guid.NewGuid(), "agency", obligation.AgencyId,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var receipt = await receiptService.DetailAsync(actor, recorded.ResourceId);
            await receiptService.AllocateAsync(actor, receipt.Id, receipt.AssignmentId,
                [new ReceiptAllocationInput(obligation.Id, "1.00")], Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var frozen = await service.DetailAsync(actor, saved.Id);
            Assert.Equal(detail.SourceHash, frozen.SourceHash);
            Assert.Equal(download.Bytes, (await service.DownloadAsync(actor, saved.Id)).Bytes);
            var successorClock = new StatementClock(later.AddSeconds(1));
            var laterService = new FinanceStatementService(f.Factory,
                new SqlCommandBoundary(f.Factory, successorClock), successorClock);
            var successor = await laterService.GenerateAsync(actor, obligation.AgencyId, date, end,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var laterDetail = await laterService.DetailAsync(actor, successor.ResourceId);
            Assert.Equal(2, laterDetail.Version);
            Assert.Equal(3, laterDetail.Sources.Count);
            Assert.Contains(laterDetail.Sources, x => x.SourceKind == "receipt" && x.Delta == "0.00");
            Assert.Contains(laterDetail.Sources, x => x.SourceKind == "receipt-application" && x.Delta == "-1.00");
            Assert.NotEqual(detail.SourceHash, laterDetail.SourceHash);
            Assert.Equal(FinanceLedgerMath.Money(obligation.InvoiceDue - 1m), laterDetail.Closing);
            Assert.Equal(due.ToString("yyyy-MM-dd"), laterDetail.Sources.Single(x => x.SourceKind == "insurance").DueDate);
            var parallel = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => laterService.GenerateAsync(actor,
                obligation.AgencyId, date, end, Guid.NewGuid().ToString("N"), Guid.NewGuid())));
            var parallelVersions = await Task.WhenAll(parallel.Select(x => laterService.DetailAsync(actor, x.ResourceId)));
            Assert.Equal(new[] { 3, 4 }, parallelVersions.Select(x => x.Version).OrderBy(x => x).ToArray());
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FinanceStatementVersion SET Closing='999.00' WHERE Id={saved.Id}"));
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DownloadAsync(f.Underwriter, saved.Id))).Status);
            var brokerRole = await db.Set<Role>().AsNoTracking().SingleAsync(x => x.Code == "broker-readonly");
            var broker = new StaffUser { AgencyId = obligation.AgencyId, State = "invited",
                Email = "statement-" + Guid.NewGuid().ToString("N") + "@example.invalid",
                DisplayName = "Fictional statement reader" };
            broker.NormalizedEmail = broker.Email.ToUpperInvariant();
            db.Add(broker); await db.SaveChangesAsync();
            db.Add(new UserRole { UserId = broker.Id, RoleId = brokerRole.Id }); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={broker.Id}");
            var brokerActor = new ActorContext(broker.Id, null, obligation.AgencyId,
                new HashSet<string>(StringComparer.Ordinal) { "broker-readonly" });
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.ListAsync(brokerActor with { AgencyId = Guid.NewGuid() }, obligation.AgencyId))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DownloadAsync(brokerActor, saved.Id))).Status);
            var requester = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "agency-admin@cover.example");
            var decider = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "system-admin@cover.example");
            var requestActor = new ActorContext(requester.Id, requester.TeamId, null,
                new HashSet<string>(StringComparer.Ordinal) { "agency-admin" });
            var decisionActor = new ActorContext(decider.Id, decider.TeamId, null,
                new HashSet<string>(StringComparer.Ordinal) { "system-admin" });
            var permissions = new AgencyPermissionService(new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var agencyVersion = await db.Set<Agency>().AsNoTracking().Where(x => x.Id == obligation.AgencyId)
                .Select(x => x.RowVersion).SingleAsync();
            var grantRequest = await permissions.Request(requestActor, obligation.AgencyId, Guid.NewGuid().ToString("N"),
                agencyVersion, AgencyPermissionRules.StatementDownload, "Fictional statement sharing approval");
            db.ChangeTracker.Clear();
            var requestVersion = await db.Set<AgencyPermissionRequest>().AsNoTracking()
                .Where(x => x.Id == grantRequest.ResourceId).Select(x => x.RowVersion).SingleAsync();
            await permissions.Decide(decisionActor, obligation.AgencyId, grantRequest.ResourceId,
                Guid.NewGuid().ToString("N"), requestVersion, "approve", "Fictional independent approval");
            db.ChangeTracker.Clear();
            Assert.Equal(download.Bytes, (await service.DownloadAsync(brokerActor, saved.Id)).Bytes);
            var grant = await db.Set<AgencyPermissionGrant>().AsNoTracking()
                .SingleAsync(x => x.RequestId == grantRequest.ResourceId);
            await permissions.Revoke(decisionActor, obligation.AgencyId, grant.Id, Guid.NewGuid().ToString("N"),
                grant.RowVersion, "Fictional statement grant withdrawn");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.DownloadAsync(brokerActor, saved.Id))).Status);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
