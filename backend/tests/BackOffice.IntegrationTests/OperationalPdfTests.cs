using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Operations;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Operations;
using PdfSharp.Pdf.IO;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalPdfTests
{
    [Theory]
    [InlineData("motor-trade-road-risks", "policy-schedule")]
    [InlineData("motor-trade-combined", "statement-of-fact")]
    [InlineData("commercial-combined", "policy-schedule")]
    [InlineData("commercial-combined", "policy-certificate")]
    [InlineData("motor-trade-road-risks", "policy-certificate")]
    [InlineData("motor-trade-road-risks", "cancellation-notice")]
    [InlineData("commercial-combined", "cancellation-notice")]
    [InlineData("motor-trade-road-risks", "endorsement")]
    [InlineData("commercial-combined", "endorsement")]
    public void ActualProductDocumentsParseAndRetainImmutableProvenance(string product, string kind)
    {
        var source = Source(product);
        if (kind == "endorsement" && product != "commercial-combined")
            source["cover"]!["endorsements"] = JsonSerializer.SerializeToNode(new[] { new { code = "demo-declared-vehicles", version = "1", text = "Use only the declared vehicles for the recorded business activities." } });
        var json = source.ToJsonString();
        if (kind == "cancellation-notice")
        {
            using var basis = JsonDocument.Parse(json);
            var date = basis.RootElement.GetProperty("term").GetProperty("startsAt").GetDateTimeOffset().AddDays(1);
            json = CancellationIssueSnapshot.Create(basis.RootElement, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), date, date, new string('c', 64), "insured-request", "demo-servicing-1"));
        }
        var template = JsonSerializer.Serialize(new { format = "document-template-1", productCode = product, kind,
            title = kind switch { "policy-certificate" => product == "commercial-combined" ? "Employers' liability certificate" : "Motor insurance certificate", "statement-of-fact" => "Statement of fact", "cancellation-notice" => "Cancellation notice", "endorsement" => "Selected endorsements", _ => "Policy schedule" },
            notice = "Café Noël — fictional demonstration cover." });
        var input = new DocumentRenderInput(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "policy-version", json, Hash(json),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), template, Hash(template), product, kind, "PL-PDF-0001", product, kind);
        var rendered = new PolicyDocumentRenderer().Render(input);
        using var parsed = PdfReader.Open(new MemoryStream(rendered.Bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(rendered.PageCount, parsed.PageCount);
        Assert.True(parsed.PageCount >= (kind is "policy-schedule" or "statement-of-fact" ? 3 : 1));
        Assert.Equal(HashBytes(rendered.Bytes), rendered.Sha256);
        Assert.Equal("policy-projection-1", rendered.ProjectionVersion);
        Assert.Equal(json, input.SourceJson);
        var path = Path.GetFullPath(Path.Combine(".local", "phase9-06-pdfs", product + "-" + kind + ".pdf"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, rendered.Bytes);
    }

    private static JsonObject Source(string product)
    {
        using var stream = typeof(OperationalPdfTests).Assembly.GetManifestResourceStream("PdfExamples.issued-" + (product == "commercial-combined" ? "motor-trade-road-risks" : product) + ".json")!;
        var motor = JsonNode.Parse(stream)!.AsObject();
        if (product != "commercial-combined") return motor;
        using var commercial = typeof(OperationalPdfTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        var source = JsonNode.Parse(commercial)!.AsObject(); source.Remove("format"); source.Remove("termIntent"); source["snapshotFormat"] = "issued-commercial-1";
        foreach (var key in new[] { "productVersionId", "term", "premium", "provenance" }) source[key] = motor[key]!.DeepClone();
        foreach (var key in new[] { "clientId", "clientAgencyRelationshipId" }) source["insured"]![key] = motor["insured"]![key]!.DeepClone();
        source["cover"]!["sections"] = JsonSerializer.SerializeToNode(new object[] {
            new { id = Guid.NewGuid(), code = "property", limit = "1000000.00", targetIds = new[] { source["risk"]!["locations"]![0]!["id"]!.GetValue<string>() } },
            new { id = Guid.NewGuid(), code = "employers-liability", limit = "10000000.00", targetIds = Array.Empty<string>() }
        });
        source["cover"]!["endorsements"] = JsonSerializer.SerializeToNode(new[] { new { code = "demo-security", version = "1", text = "Maintain the declared alarm protection." } });
        source["cover"]!["warranties"] = new JsonArray();
        source["risk"]!["liability"]!["hotWorksProcedures"] = "LONG-DECLARATION-START " + string.Concat(Enumerable.Repeat("Keep the declared fire watch and written permits in force. ", 30)) + " LONG-DECLARATION-END";
        return source;
    }
    private static string Hash(string value) => HashBytes(Encoding.UTF8.GetBytes(value));
    private static string HashBytes(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
