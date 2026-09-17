using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;
public sealed partial class ServicingProposalTests
{
    [Fact]
    public void ReorderingCoveredPremisesMembershipIsNotAMaterialChange()
    {
        var snapshot = Snapshot(); var premises = snapshot["risk"]!["premises"]!.AsArray();
        var second = premises[0]!.DeepClone(); second["id"] = Guid.NewGuid().ToString(); premises.Add(second);
        var section = snapshot["cover"]!["requestedSections"]!.AsArray().First(x => x!["code"]!.GetValue<string>() == "premises")!;
        section["premisesIds"] = new JsonArray(premises.Select(x => (JsonNode?)JsonValue.Create(x!["id"]!.GetValue<string>())).ToArray());
        var updated = section.DeepClone(); updated["premisesIds"] = new JsonArray(premises.Reverse().Select(x => (JsonNode?)JsonValue.Create(x!["id"]!.GetValue<string>())).ToArray());
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("cover", Guid.Parse(section["id"]!.GetValue<string>()), "update", new JsonObject { ["requestedSections"] = new JsonArray(updated) })), Context());
        Assert.Empty(result.Changes);
    }
    [Fact]
    public void DifferencePathsRetainActualDriverPositionsWhileMatchingStableIds()
    {
        var snapshot = Snapshot(); var drivers = snapshot["risk"]!["drivers"]!.AsArray();
        var firstId = Guid.Parse(drivers[0]!["id"]!.GetValue<string>());
        var second = drivers[0]!.DeepClone(); second["id"] = "11111111-0000-4000-8000-000000000001"; drivers.Add(second);
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", firstId, "update", new JsonObject { ["fullName"] = "Corrected original driver" })), Context());
        var difference = Assert.Single(result.Changes);
        Assert.Equal(firstId, difference.ItemId);
        Assert.Equal("/risk/drivers/0/fullName", difference.Path);
    }
}
