using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Providers;

public sealed class OpenAiProvider : ILlmProvider
{
    public string ProviderKindKey => "OpenAi";

    public OpenAiProvider()
    {
        // TODO Phase 1: inject IHttpClientFactory.
        // TODO: via IRuntimeSettingsProvider
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 1: GET /v1/models");

    public async IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
        LlmChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
        throw new NotImplementedException("TODO Phase 1: OpenAI SSE streaming.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
