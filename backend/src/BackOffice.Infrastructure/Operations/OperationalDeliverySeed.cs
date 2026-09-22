using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public static class OperationalDeliverySeed
{
    public static async Task Seed(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Delivery seed requires held initialization.");
        if(!await db.Set<SettingVersion>().AnyAsync(x=>x.Scope==MessageDeliveryService.WorkKind,token))
            db.Add(new SettingVersion{Scope=MessageDeliveryService.WorkKind,Version=1,EffectiveFrom=new(2026,1,1,0,0,0,TimeSpan.Zero),Values="{\"demo\":true,\"kind\":\"operational-delivery\",\"schemaVersion\":\"1\",\"scenario\":\"success\"}"});
        await db.SaveChangesAsync(token);
    }
    internal static string? Scenario(SettingVersion setting)
    {
        try
        {
            using var json=JsonDocument.Parse(setting.Values);var root=json.RootElement;var fields=root.EnumerateObject().Select(x=>x.Name).ToArray();
            if(setting.Scope!=MessageDeliveryService.WorkKind||fields.Length!=4||fields.Distinct().Count()!=4||root.GetProperty("demo").ValueKind!=JsonValueKind.True||
                root.GetProperty("kind").GetString()!=MessageDeliveryService.WorkKind||root.GetProperty("schemaVersion").GetString()!="1")return null;
            var scenario=root.GetProperty("scenario").GetString();return scenario is "success" or "reject" or "transient-once" or "timeout-after-success" or "retry-required"?scenario:null;
        }
        catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException){return null;}
    }
}
