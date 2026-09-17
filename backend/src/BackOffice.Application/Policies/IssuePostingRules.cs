using BackOffice.Domain;

namespace BackOffice.Application.Policies;

public sealed record IssuePostingInput(decimal Premium, decimal Tax, decimal Fee, decimal Commission, int FeeShareBasisPoints,
    string Collector, string Settlement, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record IssuePostingComponent(string Code, decimal Amount);
public sealed record IssuePostingLine(string ComponentCode, string AccountCode, decimal Debit, decimal Credit);
public sealed record IssuePosting(decimal GrossDue, decimal InvoiceDue, decimal NetDue, decimal FeeShare, decimal InsurerDue,
    decimal RetainedFee, decimal BrokerPayable, string DebtorKind, string EffectiveSettlement,
    IReadOnlyList<IssuePostingComponent> Components, IReadOnlyList<IssuePostingLine> Lines);

public static class IssuePostingRules
{
    public const decimal MaximumAmount = 9999999999999.99m;
    public static IssuePosting Calculate(IssuePostingInput input)
    {
        if (input.Collector is not ("agency" or "mga") || input.Settlement is not ("net-remittance" or "separate-payment") ||
            input.FeeShareBasisPoints is < 0 or > 10000 || input.StartsAt.Offset != TimeSpan.Zero || input.EndsAt.Offset != TimeSpan.Zero || input.StartsAt >= input.EndsAt)
            throw new ArgumentException("Issue requires valid pinned settlement and a positive UTC coverage interval.");
        foreach (var value in new[] { input.Premium, input.Tax, input.Fee, input.Commission })
            if (value < 0 || value > MaximumAmount || decimal.Round(value, 2) != value) throw new ArgumentException("Issue components require bounded nonnegative exact pennies.");
        if (input.Commission > input.Premium) throw new ArgumentException("Commission cannot exceed its original premium.");
        var share = Money.Round(input.Fee * input.FeeShareBasisPoints / 10000m).Pence / 100m;
        var gross = input.Premium + input.Tax + input.Fee;
        if (gross > MaximumAmount) throw new ArgumentException("Gross issue amount exceeds storage bounds.");
        var net = gross - input.Commission - share;
        var netted = input.Collector == "agency" && input.Settlement == "net-remittance";
        var debtor = input.Collector == "agency" ? "agency-receivable" : "relationship-receivable";
        var remunerationAccount = netted ? debtor : "broker-remuneration-payable";
        IssuePostingComponent[] components = [new("premium", input.Premium), new("tax", input.Tax), new("fee", input.Fee), new("commission", input.Commission), new("fee-share", share)];
        var lines = new List<IssuePostingLine>();
        void Pair(string code, string debit, string credit, decimal amount)
        { if (amount > 0) { lines.Add(new(code, debit, amount, 0)); lines.Add(new(code, credit, 0, amount)); } }
        Pair("premium", debtor, "insurer-payable", input.Premium); Pair("tax", debtor, "insurer-payable", input.Tax);
        Pair("fee", debtor, "fee-income", input.Fee); Pair("commission", "insurer-payable", remunerationAccount, input.Commission);
        Pair("fee-share", "fee-income", remunerationAccount, share);
        return new(gross, netted ? net : gross, net, share, input.Premium + input.Tax - input.Commission, input.Fee - share,
            netted ? 0 : input.Commission + share, input.Collector == "agency" ? "agency" : "relationship",
            netted ? "net-remittance" : "separate-payment", Array.AsReadOnly(components), lines.AsReadOnly());
    }
}
