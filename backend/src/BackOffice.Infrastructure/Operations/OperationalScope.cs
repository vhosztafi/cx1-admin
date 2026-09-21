using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed class OperationalAccessException(int status, string code) : Exception("The operational record is unavailable.")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed record OperationalParent(string Kind, Guid ParentId);
public sealed record HeldOperationalScope(ActorContext Actor, string ActorLabel, IReadOnlyList<OperationalSubject> Subjects);

// The transaction must remain held through materialization/receipt/commit.
// Registry IDs locate typed parents; they never grant access on their own.
public static class OperationalScope
{
    public static async Task<HeldOperationalScope> HoldSubjects(BackOfficeDbContext db, ActorContext actor,
        IReadOnlyList<Guid> ids, string capability, CancellationToken token = default)
    {
        Demand(db, actor, capability);
        if (ids.Count is < 1 or > 100 || ids.Any(x => x == Guid.Empty) || ids.Distinct().Count() != ids.Count) throw Missing();
        var subjects = await db.Set<OperationalSubject>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(token);
        if (subjects.Count != ids.Count) throw Missing();
        var parents = subjects.Select(Parent).ToArray();
        var held = await HoldParents(db, actor, parents, capability, token);
        return new(held.Actor, held.ActorLabel, subjects);
    }

    public static async Task<HeldOperationalScope> HoldParents(BackOfficeDbContext db, ActorContext actor,
        IReadOnlyList<OperationalParent> parents, string capability, CancellationToken token = default)
    {
        Demand(db, actor, capability);
        if (parents.Count is < 1 or > 100 || parents.Any(x => x.ParentId == Guid.Empty)) throw Missing();
        var hints = new List<(OperationalParent Parent, Guid AgencyId)>();
        foreach (var parent in parents.Distinct().OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.ParentId))
            hints.Add((parent, await AgencyHint(db, parent, token)));
        // All agencies precede any identity lock, including multi-agency batches.
        foreach (var id in hints.Select(x => x.AgencyId).Distinct().Order())
            if (!await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(HOLDLOCK,ROWLOCK) WHERE Id={id}").AsNoTracking().AnyAsync(token)) throw Missing();
        var identity = await IdentitySnapshot.Lock(db, new(actor.UserId, null), token);
        if (identity is null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code))) throw Denied();
        var current = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
        if (!current.HasCapability(capability)) throw Denied();
        foreach (var (parent, agencyId) in hints)
        {
            if (await AgencyHint(db, parent, token) != agencyId) throw Missing();
            switch (parent.Kind)
            {
                case "agency":
                    if (!current.HasCapability("agency-read")) throw Missing();
                    break;
                case "relationship":
                    if (!current.HasCapability("relationship-read")) throw Missing();
                    var relationHint = await db.Set<ClientAgencyRelationship>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == parent.ParentId, token) ?? throw Missing();
                    if (!await db.Set<ClientAccount>().FromSqlInterpolated($"SELECT * FROM ClientAccount WITH(HOLDLOCK,ROWLOCK) WHERE Id={relationHint.ClientId}").AsNoTracking().AnyAsync(token)) throw Missing();
                    var relation = await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK,ROWLOCK) WHERE Id={parent.ParentId}").AsNoTracking().SingleOrDefaultAsync(token);
                    if (relation is null || relation.AgencyId != agencyId || relation.ClientId != relationHint.ClientId || relation.State != "active") throw Missing();
                    break;
                case "quote":
                    var quote = await QuoteScope.ForQuoteAsync(db, current, parent.ParentId, QuoteAccess.Read, token);
                    if (quote.Quote.AgencyId != agencyId || quote.Scope.Relationship.State != "active") throw Missing();
                    break;
                case "policy":
                    var policy = await PolicyScope.Hold(db, current, parent.ParentId, token);
                    if (policy.AgencyId != agencyId || !await db.Set<ClientAgencyRelationship>().AnyAsync(x => x.Id == policy.RelationshipId && x.State == "active", token)) throw Missing();
                    break;
                case "servicing-draft":
                    var draftHint = await db.Set<ServicingDraft>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == parent.ParentId, token) ?? throw Missing();
                    var owned = await PolicyScope.Hold(db, current, draftHint.PolicyId, token);
                    var draft = await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(HOLDLOCK,ROWLOCK) WHERE Id={parent.ParentId}").AsNoTracking().SingleOrDefaultAsync(token);
                    if (draft is null || draft.PolicyId != owned.Id || owned.AgencyId != agencyId || !await db.Set<ClientAgencyRelationship>().AnyAsync(x => x.Id == owned.RelationshipId && x.State == "active", token)) throw Missing();
                    break;
                default: throw Missing();
            }
        }
        return new(current, identity.User.DisplayName, []);
    }

    public static OperationalParent Parent(OperationalSubject subject) => subject.Kind switch
    {
        "agency" when subject.AgencyId is Guid id => new(subject.Kind, id),
        "relationship" when subject.RelationshipId is Guid id => new(subject.Kind, id),
        "quote" when subject.QuoteId is Guid id => new(subject.Kind, id),
        "policy" when subject.PolicyId is Guid id => new(subject.Kind, id),
        "servicing-draft" when subject.ServicingDraftId is Guid id => new(subject.Kind, id),
        _ => throw Missing()
    };

    private static async Task<Guid> AgencyHint(BackOfficeDbContext db, OperationalParent parent, CancellationToken token)
    {
        Guid? agency = parent.Kind switch
        {
            "agency" => await db.Set<Agency>().Where(x => x.Id == parent.ParentId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(token),
            "relationship" => await db.Set<ClientAgencyRelationship>().Where(x => x.Id == parent.ParentId).Select(x => (Guid?)x.AgencyId).SingleOrDefaultAsync(token),
            "quote" => await db.Set<Quote>().Where(x => x.Id == parent.ParentId).Select(x => (Guid?)x.AgencyId).SingleOrDefaultAsync(token),
            "policy" => await db.Set<Policy>().Where(x => x.Id == parent.ParentId).Select(x => (Guid?)x.AgencyId).SingleOrDefaultAsync(token),
            "servicing-draft" => await (from draft in db.Set<ServicingDraft>() join policy in db.Set<Policy>() on draft.PolicyId equals policy.Id where draft.Id == parent.ParentId select (Guid?)policy.AgencyId).SingleOrDefaultAsync(token),
            _ => null
        };
        return agency ?? throw Missing();
    }

    private static void Demand(BackOfficeDbContext db, ActorContext actor, string capability)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Operational scope requires a held transaction.");
        if (capability is not ("subject-read" or "task-read" or "task-write" or "task-assign") || !actor.HasCapability(capability)) throw Denied();
    }
    private static OperationalAccessException Missing() => new(404, "operational-subject-not-found");
    private static OperationalAccessException Denied() => new(403, "operational-access-denied");
}
