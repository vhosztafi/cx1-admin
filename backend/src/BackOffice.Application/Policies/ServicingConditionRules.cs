using System.Text.Json;
using System.Text;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public sealed record ServicingParsedCondition(DateTimeOffset EffectiveAt, ReferralCondition Condition);

public static class ServicingConditionRules
{
    public static IReadOnlyList<ServicingParsedCondition> Parse(JsonElement definition,
        IReadOnlyList<ServicingEvidenceSlice> slices,IReadOnlyList<DateTimeOffset> applicableDates)
    {
        ArgumentNullException.ThrowIfNull(slices);ArgumentNullException.ThrowIfNull(applicableDates);
        if (slices.Count is <1 or >100 || applicableDates.Count is <1 or >100) throw Invalid();
        var proposals=new Dictionary<DateTimeOffset,JsonElement>();DateTimeOffset? previous=null;
        foreach(var slice in slices)
        {
            if (slice is null || slice.EffectiveAt.Offset!=TimeSpan.Zero || previous is not null && slice.EffectiveAt<=previous ||
                slice.Proposal.ValueKind!=JsonValueKind.Object || Encoding.UTF8.GetByteCount(slice.Proposal.GetRawText())>1048576 ||
                !slice.Proposal.TryGetProperty("risk",out var risk) || risk.ValueKind!=JsonValueKind.Object) throw Invalid();
            // Validate every retained target collection before selecting dates.
            // A malformed later risk cannot disappear behind an earlier condition.
            foreach(var collection in new[]{"drivers","premises","vehicles"})
            {
                if (!risk.TryGetProperty(collection,out var items)) continue;
                if (items.ValueKind!=JsonValueKind.Array || items.GetArrayLength()>1000) throw Invalid();
                var ids=new HashSet<Guid>();
                foreach(var item in items.EnumerateArray())
                    if (item.ValueKind!=JsonValueKind.Object || !item.TryGetProperty("id",out var key) ||
                        key.ValueKind!=JsonValueKind.String || !key.TryGetGuid(out var id) || id==Guid.Empty || !ids.Add(id)) throw Invalid();
            }
            proposals.Add(slice.EffectiveAt,slice.Proposal);previous=slice.EffectiveAt;
        }
        previous=null;var result=new List<ServicingParsedCondition>();
        foreach(var date in applicableDates)
        {
            if (date.Offset!=TimeSpan.Zero || previous is not null && date<=previous || !proposals.TryGetValue(date,out var proposal)) throw Invalid();
            // Parse afresh against each exact risk. Do not union drivers or
            // premises across dates or reuse wording from the final proposal.
            result.Add(new(date,ReferralRules.Condition(definition,proposal)));previous=date;
        }
        return result.AsReadOnly();
    }

    private static ArgumentException Invalid()=>new("An ordered bounded servicing schedule and exact applicable condition dates are required.");
}
