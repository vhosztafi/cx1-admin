using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityService
{
    public Task<CommandOutcome> AssignAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid caseId,byte[] version,byte[] caseVersion,
        Guid lease,Guid assignedUserId,string reason,string key,Guid correlation,CancellationToken token=default)
    {
        if(draftId==Guid.Empty || cycleId==Guid.Empty || caseId==Guid.Empty || assignedUserId==Guid.Empty || lease==Guid.Empty ||
            version is null || version.Length!=8 || caseVersion is null || caseVersion.Length!=8 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length<10)
            throw new QuoteOperationException(422,"servicing-capacity-assignment-invalid");
        reason=QuoteRatingService.Reason(reason);version=version.ToArray();caseVersion=caseVersion.ToArray();
        ServicingDecisionContext? held=null;ServicingCapacityCase? capacity=null;ServicingReferral? referral=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/capacity/{caseId:D}/assignment",key,correlation),
            new{draftId,cycleId,caseId,version=Convert.ToBase64String(version),caseVersion=Convert.ToBase64String(caseVersion),lease,assignedUserId,reason},
            "servicing.capacity-assigned",
            async(db,ct)=>
            {
                held=await HoldEscalationAuthority(db,actor,draftId,cycleId,lease,ct);
                capacity=await db.Set<ServicingCapacityCase>().FromSqlInterpolated($"SELECT * FROM ServicingCapacityCase WITH(UPDLOCK,HOLDLOCK) WHERE Id={caseId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-capacity-case-not-found");
                if(capacity.State=="superseded" || capacity.RatingId!=held.Rating.Id || capacity.ProviderId!=held.Scope.Eligible.BinderVersion.ProviderId ||
                    capacity.BinderVersionId!=held.Cycle.BinderVersionId) throw new QuoteOperationException(409,"servicing-capacity-case-stale");
                referral=await db.Set<ServicingReferral>().FromSqlInterpolated($"SELECT * FROM ServicingReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={capacity.ReferralId}").SingleAsync(ct);
                if(referral.State=="superseded") throw new QuoteOperationException(409,"servicing-capacity-case-stale");
                var identity=await IdentitySnapshot.Lock(db,new IdentityReference(assignedUserId,null),ct);
                if(identity is null || !identity.Roles.Any(x=>x.Code=="senior-underwriter" && x.Scope=="internal"))
                    throw new QuoteOperationException(409,"servicing-capacity-assignee-unavailable");
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);
                if(!CryptographicOperations.FixedTimeEquals(capacity!.RowVersion,caseVersion)) throw new QuoteOperationException(412,"servicing-capacity-case-stale");
                var now=time.GetUtcNow();var previous=referral!.AssignedUserId;
                referral.AssignedUserId=assignedUserId;referral.UpdatedAt=now;capacity.UpdatedAt=now;
                db.Entry(capacity).Property(x=>x.UpdatedAt).IsModified=true;
                db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,SubjectRecordId=caseId,
                    EventType="servicing.capacity-assignment-detail",Reason=reason,Before=JsonSerializer.Serialize(new{assignedUserId=previous}),
                    After=JsonSerializer.Serialize(new{assignedUserId}),CorrelationId=correlation});
                return await held.Receipt(db,caseId,200,now,ct);
            },token);
    }
}
