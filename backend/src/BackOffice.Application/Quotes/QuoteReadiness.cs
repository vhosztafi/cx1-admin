using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackOffice.Application.Quotes;

public sealed record QuoteReadinessIssue(string Path, string Code, string Message, string Category, string Severity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QuestionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RelatedPath = null);
public sealed record QuoteReadinessResult(Guid QuoteId, Guid RevisionId, bool Ready, IReadOnlyList<QuoteReadinessIssue> Issues);

public static class QuoteReadiness
{
    public static QuoteReadinessResult Assess(Guid quoteId, Guid revisionId, JsonElement proposal,
        QuoteTermAssessment term, string? captureUnavailableCode, DateOnly asOf)
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
        issues.AddRange(QuoteDriverRules.Assess(proposal, asOf).Select(x => new QuoteReadinessIssue(
            x.Path, x.Code, DriverMessage(x.Code), "capture", "error", x.QuestionId)));
        issues.AddRange(QuoteDriverRules.AssessDynamic(proposal).Issues.Select(x => new QuoteReadinessIssue(
            x.Path, x.Code, "Review the selected cover, driver age and licence experience.", "capture", "error", x.QuestionId)));
        issues.AddRange(QuoteVehicleOwnership.Assess(proposal).Select(x => new QuoteReadinessIssue(
            x.Path, x.Code, "Review the vehicle owner and the named driver's personal vehicle cover.", "capture", "error", x.QuestionId)));
        issues.AddRange(QuoteDriverRules.AssessHistory(proposal, asOf).Select(x => new QuoteReadinessIssue(
            x.Path, x.Code, x.Code == "history-date-after-assessment" ? "The incident date is after the assessment date." : "Recorded history requires a Yes answer to this business declaration.", "capture", "error", x.QuestionId, x.RelatedPath)));
        issues.AddRange(QuoteCaptureShape.ValidateCompleteness(proposal).Select(x => new QuoteReadinessIssue(
            x.Path.Length is > 0 and <= 500 ? x.Path : "/", x.Code, "Complete or correct the captured details.", "capture", "error")));
        return new(quoteId, revisionId, false, issues.Distinct().Take(100).ToArray());
    }
    private static string DriverMessage(string code) => code switch
    {
        "driver-relationship-ineligible" => "This relationship is not available for the declared company category.",
        "spouse-under-25" => "Spouse cover requires age 25 or above at policy inception.",
        "duplicate-policyholder-driver" => "Only one named driver can be the policyholder.",
        "motorcycle-over-1000-ineligible" => "This motorcycle option requires age 30 and two complete years of motorcycle licence experience.",
        "personal-cover-motor-trade-only" or "other-cover-motor-trade-only" => "This cover cannot be combined with motor-trade-only usage.",
        "personal-cover-required-for-relationship" or "other-cover-required-for-relationship" => "Review this cover declaration for the selected relationship and usage.",
        "other-cover-under-21" => "Driving other vehicles requires age 21 or above at policy inception.",
        "driver-ban-active-at-policy-start" => "The recorded driving ban has not expired at policy inception.",
        "conflicting-driver-name" => "The full name does not agree with the separate first name and surname.",
        "conflicting-driver-employment" => "The two motor trade employment declarations do not agree.",
        "conflicting-driver-years" => "The declared years do not match complete years at policy inception.",
        "inactive-driver-history-retained" or "inactive-prototype-other-occupation" => "Retained details conflict with the controlling answer. Review the answer or remove the details explicitly.",
        "inactive-named-drivers-retained" => "The any-driver-only basis retains named driver rows. Review the basis or remove the rows explicitly.",
        "inactive-any-driver-answer-retained" => "Any-driver restrictions remain recorded for a different driver basis. Review or clear them explicitly.",
        "driver-history-required" => "Add the history declared by this answer.",
        "licence-before-seventeenth-birthday" => "The licence issue date must be on or after the driver's seventeenth birthday.",
        "residency-before-birth" => "UK residency cannot start before the recorded date of birth.",
        "residency-in-future" => "UK residency cannot start after the assessment date.",
        "ban-duration-outside-declared-band" => "The exact ban duration does not match the declared band.",
        "conflicting-disqualification-declaration" => "The disqualification answer does not agree with the declared ban period.",
        "driver-age-out-of-range" => "Named drivers must be between 17 and 85 at policy inception.",
        "provisional-licence-not-covered" => "Provisional licences are not covered by this product.",
        "young-driver-licence-experience" => "A driver under 25 needs at least one complete year of licence experience.",
        _ => "Complete or correct the driver details."
    };
}
