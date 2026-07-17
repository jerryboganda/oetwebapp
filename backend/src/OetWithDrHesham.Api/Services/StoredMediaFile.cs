using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Services.Content;

namespace OetWithDrHesham.Api.Services;

public sealed record StoredMediaFile(Stream Stream, string ContentType, long Length);

public static class MediaStoragePolicy
{
    private static readonly HashSet<string> AllowedMediaContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf",
        "audio/mpeg", "audio/mp4", "audio/wav", "audio/ogg", "audio/webm",
        // Video Library caption tracks (WebVTT canonical; SRT accepted on upload).
        "text/vtt", "application/x-subrip"
    };

    private static readonly HashSet<string> AllowedMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf",
        ".mp3", ".m4a", ".mp4", ".wav", ".ogg", ".webm",
        // Video Library caption tracks.
        ".vtt", ".srt"
    };

    public static bool IsAllowedMediaContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var normalized = NormalizeContentType(contentType);
        return AllowedMediaContentTypes.Contains(normalized);
    }

    public static bool IsAllowedMediaExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        return AllowedMediaExtensions.Contains(Path.GetExtension(fileName));
    }

    public static bool IsAllowedAudioContentType(string? contentType, StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var normalized = NormalizeContentType(contentType);
        return options.AllowedAudioContentTypes.Any(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string GuessContentTypeFromKey(string storageKey)
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

    public static string NormalizeContentType(string contentType)
        => contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();
}

public static class FileStorageMediaExtensions
{
    public static async Task<StoredMediaFile> OpenStoredMediaFileAsync(
        this IFileStorage storage,
        string storageKey,
        string? fallbackContentType,
        CancellationToken ct)
    {
        var contentType = string.IsNullOrWhiteSpace(fallbackContentType)
            ? MediaStoragePolicy.GuessContentTypeFromKey(storageKey)
            : MediaStoragePolicy.NormalizeContentType(fallbackContentType);

        try
        {
            var result = await storage.OpenReadWithMetadataAsync(storageKey, ct);
            return new StoredMediaFile(result.Stream, contentType, result.Length);
        }
        catch (FileNotFoundException)
        {
            throw ApiException.NotFound("audio_not_found", "The requested audio file was not found.");
        }
    }
}
