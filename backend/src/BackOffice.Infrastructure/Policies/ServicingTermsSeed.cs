using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class ServicingTermsSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Terms seed requires held initialization.");
        foreach(var product in await db.Set<Product>().Where(x=>x.Code=="motor-trade-road-risks" || x.Code=="motor-trade-combined" || x.Code=="commercial-combined").ToArrayAsync(token))
            if(!await db.Set<TemplateVersion>().AnyAsync(x=>x.Code=="demo-servicing-terms" && x.ProductId==product.Id,token))
                db.Add(new TemplateVersion{Code="demo-servicing-terms",Kind="servicing-terms",ProductId=product.Id,Version=1,
                    EffectiveFrom=new(2026,1,1,0,0,0,TimeSpan.Zero),EffectiveTo=new(2035,1,1,0,0,0,TimeSpan.Zero),
                    ContentJson=JsonSerializer.Serialize(new{format="servicing-template-1",title=product.Name+" policy adjustment and statement of fact",
                        notice="Fictional demonstration. Review every effective date, retained risk, condition and adjustment price before accepting."})});
        if(!await db.Set<SettingVersion>().AnyAsync(x=>x.Scope=="servicing-delivery",token))
            db.Add(new SettingVersion{Scope="servicing-delivery",Version=1,EffectiveFrom=new(2026,1,1,0,0,0,TimeSpan.Zero),
                Values="{\"demo\":true,\"kind\":\"servicing-delivery\",\"schemaVersion\":\"1\",\"scenario\":\"success\"}"});
        await db.SaveChangesAsync(token);
    }

    internal static string? Scenario(SettingVersion setting)
    {
        try
        {
            using var parsed=JsonDocument.Parse(setting.Values);var root=parsed.RootElement;var fields=root.EnumerateObject().Select(x=>x.Name).ToArray();
            if(setting.Scope!="servicing-delivery" || fields.Length!=4 || fields.Distinct().Count()!=4 || root.GetProperty("demo").ValueKind!=JsonValueKind.True ||
                root.GetProperty("kind").GetString()!="servicing-delivery" || root.GetProperty("schemaVersion").GetString()!="1")return null;
            var value=root.GetProperty("scenario").GetString();return value is "success" or "reject" or "transient-once" or "timeout-after-success"?value:null;
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException){return null;}
    }
}
