using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;

namespace BackOffice.Api;

public static partial class RenewalPreparationEndpoints
{
    private static CommercialRenewalSubjects CommercialSubjects(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object)throw Invalid();
        QuoteHttpInput.Keys(root,"format","baseVersionId","revisionId","inputHash","propertyLocationIds","wageCategoryIds","lossRecordIds","liabilitySections");
        var format=QuoteReferralEndpoints.Text(root,"format",50);var hash=QuoteReferralEndpoints.Text(root,"inputHash",64);
        if(format!="commercial-renewal-subjects-1"||!ReferralRules.Hash(hash))throw Invalid();
        var sections=Rows("liabilitySections",3).Select(x=>x.ValueKind==JsonValueKind.String?x.GetString()!:throw Invalid()).ToArray();
        if(sections.Any(x=>x is not("employers-liability" or "public-liability" or "products-liability"))||sections.Distinct().Count()!=sections.Length)throw Invalid();
        return new(format,QuoteHttpInput.Id(root,"baseVersionId"),QuoteHttpInput.Id(root,"revisionId"),hash,
            Ids("propertyLocationIds"),Ids("wageCategoryIds"),Ids("lossRecordIds"),sections.Order(StringComparer.Ordinal).ToArray());

        JsonElement[] Rows(string name,int maximum)
        {
            if(!root.TryGetProperty(name,out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>maximum)throw Invalid();
            return rows.EnumerateArray().ToArray();
        }
        Guid[] Ids(string name)
        {
            var ids=Rows(name,500).Select(x=>x.ValueKind==JsonValueKind.String&&Guid.TryParseExact(x.GetString(),"D",out var id)&&id!=Guid.Empty?id:throw Invalid()).ToArray();
            if(ids.Distinct().Count()!=ids.Length)throw Invalid();return ids.Order().ToArray();
        }
        static QuoteHttpException Invalid()=>new(422,"commercial-renewal-experience-subjects-invalid");
    }
}
