using System.Security.Cryptography;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed class AgencyTermsStorageTests
{
    [Fact]public async Task RealSqlAgencyTermsBindIndependentProposalAndRetainPublishedVersions()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db=new BackOfficeDbContext(options);await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
            var requester=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");var reviewer=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
            var agency=new Agency{Reference="AG-TERMS-STORAGE",LegalName="Fictional terms storage"};var other=new Agency{Reference="AG-OTHER-TERMS",LegalName="Fictional other terms"};db.AddRange(agency,other);await db.SaveChangesAsync();
            var state=new AgencyStateRequest{AgencyId=agency.Id,BaseVersion=agency.RowVersion,ProposedInputFingerprint=new string('a',64),RequestedBy=requester.Id,CreatedBy=requester.Id,RequestReason="Fictional activation"};db.Add(state);await db.SaveChangesAsync();
            var productId=await db.Set<ProductVersion>().Select(x=>x.Id).FirstAsync();
            string Snapshot(string date)=>"{\"effectiveFrom\":\""+date+"\",\"commercialTerms\":{\"commissionBasis\":\"per-product\"},\"creditLimit\":\"0.00\",\"products\":[{\"productVersionId\":\""+productId+"\",\"effectiveFrom\":\""+date+"\",\"brokerCommissionBasisPoints\":1250}]}";
            var initial=new AgencyTermsVersion{AgencyId=agency.Id,Version=1,EffectiveFrom=new(2026,9,14),ApprovedStateRequestId=state.Id,CreatedBy=reviewer.Id,Snapshot=Snapshot("2026-09-14")};
            db.Add(initial);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={reviewer.Id},DecisionReason=N'Fictional approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={state.Id}");initial.CreatedAt=DateTimeOffset.UtcNow;db.Add(initial);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            async Task Denied(FormattableString sql)=>await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync(sql));
            await Denied($"UPDATE AgencyTermsVersion SET Snapshot={Snapshot("2026-09-15")} WHERE Id={initial.Id}");await Denied($"DELETE FROM AgencyTermsVersion WHERE Id={initial.Id}");
            var request=new AgencyTermsRequest{AgencyId=agency.Id,BaseVersion=agency.RowVersion,EffectiveFrom=new(2026,10,1),ProposedSnapshot=Snapshot("2026-10-01"),ProposedInputFingerprint=new string('b',64),RequestedBy=requester.Id,CreatedBy=requester.Id,RequestReason="Fictional changed terms"};db.Add(request);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await Denied($"UPDATE AgencyTermsRequest SET ProposedSnapshot={Snapshot("2026-11-01")},EffectiveFrom={new DateOnly(2026,11,1)} WHERE Id={request.Id}");
            await Denied($"UPDATE AgencyTermsRequest SET State=N'applied',DecisionBy={requester.Id},DecisionReason=N'Self approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={request.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyTermsRequest SET State=N'applied',DecisionBy={reviewer.Id},DecisionReason=N'Fictional independent approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={request.Id}");
            var version=new AgencyTermsVersion{AgencyId=other.Id,Version=2,EffectiveFrom=request.EffectiveFrom,ApprovedTermsRequestId=request.Id,CreatedBy=reviewer.Id,Snapshot=request.ProposedSnapshot};db.Add(version);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            version.AgencyId=agency.Id;version.Snapshot=version.Snapshot.Replace("0.00","1.00");db.Add(version);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            version.Snapshot=request.ProposedSnapshot;version.Version=3;db.Add(version);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            version.Version=2;db.Add(version);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await Denied($"UPDATE AgencyTermsRequest SET DecisionReason=N'Changed decision' WHERE Id={request.Id}");await Denied($"DELETE FROM AgencyTermsRequest WHERE Id={request.Id}");
            var versions=await db.Set<AgencyTermsVersion>().AsNoTracking().OrderBy(x=>x.Version).ToListAsync();Assert.Equal(2,versions.Count);Assert.Equal(initial.Snapshot,versions[0].Snapshot);Assert.Equal(request.ProposedSnapshot,versions[1].Snapshot);
            var current=await db.Set<AgencyTermsVersion>().Where(x=>x.AgencyId==agency.Id&&x.EffectiveFrom<=new DateOnly(2026,9,20)).OrderByDescending(x=>x.EffectiveFrom).FirstAsync();Assert.Equal(1,current.Version);
            var grants=await db.Set<AgencyProduct>().AsNoTracking().OrderBy(x=>x.EffectiveFrom).ToListAsync();
            Assert.Equal(2,grants.Count);Assert.All(grants,x=>{Assert.Equal(productId,x.ProductVersionId);Assert.Equal(1250,x.BrokerCommissionBasisPoints);Assert.Equal(reviewer.Id,x.CreatedBy);});
            Assert.Equal(initial.Id,grants[0].AgencyTermsVersionId);Assert.Equal(version.Id,grants[1].AgencyTermsVersionId);
            await Denied($"UPDATE AgencyProduct SET BrokerCommissionBasisPoints=100 WHERE Id={grants[0].Id}");
            await Denied($"DELETE FROM AgencyProduct WHERE Id={grants[0].Id}");
            var forged=new AgencyProduct{AgencyTermsVersionId=version.Id,ProductVersionId=productId,EffectiveFrom=version.EffectiveFrom,BrokerCommissionBasisPoints=100,CreatedBy=reviewer.Id,CreatedAt=version.CreatedAt};
            db.Add(forged);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            Assert.Equal(2,await db.Set<AgencyProduct>().CountAsync());
            var nextSnapshot=Snapshot("2026-11-01");
            var invalidSnapshots=new[]{
                nextSnapshot.Replace(productId.ToString(),Guid.NewGuid().ToString()),
                nextSnapshot.Replace("1250","10001"),
                nextSnapshot, // Replaced below with a duplicate product.
                nextSnapshot // Replaced below with an empty product set.
            };
            // Construct malformed sets structurally; formatting must not determine test inputs.
            var duplicate=System.Text.Json.Nodes.JsonNode.Parse(nextSnapshot)!;duplicate["products"]!.AsArray().Add(duplicate["products"]![0]!.DeepClone());invalidSnapshots[2]=duplicate.ToJsonString();
            var empty=System.Text.Json.Nodes.JsonNode.Parse(nextSnapshot)!;empty["products"]!.AsArray().Clear();invalidSnapshots[3]=empty.ToJsonString();
            foreach(var invalid in invalidSnapshots)
            {
                var badRequest=new AgencyTermsRequest{AgencyId=agency.Id,BaseVersion=agency.RowVersion,EffectiveFrom=new(2026,11,1),ProposedSnapshot=invalid,ProposedInputFingerprint=new string('c',64),RequestedBy=requester.Id,CreatedBy=requester.Id,RequestReason="Storage rollback fixture"};
                db.Add(badRequest);await db.SaveChangesAsync();db.ChangeTracker.Clear();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyTermsRequest SET State=N'applied',DecisionBy={reviewer.Id},DecisionReason=N'Storage fixture',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={badRequest.Id}");
                db.Add(new AgencyTermsVersion{AgencyId=agency.Id,Version=3,EffectiveFrom=badRequest.EffectiveFrom,ApprovedTermsRequestId=badRequest.Id,CreatedBy=reviewer.Id,Snapshot=invalid});
                await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
                Assert.Equal(2,await db.Set<AgencyTermsVersion>().CountAsync());Assert.Equal(2,await db.Set<AgencyProduct>().CountAsync());
            }
            Assert.Equal("draft",(await db.Set<Agency>().SingleAsync(x=>x.Id==agency.Id)).State); // Storage fixture, not an application workflow.
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
}
