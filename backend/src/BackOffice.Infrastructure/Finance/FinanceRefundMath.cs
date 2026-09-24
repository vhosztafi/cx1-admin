namespace BackOffice.Infrastructure.Finance;

public static class FinanceRefundMath
{
    // Earlier credits offset the oldest outstanding debt first. A credit can
    // return cash only after its remaining value has covered that debt.
    public static long Entitlement(long credit, long outstanding, long earlierCredits,
        long collected, long reserved)
    {
        if (credit < 0 || outstanding < 0 || earlierCredits < 0 || collected < 0 || reserved < 0)
            throw new ArgumentOutOfRangeException(nameof(credit));
        var debtAfterEarlierCredits = Math.Max(0, checked(outstanding - earlierCredits));
        var afterOffset = checked(credit - Math.Min(credit, debtAfterEarlierCredits));
        var cashCap = Math.Min(afterOffset, collected);
        if (reserved > cashCap) throw new ArgumentOutOfRangeException(nameof(reserved));
        return checked(cashCap - reserved);
    }

    public static int RequiredApprovals(long amount, long threshold)
    {
        if (amount <= 0 || threshold <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return amount <= threshold ? 1 : 2;
    }
}
