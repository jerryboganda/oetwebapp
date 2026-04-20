using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Conversation;

/// <summary>
/// Thin helper that persists conversation audio via <see cref="IFileStorage"/>
/// under a deterministic key scheme:
///
///   conversation/audio/{sha[0..2]}/{sha[2..4]}/{sha}.{ext}
///
/// Content-addressed by SHA-256 so identical TTS synthesis caches, and the
/// retention worker can sweep by the audit trail rather than guessing.
/// </summary>
public interface IConversationAudioService
{
    /// <summary>Persist raw audio bytes and return the storage key + URL for playback.</summary>
    Task<ConversationAudioRef> WriteAsync(byte[] audio, string mimeType, CancellationToken ct);
    Task<ConversationAudioRef> WriteAsync(Stream audio, string mimeType, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
    bool Delete(string key);
}

public sealed record ConversationAudioRef(string Key, string Url, string MimeType, long Bytes, string Sha256);

public sealed class ConversationAudioService(
    IFileStorage storage,
    IOptions<ConversationOptions> options,
    ILogger<ConversationAudioService> logger) : IConversationAudioService
{
    private const string Root = "conversation/audio";
    private readonly ConversationOptions _options = options.Value;

    public async Task<ConversationAudioRef> WriteAsync(byte[] audio, string mimeType, CancellationToken ct)
    {
        using var ms = new MemoryStream(audio, writable: false);
        return await WriteAsync(ms, mimeType, ct);
    }

    public async Task<ConversationAudioRef> WriteAsync(Stream audio, string mimeType, CancellationToken ct)
    {
        // Buffer into memory so we can SHA256 then write. Keep bounds so we never
        // exceed the configured audio size cap.
        using var buffer = new MemoryStream();
        var buf = new byte[8192];
        long total = 0;
        while (!ct.IsCancellationRequested)
        {
            var read = await audio.ReadAsync(buf, ct);
            if (read == 0) break;
            total += read;
            if (total > _options.MaxAudioBytes)
                throw new InvalidOperationException($"Audio exceeds MaxAudioBytes ({_options.MaxAudioBytes}).");
            await buffer.WriteAsync(buf.AsMemory(0, read), ct);
        }
        buffer.Position = 0;
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(buffer, ct)).ToLowerInvariant();
        buffer.Position = 0;
        var ext = GuessExtension(mimeType);
        var key = $"{Root}/{sha[..2]}/{sha.Substring(2, 2)}/{sha}.{ext}";
        if (!storage.Exists(key))
        {
            await storage.WriteAsync(key, buffer, ct);
            logger.LogDebug("Persisted conversation audio {Key} ({Bytes} bytes)", key, total);
        }
        return new ConversationAudioRef(
            Key: key,
            Url: $"/v1/conversations/media/{sha}.{ext}",
            MimeType: mimeType,
            Bytes: total,
            Sha256: sha);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        if (!storage.Exists(key)) return Task.FromResult<Stream?>(null);
        return storage.OpenReadAsync(key, ct).ContinueWith(t => (Stream?)t.Result, ct);
    }

    public bool Delete(string key) => storage.Delete(key);

    internal static string GuessExtension(string mime) => mime switch
    {
        "audio/mpeg" => "mp3",
        "audio/mp3" => "mp3",
        "audio/webm" => "webm",
        "audio/ogg" => "ogg",
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/mp4" => "m4a",
        _ => "bin",
    };
}
