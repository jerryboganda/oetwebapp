using Microsoft.Extensions.Logging;

namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProviderSelector
{
    Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default);
    Task<bool> IsTtsDisabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolve a specific provider by name. ElevenLabs is the only supported
    /// TTS provider, so this returns the ElevenLabs provider when
    /// <paramref name="providerName"/> is "elevenlabs" and it is configured;
    /// otherwise it returns null.
    /// </summary>
    Task<IConversationTtsProvider?> TrySelectAsync(string providerName, CancellationToken ct = default);
}

public sealed class ConversationTtsProviderSelector(
    IEnumerable<IConversationTtsProvider> providers,
    IConversationOptionsProvider optionsProvider,
    ILogger<ConversationTtsProviderSelector> logger) : IConversationTtsProviderSelector
{
    public async Task<bool> IsTtsDisabledAsync(CancellationToken ct = default)
    {
        var o = await optionsProvider.GetAsync(ct);
        return string.Equals(o.TtsProvider, "off", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default)
    {
        var options = await optionsProvider.GetAsync(ct);
        // ElevenLabs is the only TTS provider. Any value other than "off"
        // resolves to ElevenLabs; "off" disables TTS entirely.
        if (string.Equals(options.TtsProvider, "off", StringComparison.OrdinalIgnoreCase)) return null;

        var elevenLabs = providers.FirstOrDefault(p => string.Equals(p.Name, "elevenlabs", StringComparison.OrdinalIgnoreCase));
        if (elevenLabs is null)
        {
            logger.LogWarning("ElevenLabs TTS provider is not registered.");
            return null;
        }
        if (!await elevenLabs.IsConfiguredAsync(ct))
        {
            logger.LogWarning("ElevenLabs TTS is not configured (missing API key).");
            return null;
        }
        return elevenLabs;
    }

    public async Task<IConversationTtsProvider?> TrySelectAsync(string providerName, CancellationToken ct = default)
    {
        // Used by the admin Voice Design preview so the provider can be
        // auditioned regardless of the global on/off switch.
        if (!string.Equals(providerName, "elevenlabs", StringComparison.OrdinalIgnoreCase))
            return null;
        var elevenLabs = providers.FirstOrDefault(p => string.Equals(p.Name, "elevenlabs", StringComparison.OrdinalIgnoreCase));
        return elevenLabs is not null && await elevenLabs.IsConfiguredAsync(ct) ? elevenLabs : null;
    }
}
