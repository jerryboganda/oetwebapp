using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Providers;

public sealed class AzureOpenAiProvider : ILlmProvider
{
    public string ProviderKindKey => "AzureOpenAi";

    public AzureOpenAiProvider()
    {
        // TODO Phase 1: inject IHttpClientFactory.
        // TODO: via IRuntimeSettingsProvider (endpoint + key + deployment map)
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 1: deployment list from config.");

    public async IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
        LlmChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
        throw new NotImplementedException("TODO Phase 1: Azure OpenAI streaming.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
