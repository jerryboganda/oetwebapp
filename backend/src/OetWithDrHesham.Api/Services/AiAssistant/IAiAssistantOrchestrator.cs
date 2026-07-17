using OetWithDrHesham.Api.Hubs;

namespace OetWithDrHesham.Api.Services.AiAssistant;

/// <summary>
/// Core orchestrator interface for the AI Assistant. Implements a ReAct
/// (Reasoning + Acting) loop that:
/// 1. Selects system prompt based on role
/// 2. Builds message history from thread
/// 3. Calls the LLM via IAiGatewayService with tool definitions
/// 4. Executes tool calls via the tool registry
/// 5. Loops until the model produces a final text response or max iterations
///
/// Streams events back to the caller via IAsyncEnumerable.
/// </summary>
public interface IAiAssistantOrchestrator
{
    /// <summary>Creates a new thread for the given user and role.</summary>
    Task<AiAssistantThreadDto> CreateThreadAsync(
        string userId, string role, string? title, CancellationToken ct);

    /// <summary>
    /// Runs a single turn of the assistant: processes the user message through
    /// the ReAct loop and yields streaming events.
    /// </summary>
    IAsyncEnumerable<AssistantStreamEvent> RunTurnAsync(
        string threadId, string userId, string role, string userMessage,
        CancellationToken ct);

    /// <summary>Cancels a running turn for the given thread.</summary>
    Task CancelTurnAsync(string threadId, string userId, CancellationToken ct);

    /// <summary>Gets message history for a thread.</summary>
    Task<List<AiAssistantMessageDto>> GetMessagesAsync(
        string threadId, string userId, int skip, int take, CancellationToken ct);

    /// <summary>Lists threads for a user.</summary>
    Task<List<AiAssistantThreadDto>> ListThreadsAsync(
        string userId, int skip, int take, CancellationToken ct);

    /// <summary>Deletes (archives) a thread.</summary>
    Task<bool> ArchiveThreadAsync(string threadId, string userId, CancellationToken ct);
}

public sealed record AiAssistantMessageDto(
    string Id,
    string Role,
    string? Content,
    string? ToolCallsJson,
    string? ToolCallId,
    string? ToolName,
    string? Model,
    DateTimeOffset CreatedAt);
