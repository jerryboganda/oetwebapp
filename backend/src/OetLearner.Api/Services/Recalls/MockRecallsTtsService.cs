using System.Security.Cryptography;
using System.Text;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Deterministic mock TTS provider used in development, automated tests, and
/// as the default when no real provider is configured. Produces a tiny but
/// well-formed WAV silence buffer keyed by SHA-256 of the input so the same
/// text always returns the same URL — exactly matching the production
/// content-addressed pipeline.
/// </summary>
public sealed class MockRecallsTtsService : IRecallsTtsService
{
    public Task<RecallsTtsResult> GenerateWordAsync(string text, RecallsTtsOptions options, CancellationToken ct)
        => Task.FromResult(Build(text, options, kind: "word"));

    public Task<RecallsTtsResult> GenerateSentenceAsync(string sentence, RecallsTtsOptions options, CancellationToken ct)
        => Task.FromResult(Build(sentence, options, kind: "sentence"));

    private static RecallsTtsResult Build(string text, RecallsTtsOptions options, string kind)
    {
        var key = $"{options.Locale}|{options.Speed}|{options.Voice}|{kind}|{text}";
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        var url = $"/media/recalls/tts/{sha}.wav";
        return new RecallsTtsResult(url, Provider: "mock", Bytes: 44, ContentType: "audio/wav", Sha256: sha);
    }
}
