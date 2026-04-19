using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Runtime ASR provider selection. Honours <c>Pronunciation:Provider</c>:
///   - "azure"   — use Azure, error 503 if not configured
///   - "whisper" — use Whisper, error 503 if not configured
///   - "mock"    — always use mock (deterministic)
///   - "auto"    — prefer azure, fall back to whisper, fall back to mock
///
/// Thin orchestrator layer so call sites never reason about which provider is
/// live; they just call <see cref="SelectAsync"/> and get something usable.
/// </summary>
public interface IPronunciationAsrProviderSelector
{
    IPronunciationAsrProvider Select();
}

public sealed class PronunciationAsrProviderSelector(
    IEnumerable<IPronunciationAsrProvider> providers,
    IOptions<PronunciationOptions> options,
    ILogger<PronunciationAsrProviderSelector> logger) : IPronunciationAsrProviderSelector
{
    private readonly PronunciationOptions _options = options.Value;

    public IPronunciationAsrProvider Select()
    {
        var requested = (_options.Provider ?? "auto").Trim().ToLowerInvariant();
        var all = providers.ToList();

        IPronunciationAsrProvider? FindByName(string name) =>
            all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (requested == "mock") return FindByName("mock") ?? throw Unconfigured("mock");

        if (requested == "azure")
        {
            var az = FindByName("azure");
            if (az is { IsConfigured: true }) return az;
            throw Unconfigured("azure");
        }

        if (requested == "whisper")
        {
            var w = FindByName("whisper");
            if (w is { IsConfigured: true }) return w;
            throw Unconfigured("whisper");
        }

        // auto
        var azure = FindByName("azure");
        if (azure is { IsConfigured: true })
        {
            logger.LogDebug("Pronunciation ASR: selected azure (auto)");
            return azure;
        }
        var whisper = FindByName("whisper");
        if (whisper is { IsConfigured: true })
        {
            logger.LogDebug("Pronunciation ASR: selected whisper (auto; azure unconfigured)");
            return whisper;
        }
        var mock = FindByName("mock");
        if (mock is not null)
        {
            logger.LogWarning("Pronunciation ASR: falling back to mock provider — no real ASR credentials configured.");
            return mock;
        }
        throw Unconfigured("auto");
    }

    private static InvalidOperationException Unconfigured(string name) =>
        new($"Pronunciation ASR provider '{name}' is not configured. Set Pronunciation:AzureSpeechKey or Pronunciation:WhisperApiKey in configuration.");
}
