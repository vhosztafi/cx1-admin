using System.Globalization;

namespace BackOffice.Infrastructure.Finance;

public static class FinanceLedgerMath
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private const long MaxMoneyPence = 999_999_999_999_999;

    public static long Pence(decimal amount)
    {
        var scaled = amount * 100m;
        if (scaled != decimal.Truncate(scaled) || scaled > MaxMoneyPence || scaled < -MaxMoneyPence)
            throw new OverflowException("Amount cannot be represented as decimal(15,2).");
        return checked((long)scaled);
    }

    public static string Money(decimal amount) => (Pence(amount) / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static long SumPence(IEnumerable<long> amounts)
    {
        long total = 0;
        foreach (var amount in amounts) total = checked(total + amount);
        if (total > MaxMoneyPence || total < -MaxMoneyPence) throw new OverflowException("Aggregate exceeds decimal(15,2).");
        return total;
    }

    public static (long DebtorPence, long ProviderPence) Balances(IEnumerable<(long debtor, long provider)> movements)
    {
        long debtor = 0, provider = 0;
        foreach (var movement in movements)
        {
            debtor = checked(debtor + movement.debtor);
            provider = checked(provider + movement.provider);
        }
        if (Math.Abs((decimal)debtor) > MaxMoneyPence || Math.Abs((decimal)provider) > MaxMoneyPence)
            throw new OverflowException("Balance exceeds decimal(15,2).");
        return (debtor, provider);
    }

    public static DateOnly LegacyPostingDate(DateTimeOffset postedAt)
    {
        if (postedAt.Offset != TimeSpan.Zero) throw new ArgumentException("PostedAt must be UTC.", nameof(postedAt));
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(postedAt, London).DateTime);
    }
}
