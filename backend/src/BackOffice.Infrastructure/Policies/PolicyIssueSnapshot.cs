using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Infrastructure.Policies;

internal static class PolicyIssueSnapshot
{
    internal static string Create(UnderwritingDecisionContext held, Guid authorityId, IssuePosting posting, string collector, int share, IReadOnlyList<QuoteCondition> conditions)
    {
        var snapshot = JsonNode.Parse(held.Revision.ProposalJson)!.AsObject();
        snapshot["snapshotFormat"] = "issued-quote-1";
        snapshot.Remove("termIntent"); snapshot["productVersionId"] = held.Cycle.ProductVersionId;
        snapshot["insured"]!["clientId"] = held.Cycle.ClientId; snapshot["insured"]!["clientAgencyRelationshipId"] = held.Cycle.RelationshipId;
        snapshot["term"] = JsonSerializer.SerializeToNode(new { kind = held.Input.Input.Term.Kind, startsAt = held.Cycle.StartsAt, endsAt = held.Cycle.EndsAt, timeZone = "Europe/London" });
        var risk = snapshot["risk"]!.AsObject(); var input = held.Input.Input;
        if (input.AnyDriverCount == 0)
            risk["driverBasis"] = JsonSerializer.SerializeToNode(new { kind = "named", responses = risk["responses"] });
        else
        {
            JsonNode Answer(string id) => risk["responses"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == id)!["value"]!.DeepClone();
            risk["driverBasis"] = JsonSerializer.SerializeToNode(new { kind = input.Drivers.Count > 0 ? "named-and-any-driver" : "any-driver",
                minimumAge = input.AnyDriverMinimumAge, maximumAge = input.AnyDriverMaximumAge, driverCount = input.AnyDriverCount,
                maximumVehicleGrouping = Answer("MTS-06-Q05"), maximumGrossVehicleWeight = Answer("MTS-06-Q06"), maximumMotorcycleCapacity = Answer("MTS-06-Q07"), responses = risk["responses"] });
        }
        var cover = snapshot["cover"]!.AsObject();
        cover["sections"] = new JsonArray((cover["requestedSections"]?.AsArray() ?? []).Where(x => x!["selected"]!.GetValue<bool>()).Select(x => {
            var section = x!.DeepClone().AsObject(); section.Remove("selected"); return (JsonNode)section;
        }).ToArray());
        using var original = JsonDocument.Parse(held.Revision.ProposalJson);
        var facts = QuoteCoverFacts.Reconcile(original.RootElement);
        if (facts.Issues.Count != 0) throw new QuoteOperationException(409, "policy-cover-context-invalid");
        cover["sections"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = Guid.NewGuid(), code = "road-risks", coverLevel = facts.Facts["coverLevel"] }));
        foreach (var (code, fact) in new[] { ("own-vehicles", "ownVehicleLimit"), ("customer-vehicles", "customerVehicleLimit") })
            if (facts.Facts.TryGetValue(fact, out var limit))
                cover["sections"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id = Guid.NewGuid(), code, limit, excess = facts.Facts["excess"] }));
        JsonArray Wording(string kind) => new(conditions.Where(x => x.Kind == kind).OrderBy(x => x.Id).Select(x => JsonSerializer.SerializeToNode(new {
            code = x.EndorsementCode ?? x.Code, version = JsonNode.Parse(x.DefinitionJson)?["wordingVersion"]?.GetValue<string>() ?? "1", text = x.Wording })).ToArray());
        cover["endorsements"] = Wording("endorsement"); cover["warranties"] = Wording("warranty");
        var r = held.Rating!; string Money(decimal n) => PolicyIssueWriter.Money(n);
        snapshot["premium"] = JsonSerializer.SerializeToNode(new { currency = "GBP", annualPremium = Money(r.AnnualPremium), termPremium = Money(r.TermPremium), tax = Money(r.Tax), fee = Money(r.Fee),
            brokerCommission = Money(r.BrokerCommission), grossPayable = Money(posting.GrossDue), netBrokerDue = Money(posting.NetDue), insurerDue = Money(posting.InsurerDue),
            taxBasisPoints = held.Eligible.Rating.GetProperty("taxRateBps").GetInt32(), commissionBasisPoints = held.Eligible.CommissionBasisPoints,
            ratingResultId = r.Id, ruleVersion = held.Eligible.RatingVersion.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            settlement = new { termsVersionId = held.Cycle.AgencyTermsVersionId, collector, mode = posting.EffectiveSettlement, feeShareBasisPoints = share,
                brokerFeeShare = Money(posting.FeeShare), mgaFeeIncome = Money(posting.RetainedFee), brokerRemuneration = Money(r.BrokerCommission + posting.FeeShare),
                invoiceDue = Money(posting.InvoiceDue), remunerationPayable = Money(posting.BrokerPayable), netEconomicDue = Money(posting.NetDue) } });
        snapshot["provenance"] = JsonSerializer.SerializeToNode(new { source = "backoffice", quoteRevisionId = held.Revision.Id, authorityVersionId = authorityId });
        var json = snapshot.ToJsonString(); using var doc = JsonDocument.Parse(json);
        var errors = PolicySnapshotShape.Errors(doc.RootElement);
        if (errors.Count > 0) throw new InvalidOperationException("Issued policy schema: " + string.Join("; ", errors));
        return json;
    }
}
