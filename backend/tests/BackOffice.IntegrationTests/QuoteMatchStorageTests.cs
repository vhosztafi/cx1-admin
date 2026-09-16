using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteMatchOwnershipPreservesHistoricalIdentityAndScopesIntake()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            var foreign = await CreateFixture(db, "-MATCH-FOREIGN");
            var quote = await InsertQuote(db, fixture);
            var revision = Revision(quote, fixture);
            db.Add(revision); await db.SaveChangesAsync();
            quote.CurrentRevisionId = revision.Id; await db.SaveChangesAsync();
            var originalHash = revision.ContentHash.ToArray();

            // Exercise an upgrade with actual retained proposals, not an empty schema.
            await db.GetService<IMigrator>().MigrateAsync("20260916122335_QuoteEvidenceStorage");
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            var retained = await db.Set<QuoteRevision>().SingleAsync(x => x.Id == revision.Id);
            Assert.Equal(fixture.Client, retained.ClientId);
            Assert.Equal(fixture.Relationship, retained.RelationshipId);
            Assert.Equal(originalHash, retained.ContentHash);
            Assert.Equal(revision.ProposalJson, retained.ProposalJson);

            var replacement = new ClientAccount { Reference = "CL-REASSOCIATION", LegalName = "Fictional replacement", NormalizedName = "FICTIONAL REPLACEMENT" };
            db.Add(replacement); await db.SaveChangesAsync();
            var relationship = new ClientAgencyRelationship { ClientId = replacement.Id, AgencyId = fixture.Agency };
            db.Add(relationship); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET ClientId={replacement.Id},RelationshipId={relationship.Id} WHERE Id={quote.Id}");
            db.ChangeTracker.Clear();
            var current = await db.Set<Quote>().SingleAsync(x => x.Id == quote.Id);
            var next = Revision(current, fixture, 2); db.Add(next); await db.SaveChangesAsync();
            Assert.Equal(replacement.Id, next.ClientId);
            Assert.Equal(fixture.Client, (await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == revision.Id)).ClientId);
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteRevision SET ClientId={replacement.Id},RelationshipId={relationship.Id} WHERE Id={revision.Id}"));

            var wrong = Revision(current, fixture, 3); wrong.ClientId = fixture.Client; wrong.RelationshipId = fixture.Relationship;
            db.Add(wrong); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.Entry(wrong).State = EntityState.Detached;
            var intake = new MatchSubmission { Reference = "MI-QUOTE-OWNED", AgencyId = fixture.Agency, QuoteId = quote.Id, LinkedClientId = replacement.Id, LinkedRelationshipId = relationship.Id };
            db.Add(intake); await db.SaveChangesAsync();
            var duplicate = new MatchSubmission { Reference = "MI-QUOTE-DUPLICATE", AgencyId = fixture.Agency, QuoteId = quote.Id };
            db.Add(duplicate); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.Entry(duplicate).State = EntityState.Detached;
            var foreignIntake = new MatchSubmission { Reference = "MI-QUOTE-FOREIGN", AgencyId = foreign.Agency, QuoteId = quote.Id };
            db.Add(foreignIntake); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.Entry(foreignIntake).State = EntityState.Detached;
            Assert.Equal(2, await db.Set<QuoteRevision>().CountAsync());
            Assert.Single(await db.Set<MatchSubmission>().Where(x => x.QuoteId == quote.Id).ToListAsync());
        });
    }
}
