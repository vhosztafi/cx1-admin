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
    public async Task RealSqlFinanceReceiptDistinctKeysCannotSpendFinalPennyTwice()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var finance = await FinanceLedgerActor(db);
            var service = new FinanceReceiptService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var recorded = await service.RecordAsync(finance, obligation.AgencyId, "0.01", "GBP", DateOnly.FromDateTime(f.Clock.GetUtcNow().Date),
                "Fictional receipt", "manual", Guid.NewGuid(), "agency", obligation.AgencyId,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var initial = await service.DetailAsync(finance, recorded.ResourceId);
            var commands = Enumerable.Range(0, 2).Select(_ => service.AllocateAsync(finance, initial.Id, initial.AssignmentId,
                [new ReceiptAllocationInput(obligation.Id, "0.01")], Guid.NewGuid().ToString("N"), Guid.NewGuid())).ToArray();
            var outcomes = await Task.WhenAll(commands.Select(async command =>
            {
                try { await command; return "ok"; }
                catch (QuoteOperationException error) { return error.Code; }
            }));
            Assert.Single(outcomes, x => x == "ok");
            Assert.Single(outcomes, x => x == "allocation-exceeds-residual");
            var final = await service.DetailAsync(finance, initial.Id);
            Assert.Equal("0.00", final.Residual);
            Assert.Single(final.Allocations, x => x.ReversalOfId is null);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinanceReceiptReversalPayerHistoryAndSourcePostingRemainAuditable()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var actor = await FinanceLedgerActor(db);
            var service = new FinanceReceiptService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var receivedOn = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            var origin = Guid.NewGuid(); var key = Guid.NewGuid().ToString("N");
            var saved = await service.RecordAsync(actor, obligation.AgencyId, "1.00", "GBP", receivedOn,
                "Fictional traceable receipt", "manual", origin, "agency", obligation.AgencyId, key, Guid.NewGuid());
            var restarted = new FinanceReceiptService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var replay = await restarted.RecordAsync(actor, obligation.AgencyId, "1.00", "GBP", receivedOn,
                "Fictional traceable receipt", "manual", origin, "agency", obligation.AgencyId, key, Guid.NewGuid());
            Assert.True(replay.Replayed);
            Assert.Equal(saved.ResourceId, replay.ResourceId);
            var first = await restarted.DetailAsync(actor, saved.ResourceId);
            var applied = await restarted.AllocateAsync(actor, first.Id, first.AssignmentId,
                [new ReceiptAllocationInput(obligation.Id, "0.60")], Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal("0.40", (await restarted.DetailAsync(actor, first.Id)).Residual);
            var postings = await db.Set<FinancePosting>().AsNoTracking().OrderBy(x => x.SourceKind).ToArrayAsync();
            Assert.Equal(2, postings.Length);
            Assert.Equal(1m, postings.Sum(x => x.CashDelta));
            Assert.Equal(-0.60m, postings.Sum(x => x.DebtorDelta));
            Assert.All(postings, x => Assert.Equal(0m, x.DebtorDelta - x.ProviderDelta + x.CashDelta + x.InternalDelta));
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => restarted.AssignAsync(actor,
                first.Id, first.AssignmentId, "unidentified", null, "Fictional payer review pending",
                Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            Assert.Equal(400, (await Assert.ThrowsAsync<QuoteOperationException>(() => restarted.RecordAsync(actor,
                obligation.AgencyId, "0.01", "USD", receivedOn, "Wrong currency receipt", "manual",
                Guid.NewGuid(), "agency", obligation.AgencyId, Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => restarted.RecordAsync(actor,
                obligation.AgencyId, "0.01", "GBP", receivedOn, "Wrong debtor receipt", "manual",
                Guid.NewGuid(), "agency", Guid.NewGuid(), Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            Assert.Equal(400, (await Assert.ThrowsAsync<QuoteOperationException>(() => restarted.AllocateAsync(actor,
                first.Id, first.AssignmentId, [new ReceiptAllocationInput(obligation.Id, "0.00")],
                Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var concurrent = await Task.WhenAll(
                restarted.ReverseAsync(actor, applied.ResourceId,
                    "Fictional mistaken application reversed", Guid.NewGuid().ToString("N"), Guid.NewGuid()),
                restarted.AllocateAsync(actor, first.Id, first.AssignmentId,
                    [new ReceiptAllocationInput(obligation.Id, "0.40")], Guid.NewGuid().ToString("N"), Guid.NewGuid()));
            var reversed = concurrent[0];
            Assert.Equal("0.60", (await restarted.DetailAsync(actor, first.Id)).Residual);
            await restarted.ReverseAsync(actor, concurrent[1].ResourceId,
                "Fictional second application reversed", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal("1.00", (await restarted.DetailAsync(actor, first.Id)).Residual);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => restarted.ReverseAsync(actor,
                applied.ResourceId, "Fictional duplicate reversal attempt", Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var assigned = await restarted.AssignAsync(actor, first.Id, first.AssignmentId,
                "unidentified", null, "Fictional payer identity is under review", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var unresolved = await restarted.DetailAsync(actor, first.Id);
            Assert.Equal(assigned.Etag, '"' + unresolved.AssignmentId.ToString("N") + '"');
            Assert.Equal("unidentified", unresolved.PayerKind);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => restarted.AllocateAsync(actor,
                first.Id, unresolved.AssignmentId, [new ReceiptAllocationInput(obligation.Id, "0.01")],
                Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var now = f.Clock.GetUtcNow();
            db.Add(new FinancePosting { SourceKind = "receipt-application", SourceId = Guid.NewGuid(),
                AgencyId = obligation.AgencyId, RelationshipId = obligation.RelationshipId,
                PolicyId = obligation.PolicyId, TransactionId = obligation.TransactionId, DebtorKind = "agency",
                AccountingPeriodId = first.AccountingPeriodId, PostingDate = first.PostingDate,
                EffectiveAt = now, PostedAt = now, CreatedAt = now, Currency = "GBP",
                DebtorDelta = -0.01m, InternalDelta = 0.01m,
                Reason = "Fictional forged cash application", CreatedBy = actor.UserId });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Allocation SET Amount=999 WHERE Id={applied.ResourceId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Receipt SET Amount=999 WHERE Id={first.Id}"));
            Assert.Equal(1m, await db.Set<FinancePosting>().SumAsync(x => x.CashDelta));
            Assert.Equal(0m, await db.Set<FinancePosting>().SumAsync(x => x.DebtorDelta));
            Assert.Contains(unresolved.Allocations, x => x.Id == reversed.ResourceId && x.ReversalOfId == applied.ResourceId);
            Assert.Equal(2, unresolved.Allocations.Count(x => x.ReversalOfId != null));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");
            var forbiddenReplay = await Assert.ThrowsAsync<QuoteOperationException>(() => restarted.RecordAsync(actor,
                obligation.AgencyId, "1.00", "GBP", receivedOn, "Fictional traceable receipt", "manual", origin,
                "agency", obligation.AgencyId, key, Guid.NewGuid()));
            Assert.Equal(403, forbiddenReplay.Status);
        });
    }

    [Fact]
    public async Task RealSqlFinanceReceiptTwoReceiptsCompeteForOneInvoiceAndSqlRejectsOverResidual()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var invoice = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var actor = await FinanceLedgerActor(db);
            var service = new FinanceReceiptService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var amount = FinanceLedgerMath.Money(invoice.InvoiceDue);
            var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            async Task<ReceiptView> Receipt()
            {
                var saved = await service.RecordAsync(actor, invoice.AgencyId, amount, "GBP", date,
                    "Fictional invoice competition", "manual", Guid.NewGuid(), "agency", invoice.AgencyId,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid());
                return await service.DetailAsync(actor, saved.ResourceId);
            }
            var a = await Receipt(); var b = await Receipt();
            var attempts = await Task.WhenAll(new[] { a, b }.Select(async receipt =>
            {
                try { await service.AllocateAsync(actor, receipt.Id, receipt.AssignmentId,
                    [new ReceiptAllocationInput(invoice.Id, amount)], Guid.NewGuid().ToString("N"), Guid.NewGuid()); return "ok"; }
                catch (QuoteOperationException error) { return error.Code; }
            }));
            Assert.Single(attempts, x => x == "ok");
            Assert.Single(attempts, x => x == "allocation-exceeds-residual");
            await using var foreign = f.Factory.CreateDbContext();
            var period = await foreign.Set<AccountingPeriod>().AsNoTracking().SingleAsync(x => x.Id == a.AccountingPeriodId);
            var loser = (await service.DetailAsync(actor, a.Id)).Residual == amount ? a : b;
            var now = f.Clock.GetUtcNow();
            foreign.Add(new Allocation { ReceiptId = loser.Id, ObligationId = invoice.Id,
                Amount = 0.01m, AppliedAt = now, CreatedAt = now, CreatedBy = actor.UserId,
                Reason = "Fictional direct overdraw attempt", OperationId = Guid.NewGuid(), Ordinal = 1,
                AccountingPeriodId = period.Id, PostingDate = loser.PostingDate });
            await Assert.ThrowsAsync<DbUpdateException>(() => foreign.SaveChangesAsync());
            Assert.Equal(1, await db.Set<Allocation>().CountAsync(x => x.ReversalOfId == null));
            Assert.Equal(2, await db.Set<FinancePosting>().CountAsync(x => x.SourceKind == "receipt"));
            Assert.Equal(1, await db.Set<FinancePosting>().CountAsync(x => x.SourceKind == "receipt-application"));
        });
    }
}
