using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public static class AgencyUserAuthority
{
    public static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid agencyId, bool allowBroker, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("User authority requires the command transaction.");
        if (actor.AgencyId != null)
        {
            if (!allowBroker) throw Denied();
            await AgencyScope.Resolve(db, actor, agencyId, "agency-user-manage", token);
            return;
        }
        if (!actor.HasCapability("agency-admin")) throw Denied();
        var agency = await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={agencyId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (agency is null) throw new AgencyCommandException(404, "agency-not-found");
        var user = await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK,ROWLOCK) WHERE Id={actor.UserId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (user is null || user.AgencyId != null || user.State != "active") throw Denied();
        var links = await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={actor.UserId}").AsNoTracking().ToListAsync(token);
        var roles = new List<Role>();
        foreach (var link in links.OrderBy(x => x.RoleId)) roles.Add(await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").AsNoTracking().SingleAsync(token));
        if (roles.Count == 0 || roles.Any(x => x.Scope != "internal") || !actor.Roles.SetEquals(roles.Select(x => x.Code))) throw Denied();
    }
    public static async Task<string> ReadScope(BackOfficeDbContext db, ActorContext actor, Guid agencyId, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null || db.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("User reads require a serializable transaction through materialization.");
        await Authorize(db, actor, agencyId, true, token);
        var version = await db.Set<Agency>().AsNoTracking().Where(x => x.Id == agencyId).Select(x => x.RowVersion).SingleAsync(token);
        var stamp = await db.Set<StaffUser>().AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.SecurityStamp).SingleAsync(token);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { version, stamp })));
    }
    private static AgencyCommandException Denied() => new(403, "agency-scope-denied");
}
