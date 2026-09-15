using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyPermissionService(SqlCommandBoundary commands, TimeProvider time)
{
    public Task<CommandOutcome> Request(ActorContext actor, Guid agencyId, string key, byte[] version, string permission, string reason, CancellationToken token = default)
    {
        var input = AgencyPermissionRules.Request(permission, reason);
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/agencies/{agencyId}/permission-requests", key, Guid.NewGuid()), input, "agency.permission-requested",
            async (db, ct) => { await Authorize(db, actor, agencyId, true, true, ct); },
            async (db, ct) =>
            {
                var agency = await db.Set<Agency>().SingleAsync(x => x.Id == agencyId, ct); CheckVersion(version, agency.RowVersion);
                if (await db.Set<AgencyPermissionRequest>().AnyAsync(x => x.AgencyId == agencyId && x.Permission == input.Permission && x.State == "pending", ct))
                    throw new AgencyCommandException(409, "permission-request-pending");
                if (await db.Set<AgencyPermissionGrant>().AnyAsync(x => x.AgencyId == agencyId && x.Permission == input.Permission && x.RevokedAt == null, ct))
                    throw new AgencyCommandException(409, "permission-already-granted");
                var request = new AgencyPermissionRequest { AgencyId = agencyId, Permission = input.Permission, Reason = input.Reason, RequestedBy = actor.UserId, CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow() };
                db.Add(request); Touch(db, agency); Activity(db, actor, agencyId, "agency.permission-requested"); await db.SaveChangesAsync(ct);
                return Outcome(request.Id, 202, agency.RowVersion);
            }, token);
    }

    public Task<CommandOutcome> Decide(ActorContext actor, Guid agencyId, Guid requestId, string key, byte[] version, string decision, string reason, CancellationToken token = default)
    {
        reason = AgencyPermissionRules.Reason(reason);
        if (decision is not ("approve" or "reject")) throw new AgencyCommandException(422, "invalid-permission-decision");
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/agencies/{agencyId}/permission-requests/{requestId}/decision", key, Guid.NewGuid()), new { agencyId, decision, reason }, "agency.permission-decided",
            async (db, ct) =>
            {
                await Authorize(db, actor, agencyId, true, false, ct);
                if (!await db.Set<AgencyPermissionRequest>().AnyAsync(x => x.Id == requestId && x.AgencyId == agencyId, ct)) throw Missing();
            }, async (db, ct) =>
            {
                var request = await db.Set<AgencyPermissionRequest>().SingleAsync(x => x.Id == requestId && x.AgencyId == agencyId, ct);
                CheckVersion(version, request.RowVersion);
                var input = AgencyPermissionRules.Decide(request.RequestedBy, actor.UserId, request.State, decision, reason);
                request.State = input.State; request.DecisionBy = actor.UserId; request.DecisionReason = input.Reason; request.DecidedAt = time.GetUtcNow();
                Touch(db, await db.Set<Agency>().SingleAsync(x => x.Id == agencyId, ct));
                Activity(db, actor, agencyId, "agency.permission-" + input.State);
                await db.SaveChangesAsync(ct); // SQL creates the grant atomically on approval.
                return Outcome(request.Id, 200, request.RowVersion);
            }, token);
    }

    public Task<CommandOutcome> Revoke(ActorContext actor, Guid agencyId, Guid grantId, string key, byte[] version, string reason, CancellationToken token = default)
    {
        reason = AgencyPermissionRules.Reason(reason);
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/agencies/{agencyId}/permission-grants/{grantId}/revoke", key, Guid.NewGuid()), new { agencyId, reason }, "agency.permission-revoked",
            async (db, ct) =>
            {
                await Authorize(db, actor, agencyId, false, false, ct);
                if (!await db.Set<AgencyPermissionGrant>().AnyAsync(x => x.Id == grantId && x.AgencyId == agencyId, ct)) throw Missing();
            }, async (db, ct) =>
            {
                // Agency serialization fences own-user commands; revoke every
                // existing agency session and rotate identities in stable order.
                var users = await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(UPDLOCK,HOLDLOCK) WHERE AgencyId={agencyId}").OrderBy(x => x.Id).ToListAsync(ct);
                var grant = await db.Set<AgencyPermissionGrant>().SingleAsync(x => x.Id == grantId && x.AgencyId == agencyId, ct);
                CheckVersion(version, grant.RowVersion);
                if (grant.RevokedAt != null) throw new AgencyCommandException(409, "permission-already-revoked");
                var now = time.GetUtcNow(); grant.RevokedAt = now; grant.RevokedBy = actor.UserId; grant.RevocationReason = reason;
                foreach (var user in users)
                {
                    user.SecurityStamp = Guid.NewGuid().ToString("N");
                    foreach (var session in await db.Set<UserSession>().Where(x => x.UserId == user.Id && x.RevokedAt == null).ToListAsync(ct)) session.RevokedAt = now;
                }
                Touch(db, await db.Set<Agency>().SingleAsync(x => x.Id == agencyId, ct)); Activity(db, actor, agencyId, "agency.permission-revoked"); await db.SaveChangesAsync(ct);
                return Outcome(grant.Id, 200, grant.RowVersion);
            }, token);
    }

    private static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid agencyId, bool activeRequired, bool brokerRequest, CancellationToken token)
    {
        if (actor.AgencyId != null)
        {
            if (!brokerRequest) throw Denied();
            await AgencyScope.Resolve(db, actor, agencyId, "agency-permission-request", token); return;
        }
        if (!actor.HasCapability("agency-admin")) throw Denied();
        var agency = await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleOrDefaultAsync(token);
        if (agency == null || (activeRequired ? agency.State != "active" : agency.State is not ("active" or "suspended"))) throw Denied();
        var user = await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK,ROWLOCK) WHERE Id={actor.UserId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (user == null || user.AgencyId != null || user.State != "active") throw Denied();
        var links = await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={actor.UserId}").AsNoTracking().ToListAsync(token);
        var roles = new List<Role>();
        foreach (var link in links.OrderBy(x => x.RoleId)) roles.Add(await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").AsNoTracking().SingleAsync(token));
        if (roles.Count == 0 || roles.Any(x => x.Scope != "internal") || !actor.Roles.SetEquals(roles.Select(x => x.Code))) throw Denied();
    }

    private static void Touch(BackOfficeDbContext db, Agency agency) => db.Entry(agency).Property(x => x.UpdatedAt).IsModified = true;
    private void Activity(BackOfficeDbContext db, ActorContext actor, Guid agencyId, string action)
        => db.Add(new AgencyActivity { AgencyId = agencyId, ActorId = actor.UserId, CreatedBy = actor.UserId, Action = action, OccurredAt = time.GetUtcNow() });
    private static void CheckVersion(byte[] expected, byte[] current)
    {
        if (!CryptographicOperations.FixedTimeEquals(expected, current)) throw new AgencyCommandException(412, "stale-agency-permission");
    }
    private static CommandOutcome Outcome(Guid id, int status, byte[] version) => new(id, status, JsonSerializer.Serialize(new { id }), Etag: "\"" + Convert.ToBase64String(version) + "\"");
    private static AgencyCommandException Denied() => new(403, "agency-scope-denied");
    private static AgencyCommandException Missing() => new(404, "agency-permission-not-found");
}
