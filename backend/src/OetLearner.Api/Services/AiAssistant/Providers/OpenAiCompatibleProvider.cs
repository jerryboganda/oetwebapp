using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Providers;

// Generic OpenAI-compatible endpoint (Groq, Together, OpenRouter, Ollama, vLLM, etc.)
public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    public string ProviderKindKey => "OpenAiCompatible";

    public OpenAiCompatibleProvider()
    {
        // TODO Phase 1: inject IHttpClientFactory.
        // TODO: via IRuntimeSettingsProvider (endpoint + key per AiProviderConfig)
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 1: GET {endpoint}/v1/models");

    public async IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
        LlmChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
        throw new NotImplementedException("TODO Phase 1: OpenAI-compatible SSE.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
