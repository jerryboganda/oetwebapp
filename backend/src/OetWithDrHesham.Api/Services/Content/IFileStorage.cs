using System.Security.Cryptography;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;

namespace OetWithDrHesham.Api.Services.Content;

public sealed record FileStorageReadResult(Stream Stream, long Length);

// ═════════════════════════════════════════════════════════════════════════════
// IFileStorage — thin abstraction in front of disk storage. Slice 2.
//
// Wraps the concrete file operations so Slice-8+ can swap to S3 / R2 by
// implementing this interface. All upload / download / delete paths through
// the app should depend on this, never on raw File.* or Path.* directly.
//
// Path model is "keys" — opaque, POSIX-style, rooted under LocalRootPath.
// Implementations must guard against path traversal.
// ═════════════════════════════════════════════════════════════════════════════

public interface IFileStorage
{
    Task<long> WriteAsync(string key, Stream source, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);

    /// <summary>
    /// Opens a readable stream and returns metadata obtained by the same storage
    /// operation. Providers on remote hot paths must override this method.
    /// </summary>
    async Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
    {
        var stream = await OpenReadAsync(key, ct);
        try
        {
            if (!stream.CanSeek)
            {
                throw new NotSupportedException(
                    "This storage provider must implement OpenReadWithMetadataAsync for non-seekable streams.");
            }

            return new FileStorageReadResult(stream, stream.Length);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    /// <summary>Open a write stream. Caller is responsible for disposal.
    /// Concrete implementations ensure parent directories exist.</summary>
    Task<Stream> OpenWriteAsync(string key, CancellationToken ct);

    Task<bool> ExistsAsync(string key, CancellationToken ct);

    /// <summary>
    /// Asynchronously enumerate storage keys under the given prefix (recursive),
    /// in POSIX-key form. Used by maintenance sweeps (e.g. orphaned-audio
    /// cleanup). Default is empty so test doubles that do not support listing
    /// need not implement it; the real LocalFileStorage / S3 providers override.
    /// </summary>
    async IAsyncEnumerable<string> ListKeysAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        yield break;
    }

    Task<bool> DeleteAsync(string key, CancellationToken ct);
    Task<long> LengthAsync(string key, CancellationToken ct);
    Task MoveAsync(string sourceKey, string destKey, bool overwrite, CancellationToken ct);
    Task<int> DeletePrefixAsync(string prefix, CancellationToken ct);
    string? TryResolveLocalPath(string key);

    /// <summary>
    /// Wave 4 — resolve a read URL for the given storage key. Local storage
    /// returns a relative URL routed through <c>/media/file/{key}</c>; S3 /
    /// object-store providers return a presigned GET URL valid for <paramref name="ttl"/>.
    /// Returns <c>null</c> when the key is missing or the provider can't
    /// emit a URL (e.g. unconfigured).
    /// </summary>
    Uri? ResolveReadUrl(string key, TimeSpan ttl);
}

public sealed class LocalFileStorage(IWebHostEnvironment environment, IOptions<StorageOptions> options) : IFileStorage
{
    private readonly StorageOptions _options = options.Value;

    public async Task<long> WriteAsync(string key, Stream source, CancellationToken ct)
    {
        var fullPath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var dest = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return total;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(OpenLocalReadStream(key));
    }

    public Task<FileStorageReadResult> OpenReadWithMetadataAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var stream = OpenLocalReadStream(key);
        return Task.FromResult(new FileStorageReadResult(stream, stream.Length));
    }

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Stream s = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);
        return Task.FromResult(s);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        var fullPath = ResolvePath(key);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(fullPath));
    }

    public async IAsyncEnumerable<string> ListKeysAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = ResolvePath(prefix);
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(dir)) yield break;

        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(_options.LocalRootPath)
                ? _options.LocalRootPath
                : Path.Combine(environment.ContentRootPath, _options.LocalRootPath));

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            yield return Path.GetRelativePath(rootPath, file).Replace('\\', '/');
        }
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct)
    {
        var fullPath = ResolvePath(key);
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(fullPath)) return Task.FromResult(false);
        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    public Task<long> LengthAsync(string key, CancellationToken ct)
    {
        var fullPath = ResolvePath(key);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new FileInfo(fullPath).Length);
    }

    public Task MoveAsync(
        string sourceKey,
        string destKey,
        bool overwrite,
        CancellationToken ct)
    {
        var src = ResolvePath(sourceKey);
        var dst = ResolvePath(destKey);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (File.Exists(dst))
        {
            if (!overwrite) return Task.CompletedTask;
            File.Delete(dst);
        }
        File.Move(src, dst);
        return Task.CompletedTask;
    }

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken ct)
    {
        var fullPath = ResolvePath(prefix);
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(fullPath)) return Task.FromResult(0);
        var fileCount = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories).Length;
        ct.ThrowIfCancellationRequested();
        Directory.Delete(fullPath, recursive: true);
        return Task.FromResult(fileCount);
    }

    public string? TryResolveLocalPath(string key) => ResolvePath(key);

    public Uri? ResolveReadUrl(string key, TimeSpan ttl)
    {
        // LocalFileStorage delegates URL serving to the API process — the
        // route `/media/file/{key}` streams via MediaEndpoints with the same
        // role-based access check as for download. TTL is ignored locally
        // because the auth check runs on every request.
        if (string.IsNullOrWhiteSpace(key)) return null;
        return new Uri($"/media/file/{Uri.EscapeDataString(key)}", UriKind.Relative);
    }

    private FileStream OpenLocalReadStream(string key)
    {
        var fullPath = ResolvePath(key);
        try
        {
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, useAsync: true);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new FileNotFoundException($"Storage object '{key}' was not found.", key, ex);
        }
    }

    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Storage key is required.");

        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(_options.LocalRootPath)
                ? _options.LocalRootPath
                : Path.Combine(environment.ContentRootPath, _options.LocalRootPath));

        var segments = key.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment == "." || segment == ".." || segment.Contains(':', StringComparison.Ordinal)))
            throw new InvalidOperationException("Storage key contains an invalid path segment.");

        var normalized = Path.Combine(segments);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalized));
        var relative = Path.GetRelativePath(rootPath, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Storage key resolved outside the configured storage root.");
        return fullPath;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Content-addressed path helpers
// ═════════════════════════════════════════════════════════════════════════════

public static class ContentAddressed
{
    /// <summary>Given sha256 hex and extension, return the published key
    /// under the shard layout <c>{sha[0..2]}/{sha[2..4]}/{sha}.{ext}</c>.</summary>
    public static string PublishedKey(string publishedRoot, string sha256, string extension)
    {
        if (sha256.Length < 4) throw new ArgumentException("SHA-256 hex required.", nameof(sha256));
        var ext = extension.TrimStart('.').ToLowerInvariant();
        var seg1 = sha256[..2];
        var seg2 = sha256.Substring(2, 2);
        return $"{publishedRoot}/{seg1}/{seg2}/{sha256}.{ext}";
    }

    public static string StagingPartKey(string stagingRoot, string adminId, string sessionId, int partNumber)
        => $"{stagingRoot}/{adminId}/{sessionId}/{partNumber:D5}.bin";

    public static string StagingSessionPrefix(string stagingRoot, string adminId, string sessionId)
        => $"{stagingRoot}/{adminId}/{sessionId}";
}

/// <summary>
/// Streaming SHA-256 helper. Accepts an ordered sequence of streams (the
/// parts of a chunked upload) and returns total-bytes + hex hash without
/// buffering the whole file in memory.
/// </summary>
public static class StreamingSha256
{
    public static async Task<(long bytes, string sha256)> ComputeAsync(
        IEnumerable<Stream> orderedParts,
        Stream? writeTo,
        CancellationToken ct)
    {
        using var hasher = SHA256.Create();
        var buffer = new byte[81920];
        long total = 0;
        foreach (var part in orderedParts)
        {
            while (true)
            {
                var read = await part.ReadAsync(buffer, ct);
                if (read == 0) break;
                hasher.TransformBlock(buffer, 0, read, null, 0);
                if (writeTo is not null)
                    await writeTo.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }
        }
        hasher.TransformFinalBlock([], 0, 0);
        var sha = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
        return (total, sha);
    }
}
