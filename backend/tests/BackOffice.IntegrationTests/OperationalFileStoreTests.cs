using System.Security.Cryptography;
using System.Text;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalFileStoreTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n");

    [Theory]
    [InlineData(OperationalFileFault.BeforeRename)]
    [InlineData(OperationalFileFault.AfterRename)]
    public async Task DurableFileFinalizationRecoversRenameBoundariesWithoutReplacingReadyBytes(OperationalFileFault boundary)
    {
        await WithRoot(async root =>
        {
            var once = true;
            var store = new OperationalFileStore(root, [], (point, _) => { if (point == boundary && once) { once = false; throw new IOException("Simulated process boundary"); } });
            var staged = await store.Stage("fictional.pdf", "application/pdf", new MemoryStream(Pdf), FileRules.MaximumFileBytes, default);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Pdf)), staged.Sha256);
            await Assert.ThrowsAsync<OperationalFileStoreException>(() => store.OpenReady(staged.Id, staged.Description.ByteLength, staged.Sha256, default));
            await Assert.ThrowsAsync<IOException>(() => store.Finalize(staged.Id, staged.Description.ByteLength, staged.Sha256, default));
            var restarted = new OperationalFileStore(root, []);
            await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => new OperationalFileStore(root, []).Finalize(staged.Id, staged.Description.ByteLength, staged.Sha256, default)));
            await using var stream = await restarted.OpenReady(staged.Id, staged.Description.ByteLength, staged.Sha256, default);
            using var saved = new MemoryStream(); await stream.CopyToAsync(saved); Assert.Equal(Pdf, saved.ToArray());
            await Assert.ThrowsAsync<OperationalFileStoreException>(() => restarted.Finalize(staged.Id, staged.Description.ByteLength, new string('a', 64), default));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "ready")));
        });
    }

    [Fact]
    public async Task BoundedStreamingRejectsOversizeAndSpoofsWithoutLeavingPartialObjects()
    {
        await WithRoot(async root =>
        {
            var store = new OperationalFileStore(root, []);
            await Assert.ThrowsAsync<FileRuleException>(() => store.Stage("fictional.pdf", "application/pdf", new MemoryStream(Pdf), 10, default));
            await Assert.ThrowsAsync<FileRuleException>(() => store.Stage("fictional.png", "image/png", new MemoryStream(Pdf), 1000, default));
            await Assert.ThrowsAsync<FileRuleException>(() => store.Stage("../escape.pdf", "application/pdf", new MemoryStream(Pdf), 1000, default));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "pending"))); Assert.Empty(Directory.GetFiles(Path.Combine(root, "ready")));
        });
    }

    [Fact]
    public async Task CrashAfterFlushLeavesOnlyAnExpiredRecoverableOrphan()
    {
        await WithRoot(async root =>
        {
            Guid orphan = default;
            var store = new OperationalFileStore(root, [], (point, id) =>
            {
                if (point == OperationalFileFault.AfterStageFlush) { orphan = id; throw new IOException("Simulated lost response before SQL commit"); }
            });
            await Assert.ThrowsAsync<IOException>(() => store.Stage("orphan.pdf", "application/pdf", new MemoryStream(Pdf), 1000, default));
            Assert.NotEqual(Guid.Empty, orphan);
            Assert.Single(Directory.GetFiles(Path.Combine(root, "pending")));
            var restarted = new OperationalFileStore(root, []);
            await Assert.ThrowsAsync<OperationalFileStoreException>(() => restarted.OpenReady(orphan, Pdf.Length, Convert.ToHexStringLower(SHA256.HashData(Pdf)), default));
            Assert.True(await restarted.DeleteExpiredTemporary(orphan, DateTimeOffset.MaxValue, _ => Task.FromResult(false), default));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "pending")));
        });
    }

    [Fact]
    public async Task NonSeekableStreamingBoundsReadsAndPreservesHashAcrossManyChunks()
    {
        await WithRoot(async root =>
        {
            var store = new OperationalFileStore(root, []);
            var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 200000) + "\n%%EOF\n");
            using var oversized = new ChunkedStream(bytes, 17);
            await Assert.ThrowsAsync<FileRuleException>(() => store.Stage("large.pdf", "application/pdf", oversized, 1000, default));
            Assert.Equal(1001, oversized.BytesRead);
            using var source = new ChunkedStream(bytes, 739);
            var staged = await store.Stage("large.pdf", "application/pdf", source, bytes.Length, default);
            Assert.Equal(bytes.Length, staged.Description.ByteLength);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), staged.Sha256);
            await store.Finalize(staged.Id, bytes.Length, staged.Sha256, default);
            await using var download = await store.OpenReady(staged.Id, bytes.Length, staged.Sha256, default);
            using var actual = new MemoryStream(); await download.CopyToAsync(actual);
            Assert.Equal(bytes, actual.ToArray());
        });
    }

    [Fact]
    public async Task ChangedBytesFailClosedBeforeAndAfterFinalization()
    {
        await WithRoot(async root =>
        {
            var store = new OperationalFileStore(root, []);
            var staged = await store.Stage("original.pdf", "application/pdf", new MemoryStream(Pdf), 1000, default);
            var changed = (byte[])Pdf.Clone(); changed[15] ^= 1;
            var pending = Path.Combine(root, "pending", staged.Id.ToString("N") + ".upload");
            await File.WriteAllBytesAsync(pending, changed);
            var rejected = await Assert.ThrowsAsync<OperationalFileStoreException>(() => store.Finalize(staged.Id, Pdf.Length, staged.Sha256, default));
            Assert.Equal("file-content-mismatch", rejected.Code);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "ready")));
            await File.WriteAllBytesAsync(pending, Pdf);
            await store.Finalize(staged.Id, Pdf.Length, staged.Sha256, default);
            await File.WriteAllBytesAsync(Path.Combine(root, "ready", staged.Id.ToString("N") + ".bin"), changed);
            rejected = await Assert.ThrowsAsync<OperationalFileStoreException>(() => store.OpenReady(staged.Id, Pdf.Length, staged.Sha256, default));
            Assert.Equal("file-content-mismatch", rejected.Code);
            await Assert.ThrowsAsync<OperationalFileStoreException>(() => store.Finalize(staged.Id, Pdf.Length, staged.Sha256, default));
        });
    }

    [Fact]
    public async Task ReparseDirectoriesAreRejectedAtConfigurationAndBeforeWriting()
    {
        await WithRoot(async root =>
        {
            var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
            var marker = Path.Combine(outside, "retained.txt"); await File.WriteAllTextAsync(marker, "unchanged");
            var privateRoot = Path.Combine(root, "private"); var store = new OperationalFileStore(privateRoot, []);
            var link = Path.Combine(privateRoot, "pending"); Directory.Delete(link);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                    start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
                    start.ArgumentList.Add($"$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '{link.Replace("'", "''")}' -Target '{outside.Replace("'", "''")}' | Out-Null");
                    using var process = System.Diagnostics.Process.Start(start)!;
                    var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync(); await output;
                    Assert.True(process.ExitCode == 0, await error);
                }
                else Directory.CreateSymbolicLink(link, outside);
                Assert.Throws<OperationalFileStoreException>(() => new OperationalFileStore(link, []));
                var rejected = await Assert.ThrowsAsync<OperationalFileStoreException>(() => store.Stage("escape.pdf", "application/pdf", new MemoryStream(Pdf), 1000, default));
                Assert.Equal("file-path-invalid", rejected.Code);
                Assert.Equal("unchanged", await File.ReadAllTextAsync(marker)); Assert.Single(Directory.GetFiles(outside));
            }
            finally
            {
                // Delete only the generated link itself, never recurse through
                // a reparse point during fixture cleanup.
                if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link, recursive: false);
            }
        });
    }

    [Fact]
    public async Task CleanupOnlyDeletesExpiredUnreferencedTemporaryObjects()
    {
        await WithRoot(async root =>
        {
            var store = new OperationalFileStore(root, []);
            var staged = await store.Stage("fictional.pdf", "application/pdf", new MemoryStream(Pdf), 1000, default);
            Assert.False(await store.DeleteExpiredTemporary(staged.Id, DateTimeOffset.MaxValue, _ => Task.FromResult(true), default));
            Assert.False(await store.DeleteExpiredTemporary(staged.Id, DateTimeOffset.MinValue, _ => Task.FromResult(false), default));
            Assert.True(await store.DeleteExpiredTemporary(staged.Id, DateTimeOffset.MaxValue, _ => Task.FromResult(false), default));
            var ready = await store.Stage("ready.pdf", "application/pdf", new MemoryStream(Pdf), 1000, default);
            await store.Finalize(ready.Id, ready.Description.ByteLength, ready.Sha256, default);
            Assert.False(await store.DeleteExpiredTemporary(ready.Id, DateTimeOffset.MaxValue, _ => Task.FromResult(false), default));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "ready")));
            Assert.Throws<ArgumentException>(() => new OperationalFileStore(root, [root]));
            Assert.Throws<ArgumentException>(() => new OperationalFileStore("relative-file-root", []));
        });
    }

    private sealed class ChunkedStream(byte[] bytes, int chunkSize) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), bytes.Length - BytesRead);
            bytes.AsMemory(BytesRead, count).CopyTo(buffer); BytesRead += count;
            return ValueTask.FromResult(count);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task WithRoot(Func<string, Task> test)
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CoverMGA_FileTests"));
        var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        try { await test(root); }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(resolved).Length != 32)
                throw new InvalidOperationException("File fixture cleanup target changed.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }
}
