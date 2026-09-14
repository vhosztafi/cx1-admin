using System.Security.Cryptography;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed class AgencyFollowUpTests
{
    [Fact]public async Task RealSqlFollowUpsRetainOwnedEvidenceAndAppliedActivationProvenance()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db=new BackOfficeDbContext(options);await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
            var actor=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");var reviewer=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-reviewer@cover.example");
            var agency=new Agency{Reference="AG-FOLLOWUP",LegalName="Fictional follow-up"};var other=new Agency{Reference="AG-FOLLOWUP-OTHER",LegalName="Fictional other"};db.AddRange(agency,other);await db.SaveChangesAsync();
            var file=new AgencyEvidenceFile{AgencyId=agency.Id,FileName="proof.txt",ContentType="text/plain",Content="Fictional PI"u8.ToArray(),ByteLength=12,Sha256=new string('a',64),CreatedBy=actor.Id};
            // Match stored byte length/hash rather than bypass the existing evidence guards.
            file.ByteLength=file.Content.Length;file.Sha256=Convert.ToHexString(SHA256.HashData(file.Content)).ToLowerInvariant();db.Add(file);await db.SaveChangesAsync();
            var expires=DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);var rule=await db.Set<SettingVersion>().FirstAsync(x=>x.Scope=="agency-compliance");
            var evidence=new AgencyEvidence{AgencyId=agency.Id,Kind="professional-indemnity",State="verified",InputFingerprint=new string('b',64),RuleVersionId=rule.Id,FileId=file.Id,AttestedBy=actor.Id,Notes="Fictional storage evidence",VerifiedAt=DateTimeOffset.UtcNow,ExpiresOn=expires,CreatedBy=actor.Id};db.Add(evidence);await db.SaveChangesAsync();
            var request=new AgencyStateRequest{AgencyId=agency.Id,BaseVersion=agency.RowVersion,ProposedInputFingerprint=new string('c',64),RequestedBy=actor.Id,CreatedBy=actor.Id,RequestReason="Fictional activation fixture"};db.Add(request);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            async Task Denied(AgencyFollowUp row){db.Add(row);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();}
            var quarter=new AgencyFollowUp{AgencyId=agency.Id,ActivationRequestId=request.Id,Purpose="quarter-review",DueOn=DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3),CreatedBy=reviewer.Id};
            await Denied(quarter); // A pending request is not an activation.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={reviewer.Id},DecisionReason=N'Fixture decision',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={request.Id}");
            quarter.CreatedAt=DateTimeOffset.UtcNow;db.Add(quarter);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var pi=new AgencyFollowUp{AgencyId=agency.Id,EvidenceId=evidence.Id,Purpose="pi-expiry",DueOn=expires,CreatedBy=reviewer.Id};db.Add(pi);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await Denied(new(){AgencyId=agency.Id,EvidenceId=evidence.Id,Purpose="pi-expiry",DueOn=expires,CreatedBy=reviewer.Id});
            await Denied(new(){AgencyId=other.Id,EvidenceId=evidence.Id,Purpose="pi-expiry",DueOn=expires,CreatedBy=reviewer.Id});
            await Denied(new(){AgencyId=agency.Id,EvidenceId=evidence.Id,Purpose="pi-expiry",DueOn=expires.AddDays(1),CreatedBy=reviewer.Id});
            await Denied(new(){AgencyId=other.Id,ActivationRequestId=request.Id,Purpose="quarter-review",DueOn=quarter.DueOn,CreatedBy=reviewer.Id});
            await Denied(new(){AgencyId=agency.Id,ActivationRequestId=request.Id,Purpose="quarter-review",DueOn=quarter.DueOn.AddDays(1),CreatedBy=actor.Id});
            await Denied(new(){AgencyId=agency.Id,EvidenceId=evidence.Id,ActivationRequestId=request.Id,Purpose="pi-expiry",DueOn=expires,CreatedBy=reviewer.Id});
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyFollowUp SET DueOn={expires.AddDays(1)} WHERE Id={pi.Id}"));
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AgencyFollowUp WHERE Id={quarter.Id}"));
            await using var reloaded=new BackOfficeDbContext(options);var rows=await reloaded.Set<AgencyFollowUp>().AsNoTracking().OrderBy(x=>x.Purpose).ToListAsync();Assert.Equal(2,rows.Count);Assert.All(rows,x=>Assert.Equal(agency.Id,x.AgencyId));Assert.Equal(expires,rows[0].DueOn);Assert.Equal(request.Id,rows[1].ActivationRequestId);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
}
