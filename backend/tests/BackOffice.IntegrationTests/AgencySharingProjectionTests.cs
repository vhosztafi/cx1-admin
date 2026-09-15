using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencySharingProjectionTests
{
    [Fact]
    public async Task RealSqlSharingScopesSearchCountsPagesAndAuditedPreviewToCurrentRelationshipsAndGrants()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            var first = PartyDemoSeed.FirstAgencyId; var second = PartyDemoSeed.SecondAgencyId;
            Guid userId, staffId; string stamp;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, "Demo!" + Guid.NewGuid().ToString("N") + "a1", includeSupportFlags: true);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={first} OR Id={second}");
                var user = new StaffUser { State = "invited", AgencyId = second, Email = "sharing@example.test", NormalizedEmail = "SHARING@EXAMPLE.TEST", DisplayName = "Sharing broker" };
                db.Add(user); await db.SaveChangesAsync(); userId = user.Id;
                db.Add(new UserRole { UserId = userId, RoleId = await db.Set<Role>().Where(x => x.Code == "broker-admin").Select(x => x.Id).SingleAsync() });
                await db.SaveChangesAsync(); user.State = "active"; await db.SaveChangesAsync();
                var staff = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example"); staffId = staff.Id; stamp = staff.SecurityStamp;
                // A shared person must not leak the master identity into another relationship.
                var person = await db.Set<Person>().SingleAsync(x => x.Id == ContactDemoSeed.PersonId(1)); person.FullName = "SECRET MASTER IDENTITY"; await db.SaveChangesAsync();
            }
            var broker = new ActorContext(userId, null, second, new HashSet<string> { "broker-admin" });
            var staffActor = new ActorContext(staffId, null, null, new HashSet<string> { "agency-admin" });
            var own = PartyDemoSeed.RelationshipId(3, 2); var other = PartyDemoSeed.RelationshipId(3, 1);
            await using (var db = new BackOfficeDbContext(options))
            {
                var clients = await AgencySharingService.Clients(db, broker, second, new(Size: 100));
                Assert.Equal(17, clients.Total); Assert.DoesNotContain(clients.Items, x => x.Id == PartyDemoSeed.ClientId(4));
                Assert.Equal(0, (await AgencySharingService.Clients(db, broker, second, new(Search: "Traders 04"))).Total);
                Assert.Equal(0, (await AgencySharingService.Clients(db, broker, second, new(RelationshipId: other))).Total);
                var page = await AgencySharingService.Clients(db, broker, second, new(Offset: 1, Size: 1)); Assert.Equal(17, page.Total); Assert.Equal(clients.Items[1], Assert.Single(page.Items));
                var contacts = await AgencySharingService.Contacts(db, broker, second, new());
                Assert.Equal("Fictional A. Morgan", Assert.Single(contacts.Items).FullName);
                Assert.Equal(0, (await AgencySharingService.Contacts(db, broker, second, new(Search: "SECRET"))).Total);
                Assert.Equal(0, (await AgencySharingService.Contacts(db, broker, second, new(Search: "accounts@"))).Total);
                var instructions = await AgencySharingService.Instructions(db, broker, second, new());
                Assert.Equal("Provide written summaries and allow additional reading time.", Assert.Single(instructions.Items).Instruction);
                Assert.Equal(0, (await AgencySharingService.Instructions(db, broker, second, new(Search: "life-event"))).Total);
                var previewClients = await AgencySharingService.PreviewClients(db, staffActor, second, new(Size: 100)); Assert.Equal(clients.Items, previewClients.Items);
                Assert.Equal(contacts.Items, (await AgencySharingService.PreviewContacts(db, staffActor, second, new())).Items);
                Assert.Equal(instructions.Items, (await AgencySharingService.PreviewInstructions(db, staffActor, second, new())).Items);
                var json = JsonSerializer.Serialize(new { clients, contacts, instructions });
                foreach (var forbidden in new[] { "SECRET", "Internal", "Marketing", "ConsentBasis", "Reason", "OriginRelationshipId", "History", "accounts@fictional" }) Assert.DoesNotContain(forbidden, json);
                var audit = await db.Set<AuditEvent>().Where(x => x.EventType == "agency.sharing-preview").ToListAsync(); Assert.Equal(3, audit.Count);
                Assert.All(audit, x => { Assert.Equal(staffId, x.ActorId); Assert.Contains(second.ToString(), x.After!); Assert.True(x.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-5)); });
                var staff = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Id == staffId); Assert.Null(staff.AgencyId); Assert.Equal(stamp, staff.SecurityStamp);
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.Clients(db, broker, first, new()));
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.PreviewClients(db, broker, second, new()));
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.PreviewClients(db, staffActor with { UserId = userId }, second, new()));
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.PreviewClients(db, staffActor with { Roles = new HashSet<string> { "system-admin" } }, second, new()));
                foreach (var invalid in new[] { new AgencySharingQuery(Size: 0), new(Size: 101), new(Offset: -1), new(Search: new string('x', 201)), new(RelationshipId: Guid.Empty) })
                    Assert.Equal(400, (await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.Clients(db, broker, second, invalid))).Status);
            }
            // Persisted revocations take effect on the very next read.
            await using (var db = new BackOfficeDbContext(options))
            {
                var grant = await db.Set<FlagVisibility>().SingleAsync(x => x.RelationshipId == own);
                db.Remove(grant); await db.SaveChangesAsync();
                Assert.Empty((await AgencySharingService.Instructions(db, broker, second, new())).Items);
                db.Add(new FlagVisibility { FlagId = grant.FlagId, ClientId = grant.ClientId, RelationshipId = own }); await db.SaveChangesAsync();
                Assert.Single((await AgencySharingService.Instructions(db, broker, second, new())).Items);
                var contact = await db.Set<Contact>().SingleAsync(x => x.Id == ContactDemoSeed.ContactId(3)); contact.EndedAt = DateTimeOffset.UtcNow; contact.EndReason = "Fictional end"; contact.EndedBy = staffId; contact.IsPrimary = false; await db.SaveChangesAsync();
                Assert.Empty((await AgencySharingService.Contacts(db, broker, second, new())).Items);
                Assert.Empty((await AgencySharingService.Instructions(db, broker, second, new())).Items);
                contact.EndedAt = null; contact.EndedBy = null; contact.EndReason = null; await db.SaveChangesAsync();
                Assert.Single((await AgencySharingService.Instructions(db, broker, second, new())).Items);
                var relationship = await db.Set<ClientAgencyRelationship>().SingleAsync(x => x.Id == own); relationship.State = "inactive"; await db.SaveChangesAsync();
                Assert.Equal(0, (await AgencySharingService.Clients(db, broker, second, new(RelationshipId: own))).Total);
                Assert.Empty((await AgencySharingService.Instructions(db, broker, second, new())).Items);
                Assert.Empty((await AgencySharingService.Contacts(db, broker, second, new())).Items);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={second}");
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.Clients(db, broker, second, new()));
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.PreviewClients(db, staffActor, second, new()));
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={second}");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={staffId}");
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.PreviewClients(db, staffActor, second, new()));
            }
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
