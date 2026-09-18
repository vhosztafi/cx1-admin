using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class RenewalLifecycleSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Renewal invitation fixtures require held initialization.");
        foreach(var product in await db.Set<Product>().Where(x=>x.Code=="motor-trade-road-risks" || x.Code=="motor-trade-combined").ToArrayAsync(token))
            if(!await db.Set<TemplateVersion>().AnyAsync(x=>x.Code=="demo-renewal-invitation" && x.ProductId==product.Id,token))
                db.Add(new TemplateVersion{Code="demo-renewal-invitation",Kind="renewal-invitation",ProductId=product.Id,Version=1,
                    EffectiveFrom=new(2026,1,1,0,0,0,TimeSpan.Zero),EffectiveTo=new(2035,1,1,0,0,0,TimeSpan.Zero),
                    ContentJson=JsonSerializer.Serialize(new{format="renewal-template-1",title=product.Name+" renewal invitation and proposed schedule",
                        notice="Fictional demonstration. Review the new term, supplied experience, retained risk, conditions and full renewal price. Delivery invites acceptance; it does not issue cover."})});
        await db.SaveChangesAsync(token);
    }
}
