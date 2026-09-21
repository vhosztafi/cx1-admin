using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalFileMetadataTests
{
    [Fact]
    public async Task RealSqlFileMetadataPreservesOwnershipContentAndTerminalStates()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            var user = new StaffUser { Email = "files-storage@cover.example", NormalizedEmail = "FILES-STORAGE@COVER.EXAMPLE", DisplayName = "File storage" };
            var agency = new Agency { Reference = "AG-FILES-STORAGE", LegalName = "Fictional File Agency" };
            db.AddRange(user, agency); await db.SaveChangesAsync();
            var subject = new OperationalSubject { Kind = "agency", AgencyId = agency.Id, CreatedBy = user.Id };
            var work = new OutboxWork { Kind = "file-finalization", SubjectRecordId = subject.Id, OperationKey = Guid.NewGuid().ToString("N"), CreatedBy = user.Id, NextAttemptAt = DateTimeOffset.UtcNow };
            db.AddRange(subject, work); await db.SaveChangesAsync();
            FileObject Local() => new() { SubjectId = subject.Id, FileName = "fictional.pdf", MediaType = "application/pdf", ByteLength = 100, Sha256 = new string('a', 64), WorkId = work.Id, CreatedBy = user.Id };
            async Task Rejected(FileObject row)
            {
                db.Add(row); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            }
            var invalid = Local(); invalid.WorkId = null; await Rejected(invalid);
            invalid = Local(); invalid.ByteLength = 0; await Rejected(invalid);
            invalid = Local(); invalid.ByteLength = 20971521; await Rejected(invalid);
            invalid = Local(); invalid.Sha256 = new string('z', 64); await Rejected(invalid);
            invalid = Local(); invalid.State = "ready"; await Rejected(invalid);
            invalid = Local(); invalid.StorageKind = "arbitrary-path"; await Rejected(invalid);
            invalid = Local(); invalid.AgencyEvidenceFileId = Guid.NewGuid(); await Rejected(invalid);
            var file = Local(); db.Add(file); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FileObject SET Sha256={new string('b', 64)} WHERE Id={file.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FileObject SET FileName=N'changed.pdf' WHERE Id={file.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM FileObject WHERE Id={file.Id}"));
            var verified = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FileObject SET State=N'ready',VerifiedAt={verified} WHERE Id={file.Id}");
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FileObject SET State=N'pending',VerifiedAt=NULL WHERE Id={file.Id}"));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FileObject SET State=N'quarantined',FailureCode=N'file-content-mismatch' WHERE Id={file.Id}");
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE FileObject SET State=N'ready',FailureCode=NULL WHERE Id={file.Id}"));
            await using var reload = new BackOfficeDbContext(options);
            var retained = await reload.Set<FileObject>().SingleAsync();
            Assert.Equal("quarantined", retained.State); Assert.Equal(file.Sha256, retained.Sha256); Assert.Equal(subject.Id, retained.SubjectId); Assert.Equal(verified, retained.VerifiedAt);
            var bytes = System.Text.Encoding.UTF8.GetBytes("Fictional retained legacy evidence");
            var legacy = new AgencyEvidenceFile { AgencyId = agency.Id, CreatedBy = user.Id, FileName = "retained.txt", ContentType = "text/plain", Content = bytes, ByteLength = bytes.Length, Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) };
            var foreignAgency = new Agency { Reference = "AG-FILES-FOREIGN", LegalName = "Fictional Foreign Agency" };
            db.AddRange(legacy, foreignAgency); await db.SaveChangesAsync();
            var foreignSubject = new OperationalSubject { Kind = "agency", AgencyId = foreignAgency.Id, CreatedBy = user.Id }; db.Add(foreignSubject); await db.SaveChangesAsync();
            FileObject Bridge() => new() { SubjectId = subject.Id, StorageKind = "agency-evidence", AgencyEvidenceFileId = legacy.Id, FileName = legacy.FileName, MediaType = legacy.ContentType, ByteLength = legacy.ByteLength, Sha256 = legacy.Sha256, CreatedBy = user.Id, State = "ready", VerifiedAt = DateTimeOffset.UtcNow };
            var bridge = Bridge(); bridge.SubjectId = foreignSubject.Id; await Rejected(bridge);
            bridge = Bridge(); bridge.Sha256 = new string('b', 64); await Rejected(bridge);
            bridge = Bridge(); bridge.FileName = "different.txt"; await Rejected(bridge);
            bridge = Bridge(); db.Add(bridge); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await Rejected(Bridge());
            Assert.Equal(bytes, (await db.Set<AgencyEvidenceFile>().SingleAsync()).Content);
            Assert.Equal(legacy.Id, (await db.Set<FileObject>().SingleAsync(x => x.StorageKind == "agency-evidence")).AgencyEvidenceFileId);
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
