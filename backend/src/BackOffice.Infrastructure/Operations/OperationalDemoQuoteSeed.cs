using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace BackOffice.Infrastructure.Operations;

public sealed class OperationalDemoQuoteSeed(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public const string CommercialMarker="Fictional operational demo v1: commercial-incident";
    public async Task<CommercialDemoQuote> PrepareCommercialIncident(ActorContext actor,Guid relationshipId,Guid productVersionId,DateOnly startsOn,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await db.Database.OpenConnectionAsync(token);
        var resource=$"CoverMGA.OperationalCommercialDemo:{relationshipId:D}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000; IF @r<0 THROW 51972,'Operational demo preparation busy.',1;",token);
        try
        {
            Guid productId;
            await using(var transaction=await db.Database.BeginTransactionAsync(token))
            {
                var scope=await QuoteScope.ForRelationshipAsync(db,actor,relationshipId,QuoteAccess.Capture,token);
                var selected=await QuoteCaptureEligibility.ResolveAsync(db,scope,productVersionId,time.GetUtcNow(),token:token);
                if(selected.Product.Code!="commercial-combined")throw new QuoteOperationException(409,"commercial-demo-product-required");
                productId=selected.Product.Id;await transaction.CommitAsync(token);
            }
            // The immutable first revision is the durable scenario identity;
            // edited current proposal text and expired command receipts cannot
            // cause setup to manufacture another quote or overwrite that edit.
            var existing=await (from revision in db.Set<QuoteRevision>().FromSqlInterpolated($"SELECT * FROM QuoteRevision WHERE JSON_VALUE(ProposalJson,'$.risk.materialFacts')={CommercialMarker}").AsNoTracking()
                join quote in db.Set<Quote>() on revision.QuoteId equals quote.Id
                where revision.Number==1&&quote.RelationshipId==relationshipId&&quote.ProductId==productId select quote.Id).ToArrayAsync(token);
            if(existing.Length>1)throw new InvalidOperationException("Duplicate operational commercial scenario.");
            var service=new QuoteService(factory,time);Guid id;
            if(existing.Length==1)id=existing[0];
            else
            {
                using var stream=typeof(OperationalDemoQuoteSeed).Assembly.GetManifestResourceStream("CommercialDemo.Ready")!;
                var proposal=JsonNode.Parse(stream)!;
                proposal["termIntent"]!["localStartDate"]=startsOn.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
                proposal["insured"]!["tradingName"]="Fictional operational incident demonstration";
                proposal["risk"]!["materialFacts"]=CommercialMarker;
                id=(await service.CreateAsync(actor,relationshipId,productVersionId,proposal.ToJsonString(),
                    "operational-commercial-v1:"+relationshipId.ToString("D"),Guid.NewGuid(),token)).ResourceId;
            }
            var saved=await service.GetAsync(actor,id,token);return new("commercial-incident",id,saved.Quote.Reference);
        }
        finally {await db.Database.ExecuteSqlInterpolatedAsync($"EXEC sys.sp_releaseapplock @Resource={resource},@LockOwner='Session';",CancellationToken.None);}
    }
}
