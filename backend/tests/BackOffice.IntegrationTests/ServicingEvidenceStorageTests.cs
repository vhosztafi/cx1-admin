using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlServicingEvidenceFilesAreOwnedScreenedAndImmutable(string product)
    {
        await WithDatabase(async (db,password)=>
        {
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issued=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            // Test the additive migration over an issued graph in this isolated database.
            var previous=db.Database.GetMigrations().TakeWhile(x=>!x.EndsWith("_ServicingEvidenceFiles",StringComparison.Ordinal)).Last();
            await db.GetService<IMigrator>().MigrateAsync(previous);await db.Database.MigrateAsync();db.ChangeTracker.Clear();
            var drafts=new ServicingDraftService(f.Factory,f.Clock);
            var created=await drafts.CreateAsync(f.Servicing,issued.TermId,Convert.FromBase64String((await drafts.ListAsync(f.Servicing,issued.TermId)).Etag.Trim('"')),
                new("adjustment",issued.Id,JsonSerializer.SerializeToElement(new{localDate="2026-10-01",localTime="00:00",timeZone="Europe/London"}),"Fictional servicing proof storage"),Guid.NewGuid().ToString(),Guid.NewGuid());
            var content=Encoding.UTF8.GetBytes("Fictional proof, not genuine personal information.");
            var hash=Convert.ToHexStringLower(SHA256.HashData(content));var now=f.Clock.GetUtcNow();
            async Task Insert(Guid id,Guid draft,string name="proof.txt",string type="text/plain",int? length=null,string? digest=null,string screening="accepted",Guid? actor=null) =>
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingEvidenceFile (Id,DraftId,FileName,ContentType,Content,ByteLength,Sha256,ScreeningState,ScreeningMethod,CreatedBy,CreatedAt) VALUES ({id},{draft},{name},{type},{content},{length??content.Length},{digest??hash},{screening},'demo-signature-v1',{actor??f.Servicing.UserId},{now})");
            var fileId=Guid.NewGuid();await Insert(fileId,created.ResourceId);
            now=now.AddSeconds(-1);var early=await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),created.ResourceId));Assert.Equal(51311,early.Number);now=now.AddSeconds(1);
            var stored=await db.Database.SqlQueryRaw<string>("SELECT Sha256 AS Value FROM ServicingEvidenceFile").SingleAsync();Assert.Equal(hash,stored);
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),Guid.NewGuid()));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),created.ResourceId,name:"../proof.txt"));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),created.ResourceId,type:"text/html"));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),created.ResourceId,length:1));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),created.ResourceId,digest:new string('b',64)));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),created.ResourceId,screening:"pending"));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),created.ResourceId,actor:Guid.NewGuid()));
            var update=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceFile SET FileName='changed.txt' WHERE Id={fileId}"));
            Assert.Equal(51310,update.Number);
            var delete=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingEvidenceFile WHERE Id={fileId}"));Assert.Equal(51310,delete.Number);
            Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingEvidenceFile").SingleAsync());
            Assert.Equal(issued.SnapshotJson,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);
        });
    }
}
