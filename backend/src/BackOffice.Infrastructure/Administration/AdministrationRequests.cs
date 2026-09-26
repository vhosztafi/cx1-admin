using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Administration;

public sealed record AdministrationRequest(Guid Id, string Kind, string State, Guid RequestedBy,
    DateTimeOffset RequestedAt, JsonElement Proposal, Guid? DecidedBy, string? DecisionReason, string Etag);

// Append-only request and decision snapshots share the existing immutable
// setting storage. These scopes are never consumed as active configuration.
internal static class AdministrationRequests
{
    private const string Prefix = "admin-request/";
    internal static string Etag(SettingVersion row) => $"\"{row.Id:N}-{row.Version}\"";
    internal static AdministrationRequest View(SettingVersion row)
        => JsonSerializer.Deserialize<AdministrationRequest>(row.Values, ProductAdministration.Json)! with { Etag = Etag(row) };
    internal static SettingVersion Create(BackOfficeDbContext db, ActorContext actor, string kind, object input, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var data = new AdministrationRequest(id, kind, "pending", actor.UserId, now,
            JsonSerializer.SerializeToElement(input, ProductAdministration.Json), null, null, "");
        var row = new SettingVersion { Id = id, Scope = Prefix + id, Version = 1, EffectiveFrom = now,
            CreatedBy = actor.UserId, CreatedAt = now, Values = JsonSerializer.Serialize(data, ProductAdministration.Json) };
        db.Add(row); return row;
    }
    internal static async Task<SettingVersion> Hold(BackOfficeDbContext db, Guid id, string etag, string kind, CancellationToken ct)
    {
        var scope = Prefix + id;
        var row = await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(UPDLOCK,HOLDLOCK) WHERE Scope={scope}")
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct) ?? throw new QuoteOperationException(404, "administration-request-not-found");
        if (string.IsNullOrEmpty(etag)) throw new QuoteOperationException(428, "administration-version-required");
        if (Etag(row) != etag) throw new QuoteOperationException(412, "administration-version-changed");
        var value = View(row);
        if (value.Kind != kind || value.State != "pending") throw new QuoteOperationException(409, "administration-request-closed");
        return row;
    }
    internal static SettingVersion Decide(BackOfficeDbContext db, SettingVersion row, ActorContext actor, string state, string reason, DateTimeOffset now)
    {
        var data = View(row) with { State = state, DecidedBy = actor.UserId, DecisionReason = AdminAccess.Text(reason, 1000), Etag = "" };
        var next = new SettingVersion { Scope = row.Scope, Version = row.Version + 1, EffectiveFrom = now,
            CreatedBy = actor.UserId, CreatedAt = now, Values = JsonSerializer.Serialize(data, ProductAdministration.Json) };
        db.Add(next); return next;
    }
    internal static async Task<AdministrationRequest[]> List(BackOfficeDbContext db, string kind, CancellationToken ct)
    {
        var rows = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope.StartsWith(Prefix)).ToArrayAsync(ct);
        return rows.GroupBy(x => x.Scope).Select(x => View(x.MaxBy(y => y.Version)!)).Where(x => x.Kind == kind)
            .OrderByDescending(x => x.RequestedAt).ToArray();
    }
}
