using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteDemoResult(Guid AgencyId, Guid ClientId, Guid RelationshipId, IReadOnlyList<Guid> QuoteIds, int Created);

// Explicit local fixture import, not an agency approval workflow. Only the
// dedicated fictional context is bootstrapped; quotes use normal command services.
public sealed class QuoteDemo(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public static readonly Guid AgencyId = Guid.Parse("51000000-0000-4000-8000-000000000001");
    public static readonly Guid ClientId = Guid.Parse("51000000-0000-4000-8000-000000000002");
    public static readonly Guid RelationshipId = Guid.Parse("51000000-0000-4000-8000-000000000003");

    public async Task<QuoteDemoResult> SeedAsync(CancellationToken token = default)
    {
        var actor = await Context(token);
        var offers = (await new QuoteProducts(factory, time).ListAsync(actor, RelationshipId, token)).Where(x => x.CaptureEligible).ToArray();
        if (offers.Length != 2 || offers.Select(x => x.ProductCode).Distinct().Count() != 2)
            throw new QuoteOperationException(409, "quote-demo-products-unavailable");
        var service = new QuoteService(factory, time); var ids = new List<Guid>(); var created = 0;
        foreach (var offer in offers)
        {
            var names = typeof(QuoteDemo).Assembly.GetManifestResourceNames().Where(x => x.StartsWith("QuoteDemo.quote-capture-" + offer.ProductCode, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            if (names.Length != 3) throw new InvalidOperationException("Expected three bundled quote examples per product.");
            foreach (var name in names.Prepend("incomplete"))
            {
                string? proposal = null;
                if (name != "incomplete")
                {
                    await using var stream = typeof(QuoteDemo).Assembly.GetManifestResourceStream(name)!;
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                    proposal = document.RootElement.GetProperty("proposal").GetRawText();
                }
                // Stable v1 keys resume partial runs and never replace edited revisions.
                var outcome = await service.CreateAsync(actor, RelationshipId, offer.ProductVersionId, proposal,
                    "quote-demo-v1:" + offer.ProductCode + ":" + name, Guid.NewGuid(), token);
                ids.Add(outcome.ResourceId); if (!outcome.Replayed) created++;
            }
        }
        return new(AgencyId, ClientId, RelationshipId, ids, created);
    }

    private async Task<ActorContext> Context(CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'CoverMGA.QuoteDemoContext',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @result<0 THROW 51080,'Quote demo context busy.',1;", token);
        var user = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Email == "underwriter@cover.example", token);
        var roles = await (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId == user.Id select role.Code).ToListAsync(token);
        var actor = new ActorContext(user.Id, user.TeamId, user.AgencyId, roles.ToHashSet(StringComparer.Ordinal));
        if (user.State != "active" || !actor.HasCapability("quote-capture")) throw new QuoteOperationException(403, "quote-access-denied");
        if (await db.Set<Agency>().AnyAsync(x => x.Id == AgencyId, token))
        {
            // Never repair or reactivate an edited/suspended fixture on rerun.
            if (!await db.Set<ClientAgencyRelationship>().AnyAsync(x => x.Id == RelationshipId && x.ClientId == ClientId && x.AgencyId == AgencyId, token))
                throw new InvalidOperationException("The quote demo relationship has changed.");
        }
        else
        {
            var settings = await QuoteCaptureEligibility.LoadSettingsAsync(db, time.GetUtcNow(), token);
            var selectedIds = settings.Products.Keys.ToArray();
            var versions = await (from version in db.Set<ProductVersion>() join product in db.Set<Product>() on version.ProductId equals product.Id
                where selectedIds.Contains(version.Id) select new { version.Id, product.Code }).ToListAsync(token);
            if (versions.Count != 2 || !versions.Select(x => x.Code).Order().SequenceEqual(new[] { "motor-trade-combined", "motor-trade-road-risks" }))
                throw new QuoteOperationException(409, "quote-demo-products-unavailable");
            var requester = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example", token);
            var reviewer = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-reviewer@cover.example", token);
            var now = time.GetUtcNow(); var effective = new DateOnly(2026, 9, 15);
            const string effectiveText = "2026-09-15";
            const string reason = "Imported fictional pre-approved quote demonstration; not an approval workflow execution.";
            var agency = new Agency { Id = AgencyId, Reference = "AG-DEMO-QUOTES", LegalName = "Fictional Quote Demonstration Agency",
                NormalizedName = "FICTIONAL QUOTE DEMONSTRATION AGENCY", State = "active", CreatedBy = requester.Id, CreatedAt = now };
            db.Add(agency);
            db.Add(new ClientAccount { Id = ClientId, Reference = "CL-DEMO-QUOTES", LegalName = "Alex Example (fictional)", NormalizedName = "ALEX EXAMPLE FICTIONAL",
                EntityType = "sole-trader", Address = "{\"line1\":\"1 Example Street\",\"town\":\"Example Town\",\"postcode\":\"AB1 2CD\",\"country\":\"GB\"}", CreatedBy = user.Id, CreatedAt = now });
            await db.SaveChangesAsync(token);
            db.Add(new ClientAgencyRelationship { Id = RelationshipId, AgencyId = AgencyId, ClientId = ClientId, CreatedBy = user.Id, CreatedAt = now });
            db.Add(new AgencyOnboarding { AgencyId = AgencyId, Details = JsonSerializer.Serialize(new { legalName = agency.LegalName }), CreatedBy = requester.Id, CreatedAt = now });
            var snapshot = JsonSerializer.Serialize(new { effectiveFrom = effectiveText, commercialTerms = new { commissionBasis = "per-product" }, creditLimit = "0.00",
                products = versions.OrderBy(x => x.Id).Select(x => new { productVersionId = x.Id, effectiveFrom = effectiveText, brokerCommissionBasisPoints = 1250 }) });
            var request = new AgencyStateRequest { AgencyId = AgencyId, BaseVersion = agency.RowVersion, ProposedInputFingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))),
                RequestedBy = requester.Id, CreatedBy = requester.Id, CreatedAt = now, RequestReason = reason };
            db.Add(request); await db.SaveChangesAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={reviewer.Id},DecisionReason={reason},DecidedAt={now} WHERE Id={request.Id}", token);
            db.Add(new AgencyTermsVersion { AgencyId = AgencyId, Version = 1, EffectiveFrom = effective, ApprovedStateRequestId = request.Id, Snapshot = snapshot, CreatedBy = reviewer.Id, CreatedAt = now });
            db.Add(new AuditEvent { ActorId = user.Id, EventType = "quote.demo-context-imported", OccurredAt = now, CreatedAt = now, Reason = reason,
                CorrelationId = Guid.NewGuid(), After = JsonSerializer.Serialize(new { agencyId = AgencyId, clientId = ClientId, relationshipId = RelationshipId }) });
            await db.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
        return actor;
    }
}
