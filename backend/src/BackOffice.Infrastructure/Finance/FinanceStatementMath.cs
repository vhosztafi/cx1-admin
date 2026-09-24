namespace BackOffice.Infrastructure.Finance;

public sealed record StatementMovement(string SourceKey, DateOnly PostingDate, long DeltaPence, DateOnly? DueDate);
public sealed record StatementLine(string SourceKey, DateOnly PostingDate, string Debit, string Credit,
    string RunningBalance, string? DueDate, bool Overdue);
public sealed record StatementReconciliation(string Opening, string Debits, string Credits, string Closing,
    IReadOnlyList<StatementLine> Rows);

public static class FinanceStatementMath
{
    // All arithmetic is checked in pence before conversion to canonical money strings.
    public static StatementReconciliation Reconcile(IEnumerable<StatementMovement> source, DateOnly from, DateOnly to)
    {
        if (from >= to) throw new ArgumentException("Statement window must be nonempty.");
        var rows = source.OrderBy(x => x.PostingDate).ThenBy(x => x.SourceKey, StringComparer.Ordinal).ToArray();
        var opening = FinanceLedgerMath.SumPence(rows.Where(x => x.PostingDate < from).Select(x => x.DeltaPence));
        long balance = opening, debit = 0, credit = 0;
        var lines = new List<StatementLine>();
        foreach (var row in rows.Where(x => x.PostingDate >= from && x.PostingDate < to))
        {
            var positive = Math.Max(row.DeltaPence, 0);
            var negative = row.DeltaPence < 0 ? checked(-row.DeltaPence) : 0;
            debit = FinanceLedgerMath.SumPence([debit, positive]);
            credit = FinanceLedgerMath.SumPence([credit, negative]);
            balance = FinanceLedgerMath.SumPence([balance, row.DeltaPence]);
            lines.Add(new StatementLine(row.SourceKey, row.PostingDate, Money(positive), Money(negative),
                Money(balance), row.DueDate?.ToString("yyyy-MM-dd"), row.DueDate is DateOnly due && due < to.AddDays(-1) && row.DeltaPence > 0));
        }
        if (FinanceLedgerMath.SumPence([opening, debit, -credit]) != balance)
            throw new InvalidOperationException("Statement reconciliation failed.");
        return new(Money(opening), Money(debit), Money(credit), Money(balance), lines);
    }

    private static string Money(long pence) => FinanceLedgerMath.Money(pence / 100m);
}
