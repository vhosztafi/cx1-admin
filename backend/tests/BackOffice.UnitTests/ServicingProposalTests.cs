using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingProposalTests
{
    private static readonly Guid Base = Guid.NewGuid();
    private static string Proposal(object[]? changes = null) => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0", baseVersionId = Base, reason = "Fictional servicing request",
        requestedBy = new { kind = "internal" },
        commonEffectiveIntent = new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" },
        changes = changes ?? []
    });

    [Fact]
    public void IncompleteTypedProposalIsStoredCanonicallyWithExactBaseAndHash()
    {
        var a = ServicingProposalInput.Parse(Proposal(), Base);
        var b = ServicingProposalInput.Parse("  " + Proposal() + "\n", Base);
        Assert.Equal(a.Json, b.Json); Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(Proposal(), Guid.NewGuid()));
    }

    [Fact]
    public void ClosedSchemaRejectsArbitraryPayloadAndDriverDateOverride()
    {
        object Change(object payload) => new { changeId = Guid.NewGuid(), riskItemId = Guid.NewGuid(), kind = "driver", operation = "update", payload };
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(Proposal([Change(new { arbitrary = "unsafe" })]), Base));
        var json = Proposal([Change(new { })]);
        Assert.NotEmpty(ServicingProposalInput.Parse(json, Base).Json);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["changes"]![0]!["effectiveIntent"] = node["commonEffectiveIntent"]!.DeepClone();
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(node.ToJsonString(), Base));
    }

    [Fact]
    public void DuplicateFieldsNullAndOversizedInputFailBeforeCapture()
    {
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(Proposal().Replace("\"schemaVersion\":", "\"schemaVersion\":\"1.0\",\"schemaVersion\":"), Base));
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(Proposal().Replace("\"internal\"", "null"), Base));
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(new string(' ', ServicingProposalInput.MaximumBytes + 1), Base));
    }

    [Fact]
    public void ChangeIdsAreUniqueAndNeverEmpty()
    {
        var change = new { changeId = Guid.NewGuid(), riskItemId = Guid.NewGuid(), kind = "driver", operation = "update", payload = new { } };
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(Proposal([change, change]), Base));
        var empty = new { changeId = Guid.Empty, riskItemId = Guid.NewGuid(), kind = "driver", operation = "update", payload = new { } };
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(Proposal([empty]), Base));
    }
}
