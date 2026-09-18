using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Infrastructure.Policies;

internal static class ServicingIssueSnapshot
{
    internal static string Create(ServicingDecisionContext held, ServicingIssueDecision decision, PolicyTransaction transaction,
        IReadOnlyList<ServicingEvidenceSlice> slices, CalculatedServicingRating rating, int index, JsonElement contract)
    {
        var snapshot = JsonNode.Parse(slices[index].Proposal.GetRawText())!.AsObject();
        var basis = JsonNode.Parse(held.Scope.Base.SnapshotJson)!.AsObject();
        snapshot["snapshotFormat"]="issued-servicing-1"; snapshot.Remove("termIntent"); snapshot["productVersionId"]=held.Cycle.ProductVersionId;
        snapshot["insured"]!["clientId"]=held.Scope.Source.Quote.ClientId;
        snapshot["insured"]!["clientAgencyRelationshipId"]=held.Scope.Source.Quote.RelationshipId;
        snapshot["term"]=basis["term"]!.DeepClone();
        var risk = snapshot["risk"]!.AsObject(); var input = held.Input.Slices[index].Input;
        if(input.AnyDriverCount==0) risk["driverBasis"]=JsonSerializer.SerializeToNode(new{kind="named",responses=risk["responses"]});
        else
        {
            JsonNode Answer(string id)=>risk["responses"]!["answers"]!.AsArray().Single(x=>x!["questionId"]!.GetValue<string>()==id)!["value"]!.DeepClone();
            risk["driverBasis"]=JsonSerializer.SerializeToNode(new{kind=input.Drivers.Count>0?"named-and-any-driver":"any-driver",
                minimumAge=input.AnyDriverMinimumAge,maximumAge=input.AnyDriverMaximumAge,driverCount=input.AnyDriverCount,
                maximumVehicleGrouping=Answer("MTS-06-Q05"),maximumGrossVehicleWeight=Answer("MTS-06-Q06"),maximumMotorcycleCapacity=Answer("MTS-06-Q07"),responses=risk["responses"]});
        }
        var cover=snapshot["cover"]!.AsObject();
        cover["sections"]=new JsonArray((cover["requestedSections"]?.AsArray()??[]).Where(x=>x!["selected"]!.GetValue<bool>()).Select(x=>
        {var section=x!.DeepClone().AsObject();section.Remove("selected");return (JsonNode)section;}).ToArray());
        Guid SectionId(string code)
        {
            var retained=basis["cover"]!["sections"]!.AsArray().FirstOrDefault(x=>x!["code"]!.GetValue<string>()==code);
            return retained is not null?Guid.Parse(retained["id"]!.GetValue<string>()):new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(transaction.Id.ToString("D")+"/"+code)).AsSpan(0,16));
        }
        var facts=QuoteCoverFacts.Reconcile(slices[index].Proposal);
        if(facts.Issues.Count!=0)throw new QuoteOperationException(409,"servicing-cover-context-invalid");
        cover["sections"]!.AsArray().Add(JsonSerializer.SerializeToNode(new{id=SectionId("road-risks"),code="road-risks",coverLevel=facts.Facts["coverLevel"]}));
        foreach(var (code,fact) in new[]{("own-vehicles","ownVehicleLimit"),("customer-vehicles","customerVehicleLimit")})
            if(facts.Facts.TryGetValue(fact,out var limit))cover["sections"]!.AsArray().Add(JsonSerializer.SerializeToNode(new{id=SectionId(code),code,limit,excess=facts.Facts["excess"]}));
        var applied=contract.GetProperty("conditions").EnumerateArray().SelectMany(x=>ServicingConditionRules.Parse(x.GetProperty("definition"),slices,
            x.GetProperty("effectiveDates").EnumerateArray().Select(d=>d.GetDateTimeOffset()).ToArray())).Where(x=>x.EffectiveAt==slices[index].EffectiveAt).Select(x=>x.Condition).ToArray();
        foreach(var (kind,property) in new[]{("endorsement","endorsements"),("warranty","warranties")})
        {
            var wording=(basis["cover"]![property]?.AsArray()??[]).Select(x=>x!.DeepClone()).ToList();
            foreach(var condition in applied.Where(x=>x.Kind==kind))
            {
                var code=condition.EndorsementCode??condition.Code;wording.RemoveAll(x=>x["code"]!.GetValue<string>()==code);
                wording.Add(JsonSerializer.SerializeToNode(new{code,version=JsonNode.Parse(condition.DefinitionJson)?["wordingVersion"]?.GetValue<string>()??"1",text=condition.Wording})!);
            }
            cover[property]=new JsonArray(wording.ToArray());
        }
        // Snapshot premium retains cumulative term charges through this slice.
        // The separate immutable obligation is the actual amount due for this
        // issue, so a two-slice transaction never repeats its fee or invoice.
        decimal Prior(string field)=>decimal.Parse(basis["premium"]![field]!.GetValue<string>(),CultureInfo.InvariantCulture);
        var through=rating.Slices.Take(index+1).ToArray();
        var premium=Prior("termPremium")+through.Sum(x=>x.Premium);var tax=Prior("tax")+through.Sum(x=>x.Tax);
        var fee=Prior("fee")+rating.Fee;var commission=Prior("brokerCommission")+through.Sum(x=>x.BrokerCommission);
        var commercial=contract.GetProperty("commercialTerms");var settlement=commercial.GetProperty("settlement");
        var collector=settlement.GetProperty("premiumCollection").GetString()!;var netted=collector=="agency" && settlement.GetProperty("commissionSettlement").GetString()=="net-remittance";
        var shareBps=commercial.GetProperty("commercialTerms").GetProperty("feeSharing").GetString()=="agreed-split"?commercial.GetProperty("commercialTerms").GetProperty("feeShareBasisPoints").GetInt32():0;
        var share=decimal.Parse(basis["premium"]!["settlement"]!["brokerFeeShare"]!.GetValue<string>(),CultureInfo.InvariantCulture)+decimal.Round(rating.Fee*shareBps/10000m,2,MidpointRounding.AwayFromZero);
        var gross=premium+tax+fee;var net=gross-commission-share;
        string Money(decimal amount)=>PolicyIssueWriter.Money(amount);
        snapshot["premium"]=JsonSerializer.SerializeToNode(new{currency="GBP",annualPremium=Money(rating.Slices[index].AnnualPremium),termPremium=Money(premium),
            tax=Money(tax),fee=Money(fee),brokerCommission=Money(commission),grossPayable=Money(gross),netBrokerDue=Money(net),insurerDue=Money(premium+tax-commission),
            taxBasisPoints=held.Input.RatingDefinition.GetProperty("taxRateBps").GetInt32(),commissionBasisPoints=held.Input.CommissionBasisPoints,
            ratingResultId=held.Rating.Id,ruleVersion=held.Scope.Eligible.RatingVersion.Version,
            settlement=new{termsVersionId=held.Cycle.AgencyTermsVersionId,collector,mode=netted?"net-remittance":"separate-payment",feeShareBasisPoints=shareBps,
                brokerFeeShare=Money(share),mgaFeeIncome=Money(fee-share),brokerRemuneration=Money(commission+share),invoiceDue=Money(netted?net:gross),
                remunerationPayable=Money(netted?0:commission+share),netEconomicDue=Money(net)}});
        snapshot["provenance"]=JsonSerializer.SerializeToNode(new{source="backoffice",sourceQuoteId=transaction.SourceQuoteId,servicingIssueDecisionId=decision.Id,
            baseVersionId=decision.BaseVersionId,revisionId=decision.RevisionId,transactionId=transaction.Id,effectiveAt=slices[index].EffectiveAt,
            processedAt=transaction.ProcessedAt,sliceOrdinal=index+1,inputHash=Convert.ToHexStringLower(decision.InputHash)});
        var json=snapshot.ToJsonString();using var parsed=JsonDocument.Parse(json);var errors=PolicySnapshotShape.Errors(parsed.RootElement);
        if(errors.Count!=0)throw new InvalidOperationException("Issued servicing snapshot: "+string.Join("; ",errors));
        return json;
    }
}
