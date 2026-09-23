using System.Security.Cryptography;
using System.Text;
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
    [Fact]
    public Task RealSqlOperationalDocumentStoragePreservesVersionSourceAndSingleContentBinding() => WithDatabase(async (db, password) =>
    {
        Assert.False(db.Database.HasPendingModelChanges());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260921180754_OperationalDocumentTemplates");
        await migrator.MigrateAsync();
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var request = await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x => x.Kind == "policy-schedule");
        var policy = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var template = await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x => x.Id == request.TemplateVersionId);
        var subject = new OperationalSubject { Kind = "policy", PolicyId = policy.PolicyId, CreatedBy = f.Underwriter.UserId };
        db.Add(subject); await db.SaveChangesAsync();
        var document = new OperationalDocument { SubjectId = subject.Id, Kind = "policy-schedule", Visibility = "internal", CreatedBy = f.Underwriter.UserId };
        db.Add(document); await db.SaveChangesAsync();
        DocumentVersion Version() => new() { DocumentId = document.Id, Number = 1, SourceKind = "policy-version", PolicyVersionId = policy.Id,
            TemplateVersionId = template.Id, SourceHash = Convert.ToHexStringLower(policy.ContentHash), TemplateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(template.ContentJson))),
            PolicyDocumentRequestId = request.Id, WorkId = request.WorkId, OriginalName = "schedule.pdf", Reason = "Original issue request", CreatedBy = f.Underwriter.UserId };
        var invalid = Version(); invalid.SourceHash = new string('b',64); db.Add(invalid);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        invalid = Version(); invalid.TemplateHash = new string('b',64); db.Add(invalid);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var version = Version(); db.Add(version); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Add(Version()); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE DocumentVersion SET SourceHash={new string('c',64)} WHERE Id={version.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DocumentVersion WHERE Id={version.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Document SET Visibility=N'insurer' WHERE Id={document.Id}"));
        var work = new OutboxWork { Kind = "file-finalization", SubjectRecordId = subject.Id, OperationKey = "document-file/" + version.Id, CreatedBy = f.Underwriter.UserId };
        var file = new FileObject { SubjectId = subject.Id, WorkId = work.Id, FileName = version.OriginalName, MediaType = "application/pdf", ByteLength = 100,
            Sha256 = new string('a',64), CreatedBy = f.Underwriter.UserId };
        db.AddRange(work,file); await db.SaveChangesAsync();
        var content = new DocumentVersionContent { VersionId = version.Id, FileObjectId = file.Id, PageCount = 2,
            RendererVersion = "pdfsharp-migradoc-6.2.4-cover-1", ProjectionVersion = "policy-projection-1", FontVersion = "ibm-plex-sans-6.4.0-383c681f", CreatedBy = f.Underwriter.UserId };
        var foreignAgency = new Agency { Reference = "AG-DOC-FOREIGN", LegalName = "Fictional Foreign Documents" }; db.Add(foreignAgency); await db.SaveChangesAsync();
        var foreignSubject = new OperationalSubject { Kind = "agency", AgencyId = foreignAgency.Id, CreatedBy = f.Underwriter.UserId }; db.Add(foreignSubject); await db.SaveChangesAsync();
        var foreignWork = new OutboxWork { Kind = "file-finalization", SubjectRecordId = foreignSubject.Id, OperationKey = Guid.NewGuid().ToString(), CreatedBy = f.Underwriter.UserId };
        var foreignFile = new FileObject { SubjectId = foreignSubject.Id, WorkId = foreignWork.Id, FileName = version.OriginalName, MediaType = "application/pdf", ByteLength = 100,
            Sha256 = new string('a',64), CreatedBy = f.Underwriter.UserId }; db.AddRange(foreignWork,foreignFile); await db.SaveChangesAsync();
        content.FileObjectId = foreignFile.Id; db.Add(content);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        content.FileObjectId = file.Id;
        db.Add(content); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        db.Add(new DocumentVersionContent { VersionId = version.Id, FileObjectId = file.Id, PageCount = 2,
            RendererVersion = content.RendererVersion, ProjectionVersion = content.ProjectionVersion, FontVersion = content.FontVersion, CreatedBy = f.Underwriter.UserId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE DocumentVersionContent SET PageCount=3 WHERE Id={content.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM DocumentVersionContent WHERE Id={content.Id}"));
        Assert.Equal(request.PayloadJson,(await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Id==request.Id)).PayloadJson);
        Assert.Equal("pending",(await db.Set<FileObject>().SingleAsync(x=>x.Id==file.Id)).State);
        Assert.Equal(file.Id,(await db.Set<DocumentVersionContent>().SingleAsync()).FileObjectId);
        var retainedVersion = await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x => x.Id == version.Id);
        var retainedContent = await db.Set<DocumentVersionContent>().AsNoTracking().SingleAsync(x => x.Id == content.Id);
        await AssertRetainedMidDowngradeRefused(db, "20260921180754_OperationalDocumentTemplates");
        Assert.Equal(retainedVersion.SourceHash, (await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x => x.Id == version.Id)).SourceHash);
        Assert.Equal(retainedContent.FileObjectId, (await db.Set<DocumentVersionContent>().AsNoTracking().SingleAsync(x => x.Id == content.Id)).FileObjectId);
    });
}
