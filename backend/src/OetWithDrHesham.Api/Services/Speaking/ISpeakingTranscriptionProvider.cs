using System.Text.Json.Serialization;

namespace OetWithDrHesham.Api.Services.Speaking;

/// <summary>
/// Provider abstraction over an external ASR (Automatic Speech
/// Recognition) engine used to transcribe Speaking session audio.
///
/// Phase 4 of the OET Speaking module plan (B.2 — ASR transcription
/// pipeline). The Speaking transcription pipeline is the single
/// orchestrator; this interface is the contract every concrete engine
/// (Whisper, Deepgram, Azure Speech, ...) must satisfy.
///
/// Implementations MUST:
/// <list type="bullet">
///   <item>Be deterministic on a given <c>mediaAssetUrl</c> + <c>language</c>
///         within a single billing window so the pipeline can short-circuit
///         duplicate calls.</item>
///   <item>Surface transient failures by throwing exceptions — the
///         pipeline persists a failed status row and is allowed to retry.</item>
///   <item>Return a serialised <c>segmentsJson</c> array that mirrors the
///         schema documented on <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript.SegmentsJson"/>:
///         <c>{ speaker, startMs, endMs, text, confidence, words[] }</c>.</item>
/// </list>
/// </summary>
public interface ISpeakingTranscriptionProvider
{
    /// <summary>Stable provider code persisted onto
    /// <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript.Provider"/>
    /// (e.g. <c>"whisper"</c>, <c>"mock"</c>).</summary>
    string ProviderCode { get; }

    /// <summary>Synchronously transcribe the audio referenced by
    /// <paramref name="mediaAssetUrl"/>. Implementations may resolve the
    /// URL via signed storage, HTTP, or local filesystem; the contract is
    /// transparent to callers.</summary>
    /// <param name="mediaAssetUrl">Storage key, signed URL, or absolute
    /// path the provider can read. Must not be null or whitespace.</param>
    /// <param name="language">ISO-639-1 hint (e.g. <c>"en"</c>). Providers
    /// are free to auto-detect but must echo the resolved code back on the
    /// result so analytics has a stable value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SpeakingTranscriptionProviderResult> TranscribeAsync(
        string mediaAssetUrl,
        string language,
        CancellationToken ct);
}

/// <summary>
/// Engine-agnostic result returned by every
/// <see cref="ISpeakingTranscriptionProvider"/>. Shaped to the columns on
/// <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript"/> so the pipeline
/// can persist without reshaping.
/// </summary>
public sealed record SpeakingTranscriptionProviderResult
{
    /// <summary>Stable provider code (e.g. <c>"whisper"</c>,
    /// <c>"deepgram"</c>, <c>"mock"</c>). Persisted on
    /// <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript.Provider"/>.</summary>
    public required string Provider { get; init; }

    /// <summary>ISO-639-1 code the provider actually used. Persisted on
    /// <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript.Language"/>.</summary>
    public required string Language { get; init; }

    /// <summary>JSON array of segment objects with shape
    /// <c>{ speaker, startMs, endMs, text, confidence, words[] }</c>.
    /// Persisted verbatim into <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript.SegmentsJson"/>.</summary>
    public required string SegmentsJson { get; init; }

    /// <summary>Total word count over the transcript. Persisted on
    /// <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript.WordCount"/>.</summary>
    public required int WordCount { get; init; }

    /// <summary>Mean confidence over the transcript segments, in [0, 1].
    /// Persisted on
    /// <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript.MeanConfidence"/>.</summary>
    public required double MeanConfidence { get; init; }

    /// <summary>Optional model identifier the provider used (e.g.
    /// <c>"whisper-large-v3"</c>). Surfaced through analytics but not
    /// stored as a column.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }
}

/// <summary>Shape returned by
/// <see cref="SpeakingTranscriptionPipeline.GetStatusAsync"/>. Read by
/// learners and admins to poll the state machine.</summary>
public sealed record SpeakingTranscriptionStatus
{
    public required string SpeakingSessionId { get; init; }

    /// <summary><c>idle</c> | <c>queued</c> | <c>processing</c> |
    /// <c>completed</c> | <c>failed</c>.</summary>
    public required string State { get; init; }

    /// <summary>Machine-readable status code (e.g. <c>"pending"</c>,
    /// <c>"completed"</c>, <c>"provider_error"</c>).</summary>
    public required string StatusReasonCode { get; init; }

    public required string StatusMessage { get; init; }

    public bool Retryable { get; init; }

    public int RetryCount { get; init; }

    public DateTimeOffset? QueuedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>FK to the latest <see cref="OetWithDrHesham.Api.Domain.SpeakingTranscript"/>
    /// row, or null until the pipeline has completed at least once.</summary>
    public string? LatestTranscriptId { get; init; }

    public string? Provider { get; init; }

    public string? Language { get; init; }

    public int? WordCount { get; init; }

    public double? MeanConfidence { get; init; }
}
