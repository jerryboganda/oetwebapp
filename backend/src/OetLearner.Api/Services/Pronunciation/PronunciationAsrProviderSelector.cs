using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Pronunciation;

/// <summary>
/// Runtime ASR provider selection. Honours <c>Pronunciation:Provider</c>:
///   - "azure"   — use Azure, error 503 if not configured
///   - "gemini"  — use Gemini native-audio scoring, error 503 if not configured
///   - "whisper" — use Whisper, error 503 if not configured
///   - "mock"    — use mock in non-production only (deterministic)
///   - "auto"    — prefer azure, then gemini, then whisper; mock fallback is non-production only
///
/// Thin orchestrator layer so call sites never reason about which provider is
/// live; they just call <see cref="SelectAsync"/> and get something usable.
/// </summary>
public interface IPronunciationAsrProviderSelector
{
    Task<IPronunciationAsrProvider> SelectAsync(CancellationToken ct);
}

public sealed class PronunciationAsrProviderSelector(
    IEnumerable<IPronunciationAsrProvider> providers,
    IOptions<PronunciationOptions> options,
    IPronunciationCredentialResolver credentialResolver,
    IWebHostEnvironment environment,
    ILogger<PronunciationAsrProviderSelector> logger) : IPronunciationAsrProviderSelector
{
    private static readonly string[] RegistryProviderCodes =
        ["azure-phoneme", "gemini-pronunciation-audio", "whisper-asr"];

    private readonly PronunciationOptions _options = options.Value;

    public async Task<IPronunciationAsrProvider> SelectAsync(CancellationToken ct)
    {
        await WarmRegistryCredentialsAsync(ct);

        var requested = (_options.Provider ?? "auto").Trim().ToLowerInvariant();
        var all = providers.ToList();

        IPronunciationAsrProvider? FindByName(string name) =>
            all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (requested == "mock")
        {
            if (environment.IsProduction())
                throw new InvalidOperationException("Mock pronunciation ASR provider is forbidden in production.");
            return FindByName("mock") ?? throw Unconfigured("mock");
        }

        if (requested == "azure")
        {
            var az = FindByName("azure");
            if (az is not null && await az.IsConfiguredAsync(ct)) return az;
            throw Unconfigured("azure");
        }

        if (requested == "gemini")
        {
            var g = FindByName("gemini");
            if (g is not null && await g.IsConfiguredAsync(ct)) return g;
            throw Unconfigured("gemini");
        }

        if (requested == "whisper")
        {
            var w = FindByName("whisper");
            if (w is not null && await w.IsConfiguredAsync(ct)) return w;
            throw Unconfigured("whisper");
        }

        if (requested != "auto")
            throw new InvalidOperationException(
                $"Unsupported Pronunciation:Provider value '{_options.Provider}'. Use auto, azure, gemini, whisper, or mock.");

        // auto
        var azure = FindByName("azure");
        if (azure is not null && await azure.IsConfiguredAsync(ct))
        {
            logger.LogDebug("Pronunciation ASR: selected azure (auto)");
            return azure;
        }
        var gemini = FindByName("gemini");
        if (gemini is not null && await gemini.IsConfiguredAsync(ct))
        {
            logger.LogDebug("Pronunciation ASR: selected gemini (auto; azure unconfigured)");
            return gemini;
        }
        var whisper = FindByName("whisper");
        if (whisper is not null && await whisper.IsConfiguredAsync(ct))
        {
            logger.LogDebug("Pronunciation ASR: selected whisper (auto; azure/gemini unconfigured)");
            return whisper;
        }
        var mock = FindByName("mock");
        if (mock is not null && !environment.IsProduction())
        {
            logger.LogWarning("Pronunciation ASR: falling back to mock provider — no real ASR credentials configured.");
            return mock;
        }
        throw Unconfigured("auto");
    }

    private static InvalidOperationException Unconfigured(string name) =>
        new($"Pronunciation ASR provider '{name}' is not configured. Set Azure, Gemini, or Whisper pronunciation credentials in configuration or the AI provider registry.");

    private async Task WarmRegistryCredentialsAsync(CancellationToken ct)
    {
        foreach (var providerCode in RegistryProviderCodes)
        {
            try
            {
                await credentialResolver.ResolveAsync(providerCode, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex,
                    "Pronunciation ASR registry credential warm-up failed for {ProviderCode}; falling back to static options for that provider.",
                    providerCode);
            }
        }
    }
}
