using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class CommercialExposureSeed
{
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Exposure initialization requires a held transaction.");
        var product = await db.Set<Product>().SingleAsync(x => x.Code == CommercialCaptureRules.ProductCode, token);
        var binders = await db.Set<BinderVersion>().Where(x => x.ProductId == product.Id && x.State == "published").OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id).ToArrayAsync(token);
        var actor = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example", token);
        foreach (var binder in binders)
        {
            var book = await db.Set<CommercialExposureBook>().SingleOrDefaultAsync(x => x.ProductId == product.Id && x.ProviderId == binder.ProviderId, token);
            if (book is null)
            {
                book = new() { ProductId = product.Id, ProviderId = binder.ProviderId, Code = "commercial-" + binder.ProviderId.ToString("N"), CreatedBy = actor.Id };
                db.Add(book); await db.SaveChangesAsync(token);
            }
            if (!await db.Set<CommercialExposureBinder>().AnyAsync(x => x.BinderVersionId == binder.Id, token))
                db.Add(new CommercialExposureBinder { BookId = book.Id, BinderVersionId = binder.Id, CreatedBy = actor.Id });
            if (!await db.Set<CommercialExposureLimitVersion>().AnyAsync(x => x.BookId == book.Id, token))
            {
                // Fictional publication date is stable, independent of the machine
                // clock, so the documented demo known-date remains reproducible.
                var row = new CommercialExposureLimitVersion { BookId = book.Id, Version = 1, Amount = 40_000_000m,
                    EffectiveFrom = binder.EffectiveFrom, EffectiveTo = binder.EffectiveTo,
                    PublishedAt = binder.EffectiveFrom, CreatedBy = actor.Id };
                SetPublication(row, "fictional-demo-default"); db.Add(row);
            }
            await db.SaveChangesAsync(token);
        }
    }

    public static void SetPublication(CommercialExposureLimitVersion row, string reason)
    {
        row.PublicationJson = JsonSerializer.Serialize(new { schemaVersion = "commercial-exposure-limit-1", bookId = row.BookId,
            district = row.District, version = row.Version, amount = row.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            effectiveFrom = row.EffectiveFrom, effectiveTo = row.EffectiveTo, publishedAt = row.PublishedAt,
            publishedBy = row.CreatedBy, supersedesLimitId = row.SupersedesLimitId, reason });
        row.ContentHash = SHA256.HashData(Encoding.UTF8.GetBytes(row.PublicationJson));
    }
}
