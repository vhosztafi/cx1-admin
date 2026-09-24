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

    [Fact]
    public async Task RealSqlFinanceReceiptCrossedInvoiceOrderCannotPartiallyPostOrOverdraw()
    {
        await RunServicingRatingRequests("motor-trade-road-risks", "terms-prepare",
            onAccepted: async (db, f, cycle, acceptance, fence, etag) =>
        {
            await new ServicingIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, cycle.DraftId,
                Convert.FromBase64String(etag.Trim('"')), fence,
                new(cycle.Id, acceptance.RatingId, acceptance.TermsVersionId, acceptance.Id,
                    acceptance.TermsHash, acceptance.AssuranceHash, "Issue fictional crossed invoice adjustment"),
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var invoices = await db.Set<IssueFinancialObligation>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(2, invoices.Length);
            Assert.Equal(invoices[0].AgencyId, invoices[1].AgencyId);
            Assert.Equal(invoices[0].DebtorKind, invoices[1].DebtorKind);
            Assert.Equal(invoices[0].DebtorAgencyId, invoices[1].DebtorAgencyId);
            Assert.Equal("agency", invoices[0].DebtorKind);
            var actor = await FinanceLedgerActor(db);
            var factory = f.Factory;
            var clock = f.Clock;
            var serviceA = new FinanceReceiptService(factory, new SqlCommandBoundary(factory, clock), clock);
            var serviceB = new FinanceReceiptService(factory, new SqlCommandBoundary(factory, clock), clock);
            var total = FinanceLedgerMath.Money(FinanceReceiptMath.Money(checked(
                FinanceLedgerMath.Pence(invoices[0].InvoiceDue) + FinanceLedgerMath.Pence(invoices[1].InvoiceDue))));
            var date = DateOnly.FromDateTime(clock.GetUtcNow().Date);
            async Task<ReceiptView> Receipt(FinanceReceiptService service)
            {
                var saved = await service.RecordAsync(actor, invoices[0].AgencyId, total, "GBP", date,
                    "Fictional crossed invoice race", "manual", Guid.NewGuid(), "agency", invoices[0].AgencyId,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid());
                return await service.DetailAsync(actor, saved.ResourceId);
            }
            var a = await Receipt(serviceA);
            var b = await Receipt(serviceB);
            var forward = invoices.Select(x => new ReceiptAllocationInput(x.Id, FinanceLedgerMath.Money(x.InvoiceDue))).ToArray();
            var reverse = forward.Reverse().ToArray();
            async Task<string> Attempt(FinanceReceiptService service, ReceiptView receipt,
                IReadOnlyList<ReceiptAllocationInput> items)
            {
                try
                {
                    await service.AllocateAsync(actor, receipt.Id, receipt.AssignmentId, items,
                        Guid.NewGuid().ToString("N"), Guid.NewGuid());
                    return "ok";
                }
                catch (QuoteOperationException error) { return error.Code; }
            }
            var outcomes = await Task.WhenAll(Attempt(serviceA, a, forward), Attempt(serviceB, b, reverse));
            Assert.Single(outcomes, x => x == "ok");
            Assert.Single(outcomes, x => x == "allocation-exceeds-residual");
            var allocations = await db.Set<Allocation>().AsNoTracking().Where(x => x.ReversalOfId == null).ToArrayAsync();
            Assert.Equal(2, allocations.Length);
            Assert.Single(allocations.Select(x => x.ReceiptId).Distinct());
            foreach (var invoice in invoices)
                Assert.Equal(invoice.InvoiceDue, allocations.Single(x => x.ObligationId == invoice.Id).Amount);
            var winner = allocations[0].ReceiptId;
            Assert.Equal("0.00", (await serviceA.DetailAsync(actor, winner)).Residual);
            Assert.Equal(total, (await serviceB.DetailAsync(actor, winner == a.Id ? b.Id : a.Id)).Residual);
            Assert.Equal(2, await db.Set<FinancePosting>().CountAsync(x => x.SourceKind == "receipt"));
            Assert.Equal(2, await db.Set<FinancePosting>().CountAsync(x => x.SourceKind == "receipt-application"));
            Assert.Equal(0m, await db.Set<FinancePosting>().Where(x => x.SourceKind == "receipt-application")
                .SumAsync(x => x.CashDelta));
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
