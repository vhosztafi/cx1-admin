using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record CommercialDemoQuote(string Scenario, Guid QuoteId, string Reference);

// Explicit preparation in an already approved fictional relationship. No approval,
// authority grant, issue decision or worker result is manufactured by this helper.
public sealed class CommercialDemoSeed(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<IReadOnlyList<CommercialDemoQuote>> SeedAsync(ActorContext actor, Guid relationshipId, Guid productVersionId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await db.Database.OpenConnectionAsync(token);
        var resource = $"CoverMGA.CommercialDemo:{relationshipId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; IF @result<0 THROW 51972,'Commercial demo preparation busy.',1;", token);
        try
        {
            Guid productId;
            await using (var transaction = await db.Database.BeginTransactionAsync(token))
            {
                // Capture eligibility is checked before even discovering retained scenarios.
                var scope = await QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Capture, token);
                var selected = await QuoteCaptureEligibility.ResolveAsync(db, scope, productVersionId, time.GetUtcNow(), token: token);
                if (selected.Product.Code != "commercial-combined") throw new QuoteOperationException(409, "commercial-demo-product-required");
                productId = selected.Product.Id; await transaction.CommitAsync(token);
            }
            var service = new QuoteService(factory, time); var result = new List<CommercialDemoQuote>();
            foreach (var scenario in new[] { "two-location", "flood-referral", "outside-appetite", "conditional-capacity", "capacity-contender" })
            {
                var marker = $"Fictional commercial demo v1: {scenario}";
                var retained = await (from revision in db.Set<QuoteRevision>().FromSqlInterpolated($"SELECT * FROM QuoteRevision WHERE JSON_VALUE(ProposalJson,'$.risk.materialFacts')={marker}").AsNoTracking()
                    join quote in db.Set<Quote>() on revision.QuoteId equals quote.Id
                    where revision.Number == 1 && quote.RelationshipId == relationshipId && quote.ProductId == productId select quote.Id).ToArrayAsync(token);
                if (retained.Length > 1) throw new InvalidOperationException("Duplicate commercial demo scenario.");
                Guid id;
                if (retained.Length == 1) id = retained[0];
                else
                {
                    using var stream = typeof(CommercialDemoSeed).Assembly.GetManifestResourceStream("CommercialDemo.Ready")!;
                    var proposal = JsonNode.Parse(stream)!;
                    proposal["termIntent"]!["localStartDate"] = "2026-11-01";
                    proposal["insured"]!["tradingName"] = "Fictional commercial demo — " + scenario;
                    proposal["risk"]!["materialFacts"] = marker;
                    if (scenario == "flood-referral") proposal["risk"]!["locations"]![0]!["floodZone"] = "3";
                    if (scenario == "conditional-capacity") proposal["risk"]!["locations"]![0]!["buildings"] = "2500000.01";
                    // Within the district's own 40m bound, but the issued base
                    // policy consumes enough shared headroom to exceed the book.
                    if (scenario == "capacity-contender") proposal["risk"]!["locations"]![0]!["buildings"] = "39900000.01";
                    if (scenario == "outside-appetite")
                        proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote.ed8870d17328")!["value"] = true;
                    var created = await service.CreateAsync(actor, relationshipId, productVersionId, proposal.ToJsonString(),
                        $"commercial-demo-v1:{relationshipId:D}:{scenario}", Guid.NewGuid(), token);
                    id = created.ResourceId;
                }
                var saved = await service.GetAsync(actor, id, token);
                result.Add(new(scenario, id, saved.Quote.Reference));
            }
            return result;
        }
        finally { await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource},@LockOwner='Session';", CancellationToken.None); }
    }
}
