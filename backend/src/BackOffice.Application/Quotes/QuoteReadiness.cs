using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackOffice.Application.Quotes;

public sealed record QuoteReadinessIssue(string Path, string Code, string Message, string Category, string Severity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QuestionId = null);
public sealed record QuoteReadinessResult(Guid QuoteId, Guid RevisionId, bool Ready, IReadOnlyList<QuoteReadinessIssue> Issues);

public static class QuoteReadiness
{
    public static QuoteReadinessResult Assess(Guid quoteId, Guid revisionId, JsonElement proposal,
        QuoteTermAssessment term, string? captureUnavailableCode)
    {
        // Capture APIs precede semantic sections (05-03..06), evidence (05-08)
        // and matching (05-10). Never report readiness until those gates run.
        // This server-owned blocker cannot be cleared by captured answers.
        var issues = new List<QuoteReadinessIssue>
        {
            new("/", "quote-assessment-unavailable", "The quote cannot yet be assessed for progression.", "configuration", "error")
        };
        if (captureUnavailableCode is not null)
            issues.Add(new("/", captureUnavailableCode, "Capture is unavailable for this quote. Review its current status and product access.", "eligibility", "error"));
        issues.AddRange(term.Issues.Select(x => new QuoteReadinessIssue(x.Path, x.Code, "Complete or correct the policy term.", "capture", "error")));
        issues.AddRange(QuoteBusinessRules.Assess(proposal).Select(x => new QuoteReadinessIssue(
            x.Path, x.Code, "Complete or correct the business details.", "capture", "error", x.QuestionId)));
        issues.AddRange(QuoteDriverRules.Assess(proposal).Select(x => new QuoteReadinessIssue(
            x.Path, x.Code, "Complete or correct the driver details.", "capture", "error", x.QuestionId)));
        issues.AddRange(QuoteCaptureShape.ValidateCompleteness(proposal).Select(x => new QuoteReadinessIssue(
            x.Path.Length is > 0 and <= 500 ? x.Path : "/", x.Code, "Complete or correct the captured details.", "capture", "error")));
        return new(quoteId, revisionId, false, issues.Distinct().Take(100).ToArray());
    }
}
