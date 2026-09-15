using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Identity;

public sealed record IdentityReference(Guid UserId, Guid? AgencyId);
public sealed record LockedIdentity(StaffUser User, IReadOnlyList<Role> Roles);

// The initial reference is only a lookup hint. Recheck it after acquiring locks
// in agency -> user -> roles order, before credentials or sessions are touched.
public static class IdentitySnapshot
{
    public static Task<IdentityReference?> Reference(BackOfficeDbContext db, Guid userId, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction != null) throw new InvalidOperationException("Read the identity hint before starting its lock transaction.");
        return db.Set<StaffUser>().AsNoTracking().Where(x => x.Id == userId).Select(x => new IdentityReference(x.Id, x.AgencyId)).SingleOrDefaultAsync(token);
    }
    public static async Task<LockedIdentity?> Lock(BackOfficeDbContext db, IdentityReference reference, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Identity resolution requires a held transaction.");
        Agency? agency = null;
        if (reference.AgencyId is Guid agencyId)
            agency = await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(HOLDLOCK,ROWLOCK) WHERE Id={agencyId}").AsNoTracking().SingleOrDefaultAsync(token);
        var user = await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK,ROWLOCK) WHERE Id={reference.UserId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (user == null || user.AgencyId != reference.AgencyId || user.State != "active") return null;
        var links = await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={user.Id}").AsNoTracking().ToListAsync(token);
        var roles = new List<Role>();
        foreach (var link in links.OrderBy(x => x.RoleId))
            roles.Add(await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").AsNoTracking().SingleAsync(token));
        if (roles.Count == 0) return null;
        if (reference.AgencyId == null)
        {
            if (roles.Any(x => x.Scope != "internal")) return null;
        }
        else if (agency == null || AgencyAccessRules.ActiveRole(user.AgencyId, user.State, agency.State, roles.Select(x => new AgencyIdentityRole(x.Code, x.Scope)).ToArray()) == null) return null;
        return new(user, roles);
    }
}
