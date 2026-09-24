using System.Globalization;
using System.Text.RegularExpressions;

namespace BackOffice.Infrastructure.Finance;

public static class FinanceReconciliationMath
{
    private static readonly Regex Canonical = new("^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static long SignedPence(string value)
    {
        if (value is null || !Canonical.IsMatch(value) ||
            !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount))
            throw new ArgumentOutOfRangeException(nameof(value), "Canonical signed GBP amount required.");
        var pence = FinanceLedgerMath.Pence(amount);
        if (pence == 0) throw new ArgumentOutOfRangeException(nameof(value), "Zero is not bank movement evidence.");
        return pence;
    }

    public static long Residual(long source, IEnumerable<long> matched, IEnumerable<long> reversed)
    {
        if (source == 0) throw new ArgumentOutOfRangeException(nameof(source));
        var sign = Math.Sign(source);
        long applied = 0, undone = 0;
        foreach (var value in matched)
        {
            if (value == 0 || Math.Sign(value) != sign) throw new ArgumentOutOfRangeException(nameof(matched));
            applied = checked(applied + value);
        }
        foreach (var value in reversed)
        {
            if (value == 0 || Math.Sign(value) != sign) throw new ArgumentOutOfRangeException(nameof(reversed));
            undone = checked(undone + value);
        }
        if (Math.Abs((decimal)(applied - undone)) > Math.Abs((decimal)source) ||
            Math.Sign(applied - undone) != sign && applied != undone ||
            Math.Abs((decimal)undone) > Math.Abs((decimal)applied))
            throw new ArgumentOutOfRangeException(nameof(matched), "Match evidence exceeds its signed cash source.");
        return checked(source - applied + undone);
    }
}
