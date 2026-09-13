using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace BackOffice.Api;

public sealed class OperationalPaging(IDataProtectionProvider protection, TimeProvider time)
{
    private readonly IDataProtector protector = protection.CreateProtector("CoverMGA.OperationalCursor.v1");

    public Page? Read(HttpContext context, params string[] filters)
    {
        var query = context.Request.Query;
        if (query.Any(x => x.Value.Count != 1 || string.IsNullOrEmpty(x.Value[0]) || !(filters.Contains(x.Key) || x.Key is "cursor" or "pageSize"))) return null;
        var size = 25;
        if (query.TryGetValue("pageSize", out var suppliedSize) && (!int.TryParse(suppliedSize.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 100)) return null;
        var actor = LocalIdentityService.Actor(context.User).UserId;
        var route = context.Request.Path.ToString();
        var filter = JsonSerializer.Serialize(filters.Order().Select(x => new[] {x, query[x].ToString()}));
        var now = time.GetUtcNow();
        if (!query.TryGetValue("cursor", out var cursor)) return new Page(actor, route, filter, size, 0, now, now.AddMinutes(15));
        if (cursor.ToString().Length is < 1 or > 2048) return null;
        try
        {
            var page = JsonSerializer.Deserialize<Page>(protector.Unprotect(cursor.ToString()));
            return page is not null && page.Actor == actor && page.Route == route && page.Filter == filter && page.Size == size &&
                page.Offset >= 0 && page.Offset <= int.MaxValue - page.Size && page.AsOf <= now && page.ExpiresAt > now ? page : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public string? Next(Page page, bool hasMore) => hasMore && page.Offset <= int.MaxValue - page.Size
        ? protector.Protect(JsonSerializer.Serialize(page with {Offset = page.Offset + page.Size})) : null;

    public sealed record Page(Guid Actor, string Route, string Filter, int Size, int Offset, DateTimeOffset AsOf, DateTimeOffset ExpiresAt);
}
