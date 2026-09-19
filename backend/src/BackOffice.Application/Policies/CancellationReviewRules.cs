namespace BackOffice.Application.Policies;

public sealed record CancellationPostedComponent(Guid Id, string Code, decimal Amount,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, Guid? OriginalComponentId);
public sealed record CancellationReturnPreview(ServicingPosting Posting, decimal RetainedFee, decimal RetainedFeeShare);

// Operates on the complete, locked, same-owner posted ledger supplied by the
// service. No current rating or rates may replace these original amounts.
public static class CancellationReviewRules
{
    public const string Version = "demo-servicing-1";
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    private static readonly string[] Codes = ["premium", "tax", "commission", "fee", "fee-share"];

    public static CancellationReturnPreview Calculate(DateTimeOffset termStart, DateTimeOffset termEnd,
        DateTimeOffset effective, string collector, string settlement, IReadOnlyList<CancellationPostedComponent> components,
        IReadOnlySet<Guid>? returnedComponentIds = null)
    {
        if (termStart.Offset != TimeSpan.Zero || termEnd.Offset != TimeSpan.Zero || effective.Offset != TimeSpan.Zero ||
            termStart >= termEnd || effective < termStart || effective >= termEnd || components is null || components.Count is < 5 or > 1505)
            throw Invalid();
        var ids = new HashSet<Guid>();
        foreach (var row in components)
        {
            if (row is null || row.Id == Guid.Empty || !ids.Add(row.Id) || row.OriginalComponentId is not null ||
                returnedComponentIds?.Contains(row.Id) == true || !Codes.Contains(row.Code, StringComparer.Ordinal) ||
                row.Amount is > IssuePostingRules.MaximumAmount or < -IssuePostingRules.MaximumAmount || decimal.Round(row.Amount, 2) != row.Amount ||
                row.StartsAt.Offset != TimeSpan.Zero || row.EndsAt.Offset != TimeSpan.Zero || row.StartsAt < termStart || row.EndsAt > termEnd ||
                row.StartsAt >= row.EndsAt || row.StartsAt > effective || LocalDay(row.StartsAt) >= LocalDay(row.EndsAt))
                throw Invalid();
        }
        if (Codes.Any(code => !components.Any(x => x.Code == code))) throw Invalid();
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var movements = new List<ServicingPostingMovement>();
        foreach (var row in components.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.StartsAt).ThenBy(x => x.Id))
        {
            var ordinal = ordinals.GetValueOrDefault(row.Code) + 1;
            ordinals[row.Code] = ordinal;
            var amount = row.Code is "fee" or "fee-share" ? 0m :
                ServicingRules.ReverseUnearned(row.Amount, LocalDay(row.StartsAt), LocalDay(row.EndsAt), LocalDay(effective));
            // Expired zero movements retain lineage; their interval is the
            // cancellation interval, never a negative or empty source interval.
            movements.Add(new(row.Code, ordinal, amount, effective, termEnd, row.Id));
        }
        var posting = ServicingPostingRules.Calculate(new("cancellation", collector, settlement, movements));
        var fee = components.Where(x => x.Code == "fee").Sum(x => x.Amount);
        var share = components.Where(x => x.Code == "fee-share").Sum(x => x.Amount);
        if (decimal.Abs(fee) > IssuePostingRules.MaximumAmount || decimal.Abs(share) > IssuePostingRules.MaximumAmount) throw Invalid();
        return new(posting, fee, share);
    }

    private static DateOnly LocalDay(DateTimeOffset value) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, London).DateTime);
    private static ArgumentException Invalid() => new("Cancellation requires complete original, unreturned, in-term posted components without future slices.");
}
