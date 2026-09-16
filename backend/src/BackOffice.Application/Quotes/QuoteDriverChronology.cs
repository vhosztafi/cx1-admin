using System.Text.Json;

namespace BackOffice.Application.Quotes;

public static partial class QuoteDriverRules
{
    private static IReadOnlyList<QuoteFieldIssue> AssessChronologyAndDeclarations(JsonElement proposal, DateOnly asOf)
    {
        if (asOf < new DateOnly(1900, 1, 1)) throw new ArgumentOutOfRangeException(nameof(asOf));
        var issues = new List<QuoteFieldIssue>(); var index = 0;
        void Add(string code, string path, int? question = null)
        {
            if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code, path, question is null ? null : Id(question.Value)));
        }
        foreach (var driver in Items(At(proposal, "risk.drivers")))
        {
            var root = $"/risk/drivers/{index++}";
            var birth = At(driver, "dateOfBirth"); var issued = At(driver, "licence.issuedOn");
            if (Date(Text(birth), out var birthDate) && Date(Text(issued), out var issueDate) &&
                (issueDate < birthDate || Years(birth, issued) is < 17)) Add("licence-before-seventeenth-birthday", root + "/licence/issuedOn", 24);
            var residence = Answer(At(driver, "responses"), root + "/responses", Id(22));
            if (Date(Text(residence.Value), out var residenceDate))
            {
                if (Date(Text(birth), out birthDate) && residenceDate < birthDate) Add("residency-before-birth", residence.Path, 22);
                if (residenceDate > asOf) Add("residency-in-future", residence.Path, 22);
            }
            foreach (var field in new[] { "postcode", "houseNumber", "street", "town", "city", "county" })
                if (Text(At(driver, "address." + field)) is { } text && text.Length > (field == "postcode" ? 10 : 50)) Add("source-text-too-long", root + "/address/" + field);
            var disability = Answer(At(driver, "responses"), root + "/responses", Id(47));
            if (Text(disability.Value) is { Length: > 50 }) Add("source-text-too-long", disability.Path, 47);
            var child = 0;
            foreach (var conviction in Items(At(driver, "convictions")))
            {
                var path = $"{root}/convictions/{child++}"; var band = Text(At(conviction, "declaredBanPeriod"));
                (int Min, int Max)? range = band switch {
                    "none" => (0, 0), "under-3-months" => (1, 2), "3-to-6-months" => (3, 6),
                    "6-to-12-months" => (6, 12), "over-12-months" => (13, 1200), _ => null
                };
                if (range is null) { Add("declared-ban-period-required", path + "/declaredBanPeriod"); continue; }
                var banned = band != "none"; var disqualified = At(conviction, "disqualified").ValueKind;
                if (disqualified != (banned ? JsonValueKind.True : JsonValueKind.False)) Add("conflicting-disqualification-declaration", path + "/disqualified", 38);
                var months = Number(At(conviction, "banMonths"));
                if (banned && months is null) Add("exact-ban-duration-required", path + "/banMonths", 39);
                if (months is not null && (months < range.Value.Min || months > range.Value.Max)) Add("ban-duration-outside-declared-band", path + "/banMonths", 39);
            }
        }
        return issues;
    }
}
