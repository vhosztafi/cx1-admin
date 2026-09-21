using System.Security.Cryptography;
using BackOffice.Application.Operations;

namespace BackOffice.Infrastructure.Operations;

public sealed record StagedOperationalFile(Guid Id, OperationalFileDescription Description, string Sha256);
public enum OperationalFileFault { AfterStageFlush, BeforeRename, AfterRename }
public sealed class OperationalFileStoreException(string code) : IOException("The stored file is unavailable.")
{
    public string Code { get; } = code;
}

public interface IOperationalFileStore
{
    Task<StagedOperationalFile> Stage(string name, string mediaType, Stream source, int maximumBytes, CancellationToken token);
    Task Finalize(Guid id, long length, string sha256, CancellationToken token);
    Task<Stream> OpenReady(Guid id, long length, string sha256, CancellationToken token);
    IReadOnlyList<Guid> ExpiredTemporary(DateTimeOffset olderThan, int maximum);
    Task<bool> DeleteExpiredTemporary(Guid id, DateTimeOffset olderThan, Func<CancellationToken, Task<bool>> isReferenced, CancellationToken token);
}

// Private persistent volume; only generated identifiers become filesystem names.
// The configured directory must be writable only by the application/operator.
public sealed class OperationalFileStore : IOperationalFileStore
{
    private readonly string root;
    private readonly Action<OperationalFileFault, Guid>? fault;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public OperationalFileStore(string root, IReadOnlyList<string> forbiddenRoots, Action<OperationalFileFault, Guid>? fault = null)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Configure an absolute private file volume.", nameof(root));
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); this.fault = fault;
        if (this.root == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(this.root)!) || forbiddenRoots.Any(path => Within(this.root, Path.GetFullPath(path))))
            throw new ArgumentException("File storage must be a dedicated nonstatic directory.", nameof(root));
        CheckAncestors(this.root); Directory.CreateDirectory(this.root); CheckAncestors(this.root);
        foreach (var area in new[] { "pending", "ready" }) { var path = Path.Combine(this.root, area); CheckAncestors(path); Directory.CreateDirectory(path); CheckAncestors(path); }
    }

    public async Task<StagedOperationalFile> Stage(string name, string mediaType, Stream source, int maximumBytes, CancellationToken token)
    {
        FileRules.ValidateNameAndType(name, mediaType);
        if (maximumBytes is < 1 or > FileRules.MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var id = Guid.NewGuid(); var path = ObjectPath("pending", id); var durable = false;
        try
        {
            OperationalFileDescription description; string hash;
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                CheckAncestors(path);
                var buffer = new byte[65536]; var header = new byte[FileRules.HeaderBytes]; var trailer = new byte[FileRules.TrailerBytes];
                var headerCount = 0; var trailerCount = 0; long length = 0;
                while (true)
                {
                    // Read at most one byte beyond the bound, even for an
                    // untrusted non-seekable stream with no Content-Length.
                    var count = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes - length + 1)), token);
                    if (count == 0) break;
                    length += count; if (length > maximumBytes) throw new FileRuleException("file-size-invalid");
                    var head = Math.Min(count, header.Length - headerCount); buffer.AsSpan(0, head).CopyTo(header.AsSpan(headerCount)); headerCount += head;
                    var tail = Math.Min(count, trailer.Length); var retained = Math.Min(trailerCount, trailer.Length - tail);
                    trailer.AsSpan(trailerCount - retained, retained).CopyTo(trailer); buffer.AsSpan(count - tail, tail).CopyTo(trailer.AsSpan(retained)); trailerCount = retained + tail;
                    hasher.AppendData(buffer, 0, count); await target.WriteAsync(buffer.AsMemory(0, count), token);
                }
                description = FileRules.Validate(name, mediaType, length, header.AsSpan(0, headerCount), trailer.AsSpan(0, trailerCount), maximumBytes);
                await target.FlushAsync(token); target.Flush(flushToDisk: true);
                hash = Convert.ToHexStringLower(hasher.GetHashAndReset());
            }
            durable = true;
            // A process crash here leaves only an unreferenced temporary object;
            // SQL cannot claim it is ready. Expired orphan cleanup can remove it.
            fault?.Invoke(OperationalFileFault.AfterStageFlush, id);
            return new(id, description, hash);
        }
        finally
        {
            if (!durable) { CheckAncestors(path); if (File.Exists(path)) File.Delete(path); }
        }
    }

    public async Task Finalize(Guid id, long length, string sha256, CancellationToken token)
    {
        ValidateExpected(id, length, sha256);
        var pending = ObjectPath("pending", id); var ready = ObjectPath("ready", id);
        if (File.Exists(ready)) { await Verify(ready, length, sha256, token); return; }
        try
        {
            // Other processes may rename this same immutable object while it
            // is being verified. Keep the read handle valid across that rename.
            await using (var stream = await OpenVerified(pending, length, sha256, token, allowRename: true)) { }
            fault?.Invoke(OperationalFileFault.BeforeRename, id);
            CheckAncestors(pending); CheckAncestors(ready);
            File.Move(pending, ready, overwrite: false);
        }
        catch (IOException ex) when (File.Exists(ready) && (ex is not OperationalFileStoreException storeError || storeError.Code == "file-bytes-missing"))
        {
            // A competing finalizer may have completed the identical rename.
            // Verify instead of overwriting the immutable destination.
            await Verify(ready, length, sha256, token); return;
        }
        fault?.Invoke(OperationalFileFault.AfterRename, id);
        await Verify(ready, length, sha256, token);
    }

    public Task<Stream> OpenReady(Guid id, long length, string sha256, CancellationToken token)
    {
        ValidateExpected(id, length, sha256); return OpenVerified(ObjectPath("ready", id), length, sha256, token);
    }

    // The SQL owner must hold its file-id lock while checking references. This
    // method cannot remove ready objects or traverse caller-supplied paths.
    public async Task<bool> DeleteExpiredTemporary(Guid id, DateTimeOffset olderThan, Func<CancellationToken, Task<bool>> isReferenced, CancellationToken token)
    {
        var path = ObjectPath("pending", id);
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) >= olderThan.UtcDateTime || await isReferenced(token)) return false;
        token.ThrowIfCancellationRequested(); CheckAncestors(path);
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) >= olderThan.UtcDateTime) return false;
        File.Delete(path); return true;
    }

    public IReadOnlyList<Guid> ExpiredTemporary(DateTimeOffset olderThan, int maximum)
    {
        if (maximum is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximum));
        var directory = Path.Combine(root, "pending"); CheckAncestors(directory);
        var result = new List<Guid>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.upload", SearchOption.TopDirectoryOnly))
        {
            CheckAncestors(path);
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) && id != Guid.Empty && File.GetLastWriteTimeUtc(path) < olderThan.UtcDateTime)
                result.Add(id);
            if (result.Count == maximum) break;
        }
        return result;
    }

    private async Task Verify(string path, long length, string sha256, CancellationToken token)
    {
        await using var stream = await OpenVerified(path, length, sha256, token);
    }

    private static async Task<Stream> OpenVerified(string path, long length, string sha256, CancellationToken token, bool allowRename = false)
    {
        CheckAncestors(path);
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | (allowRename ? FileShare.Delete : FileShare.None), 65536, FileOptions.Asynchronous | FileOptions.SequentialScan); }
        catch (FileNotFoundException) { throw new OperationalFileStoreException("file-bytes-missing"); }
        catch (DirectoryNotFoundException) { throw new OperationalFileStoreException("file-bytes-missing"); }
        try
        {
            CheckAncestors(path);
            if (stream.Length != length) throw new OperationalFileStoreException("file-content-mismatch");
            var actual = await SHA256.HashDataAsync(stream, token);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(sha256))) throw new OperationalFileStoreException("file-content-mismatch");
            stream.Position = 0; return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }

    private string ObjectPath(string area, Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A file identity is required.", nameof(id));
        var path = Path.GetFullPath(Path.Combine(root, area, id.ToString("N") + (area == "pending" ? ".upload" : ".bin")));
        if (!Within(path, root)) throw new OperationalFileStoreException("file-path-invalid");
        CheckAncestors(path); return path;
    }
    private static bool Within(string path, string parent) => string.Equals(path, Path.TrimEndingDirectorySeparator(parent), PathComparison) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, PathComparison);
    private static void CheckAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new OperationalFileStoreException("file-path-invalid"); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static void ValidateExpected(Guid id, long length, string hash)
    {
        if (id == Guid.Empty || length is < 1 or > FileRules.MaximumFileBytes || hash is not { Length: 64 } || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new OperationalFileStoreException("file-metadata-invalid");
    }
}
