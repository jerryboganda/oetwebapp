using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Security;

namespace OetWithDrHesham.Api.Services.Conversation.Asr;

public interface IConversationAsrProviderSelector
{
    Task<IConversationAsrProvider> SelectAsync(CancellationToken ct = default);
    Task<IConversationRealtimeAsrProvider?> TrySelectRealtimeAsync(CancellationToken ct = default);
}

public sealed class ConversationAsrProviderSelector(
    IEnumerable<IConversationAsrProvider> providers,
    IEnumerable<IConversationRealtimeAsrProvider> realtimeProviders,
    IConversationOptionsProvider optionsProvider,
    ILaunchReadinessService launchReadinessService,
    IWebHostEnvironment environment,
    IConfiguration configuration,
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
            if (requested != "mock" && !await p.IsConfiguredAsync(ct))
                throw new InvalidOperationException($"ASR provider '{requested}' not configured.");
            if (string.Equals(requested, "mock", StringComparison.Ordinal) && IsProductionMockForbidden())
                throw new InvalidOperationException(
                    "Conversation ASR is pinned to 'mock', which is forbidden in production. Configure Whisper/Azure/Deepgram, or set the allow-mock override.");
            return p;
        }

        foreach (var candidate in new[] { "azure", "whisper", "deepgram" })
        {
            var p = Find(candidate);
            if (p is not null && await p.IsConfiguredAsync(ct))
            {
                logger.LogDebug("Conversation ASR: selected {Provider} (auto)", candidate);
                return p;
            }
        }
        var mock = Find("mock");
        if (mock is not null)
        {
            // Fail loud in production instead of silently transcribing with the
            // deterministic mock (which produced canned "candidate" text and made
            // a mis-keyed Whisper look like it was "working"). Dev/test keep the
            // mock fallback so the loop runs without external credentials.
            if (IsProductionMockForbidden())
                throw new InvalidOperationException(
                    "No real Conversation ASR provider (Whisper/Azure/Deepgram) is configured; refusing to fall back to the mock transcriber in production. Configure Speaking Whisper credentials in the admin AI providers panel.");
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
        if (IsProductionMockForbidden() && requested == "mock")
        {
            throw new InvalidOperationException("Production realtime Conversation ASR cannot use the mock provider. Configure a real provider or disable realtime STT.");
        }

        var all = realtimeProviders.ToList();
        IConversationRealtimeAsrProvider? Find(string name) =>
            all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (requested is "mock" or "elevenlabs" or "elevenlabs-stt" or "elevenlabs-scribe")
        {
            var lookup = requested.StartsWith("elevenlabs", StringComparison.Ordinal) ? "elevenlabs-stt" : requested;
            if (lookup != "mock" && !await CanUseRealProviderAsync(options, lookup, ct))
            {
                logger.LogWarning("Conversation realtime ASR provider {Provider} is configured but real-provider readiness gates are incomplete.", lookup);
                return null;
            }

            var provider = Find(lookup);
            if (provider is null) return null;
            return await provider.IsConfiguredAsync(ct) ? provider : null;
        }

        foreach (var provider in all)
        {
            if (!await provider.IsConfiguredAsync(ct)) continue;

            if (!IsMockProvider(provider.Name) && !await CanUseRealProviderAsync(options, provider.Name, ct))
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

    private bool IsProductionMockForbidden()
        => environment.IsProduction()
           && !configuration.GetValue<bool>(ProductionProviderSafetyValidator.AllowMockProvidersKey);

    private async Task<bool> CanUseRealProviderAsync(ConversationOptions options, string providerName, CancellationToken ct)
    {
        if (!options.RealtimeSttAllowRealProvider) return false;
        if (!options.RealtimeSttRealProviderProductionAuthorized) return false;
        if (options.RealtimeSttMonthlyBudgetCapUsd <= 0) return false;
        if (options.RealtimeSttDailyAudioSecondsPerUser <= 0) return false;
        if (options.RealtimeSttEstimatedCostUsdPerMinute <= 0) return false;
        if (!options.RealtimeSttAssumeLearnersAdult) return false;

        var topology = (options.RealtimeSttProviderSessionTopology ?? string.Empty).Trim().ToLowerInvariant();
        if (topology is not ("single-instance" or "single-region-sticky" or "distributed")) return false;
        if (string.IsNullOrWhiteSpace(options.RealtimeSttRegionId)) return false;

        if (string.IsNullOrWhiteSpace(providerName)) return false;

        var readiness = await launchReadinessService.GetSettingsAsync(ct);
        return IsRealtimeLaunchReady(readiness);
    }

    private static bool IsRealtimeLaunchReady(AdminLaunchReadinessSettingsResponse readiness)
        => IsLaunchApproved(readiness.RealtimeLegalApprovalStatus)
           && IsLaunchApproved(readiness.RealtimePrivacyApprovalStatus)
           && IsLaunchApproved(readiness.RealtimeProtectedSmokeStatus)
           && readiness.RealtimeSpendCapApproved
           && readiness.RealtimeTopologyApproved
           && !string.IsNullOrWhiteSpace(readiness.RealtimeEvidenceUrl);

    private static bool IsLaunchApproved(string? value)
        => string.Equals(value?.Trim(), "approved", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value?.Trim(), "complete", StringComparison.OrdinalIgnoreCase);
}
