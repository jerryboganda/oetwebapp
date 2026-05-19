using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Providers;

// Streaming delta yielded by a provider during a chat completion.
public sealed class ChatStreamDelta
{
    public string? TextDelta { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArgsDelta { get; init; }
    public bool IsFinal { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
}

public sealed class LlmChatMessage
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = string.Empty;
}

public sealed class LlmChatRequest
{
    public IReadOnlyList<LlmChatMessage> Messages { get; init; } = new List<LlmChatMessage>();
    public string Model { get; init; } = string.Empty;
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public IReadOnlyList<object>? Tools { get; init; } // TODO Phase 1: typed tool schema
}

public interface ILlmProvider
{
    string ProviderKindKey { get; }

    // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct);

    // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
    IAsyncEnumerable<ChatStreamDelta> StreamChatAsync(LlmChatRequest request, CancellationToken ct);
}
