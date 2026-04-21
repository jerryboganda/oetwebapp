using Microsoft.Extensions.Logging;

namespace OetLearner.Api.Services.Conversation.Asr;

public interface IConversationAsrProviderSelector
{
    Task<IConversationAsrProvider> SelectAsync(CancellationToken ct = default);
}

public sealed class ConversationAsrProviderSelector(
    IEnumerable<IConversationAsrProvider> providers,
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
}
