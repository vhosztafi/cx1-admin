using System.Globalization;
using System.Text.RegularExpressions;

namespace BackOffice.Infrastructure.Finance;

public static class FinanceReceiptMath
{
    private static readonly Regex Canonical = new("^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static long PositivePence(string value)
    {
        if (value is null || !Canonical.IsMatch(value) ||
            !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw new ArgumentOutOfRangeException(nameof(value), "Positive canonical GBP amount required.");
        var pence = FinanceLedgerMath.Pence(amount);
        if (pence <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Cash amount must be positive.");
        return pence;
    }

    public static long Residual(long capacity, IEnumerable<long> applied, IEnumerable<long> reversed)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        var application = applied.Aggregate(0L, (sum, value) => value > 0 ? checked(sum + value) :
            throw new ArgumentOutOfRangeException(nameof(applied)));
        var reversal = reversed.Aggregate(0L, (sum, value) => value > 0 ? checked(sum + value) :
            throw new ArgumentOutOfRangeException(nameof(reversed)));
        if (reversal > application || application - reversal > capacity)
            throw new ArgumentOutOfRangeException(nameof(applied), "Residual evidence exceeds its source.");
        return capacity - application + reversal;
    }

    public static decimal Money(long pence) => pence / 100m;
}
