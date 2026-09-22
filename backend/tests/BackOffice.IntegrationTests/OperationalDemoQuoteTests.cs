using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalDemoCommercialPreparationPreservesOriginalQuoteAndStaffEdits()=>WithDatabase(async(db,password)=>
    {
        await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true,includeCommercialCapture:true,includeCommercialUnderwriting:true);
        var f=await Fixture(db,3,"commercial-combined");var original=await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x=>x.QuoteId==f.Quote);
        var relationship=await db.Set<Quote>().Where(x=>x.Id==f.Quote).Select(x=>x.RelationshipId).SingleAsync();
        var factory=new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(),x=>x.UseCompatibilityLevel(160)).Options);
        var clock=new RatingClock();var seed=new OperationalDemoQuoteSeed(factory,clock);var start=new DateOnly(2026,9,21);
        var created=await seed.PrepareCommercialIncident(f.Actor,relationship,f.ProductVersion,start);
        Assert.NotEqual(f.Quote,created.QuoteId);var quotes=new QuoteService(factory,clock);var saved=await quotes.GetAsync(f.Actor,created.QuoteId);
        var proposal=JsonNode.Parse(saved.Revision.ProposalJson)!;Assert.Equal("2026-09-21",proposal["termIntent"]!["localStartDate"]!.GetValue<string>());
        proposal["insured"]!["tradingName"]="Staff edited fictional operations business";
        await quotes.SaveAsync(f.Actor,created.QuoteId,saved.Quote.RowVersion,proposal.ToJsonString(),"Keep staff edits",Guid.NewGuid().ToString(),Guid.NewGuid());
        var second=await seed.PrepareCommercialIncident(f.Actor,relationship,f.ProductVersion,start.AddDays(1));Assert.Equal(created.QuoteId,second.QuoteId);
        var retained=await quotes.GetAsync(f.Actor,created.QuoteId);Assert.Equal(2,retained.Revision.Number);Assert.Contains("Staff edited fictional operations business",retained.Revision.ProposalJson);
        Assert.Equal(original.ProposalJson,await db.Set<QuoteRevision>().Where(x=>x.Id==original.Id).Select(x=>x.ProposalJson).SingleAsync());
        Assert.Equal(2,await db.Set<Quote>().CountAsync());Assert.Empty(await db.Set<Policy>().ToListAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={f.Actor.UserId}");
        await Assert.ThrowsAsync<QuoteOperationException>(()=>seed.PrepareCommercialIncident(f.Actor,relationship,f.ProductVersion,start));
    });
}
