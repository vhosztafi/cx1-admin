using System.Security.Cryptography;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);

    public Task<CommandOutcome> CreateAsync(ActorContext actor, Guid draftId, Guid cycleId, Guid referralId,
        byte[] version, byte[] referralVersion, Guid lease, string reason, string key, Guid correlation,
        CancellationToken token = default)
    {
        if (draftId == Guid.Empty || cycleId == Guid.Empty || referralId == Guid.Empty || lease == Guid.Empty ||
            version is null || version.Length != 8 || referralVersion is null || referralVersion.Length != 8 ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            throw new QuoteOperationException(422, "servicing-capacity-input-invalid");
        reason = QuoteRatingService.Reason(reason); version = version.ToArray(); referralVersion = referralVersion.ToArray();
        ServicingDecisionContext? held = null; ServicingReferral? referral = null; Guid providerId = Guid.Empty;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/drafts/{draftId:D}/capacity", key, correlation),
            new { draftId, cycleId, referralId, version = Convert.ToBase64String(version),
                referralVersion = Convert.ToBase64String(referralVersion), lease, reason }, "servicing.capacity-created",
            async (db, ct) =>
            {
                var now = time.GetUtcNow();
                held = await ServicingDecisionContext.Hold(db, actor, draftId, "underwriting-escalate", now, ct, cycleId);
                if (held.Rating.ExpiresAt <= now) throw new QuoteOperationException(409, "servicing-rating-expired");
                await new ServicingDraftService(factory, time).DemandLease(db, draftId, held.Scope.Source.Scope.Actor.UserId, lease, ct);
                var remaining = held.Input.Term with { Kind = "short-period", StartsAt = held.Input.Slices[0].EffectiveAt };
                var grants = await QuoteUnderwritingScope.GrantsAsync(db, held.Scope.Source, held.Cycle.ProductVersionId,
                    held.Scope.Eligible.BinderVersion, held.Scope.Eligible.Capture.Product.Code, remaining, now, ct);
                // A current effective grant is required even for escalation and
                // receipt replay. Escalation itself does not approve excess cover.
                if (grants.Count == 0) throw new QuoteOperationException(403, "servicing-capacity-authority-required");
                referral = await db.Set<ServicingReferral>().FromSqlInterpolated($"SELECT * FROM ServicingReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={referralId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "servicing-referral-not-found");
                if (referral.State is "superseded" or "declined") throw new QuoteOperationException(409, "servicing-referral-reopen-required");
                providerId = held.Scope.Eligible.BinderVersion.ProviderId;
                if (!await db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={providerId} AND State='active'").AsNoTracking().AnyAsync(ct))
                    throw new QuoteOperationException(409, "servicing-capacity-provider-unavailable");
            },
            async (db, ct) =>
            {
                await held!.Current(db, factory, time, version, lease, ct);
                if (!CryptographicOperations.FixedTimeEquals(referral!.RowVersion, referralVersion))
                    throw new QuoteOperationException(412, "servicing-referral-stale");
                if (await db.Set<ServicingCapacityCase>().AnyAsync(x => x.ReferralId == referralId, ct))
                    throw new QuoteOperationException(409, "servicing-capacity-case-exists");
                var now = time.GetUtcNow();
                var row = new ServicingCapacityCase { DraftId = draftId, CycleId = cycleId, RevisionId = held.Cycle.RevisionId,
                    RatingId = held.Rating.Id, ReferralId = referralId, ProviderId = providerId, BinderVersionId = held.Cycle.BinderVersionId,
                    RaisedBy = held.Scope.Source.Scope.Actor.UserId, Reason = reason, CreatedBy = held.Scope.Source.Scope.Actor.UserId,
                    CreatedAt = now, UpdatedAt = now };
                db.Add(row);
                return await held.Receipt(db, row.Id, 201, now, ct);
            }, token);
    }
}
