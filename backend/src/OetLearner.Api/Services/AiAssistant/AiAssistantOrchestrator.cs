using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Hubs;
using OetLearner.Api.Services.AiAssistant.SystemPrompts;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.AiAssistant;

public sealed class AiAssistantOrchestrator(
    IServiceScopeFactory scopeFactory,
    IAiAssistantGateway gateway,
    IAiToolRegistry toolRegistry,
    IAiToolInvoker toolInvoker,
    ISystemPromptProvider systemPromptProvider,
    ILogger<AiAssistantOrchestrator> logger) : IAiAssistantOrchestrator
{
    private const int MaxReActIterations = 10;
    private const int MaxMessagesInContext = 50;
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTurns = new();

    public async Task<AiAssistantThreadDto> CreateThreadAsync(
        string userId, string role, string? title, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var thread = new AiAssistantThread
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = userId,
            Role = role,
            Title = title ?? "New conversation",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.AiAssistantThreads.Add(thread);
        await db.SaveChangesAsync(ct);

        return new AiAssistantThreadDto(thread.Id, thread.Title ?? "New conversation",
            thread.Role, thread.CreatedAt);
    }

    public async IAsyncEnumerable<AssistantStreamEvent> RunTurnAsync(
        string threadId, string userId, string role, string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeTurns[threadId] = turnCts;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

            // Verify thread ownership
            var thread = await db.AiAssistantThreads
                .FirstOrDefaultAsync(t => t.Id == threadId && t.UserId == userId, turnCts.Token);

            if (thread == null)
            {
                yield return new AssistantTurnError("THREAD_NOT_FOUND", "Thread not found or access denied.");
                yield break;
            }

            // Persist user message
            var userMsg = new AiAssistantMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                ThreadId = threadId,
                Role = "user",
                Content = userMessage,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AiAssistantMessages.Add(userMsg);
            await db.SaveChangesAsync(turnCts.Token);

            // Build message history for context
            var history = await db.AiAssistantMessages
                .Where(m => m.ThreadId == threadId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(MaxMessagesInContext)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(turnCts.Token);

            // Get system prompt for role
            var systemPrompt = systemPromptProvider.GetSystemPrompt(role, userId);

            // Get available tools for role
            var featureCode = GetFeatureCode(role);
            var tools = await toolRegistry.ResolveForFeatureAsync(featureCode, turnCts.Token);

            // ReAct loop
            var fullResponse = new StringBuilder();
            string? finalMessageId = null;

            for (int iteration = 0; iteration < MaxReActIterations; iteration++)
            {
                turnCts.Token.ThrowIfCancellationRequested();

                // Build messages array for the LLM
                var messages = BuildLlmMessages(systemPrompt, history);

                // Call LLM via gateway with streaming
                var toolCalls = new List<LlmToolCall>();
                var responseText = new StringBuilder();

                await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                    featureCode, userId, messages, tools, thread.ModelOverride, turnCts.Token))
                {
                    switch (chunk)
                    {
                        case LlmTextChunk text:
                            responseText.Append(text.Text);
                            fullResponse.Append(text.Text);
                            yield return new AssistantTextDelta(text.Text);
                            break;

                        case LlmToolCallChunk toolCall:
                            toolCalls.Add(new LlmToolCall(toolCall.Id, toolCall.Name, toolCall.Arguments));
                            break;
                    }
                }

                // If no tool calls, we're done — this is the final response
                if (toolCalls.Count == 0)
                {
                    var assistantMsg = new AiAssistantMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        ThreadId = threadId,
                        Role = "assistant",
                        Content = responseText.ToString(),
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.AiAssistantMessages.Add(assistantMsg);
                    finalMessageId = assistantMsg.Id;
                    break;
                }

                // Persist assistant message with tool calls
                var toolCallMsg = new AiAssistantMessage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThreadId = threadId,
                    Role = "assistant",
                    Content = responseText.Length > 0 ? responseText.ToString() : null,
                    ToolCallsJson = JsonSerializer.Serialize(toolCalls),
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.AiAssistantMessages.Add(toolCallMsg);
                history.Add(toolCallMsg);

                // Execute each tool call
                foreach (var toolCall in toolCalls)
                {
                    yield return new AssistantToolCallStart(toolCall.Id, toolCall.Name, toolCall.Arguments);

                    var toolCtx = new AiToolContext(featureCode, userId, null, toolCallMsg.Id, iteration);
                    var argsElement = JsonSerializer.Deserialize<JsonElement>(toolCall.Arguments);

                    var result = await toolInvoker.InvokeAsync(toolCall.Name, argsElement, toolCtx, turnCts.Token);
                    var resultJson = result.ResultJson.HasValue
                        ? result.ResultJson.Value.GetRawText()
                        : JsonSerializer.Serialize(new { error = result.ErrorMessage ?? "Tool execution failed" });

                    var isError = result.Outcome != AiToolOutcome.Success;
                    yield return new AssistantToolCallResult(toolCall.Id, resultJson, isError);

                    // Persist tool result message
                    var toolResultMsg = new AiAssistantMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        ThreadId = threadId,
                        Role = "tool",
                        Content = resultJson,
                        ToolCallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.AiAssistantMessages.Add(toolResultMsg);
                    history.Add(toolResultMsg);
                }
            }

            // Update thread timestamp and auto-title
            thread.UpdatedAt = DateTimeOffset.UtcNow;
            if (thread.Title == "New conversation" && fullResponse.Length > 0)
            {
                thread.Title = fullResponse.ToString()[..Math.Min(80, fullResponse.Length)].Trim();
            }

            await db.SaveChangesAsync(turnCts.Token);

            yield return new AssistantTurnComplete(
                finalMessageId ?? "unknown",
                fullResponse.ToString());
        }
        finally
        {
            _activeTurns.TryRemove(threadId, out _);
        }
    }

    public Task CancelTurnAsync(string threadId, string userId, CancellationToken ct)
    {
        if (_activeTurns.TryRemove(threadId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        return Task.CompletedTask;
    }

    public async Task<List<AiAssistantMessageDto>> GetMessagesAsync(
        string threadId, string userId, int skip, int take, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var thread = await db.AiAssistantThreads
            .FirstOrDefaultAsync(t => t.Id == threadId && t.UserId == userId, ct);
        if (thread == null) return [];

        return await db.AiAssistantMessages
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.CreatedAt)
            .Skip(skip).Take(take)
            .Select(m => new AiAssistantMessageDto(
                m.Id, m.Role, m.Content, m.ToolCallsJson,
                m.ToolCallId, m.ToolName, m.Model, m.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<List<AiAssistantThreadDto>> ListThreadsAsync(
        string userId, int skip, int take, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        return await db.AiAssistantThreads
            .Where(t => t.UserId == userId && !t.IsArchived)
            .OrderByDescending(t => t.UpdatedAt)
            .Skip(skip).Take(take)
            .Select(t => new AiAssistantThreadDto(t.Id, t.Title ?? "Untitled", t.Role, t.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> ArchiveThreadAsync(string threadId, string userId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var thread = await db.AiAssistantThreads
            .FirstOrDefaultAsync(t => t.Id == threadId && t.UserId == userId, ct);
        if (thread == null) return false;

        thread.IsArchived = true;
        thread.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static string GetFeatureCode(string role) => role switch
    {
        "admin" => AiFeatureCodes.AiAssistantAdmin,
        "expert" => AiFeatureCodes.AiAssistantExpert,
        _ => AiFeatureCodes.AiAssistantLearner,
    };

    private static List<LlmMessage> BuildLlmMessages(string systemPrompt, List<AiAssistantMessage> history)
    {
        var messages = new List<LlmMessage> { new("system", systemPrompt) };

        foreach (var msg in history)
        {
            if (msg.Role == "tool")
            {
                messages.Add(new LlmMessage("tool", msg.Content ?? "")
                {
                    ToolCallId = msg.ToolCallId,
                    Name = msg.ToolName,
                });
            }
            else if (msg.ToolCallsJson != null)
            {
                messages.Add(new LlmMessage("assistant", msg.Content ?? "")
                {
                    ToolCallsJson = msg.ToolCallsJson,
                });
            }
            else
            {
                messages.Add(new LlmMessage(msg.Role, msg.Content ?? ""));
            }
        }

        return messages;
    }
}

// --- Gateway streaming abstractions ---

/// <summary>Chunk types yielded by the streaming gateway.</summary>
public abstract record LlmStreamChunk;
public sealed record LlmTextChunk(string Text) : LlmStreamChunk;
public sealed record LlmToolCallChunk(string Id, string Name, string Arguments) : LlmStreamChunk;
public sealed record LlmToolCall(string Id, string Name, string Arguments);

/// <summary>Message in the LLM conversation format.</summary>
public sealed class LlmMessage(string role, string content)
{
    public string Role { get; } = role;
    public string Content { get; } = content;
    public string? ToolCallId { get; init; }
    public string? Name { get; init; }
    public string? ToolCallsJson { get; init; }
}
