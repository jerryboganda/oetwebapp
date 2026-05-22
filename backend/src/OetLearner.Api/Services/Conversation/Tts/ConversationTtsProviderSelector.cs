using Microsoft.Extensions.Logging;

namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProviderSelector
{
    Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default);
    Task<bool> IsTtsDisabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolve a specific provider by name without consulting the admin
    /// TtsProvider preference. Used by the Voice Studio probe/preview
    /// endpoints so admins can audition the Qwen3 provider even when the
    /// active TtsProvider is something else. Returns the provider only if
    /// it is registered AND configured.
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
        if (string.Equals(options.TtsProvider, "off", StringComparison.OrdinalIgnoreCase)) return null;
        var requested = (options.TtsProvider ?? "auto").Trim().ToLowerInvariant();
        var all = providers.ToList();
        IConversationTtsProvider? Find(string name) =>
            all.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (requested is "azure" or "digitalocean-qwen3-tts" or "elevenlabs" or "cosyvoice" or "chattts" or "gptsovits" or "mock")
        {
            var p = Find(requested);
            if (p is null) { logger.LogWarning("TTS '{Name}' not registered.", requested); return Find("mock"); }
            if (!p.IsConfigured && requested != "mock")
            {
                logger.LogWarning("TTS '{Name}' not configured, falling back to mock.", requested);
                return Find("mock");
            }
            return p;
        }

        foreach (var candidate in new[] { "azure", "digitalocean-qwen3-tts", "cosyvoice", "chattts", "gptsovits", "elevenlabs" })
        {
            var p = Find(candidate);
            if (p is { IsConfigured: true }) return p;
        }
        return Find("mock");
    }

    public Task<IConversationTtsProvider?> TrySelectAsync(string providerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerName)) return Task.FromResult<IConversationTtsProvider?>(null);
        var p = providers.FirstOrDefault(x => string.Equals(x.Name, providerName, StringComparison.OrdinalIgnoreCase));
        if (p is null) return Task.FromResult<IConversationTtsProvider?>(null);
        if (!p.IsConfigured && !string.Equals(p.Name, "mock", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IConversationTtsProvider?>(null);
        return Task.FromResult<IConversationTtsProvider?>(p);
    }
}
