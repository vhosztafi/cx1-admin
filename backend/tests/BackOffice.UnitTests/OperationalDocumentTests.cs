using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalDocumentTests
{
    private static DocumentGenerateInput Input() => new("policy-schedule", new("policy-version", PolicyVersionId: Guid.NewGuid()), Guid.NewGuid(), "internal", "Generate retained policy schedule");

    [Fact]
    public void ExactPolicySourceAndExplicitRegenerationAreAccepted()
    {
        var input = Input(); DocumentRules.Validate(input);
        DocumentRules.Validate(input with { DocumentId = Guid.NewGuid() });
    }

    [Theory]
    [InlineData("mixed-source")]
    [InlineData("missing-source")]
    [InlineData("empty-template")]
    [InlineData("unknown-kind")]
    [InlineData("internal-relationship")]
    [InlineData("agency-missing-relationship")]
    [InlineData("blank-reason")]
    [InlineData("control-reason")]
    [InlineData("empty-document")]
    public void InvalidSourceAndAudienceCommandsFailBeforePersistence(string mutation)
    {
        var input = Input();
        input = mutation switch
        {
            "mixed-source" => input with { Source = input.Source with { QuoteRevisionId = Guid.NewGuid() } },
            "missing-source" => input with { Source = new("policy-version") },
            "empty-template" => input with { TemplateVersionId = Guid.Empty },
            "unknown-kind" => input with { Kind = "arbitrary-html" },
            "internal-relationship" => input with { RelationshipId = Guid.NewGuid() },
            "agency-missing-relationship" => input with { Visibility = "agency" },
            "blank-reason" => input with { Reason = " " },
            "control-reason" => input with { Reason = "change\u0000source" },
            _ => input with { DocumentId = Guid.Empty }
        };
        Assert.Throws<DocumentRuleException>(() => DocumentRules.Validate(input));
    }

    [Fact]
    public void QuotationRequiresExactTermsAndCannotIssueCertificate()
    {
        var input = Input() with { Kind = "quotation", Source = new("quote-revision", QuoteRevisionId: Guid.NewGuid()) };
        Assert.Throws<DocumentRuleException>(() => DocumentRules.Validate(input));
        input = input with { Source = input.Source with { QuoteTermsVersionId = Guid.NewGuid() } };
        DocumentRules.Validate(input);
        Assert.Throws<DocumentRuleException>(() => DocumentRules.Validate(input with { Kind = "policy-certificate" }));
        DocumentRules.Validate(input with { Kind = "statement-of-fact" });
    }

    [Fact]
    public void ServicingSourceCannotCreateIssuedCoverAndAgencyAudienceRequiresRelationship()
    {
        var input = Input() with { Kind = "renewal-invitation", Source = new("servicing-terms", TermsVersionId: Guid.NewGuid()), Visibility = "agency", RelationshipId = Guid.NewGuid() };
        DocumentRules.Validate(input);
        Assert.Throws<DocumentRuleException>(() => DocumentRules.Validate(input with { Kind = "policy-schedule" }));
        Assert.Throws<DocumentRuleException>(() => DocumentRules.Validate(input with { Source = input.Source with { QuoteTermsVersionId = Guid.NewGuid() } }));
    }
}
