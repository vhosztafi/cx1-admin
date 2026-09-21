using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalFileCommandTests
{
    [Fact]
    public async Task RealSqlUploadReplayRollbackCrashRecoveryAndRevokedAuthority()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CoverMGA_FileCommands")); var root = Path.Combine(parent, owned);
        try
        {
            await using var db = new BackOfficeDbContext(options); await db.Database.MigrateAsync();
            var user = new StaffUser { Email = "file-command@cover.example", NormalizedEmail = "FILE-COMMAND@COVER.EXAMPLE", DisplayName = "File command actor" };
            var role = new Role { Code = "agency-admin", Scope = "internal" };
            var agency = new Agency { Reference = "AG-FILE-COMMAND", LegalName = "Fictional Files" };
            db.AddRange(user, role, agency); await db.SaveChangesAsync(); db.Add(new UserRole { UserId = user.Id, RoleId = role.Id }); await db.SaveChangesAsync();
            var actor = new ActorContext(user.Id, null, null, new HashSet<string> { "agency-admin" });
            var factory = new Factory(options); var boundary = new SqlCommandBoundary(factory, TimeProvider.System);
            var subject = await new TaskService(factory, boundary, TimeProvider.System).Register(actor, new("agency", agency.Id), "subject", default);
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nFictional test document\n%%EOF\n");
            var store = new OperationalFileStore(root, []);
            var service = new FileService(factory, boundary, store, TimeProvider.System);
            Task<CommandOutcome> Upload(FileService target, string key, ActorContext? who = null, byte[]? content = null) => target.Upload(who ?? actor, subject.ResourceId, "fictional.pdf", "application/pdf", new MemoryStream(content ?? bytes), key, default);
            var rollback = new FileService(factory, boundary, store, TimeProvider.System, point => { if (point == FileCommandFault.BeforeMetadataCommit) throw new IOException("Simulated SQL rollback"); });
            await Assert.ThrowsAsync<IOException>(() => Upload(rollback, "rollback"));
            Assert.Empty(await db.Set<FileObject>().ToListAsync()); Assert.Empty(await db.Set<OutboxWork>().Where(x => x.Kind == "file-finalization").ToListAsync());
            var created = await Upload(service, "upload"); Assert.Equal(202, created.Status);
            using (var payload = JsonDocument.Parse(created.Body)) { Assert.Equal("pending", payload.RootElement.GetProperty("state").GetString()); Assert.False(payload.RootElement.TryGetProperty("fileId", out _)); }
            Assert.True((await Upload(service, "upload")).Replayed);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => Upload(service, "upload", content: Encoding.ASCII.GetBytes("%PDF-1.7\nChanged fictional bytes\n%%EOF\n")));
            Assert.Single(await db.Set<FileObject>().ToListAsync());
            Assert.Equal(409, (await Assert.ThrowsAsync<OperationalAccessException>(() => service.DownloadUpload(actor, created.ResourceId, default))).Status);
            var failOnce = true;
            var crashingStore = new OperationalFileStore(root, [], (point, _) => { if (point == OperationalFileFault.AfterRename && failOnce) { failOnce = false; throw new SimulatedCrash(); } });
            var worker = new FileFinalizationWorker(factory, crashingStore, TimeProvider.System);
            await Assert.ThrowsAsync<SimulatedCrash>(() => worker.Process(created.ResourceId, default));
            Assert.Equal("pending", (await db.Set<FileObject>().AsNoTracking().SingleAsync()).State);
            var restarted = new FileFinalizationWorker(factory, new OperationalFileStore(root, []), TimeProvider.System);
            Assert.True(await restarted.Process(created.ResourceId, default));
            Assert.False(await restarted.Process(created.ResourceId, default));
            await using (var downloaded = await service.DownloadUpload(actor, created.ResourceId, default))
            {
                using var actual = new MemoryStream(); await downloaded.Content.CopyToAsync(actual);
                Assert.Equal(bytes, actual.ToArray()); Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), downloaded.Sha256);
            }
            Assert.Equal("succeeded", (await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == created.ResourceId)).State);
            var lostResponse = new FileService(factory, boundary, store, TimeProvider.System, point => { if (point == FileCommandFault.AfterMetadataCommit) throw new IOException("Simulated lost committed response"); });
            await Assert.ThrowsAsync<IOException>(() => Upload(lostResponse, "lost-response"));
            var recovered = await Upload(service, "lost-response"); Assert.True(recovered.Replayed);
            Assert.True(await restarted.Process(recovered.ResourceId, default));
            var legacyBytes = Encoding.UTF8.GetBytes("Fictional original retained text evidence");
            var legacy = new AgencyEvidenceFile { AgencyId = agency.Id, CreatedBy = user.Id, FileName = "retained.txt", ContentType = "text/plain", Content = legacyBytes,
                ByteLength = legacyBytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(legacyBytes)) };
            db.Add(legacy); await db.SaveChangesAsync();
            var bridge = await service.BridgeLegacy(actor, subject.ResourceId, "agency-evidence", legacy.Id, "legacy", default);
            Assert.Equal(bridge, await service.BridgeLegacy(actor, subject.ResourceId, "agency-evidence", legacy.Id, "legacy-another-key", default));
            await using (var downloaded = await service.DownloadFile(actor, bridge, default))
            {
                using var actual = new MemoryStream(); await downloaded.Content.CopyToAsync(actual); Assert.Equal(legacyBytes, actual.ToArray()); Assert.Equal("text/plain", downloaded.MediaType);
            }
            Assert.Equal(legacyBytes, (await db.Set<AgencyEvidenceFile>().AsNoTracking().SingleAsync()).Content);
            var corrupt = await Upload(service, "corrupt");
            var corruptId = await db.Set<FileObject>().Where(x => x.WorkId == corrupt.ResourceId).Select(x => x.Id).SingleAsync();
            await File.WriteAllBytesAsync(Path.Combine(root, "pending", corruptId.ToString("N") + ".upload"), Encoding.ASCII.GetBytes("Tampered bytes"));
            Assert.True(await restarted.Process(corrupt.ResourceId, default));
            Assert.Equal("quarantined", (await db.Set<FileObject>().AsNoTracking().SingleAsync(x => x.Id == corruptId)).State);
            Assert.Equal(409, (await Assert.ThrowsAsync<OperationalAccessException>(() => service.DownloadUpload(actor, corrupt.ResourceId, default))).Status);
            await using (var transaction = await db.Database.BeginTransactionAsync()) { await WorkflowTaskSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var ruleId = await db.Set<SettingVersion>().Where(x => x.Scope == "workflow-task/demo-job-exception").Select(x => x.Id).SingleAsync();
            var exceptionId = await db.Set<JobException>().Where(x => x.WorkId == corrupt.ResourceId).Select(x => x.Id).SingleAsync();
            var workflows = new WorkflowTaskService(factory, new TaskService(factory, boundary, TimeProvider.System), TimeProvider.System);
            var taskId = await workflows.Reconcile(ruleId, "job-exception", exceptionId, default); Assert.NotNull(taskId);
            Assert.Equal(taskId, await workflows.Reconcile(ruleId, "job-exception", exceptionId, default));
            Assert.Equal(subject.ResourceId, (await db.Set<OperationalTask>().AsNoTracking().SingleAsync(x => x.Id == taskId)).SubjectId);
            var revoked = await Upload(service, "revoked");
            var referencedId = await db.Set<FileObject>().Where(x => x.WorkId == revoked.ResourceId).Select(x => x.Id).SingleAsync();
            foreach (var path in Directory.GetFiles(Path.Combine(root, "pending"))) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
            Assert.True(await service.CleanupExpiredTemporary(default) > 0);
            Assert.True(File.Exists(Path.Combine(root, "pending", referencedId.ToString("N") + ".upload")));
            Assert.True(File.Exists(Path.Combine(root, "pending", corruptId.ToString("N") + ".upload")));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "pending")).Length);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "ready")).Length);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={user.Id}");
            Assert.Equal(403, (await Assert.ThrowsAsync<OperationalAccessException>(() => Upload(service, "upload"))).Status);
            await Assert.ThrowsAsync<OperationalAccessException>(() => service.DownloadUpload(actor, created.ResourceId, default));
            await Assert.ThrowsAsync<OperationalAccessException>(() => restarted.Process(revoked.ResourceId, default));
            Assert.Equal("pending", (await db.Set<FileObject>().AsNoTracking().SingleAsync(x => x.WorkId == revoked.ResourceId)).State);
        }
        finally
        {
            if (connection.InitialCatalog != owned || !owned.StartsWith("CoverMGA_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(resolved) != owned) throw new InvalidOperationException("File cleanup target changed.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    private sealed class SimulatedCrash : Exception;
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(options);
    }
}
