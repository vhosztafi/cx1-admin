using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlOperationalFileLegacyQuoteAndServicingKeepOriginalBytesAndOwners()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(); var originalSnapshot = version.SnapshotJson;
            var drafts = new ServicingDraftService(f.Factory, f.Clock); var listed = await drafts.ListAsync(f.Servicing, version.TermId);
            var created = await drafts.CreateAsync(f.Servicing, version.TermId, Convert.FromBase64String(listed.Etag.Trim('"')),
                new("adjustment", version.Id, JsonSerializer.SerializeToElement(new { localDate = "2026-10-01", localTime = "00:00", timeZone = "Europe/London" }), "Fictional evidence bridge check"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var originalQuoteFiles = await db.Set<QuoteEvidenceFile>().AsNoTracking().Where(x => x.QuoteId == f.QuoteId).OrderBy(x => x.Id).ToArrayAsync();
            Assert.NotEmpty(originalQuoteFiles); var quoteFile = originalQuoteFiles[0];
            var sourceBytes = Encoding.ASCII.GetBytes("%PDF-1.7\nFictional servicing evidence\n%%EOF\n");
            var servicingFile = new ServicingEvidenceFile { DraftId = created.ResourceId, FileName = "servicing.pdf", ContentType = "application/pdf", Content = sourceBytes,
                ByteLength = sourceBytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(sourceBytes)), CreatedBy = f.Servicing.UserId };
            db.Add(servicingFile); await db.SaveChangesAsync();
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CoverMGA_LegacyFiles")); var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            try
            {
                var boundary = new SqlCommandBoundary(f.Factory, f.Clock); var tasks = new TaskService(f.Factory, boundary, f.Clock);
                var files = new FileService(f.Factory, boundary, new OperationalFileStore(root, []), f.Clock);
                var quoteSubject = await tasks.Register(f.Underwriter, new("quote", f.QuoteId), "quote-files", default);
                var draftSubject = await tasks.Register(f.Underwriter, new("servicing-draft", created.ResourceId), "draft-files", default);
                await Assert.ThrowsAsync<OperationalAccessException>(() => files.BridgeLegacy(f.Underwriter, draftSubject.ResourceId, "quote-evidence", quoteFile.Id, "wrong-parent", default));
                foreach (var source in new[] { (Kind: "quote-evidence", Subject: quoteSubject.ResourceId, Id: quoteFile.Id, Bytes: quoteFile.Content), (Kind: "servicing-evidence", Subject: draftSubject.ResourceId, Id: servicingFile.Id, Bytes: sourceBytes) })
                {
                    var bridge = await files.BridgeLegacy(f.Underwriter, source.Subject, source.Kind, source.Id, source.Kind, default);
                    Assert.Equal(bridge, await files.BridgeLegacy(f.Underwriter, source.Subject, source.Kind, source.Id, source.Kind + "-replay", default));
                    await using var download = await files.DownloadFile(f.Underwriter, bridge, default);
                    using var actual = new MemoryStream(); await download.Content.CopyToAsync(actual); Assert.Equal(source.Bytes, actual.ToArray());
                }
                Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
                Assert.Equal(JsonSerializer.Serialize(originalQuoteFiles), JsonSerializer.Serialize(await db.Set<QuoteEvidenceFile>().AsNoTracking().Where(x => x.QuoteId == f.QuoteId).OrderBy(x => x.Id).ToArrayAsync()));
                Assert.Equal(sourceBytes, (await db.Set<ServicingEvidenceFile>().AsNoTracking().SingleAsync(x => x.Id == servicingFile.Id)).Content);
                Assert.Equal(originalSnapshot, (await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);
                var originals = await db.Set<FileObject>().AsNoTracking().ToArrayAsync(); Assert.Equal(2, originals.Length); Assert.All(originals, row => Assert.Null(row.WorkId));
            }
            finally
            {
                var resolved = Path.GetFullPath(root);
                if (!resolved.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(resolved).Length != 32) throw new InvalidOperationException("Legacy file fixture target changed.");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        });
    }
}
