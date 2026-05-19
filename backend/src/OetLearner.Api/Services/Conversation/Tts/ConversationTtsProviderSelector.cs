using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProviderSelector
{
    Task<IConversationTtsProvider?> TrySelectAsync(CancellationToken ct = default);
    Task<bool> IsTtsDisabledAsync(CancellationToken ct = default);
}

public sealed class ConversationTtsProviderSelector(
    IEnumerable<IConversationTtsProvider> providers,
    IConversationOptionsProvider optionsProvider,
    ILogger<ConversationTtsProviderSelector> logger,
    IHostEnvironment? environment = null) : IConversationTtsProviderSelector
{
    private readonly bool _allowMockProvider = environment is null || environment.IsDevelopment();

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
            if (requested == "mock" && !_allowMockProvider) throw MockNotAllowed();
            if (p is null)
            {
                logger.LogWarning("TTS '{Name}' not registered.", requested);
                return _allowMockProvider ? Find("mock") : null;
            }
            if (!p.IsConfigured && requested != "mock")
            {
                logger.LogWarning(_allowMockProvider
                    ? "TTS '{Name}' not configured, falling back to mock."
                    : "TTS '{Name}' not configured; mock fallback is disabled outside Development.", requested);
                return _allowMockProvider ? Find("mock") : null;
            }
            return p;
        }

        foreach (var candidate in new[] { "azure", "elevenlabs", "cosyvoice", "chattts", "gptsovits" })
        {
            var p = Find(candidate);
            if (p is { IsConfigured: true }) return p;
        }
        return _allowMockProvider ? Find("mock") : null;
    }

    private static InvalidOperationException MockNotAllowed() =>
        new("Conversation TTS cannot use the mock provider outside the Development environment.");
}
