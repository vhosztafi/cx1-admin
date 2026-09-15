using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencySharedClient(Guid Id, Guid RelationshipId, string Reference, string LegalName, string EntityType, string? CompanyNumber);
public sealed record AgencySharedContact(Guid Id, Guid ClientId, Guid RelationshipId, Guid PersonId, string FullName, string? FirstName, string? Surname, string Role, string? Email, string? Telephone, bool IsPrimary);
public sealed record AgencySharedInstruction(Guid Id, Guid ClientId, Guid RelationshipId, Guid PersonId, string Instruction, DateOnly ReviewOn);
public sealed record AgencySharingQuery(string? Search = null, int Offset = 0, int Size = 25, Guid? RelationshipId = null);
public sealed record AgencySharingPage<T>(IReadOnlyList<T> Items, int Total, int Offset, int Size);

// These materialized reads are the common boundary for future external endpoints
// and staff preview. No IQueryable or caller-created scope token escapes it.
public static partial class AgencySharingService
{
    public static Task<AgencySharingPage<AgencySharedClient>> Clients(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, false, "clients", ClientRows, token);
    public static Task<AgencySharingPage<AgencySharedClient>> PreviewClients(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, true, "clients", ClientRows, token);
    public static Task<AgencySharingPage<AgencySharedContact>> Contacts(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, false, "contacts", ContactRows, token);
    public static Task<AgencySharingPage<AgencySharedContact>> PreviewContacts(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, true, "contacts", ContactRows, token);
    public static Task<AgencySharingPage<AgencySharedInstruction>> Instructions(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, false, "instructions", InstructionRows, token);
    public static Task<AgencySharingPage<AgencySharedInstruction>> PreviewInstructions(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, true, "instructions", InstructionRows, token);

    private static async Task<AgencySharingPage<T>> Read<T>(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, bool preview, string section,
        Func<BackOfficeDbContext, Guid, AgencySharingQuery, IQueryable<T>> project, CancellationToken token)
    {
        if (query.Size is < 1 or > 100 || query.Offset < 0 || query.Offset > int.MaxValue - query.Size || query.Search?.Length > 200 || query.RelationshipId == Guid.Empty)
            throw new AgencyCommandException(400, "invalid-sharing-query");
        query = query with { Search = query.Search?.Trim() };
        // Repeatable authorization, membership and grant predicates through count,
        // materialization and audit. Every page resolves current scope afresh.
        RequireSerializableIfPresent(db);
        await using var transaction = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        if (preview) await AuthorizePreview(db, actor, agencyId, token);
        else await AgencyScope.Resolve(db, actor, agencyId, "agency-sharing-read", token);
        var rows = project(db, agencyId, query);
        var total = await rows.CountAsync(token);
        var items = await rows.Skip(query.Offset).Take(query.Size).ToListAsync(token);
        if (preview)
        {
            db.Add(new AuditEvent { ActorId = actor.UserId, CreatedBy = actor.UserId, OccurredAt = DateTimeOffset.UtcNow, EventType = "agency.sharing-preview", CorrelationId = Guid.NewGuid(),
                After = JsonSerializer.Serialize(new { agencyId, section }) });
            await db.SaveChangesAsync(token);
        }
        if (transaction != null) await transaction.CommitAsync(token);
        return new(items, total, query.Offset, query.Size);
    }

    private static void RequireSerializableIfPresent(BackOfficeDbContext db)
    {
        if (db.Database.CurrentTransaction is { } current && current.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Sharing reads require serializable transaction isolation.");
    }

    private static async Task AuthorizePreview(BackOfficeDbContext db, ActorContext actor, Guid agencyId, CancellationToken token)
    {
        if (!actor.HasCapability("agency-read")) throw Denied();
        var agency = await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(HOLDLOCK,ROWLOCK) WHERE Id={agencyId}").AsNoTracking().SingleOrDefaultAsync(token);
        var user = await db.Set<StaffUser>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId, token);
        if (agency?.State != "active" || user is null || user.State != "active" || user.AgencyId != null) throw Denied();
        var roles = await (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId == user.Id select role).AsNoTracking().ToListAsync(token);
        if (roles.Count == 0 || roles.Any(x => x.Scope != "internal") || !actor.Roles.SetEquals(roles.Select(x => x.Code))) throw Denied();
    }

    private static IQueryable<ClientAgencyRelationship> Relationships(BackOfficeDbContext db, Guid agencyId, AgencySharingQuery query)
        => db.Set<ClientAgencyRelationship>().AsNoTracking().Where(r => r.AgencyId == agencyId && r.State == "active" &&
            (query.RelationshipId == null || r.Id == query.RelationshipId));

    private static IQueryable<AgencySharedClient> ClientRows(BackOfficeDbContext db, Guid agencyId, AgencySharingQuery query)
        => (from relationship in Relationships(db, agencyId, query)
            join client in db.Set<ClientAccount>().AsNoTracking() on relationship.ClientId equals client.Id
            where string.IsNullOrEmpty(query.Search) || client.LegalName.Contains(query.Search) || client.Reference.Contains(query.Search) || (client.CompanyNumber != null && client.CompanyNumber.Contains(query.Search))
            orderby client.Reference, relationship.Id
            select new AgencySharedClient(client.Id, relationship.Id, client.Reference, client.LegalName, client.EntityType, client.CompanyNumber));

    private static IQueryable<Contact> ScopedContacts(BackOfficeDbContext db, Guid agencyId, AgencySharingQuery query)
    {
        var relationships = Relationships(db, agencyId, query);
        return db.Set<Contact>().AsNoTracking().Where(c => c.EndedAt == null && relationships.Any(r => r.Id == c.RelationshipId && r.ClientId == c.ClientId));
    }

    private static IQueryable<AgencySharedContact> ContactRows(BackOfficeDbContext db, Guid agencyId, AgencySharingQuery query)
        => ScopedContacts(db, agencyId, query)
            .Where(c => string.IsNullOrEmpty(query.Search) || c.DeclaredFullName.Contains(query.Search) || (c.Email != null && c.Email.Contains(query.Search)))
            .OrderBy(c => c.DeclaredFullName).ThenBy(c => c.Id)
            .Select(c => new AgencySharedContact(c.Id, c.ClientId, c.RelationshipId, c.PersonId, c.DeclaredFullName, c.DeclaredFirstName, c.DeclaredSurname, c.Role, c.Email, c.Telephone, c.IsPrimary));

    private static IQueryable<AgencySharedInstruction> InstructionRows(BackOfficeDbContext db, Guid agencyId, AgencySharingQuery query)
    {
        var relationships = Relationships(db, agencyId, query);
        var contacts = ScopedContacts(db, agencyId, query);
        return (from grant in db.Set<FlagVisibility>().AsNoTracking()
            join flag in db.Set<SupportFlag>().AsNoTracking() on grant.FlagId equals flag.Id
            where flag.ClientId == grant.ClientId && flag.EndedAt == null && flag.AgencyInstruction != null &&
                relationships.Any(r => r.Id == grant.RelationshipId && r.ClientId == grant.ClientId) &&
                contacts.Any(c => c.RelationshipId == grant.RelationshipId && c.ClientId == grant.ClientId && c.PersonId == flag.PersonId) &&
                (string.IsNullOrEmpty(query.Search) || flag.AgencyInstruction.Contains(query.Search))
            orderby flag.Id, grant.RelationshipId
            select new AgencySharedInstruction(flag.Id, flag.ClientId, grant.RelationshipId, flag.PersonId, flag.AgencyInstruction!, flag.ReviewOn));
    }

    private static AgencyCommandException Denied() => new(403, "agency-scope-denied");
}
