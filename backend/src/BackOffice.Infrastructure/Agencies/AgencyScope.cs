using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed record ResolvedAgencyActor(Guid UserId,Guid AgencyId,string Role);

// Caller must retain the transaction through its scoped query/command. This is not
// an endpoint and does not enable broker authentication or broaden PartyScope.
public static class AgencyScope
{
    public static async Task<ResolvedAgencyActor> Resolve(BackOfficeDbContext db,ActorContext actor,Guid requestedAgency,string capability,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Agency scope requires the caller's transaction.");
        if(actor.AgencyId is not Guid agencyId||agencyId==Guid.Empty||agencyId!=requestedAgency)throw Denied();
        // Agency first, then identity and role rows: suspension/user changes cannot
        // commit between this authorization and the caller's protected read/write.
        var agencyQuery=capability is "agency-user-manage" or "agency-permission-request"
            ?db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={agencyId}")
            :db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(HOLDLOCK,ROWLOCK) WHERE Id={agencyId}");
        var agency=await agencyQuery.AsNoTracking().SingleOrDefaultAsync(token);
        var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK,ROWLOCK) WHERE Id={actor.UserId}").AsNoTracking().SingleOrDefaultAsync(token);
        if(agency is null||user is null||user.AgencyId!=agencyId)throw Denied();
        var links=await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={user.Id}").AsNoTracking().ToListAsync(token);
        var roles=new List<AgencyIdentityRole>();
        foreach(var link in links.OrderBy(x=>x.RoleId))
        {
            var role=await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").AsNoTracking().SingleAsync(token);
            roles.Add(new(role.Code,role.Scope));
        }
        var current=AgencyAccessRules.ActiveRole(user.AgencyId,user.State,agency.State,roles);
        if(current is null||!actor.Roles.SetEquals(roles.Select(x=>x.Code))||!AgencyAccessRules.Allows(current,capability))throw Denied();
        return new(user.Id,agencyId,current);
    }
    private static AgencyCommandException Denied()=>new(403,"agency-scope-denied");
}
