using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed partial class ServicingProposalTests
{
    [Fact]
    public void PremisesReplacementClearsAddressAndRemovalRequiresCorrectingCoveredPremises()
    {
        var snapshot = Snapshot();
        var premises = snapshot["risk"]!["premises"]![0]!;
        var id = Guid.Parse(premises["id"]!.GetValue<string>());
        var original = snapshot.ToJsonString();
        var replacement = Change("premises", id, "update", new JsonObject { ["address"] = new JsonObject { ["street"] = "Fictional revised street" } });
        replacement["payloadMode"] = "replace";
        var result = ServicingProposalRules.Assess(original, Draft(replacement), Context());
        var address = result.Proposed.GetProperty("risk").GetProperty("premises")[0].GetProperty("address");
        Assert.Equal("Fictional revised street", address.GetProperty("street").GetString());
        Assert.False(address.TryGetProperty("postcode", out _));
        Assert.Equal(original, snapshot.ToJsonString());
        var section = snapshot["cover"]!["requestedSections"]![0]!;
        section["premisesIds"] = new JsonArray(id.ToString());
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("premises", id, "remove")), Context()));
    }
}
