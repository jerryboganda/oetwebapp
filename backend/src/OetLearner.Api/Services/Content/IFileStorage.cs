using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Content;

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

    /// <summary>Open a write stream. Caller is responsible for disposal.
    /// Concrete implementations ensure parent directories exist.</summary>
    Task<Stream> OpenWriteAsync(string key, CancellationToken ct);

    bool Exists(string key);
    bool Delete(string key);
    long Length(string key);
    void Move(string sourceKey, string destKey, bool overwrite);
    int DeletePrefix(string prefix);
    string? TryResolveLocalPath(string key);
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
        var fullPath = ResolvePath(key);
        Stream s = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(s);
    }

    public Task<Stream> OpenWriteAsync(string key, CancellationToken ct)
    {
        var fullPath = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Stream s = new FileStream(fullPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);
        return Task.FromResult(s);
    }

    public bool Exists(string key) => File.Exists(ResolvePath(key));

    public bool Delete(string key)
    {
        var fullPath = ResolvePath(key);
        if (!File.Exists(fullPath)) return false;
        File.Delete(fullPath);
        return true;
    }

    public long Length(string key) => new FileInfo(ResolvePath(key)).Length;

    public void Move(string sourceKey, string destKey, bool overwrite)
    {
        var src = ResolvePath(sourceKey);
        var dst = ResolvePath(destKey);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (File.Exists(dst))
        {
            if (!overwrite) return;
            File.Delete(dst);
        }
        File.Move(src, dst);
    }

    public int DeletePrefix(string prefix)
    {
        var fullPath = ResolvePath(prefix);
        if (!Directory.Exists(fullPath)) return 0;
        var fileCount = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories).Length;
        Directory.Delete(fullPath, recursive: true);
        return fileCount;
    }

    public string? TryResolveLocalPath(string key) => ResolvePath(key);

    private string ResolvePath(string key)
    {
        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(_options.LocalRootPath)
                ? _options.LocalRootPath
                : Path.Combine(environment.ContentRootPath, _options.LocalRootPath));
        var normalized = key
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalized));
        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
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
