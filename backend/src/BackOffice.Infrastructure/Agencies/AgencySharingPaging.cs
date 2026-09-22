using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public static partial class AgencySharingService
{
    // Callers retain the same serializable transaction through cursor validation,
    // projection materialization and audit. Fingerprints contain public projection
    // values, not hidden flag reasons, person master data or marketing consent.
    public static Task<string> PreviewPageScope(BackOfficeDbContext db, ActorContext actor, Guid agencyId, string section, AgencySharingQuery query, CancellationToken token = default)
        => PageScope(db,actor,agencyId,section,query,true,token);

    public static Task<string> PageScope(BackOfficeDbContext db, ActorContext actor, Guid agencyId, string section, AgencySharingQuery query, CancellationToken token = default)
        => PageScope(db,actor,agencyId,section,query,false,token);

    private static async Task<string> PageScope(BackOfficeDbContext db, ActorContext actor, Guid agencyId, string section, AgencySharingQuery query, bool preview, CancellationToken token)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Sharing cursor scope requires a transaction.");
        RequireSerializableIfPresent(db);
        if (preview) await AuthorizePreview(db, actor, agencyId, token);
        else await AgencyScope.Resolve(db, actor, agencyId, "agency-sharing-read", token);
        if (query.Search?.Length > 200 || query.RelationshipId == Guid.Empty) throw new AgencyCommandException(400, "invalid-sharing-query");
        query = query with { Search = query.Search?.Trim() };
        object visible;
        switch (section)
        {
            case "open-items":
                visible = await OpenItemRows(db, agencyId, query, token);
                break;
            case "policies":
                visible = await PolicyRows(db, agencyId, query).ToListAsync(token);
                break;
            case "quotes":
                visible = await QuoteRows(db, agencyId, query).ToListAsync(token);
                break;
            case "clients":
                visible = (await ClientRows(db, agencyId, query).ToListAsync(token)).Select(x => new { x.Id, x.RelationshipId, x.Reference, x.LegalName }).ToArray();
                break;
            case "contacts":
                visible = (await ContactRows(db, agencyId, query).ToListAsync(token)).Select(x => new { x.Id, x.PersonId, x.FullName, x.Role, x.Email, x.Telephone, x.IsPrimary }).ToArray();
                break;
            case "instructions":
                visible = (await InstructionRows(db, agencyId, query).ToListAsync(token)).Select(x => new { x.Id, x.PersonId, x.ContactName, x.Instruction, x.ReviewOn }).ToArray();
                break;
            default: throw new AgencyCommandException(400, "invalid-sharing-section");
        }
        var agencyVersion = await db.Set<Agency>().AsNoTracking().Where(x => x.Id == agencyId).Select(x => x.RowVersion).SingleAsync(token);
        var stamp = await db.Set<StaffUser>().AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.SecurityStamp).SingleAsync(token);
        var grants = await db.Set<AgencyPermissionGrant>().AsNoTracking().Where(x => x.AgencyId == agencyId).OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToListAsync(token);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { agencyId, section, agencyVersion, stamp, grants, visible })));
    }
}
