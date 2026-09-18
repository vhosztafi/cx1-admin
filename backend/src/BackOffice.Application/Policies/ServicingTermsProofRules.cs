using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application.Underwriting;

namespace BackOffice.Application.Policies;

public static class ServicingTermsProofRules
{
    public static ServicingProofRequirement Requirement(ServicingProofContext context,ServicingTermsSubject terms,string code,
        IReadOnlyList<DateTimeOffset> effectiveDates)
    {
        if(context is null || context.Pins is null || terms is null || code is not ("signed-statement" or "acceptance-proof") ||
            new[]{terms.DraftId,terms.CycleId,terms.RevisionId,terms.BaseVersionId,terms.RatingId,terms.TermsId}.Contains(Guid.Empty) ||
            context.DraftId!=terms.DraftId || context.CycleId!=terms.CycleId || context.RevisionId!=terms.RevisionId || context.RatingId!=terms.RatingId ||
            !ReferralRules.Hash(context.InputHash) || !ReferralRules.Hash(terms.TermsHash) || effectiveDates is null || effectiveDates.Count is <1 or >100)
            throw Invalid();
        DateTimeOffset? prior=null;
        foreach(var date in effectiveDates)
        {
            if(date.Offset!=TimeSpan.Zero || prior is not null && date<=prior) throw Invalid();
            prior=date;
        }
        var dates=effectiveDates.ToArray();
        var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{
            format="servicing-terms-proof-1",context,terms,code,effectiveDates=dates})));
        return new(code,code=="signed-statement"?"Signed servicing statement":"Servicing terms acceptance evidence",
            $"/terms/{terms.TermsId:D}",null,dates,hash){Context=context,TermsVersionId=terms.TermsId};
    }

    private static ArgumentException Invalid()=>new("Terms proof requires exact current contract ownership, purpose and ordered effective dates.");
}
