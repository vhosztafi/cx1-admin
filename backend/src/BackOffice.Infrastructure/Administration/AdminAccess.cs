using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Infrastructure.Administration;

public static class AdminAccess
{
    public static async Task Authorize(BackOfficeDbContext db, ActorContext actor, CancellationToken token)
    {
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actor.UserId, null), token);
        if (identity is null || !identity.Roles.Any(x => x.Code == "system-admin"))
            throw new QuoteOperationException(403, "administration-access-denied");
    }
    public static string Etag(byte[] version) => "\"" + Convert.ToBase64String(version) + "\"";
    public static void Version(MutableRecord row, string etag)
    {
        if (string.IsNullOrEmpty(etag)) throw new QuoteOperationException(428, "administration-version-required");
        if (Etag(row.RowVersion) != etag) throw new QuoteOperationException(412, "administration-version-changed");
    }
    public static string Text(string? value, int max = 200)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim()
            : throw new QuoteOperationException(400, "administration-input-invalid");
    public static void Audit(BackOfficeDbContext db, ActorContext actor, Guid id, string kind,
        string reason, object? before, object after, DateTimeOffset now)
        => db.Add(new AuditEvent { ActorId = actor.UserId, CreatedBy = actor.UserId, SubjectRecordId = id,
            EventType = kind, Reason = Text(reason, 1000), OccurredAt = now, CorrelationId = Guid.NewGuid(),
            Before = before is null ? null : JsonSerializer.Serialize(before), After = JsonSerializer.Serialize(after) });
}
