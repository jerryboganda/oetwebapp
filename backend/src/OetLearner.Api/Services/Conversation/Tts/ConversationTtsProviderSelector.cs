using Microsoft.Extensions.Logging;

namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProviderSelector
{
    Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default);
    Task<bool> IsTtsDisabledAsync(CancellationToken ct = default);
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

        if (requested is "azure" or "elevenlabs" or "cosyvoice" or "chattts" or "gptsovits" or "mock")
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

        foreach (var candidate in new[] { "azure", "elevenlabs", "cosyvoice", "chattts", "gptsovits" })
        {
            var p = Find(candidate);
            if (p is { IsConfigured: true }) return p;
        }
        return Find("mock");
    }
}
