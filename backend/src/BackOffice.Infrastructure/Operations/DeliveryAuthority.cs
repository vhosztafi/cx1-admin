using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

internal static class DeliveryAuthority
{
    internal static async Task<DeliveryContent> HoldSender(BackOfficeDbContext db,OperationalDelivery delivery,CancellationToken token)
    {
        var user=await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==delivery.CreatedBy,token);
        if(user is null||user.State!="active"||user.AgencyId is not null)throw new OperationalAccessException(403,"delivery-sender-unavailable");
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code).ToArrayAsync(token);
        var actor=new ActorContext(user.Id,user.TeamId,null,roles.ToHashSet(StringComparer.Ordinal));
        var held=await OperationalScope.HoldSubjects(db,actor,[delivery.SubjectId],delivery.MessageVersionId is null?"document-send":"message-send",token);
        var content=JsonSerializer.Deserialize<DeliveryContent>(delivery.ContentJson,CommunicationScope.Json)??throw new OperationalAccessException(409,"invalid-delivery-snapshot");
        if(content.Format!="operational-delivery-1"||content.SubjectId!=delivery.SubjectId||content.RelationshipId!=delivery.RelationshipId||content.Recipients is null||content.Attachments is null||
            DeliverySnapshots.Hash(delivery.ContentJson)!=delivery.ContentHash)throw new OperationalAccessException(409,"invalid-delivery-snapshot");
        var cancellation=await db.Set<CancellationNoticeDispatch>().AsNoTracking().SingleOrDefaultAsync(x=>x.DeliveryId==delivery.Id,token);
        var current=cancellation is null
            ? await DeliverySnapshots.Capture(db,actor,held.Subjects.Single(),delivery.RelationshipId,content.Subject,
                new(content.Body,content.Recipients.Select(x=>x.ContactId).ToArray(),content.Attachments.Select(x=>x.VersionId).ToArray()),token)
            : await CancellationNoticeDelivery.Capture(db,actor,cancellation.ConsequenceId,cancellation.DocumentVersionId,token);
        if(DeliverySnapshots.Serialize(current)!=delivery.ContentJson)throw new OperationalAccessException(409,"delivery-context-changed");
        return content;
    }
    internal static bool MatchesWork(OperationalDelivery delivery,OutboxWork work,JobLease lease)=>
        work.Id==delivery.WorkId&&work.Kind==MessageDeliveryService.WorkKind&&lease.Kind==work.Kind&&work.SubjectRecordId==delivery.Id&&
        work.ScenarioVersionId==delivery.ScenarioVersionId&&lease.ScenarioVersionId==delivery.ScenarioVersionId&&
        work.OperationKey==$"operational-delivery/{delivery.Id:N}"&&lease.OperationKey==work.OperationKey&&
        work.Payload==delivery.ContentJson&&lease.Payload==delivery.ContentJson&&DeliverySnapshots.Hash(work.Payload)==delivery.ContentHash;
}
