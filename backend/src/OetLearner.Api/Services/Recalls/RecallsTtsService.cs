using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Conversation.Tts;

namespace OetLearner.Api.Services.Recalls;

public sealed class RecallsTtsService(
    IConversationTtsProviderSelector ttsSelector,
    IFileStorage storage,
    IWebHostEnvironment environment,
    ILogger<RecallsTtsService> logger) : IRecallsTtsService
{
    private const string Root = "recalls/tts";

    public Task<RecallsTtsResult> GenerateWordAsync(string text, RecallsTtsOptions options, CancellationToken ct)
        => GenerateAsync(text, options, "word", ct);

    public Task<RecallsTtsResult> GenerateSentenceAsync(string sentence, RecallsTtsOptions options, CancellationToken ct)
        => GenerateAsync(sentence, options, "sentence", ct);

    private async Task<RecallsTtsResult> GenerateAsync(string text, RecallsTtsOptions options, string kind, CancellationToken ct)
    {
        var provider = await ttsSelector.TrySelectAsync(ct);
        if (provider is not null && !string.Equals(provider.Name, "mock", StringComparison.OrdinalIgnoreCase))
        {
            var result = await provider.SynthesizeAsync(
                new ConversationTtsRequest(
                    Text: text,
                    Voice: ResolveVoice(options, provider.Name),
                    Locale: string.IsNullOrWhiteSpace(options.Locale) ? "en-GB" : options.Locale,
                    Rate: ResolveRate(options.Speed)),
                ct);

            return await PersistAsync(result.Audio, result.MimeType, provider.Name, text, options, kind, ct);
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Recalls TTS requires a configured real TTS provider in Production. Configure Conversation:TtsProvider with Azure, ElevenLabs, CosyVoice, ChatTTS, or GPT-SoVITS credentials.");
        }

        logger.LogWarning("Recalls TTS is using stored mock audio because no real TTS provider is configured.");
        var mockAudio = BuildSilentWav();
        return await PersistAsync(mockAudio, "audio/wav", "mock", text, options, kind, ct);
    }

    private async Task<RecallsTtsResult> PersistAsync(
        byte[] audio,
        string contentType,
        string provider,
        string text,
        RecallsTtsOptions options,
        string kind,
        CancellationToken ct)
    {
        var keyMaterial = $"{provider}|{contentType}|{options.Locale}|{options.Speed}|{ResolveVoice(options, provider)}|{kind}|{text}";
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial))).ToLowerInvariant();
        var extension = ExtensionFor(contentType);
        var key = $"{Root}/{sha[..2]}/{sha.Substring(2, 2)}/{sha}.{extension}";
        if (!storage.Exists(key))
        {
            await using var stream = new MemoryStream(audio, writable: false);
            await storage.WriteAsync(key, stream, ct);
        }

        return new RecallsTtsResult(
            Url: key,
            Provider: provider,
            Bytes: audio.Length,
            ContentType: contentType,
            Sha256: sha);
    }

    private static string ResolveVoice(RecallsTtsOptions options, string provider)
    {
        if (!string.IsNullOrWhiteSpace(options.Voice) && options.Voice != "default") return options.Voice;
        return string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase) ? "en-GB-SoniaNeural" : string.Empty;
    }

    private static double ResolveRate(string? speed) => (speed ?? "normal").ToLowerInvariant() switch
    {
        "slow" => 0.82,
        "very_slow" => 0.68,
        _ => 1.0,
    };

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "audio/mpeg" or "audio/mp3" => "mp3",
        "audio/webm" => "webm",
        "audio/ogg" => "ogg",
        "audio/mp4" => "m4a",
        "audio/wav" or "audio/x-wav" => "wav",
        _ => "bin",
    };

    private static byte[] BuildSilentWav()
    {
        const int sampleRate = 16000;
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleCount = sampleRate / 4;
        var dataSize = sampleCount * channels * (bitsPerSample / 8);
        using var stream = new MemoryStream(44 + dataSize);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();

        return stream.ToArray();
    }
}