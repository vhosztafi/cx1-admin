using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Parties;

// AgencyId must come from trusted identity resolution, never a request field. Local
// authentication still refuses external accounts; these projections are also tested directly.
public sealed class PartyScope(ActorContext actor)
{
    private bool InternalRead => actor.HasCapability("client-read");
    private bool AgencyRead => actor.AgencyId is not null && actor.Roles.Contains("agency-admin");

    public IQueryable<ClientAgencyRelationship> Relationships(BackOfficeDbContext db)
    {
        var query=db.Set<ClientAgencyRelationship>().AsNoTracking();
        if (InternalRead) return query;
        return AgencyRead ? query.Where(x => x.AgencyId==actor.AgencyId && x.State=="active") : query.Where(x => false);
    }

    public IQueryable<ClientAccount> Clients(BackOfficeDbContext db)
    {
        var query=db.Set<ClientAccount>().AsNoTracking();
        if (InternalRead) return query;
        var relationships=Relationships(db);
        return query.Where(x => relationships.Any(r => r.ClientId==x.Id));
    }

    public IQueryable<Agency> Agencies(BackOfficeDbContext db)
    {
        var query=db.Set<Agency>().AsNoTracking();
        if (InternalRead) return query;
        return AgencyRead ? query.Where(x => x.Id==actor.AgencyId) : query.Where(x => false);
    }

    public IQueryable<ClientAccount> SearchClients(BackOfficeDbContext db,string term)
    {
        var clients=Clients(db);
        if(term.Length==0)return clients;
        var relationships=Relationships(db);var agencies=Agencies(db);
        // Every predicate, including agency discovery, uses the same trusted scope.
        // EF translates Contains with literal LIKE escaping; user wildcards are not patterns.
        return clients.Where(x => x.NormalizedName.Contains(term) || x.Reference.Contains(term) ||
            (x.CompanyNumber!=null && x.CompanyNumber.Contains(term)) ||
            relationships.Any(r => r.ClientId==x.Id && agencies.Any(a => a.Id==r.AgencyId &&
                (a.LegalName.ToUpper().Contains(term) || a.Reference.Contains(term)))));
    }

    public IQueryable<ClientActivity> Activity(BackOfficeDbContext db)
    {
        var clients=Clients(db);var relationships=Relationships(db);
        var query=db.Set<ClientActivity>().AsNoTracking().Where(x => clients.Any(c => c.Id==x.ClientId));
        // Relationship-free internal events are not automatically shared externally.
        return InternalRead ? query : query.Where(x => x.RelationshipId!=null && relationships.Any(r => r.Id==x.RelationshipId));
    }
}
