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
    private static readonly ConcurrentDictionary<string, (string UserId, CancellationTokenSource Cts)> _activeTurns = new();

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
            thread.Role, thread.CreatedAt, thread.ModelOverride);
    }

    public async Task<bool> RenameThreadAsync(
        string threadId, string userId, string title, CancellationToken ct)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Length > 256) return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var thread = await db.AiAssistantThreads
            .FirstOrDefaultAsync(t => t.Id == threadId && t.UserId == userId, ct);
        if (thread is null) return false;

        thread.Title = trimmed;
        thread.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetThreadModelAsync(
        string threadId, string userId, string? model, CancellationToken ct)
    {
        var trimmed = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (trimmed is not null && trimmed.Length > 128) return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();

        var thread = await db.AiAssistantThreads
            .FirstOrDefaultAsync(t => t.Id == threadId && t.UserId == userId, ct);
        if (thread is null) return false;

        thread.ModelOverride = trimmed;
        thread.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async IAsyncEnumerable<AssistantStreamEvent> RunTurnAsync(
        string threadId, string userId, string role, string userMessage,
        CompanionContextEnvelope? context,
        [EnumeratorCancellation] CancellationToken ct,
        IReadOnlyList<AiProviderImageAttachment>? imageAttachments = null,
        AiProviderDocumentAttachment? documentAttachment = null)
    {
        using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_activeTurns.TryAdd(threadId, (userId, turnCts)))
        {
            yield return new AssistantTurnError("TURN_ALREADY_RUNNING", "A task is already running in this conversation. Wait for it or cancel it before starting another.");
            yield break;
        }

        try
        {
            // DB-over-env orchestration tunables (admin-configurable, 30s cache).
            var aiAssistant = (await settingsProvider.GetAsync(turnCts.Token)).AiAssistant;
            var isAdminTask = string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase);
            // The runtime setting is a short-turn allowance, not a reason to
            // abandon a progressing admin task and require another user send.
            // Keep a separate runaway ceiling and finish with a results-only call.
            var maxReActIterations = isAdminTask ? 1000 : aiAssistant.MaxIterations;
            var taskClock = System.Diagnostics.Stopwatch.StartNew();
            if (!string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase))
            {
                maxReActIterations = Math.Min(maxReActIterations, 6);
            }
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

            // THE companion access gate, for the turn itself (SAMI §9). The hub is
            // reachable directly, so without this the session endpoint's paywall
            // would be advisory: a learner whose package does not include the
            // companion could simply stream a turn. This is the SAME decision —
            // ICompanionAccessResolver, also used by GET /v1/companion/session and
            // the operator read — never a second, parallel rule set. It runs before
            // the learner's message is persisted, so a refused turn leaves no
            // half-written thread. Admin/expert staff turns keep their own surface
            // and are untouched.
            //
            // The master flag (ai_learning_companion) is deliberately NOT consulted
            // here: the resolver is the entitlement decision, and the session
            // endpoint already reports canChat=false when the platform switch is
            // off, so consulting it here too would only let a client that ignores
            // the flag contradict the decision the surface was given.
            if (!string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase))
            {
                var accessDecision = await TryResolveCompanionAccessAsync(scope.ServiceProvider, userId, turnCts.Token);
                if (accessDecision is { CanChat: false })
                {
                    yield return new AssistantTurnError(
                        "COMPANION_ACCESS_DENIED",
                        DescribeCompanionDenial(accessDecision.Reason));
                    yield break;
                }
            }

            // Persist user message. Attachment content is NOT stored here:
            // images ride the live provider call (ubag_attachments) and the
            // document excerpt rides the prompt of this turn only, so the
            // message table stays text + tool history like before.
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
            IReadOnlyList<CompanionEvidence> evidence = Array.Empty<CompanionEvidence>();

            if (!string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(role, ApplicationUserRoles.Expert, StringComparison.OrdinalIgnoreCase))
            {
                var companion = await BuildCompanionPromptAsync(
                    scope.ServiceProvider, userId, userMessage, systemPrompt, context, turnCts.Token);
                systemPrompt = companion.Prompt;
                systemPrompt += "\nYour public name is OET Personal Ai Assistant. Do not disclose internal provider or model identifiers. "
                    + "Help only with OET preparation, English study and the learner's authorized study material and study plan. "
                    + "For unrelated requests, briefly redirect to OET study. You have no codebase, filesystem, shell or deployment access; "
                    + "never offer programming help or claim access to those resources.";
                citations = companion.Citations;
                evidence = companion.Evidence;
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
            var iterationsExhausted = true;

            for (int iteration = 0; iteration < maxReActIterations + (isAdminTask ? 1 : 0); iteration++)
            {
                turnCts.Token.ThrowIfCancellationRequested();
                var resultsOnly = isAdminTask &&
                    (iteration == maxReActIterations || taskClock.Elapsed >= TimeSpan.FromMinutes(30));

                // Build messages array for the LLM. This turn's images ride on
                // the live user message (never persisted), so history replays
                // text-only and only the current call carries vision parts.
                var messages = BuildLlmMessages(systemPrompt, isAdminTask
                    ? BoundTaskHistory(history, userMsg, Math.Clamp(maxMessagesInContext, 16, 100))
                    : history);
                if (resultsOnly)
                {
                    messages.Add(new LlmMessage("user", "The task safety budget has been reached. Return the results already established, what was completed, and any remaining work or blocker. Do not call tools or claim unfinished work is complete."));
                }
                var liveUser = messages.LastOrDefault(m => m.Role == "user");
                if (liveUser is not null && imageAttachments is { Count: > 0 })
                {
                    messages[messages.IndexOf(liveUser)] = new LlmMessage(liveUser.Role, liveUser.Content)
                    {
                        ToolCallId = liveUser.ToolCallId,
                        Name = liveUser.Name,
                        ToolCallsJson = liveUser.ToolCallsJson,
                        ImageAttachments = imageAttachments,
                    };
                }

                // Call LLM via gateway with streaming
                var toolCalls = new List<LlmToolCall>();
                var responseText = new StringBuilder();
                string? servedModel = null;
                string? servedProviderCode = null;
                string? providerState = null;

                await foreach (var chunk in gateway.StreamCompleteWithToolsAsync(
                    featureCode, userId, messages, resultsOnly ? Array.Empty<AiToolDefinition>() : tools, thread.ModelOverride, turnCts.Token,
                    imageAttachments, documentAttachment,
                    conversationKey: threadId, isContinuation: iteration > 0))
                {
                    switch (chunk)
                    {
                        case LlmProviderStateChunk state:
                            providerState = state.State;
                            break;
                        case LlmTextChunk text:
                            responseText.Append(text.Text);
                            fullResponse.Append(text.Text);
                            yield return new AssistantTextDelta(text.Text);
                            break;

                        case LlmServedModel served:
                            servedModel = served.Model;
                            servedProviderCode = served.ProviderCode;
                            break;

                        case LlmToolCallChunk toolCall:
                            toolCalls.Add(new LlmToolCall(toolCall.Id, toolCall.Name, toolCall.Arguments));
                            break;
                    }
                }

                // A provider ignoring the empty tool list must not execute more
                // work after the safety budget. Never replay its requested tools.
                if (resultsOnly && toolCalls.Count > 0)
                {
                    yield return new AssistantTurnError("TASK_BUDGET_REACHED", "The task safety budget was reached. Completed tool results are saved; the provider did not return a final summary.");
                    yield break;
                }

                // If no tool calls, we're done — this is the final response
                if (toolCalls.Count == 0)
                {
                    // D-SAMI-001 (SAMI UAT, 2026-10-07): the stream can end with zero
                    // events (provider-level failure that produced neither text nor an
                    // error chunk). Persisting that as a silent empty answer looks to
                    // the learner like the assistant ignoring them. Surface it as a
                    // retryable provider failure instead.
                    if (responseText.Length == 0)
                    {
                        logger.LogError(
                            "Assistant turn produced an empty completion for {UserId} thread {ThreadId} (model {Model}); treating as a provider failure.",
                            userId, threadId, thread.ModelOverride);
                        yield return new AssistantTurnError(
                            "PROVIDER_EMPTY_COMPLETION",
                            "The AI provider returned an empty response for that turn. Please try again in a moment.");
                        yield break;
                    }

                    var assistantMsg = new AiAssistantMessage
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        ThreadId = threadId,
                        Role = "assistant",
                        Content = responseText.ToString(),
                        EncryptedProviderState = providerState,
                        Model = string.IsNullOrWhiteSpace(servedModel) ? thread.ModelOverride : servedModel,
                        // Bound to the final answer only: the intermediate
                        // tool-call messages are not what the learner reads.
                        CitationsJson = citations.Count > 0
                            ? JsonSerializer.Serialize(citations)
                            : null,
                        CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.AiAssistantMessages.Add(assistantMsg);
                    finalMessageId = assistantMsg.Id;
                    iterationsExhausted = false;
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
                    EncryptedProviderState = providerState,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.AiAssistantMessages.Add(toolCallMsg);
                history.Add(toolCallMsg);
                await db.SaveChangesAsync(turnCts.Token);

                // Execute each tool call
                foreach (var toolCall in toolCalls)
                {
                    yield return new AssistantToolCallStart(toolCall.Id, toolCall.Name, toolCall.Arguments);

                    var toolCtx = new AiToolContext(featureCode, userId, null, toolCallMsg.Id, iteration,
                        IsAdmin: string.Equals(role, ApplicationUserRoles.Admin, StringComparison.OrdinalIgnoreCase),
                        ThreadId: threadId, TurnId: userMsg.Id);
                    var result = await toolInvoker.InvokeAsync(
                        new OetLearner.Api.Services.Rulebook.AiToolCall
                        {
                            Id = toolCall.Id,
                            ToolCode = toolCall.Name,
                            ArgsJson = string.IsNullOrWhiteSpace(toolCall.Arguments) ? "{}" : toolCall.Arguments,
                        },
                        toolCtx, turnCts.Token);
                    var resultJson = result.ResultJson.HasValue
                        ? result.ResultJson.Value.GetRawText()
                        : JsonSerializer.Serialize(new { error = result.ErrorMessage ?? "Tool execution failed" });

                    var isError = result.Outcome != AiToolOutcome.Success;

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
                    await db.SaveChangesAsync(CancellationToken.None);
                    yield return new AssistantToolCallResult(toolCall.Id, resultJson, isError);
                }
            }

            // The loop above exits via `break` (a real final answer) or by
            // running out of iterations while the model was still mid tool
            // call. Falling through silently used to yield a completion event
            // with empty/near-empty content and no stored message -- from the
            // learner's side that looks exactly like the assistant hanging
            // forever on a long, tool-heavy turn (a large-codebase admin task
            // easily exhausts a low iteration cap). Make the exhaustion itself
            // a real, persisted answer instead of a silent no-op.
            if (iterationsExhausted)
            {
                var note = fullResponse.Length > 0
                    ? $"{fullResponse}\n\n[This turn used all {maxReActIterations} tool-call steps available and stopped there. Ask me to continue and I'll pick up where I left off.]"
                    : $"I used all {maxReActIterations} tool-call steps available for this turn without finishing. Ask me to continue and I'll pick up where I left off.";
                fullResponse.Clear();
                fullResponse.Append(note);

                var exhaustedMsg = new AiAssistantMessage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThreadId = threadId,
                    Role = "assistant",
                    Content = note,
                    CitationsJson = citations.Count > 0 ? JsonSerializer.Serialize(citations) : null,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.AiAssistantMessages.Add(exhaustedMsg);
                finalMessageId = exhaustedMsg.Id;
            }

            // Update thread timestamp and auto-title
            thread.UpdatedAt = DateTimeOffset.UtcNow;
            if (thread.Title == "New conversation" && fullResponse.Length > 0)
            {
                thread.Title = fullResponse.ToString()[..Math.Min(80, fullResponse.Length)].Trim();
            }

            await db.SaveChangesAsync(turnCts.Token);

            // Output-side leak screen — the last control, and the only one that
            // can see what the model actually said.
            //
            // Everything else guards the way IN: the prefilter decides what may
            // be retrieved, the extraction budget how much may be packed, the
            // prompt what may be said. None of them can catch a model that
            // reproduces a paid source anyway, emits a credential, or repeats
            // acceptance-pack scaffolding that should never have been in the
            // corpus. On a finding the stored message is replaced, so the
            // conversation history does not keep the leak, and the turn ends as
            // an error rather than a doctored answer.
            var answer = fullResponse.ToString();
            var leak = CompanionLeakDetector.Screen(answer, evidence.Select(e => e.CanaryTag));
            if (!leak.Blocked) leak = CompanionLeakDetector.ScreenVerbatimReuse(answer, evidence);

            if (leak.Blocked)
            {
                logger.LogError(
                    "Companion output blocked for {UserId} on thread {ThreadId}: {Findings}",
                    userId, threadId, string.Join("; ", leak.Findings));

                if (finalMessageId is not null)
                {
                    // Read through the context rather than through the in-memory
                    // history list: the final assistant message is added to the
                    // DbSet and never to that list, so looking there would find
                    // nothing and the leak would quietly stay in the stored
                    // conversation while the learner saw a refusal.
                    var stored = await db.AiAssistantMessages
                        .FirstOrDefaultAsync(m => m.Id == finalMessageId, turnCts.Token);
                    if (stored is not null)
                    {
                        stored.Content =
                            "[Withheld by the content-protection check. The assistant may not reproduce paid " +
                            "material at length — ask for an explanation of the concept instead.]";
                        await db.SaveChangesAsync(turnCts.Token);
                    }
                }

                yield return new AssistantTurnError(
                    "OUTPUT_WITHHELD",
                    "I stopped that answer because it was reproducing the source material rather than teaching " +
                    "it. Ask me to explain the idea, or to walk you through it in my own words.");

                yield break;
            }

            yield return new AssistantTurnComplete(
                finalMessageId ?? "unknown",
                fullResponse.ToString());
        }
        finally
        {
            _activeTurns.TryRemove(threadId, out _);
        }
    }

    public Task<bool> CancelTurnAsync(string threadId, string userId, CancellationToken ct)
    {
        // Ownership check mirrors RunTurnAsync/SetThreadModelAsync's `t.Id == threadId &&
        // t.UserId == userId` filter — _activeTurns is keyed only by threadId, so without
        // this any caller who knows a threadId could cancel another user's turn.
        if (_activeTurns.TryGetValue(threadId, out var turn) && turn.UserId == userId)
        {
            // Cancel only — RunTurnAsync's own `using var turnCts` disposes this CTS
            // exactly once when the turn actually finishes. Disposing it here would
            // race that still-running consumer and risk an ObjectDisposedException
            // from an unrelated in-flight call.
            turn.Cts.Cancel();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
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
            .Select(t => new AiAssistantThreadDto(t.Id, t.Title ?? "Untitled", t.Role, t.CreatedAt, t.ModelOverride))
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
                return new CompanionPromptResult(fallbackPrompt, [], []);
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

            return new CompanionPromptResult(prompt, citations, retrieval.Evidence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Companion prompt composition failed for {UserId}; using the static learner prompt.", userId);
            return new CompanionPromptResult(fallbackPrompt, [], []);
        }
    }

    /// <summary>Composed prompt plus the sources it was grounded in.</summary>
    private sealed record CompanionPromptResult(
        string Prompt,
        IReadOnlyList<AssistantCitation> Citations,
        IReadOnlyList<CompanionEvidence> Evidence);

    private static string GetFeatureCode(string role) => role switch
    {
        ApplicationUserRoles.Admin => AiFeatureCodes.AiAssistantAdmin,
        ApplicationUserRoles.Expert => AiFeatureCodes.AiAssistantExpert,
        _ => AiFeatureCodes.AiAssistantLearner,
    };

    /// <summary>
    /// The shared companion access decision for a learner turn, or null when the
    /// gate itself could not be evaluated.
    ///
    /// <para>
    /// A gate FAILURE (not a denial) deliberately returns null and lets the turn
    /// continue, because the alternative — refusing every turn while the
    /// entitlement read is broken — would take working chat away from learners who
    /// are entitled to it, without protecting anything: the gateway still applies
    /// its own permission and kill-switch checks on the same request, and every
    /// call is still recorded. A denial that WAS resolved (the normal case) is
    /// honoured strictly. Cancellation is never swallowed.
    /// </para>
    /// </summary>
    private async Task<CompanionAccessDecision?> TryResolveCompanionAccessAsync(
        IServiceProvider scopedProvider,
        string userId,
        CancellationToken ct)
    {
        try
        {
            var resolver = scopedProvider.GetRequiredService<ICompanionAccessResolver>();
            return await resolver.ResolveAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Companion access could not be resolved for {UserId}; the turn continues and the gateway's own kill-switch/permission checks still apply.",
                userId);
            return null;
        }
    }

    /// <summary>
    /// Learner-facing wording for a denied turn. Deliberately mirrors the paywall
    /// reasons in <c>messages/*/companion.json</c> rather than inventing a second
    /// vocabulary: the learner sees the same explanation whether the surface asked
    /// up front (<c>GET /v1/companion/session</c>) or the turn was refused here.
    /// </summary>
    private static string DescribeCompanionDenial(string reason) => reason switch
    {
        CompanionAccessReasons.ManuallyDisabled =>
            "The AI Learning Companion has been switched off for your account. Contact support if you think this is wrong.",
        CompanionAccessReasons.Expired =>
            "Your AI Learning Companion access has ended. Choose a package that includes it to carry on.",
        CompanionAccessReasons.PackageRequired =>
            "The AI Learning Companion is not part of your current package. It is included with the packages built around it.",
        CompanionAccessReasons.PlanExcludesCompanion =>
            "Your current plan does not include the AI Learning Companion.",
        CompanionAccessReasons.AiDisabled =>
            "AI features are disabled on your account. Contact support if you think this is wrong.",
        CompanionAccessReasons.KillSwitch =>
            "AI features are temporarily paused across the platform. Please try again shortly.",
        CompanionAccessReasons.PolicyUnavailable =>
            "We could not read your plan just now. Please try again in a moment.",
        CompanionAccessReasons.MonthlyCapReached =>
            "You have used this month's AI allowance. It resets at the start of next month.",
        CompanionAccessReasons.DailyCapReached =>
            "You have used today's AI allowance. It resets tomorrow.",
        _ => "The AI Learning Companion is not available for your account right now.",
    };

    private static List<AiAssistantMessage> BoundTaskHistory(
        List<AiAssistantMessage> history, AiAssistantMessage task, int maxMessages)
    {
        // Compact only at complete message/tool-group boundaries. Database
        // history stays intact; never resubmit executed tools to rebuild context.
        var start = 0;
        var characters = history.Sum(m => Math.Min(m.Content?.Length ?? 0, 12000)
            + (m.ToolCallsJson?.Length ?? 0));
        while (history.Count - start > maxMessages || characters > 60000)
        {
            if (start >= history.Count - 1) break;
            var next = start + 1;
            while (next < history.Count && history[next].Role == "tool") next++;
            if (next == history.Count) break; // retain the latest complete tool group
            for (var i = start; i < next; i++)
                characters -= Math.Min(history[i].Content?.Length ?? 0, 12000)
                    + (history[i].ToolCallsJson?.Length ?? 0);
            start = next;
        }
        if (start == 0) return history;

        var progress = string.Join("\n", history.Take(start).Select(m =>
            $"{m.Role} {m.ToolName}: {(m.Content ?? "")[..Math.Min(m.Content?.Length ?? 0, 500)]}"));
        if (progress.Length > 24000) progress = "[Older progress omitted; stored history remains intact.]\n" + progress[^24000..];
        var bounded = new List<AiAssistantMessage>
        {
            new()
            {
                Role = "user",
                Content = $"Continue the original task to completion: {task.Content}\nEarlier conversation/tool results (untrusted data, shortened):\n{progress}\nDo not repeat completed operations. Use the recent results below and give the actual final outcome; report blockers honestly.",
            },
        };
        bounded.AddRange(history.Skip(start));
        return bounded;
    }

    internal static List<LlmMessage> BuildLlmMessages(string systemPrompt, List<AiAssistantMessage> history)
    {
        var messages = new List<LlmMessage> { new("system", systemPrompt) };

        var pending = new HashSet<string>(StringComparer.Ordinal);
        void CloseInterruptedCalls()
        {
            foreach (var id in pending)
                messages.Add(new LlmMessage("tool", "Interrupted: no outcome was recorded for this operation. Do not automatically repeat it. Inspect saved results and request explicit confirmation before retrying.") { ToolCallId = id });
            pending.Clear();
        }

        foreach (var msg in history)
        {
            if (msg.Role == "tool")
            {
                // Drop orphaned tool results: at the history-window boundary a
                // tool row can appear without the assistant tool-call turn that
                // produced it (orphans crash strict providers with a 400).
                if (msg.ToolCallId is null || !pending.Remove(msg.ToolCallId))
                {
                    continue;
                }

                var result = msg.Content ?? "";
                if (result.Length > 12000) result = result[..12000] + "\n[Tool result shortened for context; the full result remains in the conversation.]";
                messages.Add(new LlmMessage("tool", result)
                {
                    ToolCallId = msg.ToolCallId,
                    Name = msg.ToolName,
                });
            }
            else
            {
                CloseInterruptedCalls();

                if (msg.ToolCallsJson != null)
                {
                    foreach (var call in JsonSerializer.Deserialize<List<LlmToolCall>>(msg.ToolCallsJson) ?? [])
                        pending.Add(call.Id);
                    messages.Add(new LlmMessage("assistant", msg.Content ?? "")
                    {
                        ToolCallsJson = msg.ToolCallsJson,
                        ProviderState = msg.EncryptedProviderState,
                    });
                }
                else
                {
                    messages.Add(new LlmMessage(msg.Role, msg.Content ?? "") { ProviderState = msg.EncryptedProviderState });
                }
            }
        }

        CloseInterruptedCalls();
        return messages;
    }
}

// --- Gateway streaming abstractions ---

/// <summary>Chunk types yielded by the streaming gateway.</summary>
public abstract record LlmStreamChunk;
public sealed record LlmTextChunk(string Text) : LlmStreamChunk;
internal sealed record LlmProviderStateChunk(string State) : LlmStreamChunk;
public sealed record LlmToolCallChunk(string Id, string Name, string Arguments) : LlmStreamChunk;
/// <summary>Model actually served for this turn (provider echo preferred,
/// routed request model otherwise), plus the provider row code.</summary>
public sealed record LlmServedModel(string? Model, string ProviderCode) : LlmStreamChunk;
public sealed record LlmToolCall(string Id, string Name, string Arguments);

/// <summary>Message in the LLM conversation format.</summary>
public sealed class LlmMessage(string role, string content)
{
    public string Role { get; } = role;
    public string Content { get; } = content;
    public string? ToolCallId { get; init; }
    public string? Name { get; init; }
    public string? ToolCallsJson { get; init; }
    public string? ProviderState { get; init; }
    /// <summary>Inline images attached to this turn. Carried onto the
    /// provider <c>AiChatMessage</c> so vision-capable providers (UBAG
    /// ubag_attachments) actually see the upload.</summary>
    public IReadOnlyList<AiProviderImageAttachment>? ImageAttachments { get; init; }
}
