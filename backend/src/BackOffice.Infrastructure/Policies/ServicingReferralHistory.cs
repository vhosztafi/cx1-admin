using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingDecisionView(Guid Id,int Sequence,string Outcome,string Reason,string? Question,Guid ActorId,Guid AuthorityVersionId,Guid GrantId,
    DateTimeOffset DecidedAt,JsonElement Conditions);
public sealed record ServicingDecisionPage(Guid ReferralId,Guid CycleId,IReadOnlyList<ServicingDecisionView> Items,int? NextAfterSequence);

public sealed partial class ServicingReferralService
{
    public async Task<ServicingDecisionPage> DecisionsAsync(ActorContext actor,Guid draftId,Guid referralId,int afterSequence=0,int pageSize=50,CancellationToken token=default)
    {
        if(referralId==Guid.Empty || afterSequence<0 || pageSize is <1 or >50) throw new QuoteOperationException(422,"servicing-referral-page-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await ServicingDraftService.HoldDraft(db,actor,draftId,false,token);
        var referral=await db.Set<ServicingReferral>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==referralId && x.DraftId==draftId,token)
            ??throw new QuoteOperationException(404,"servicing-referral-not-found");
        var rows=await db.Set<ServicingReferralDecision>().AsNoTracking().Where(x=>x.DraftId==draftId && x.ReferralId==referralId && x.Sequence>afterSequence)
            .OrderBy(x=>x.Sequence).Take(pageSize+1).ToArrayAsync(token);
        var items=rows.Take(pageSize).Select(DecisionView).ToArray();
        await tx.CommitAsync(token);return new(referralId,referral.CycleId,items,rows.Length>pageSize?items[^1].Sequence:null);
    }

    private static ServicingDecisionView DecisionView(ServicingReferralDecision x)=>new(x.Id,x.Sequence,x.Outcome,x.Reason,x.Question,x.ActorId,x.AuthorityVersionId,x.GrantId,
        x.DecidedAt,JsonSerializer.Deserialize<JsonElement>(x.ConditionsJson));
}
