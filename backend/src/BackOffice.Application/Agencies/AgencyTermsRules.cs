using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Parties;

namespace BackOffice.Application.Agencies;

public sealed record ValidatedAgencyTerms(DateOnly EffectiveFrom,string Reason,string SnapshotJson,string Fingerprint,IReadOnlyList<AgencyProductInput> Products);

// Complete agreed-term proposals, unlike partial onboarding drafts. Database
// eligibility, current agency version and independent approval remain service checks.
public static class AgencyTermsRules
{
    public static ValidatedAgencyTerms ValidateProposal(JsonElement input,DateOnly today,DateOnly? latestEffectiveFrom)
        =>Validate(input,today,latestEffectiveFrom,false);

    // Read an immutable version without treating its historical date as a new proposal.
    // Current/future selection belongs to the caller's explicit business date.
    public static ValidatedAgencyTerms ReadPublished(JsonElement snapshot)
    {
        if(Encoding.UTF8.GetByteCount(snapshot.GetRawText())>65536)throw new AgencyCommandException(413,"agency-terms-too-large");
        Closed(snapshot,["effectiveFrom","commercialTerms","settlement","paymentTermsDays","creditLimit","products"]);
        var input=JsonNode.Parse(snapshot.GetRawText())!.AsObject();input["reason"]="Published terms assessment";
        using var document=JsonDocument.Parse(input.ToJsonString());
        return Validate(document.RootElement,DateOnly.MinValue,null,false);
    }

    // Activation captures the saved declarations without rewriting their historical
    // effective date. Future changes continue to use ValidateProposal.
    public static ValidatedAgencyTerms ExtractInitial(JsonElement savedDraft,IReadOnlyList<AgencyProductInput> products,DateOnly today,string reason)
    {
        var normalized=AgencyDraftRules.Validate(savedDraft);
        using var draft=JsonDocument.Parse(normalized.Json);
        var input=new JsonObject();
        foreach(var key in new[]{"commercialTerms","settlement","paymentTermsDays","creditLimit"})
        {
            if(!draft.RootElement.TryGetProperty(key,out var value))throw new AgencyCommandException(422,"agency-terms-incomplete");
            input[key]=JsonNode.Parse(value.GetRawText());
        }
        if(!draft.RootElement.GetProperty("commercialTerms").TryGetProperty("effectiveFrom",out var effective))throw new AgencyCommandException(422,"agency-terms-incomplete");
        input["effectiveFrom"]=JsonNode.Parse(effective.GetRawText());input["reason"]=reason;
        input["products"]=new JsonArray(products.Select(x=>(JsonNode)new JsonObject{{"productVersionId",x.ProductVersionId.ToString("D")},{"effectiveFrom",x.EffectiveFrom.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)},{"brokerCommissionBasisPoints",x.BrokerCommissionBasisPoints}}).ToArray());
        using var complete=JsonDocument.Parse(input.ToJsonString());
        return Validate(complete.RootElement,today,null,true);
    }
    private static ValidatedAgencyTerms Validate(JsonElement input,DateOnly today,DateOnly? latestEffectiveFrom,bool initial)
    {
        if(Encoding.UTF8.GetByteCount(input.GetRawText())>65536)throw new AgencyCommandException(413,"agency-terms-too-large");
        Closed(input,["effectiveFrom","reason","commercialTerms","settlement","paymentTermsDays","creditLimit","products"]);
        var effective=Date(input.GetProperty("effectiveFrom"));
        if(initial)
        {
            if(effective==default||effective>today)throw new AgencyCommandException(422,"agency-initial-terms-date");
        }
        else ValidateSchedule(effective,today,latestEffectiveFrom);
        var reason=Reason(input.GetProperty("reason"));
        var details=new JsonObject();
        foreach(var key in new[]{"commercialTerms","settlement","paymentTermsDays","creditLimit"})details[key]=JsonNode.Parse(input.GetProperty(key).GetRawText());
        using var source=JsonDocument.Parse(details.ToJsonString());var normalized=AgencyDraftRules.Validate(source.RootElement);
        using var draft=JsonDocument.Parse(normalized.Json);var root=draft.RootElement;
        foreach(var path in new[]{"commercialTerms.effectiveFrom","commercialTerms.commissionBasis","commercialTerms.feeSharing","commercialTerms.volumeCommitmentMode","commercialTerms.minimumPremiumOverrideMode","commercialTerms.referralRouting","settlement.statementCycle","settlement.method","settlement.premiumCollection","settlement.commissionSettlement","paymentTermsDays","creditLimit"})Require(root,path);
        var commercial=root.GetProperty("commercialTerms");
        if(Date(commercial.GetProperty("effectiveFrom"))!=effective)throw new AgencyCommandException(422,"agency-terms-date-mismatch");
        Conditional(root,"commissionBasis","flat-rate","flatCommissionBasisPoints");
        Conditional(root,"feeSharing","agreed-split","feeShareBasisPoints");
        if(AgencyEvidenceRules.Text(root,"commercialTerms.volumeCommitmentMode")!="none")Require(root,"commercialTerms.volumeCommitment");
        Conditional(root,"minimumPremiumOverrideMode","capacity-provider-agreed","minimumPremiumOverride");
        var array=input.GetProperty("products");if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength() is <1 or >3)throw new AgencyCommandException(422,"agency-terms-products");
        var products=new List<AgencyProductInput>();
        foreach(var item in array.EnumerateArray())
        {
            Closed(item,["productVersionId","effectiveFrom","brokerCommissionBasisPoints"]);
            if(item.GetProperty("productVersionId").ValueKind!=JsonValueKind.String||!Guid.TryParseExact(item.GetProperty("productVersionId").GetString(),"D",out var id)||id==Guid.Empty||item.GetProperty("brokerCommissionBasisPoints").ValueKind!=JsonValueKind.Number||!item.GetProperty("brokerCommissionBasisPoints").TryGetInt32(out var bps))throw new AgencyCommandException(422,"agency-terms-products");
            products.Add(new(id,Date(item.GetProperty("effectiveFrom")),bps));
        }
        AgencyDraftRules.ValidateProducts(products);
        // A full new version must not backdate a product or begin with no product.
        if(products.Any(x=>x.EffectiveFrom<effective)||!products.Any(x=>x.EffectiveFrom==effective))throw new AgencyCommandException(422,"agency-terms-product-dates");
        var ordered=products.OrderBy(x=>x.ProductVersionId).ToArray();
        var snapshot=new JsonObject{{"effectiveFrom",effective.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)}};
        foreach(var property in root.EnumerateObject())snapshot[property.Name]=JsonNode.Parse(property.Value.GetRawText());
        snapshot["products"]=new JsonArray(ordered.Select(x=>(JsonNode)new JsonObject{{"productVersionId",x.ProductVersionId.ToString("D")},{"effectiveFrom",x.EffectiveFrom.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)},{"brokerCommissionBasisPoints",x.BrokerCommissionBasisPoints}}).ToArray());
        var json=snapshot.ToJsonString();var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new(effective,reason,json,fingerprint,Array.AsReadOnly(ordered));
    }
    public static void ValidateSchedule(DateOnly effective,DateOnly today,DateOnly? latest)
    {
        if(effective==default||effective<today||latest is DateOnly prior&&effective<=prior)throw new AgencyCommandException(422,"agency-terms-schedule");
    }
    private static void Conditional(JsonElement root,string mode,string enabled,string field){if(AgencyEvidenceRules.Text(root,"commercialTerms."+mode)==enabled)Require(root,"commercialTerms."+field);}
    private static void Require(JsonElement root,string path){if(!AgencyEvidenceRules.Present(root,path))throw new PartyValidationException([new("/"+path.Replace('.','/'),"required","Enter the complete agreed terms.")]);}
    private static DateOnly Date(JsonElement value){if(value.ValueKind!=JsonValueKind.String||!DateOnly.TryParseExact(value.GetString(),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var result))throw new AgencyCommandException(422,"agency-terms-date");return result;}
    private static string Reason(JsonElement value){if(value.ValueKind!=JsonValueKind.String)throw new AgencyCommandException(422,"agency-terms-reason");var text=value.GetString()!;if(string.IsNullOrWhiteSpace(text)||text.Length>1000||text.Any(char.IsControl))throw new AgencyCommandException(422,"agency-terms-reason");return text.Trim();}
    private static void Closed(JsonElement value,string[] fields)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new AgencyCommandException(422,"agency-terms-shape");var found=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())if(!fields.Contains(property.Name,StringComparer.Ordinal)||!found.Add(property.Name)||property.Value.ValueKind==JsonValueKind.Null)throw new AgencyCommandException(422,"agency-terms-shape");
        if(found.Count!=fields.Length)throw new AgencyCommandException(422,"agency-terms-incomplete");
    }
}
