using Microsoft.Extensions.Logging;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Asr;

public interface IConversationAsrProviderSelector
{
    Task<IConversationAsrProvider> SelectAsync(CancellationToken ct = default);
    Task<IConversationRealtimeAsrProvider?> TrySelectRealtimeAsync(CancellationToken ct = default);
}

public sealed class ConversationAsrProviderSelector(
    IEnumerable<IConversationAsrProvider> providers,
    IEnumerable<IConversationRealtimeAsrProvider> realtimeProviders,
    IConversationOptionsProvider optionsProvider,
    ILogger<ConversationAsrProviderSelector> logger) : IConversationAsrProviderSelector
{
    public async Task<IConversationAsrProvider> SelectAsync(CancellationToken ct = default)
    {
        var options = await optionsProvider.GetAsync(ct);
        var requested = (options.AsrProvider ?? "auto").Trim().ToLowerInvariant();
        var all = providers.ToList();
        IConversationAsrProvider? Find(string name) =>
            all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (requested is "azure" or "whisper" or "deepgram" or "mock")
        {
            var p = Find(requested);
            if (p is null) throw new InvalidOperationException($"ASR provider '{requested}' not registered.");
            if (!p.IsConfigured && requested != "mock")
                throw new InvalidOperationException($"ASR provider '{requested}' not configured.");
            return p;
        }

        foreach (var candidate in new[] { "azure", "whisper", "deepgram" })
        {
            var p = Find(candidate);
            if (p is { IsConfigured: true })
            {
                logger.LogDebug("Conversation ASR: selected {Provider} (auto)", candidate);
                return p;
            }
        }
        var mock = Find("mock");
        if (mock is not null)
        {
            logger.LogWarning("Conversation ASR: falling back to mock.");
            return mock;
        }
        throw new InvalidOperationException("No ASR providers registered.");
    }

    public async Task<IConversationRealtimeAsrProvider?> TrySelectRealtimeAsync(CancellationToken ct = default)
    {
        var options = await optionsProvider.GetAsync(ct);
        if (!options.RealtimeSttEnabled) return null;

        var requested = (options.RealtimeAsrProvider ?? "mock").Trim().ToLowerInvariant();
        var all = realtimeProviders.ToList();
        IConversationRealtimeAsrProvider? Find(string name) =>
            all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (requested is "mock" or "elevenlabs" or "elevenlabs-stt" or "elevenlabs-scribe")
        {
            var lookup = requested.StartsWith("elevenlabs", StringComparison.Ordinal) ? "elevenlabs-stt" : requested;
            if (lookup != "mock" && !CanUseRealProvider(options, lookup))
            {
                logger.LogWarning("Conversation realtime ASR provider {Provider} is configured but real-provider readiness gates are incomplete.", lookup);
                return null;
            }

            var provider = Find(lookup);
            if (provider is null) return null;
            return provider.IsConfigured ? provider : null;
        }

        foreach (var provider in all.Where(provider => provider.IsConfigured))
        {
            if (!IsMockProvider(provider.Name) && !CanUseRealProvider(options, provider.Name))
            {
                logger.LogWarning("Conversation realtime ASR auto skipped provider {Provider} because real-provider readiness gates are incomplete.", provider.Name);
                continue;
            }

            logger.LogDebug("Conversation realtime ASR: selected {Provider} (auto)", provider.Name);
            return provider;
        }

        return null;
    }

    private static bool IsMockProvider(string providerName)
        => string.Equals(providerName, "mock", StringComparison.OrdinalIgnoreCase);

    private static bool CanUseRealProvider(ConversationOptions options, string providerName)
    {
        if (!options.RealtimeSttAllowRealProvider) return false;
        if (!options.RealtimeSttRealProviderProductionAuthorized) return false;
        if (options.RealtimeSttMonthlyBudgetCapUsd > 0 && options.RealtimeSttEstimatedCostUsdPerMinute <= 0) return false;
        if (!options.RealtimeSttAssumeLearnersAdult) return false;

        var topology = (options.RealtimeSttProviderSessionTopology ?? string.Empty).Trim().ToLowerInvariant();
        if (topology is not ("single-instance" or "single-region-sticky" or "distributed")) return false;
        if (string.IsNullOrWhiteSpace(options.RealtimeSttRegionId)) return false;

        return !string.IsNullOrWhiteSpace(providerName);
    }
}
