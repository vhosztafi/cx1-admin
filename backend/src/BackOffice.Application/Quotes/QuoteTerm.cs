using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

public sealed record QuoteLocalTime(DateTimeOffset? Instant, int? UtcOffsetMinutes, string? Code);
public sealed record ResolvedQuoteTerm(string Kind, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimeZone);
public sealed record QuoteTermAssessment(ResolvedQuoteTerm? Term, IReadOnlyList<QuoteFieldIssue> Issues);

// Pure assessment of captured intent, after the strict draft boundary. Missing
// answers remain saveable; this result is one input to readiness, not readiness itself.
public static class QuoteTerm
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static QuoteLocalTime ResolveLondonTime(string? date, string? time, int? offset = null)
    {
        if (!Date(date, out var day) || time is not { Length: 5 } ||
            !TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
            return new(null, null, "invalid-local-time");
        var local = day.ToDateTime(clock, DateTimeKind.Unspecified);
        var candidates = new List<QuoteLocalTime>();
        // These are the offsets allowed by the capture contract. Round trips use
        // timezone rules, not assumed transition dates or a machine-local zone.
        foreach (var minutes in new[] { 0, 60 })
        {
            DateTimeOffset instant;
            try { instant = new DateTimeOffset(local, TimeSpan.FromMinutes(minutes)).ToUniversalTime(); }
            catch (ArgumentOutOfRangeException) { continue; }
            if (TimeZoneInfo.ConvertTime(instant, London).DateTime == local)
                candidates.Add(new(instant, minutes, null));
        }
        if (candidates.Count == 0) return new(null, null, "nonexistent-local-time");
        if (offset is not null) return candidates.SingleOrDefault(x => x.UtcOffsetMinutes == offset) ?? new(null, null, "local-offset-mismatch");
        return candidates.Count == 1 ? candidates[0] : new(null, null, "ambiguous-local-time");
    }

    public static QuoteTermAssessment Assess(JsonElement intent = default)
    {
        if (intent.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return new(null, [new("invalid-term-intent", "/termIntent")]);
        var issues = new List<QuoteFieldIssue>();
        void Add(string code, string field) => issues.Add(new(code, "/termIntent/" + field));
        bool Has(string field) => intent.ValueKind == JsonValueKind.Object && intent.TryGetProperty(field, out _);
        string? Text(string field) => Has(field) && intent.GetProperty(field).ValueKind == JsonValueKind.String ? intent.GetProperty(field).GetString() : null;
        foreach (var field in new[] { "kind", "localStartDate", "localStartTime", "timeZone" })
            if (!Has(field)) Add("required-term-field", field);
        var kind = Text("kind");
        if (Has("kind") && kind is not ("annual" or "short-period")) Add("unsupported-term-kind", "kind");
        if (Has("timeZone") && Text("timeZone") != "Europe/London") Add("unsupported-term-zone", "timeZone");
        if (kind == "short-period") foreach (var field in new[] { "localEndDate", "localEndTime" })
                if (!Has(field)) Add("required-term-field", field);
        if (kind == "annual") foreach (var field in new[] { "localEndDate", "localEndTime" })
                if (Has(field)) Add("annual-end-is-derived", field);
        if (issues.Count != 0) return new(null, issues.ToArray());

        QuoteLocalTime Resolve(string? date, string? time, string offsetField)
        {
            if (!Has(offsetField)) return ResolveLondonTime(date, time);
            var value = intent.GetProperty(offsetField);
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var offset)
                ? ResolveLondonTime(date, time, offset) : new(null, null, "local-offset-mismatch");
        }
        var start = Resolve(Text("localStartDate"), Text("localStartTime"), "utcOffsetMinutes");
        if (start.Code is { } startCode) Add(startCode, IsOffsetIssue(startCode) ? "utcOffsetMinutes" : "localStartTime");
        var endDate = Text("localEndDate");
        if (kind == "annual")
        {
            if (!Date(Text("localStartDate"), out var day) || day.Year == 9999)
            {
                Add("invalid-local-date", "localStartDate");
                return new(null, issues.ToArray());
            }
            endDate = day.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        var end = Resolve(endDate, Text(kind == "annual" ? "localStartTime" : "localEndTime"), "endUtcOffsetMinutes");
        if (end.Code is { } endCode) Add(kind == "annual" ? "annual-end-" + endCode : endCode,
            IsOffsetIssue(endCode) ? "endUtcOffsetMinutes" : kind == "annual" ? "localStartTime" : "localEndTime");
        if (issues.Count != 0) return new(null, issues.ToArray());
        if (end.Instant <= start.Instant) return new(null, [new("end-must-follow-start", "/termIntent/localEndTime")]);
        return new(new(kind!, start.Instant!.Value, end.Instant!.Value, "Europe/London"), []);
    }

    private static bool IsOffsetIssue(string code) => code is "ambiguous-local-time" or "local-offset-mismatch";
    private static bool Date(string? text, out DateOnly value)
    {
        value = default;
        return text is { Length: 10 } && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
