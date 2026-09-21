using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

// Read-only discovery follows QuoteDiscovery's serializable query boundary.
// Commands use OperationalScope's held parent lock order instead.
public static class TaskDiscovery
{
    public static async Task<ActorContext> Authorize(BackOfficeDbContext db, ActorContext actor, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Task discovery requires a held transaction.");
        if (!actor.HasCapability("task-read")) throw new OperationalAccessException(403, "task-access-denied");
        var identity = await IdentitySnapshot.Lock(db, new(actor.UserId, null), token);
        if (identity is null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code))) throw new OperationalAccessException(403, "task-access-denied");
        var current = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
        if (!current.HasCapability("task-read")) throw new OperationalAccessException(403, "task-access-denied");
        return current;
    }

    public static IQueryable<OperationalTask> Rows(BackOfficeDbContext db, ActorContext actor)
    {
        var agencyRead = actor.HasCapability("agency-read"); var relationshipRead = actor.HasCapability("relationship-read");
        var quoteRead = actor.HasCapability("quote-read"); var policyRead = actor.HasCapability("policy-read");
        var relationships = db.Set<ClientAgencyRelationship>().Where(x => x.State == "active");
        var quotes = db.Set<Quote>().Where(q => q.CurrentRevisionId != null && relationships.Any(r => r.Id == q.RelationshipId && r.ClientId == q.ClientId && r.AgencyId == q.AgencyId));
        var policies = db.Set<Policy>().Where(p => quotes.Any(q => q.Id == p.SourceQuoteId && q.BoundPolicyId == p.Id && q.ClientId == p.ClientId && q.RelationshipId == p.RelationshipId && q.AgencyId == p.AgencyId));
        var subjects = db.Set<OperationalSubject>().Where(s =>
            agencyRead && s.Kind == "agency" && db.Set<Agency>().Any(a => a.Id == s.AgencyId) ||
            relationshipRead && s.Kind == "relationship" && relationships.Any(r => r.Id == s.RelationshipId) ||
            quoteRead && s.Kind == "quote" && quotes.Any(q => q.Id == s.QuoteId) ||
            policyRead && s.Kind == "policy" && policies.Any(p => p.Id == s.PolicyId) ||
            policyRead && s.Kind == "servicing-draft" && db.Set<ServicingDraft>().Any(d => d.Id == s.ServicingDraftId && policies.Any(p => p.Id == d.PolicyId)));
        return db.Set<OperationalTask>().AsNoTracking().Where(t => actor.HasCapability("task-read") && subjects.Any(s => s.Id == t.SubjectId));
    }

    // Scoped rows only: hidden records cannot affect the count or cursor version.
    public static async Task<string> Version(BackOfficeDbContext db, IQueryable<OperationalTask> rows, CancellationToken token)
    {
        var versions = await rows.OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToArrayAsync(token);
        // A user's team change also changes team-view membership without updating
        // the assigned task head. Include only owners of currently visible tasks.
        var owners = await db.Set<StaffUser>().Where(u => rows.Any(t => t.OwnerId == u.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion }).ToArrayAsync(token);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { versions, owners })));
    }
}
