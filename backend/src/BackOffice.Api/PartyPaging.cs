using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using Microsoft.AspNetCore.DataProtection;

namespace BackOffice.Api;

public sealed class PartyPaging(IDataProtectionProvider protection, TimeProvider time)
{
    private readonly IDataProtector protector=protection.CreateProtector("CoverMGA.PartyCursor.v1");
    public Page? Read(HttpContext context,ActorContext actor,string ordering,params string[] filters)
        => ReadCore(context,actor,ordering,null,filters);
    public Page? ReadBound(HttpContext context,ActorContext actor,string ordering,string authorityVersion,params string[] filters)
        => ReadCore(context,actor,ordering,authorityVersion,filters);
    private Page? ReadCore(HttpContext context,ActorContext actor,string ordering,string? authorityVersion,string[] filters)
    {
        var query=context.Request.Query;
        if (query.Any(x => x.Value.Count!=1 || string.IsNullOrEmpty(x.Value[0]) || !(filters.Contains(x.Key) || x.Key is "cursor" or "pageSize"))) return null;
        var size=25;
        if (query.TryGetValue("pageSize",out var value) && (!int.TryParse(value.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out size) || size is <1 or >100)) return null;
        var scope=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {actor.UserId,actor.TeamId,actor.AgencyId,Roles=actor.Roles.Order(StringComparer.Ordinal).ToArray()})));
        if (authorityVersion is not null) scope=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {scope,authorityVersion})));
        var route=context.Request.Path.ToString();
        var filter=JsonSerializer.Serialize(filters.Order(StringComparer.Ordinal).Select(x => new[] {x,query[x].ToString()}));
        var now=time.GetUtcNow();
        if (!query.TryGetValue("cursor",out var cursor)) return new Page(scope,route,filter,ordering,size,0,now,now.AddMinutes(15));
        if (cursor.ToString().Length is <1 or >2048) return null;
        try
        {
            var page=JsonSerializer.Deserialize<Page>(protector.Unprotect(cursor.ToString()));
            return page is not null && page.Scope==scope && page.Route==route && page.Filter==filter && page.Ordering==ordering && page.Size==size &&
                page.Offset>=0 && page.Offset<=int.MaxValue-size && page.AsOf<=now && page.ExpiresAt>now ? page : null;
        }
        catch (CryptographicException) {return null;}
        catch (JsonException) {return null;}
    }
    public string? Next(Page page,bool more) => more && page.Offset<=int.MaxValue-page.Size
        ? protector.Protect(JsonSerializer.Serialize(page with {Offset=page.Offset+page.Size})) : null;
    public sealed record Page(string Scope,string Route,string Filter,string Ordering,int Size,int Offset,DateTimeOffset AsOf,DateTimeOffset ExpiresAt);
}
