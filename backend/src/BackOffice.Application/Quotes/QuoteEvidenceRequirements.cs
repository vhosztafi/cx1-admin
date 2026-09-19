using System.Text.Json;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

public sealed record QuoteEvidenceRequirement(string Code, string Path, Guid? RiskItemId, string Label);

// Requirements are derived from declarations, never from caller-supplied received flags.
// The evidence service will assess actual immutable associations against these requirements.
public static class QuoteEvidenceRequirements
{
    public static IReadOnlyList<QuoteEvidenceRequirement> ForProposal(JsonElement proposal)
    {
        // Commercial proof subjects are introduced with the CC evidence workflow.
        // A commercial proposal can never acquire Motor Trade proof requirements.
        if (Text(At(proposal, "productCode")) == CommercialCaptureRules.ProductCode) return [];
        var result = new List<QuoteEvidenceRequirement>
        {
            new("motor-trader-proof", "/risk/business", null, "Documentary evidence that the proposer is a motor trader")
        };
        var index = 0;
        foreach (var driver in Items(At(proposal, "risk.drivers")))
        {
            if (Guid.TryParse(Text(At(driver, "id")), out var id))
            {
                result.Add(new("photocard-both-sides", $"/risk/drivers/{index}", id, "Both sides of this driver's photocard licence"));
                result.Add(new("driving-record", $"/risk/drivers/{index}", id, "This driver's DVLA driving record"));
            }
            index++;
        }
        var insurance = At(proposal, "risk.previousInsurance");
        var source = Reference(Answer(insurance, "/risk/previousInsurance", "MTS-05-Q10"), "noClaimBonuses");
        var sourceValue = Number(At(source, "value"));
        var claimed = Boolean(Answer(insurance, "/risk/previousInsurance", "prototype.quote.1bdc05ff8b3d").Value);
        var introductory = Boolean(Answer(insurance, "/risk/previousInsurance", "prototype.quote.3fad63dd9abf").Value);
        if (sourceValue is > 1 || claimed == true || introductory == true || Number(At(insurance, "noClaimsYears")) is > 0)
            result.Add(new("no-claims-proof", "/risk/previousInsurance", null, "Proof of the declared no-claims or introductory discount"));
        return result;
    }
}
