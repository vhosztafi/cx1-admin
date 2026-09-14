using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Parties;

public sealed record SafeSupportInstruction(Guid Id,Guid PersonId,string Instruction,DateOnly ReviewOn);
public sealed class SupportFlagAccessException:Exception
{ public SupportFlagAccessException():base("Support record not found."){} }

public sealed class SupportFlagScope(ActorContext actor)
{
    public IQueryable<SupportFlag> InternalFlags(BackOfficeDbContext db)
    {
        var relationships=new PartyScope(actor).Relationships(db);
        return db.Set<SupportFlag>().AsNoTracking().Where(x=>actor.HasCapability("support-internal-read") &&
            relationships.Any(r=>r.Id==x.OriginRelationshipId && r.ClientId==x.ClientId));
    }
    public IQueryable<SupportFlagHistory> InternalHistory(BackOfficeDbContext db)
    {
        var flags=InternalFlags(db);
        return db.Set<SupportFlagHistory>().AsNoTracking().Where(x=>flags.Any(f=>f.Id==x.FlagId));
    }
    // Caller authorizes either explicit agency-safe read or an audited internal preview.
    // Return only the safe DTO; no flag category, origin, reason, history or hidden count.
    public IQueryable<SafeSupportInstruction> SafeInstructions(BackOfficeDbContext db,Guid relationshipId)
    {
        var scope=new PartyScope(actor);var relationships=scope.Relationships(db);var contacts=scope.Contacts(db);
        return from flag in db.Set<SupportFlag>().AsNoTracking()
            where flag.EndedAt==null && flag.AgencyInstruction!=null &&
                db.Set<FlagVisibility>().Any(g=>g.FlagId==flag.Id && g.ClientId==flag.ClientId && g.RelationshipId==relationshipId) &&
                relationships.Any(r=>r.Id==relationshipId && r.ClientId==flag.ClientId && r.State=="active") &&
                contacts.Any(c=>c.RelationshipId==relationshipId && c.ClientId==flag.ClientId && c.PersonId==flag.PersonId)
            select new SafeSupportInstruction(flag.Id,flag.PersonId,flag.AgencyInstruction!,flag.ReviewOn);
    }
    // The lifecycle service must hold all relevant parent locks while validating.
    public async Task<Guid> ValidateMembershipAsync(BackOfficeDbContext db,Guid originRelationshipId,Guid personId,
        IReadOnlyCollection<Guid> grantIds,CancellationToken token=default)
    {
        if(!actor.HasCapability("support-write") || personId==Guid.Empty || grantIds.Count>100 ||
            grantIds.Any(x=>x==Guid.Empty) || grantIds.Distinct().Count()!=grantIds.Count)throw new SupportFlagAccessException();
        var scope=new PartyScope(actor);var relationships=scope.Relationships(db);var contacts=scope.Contacts(db);
        var origin=await relationships.SingleOrDefaultAsync(x=>x.Id==originRelationshipId && x.State=="active",token)
            ?? throw new SupportFlagAccessException();
        var ids=grantIds.Append(originRelationshipId).Distinct().ToArray();
        var valid=await relationships.CountAsync(r=>ids.Contains(r.Id) && r.ClientId==origin.ClientId && r.State=="active" &&
            contacts.Any(c=>c.RelationshipId==r.Id && c.ClientId==origin.ClientId && c.PersonId==personId),token);
        if(valid!=ids.Length)throw new SupportFlagAccessException();
        return origin.ClientId;
    }
}
