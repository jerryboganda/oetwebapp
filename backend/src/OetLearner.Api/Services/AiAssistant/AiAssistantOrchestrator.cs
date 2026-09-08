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
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Services.AiAssistant;

public sealed class AiAssistantOrchestrator(
    IServiceScopeFactory scopeFactory,
    IAiAssistantGateway gateway,
    IAiToolRegistry toolRegistry,
    IAiToolInvoker toolInvoker,
    ISystemPromptProvider systemPromptProvider,
    IRuntimeSettingsProvider settingsProvider,
    ILogger<AiAssistantOrchestrator> logger) : IAiAssistantOrchestrator
{
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
        CompanionContextEnvelope? context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeTurns[threadId] = turnCts;

        try
        {
            // DB-over-env orchestration tunables (admin-configurable, 30s cache).
            var aiAssistant = (await settingsProvider.GetAsync(turnCts.Token)).AiAssistant;
            var maxReActIterations = aiAssistant.MaxIterations;
            var maxMessagesInContext = aiAssistant.MaxContextMessages;

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
                .Take(maxMessagesInContext)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(turnCts.Token);

            // Get system prompt for role.
            //
            // Learners get the AI Learning Companion prompt: persona, their own
            // profile, entitlement-filtered evidence and the safety boundaries
            // (docs/ai-learning-companion/). Admin and expert keep the existing
            // developer-assistant prompts untouched.
            //
            // If anything in the companion path fails we fall back to the previous
            // static prompt rather than dropping the turn — but the fallback cannot
            // leak protected content, because it carries no retrieved evidence.
            var systemPrompt = systemPromptProvider.GetSystemPrompt(role, userId);
            IReadOnlyList<AssistantCitation> citations = Array.Empty<AssistantCitation>();

            if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, "expert", StringComparison.OrdinalIgnoreCase))
            {
                var companion = await BuildCompanionPromptAsync(
                    scope.ServiceProvider, userId, userMessage, systemPrompt, context, turnCts.Token);
                systemPrompt = companion.Prompt;
                citations = companion.Citations;
            }

            // Emitted before the first token so the surface can show what the
            // answer is grounded in while it is still being written. The client
            // binds them to the message id that arrives with MessageComplete.
            if (citations.Count > 0)
            {
                yield return new AssistantCitationsResolved(citations);
            }

            // Get available tools for role
            var featureCode = GetFeatureCode(role);
            var tools = await toolRegistry.ResolveForFeatureAsync(featureCode, turnCts.Token);

            // ReAct loop
            var fullResponse = new StringBuilder();
            string? finalMessageId = null;

            for (int iteration = 0; iteration < maxReActIterations; iteration++)
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
                        // Bound to the final answer only: the intermediate
                        // tool-call messages are not what the learner reads.
                        CitationsJson = citations.Count > 0
                            ? JsonSerializer.Serialize(citations)
                            : null,
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

                    var toolCtx = new AiToolContext(featureCode, userId, null, toolCallMsg.Id, iteration,
                        IsAdmin: string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
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
                m.ToolCallId, m.ToolName, m.Model, m.CreatedAt, m.CitationsJson))
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

    /// <summary>
    /// Builds the grounded companion system prompt for a learner turn.
    /// Resolved from the request scope because the companion services are scoped.
    /// </summary>
    private async Task<CompanionPromptResult> BuildCompanionPromptAsync(
        IServiceProvider scopedProvider,
        string userId,
        string userMessage,
        string fallbackPrompt,
        CompanionContextEnvelope? envelope,
        CancellationToken ct)
    {
        try
        {
            var flags = scopedProvider.GetRequiredService<ICompanionFeatureFlags>();
            if (!await flags.IsEnabledAsync(ct))
            {
                return new CompanionPromptResult(fallbackPrompt, Array.Empty<AssistantCitation>());
            }

            var contextResolver = scopedProvider.GetRequiredService<ICompanionContextResolver>();
            var retriever = scopedProvider.GetRequiredService<ICompanionRetriever>();
            var composer = scopedProvider.GetRequiredService<ICompanionPromptComposer>();

            var context = await contextResolver.ResolveAsync(userId, envelope, ct);
            var retrieval = await retriever.RetrieveAsync(userMessage, context, maxResults: 8, ct);

            logger.LogDebug(
                "Companion turn for {UserId}: {Evidence} evidence, vector={Vector}, conflict={Conflict}, examMode={ExamMode}",
                userId, retrieval.Evidence.Count, retrieval.VectorSearchUsed, retrieval.AuthorityConflict, context.ExamMode);

            var prompt = await composer.ComposeAsync(context, retrieval, ct);

            // The [S#] labels in the prompt and the ordinals here are the same
            // sequence, so a learner can match a claim to a source.
            var citations = retrieval.Evidence
                .Select((e, index) => new AssistantCitation(
                    Ordinal: index + 1,
                    SourceKey: e.SourceKey,
                    SourceTitle: e.SourceTitle,
                    Authority: e.Authority.ToString(),
                    Heading: e.Heading,
                    PageNumber: e.PageNumber,
                    TimestampSeconds: e.TimestampSeconds))
                .ToList();

            return new CompanionPromptResult(prompt, citations);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Companion prompt composition failed for {UserId}; using the static learner prompt.", userId);
            return new CompanionPromptResult(fallbackPrompt, Array.Empty<AssistantCitation>());
        }
    }

    /// <summary>Composed prompt plus the sources it was grounded in.</summary>
    private sealed record CompanionPromptResult(
        string Prompt,
        IReadOnlyList<AssistantCitation> Citations);

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
