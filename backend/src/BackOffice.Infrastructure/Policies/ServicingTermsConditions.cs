using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingTermsService
{
    internal static async Task RequireConditionTerms(BackOfficeDbContext db,ServicingDecisionContext held,ReferralCondition condition,CancellationToken token)
    {
        if(condition.TermsVersionId is not {} id)return;
        var terms=await CurrentTerms(db,held,id,held.AssessedAt,token);
        if(condition.Code!="provide-signed-statement" || condition.TermsHash!=terms.TermsHash)
            throw new QuoteOperationException(409,"servicing-condition-terms-stale");
    }
}
