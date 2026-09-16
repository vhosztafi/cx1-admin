using System.Text.Json;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteEvidenceRequirementsTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void EachActualDriverRequiresSeparateProofEvenWhenReceivedIsClaimed(string product)
    {
        using var document = JsonDocument.Parse($$$$"""
            {"productCode":"{{{{product}}}}","risk":{"drivers":[
              {"id":"10000000-0000-4000-8000-000000000001","evidenceReceived":true},
              {"id":"10000000-0000-4000-8000-000000000002"}]}}
            """);
        var requirements = QuoteEvidenceRequirements.ForProposal(document.RootElement);
        Assert.Equal(5, requirements.Count);
        Assert.Equal(2, requirements.Count(x => x.Code == "photocard-both-sides"));
        Assert.Equal(2, requirements.Count(x => x.Code == "driving-record"));
        Assert.Equal(2, requirements.Where(x => x.RiskItemId is not null).Select(x => x.RiskItemId).Distinct().Count());
        Assert.Contains(requirements, x => x.Path == "/risk/drivers/1");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, true)]
    public void DeclaredDiscountRequiresActualProof(int years, bool expected)
    {
        using var document = JsonDocument.Parse($$$$"""{"risk":{"previousInsurance":{"noClaimsYears":{{{{years}}}},"proofReceived":true}}}""");
        var requirements = QuoteEvidenceRequirements.ForProposal(document.RootElement);
        Assert.Equal(expected, requirements.Any(x => x.Code == "no-claims-proof"));
        Assert.Contains(requirements, x => x.Code == "motor-trader-proof" && x.RiskItemId is null);
    }
}
