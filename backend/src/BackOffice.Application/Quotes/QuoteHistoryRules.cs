using System.Text.Json;

namespace BackOffice.Application.Quotes;

public sealed record QuoteHistoryIssue(string Code, string Path, string? QuestionId = null, string? RelatedPath = null);

public static partial class QuoteDriverRules
{
    // The API supplies the trusted assessment date. Policy inception and client
    // clocks must not shorten the lookback period or admit future incidents.
    public static IReadOnlyList<QuoteHistoryIssue> AssessHistory(JsonElement proposal, DateOnly asOf)
    {
        if (asOf < new DateOnly(1900, 1, 1)) throw new ArgumentOutOfRangeException(nameof(asOf));
        var issues = new List<QuoteHistoryIssue>();
        var groups = new (string Key, string Suffix, int? Years)[] {
            ("convictions", "ef70e80708bb", 5), ("losses", "36da21d3c935", 3),
            ("countyCourtJudgments", "922ca15dc9ed", 5), ("criminalConvictions", "46414cc10100", null)
        };
        var driverIndex = 0;
        foreach (var driver in Items(At(proposal, "risk.drivers")))
        {
            var root = $"/risk/drivers/{driverIndex++}";
            foreach (var (key, suffix, years) in groups)
            {
                var child = 0;
                foreach (var row in Items(At(driver, key)))
                {
                    if (issues.Count >= QuoteCaptureShape.MaximumIssues) return issues;
                    var path = $"{root}/{key}/{child++}";
                    if (!Date(Text(At(row, "occurredOn")), out var occurred)) continue;
                    if (occurred > asOf) { issues.Add(new("history-date-after-assessment", path + "/occurredOn")); continue; }
                    var pending = Answer(At(row, "responses"), path + "/responses", "prototype.addconv.prosecution-status");
                    if (years is { } lookback && occurred < asOf.AddYears(-lookback) &&
                        !(key == "convictions" && QuoteCatalogueIdentity.TrustedValue(pending.Value, "prototype.addconv.prosecution-status") == 2)) continue;
                    var question = "prototype.quote." + suffix;
                    var answer = Answer(At(proposal, "risk.business.responses"), "/risk/business/responses", question);
                    if (answer.Value.ValueKind != JsonValueKind.True) issues.Add(new("history-declaration-required", answer.Path, question, path));
                }
            }
        }
        return issues;
    }
}
