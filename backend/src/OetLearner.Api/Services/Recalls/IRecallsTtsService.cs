namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Provider-agnostic interface for British-English text-to-speech generation.
/// See <c>docs/RECALLS-MODULE-PLAN.md</c> §7. Implementations must produce
/// content-addressed audio via <c>IFileStorage</c>; never write raw bytes
/// to the filesystem from callers.
/// </summary>
public interface IRecallsTtsService
{
    /// <summary>Generate audio for a single word/term.</summary>
    Task<RecallsTtsResult> GenerateWordAsync(string text, RecallsTtsOptions options, CancellationToken ct);

    /// <summary>Generate audio for an example sentence (slower, full prosody).</summary>
    Task<RecallsTtsResult> GenerateSentenceAsync(string sentence, RecallsTtsOptions options, CancellationToken ct);
}

public record RecallsTtsOptions(
    string Locale = "en-GB",
    string Speed = "normal",     // normal | slow | very_slow
    string Voice = "default",
    bool ForceRegenerate = false);

public record RecallsTtsResult(
    string Url,
    string Provider,
    int Bytes,
    string ContentType,
    string Sha256);
