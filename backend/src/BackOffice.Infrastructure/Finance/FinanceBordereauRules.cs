using System.Text;
using System.Text.RegularExpressions;

namespace BackOffice.Infrastructure.Finance;

public sealed record BordereauExportRow(Guid SourceJournalId, string PolicyReference, string ProviderProductCode,
    string AgencyReference, string Premium, string Tax, string Fee, string Commission, string NetDue);
public sealed record BordereauValidationIssue(Guid? SourceJournalId, string Field, string Code);

public static class FinanceBordereauValidation
{
    private static readonly Regex Money = new("^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<BordereauValidationIssue> Check(IReadOnlyList<BordereauExportRow> rows)
    {
        var failures = new List<BordereauValidationIssue>();
        if (rows.Count == 0) failures.Add(new(null, "rows", "no-included-rows"));
        var seen = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row.SourceJournalId == Guid.Empty || !seen.Add(row.SourceJournalId))
                failures.Add(new(row.SourceJournalId, "sourceJournalId", "duplicate-or-missing-source"));
            Field("policyReference", row.PolicyReference, 100);
            Field("providerProductCode", row.ProviderProductCode, 100);
            Field("agencyReference", row.AgencyReference, 100);
            Amount("premium", row.Premium); Amount("tax", row.Tax); Amount("fee", row.Fee);
            Amount("commission", row.Commission); Amount("netDue", row.NetDue);

            void Field(string name, string value, int maximum)
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(c => c == '\0'))
                    failures.Add(new(row.SourceJournalId, name, "mapping-required"));
            }
            void Amount(string name, string value)
            {
                if (!Money.IsMatch(value)) failures.Add(new(row.SourceJournalId, name, "canonical-money-required"));
            }
        }
        return failures;
    }
}

public static class BordereauCsv
{
    public static string SafeText(string value)
    {
        if (value.Length == 0) return value;
        var trimmed = value.TrimStart();
        return value[0] is '\t' or '\r' or '\n' || trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@'
            ? "'" + value : value;
    }

    public static byte[] Write(IReadOnlyList<BordereauExportRow> rows)
    {
        var csv = new StringBuilder();
        csv.Append("source_journal_id,policy_reference,provider_product_code,agency_reference,premium,tax,fee,commission,net_due\r\n");
        foreach (var row in rows)
        {
            csv.Append(row.SourceJournalId.ToString("D")).Append(',')
                .Append(Text(SafeText(row.PolicyReference))).Append(',')
                .Append(Text(SafeText(row.ProviderProductCode))).Append(',')
                .Append(Text(SafeText(row.AgencyReference))).Append(',')
                .Append(row.Premium).Append(',').Append(row.Tax).Append(',').Append(row.Fee).Append(',')
                .Append(row.Commission).Append(',').Append(row.NetDue).Append("\r\n");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }

    private static string Text(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
