using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Conversation;

public interface IConversationAudioService
{
    Task<ConversationAudioRef> WriteAsync(byte[] audio, string mimeType, CancellationToken ct);
    Task<ConversationAudioRef> WriteAsync(Stream audio, string mimeType, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
    bool Delete(string key);
}

public sealed record ConversationAudioRef(string Key, string Url, string MimeType, long Bytes, string Sha256);

public sealed class ConversationAudioService(
    IFileStorage storage,
    IConversationOptionsProvider optionsProvider,
    ILogger<ConversationAudioService> logger) : IConversationAudioService
{
    private const string Root = "conversation/audio";

    public async Task<ConversationAudioRef> WriteAsync(byte[] audio, string mimeType, CancellationToken ct)
    {
        using var ms = new MemoryStream(audio, writable: false);
        return await WriteAsync(ms, mimeType, ct);
    }

    public async Task<ConversationAudioRef> WriteAsync(Stream audio, string mimeType, CancellationToken ct)
    {
        var options = await optionsProvider.GetAsync(ct);
        var ext = GuessExtension(mimeType);
        var tempKey = $"{Root}/_tmp/{Guid.NewGuid():N}.{ext}";

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[81920];
        long total = 0;
        try
        {
            await using (var dest = await storage.OpenWriteAsync(tempKey, ct))
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var read = await audio.ReadAsync(buf, ct);
                    if (read == 0) break;
                    total += read;
                    if (total > options.MaxAudioBytes)
                        throw new InvalidOperationException($"Audio exceeds MaxAudioBytes ({options.MaxAudioBytes}).");
                    hasher.AppendData(buf, 0, read);
                    await dest.WriteAsync(buf.AsMemory(0, read), ct);
                }
            }

            var sha = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            var key = $"{Root}/{sha[..2]}/{sha.Substring(2, 2)}/{sha}.{ext}";
            if (storage.Exists(key))
            {
                storage.Delete(tempKey);
            }
            else
            {
                storage.Move(tempKey, key, overwrite: true);
                logger.LogDebug("Persisted conversation audio {Key} ({Bytes} bytes)", key, total);
            }
            return new ConversationAudioRef(key,
                $"/v1/conversations/media/{sha}.{ext}", mimeType, total, sha);
        }
        catch
        {
            try { storage.Delete(tempKey); } catch { }
            throw;
        }
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        if (!storage.Exists(key)) return null;
        return await storage.OpenReadAsync(key, ct);
    }

    public bool Delete(string key) => storage.Delete(key);

    internal static string GuessExtension(string mime) => mime switch
    {
        "audio/mpeg" or "audio/mp3" => "mp3",
        "audio/webm" => "webm",
        "audio/ogg" => "ogg",
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/mp4" => "m4a",
        _ => "bin",
    };
}
