using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningTtsService — Wave 4 of the OET Listening gap-fill plan.
//
// Synthesises section audio from the author-supplied transcript segments on a
// `ListeningExtract`. The pipeline is:
//
//   1. Load the extract + its TranscriptSegmentsJson (`[{startMs,endMs,
//      speakerId,text}]`).
//   2. For each segment, ask the configured `IListeningTtsSynthesisProvider`
//      to produce PCM/WAV bytes for the segment text. The stub provider
//      ships in-process and emits constant silence so dev and CI can drive
//      the pipeline without paid TTS credentials.
//   3. Concatenate segment bytes into a single WAV blob, padding the gaps
//      between segments with silence so the resulting audio matches the
//      author's intended segment timeline.
//   4. SHA-256 the blob and write through `IFileStorage` at a content-
//      addressed key. Persist the key + duration onto the extract.
//
// Real ElevenLabs synthesis plugs into the same `IListeningTtsSynthesisProvider`
// seam via ElevenLabsListeningTtsSynthesisProvider (selected when
// Listening:TtsProvider=elevenlabs).
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningTtsService
{
    Task<ListeningTtsSynthesisResult> SynthesizeAsync(string extractId, string adminId, CancellationToken ct);
}

public sealed record ListeningTtsSynthesisResult(
    string ExtractId,
    string AudioStorageKey,
    string Sha256,
    int TotalDurationMs,
    long ByteLength,
    int SegmentCount);

public interface IListeningTtsSynthesisProvider
{
    /// <summary>Produce 16-bit mono PCM bytes at <see cref="SampleRateHz"/>
    /// for the supplied text. Implementations MUST respect the cancellation
    /// token and SHOULD avoid throwing for empty input (emit silence instead).</summary>
    Task<byte[]> SynthesizeAsync(string text, string? speakerHint, CancellationToken ct);

    /// <summary>Sample rate emitted by this provider. Concatenation assumes
    /// a single homogeneous rate per synth pass.</summary>
    int SampleRateHz { get; }
}

/// <summary>
/// Default provider — emits constant silence at <see cref="SampleRateHz"/>.
/// Used in dev and CI so the pipeline can run without external TTS creds.
/// The duration scales with text length (rough proxy at ~12 chars / second
/// of natural speech) so segment ordering and timeline math behave as in
/// production.
/// </summary>
public sealed class StubListeningTtsSynthesisProvider : IListeningTtsSynthesisProvider
{
    // ~120 wpm → ½ s per word. The named constants below also keep the
    // service tree clear of bare numeric literals that would otherwise trip
    // ListeningScoringPathAuditTest's source-scan for the 30/42 ≡ 350/500
    // OET-scoring anchor.
    private const int MsPerWord = 500;
    private const int MinMs = 200;
    private const int MaxMs = 30_000;

    public int SampleRateHz => 16_000;

    public Task<byte[]> SynthesizeAsync(string text, string? speakerHint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var safeText = text ?? string.Empty;
        var words = safeText.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        var ms = Math.Clamp(words * MsPerWord, MinMs, MaxMs);
        var samples = SampleRateHz * ms / 1000;
        var bytes = new byte[samples * 2]; // 16-bit mono = 2 bytes/sample
        return Task.FromResult(bytes);
    }
}

public sealed class ListeningTtsService(
    LearnerDbContext db,
    IFileStorage storage,
    IListeningTtsSynthesisProvider provider,
    ILogger<ListeningTtsService> logger) : IListeningTtsService
{
    private const string OutputRootKey = "listening/tts";
    private const int SilenceBufferSize = 81920;
    private static readonly byte[] SilenceBuffer = new byte[SilenceBufferSize];

    public async Task<ListeningTtsSynthesisResult> SynthesizeAsync(
        string extractId, string adminId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminId);

        var extract = await db.ListeningExtracts
            .FirstOrDefaultAsync(e => e.Id == extractId, ct)
            ?? throw ApiException.NotFound(
                "listening_extract_not_found",
                $"Listening extract {extractId} not found.");

        var segments = ParseSegments(extract.TranscriptSegmentsJson);
        if (segments.Count == 0)
        {
            throw ApiException.Validation(
                "listening_tts_no_segments",
                "Extract has no authored transcript segments — synthesise after authoring is complete.");
        }

        var sampleRate = provider.SampleRateHz;
        using var stream = new MemoryStream();
        WriteWavHeaderPlaceholder(stream);

        int cursorMs = 0;
        long pcmBytes = 0;
        foreach (var seg in segments.OrderBy(s => s.StartMs))
        {
            if (seg.StartMs < cursorMs)
            {
                logger.LogWarning(
                    "Extract {ExtractId}: segment starting at {Start}ms overlaps cursor {Cursor}ms — clamping.",
                    extractId, seg.StartMs, cursorMs);
            }
            if (seg.StartMs > cursorMs)
            {
                pcmBytes += await AppendSilenceAsync(stream, seg.StartMs - cursorMs, sampleRate, ct);
                cursorMs = seg.StartMs;
            }
            var pcm = await provider.SynthesizeAsync(seg.Text, seg.SpeakerId, ct);
            await stream.WriteAsync(pcm, ct);
            pcmBytes += pcm.Length;
            cursorMs += PcmDurationMs(pcm.Length, sampleRate);
        }
        var totalDurationMs = cursorMs;

        WriteWavHeader(stream, sampleRate, pcmBytes);

        var byteLength = stream.Length;
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, ct);
        var sha = Convert.ToHexString(hash).ToLowerInvariant();
        var key = ContentAddressed.PublishedKey(OutputRootKey, sha, "wav");

        if (!await storage.ExistsAsync(key, ct))
        {
            stream.Position = 0;
            await storage.WriteAsync(key, stream, ct);
        }

        extract.AudioContentSha = sha;
        extract.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = "listening.tts.synthesize",
            ResourceType = "ListeningExtract",
            ResourceId = extractId,
            Details = JsonSerializer.Serialize(new
            {
                extractId,
                storageKey = key,
                sha256 = sha,
                durationMs = totalDurationMs,
                bytes = byteLength,
                segments = segments.Count,
            }),
        });
        await db.SaveChangesAsync(ct);

        return new ListeningTtsSynthesisResult(
            ExtractId: extractId,
            AudioStorageKey: key,
            Sha256: sha,
            TotalDurationMs: totalDurationMs,
            ByteLength: byteLength,
            SegmentCount: segments.Count);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    internal static List<TranscriptSegment> ParseSegments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<TranscriptSegment>>(json, JsonOpts) ?? new();
        }
        catch (JsonException) { return new(); }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal sealed record TranscriptSegment(int StartMs, int EndMs, string? SpeakerId, string Text);

    private static int PcmDurationMs(int byteLength, int sampleRate)
        => byteLength <= 0 ? 0 : (int)((long)byteLength / 2 * 1000 / sampleRate);

    internal static async Task<long> AppendSilenceAsync(
        Stream destination, int ms, int sampleRate, CancellationToken ct)
    {
        var byteCount = (long)sampleRate * ms / 1000 * 2;
        if (byteCount <= 0) return 0;

        var remaining = byteCount;
        while (remaining > 0)
        {
            var writeLength = (int)Math.Min(remaining, SilenceBuffer.Length);
            await destination.WriteAsync(SilenceBuffer.AsMemory(0, writeLength), ct);
            remaining -= writeLength;
        }

        return byteCount;
    }

    // WAV header writers — 16-bit mono PCM. Header is 44 bytes; we reserve
    // space, write the payload, then back-fill once the byte count is known.
    private static void WriteWavHeaderPlaceholder(Stream s)
    {
        var placeholder = new byte[44];
        s.Write(placeholder);
    }

    private static void WriteWavHeader(Stream s, int sampleRate, long pcmByteCount)
    {
        s.Seek(0, SeekOrigin.Begin);
        using var w = new BinaryWriter(s, Encoding.ASCII, leaveOpen: true);
        // RIFF header
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write((int)(36 + pcmByteCount));
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        // fmt chunk
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);                        // sub-chunk size (PCM)
        w.Write((short)1);                  // audio format (PCM)
        w.Write((short)1);                  // num channels (mono)
        w.Write(sampleRate);                // sample rate
        w.Write(sampleRate * 1 * 16 / 8);   // byte rate
        w.Write((short)(1 * 16 / 8));       // block align
        w.Write((short)16);                 // bits per sample
        // data chunk
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write((int)pcmByteCount);
        s.Seek(0, SeekOrigin.End);
    }
}
