using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencySharedIdentity(Guid Id, string Reference, string LegalName, string State);
public sealed record AgencySharedProduct(string ProductCode, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool Available);
public sealed record AgencySharedPermission(string Permission, bool Granted, bool Available);
public sealed record AgencyUnavailableSection(string Kind, string State, int OwningPhase, string Message);
public sealed record AgencySharingContext(AgencySharedIdentity Agency, IReadOnlyList<AgencySharedProduct> Products, IReadOnlyList<AgencySharedPermission> Permissions, IReadOnlyList<AgencyUnavailableSection> UnavailableSections);

public static partial class AgencySharingService
{
    public static Task<AgencySharingContext> Context(BackOfficeDbContext db, ActorContext actor, Guid agencyId, TimeProvider? time = null, CancellationToken token = default)
        => ContextRead(db, actor, agencyId, false, time ?? TimeProvider.System, token);
    public static Task<AgencySharingContext> PreviewContext(BackOfficeDbContext db, ActorContext actor, Guid agencyId, TimeProvider? time = null, CancellationToken token = default)
        => ContextRead(db, actor, agencyId, true, time ?? TimeProvider.System, token);

    private static async Task<AgencySharingContext> ContextRead(BackOfficeDbContext db, ActorContext actor, Guid agencyId, bool preview, TimeProvider time, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (preview) await AuthorizePreview(db, actor, agencyId, token);
        else await AgencyScope.Resolve(db, actor, agencyId, "agency-context-read", token);
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var agency = await db.Set<Agency>().AsNoTracking().SingleAsync(x => x.Id == agencyId, token);
        var terms = db.Set<AgencyTermsVersion>().AsNoTracking().Where(x => x.AgencyId == agencyId);
        var current = await terms.Where(x => x.EffectiveFrom <= today).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstOrDefaultAsync(token);
        var products = new List<AgencySharedProduct>();
        if (current != null)
        {
            var end = await terms.Where(x => x.EffectiveFrom > current.EffectiveFrom).MinAsync(x => (DateOnly?)x.EffectiveFrom, token);
            products = await (from grant in db.Set<AgencyProduct>().AsNoTracking()
                join version in db.Set<ProductVersion>().AsNoTracking() on grant.ProductVersionId equals version.Id
                join product in db.Set<Product>().AsNoTracking() on version.ProductId equals product.Id
                where grant.AgencyTermsVersionId == current.Id
                orderby product.Code
                select new AgencySharedProduct(product.Code, product.Name, grant.EffectiveFrom, end, false)).ToListAsync(token);
        }
        var granted = await db.Set<AgencyPermissionGrant>().AsNoTracking().AnyAsync(x => x.AgencyId == agencyId && x.Permission == AgencyPermissionRules.BordereauDownload && x.GrantedAt <= now && x.RevokedAt == null, token);
        // Approved product/permission provenance is separate from unfinished
        // workflows. These summaries must never fabricate policy or ledger data.
        var result = new AgencySharingContext(new(agency.Id, agency.Reference, agency.LegalName, agency.State), products,
            [new(AgencyPermissionRules.BordereauDownload, granted, false)],
            [new("quotes", "unavailable", 5, "Quote workflows are delivered in phase 5."),
             new("policies", "unavailable", 6, "Policy workflows are delivered in phase 6."),
             new("tasks", "unavailable", 9, "Shared task workflows are delivered in phase 9."),
             new("statements", "unavailable", 10, "Statements and bordereau downloads are delivered in phase 10.")]);
        if (preview)
        {
            db.Add(new AuditEvent { ActorId = actor.UserId, CreatedBy = actor.UserId, OccurredAt = now, EventType = "agency.sharing-preview", CorrelationId = Guid.NewGuid(), After = JsonSerializer.Serialize(new { agencyId, section = "context" }) });
            await db.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token); return result;
    }
}
