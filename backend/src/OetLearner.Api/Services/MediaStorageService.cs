using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services;

public sealed record StoredMediaFile(Stream Stream, string ContentType, long Length);

public sealed class MediaStorageService(IWebHostEnvironment environment, IOptions<StorageOptions> options)
{
    private readonly StorageOptions _options = options.Value;

    public bool IsAllowedAudioContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var normalized = NormalizeContentType(contentType);
        return _options.AllowedAudioContentTypes.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<long> SaveAsync(string storageKey, Stream source, CancellationToken cancellationToken)
    {
        var fullPath = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var destination = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var maxBytes = _options.MaxUploadBytes > 0 ? _options.MaxUploadBytes : 25L * 1024 * 1024;
        var buffer = new byte[81920];
        long totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maxBytes)
            {
                throw ApiException.Validation(
                    "audio_file_too_large",
                    $"Audio uploads must be {maxBytes / (1024 * 1024)} MB or smaller.",
                    [new ApiFieldError("audio", "too_large", "Record a shorter file or increase the configured upload limit.")]);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return totalBytes;
    }

    private static readonly HashSet<string> AllowedMediaContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf"
    };

    private static readonly HashSet<string> AllowedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf"
    };

    public static bool IsAllowedMediaContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var normalized = contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();
        return AllowedMediaContentTypes.Contains(normalized);
    }

    public static bool IsAllowedMediaExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return AllowedMediaExtensions.Contains(Path.GetExtension(fileName));
    }

    public bool DeleteFile(string storageKey)
    {
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath)) return false;
        File.Delete(fullPath);
        return true;
    }

    public bool Exists(string storageKey) => File.Exists(ResolvePath(storageKey));

    public long GetLength(string storageKey) => new FileInfo(ResolvePath(storageKey)).Length;

    public StoredMediaFile OpenRead(string storageKey, string? fallbackContentType = null)
    {
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw ApiException.NotFound("audio_not_found", "The requested audio file was not found.");
        }

        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: false);

        var contentType = string.IsNullOrWhiteSpace(fallbackContentType)
            ? GuessContentTypeFromKey(storageKey)
            : NormalizeContentType(fallbackContentType);

        return new StoredMediaFile(stream, contentType, stream.Length);
    }

    private string ResolvePath(string storageKey)
    {
        var rootPath = Path.GetFullPath(
            Path.IsPathRooted(_options.LocalRootPath)
                ? _options.LocalRootPath
                : Path.Combine(environment.ContentRootPath, _options.LocalRootPath));

        var normalizedKey = storageKey
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalizedKey));
        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key resolved outside the configured storage root.");
        }

        return fullPath;
    }

    private static string NormalizeContentType(string contentType)
        => contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();

    private static string GuessContentTypeFromKey(string storageKey)
    {
        return Path.GetExtension(storageKey).ToLowerInvariant() switch
        {
            ".webm" => "audio/webm",
            ".ogg" => "audio/ogg",
            ".mp3" => "audio/mpeg",
            ".mp4" or ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };
    }
}
