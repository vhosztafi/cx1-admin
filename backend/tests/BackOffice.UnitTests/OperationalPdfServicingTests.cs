using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalPdfServicingTests
{
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DocumentRenderInput Input(bool renewal)
    {
        using var stream = typeof(OperationalPdfServicingTests).Assembly.GetManifestResourceStream("QuoteExamples.quote-capture-motor-trade-road-risks.json")!;
        var proposal = JsonNode.Parse(stream)!["proposal"]!;
        var templateId = Guid.NewGuid();
        var template = JsonSerializer.SerializeToElement(new { format = renewal ? "renewal-template-1" : "servicing-template-1", title = "Proposed policy terms", notice = "Fictional proposed cover only." });
        var date = "2026-10-01T00:00:00Z";
        var source = JsonSerializer.Serialize(new {
            format = renewal ? "renewal-contract-1" : "servicing-contract-1", draftId = Guid.NewGuid(), cycleId = Guid.NewGuid(), revisionId = Guid.NewGuid(),
            baseVersionId = Guid.NewGuid(), baseTermId = Guid.NewGuid(), policyId = Guid.NewGuid(), ratingId = Guid.NewGuid(), templateVersionId = templateId,
            inputHash = new string('a', 64), ratingHash = new string('b', 64), effectiveDates = new[] { date }, slices = new[] { new { effectiveAt = date, proposal } },
            template, ratingInput = new { }, rating = new { }, commercialTerms = new { },
            conditions = Array.Empty<object>(), expiresAt = "2026-09-30T00:00:00Z",
            price = new { currency = "GBP", premium = renewal ? "1000.00" : "-100.00", tax = renewal ? "120.00" : "-12.00", fee = "0.00", brokerCommission = renewal ? "100.00" : "-10.00", grossPayable = renewal ? "1120.00" : "-112.00", netDue = renewal ? "1020.00" : "-102.00" }, documentState = "structured-payload"
        });
        var templateJson = template.GetRawText();
        return new(Guid.NewGuid(), "servicing-terms", source, Hash(source), templateId, templateJson, Hash(templateJson), "motor-trade-road-risks",
            renewal ? "renewal-invitation" : "quotation", "PL-DEMO-1", "motor-trade-road-risks", renewal ? "renewal-invitation" : "servicing-terms");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProposedServicingTermsRetainEffectiveSlicesAndSignedPrice(bool renewal)
    {
        var input = Input(renewal); var contract = DocumentRenderContract.Create(input);
        var sections = DocumentPolicyProjection.Create(contract);
        Assert.Contains(sections.SelectMany(x => x.Fields), x => x.Value == (renewal ? "£1,120.00" : "£-112.00"));
        Assert.Contains(sections, x => x.Title.Contains("2026-10-01", StringComparison.Ordinal));
        Assert.Contains(sections.SelectMany(x => x.Fields), x => x.Value == "Jamie Example");
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("template")]
    [InlineData("product")]
    [InlineData("effective-date")]
    [InlineData("empty-slices")]
    [InlineData("unknown-field")]
    [InlineData("price")]
    [InlineData("certificate")]
    public void ServicingTermsRejectSubstitutionAndDoNotIssueCover(string mutation)
    {
        var input = Input(true); var source = JsonNode.Parse(input.SourceJson)!;
        switch (mutation)
        {
            case "template": source["templateVersionId"] = Guid.NewGuid(); break;
            case "product": source["slices"]![0]!["proposal"]!["productCode"] = "commercial-combined"; break;
            case "effective-date": source["effectiveDates"]![0] = "2026-10-02T00:00:00Z"; break;
            case "empty-slices": source["slices"] = new JsonArray(); break;
            case "unknown-field": source["script"] = "execute()"; break;
            case "price": source["price"]!["premium"] = "Unknown"; break;
            case "certificate": input = input with { Kind = "policy-certificate", TemplateKind = "policy-certificate" }; break;
        }
        var json = source.ToJsonString(); input = input with { SourceJson = json, SourceHash = mutation == "hash" ? new string('c', 64) : Hash(json) };
        Assert.Throws<DocumentRenderException>(() => DocumentRenderContract.Create(input));
    }
}
