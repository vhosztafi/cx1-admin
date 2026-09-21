using System.Text.Json.Serialization;

namespace BackOffice.Application.Operations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentSourceInput([property: JsonRequired] string Kind, Guid? PolicyVersionId = null,
    Guid? QuoteRevisionId = null, Guid? TermsVersionId = null, Guid? QuoteTermsVersionId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DocumentGenerateInput([property: JsonRequired] string Kind, [property: JsonRequired] DocumentSourceInput Source,
    [property: JsonRequired] Guid TemplateVersionId, [property: JsonRequired] string Visibility,
    [property: JsonRequired] string Reason, Guid? RelationshipId = null, Guid? DocumentId = null);

public sealed class DocumentRuleException(string code) : Exception("The document command is invalid.")
{
    public string Code { get; } = code;
}

// Shape validation never grants access: SQL callers must hold current parent,
// source, template and audience ownership before command replay/application.
public static class DocumentRules
{
    public static void Validate(DocumentGenerateInput input)
    {
        if (input is null || input.Source is null || input.TemplateVersionId == Guid.Empty || input.DocumentId == Guid.Empty ||
            input.Kind is not ("quotation" or "statement-of-fact" or "policy-schedule" or "policy-certificate" or "endorsement" or "renewal-invitation" or "cancellation-notice"))
            throw Invalid("invalid-document-command");
        ValidateAudience(input.Visibility, input.RelationshipId);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 1000 || input.Reason.Any(char.IsControl))
            throw Invalid("document-reason-required");
        var source = input.Source;
        var valid = source.Kind switch
        {
            "policy-version" => Present(source.PolicyVersionId) && source.QuoteRevisionId is null && source.TermsVersionId is null && source.QuoteTermsVersionId is null &&
                input.Kind is "statement-of-fact" or "policy-schedule" or "policy-certificate" or "endorsement" or "cancellation-notice",
            "quote-revision" => Present(source.QuoteRevisionId) && source.PolicyVersionId is null && source.TermsVersionId is null &&
                (source.QuoteTermsVersionId is null || Present(source.QuoteTermsVersionId)) &&
                (input.Kind == "statement-of-fact" || input.Kind == "quotation" && Present(source.QuoteTermsVersionId)),
            "servicing-terms" => Present(source.TermsVersionId) && source.PolicyVersionId is null && source.QuoteRevisionId is null && source.QuoteTermsVersionId is null &&
                input.Kind is "statement-of-fact" or "quotation" or "renewal-invitation",
            _ => false
        };
        if (!valid) throw Invalid("invalid-document-source");
    }

    public static void ValidateAudience(string visibility, Guid? relationshipId)
    {
        if (!(visibility switch { "internal" or "insurer" => relationshipId is null, "agency" => Present(relationshipId), _ => false }))
            throw Invalid("invalid-document-audience");
    }

    private static bool Present(Guid? value) => value is Guid id && id != Guid.Empty;
    private static DocumentRuleException Invalid(string code) => new(code);
}
