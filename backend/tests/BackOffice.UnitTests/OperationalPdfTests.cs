using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Operations;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalPdfTests
{
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DocumentRenderInput Input(string kind = "policy-schedule", bool commercial = false)
    {
        using var stream = typeof(OperationalPdfTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-motor-trade-road-risks.json")!;
        var source = commercial ? CommercialIssueSnapshotShapeTests.Snapshot().ToJsonString() : JsonNode.Parse(stream)!.ToJsonString();
        var product = commercial ? "commercial-combined" : "motor-trade-road-risks";
        var template = JsonSerializer.Serialize(new { format = "document-template-1", productCode = product, kind,
            title = "Fictional cover", notice = "Demonstration only. No insurance is provided." });
        return new(Guid.NewGuid(), "policy-version", source, Hash(source), Guid.NewGuid(), template, Hash(template), product, kind, "PL-DEMO-001", product, kind);
    }
    private static DocumentRenderInput ChangeSource(DocumentRenderInput input, Action<JsonNode> change)
    {
        var node = JsonNode.Parse(input.SourceJson)!; change(node); var json = node.ToJsonString();
        return input with { SourceJson = json, SourceHash = Hash(json) };
    }

    [Fact]
    public void ExactSourceAndTemplateSurviveValidationWithoutRewriting()
    {
        var input = Input(); var contract = DocumentRenderContract.Create(input);
        Assert.Equal(input, contract.Input);
        Assert.Equal("Fictional cover", contract.Title);
        Assert.False(contract.LegacyTemplate);
        Assert.Equal("Alex", contract.Source.GetProperty("insured").GetProperty("firstName").GetString());
    }

    [Theory]
    [InlineData("source-hash")]
    [InlineData("template-hash")]
    [InlineData("source-id")]
    [InlineData("template-id")]
    [InlineData("source-kind")]
    [InlineData("product")]
    [InlineData("kind")]
    [InlineData("source-shape")]
    [InlineData("duplicate-source")]
    [InlineData("duplicate-template")]
    [InlineData("template-kind")]
    [InlineData("template-product")]
    [InlineData("template-code")]
    [InlineData("template-markup")]
    [InlineData("template-url")]
    [InlineData("template-length")]
    [InlineData("reference-control")]
    [InlineData("template-owner")]
    [InlineData("template-row-kind")]
    public void RejectsUntrustedOrMismatchedRenderInputs(string mutation)
    {
        var input = Input(); var template = JsonNode.Parse(input.TemplateJson)!;
        switch (mutation)
        {
            case "source-hash": input = input with { SourceHash = new string('0', 64) }; break;
            case "template-hash": input = input with { TemplateHash = new string('0', 64) }; break;
            case "source-id": input = input with { SourceId = Guid.Empty }; break;
            case "template-id": input = input with { TemplateId = Guid.Empty }; break;
            case "source-kind": input = input with { SourceKind = "current-policy" }; break;
            case "product": input = input with { ProductCode = "commercial-combined" }; break;
            case "kind": input = input with { Kind = "arbitrary" }; break;
            case "source-shape": input = ChangeSource(input, n => n["risk"] = new JsonObject()); break;
            case "duplicate-source":
                var duplicate = input.SourceJson.Insert(1, "\"productCode\":\"commercial-combined\",");
                input = input with { SourceJson = duplicate, SourceHash = Hash(duplicate) }; break;
            case "duplicate-template":
                var duplicateTemplate = input.TemplateJson.Insert(1, "\"title\":\"Other title\",");
                input = input with { TemplateJson = duplicateTemplate, TemplateHash = Hash(duplicateTemplate) }; break;
            case "template-kind": template["kind"] = "policy-certificate"; break;
            case "template-product": template["productCode"] = "commercial-combined"; break;
            case "template-code": template["script"] = "execute()"; break;
            case "template-markup": template["title"] = "<script>execute()</script>"; break;
            case "template-url": template["logoUrl"] = "https://example.test/logo.png"; break;
            case "template-length": template["notice"] = new string('x', 4001); break;
            case "reference-control": input = input with { Reference = "PL\nForged" }; break;
            case "template-owner": input = input with { TemplateProductCode = "commercial-combined" }; break;
            case "template-row-kind": input = input with { TemplateKind = "policy-certificate" }; break;
        }
        if (mutation.StartsWith("template-", StringComparison.Ordinal) && mutation != "template-hash" && mutation != "template-id")
        {
            var json = template.ToJsonString(); input = input with { TemplateJson = json, TemplateHash = Hash(json) };
        }
        Assert.Throws<DocumentRenderException>(() => DocumentRenderContract.Create(input));
    }

    [Fact]
    public void LegacyPendingNoticeIsRetainedAsProvenance()
    {
        var input = Input(); const string template = "{\"format\":\"policy-template-1\",\"title\":\"Historical schedule\",\"notice\":\"Document generation is requested and remains pending.\"}";
        input = input with { TemplateJson = template, TemplateHash = Hash(template) };
        var contract = DocumentRenderContract.Create(input);
        Assert.True(contract.LegacyTemplate);
        Assert.Equal(template, contract.Input.TemplateJson);
        Assert.Equal("Document generation is requested and remains pending.", contract.Notice);
    }

    [Fact]
    public void EmployersCertificateRequiresSelectedSectionNotJustDeclaredLimit()
    {
        var input = Input("policy-certificate", true);
        Assert.Throws<DocumentRenderException>(() => DocumentRenderContract.Create(input));
        input = ChangeSource(input, n => n["cover"]!["sections"]!.AsArray().Add(JsonSerializer.SerializeToNode(new {
            id = Guid.NewGuid(), code = "employers-liability", limit = "10000000.00", targetIds = Array.Empty<string>() })));
        Assert.Equal("commercial-combined", DocumentRenderContract.Create(input).Input.ProductCode);
    }

    [Theory]
    [InlineData("policy-schedule")]
    [InlineData("policy-certificate")]
    [InlineData("policy-statement")]
    [InlineData("endorsement")]
    public void CancelledSourceCannotRenderActiveCover(string kind)
    {
        var input = Cancelled(Input(kind));
        Assert.Throws<DocumentRenderException>(() => DocumentRenderContract.Create(input));
    }

    [Fact]
    public void CancellationNoticeRequiresAnExactCancellationSnapshot()
    {
        var input = Input("cancellation-notice");
        Assert.Throws<DocumentRenderException>(() => DocumentRenderContract.Create(input));
        Assert.Equal("cancelled", DocumentRenderContract.Create(Cancelled(input)).Source.GetProperty("cancellation").GetProperty("outcome").GetString());
    }

    private static DocumentRenderInput Cancelled(DocumentRenderInput input)
    {
        using var parsed = JsonDocument.Parse(input.SourceJson);
        var date = parsed.RootElement.GetProperty("term").GetProperty("startsAt").GetDateTimeOffset().AddDays(1);
        var json = CancellationIssueSnapshot.Create(parsed.RootElement, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            input.SourceId, Guid.NewGuid(), Guid.NewGuid(), date, date, new string('a',64), "insured-request", "demo-servicing-1"));
        return input with { SourceJson = json, SourceHash = Hash(json) };
    }

    [Fact]
    public void StatementUsesFrozenReadableQuestionsAndExactSavedAnswers()
    {
        var sections = DocumentPolicyProjection.Create(DocumentRenderContract.Create(Input("statement-of-fact")));
        var fields = sections.SelectMany(x => x.Fields).ToArray();
        Assert.Contains(fields, x => x.Label.Contains("Cover Level:", StringComparison.Ordinal) && x.Value == "Comprehensive");
        Assert.Contains(fields, x => x.Label.Contains("Has anyone on this policy", StringComparison.Ordinal) && x.Value == "No");
        Assert.Contains(fields, x => x.Value == "Jamie Example");
        Assert.DoesNotContain(fields, x => x.Label.Contains("prototype.", StringComparison.Ordinal) || x.Label == "Client Id");
        Assert.DoesNotContain(sections, x => x.Title == "Premium and charges");
    }

    [Fact]
    public void ScheduleSeparatesSelectedCoverFromRequestedOptionsAndFormatsMoney()
    {
        var sections = DocumentPolicyProjection.Create(DocumentRenderContract.Create(Input()));
        Assert.Contains(sections, x => x.Title == "Selected cover" && x.Fields.Any(f => f.Value == "£10,000.00"));
        Assert.Contains(sections, x => x.Title == "Requested options (declarations)" && x.Fields.Any(f => f.Value == "No"));
        Assert.Contains(sections, x => x.Title == "Premium and charges");
        Assert.Contains(sections.SelectMany(x => x.Fields), x => x.Label == "Wage Roll" && x.Value == "£25,000.00");
    }

    [Fact]
    public void CommercialProjectionHasPropertyDeclarationsAndNoMotorSections()
    {
        var sections = DocumentPolicyProjection.Create(DocumentRenderContract.Create(Input(commercial: true)));
        Assert.Contains(sections, x => x.Title == "Locations");
        Assert.Contains(sections, x => x.Title == "Wages");
        Assert.DoesNotContain(sections, x => x.Title is "Drivers" or "Vehicles" or "Motor cover");
    }

    [Fact]
    public void CancellationDoesNotPresentRetainedPremiumAsARefundOrCoverAsActive()
    {
        var sections = DocumentPolicyProjection.Create(DocumentRenderContract.Create(Cancelled(Input("cancellation-notice"))));
        Assert.Contains(sections, x => x.Title == "Cancellation");
        Assert.DoesNotContain(sections, x => x.Title is "Selected cover" or "Premium and charges");
        Assert.Contains(sections.SelectMany(x => x.Fields), x => x.Value.Contains("does not calculate a refund", StringComparison.Ordinal));
    }
}
