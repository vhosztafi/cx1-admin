using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Operations;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalPdfQuoteTests
{
    private static DocumentRenderInput Quote()
    {
        using var stream = typeof(OperationalPdfQuoteTests).Assembly.GetManifestResourceStream("QuoteExamples.quote-capture-motor-trade-road-risks.json")!;
        var proposal = JsonNode.Parse(stream)!["proposal"]!.ToJsonString();
        var pins = new QuoteVersionPins(Guid.NewGuid(), Guid.NewGuid(), "1.0", "mt-capture-57b711ca02317ca0", "mt-capture-57b711ca02317ca0");
        var canonical = QuoteCanonicalJson.Create(proposal, pins);
        const string template = "{\"format\":\"quote-template-1\",\"title\":\"Motor Trade statement of fact\",\"notice\":\"Fictional proposed cover.\"}";
        return new(Guid.NewGuid(), "quote-revision", canonical.Json, canonical.ContentHash, Guid.NewGuid(), template,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(template))), "motor-trade-road-risks", "statement-of-fact", "QT-DEMO-1", "motor-trade-road-risks", "quote-terms", pins);
    }

    [Fact]
    public void QuoteStatementVerifiesConfigurationBoundHashAndPrintsProposedDeclarations()
    {
        var input = Quote(); var contract = DocumentRenderContract.Create(input);
        Assert.Equal(input.SourceHash, contract.Input.SourceHash);
        var sections = DocumentPolicyProjection.Create(contract);
        Assert.Contains(sections, x => x.Title == "Proposed period");
        Assert.DoesNotContain(sections, x => x.Title == "Selected cover");
        Assert.Contains(sections.SelectMany(x => x.Fields), x => x.Value == "Jamie Example");
    }

    [Theory]
    [InlineData("plain-hash")]
    [InlineData("changed-pins")]
    [InlineData("missing-pins")]
    [InlineData("certificate")]
    [InlineData("unpriced-quotation")]
    public void QuoteCannotSubstituteConfigurationOrClaimIssuedOrPricedCover(string mutation)
    {
        var input = Quote();
        input = mutation switch
        {
            "plain-hash" => input with { SourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.SourceJson))) },
            "changed-pins" => input with { QuotePins = input.QuotePins! with { ProductVersionId = Guid.NewGuid() } },
            "missing-pins" => input with { QuotePins = null },
            "certificate" => input with { Kind = "policy-certificate", TemplateKind = "policy-certificate" },
            _ => input with { Kind = "quotation" }
        };
        Assert.Throws<DocumentRenderException>(() => DocumentRenderContract.Create(input));
    }

    private static DocumentRenderInput Priced()
    {
        var input = Quote() with { Kind = "quotation" };
        using var source = JsonDocument.Parse(input.SourceJson);
        using var template = JsonDocument.Parse(input.TemplateJson);
        var cycle = Guid.NewGuid(); var rating = Guid.NewGuid(); var root = source.RootElement;
        var payload = JsonSerializer.SerializeToElement(new {
            format = "quote-contract-1", quoteId = Guid.NewGuid(), cycleId = cycle, revisionId = input.SourceId, revisionHash = input.SourceHash,
            ratingId = rating, templateVersionId = input.TemplateId, template = template.RootElement,
            clientId = Guid.NewGuid(), relationshipId = Guid.NewGuid(), insuredName = "Alex Example", agencyName = "Fictional agency",
            productVersionId = input.QuotePins!.ProductVersionId, agencyTermsVersionId = input.QuotePins.AgencyTermsVersionId,
            productCode = input.ProductCode, risk = root.GetProperty("risk"), cover = root.GetProperty("cover"), termIntent = root.GetProperty("termIntent"),
            startsAt = "2026-09-15T08:00:00Z", endsAt = "2027-09-15T08:00:00Z", expiresAt = "2026-09-14T08:00:00Z",
            conditions = new[] { new { id = Guid.NewGuid(), code = "demo-security", kind = "warranty", wording = "Keep the reviewed alarm active.", endorsementCode = (string?)null, decisionId = Guid.NewGuid(), definition = new { code = "demo-security" } } },
            rating = new { format = "retained-demo-rating" }, price = new { currency = "GBP", annualPremium = "1000.00", termPremium = "1000.00", tax = "120.00", fee = "20.00", grossPayable = "1140.00", brokerCommission = "100.00" },
            settlement = new { collector = "agency", mode = "net", commissionRateBps = 1000, feeShareBps = 0 }, documentState = "structured-payload"
        });
        return input with { QuoteTerms = new(Guid.NewGuid(), payload.GetRawText(), UnderwritingHashes.Terms(cycle, rating, input.QuotePins, payload)) };
    }

    [Fact]
    public void QuotationUsesExactRetainedTermsPriceAndConditions()
    {
        var input = Priced(); var contract = DocumentRenderContract.Create(input);
        var fields = DocumentPolicyProjection.Create(contract).SelectMany(x => x.Fields).ToArray();
        Assert.Contains(fields, x => x.Value == "£1,140.00");
        Assert.Contains(fields, x => x.Value == "Keep the reviewed alarm active.");
        Assert.NotNull(contract.QuotationTerms);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("revision")]
    [InlineData("template")]
    [InlineData("risk")]
    [InlineData("price")]
    [InlineData("field")]
    public void QuotationRejectsRehashedButMismatchedOrMalformedTerms(string mutation)
    {
        var input = Priced(); var terms = JsonNode.Parse(input.QuoteTerms!.Json)!;
        switch (mutation)
        {
            case "revision": terms["revisionId"] = Guid.NewGuid(); break;
            case "template": terms["templateVersionId"] = Guid.NewGuid(); break;
            case "risk": terms["risk"]!["business"]!["description"] = "Changed after approval"; break;
            case "price": terms["price"]!["grossPayable"] = "invented"; break;
            case "field": terms["externalAsset"] = "https://example.test/hidden"; break;
        }
        var value = JsonSerializer.SerializeToElement(terms);
        var hash = mutation == "hash" ? new string('a', 64) : UnderwritingHashes.Terms(value.GetProperty("cycleId").GetGuid(), value.GetProperty("ratingId").GetGuid(), input.QuotePins!, value);
        input = input with { QuoteTerms = input.QuoteTerms with { Json = value.GetRawText(), Hash = hash } };
        Assert.Throws<DocumentRenderException>(() => DocumentRenderContract.Create(input));
    }
}
