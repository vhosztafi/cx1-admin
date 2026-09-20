using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public static class CommercialServicingEvidenceRules
{
    public static IReadOnlyList<ServicingProofRequirement> Requirements(ServicingProofContext context,IReadOnlyList<ServicingEvidenceSlice> slices)
    {
        if(context is null || context.Pins is null || slices is null || slices.Count is <1 or >100 ||
            new[]{context.DraftId,context.CycleId,context.RevisionId,context.RatingId,context.Pins.ProductVersionId,context.Pins.AgencyTermsVersionId}.Contains(Guid.Empty) ||
            !ReferralRules.Hash(context.InputHash) || !CommercialCaptureRules.Accepts(context.Pins.SchemaVersion,context.Pins.QuestionSetVersion,context.Pins.ReferenceVersion)) throw Invalid();
        var requirements=new Dictionary<(string Code,Guid? Id),(string Label,string Path,List<DateTimeOffset> Dates)>();
        var schedule=new List<object>();DateTimeOffset? previous=null;long bytes=0;
        foreach(var slice in slices)
        {
            if(slice is null || slice.EffectiveAt.Offset!=TimeSpan.Zero || previous is not null && slice.EffectiveAt<=previous || slice.Proposal.ValueKind!=JsonValueKind.Object) throw Invalid();
            previous=slice.EffectiveAt;PreparedQuoteCapture prepared;IReadOnlyList<CommercialProof> proofs;
            try
            {
                prepared=CommercialCaptureRules.Prepare(slice.Proposal.GetRawText(),context.Pins);
                proofs=CommercialEvidenceRules.Requirements(slice.Proposal);
            }
            catch(Exception error) when(error is QuoteInputException or QuoteValidationException or InvalidOperationException or KeyNotFoundException)
            { throw Invalid(); }
            bytes+=System.Text.Encoding.UTF8.GetByteCount(prepared.Input.Json);if(bytes>ServicingRatingInput.MaximumBytes)throw Invalid();
            schedule.Add(new{slice.EffectiveAt,prepared.Input.ContentHash});
            foreach(var proof in proofs)
            {
                if(!requirements.TryGetValue((proof.Code,proof.RiskItemId),out var current))
                {
                    if(requirements.Count>=5000)throw Invalid();current=(proof.Label,proof.Path,[]);requirements.Add((proof.Code,proof.RiskItemId),current);
                }
                current.Dates.Add(slice.EffectiveAt);
            }
        }
        var scheduleHash=Hash(new{format="commercial-servicing-proof-schedule-1",schedule});
        return requirements.OrderBy(x=>x.Key.Code,StringComparer.Ordinal).ThenBy(x=>x.Key.Id).Select(x=>new ServicingProofRequirement(
            x.Key.Code,x.Value.Label,x.Value.Path,x.Key.Id,x.Value.Dates.AsReadOnly(),
            Hash(new{format="commercial-servicing-proof-1",context.DraftId,context.CycleId,context.RevisionId,context.RatingId,context.InputHash,
                scheduleHash,code=x.Key.Code,riskItemId=x.Key.Id,effectiveDates=x.Value.Dates})){Context=context}).ToArray();
    }
    private static string Hash<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static ArgumentException Invalid()=>new("Commercial servicing proof requires coherent current pins and a complete ordered commercial schedule.");
}
