using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public static class OperationalMidSeed
{
    public const string Scope="operational-mid";
    public static async Task Seed(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("MID seed requires held initialization.");
        if(!await db.Set<SettingVersion>().AnyAsync(x=>x.Scope==Scope,token))db.Add(new SettingVersion{Scope=Scope,Version=1,EffectiveFrom=new(2026,1,1,0,0,0,TimeSpan.Zero),Values="{\"demo\":true,\"kind\":\"operational-mid\",\"schemaVersion\":\"2\",\"scenario\":\"success\"}"});
        await db.SaveChangesAsync(token);
    }
    internal static string? Scenario(SettingVersion setting)
    {
        try{using var doc=JsonDocument.Parse(setting.Values);var root=doc.RootElement;var names=root.EnumerateObject().Select(x=>x.Name).ToArray();
            if(setting.Scope!=Scope||names.Length!=4||names.Distinct().Count()!=4||root.GetProperty("demo").ValueKind!=JsonValueKind.True||root.GetProperty("kind").GetString()!=Scope||root.GetProperty("schemaVersion").GetString()!="2")return null;
            var value=root.GetProperty("scenario").GetString();return value is "success" or "reject" or "transient-once" or "timeout-after-success" or "retry-required"?value:null;
        }catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException){return null;}
    }
}
