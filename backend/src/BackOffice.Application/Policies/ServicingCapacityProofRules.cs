using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public static class ServicingCapacityProofRules
{
    public static ServicingProofRequirement Requirement(ServicingProofContext context,Guid submissionId,string submissionHash,
        IReadOnlyList<DateTimeOffset> effectiveDates)
    {
        if(context is null || context.Pins is null || new[]{context.DraftId,context.CycleId,context.RevisionId,context.RatingId,submissionId}.Contains(Guid.Empty) ||
            !ReferralRules.Hash(context.InputHash) || !ReferralRules.Hash(submissionHash) || effectiveDates is null || effectiveDates.Count is <1 or >100)
            throw Invalid();
        DateTimeOffset? prior=null;
        foreach(var date in effectiveDates)
        {
            if(date.Offset!=TimeSpan.Zero || prior is not null && date<=prior) throw Invalid();
            prior=date;
        }
        var dates=effectiveDates.ToArray();
        var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            format="servicing-capacity-proof-1",context,submissionId,submissionHash,effectiveDates=dates })));
        return new("capacity-response","Capacity provider response",$"/capacity/submissions/{submissionId:D}",null,dates,hash)
            {Context=context,CapacitySubmissionId=submissionId};
    }

    private static ArgumentException Invalid()=>new("Capacity response proof requires exact submission ownership and an ordered current risk schedule.");
}
