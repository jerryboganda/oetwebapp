using System;
using System.Collections.Generic;
using System.Linq;

namespace OetLearner.Api.Services.AiAssistant.Providers;

public sealed class LlmProviderRegistry
{
    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;

    public LlmProviderRegistry(IEnumerable<ILlmProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.ProviderKindKey, StringComparer.OrdinalIgnoreCase);
    }

    public ILlmProvider Get(string providerKindKey)
    {
        // TODO Phase 1: throw a typed exception instead of KeyNotFoundException.
        return _providers[providerKindKey];
    }

    public IReadOnlyCollection<string> AvailableKinds => _providers.Keys.ToArray();
}
