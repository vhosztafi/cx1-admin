using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Policies;
public static class PolicyClientActivitySeed
{
    // Append the missing client projection of real issue transactions from the
    // preceding slice. No original quote, policy or activity row is rewritten.
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Activity reconciliation requires held initialization.");
        var issued = await (from p in db.Set<Policy>() join t in db.Set<PolicyTransaction>() on p.Id equals t.PolicyId
            where t.Kind == "new-business" && t.Sequence == 1 && !db.Set<ClientActivity>().Any(a => a.RecordId == p.SourceQuoteId && a.RecordKind == "quote" && a.EventType == "policy.issued")
            select new { p.ClientId, p.RelationshipId, p.SourceQuoteId, t.ProcessedAt, t.CreatedBy }).ToArrayAsync(token);
        foreach (var row in issued) db.Add(new ClientActivity { ClientId = row.ClientId, RelationshipId = row.RelationshipId, RecordKind = "quote", RecordId = row.SourceQuoteId,
            EventType = "policy.issued", ActorId = row.CreatedBy, CreatedBy = row.CreatedBy, CreatedAt = row.ProcessedAt, OccurredAt = row.ProcessedAt });
        await db.SaveChangesAsync(token);
    }
}
