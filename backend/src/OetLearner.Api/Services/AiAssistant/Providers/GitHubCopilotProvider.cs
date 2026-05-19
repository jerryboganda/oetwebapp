using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Providers;

public sealed class GitHubCopilotProvider : ILlmProvider
{
    public string ProviderKindKey => "GitHubCopilot";

    public GitHubCopilotProvider()
    {
        // TODO Phase 1: inject IHttpClientFactory + token resolver.
        // TODO: via IRuntimeSettingsProvider
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 1: GitHub Copilot model list endpoint.");

    public async IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(
        LlmChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
        throw new NotImplementedException("TODO Phase 1: GitHub Copilot streaming.");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
