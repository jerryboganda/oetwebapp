using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Conversation.Tts;

public interface IConversationTtsProviderSelector
{
    IConversationTtsProvider? TrySelect();
    bool TtsDisabled { get; }
}

public sealed class ConversationTtsProviderSelector(
    IEnumerable<IConversationTtsProvider> providers,
    IOptions<ConversationOptions> options,
    ILogger<ConversationTtsProviderSelector> logger) : IConversationTtsProviderSelector
{
    private readonly ConversationOptions _options = options.Value;

    public bool TtsDisabled => string.Equals(_options.TtsProvider, "off", StringComparison.OrdinalIgnoreCase);

    public IConversationTtsProvider? TrySelect()
    {
        if (TtsDisabled) return null;
        var requested = (_options.TtsProvider ?? "auto").Trim().ToLowerInvariant();
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
