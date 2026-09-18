using System.Text.Json;
using System.Security.Cryptography;

namespace BackOffice.Application.Policies;

public sealed record ServicingWarrantyInput(Guid Id,JsonElement Definition,IReadOnlyList<DateTimeOffset> EffectiveDates);

public static class ServicingWarrantyRules
{
    public static ServicingProofRequirement? Requirement(ServicingProofContext context,IReadOnlyList<ServicingEvidenceSlice> slices,
        IReadOnlyList<ServicingWarrantyInput> conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        // Reuse the complete schedule/identity validation and fingerprint. The
        // acknowledgement covers the complete current warranty set, not just a
        // purpose label that could be reused for different contractual wording.
        var schedule=ServicingEvidenceRules.Requirements(context,slices);
        if(conditions.Count>100 || conditions.Any(x=>x is null || x.Id==Guid.Empty) || conditions.Select(x=>x.Id).Distinct().Count()!=conditions.Count)
            throw new ArgumentException("A bounded unique current warranty set is required.");
        if(conditions.Count==0) return null;
        var dates=new SortedSet<DateTimeOffset>();var warranties=new List<object>();
        foreach(var item in conditions.OrderBy(x=>x.Id))
        {
            var parsed=ServicingConditionRules.Parse(item.Definition,slices,item.EffectiveDates);
            if(parsed.Any(x=>x.Condition.Kind!="warranty" || x.Condition.RequirementCode!="warranty-acknowledgement"))
                throw new ArgumentException("Only parsed warranties can request an acknowledgement.");
            foreach(var row in parsed) dates.Add(row.EffectiveAt);
            warranties.Add(new {item.Id,Definition=JsonSerializer.Deserialize<JsonElement>(item.Definition.GetRawText()),
                Dates=parsed.Select(x=>new {x.EffectiveAt,x.Condition.Wording,x.Condition.TargetIds,x.Condition.EndorsementCode}).ToArray()});
        }
        var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            format="servicing-warranty-acknowledgement-1",context,
            schedule=schedule.Select(x=>x.InputFingerprint).ToArray(),warranties
        })));
        return new("warranty-acknowledgement","Acknowledgement of all current dated warranties","/underwriting/warranties",null,dates.ToArray(),hash){Context=context};
    }
}
