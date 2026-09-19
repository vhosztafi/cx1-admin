using System.Text.Json;

namespace BackOffice.Application.Quotes;

public sealed record QuoteDraftValidation(CanonicalQuoteInput? Input, IReadOnlyList<QuoteFieldIssue> Issues);

// Save-shape and identity gate for partial drafts. No authority or semantic
// readiness is granted: valid-but-ineligible selections may remain as declared
// draft data and must produce readiness issues rather than being silently erased.
public static class QuoteCaptureBoundary
{
    public static QuoteDraftValidation Validate(string text, QuoteVersionPins pins)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pins);
        if (CommercialCaptureRules.Accepts(pins.SchemaVersion, pins.QuestionSetVersion, pins.ReferenceVersion))
            return CommercialCaptureRules.Validate(text, pins);
        if (pins.SchemaVersion != "1.0" || pins.QuestionSetVersion != QuoteCatalogueIdentity.Version || pins.ReferenceVersion != QuoteCatalogueIdentity.Version)
            throw new InvalidOperationException("Pinned quote capture configuration is unavailable.");
        CanonicalQuoteInput canonical;
        try { canonical = QuoteCanonicalJson.Create(text, pins); }
        catch (QuoteInputException error) { return new(null, [new(error.Code, "")]); }
        using var document = JsonDocument.Parse(canonical.Json);
        var proposal = document.RootElement;
        var issues = QuoteCaptureShape.Validate(proposal);
        if (issues.Count > 0) return new(null, issues);
        issues = QuoteCatalogueIdentity.ValidateQuestions(proposal);
        if (issues.Count > 0) return new(null, issues);
        issues = QuoteCatalogueIdentity.ValidateReferences(proposal);
        if (issues.Count > 0) return new(null, issues);
        issues = QuoteItemIdentity.Validate(proposal);
        return issues.Count > 0 ? new(null, issues.Take(QuoteCaptureShape.MaximumIssues).ToArray()) : new(canonical, []);
    }
}
