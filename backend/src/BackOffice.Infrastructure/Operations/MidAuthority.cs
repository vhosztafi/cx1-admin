using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
internal static class MidAuthority
{
    internal static async Task Hold(BackOfficeDbContext db,ActorContext actor,MidSubmission submission,string capability,CancellationToken token)
    {
        await OperationalScope.HoldParents(db,actor,[new("policy",submission.PolicyId)],capability,token);
        var snapshot=await MidSnapshots.Capture(db,submission.WorkId,token);
        if(snapshot.VersionId!=submission.VersionId||snapshot.BaseVersionId!=submission.BaseVersionId||MidSnapshots.Serialize(snapshot)!=submission.RequestJson||MidSnapshots.Hash(submission.RequestJson)!=submission.RequestHash)throw MidSnapshots.Invalid();
    }
    internal static async Task<ActorContext> Sender(BackOfficeDbContext db,Guid? userId,CancellationToken token)
    {
        var user=await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==userId,token);
        if(user is not{State:"active",AgencyId:null})throw new OperationalAccessException(403,"mid-sender-unavailable");
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role.Code).ToArrayAsync(token);
        return new(user.Id,user.TeamId,null,roles.ToHashSet(StringComparer.Ordinal));
    }
    internal static async Task HoldSender(BackOfficeDbContext db,MidSubmission submission,CancellationToken token)=>await Hold(db,await Sender(db,submission.CreatedBy,token),submission,"mid-retry",token);
    internal static async Task<bool> Matches(BackOfficeDbContext db,MidSubmission submission,OutboxWork work,JobLease lease,CancellationToken token)
    {
        var mid=await db.Set<PolicyMidIntent>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==submission.PolicyMidIntentId,token);
        var cancellation=await db.Set<CancellationConsequence>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==submission.CancellationConsequenceId,token);
        var payload=mid?.PayloadJson??cancellation?.PayloadJson;var kind=mid is not null?"mid-update":"cancellation-mid-removal";
        var key=mid is not null?$"mid-update/{mid.Id:N}":$"cancellation-mid-removal/{submission.TransactionId:N}";
        return work.Id==submission.WorkId&&work.Kind==kind&&work.SubjectRecordId==(mid?.Id??cancellation?.Id)&&work.OperationKey==key&&
            work.Payload==payload&&work.CreatedBy==submission.CreatedBy&&lease.WorkId==work.Id&&lease.Kind==work.Kind&&lease.Payload==work.Payload&&
            lease.OperationKey==work.OperationKey&&lease.ScenarioVersionId==work.ScenarioVersionId&&work.ScenarioVersionId is not null;
    }
}
