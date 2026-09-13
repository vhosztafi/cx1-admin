using System.Globalization;
using System.Text.RegularExpressions;

namespace BackOffice.Domain;

/// <summary>Exact signed GBP minor units. Risk-value positivity is a use-case rule.</summary>
public readonly partial record struct Money(long Pence)
{
    public static Money Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Format().IsMatch(value))
            throw new FormatException("Money requires invariant digits and exactly two decimal places.");
        var pounds = decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return new Money(checked((long)(pounds * 100m)));
    }

    public static Money Round(decimal pounds) => new(checked((long)decimal.Round(pounds * 100m, 0, MidpointRounding.AwayFromZero)));
    public override string ToString() => (Pence / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    public static Money operator +(Money left, Money right) => new(checked(left.Pence + right.Pence));
    public static Money operator -(Money left, Money right) => new(checked(left.Pence - right.Pence));

    [GeneratedRegex(@"\A-?(0|[1-9][0-9]{0,12})\.[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
