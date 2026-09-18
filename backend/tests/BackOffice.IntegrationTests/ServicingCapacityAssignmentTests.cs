using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacityAssignment(BackOfficeDbContext db,DecisionFixture f,ServicingCapacityCase capacity,Guid editLease)
    {
        var service=new ServicingCapacityService(f.Factory,f.Clock);
        var version=await db.Set<ServicingDraft>().Where(x=>x.Id==capacity.DraftId).Select(x=>x.RowVersion).SingleAsync();
        var child=await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.RowVersion).SingleAsync();
        var senior=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");
        Task<CommandOutcome> Assign(Guid target,byte[] expected,string key)=>service.AssignAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,
            capacity.Id,expected,child,editLease,target,"Route fictional carrier case to eligible senior",key,Guid.NewGuid());
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Assign(f.Underwriter.UserId,version,Guid.NewGuid().ToString()))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Assign(Guid.NewGuid(),version,Guid.NewGuid().ToString()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Assign(senior.Id,new byte[8],Guid.NewGuid().ToString()))).Status);
        var before=await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==capacity.Id);
        var grants=await db.Set<UserAuthorityGrant>().CountAsync();var key=Guid.NewGuid().ToString();
        var assigned=await Assign(senior.Id,version,key);Assert.Equal(200,assigned.Status);Assert.True((await Assign(senior.Id,version,key)).Replayed);
        Assert.Equal(senior.Id,await db.Set<ServicingReferral>().Where(x=>x.Id==capacity.ReferralId).Select(x=>x.AssignedUserId).SingleAsync());
        var after=await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==capacity.Id);
        Assert.Equal(before.State,after.State);Assert.Equal(before.CurrentResponseId,after.CurrentResponseId);Assert.Equal(grants,await db.Set<UserAuthorityGrant>().CountAsync());
        var roles=await db.Set<UserRole>().AsNoTracking().Where(x=>x.UserId==senior.Id).ToArrayAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={senior.Id}");
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Assign(senior.Id,version,key))).Status);
        db.ChangeTracker.Clear();db.AddRange(roles);await db.SaveChangesAsync();
    }
}
