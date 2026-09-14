using System.Security.Cryptography;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed class AgencyApprovalStorageTests
{
    [Fact]public async Task RealSqlAgencyStateProposalPreservesBaseAndRequiresIndependentImmutableDecision()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db=new BackOfficeDbContext(options);await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
            var requester=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");var reviewer=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");var underwriter=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="underwriter@cover.example");
            var agency=new Agency{Reference="AG-PROPOSAL-STORAGE",LegalName="Fictional proposal storage"};db.Add(agency);await db.SaveChangesAsync();var initialVersion=agency.RowVersion.ToArray();
            AgencyStateRequest New()=>new(){AgencyId=agency.Id,BaseVersion=initialVersion,ProposedInputFingerprint=new string('a',64),RequestedBy=requester.Id,CreatedBy=requester.Id,RequestReason="Fictional activation review"};
            var request=New();db.Add(request);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            Assert.Equal(initialVersion,(await db.Set<Agency>().AsNoTracking().SingleAsync(x=>x.Id==agency.Id)).RowVersion);
            Assert.Equal(initialVersion,(await db.Set<AgencyStateRequest>().AsNoTracking().SingleAsync()).BaseVersion);
            var duplicate=New();db.Add(duplicate);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            async Task Denied(FormattableString sql)=>await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync(sql));
            await Denied($"UPDATE AgencyStateRequest SET RequestReason=N'fictional activation review' WHERE Id={request.Id}");
            await Denied($"UPDATE AgencyStateRequest SET ProposedInputFingerprint={new string('b',64)} WHERE Id={request.Id}");
            await Denied($"UPDATE AgencyStateRequest SET RequestedState=N'suspended' WHERE Id={request.Id}");
            var now=DateTimeOffset.UtcNow;
            await Denied($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={requester.Id},DecisionReason=N'Self approval',DecidedAt={now} WHERE Id={request.Id}");
            await Denied($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={underwriter.Id},DecisionReason=N'Wrong role',DecidedAt={now} WHERE Id={request.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'rejected',DecisionBy={reviewer.Id},DecisionReason=N'Fictional rejection',DecidedAt={now} WHERE Id={request.Id}");
            var saved=await db.Set<AgencyStateRequest>().AsNoTracking().SingleAsync();Assert.Equal("rejected",saved.State);Assert.Equal(reviewer.Id,saved.DecisionBy);Assert.Equal("Fictional activation review",saved.RequestReason);
            await Denied($"UPDATE AgencyStateRequest SET DecisionReason=N'Changed history' WHERE Id={request.Id}");await Denied($"DELETE FROM AgencyStateRequest WHERE Id={request.Id}");
            var next=New();db.Add(next);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'stale',DecisionReason=N'Base changed',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={next.Id}");
            await Denied($"UPDATE AgencyStateRequest SET State=N'pending',DecisionReason=NULL,DecidedAt=NULL WHERE Id={next.Id}");
            var predecided=New();predecided.State="applied";predecided.DecisionBy=reviewer.Id;predecided.DecisionReason="Skip pending";predecided.DecidedAt=DateTimeOffset.UtcNow;db.Add(predecided);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            var wrongRequester=New();wrongRequester.RequestedBy=underwriter.Id;wrongRequester.CreatedBy=underwriter.Id;db.Add(wrongRequester);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            Assert.Equal(2,await db.Set<AgencyStateRequest>().CountAsync());Assert.Equal("draft",(await db.Set<Agency>().SingleAsync(x=>x.Id==agency.Id)).State);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
}
