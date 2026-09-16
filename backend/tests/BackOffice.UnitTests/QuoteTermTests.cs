using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteTermTests
{
    [Theory]
    [InlineData("2026-03-29", "01:30", null, "nonexistent-local-time")]
    [InlineData("2026-10-25", "01:30", null, "ambiguous-local-time")]
    [InlineData("2026-07-01", "12:00", 0, "local-offset-mismatch")]
    [InlineData("2026-01-01", "12:00", 60, "local-offset-mismatch")]
    [InlineData("2026-01-01", "12:00", 30, "local-offset-mismatch")]
    [InlineData("2026-02-29", "00:00", null, "invalid-local-time")]
    [InlineData("2026-04-31", "00:00", null, "invalid-local-time")]
    [InlineData("2026-01-01", "24:00", null, "invalid-local-time")]
    [InlineData("2026-01-01", "1:30", null, "invalid-local-time")]
    [InlineData("2026-13-01", "12:00", null, "invalid-local-time")]
    [InlineData(" 2026-01-01", "12:00", null, "invalid-local-time")]
    [InlineData("2026-01-01", "12:00 ", null, "invalid-local-time")]
    public void InvalidOrUnresolvedLocalTimesNeverInventAnInstant(string date, string time, int? offset, string code)
    {
        var result = QuoteTerm.ResolveLondonTime(date, time, offset);
        Assert.Equal(code, result.Code); Assert.Null(result.Instant); Assert.Null(result.UtcOffsetMinutes);
    }

    [Theory]
    [InlineData("2026-10-25", "01:30", 60, "2026-10-25T00:30:00Z")]
    [InlineData("2026-10-25", "01:30", 0, "2026-10-25T01:30:00Z")]
    [InlineData("2028-02-29", "00:00", null, "2028-02-29T00:00:00Z")]
    public void ExplicitRepeatedHourChoicesAndLeapDaysResolve(string date, string time, int? offset, string expected)
    {
        var result = QuoteTerm.ResolveLondonTime(date, time, offset);
        Assert.Null(result.Code); Assert.Equal(DateTimeOffset.Parse(expected), result.Instant);
        Assert.Equal(TimeSpan.Zero, result.Instant!.Value.Offset);
        if (offset is not null) Assert.Equal(offset, result.UtcOffsetMinutes);
    }

    [Fact]
    public void IncompleteAndUnsupportedIntentProducesFieldIssues()
    {
        var missing = QuoteTerm.Assess(); Assert.Null(missing.Term);
        Assert.Equal(new[] { "kind", "localStartDate", "localStartTime", "timeZone" }, missing.Issues.Select(x => x.Path[12..]));
        Assert.All(missing.Issues, x => Assert.Equal("required-term-field", x.Code));
        var input = Annual("2026-01-01"); input["kind"] = "short-period";
        Assert.Equal(new[] { "/termIntent/localEndDate", "/termIntent/localEndTime" }, Assess(input).Issues.Select(x => x.Path));
        input = Annual("2026-01-01"); input["timeZone"] = "UTC";
        Assert.Equal("unsupported-term-zone", Assert.Single(Assess(input).Issues).Code);
        input = Annual("2026-01-01"); input["kind"] = "monthly";
        Assert.Equal("unsupported-term-kind", Assert.Single(Assess(input).Issues).Code);
        input = Annual("2026-01-01"); input["localEndDate"] = "2027-01-01";
        Assert.Equal("annual-end-is-derived", Assert.Single(Assess(input).Issues).Code);
        using var wrong = JsonDocument.Parse("null");
        Assert.Equal("invalid-term-intent", Assert.Single(QuoteTerm.Assess(wrong.RootElement).Issues).Code);
    }

    [Fact]
    public void AnnualTermsUseLocalCalendarAnniversariesAndDoNotMutateIntent()
    {
        var input = Annual("2028-02-29", "12:30"); var before = input.ToJsonString();
        var result = Assess(input); Assert.Empty(result.Issues);
        Assert.Equal(new ResolvedQuoteTerm("annual", DateTimeOffset.Parse("2028-02-29T12:30:00Z"), DateTimeOffset.Parse("2029-02-28T12:30:00Z"), "Europe/London"), result.Term);
        Assert.Equal(before, input.ToJsonString());
        var boundary = Assess(Annual("2026-03-29", "00:30")).Term!;
        Assert.Equal(DateTimeOffset.Parse("2026-03-29T00:30:00Z"), boundary.StartsAt);
        Assert.Equal(DateTimeOffset.Parse("2027-03-28T23:30:00Z"), boundary.EndsAt);
    }

    [Fact]
    public void AnnualEndGapsAndRepeatedHoursRequireResolution()
    {
        var gap = Assess(Annual("2026-03-28", "01:30")); Assert.Null(gap.Term);
        Assert.Equal(new QuoteFieldIssue("annual-end-nonexistent-local-time", "/termIntent/localStartTime"), Assert.Single(gap.Issues));
        var input = Annual("2025-10-25", "01:30");
        Assert.Equal(new QuoteFieldIssue("annual-end-ambiguous-local-time", "/termIntent/endUtcOffsetMinutes"), Assert.Single(Assess(input).Issues));
        input["endUtcOffsetMinutes"] = 0;
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T01:30:00Z"), Assess(input).Term!.EndsAt);
    }

    [Fact]
    public void ShortPeriodChronologyUsesInstantsAcrossRepeatedHours()
    {
        var input = Annual("2026-10-25", "01:45"); input["kind"] = "short-period"; input["utcOffsetMinutes"] = 60;
        input["localEndDate"] = "2026-10-25"; input["localEndTime"] = "01:15"; input["endUtcOffsetMinutes"] = 0;
        var result = Assess(input); Assert.Empty(result.Issues);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Term!.EndsAt - result.Term.StartsAt);
        input["utcOffsetMinutes"] = 0; input["endUtcOffsetMinutes"] = 60;
        Assert.Equal(new QuoteFieldIssue("end-must-follow-start", "/termIntent/localEndTime"), Assert.Single(Assess(input).Issues));
        input["localEndTime"] = "01:45"; input["endUtcOffsetMinutes"] = 0;
        Assert.Null(Assess(input).Term); Assert.Equal("end-must-follow-start", Assert.Single(Assess(input).Issues).Code);
    }

    [Fact]
    public void InvalidOffsetsAndOutOfRangeAnnualEndReturnIssuesWithoutThrowing()
    {
        var input = Annual("2026-01-01"); input["utcOffsetMinutes"] = "0";
        Assert.Equal(new QuoteFieldIssue("local-offset-mismatch", "/termIntent/utcOffsetMinutes"), Assert.Single(Assess(input).Issues));
        input = Annual("9999-01-01"); Assert.Null(Assess(input).Term);
        Assert.Equal(new QuoteFieldIssue("invalid-local-date", "/termIntent/localStartDate"), Assert.Single(Assess(input).Issues));
        input = Annual("2026-02-29"); Assert.Null(Assess(input).Term);
        Assert.Contains(Assess(input).Issues, x => x.Code == "invalid-local-date");
    }

    private static JsonObject Annual(string date, string time = "00:00") => new()
    {
        ["kind"] = "annual", ["localStartDate"] = date, ["localStartTime"] = time, ["timeZone"] = "Europe/London"
    };

    private static QuoteTermAssessment Assess(JsonObject input)
    {
        using var document = JsonDocument.Parse(input.ToJsonString());
        return QuoteTerm.Assess(document.RootElement);
    }
}
