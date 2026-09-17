using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal sealed record ServicingDecisionContext(HeldServicingRating Scope,ServicingCycle Cycle,ServicingRatingResult Rating,
    ServicingRatingRequestInput Input)
{
    internal static async Task<ServicingDecisionContext> Hold(BackOfficeDbContext db,ActorContext actor,Guid draftId,
        string capability,DateTimeOffset now,CancellationToken token,Guid? requestedCycle=null)
    {
        if (!actor.HasCapability(capability)) throw new QuoteOperationException(403,"servicing-evidence-denied");
        var scope=await ServicingRatingScope.HoldAsync(db,actor,draftId,now,token);
        if (!scope.Source.Scope.Actor.HasCapability(capability)) throw new QuoteOperationException(403,"servicing-evidence-denied");
        if (requestedCycle is { } requested && !await db.Set<ServicingCycle>().AnyAsync(x=>x.Id==requested && x.DraftId==draftId,token))
            throw new QuoteOperationException(404,"servicing-cycle-not-found");
        var id=scope.Draft.CurrentCycleId ?? throw new QuoteOperationException(409,"servicing-rating-required");
        if (requestedCycle is { } expected && expected!=id) throw new QuoteOperationException(409,"servicing-rating-cycle-stale");
        var cycle=await db.Set<ServicingCycle>().FromSqlInterpolated($"SELECT * FROM ServicingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={id} AND DraftId={draftId}").SingleAsync(token);
        ServicingRatingRequestInput input;
        try { input=ServicingRatingInput.Read(cycle.InputJson,cycle.InputHash); }
        catch (ArgumentException) { throw new QuoteOperationException(409,"servicing-rating-input-unavailable"); }
        if (!ServicingRatingScope.Matches(scope,cycle,input) || cycle.State!="rated" || cycle.CurrentRatingId is null)
            throw new QuoteOperationException(409,"servicing-rating-cycle-stale");
        var latest=await db.Set<PolicyVersion>().Where(x=>x.PolicyId==scope.Draft.PolicyId && x.TermId==scope.Draft.BaseTermId)
            .OrderByDescending(x=>x.EffectiveAt).ThenByDescending(x=>x.Sequence).Select(x=>x.Id).FirstAsync(token);
        if (latest!=cycle.BaseVersionId) throw new QuoteOperationException(409,"servicing-base-stale");
        var rating=await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId && x.CycleId==cycle.Id && x.DraftId==draftId && x.RevisionId==cycle.RevisionId,token);
        if (rating.Outcome!="rated") throw new QuoteOperationException(409,"servicing-rating-required");
        return new(scope,cycle,rating,input);
    }

    internal async Task Current(BackOfficeDbContext db,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time,
        byte[] version,Guid lease,CancellationToken token)
    {
        if (version.Length!=8) throw new QuoteOperationException(400,"servicing-evidence-input-invalid");
        if (!CryptographicOperations.FixedTimeEquals(Scope.Draft.RowVersion,version)) throw new QuoteOperationException(412,"servicing-version-conflict");
        if (Rating.ExpiresAt<=time.GetUtcNow()) throw new QuoteOperationException(409,"servicing-rating-expired");
        await new ServicingDraftService(factory,time).DemandLease(db,Scope.Draft.Id,Scope.Source.Scope.Actor.UserId,lease,token);
    }

    internal async Task<CommandOutcome> Receipt(BackOfficeDbContext db,Guid id,int status,DateTimeOffset now,CancellationToken token)
    {
        var draft=Scope.Draft;draft.UpdatedAt=now;db.Entry(draft).Property(x=>x.UpdatedAt).IsModified=true;
        await db.SaveChangesAsync(token);var etag="\""+Convert.ToBase64String(draft.RowVersion)+"\"";
        return new(id,status,JsonSerializer.Serialize(new {id,draftId=draft.Id,revisionId=Cycle.RevisionId,cycleId=Cycle.Id,draftEtag=etag}),Etag:etag);
    }
}
