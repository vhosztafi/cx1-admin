using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
internal static class ClaimsAuthority
{
    internal static async Task<ClaimsSnapshot> Hold(BackOfficeDbContext db,ActorContext actor,ClaimsHandoff handoff,IncidentOccurrenceResolver resolver,CancellationToken token)
    {
        var incident=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==handoff.IncidentId&&x.PolicyId==handoff.PolicyId,token)??throw ClaimsSnapshots.Missing();
        var snapshot=await ClaimsSnapshots.Capture(db,actor,incident,handoff.RevisionId,handoff.ResolutionId,handoff.AdministratorId,resolver,token);
        if(ClaimsSnapshots.Serialize(snapshot)!=handoff.RequestJson||DeliverySnapshots.Hash(handoff.RequestJson)!=handoff.RequestHash||snapshot.SourceVersionId!=handoff.SourceVersionId)throw ClaimsSnapshots.Invalid();
        return snapshot;
    }
    internal static async Task<ClaimsSnapshot> HoldSender(BackOfficeDbContext db,ClaimsRequest request,ClaimsHandoff handoff,IncidentOccurrenceResolver resolver,CancellationToken token)
    {
        var user=await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.CreatedBy,token);
        if(user is not{State:"active",AgencyId:null})throw new OperationalAccessException(403,"claims-sender-unavailable");
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code).ToArrayAsync(token);
        var actor=new ActorContext(user.Id,user.TeamId,null,roles.ToHashSet(StringComparer.Ordinal));return await Hold(db,actor,handoff,resolver,token);
    }
    internal static bool Matches(ClaimsRequest request,ClaimsHandoff handoff,OutboxWork work,JobLease lease)
    {
        if(work.Id!=request.WorkId||work.Kind!=ClaimsHandoffService.WorkKind||work.SubjectRecordId!=request.Id||request.HandoffId!=handoff.Id||
            work.ScenarioVersionId!=request.ScenarioVersionId||lease.ScenarioVersionId!=request.ScenarioVersionId||lease.Kind!=work.Kind||
            work.OperationKey!=$"operational-claims/{request.Id:N}"||lease.OperationKey!=work.OperationKey||work.Payload!=request.PayloadJson||lease.Payload!=request.PayloadJson||DeliverySnapshots.Hash(request.PayloadJson)!=request.PayloadHash)return false;
        try{using var doc=JsonDocument.Parse(request.PayloadJson);var root=doc.RootElement;return root.GetProperty("format").GetString()=="claims-operation-2"&&root.GetProperty("requestId").GetGuid()==request.Id&&root.GetProperty("handoffId").GetGuid()==handoff.Id&&root.GetProperty("purpose").GetString()==request.Purpose&&root.GetProperty("handoffHash").GetString()==handoff.RequestHash;}
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){return false;}
    }
}
