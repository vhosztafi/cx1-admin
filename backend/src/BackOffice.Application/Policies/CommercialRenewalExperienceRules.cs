using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Policies;

public sealed record CommercialRenewalSubjects(string Format,Guid BaseVersionId,Guid RevisionId,string InputHash,
    IReadOnlyList<Guid> PropertyLocationIds,IReadOnlyList<Guid> WageCategoryIds,IReadOnlyList<Guid> LossRecordIds,
    IReadOnlyList<string> LiabilitySections);

// Overall supplied financial experience is counted once. This manifest records
// which complete commercial risk its source document and review describe.
public static class CommercialRenewalExperienceRules
{
    public static CommercialRenewalSubjects Subjects(Guid baseVersionId,Guid revisionId,JsonElement proposal,QuoteVersionPins pins)
    {
        if(baseVersionId==Guid.Empty || revisionId==Guid.Empty || proposal.ValueKind!=JsonValueKind.Object || pins is null)throw Invalid();
        PreparedQuoteCapture prepared;
        try{prepared=CommercialCaptureRules.Prepare(proposal.GetRawText(),pins);}
        catch(Exception error) when(error is QuoteInputException or QuoteValidationException or InvalidOperationException or KeyNotFoundException){throw Invalid();}
        if(!proposal.TryGetProperty("risk",out var risk) || !risk.TryGetProperty("liability",out var liability))throw Invalid();
        var employersSelected=risk.TryGetProperty("declarations",out var declarations) && declarations.TryGetProperty("answers",out var answers) &&
            answers.EnumerateArray().Any(x=>x.GetProperty("questionId").GetString()=="prototype.quote.36ef01068295" && x.GetProperty("value").ValueKind==JsonValueKind.True);
        var sections=new List<string>();
        foreach(var (field,code) in new[]{("employersLimit","employers-liability"),("publicLimit","public-liability"),("productsLimit","products-liability")})
            if((field!="employersLimit" || employersSelected) && liability.TryGetProperty(field,out var value) && value.ValueKind==JsonValueKind.String &&
                decimal.TryParse(value.GetString(),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount) && amount>0)sections.Add(code);
        Guid[] Ids(string collection)=>risk.TryGetProperty(collection,out var rows) && rows.ValueKind==JsonValueKind.Array
            ?rows.EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()).Order().ToArray():throw Invalid();
        return new("commercial-renewal-subjects-1",baseVersionId,revisionId,prepared.Input.ContentHash,Ids("locations"),Ids("wages"),Ids("losses"),sections.Order(StringComparer.Ordinal).ToArray());
    }

    public static bool Matches(CommercialRenewalSubjects? supplied,CommercialRenewalSubjects current)=>supplied is not null &&
        supplied.Format=="commercial-renewal-subjects-1" && supplied.Format==current.Format && supplied.BaseVersionId==current.BaseVersionId &&
        supplied.RevisionId==current.RevisionId && supplied.InputHash==current.InputHash &&
        supplied.PropertyLocationIds is not null && supplied.PropertyLocationIds.SequenceEqual(current.PropertyLocationIds) &&
        supplied.WageCategoryIds is not null && supplied.WageCategoryIds.SequenceEqual(current.WageCategoryIds) &&
        supplied.LossRecordIds is not null && supplied.LossRecordIds.SequenceEqual(current.LossRecordIds) &&
        supplied.LiabilitySections is not null && supplied.LiabilitySections.SequenceEqual(current.LiabilitySections);

    private static ArgumentException Invalid()=>new("Commercial renewal experience requires the complete saved commercial risk and owned version identities.");
}
