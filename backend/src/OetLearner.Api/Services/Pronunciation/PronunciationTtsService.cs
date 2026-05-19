using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Conversation.Tts;

namespace OetLearner.Api.Services.Pronunciation;

// ═════════════════════════════════════════════════════════════════════════════
// PronunciationTtsService — admin-side helper to auto-generate the per-drill
// "model reference" audio that learners mimic in pronunciation practice.
//
// MISSION-CRITICAL constraints (AGENTS.md):
//   • TTS routing MUST go through IConversationTtsProviderSelector. We never
//     instantiate Azure/ElevenLabs/etc. directly here.
//   • Audio I/O MUST go through IFileStorage. We never touch File.* / Path.*.
//   • In Production, falling back to the mock TTS provider for a drill that
//     learners will hear is unacceptable — throw a clear, actionable error.
//   • Audio is content-addressed (SHA-256). MediaAsset rows are deduplicated
//     against the sha so re-generating the same text/voice is free.
// ═════════════════════════════════════════════════════════════════════════════

public sealed record PronunciationTtsResult(
    string MediaAssetId,
    string StorageKey,
    string Sha256,
    int Bytes,
    int DurationMs,
    string ProviderName,
    string MimeType);

public interface IPronunciationTtsService
{
    Task<PronunciationTtsResult> GenerateModelAudioAsync(
        string drillId,
        string text,
        string? voiceOverride,
        CancellationToken ct);
}

public sealed class PronunciationTtsService(
    IConversationTtsProviderSelector ttsSelector,
    IFileStorage storage,
    LearnerDbContext db,
    IWebHostEnvironment environment,
    ILogger<PronunciationTtsService> logger) : IPronunciationTtsService
{
    private const int MaxTextLength = 5000;

    public async Task<PronunciationTtsResult> GenerateModelAudioAsync(
        string drillId,
        string text,
        string? voiceOverride,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(drillId))
            throw new ArgumentException("drillId is required.", nameof(drillId));
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required to generate model audio.", nameof(text));

        var trimmed = text.Trim();
        if (trimmed.Length > MaxTextLength)
            throw new ArgumentException(
                $"Text exceeds maximum length of {MaxTextLength} characters.", nameof(text));

        var drill = await db.PronunciationDrills.FirstOrDefaultAsync(d => d.Id == drillId, ct)
            ?? throw new InvalidOperationException($"Pronunciation drill '{drillId}' not found.");

        var provider = await ttsSelector.TrySelectAsync(ct);
        if (provider is null ||
            string.Equals(provider.Name, "mock", StringComparison.OrdinalIgnoreCase))
        {
            // Learners hear this audio. A mock/silent file is never acceptable
            // as a "model reference", even in dev — refuse uniformly.
            throw new InvalidOperationException(
                "Pronunciation model audio requires a configured real TTS provider " +
                "(Azure, ElevenLabs, CosyVoice, ChatTTS, or GPT-SoVITS). " +
                "Set Conversation:TtsProvider to a configured provider, or paste an " +
                "audio URL manually instead.");
        }

        var voice = string.IsNullOrWhiteSpace(voiceOverride) ? string.Empty : voiceOverride.Trim();
        var request = new ConversationTtsRequest(trimmed, voice, "en-GB");

        ConversationTtsResult result;
        try
        {
            result = await provider.SynthesizeAsync(request, ct);
        }
        catch (ConversationTtsException ex)
        {
            logger.LogWarning(ex,
                "Pronunciation TTS provider '{Provider}' failed for drill {DrillId}: {Code}",
                provider.Name, drillId, ex.Code);
            throw new InvalidOperationException(
                $"TTS provider '{provider.Name}' failed: {ex.Message}", ex);
        }

        if (result.Audio is null || result.Audio.Length == 0)
            throw new InvalidOperationException(
                $"TTS provider '{provider.Name}' returned an empty audio payload.");

        var sha = Convert.ToHexString(SHA256.HashData(result.Audio)).ToLowerInvariant();
        var ext = ExtensionFor(result.MimeType);
        var key = $"pronunciation/model-audio/{sha[..2]}/{sha.Substring(2, 2)}/{sha}.{ext}";

        if (!storage.Exists(key))
        {
            await using var stream = new MemoryStream(result.Audio, writable: false);
            await storage.WriteAsync(key, stream, ct);
        }

        var media = await db.MediaAssets.FirstOrDefaultAsync(m => m.Sha256 == sha, ct);
        if (media is null)
        {
            media = new MediaAsset
            {
                Id = $"med_{sha[..16]}",
                OriginalFilename = $"pronunciation-{drillId}.{ext}",
                MimeType = result.MimeType,
                Format = ext,
                SizeBytes = result.Audio.Length,
                DurationSeconds = result.DurationMs > 0 ? Math.Max(1, result.DurationMs / 1000) : null,
                StoragePath = key,
                Status = MediaAssetStatus.Ready,
                Sha256 = sha,
                MediaKind = "audio",
                UploadedBy = "system",
                UploadedAt = DateTimeOffset.UtcNow,
                ProcessedAt = DateTimeOffset.UtcNow,
            };
            db.MediaAssets.Add(media);
        }

        drill.AudioModelAssetId = media.Id;
        drill.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        if (environment.IsDevelopment())
        {
            logger.LogInformation(
                "Pronunciation TTS: drill={DrillId} provider={Provider} bytes={Bytes} sha={Sha} key={Key}",
                drillId, provider.Name, result.Audio.Length, sha, key);
        }

        return new PronunciationTtsResult(
            media.Id, key, sha, result.Audio.Length, result.DurationMs,
            provider.Name, result.MimeType);
    }

    private static string ExtensionFor(string mimeType)
    {
        var mt = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        return mt switch
        {
            "audio/mpeg" or "audio/mp3" => "mp3",
            "audio/wav" or "audio/x-wav" or "audio/wave" => "wav",
            "audio/ogg" or "audio/ogg; codecs=opus" => "ogg",
            "audio/webm" => "webm",
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" => "m4a",
            _ => throw new InvalidOperationException(
                $"Unsupported TTS audio MIME type '{mimeType}'. " +
                "Expected audio/mpeg, audio/wav, audio/ogg, audio/webm, or audio/mp4."),
        };
    }
}
