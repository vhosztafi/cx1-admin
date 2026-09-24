using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlFinanceReconciliationRetainsDistinctImportsAndVisibleExplainedVariance()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var agencyId = (await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync()).AgencyId;
            var actor = await FinanceLedgerActor(db);
            var service = new FinanceReconciliationService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            var firstKey = Guid.NewGuid().ToString("N");
            Task<CommandOutcome> Import(string importKey, string reference = "Same bank reference", string raw = "{\"source\":\"bank-file-1\"}", string? commandKey = null)
                => service.ImportAsync(actor, agencyId, importKey, date, reference, "10.00", "GBP", raw,
                    commandKey ?? Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var first = await Import("file-1:row-1", " Same bank reference ", commandKey: firstKey);
            var replay = await Import("file-1:row-1", " Same bank reference ");
            Assert.Equal(first.ResourceId, replay.ResourceId);
            Assert.Contains("reimported", replay.Body);
            var distinct = await Import("file-2:row-7");
            Assert.NotEqual(first.ResourceId, distinct.ResourceId);
            var firstView = await service.BankLineAsync(actor, first.ResourceId);
            Assert.Contains(distinct.ResourceId, firstView.DuplicateCandidateIds);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                Import("file-1:row-1", raw: "{\"source\":\"changed\"}"))).Status);
            var period = await service.CreateAsync(actor, agencyId, date.AddDays(-1), date.AddDays(1),
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CreateAsync(actor, agencyId,
                date, date.AddDays(2), Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT Reconciliation(Id,AgencyId,[From],[To],CreatedAt,CreatedBy,UpdatedAt)
                VALUES({Guid.NewGuid()},{agencyId},{date},{date.AddDays(2)},
                    {f.Clock.GetUtcNow()},{actor.UserId},{f.Clock.GetUtcNow()})
                """));
            var initial = await service.DetailAsync(actor, period.ResourceId);
            Assert.Equal(2, initial.UnaddressedCount);
            Assert.Equal("20.00", initial.AbsoluteVariance);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CompleteAsync(actor,
                period.ResourceId, Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            await service.ExcludeAsync(actor, period.ResourceId, distinct.ResourceId, first.ResourceId,
                "Bank statement page 7 row 2", "Confirmed duplicate of original import row",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal(1, (await service.DetailAsync(actor, period.ResourceId)).UnaddressedCount);
            await service.ExplainAsync(actor, period.ResourceId, first.ResourceId,
                "Awaiting a separately posted cash receipt", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var explained = await service.DetailAsync(actor, period.ResourceId);
            Assert.Equal(0, explained.UnaddressedCount);
            Assert.Equal("10.00", explained.NetVariance);
            Assert.Equal("10.00", explained.AbsoluteVariance);
            await service.CompleteAsync(actor, period.ResourceId, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal("10.00", (await service.DetailAsync(actor, period.ResourceId)).NetVariance);
            Assert.Empty(await db.Set<FinancePosting>().ToArrayAsync());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Import("file-3:row-1"))).Status);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE BankLine SET Reference='forged change' WHERE Id={first.ResourceId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Reconciliation SET CompletedAt=NULL,CompletedBy=NULL WHERE Id={period.ResourceId}"));
            Assert.False(db.Database.HasPendingModelChanges());
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                service.ImportAsync(actor, agencyId, "file-1:row-1", date, " Same bank reference ", "10.00", "GBP",
                    "{\"source\":\"bank-file-1\"}", firstKey, Guid.NewGuid()))).Status);
        });
    }

    [Fact]
    public async Task RealSqlFinanceReconciliationMatchesOnlyRealSignedCashAndUniqueReversals()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var agencyId = (await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync()).AgencyId;
            var actor = await FinanceLedgerActor(db);
            var service = new FinanceReconciliationService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var receiptService = new FinanceReceiptService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            async Task<Guid> Receipt(string amount)
            {
                var saved = await receiptService.RecordAsync(actor, agencyId, amount, "GBP", date,
                    "Fictional cash receipt", "manual", Guid.NewGuid(), "agency", agencyId,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid());
                return await db.Set<FinancePosting>().AsNoTracking().Where(x => x.SourceKind == "receipt" && x.SourceId == saved.ResourceId)
                    .Select(x => x.Id).SingleAsync();
            }
            var firstPosting = await Receipt("10.00");
            var secondPosting = await Receipt("5.00");
            var period = await service.CreateAsync(actor, agencyId, date, date.AddDays(1),
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var line = await service.ImportAsync(actor, agencyId, "split-file:row-1", date,
                "Split deposit", "15.00", "GBP", "{\"source\":\"split-fixture\"}",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() =>
                receiptService.RecordAsync(actor, agencyId, "1.00", "GBP", date,
                    "Split deposit", "bank-import", line.ResourceId, "agency", agencyId,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var first = await service.MatchAsync(actor, period.ResourceId, line.ResourceId, firstPosting,
                "10.00", "First saved cash source", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal("5.00", (await service.DetailAsync(actor, period.ResourceId)).Lines.Single().Residual);
            var second = await service.MatchAsync(actor, period.ResourceId, line.ResourceId, secondPosting,
                "5.00", "Second saved cash source", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal("0.00", (await service.DetailAsync(actor, period.ResourceId)).Lines.Single().Residual);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.MatchAsync(actor,
                period.ResourceId, line.ResourceId, firstPosting, "0.01", "Overdrawn source attempt",
                Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var candidate = await service.ImportAsync(actor, agencyId, "split-file:duplicate", date,
                "Split deposit", "15.00", "GBP", "{\"source\":\"other-row\"}",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ExcludeAsync(actor,
                period.ResourceId, line.ResourceId, candidate.ResourceId, "bank evidence from statement",
                "Matched line cannot be excluded", Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var negative = await service.ImportAsync(actor, agencyId, "split-file:debit", date,
                "Bank debit", "-5.00", "GBP", "{\"source\":\"negative-fixture\"}",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var outside = await service.ImportAsync(actor, agencyId, "split-file:outside", date.AddDays(2),
                "Future bank evidence", "1.00", "GBP", "{\"source\":\"future-fixture\"}",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.MatchAsync(actor,
                period.ResourceId, negative.ResourceId, secondPosting, "-5.00", "Opposite sign source attempt",
                Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            Assert.Equal(400, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ImportAsync(actor,
                agencyId, "split-file:wrong-currency", date, "USD attempt", "1.00", "USD", "{}",
                Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var reversed = await service.ReverseAsync(actor, second.ResourceId,
                "Duplicate reconciliation match reversed", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal("5.00", (await service.DetailAsync(actor, period.ResourceId)).Lines.Single(x => x.BankLineId == line.ResourceId).Residual);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReverseAsync(actor,
                second.ResourceId, "Second reversal must be rejected", Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var restarted = new FinanceReconciliationService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            Assert.Contains((await restarted.DetailAsync(actor, period.ResourceId)).Matches,
                x => x.Id == reversed.ResourceId && x.ReversalOfId == second.ResourceId);
            // Direct SQL cannot overdraw a saved source or rewrite the evidence.
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT ReconciliationMatch(Id,ReconciliationId,BankLineId,FinancePostingId,SignedAmount,Reason,MatchedAt,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{period.ResourceId},{line.ResourceId},{firstPosting},100.00,
                    'Forged direct overmatch',{f.Clock.GetUtcNow()},{f.Clock.GetUtcNow()},{actor.UserId})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT ReconciliationMatch(Id,ReconciliationId,BankLineId,FinancePostingId,SignedAmount,Reason,MatchedAt,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{period.ResourceId},{line.ResourceId},{firstPosting},1.00,
                    'Aggregate target overmatch',{f.Clock.GetUtcNow()},{f.Clock.GetUtcNow()},{actor.UserId})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT ReconciliationMatch(Id,ReconciliationId,BankLineId,FinancePostingId,SignedAmount,Reason,MatchedAt,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{period.ResourceId},{outside.ResourceId},{firstPosting},1.00,
                    'Outside period source attempt',{f.Clock.GetUtcNow()},{f.Clock.GetUtcNow()},{actor.UserId})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT BankLineExclusion(Id,ReconciliationId,BankLineId,DuplicateOfBankLineId,EvidenceReference,Reason,ExcludedAt,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{period.ResourceId},{line.ResourceId},{candidate.ResourceId},
                    'Bank statement page 2','Forged matched-line exclusion',{f.Clock.GetUtcNow()},{f.Clock.GetUtcNow()},{actor.UserId})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ReconciliationMatch SET SignedAmount=999 WHERE Id={first.ResourceId}"));
            Assert.Equal(3, await db.Set<ReconciliationMatch>().CountAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinanceReconciliationDistinctKeysCannotMatchLastPennyTwice()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var agencyId = (await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync()).AgencyId;
            var actor = await FinanceLedgerActor(db);
            var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
            var service = new FinanceReconciliationService(f.Factory, boundary, f.Clock);
            var receiptService = new FinanceReceiptService(f.Factory, boundary, f.Clock);
            var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            var receipt = await receiptService.RecordAsync(actor, agencyId, "0.01", "GBP", date,
                "Fictional last penny", "manual", Guid.NewGuid(), "agency", agencyId,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var postingId = await db.Set<FinancePosting>().AsNoTracking().Where(x => x.SourceKind == "receipt" && x.SourceId == receipt.ResourceId)
                .Select(x => x.Id).SingleAsync();
            var line = await service.ImportAsync(actor, agencyId, "last-penny:1", date,
                "Fictional last penny", "0.01", "GBP", "{\"source\":\"race\"}",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var period = await service.CreateAsync(actor, agencyId, date, date.AddDays(1),
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var attempts = Enumerable.Range(0, 2).Select(_ =>
                service.MatchAsync(actor, period.ResourceId, line.ResourceId, postingId, "0.01",
                    "Independent last penny match", Guid.NewGuid().ToString("N"), Guid.NewGuid())).ToArray();
            var outcomes = await Task.WhenAll(attempts.Select(async attempt =>
            {
                try { await attempt; return "saved"; }
                catch (QuoteOperationException error) { return error.Code; }
            }));
            Assert.Single(outcomes, x => x == "saved");
            Assert.Single(outcomes, x => x == "match-exceeds-residual");
            var restarted = new FinanceReconciliationService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var detail = await restarted.DetailAsync(actor, period.ResourceId);
            Assert.Single(detail.Matches);
            Assert.Equal("0.00", detail.Lines.Single().Residual);
            Assert.Equal(1, await db.Set<IdempotencyRecord>().CountAsync(x => x.Route.EndsWith($"/reconciliations/{period.ResourceId:N}/matches")));
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinanceReconciliationRequiresVisibleCashSourceExplanationEvenWithoutBankLines()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var agencyId = (await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync()).AgencyId;
            var actor = await FinanceLedgerActor(db);
            var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
            var service = new FinanceReconciliationService(f.Factory, boundary, f.Clock);
            var receipts = new FinanceReceiptService(f.Factory, boundary, f.Clock);
            var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            var receipt = await receipts.RecordAsync(actor, agencyId, "7.00", "GBP", date,
                "Unmatched real cash", "manual", Guid.NewGuid(), "agency", agencyId,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var postingId = await db.Set<FinancePosting>().AsNoTracking().Where(x => x.SourceKind == "receipt" &&
                x.SourceId == receipt.ResourceId).Select(x => x.Id).SingleAsync();
            var period = await service.CreateAsync(actor, agencyId, date, date.AddDays(1),
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var view = await service.DetailAsync(actor, period.ResourceId);
            Assert.Empty(view.Lines);
            Assert.Single(view.Targets);
            Assert.Equal("7.00", view.Targets.Single().Residual);
            Assert.Equal(1, view.UnaddressedCount);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CompleteAsync(actor,
                period.ResourceId, Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Reconciliation SET CompletedAt={f.Clock.GetUtcNow()},CompletedBy={actor.UserId} WHERE Id={period.ResourceId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT ReconciliationTargetVariance(Id,ReconciliationId,FinancePostingId,SignedResidual,Reason,ExplainedAt,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{period.ResourceId},{postingId},1.00,'Forged residual variance',
                    {f.Clock.GetUtcNow()},{f.Clock.GetUtcNow()},{actor.UserId})
                """));
            var explanation = await service.ExplainTargetAsync(actor, period.ResourceId, postingId,
                "Unmatched deposit retained for next bank statement", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.NotEqual(Guid.Empty, explanation.ResourceId);
            var addressed = await service.DetailAsync(actor, period.ResourceId);
            Assert.Equal(0, addressed.UnaddressedCount);
            Assert.Equal("7.00", addressed.Targets.Single().Residual);
            Assert.True(addressed.Targets.Single().Addressed);
            await service.CompleteAsync(actor, period.ResourceId, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.NotNull((await service.DetailAsync(actor, period.ResourceId)).CompletedAt);
            Assert.Single(await db.Set<FinancePosting>().AsNoTracking().ToArrayAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
