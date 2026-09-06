using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OetLearner.Api.Services.AiAssistant;

namespace OetLearner.Api.Hubs;

/// <summary>
/// SignalR hub for the AI Assistant. Supports multi-role access (admin, expert, learner)
/// with role-scoped tool availability and system prompts.
///
/// Client → Server methods:
///   StartTurn(threadId, userMessage) — begins an assistant turn with streaming response
///   CancelTurn(threadId) — cancels a running turn
///   CreateThread(title?) — creates a new thread
///   ListThreads(skip, take) — lists user's threads
///
/// Server → Client events:
///   MessageDelta(threadId, chunk) — streaming text chunk
///   MessageComplete(threadId, messageId, fullContent) — turn complete
///   ToolCallStart(threadId, toolCallId, toolName, args) — tool invocation starting
///   ToolCallResult(threadId, toolCallId, result) — tool result
///   TurnError(threadId, errorCode, errorMessage) — error during turn
///   ThreadCreated(threadId, title) — new thread created
/// </summary>
[Authorize]
public class AiAssistantHub(
    IAiAssistantOrchestrator orchestrator,
    ILogger<AiAssistantHub> logger) : Hub
{
    private static string? GetUserId(HubCallerContext context)
        => context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string GetUserRole(HubCallerContext context)
    {
        var claims = context.User;
        if (claims?.IsInRole("admin") == true || claims?.IsInRole("system_admin") == true)
            return "admin";
        if (claims?.IsInRole("expert") == true)
            return "expert";
        return "learner";
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId))
        {
            Context.Abort();
            return;
        }

        // Add to user-specific group for targeted notifications
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        logger.LogInformation("AI Assistant hub connected: {UserId} ({Role})",
            userId, GetUserRole(Context));
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Creates a new conversation thread for the current user.
    /// </summary>
    public async Task<AiAssistantThreadDto?> CreateThread(string? title)
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId)) return null;

        var role = GetUserRole(Context);
        var thread = await orchestrator.CreateThreadAsync(userId, role, title, Context.ConnectionAborted);

        await Clients.Caller.SendAsync("ThreadCreated", thread.Id, thread.Title);
        return thread;
    }

    /// <summary>
    /// Starts an assistant turn: sends user message, triggers the ReAct loop,
    /// and streams response chunks back via MessageDelta events.
    /// </summary>
    public async Task StartTurn(string threadId, string userMessage)
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId)) return;

        var role = GetUserRole(Context);

        try
        {
            await foreach (var evt in orchestrator.RunTurnAsync(
                threadId, userId, role, userMessage, Context.ConnectionAborted))
            {
                switch (evt)
                {
                    case AssistantTextDelta delta:
                        await Clients.Caller.SendAsync("MessageDelta", threadId, delta.Chunk,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantToolCallStart toolStart:
                        await Clients.Caller.SendAsync("ToolCallStart", threadId,
                            toolStart.ToolCallId, toolStart.ToolName, toolStart.ArgsJson,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantToolCallResult toolResult:
                        await Clients.Caller.SendAsync("ToolCallResult", threadId,
                            toolResult.ToolCallId, toolResult.ResultJson, toolResult.IsError,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantCitationsResolved citations:
                        await Clients.Caller.SendAsync("Citations", threadId,
                            citations.Citations,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantTurnComplete complete:
                        await Clients.Caller.SendAsync("MessageComplete", threadId,
                            complete.MessageId, complete.FullContent,
                            cancellationToken: Context.ConnectionAborted);
                        break;

                    case AssistantTurnError error:
                        await Clients.Caller.SendAsync("TurnError", threadId,
                            error.ErrorCode, error.ErrorMessage,
                            cancellationToken: Context.ConnectionAborted);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or cancelled — normal flow
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI Assistant turn failed for user {UserId} thread {ThreadId}", userId, threadId);
            await Clients.Caller.SendAsync("TurnError", threadId, "INTERNAL_ERROR",
                "An unexpected error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Cancels a running turn for the given thread.
    /// </summary>
    public async Task CancelTurn(string threadId)
    {
        var userId = GetUserId(Context);
        if (string.IsNullOrWhiteSpace(userId)) return;

        await orchestrator.CancelTurnAsync(threadId, userId, Context.ConnectionAborted);
        await Clients.Caller.SendAsync("TurnCancelled", threadId);
    }
}

// --- DTOs for hub communication ---

public sealed record AiAssistantThreadDto(string Id, string Title, string Role, DateTimeOffset CreatedAt);

// --- Streaming event types ---

public abstract record AssistantStreamEvent;
public sealed record AssistantTextDelta(string Chunk) : AssistantStreamEvent;
public sealed record AssistantToolCallStart(string ToolCallId, string ToolName, string ArgsJson) : AssistantStreamEvent;
public sealed record AssistantToolCallResult(string ToolCallId, string ResultJson, bool IsError) : AssistantStreamEvent;
public sealed record AssistantTurnComplete(string MessageId, string FullContent) : AssistantStreamEvent;
public sealed record AssistantTurnError(string ErrorCode, string ErrorMessage) : AssistantStreamEvent;

/// <summary>
/// The approved sources an AI Learning Companion answer is grounded in, emitted
/// before the first token. Only the companion path produces these; the admin and
/// expert assistants have no retrieval step, so their turns never send this
/// event and clients that ignore it are unaffected.
/// </summary>
public sealed record AssistantCitationsResolved(IReadOnlyList<AssistantCitation> Citations) : AssistantStreamEvent;

/// <summary>
/// One cited source. <paramref name="Ordinal"/> matches the <c>[S#]</c> label the
/// prompt gave the model, so a claim in the answer can be traced to a source.
/// Carries no source text — a citation names where an answer came from; it is not
/// a second channel for delivering paid content.
/// </summary>
public sealed record AssistantCitation(
    int Ordinal,
    string SourceKey,
    string SourceTitle,
    string Authority,
    string? Heading,
    int? PageNumber,
    int? TimestampSeconds);
