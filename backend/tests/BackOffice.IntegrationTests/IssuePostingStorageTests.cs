using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<(IssueFinancialObligation Obligation, Journal Journal, List<JournalLine> Lines)> StoredIssuePosting(BackOfficeDbContext db, PolicyFixture f, bool omitLast)
    {
        var rating = await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.Id == f.Transaction.RatingId);
        var terms = await db.Set<AgencyTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == f.Cycle.AgencyTermsVersionId);
        using var json = JsonDocument.Parse(terms.Snapshot); var root = json.RootElement;
        var share = root.GetProperty("commercialTerms").GetProperty("feeSharing").GetString() == "agreed-split" ? root.GetProperty("commercialTerms").GetProperty("feeShareBasisPoints").GetInt32() : 0;
        var posting = IssuePostingRules.Calculate(new(rating.TermPremium, rating.Tax, rating.Fee, rating.BrokerCommission, share,
            root.GetProperty("settlement").GetProperty("premiumCollection").GetString()!, root.GetProperty("settlement").GetProperty("commissionSettlement").GetString()!, f.Term.StartsAt, f.Term.EndsAt));
        var provider = await db.Set<BinderVersion>().Where(x => x.Id == f.Cycle.BinderVersionId).Select(x => x.ProviderId).SingleAsync();
        var obligation = new IssueFinancialObligation { PolicyId = f.Policy.Id, TermId = f.Term.Id, TransactionId = f.Transaction.Id,
            AgencyId = f.Policy.AgencyId, ClientId = f.Policy.ClientId, RelationshipId = f.Policy.RelationshipId, ProviderId = provider, AgencyTermsVersionId = terms.Id,
            DebtorKind = posting.DebtorKind, DebtorAgencyId = posting.DebtorKind == "agency" ? f.Policy.AgencyId : null, DebtorRelationshipId = posting.DebtorKind == "relationship" ? f.Policy.RelationshipId : null,
            Settlement = posting.EffectiveSettlement, Premium = rating.TermPremium, Tax = rating.Tax, Fee = rating.Fee, Commission = rating.BrokerCommission, FeeShare = posting.FeeShare,
            GrossDue = posting.GrossDue, InvoiceDue = posting.InvoiceDue, NetDue = posting.NetDue, BrokerPayable = posting.BrokerPayable, TermsSnapshotJson = terms.Snapshot,
            CreatedAt = f.Source.Clock.Current, CreatedBy = f.Source.Underwriter.UserId };
        db.Add(obligation); await db.SaveChangesAsync();
        var components = posting.Components.Select(x => new IssueFinancialComponent { ObligationId = obligation.Id, TransactionId = f.Transaction.Id, Code = x.Code, Amount = x.Amount,
            CoverageStartsAt = f.Term.StartsAt, CoverageEndsAt = f.Term.EndsAt, CreatedAt = f.Source.Clock.Current, CreatedBy = f.Source.Underwriter.UserId }).ToArray();
        db.AddRange(components); await db.SaveChangesAsync();
        var journal = new Journal { TransactionId = f.Transaction.Id, ObligationId = obligation.Id, CreatedAt = f.Source.Clock.Current, CreatedBy = f.Source.Underwriter.UserId }; db.Add(journal); await db.SaveChangesAsync();
        var lines = posting.Lines.Select(x => new JournalLine { JournalId = journal.Id, TransactionId = f.Transaction.Id, SourceComponentId = components.Single(c => c.Code == x.ComponentCode).Id,
            ComponentCode = x.ComponentCode, AccountCode = x.AccountCode, Debit = x.Debit, Credit = x.Credit, CoverageStartsAt = f.Term.StartsAt, CoverageEndsAt = f.Term.EndsAt,
            PartyKind = x.AccountCode == "fee-income" ? "internal" : x.AccountCode == "insurer-payable" ? "provider" : x.AccountCode == "relationship-receivable" ? "relationship" : "agency",
            PartyId = x.AccountCode == "fee-income" ? null : x.AccountCode == "insurer-payable" ? provider : x.AccountCode == "relationship-receivable" ? f.Policy.RelationshipId : f.Policy.AgencyId,
            CreatedAt = f.Source.Clock.Current, CreatedBy = f.Source.Underwriter.UserId }).ToList();
        db.AddRange(omitLast ? lines.SkipLast(1) : lines); await db.SaveChangesAsync(); return (obligation, journal, lines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSqlIssuePostingRequiresCompleteBalanceAndRejectsLateMutation(bool omitLast)
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await StoredPolicy(db, password); var posting = await StoredIssuePosting(db, f, omitLast); var now = f.Source.Clock.Current;
            if (omitLast)
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt={now} WHERE Id={posting.Journal.Id}")); Assert.Equal(51192, error.Number);
                Assert.Null(await db.Set<Journal>().Where(x => x.Id == posting.Journal.Id).Select(x => x.PostedAt).SingleAsync());
                db.Add(posting.Lines.Last()); await db.SaveChangesAsync();
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt={now} WHERE Id={posting.Journal.Id}"); db.ChangeTracker.Clear();
            Assert.Equal(0m, await db.Set<JournalLine>().Where(x => x.JournalId == posting.Journal.Id).SumAsync(x => x.Debit - x.Credit));
            Assert.Equal(posting.Obligation.InvoiceDue, await db.Set<JournalLine>().Where(x => x.JournalId == posting.Journal.Id && (x.AccountCode == "agency-receivable" || x.AccountCode == "relationship-receivable")).SumAsync(x => x.Debit - x.Credit));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE JournalLine SET Debit=0 WHERE Id={posting.Lines[0].Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE JournalLine WHERE Id={posting.Lines[0].Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt=NULL WHERE Id={posting.Journal.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE IssueFinancialObligation SET InvoiceDue=0 WHERE Id={posting.Obligation.Id}"));
            var source = posting.Lines[0];
            db.Add(new JournalLine { JournalId = source.JournalId, TransactionId = source.TransactionId, SourceComponentId = source.SourceComponentId, ComponentCode = source.ComponentCode,
                AccountCode = "fee-income", PartyKind = "internal", Credit = source.Debit, CoverageStartsAt = source.CoverageStartsAt, CoverageEndsAt = source.CoverageEndsAt });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        });
    }

    [Fact]
    public async Task RealSqlPolicyDocumentRequestsAreUniqueAndRequiredBeforeBoundQuote()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await StoredPolicy(db, password); var posting = await StoredIssuePosting(db, f, false); var now = f.Source.Clock.Current;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Journal SET PostedAt={now} WHERE Id={posting.Journal.Id}");
            PolicyDocumentRequest? first = null;
            foreach (var kind in new[] { "policy-schedule", "policy-certificate", "policy-statement" })
            {
                var template = new TemplateVersion { ProductId = f.Policy.ProductId, Code = "fictional-" + kind, Version = 1, Kind = kind, EffectiveFrom = now.AddDays(-1), EffectiveTo = now.AddYears(5), ContentJson = "{\"format\":\"policy-template-1\",\"title\":\"Fictional requested document\",\"notice\":\"Generation pending\"}" };
                db.Add(template); await db.SaveChangesAsync();
                var request = new PolicyDocumentRequest { PolicyId = f.Policy.Id, TermId = f.Term.Id, TransactionId = f.Transaction.Id, VersionId = f.Version.Id, Kind = kind, TemplateVersionId = template.Id, CreatedAt = now, CreatedBy = f.Source.Underwriter.UserId };
                request.PayloadJson = JsonSerializer.Serialize(new { documentId = request.Id, policyId = f.Policy.Id, versionId = f.Version.Id, snapshot = JsonSerializer.Deserialize<JsonElement>(f.Version.SnapshotJson) });
                request.PayloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.PayloadJson));
                var work = new OutboxWork { Kind = "policy-document", SubjectRecordId = request.Id, OperationKey = "policy-document/" + request.Id.ToString("N"), Payload = request.PayloadJson, CreatedAt = now, NextAttemptAt = now };
                db.Add(work); await db.SaveChangesAsync(); request.WorkId = work.Id; db.Add(request); await db.SaveChangesAsync(); first ??= request;
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET State=N'bound',BoundPolicyId={f.Policy.Id} WHERE Id={f.Policy.SourceQuoteId}");
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET State=N'accepted',BoundPolicyId=NULL WHERE Id={f.Policy.SourceQuoteId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicyDocumentRequest SET PayloadJson=N'{{}}' WHERE Id={first!.Id}"));
            var duplicate = new PolicyDocumentRequest { PolicyId = first!.PolicyId, TermId = first.TermId, TransactionId = first.TransactionId, VersionId = first.VersionId, Kind = first.Kind, TemplateVersionId = first.TemplateVersionId, WorkId = first.WorkId, PayloadJson = first.PayloadJson, PayloadHash = first.PayloadHash };
            db.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            Assert.Equal(3, await db.Set<PolicyDocumentRequest>().CountAsync()); Assert.All(await db.Set<PolicyDocumentRequest>().ToArrayAsync(), x => Assert.Equal("requested", x.State));
        });
    }
}
