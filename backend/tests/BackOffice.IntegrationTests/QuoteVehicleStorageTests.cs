using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlVehicleRevisionsKeepCurrentRegistrationProjectionAndRejectOrphansAtomically()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync()) { await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var userId = await db.Set<StaffUser>().Where(row => row.Email == "underwriter@cover.example").Select(row => row.Id).SingleAsync();
            var actor = new ActorContext(userId,null,null,new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(),sql => sql.UseCompatibilityLevel(160)).Options;
            var service = new QuoteService(new QuoteFactory(options),new QuoteTime());
            var vehicleId = Guid.NewGuid();
            var proposal = JsonSerializer.SerializeToNode(new { schemaVersion = "1.0", productCode = "motor-trade-road-risks", risk = new { vehicles = new[] { new { id = vehicleId, registration = "AA11 AAA", register = "owned-not-for-sale" } } } })!;
            var create = await service.CreateAsync(actor,fixture.Relationship,fixture.ProductVersion,proposal.ToJsonString(),"vehicle-create",Guid.NewGuid());
            var first = await service.GetAsync(actor,create.ResourceId);
            foreach (var field in new[] { "ownerDriverId","specifiedVehicleIds" })
            {
                var invalid = proposal.DeepClone();
                if (field == "ownerDriverId") invalid["risk"]!["vehicles"]![0]![field] = Guid.NewGuid().ToString(); else invalid["risk"]![field] = new JsonArray(Guid.NewGuid().ToString());
                await Assert.ThrowsAsync<QuoteValidationException>(() => service.SaveAsync(actor,first.Quote.Id,first.Quote.RowVersion,invalid.ToJsonString(),null,"orphan-" + field,Guid.NewGuid()));
            }
            Assert.Equal(1,await db.Set<QuoteRevision>().CountAsync()); Assert.Equal(1,await db.Set<QuoteActivity>().CountAsync());
            Assert.Equal("AA11AAA",(await db.Set<QuoteRegistration>().SingleAsync()).NormalizedRegistration);
            proposal["risk"]!["vehicles"]![0]!["registration"] = "BB22 BBB";
            await service.SaveAsync(actor,first.Quote.Id,first.Quote.RowVersion,proposal.ToJsonString(),null,"vehicle-rename",Guid.NewGuid());
            var second = await service.GetAsync(actor,first.Quote.Id); Assert.Equal("BB22BBB",(await db.Set<QuoteRegistration>().AsNoTracking().SingleAsync()).NormalizedRegistration);
            Assert.Equal(vehicleId,(await db.Set<QuoteRegistration>().AsNoTracking().SingleAsync()).VehicleId);
            var duplicate = proposal["risk"]!["vehicles"]![0]!.DeepClone(); duplicate["id"] = Guid.NewGuid().ToString(); duplicate["registration"] = "BB22BBB";
            proposal["risk"]!["vehicles"]!.AsArray().Add(duplicate);
            await service.SaveAsync(actor,first.Quote.Id,second.Quote.RowVersion,proposal.ToJsonString(),null,"vehicle-duplicate-draft",Guid.NewGuid());
            var third = await service.GetAsync(actor,first.Quote.Id);
            using (var doc = JsonDocument.Parse(third.Revision.ProposalJson)) Assert.Contains(QuoteVehicleRules.Assess(doc.RootElement,new DateOnly(2026,9,15)),issue => issue.Code == "duplicate-registration" && issue.Path == "/risk/vehicles/1/registration");
            Assert.Equal(2,await db.Set<QuoteRegistration>().CountAsync());
            proposal["risk"]!["vehicles"] = new JsonArray();
            await service.SaveAsync(actor,first.Quote.Id,third.Quote.RowVersion,proposal.ToJsonString(),null,"vehicle-remove",Guid.NewGuid());
            Assert.Empty(await db.Set<QuoteRegistration>().AsNoTracking().ToListAsync());
            Assert.Equal(first.Revision.ProposalJson,(await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(row => row.Id == first.Revision.Id)).ProposalJson);
            Assert.Contains("BB22 BBB",(await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(row => row.Id == third.Revision.Id)).ProposalJson,StringComparison.Ordinal);
            Assert.Equal(4,await db.Set<QuoteRevision>().CountAsync());
        });
    }
}
