using System.Globalization;
using System.Text.RegularExpressions;

namespace BackOffice.Infrastructure.Finance;

public static partial class FinancePeriodMath
{
    [GeneratedRegex("^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalMoney();

    public static decimal Parse(string value)
    {
        if (value is null || !CanonicalMoney().IsMatch(value) ||
            !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) ||
            FinanceLedgerMath.Money(amount) != value || FinanceLedgerMath.Pence(amount) == 0)
            throw new FormatException("A nonzero canonical signed GBP amount is required.");
        return amount;
    }

    public static bool TryBalanced(string debtor, string provider, string cash, string internalDelta)
    {
        try
        {
            var values = new[] { debtor, provider, cash, internalDelta }.Select(ParseOrZero).ToArray();
            if (values.All(x => x == 0)) return false;
            return values[0] - values[1] + values[2] + values[3] == 0;
        }
        catch (Exception error) when (error is FormatException or OverflowException) { return false; }
    }

    public static decimal ParseOrZero(string value)
    {
        if (value == "0.00") return 0;
        return Parse(value);
    }
}
