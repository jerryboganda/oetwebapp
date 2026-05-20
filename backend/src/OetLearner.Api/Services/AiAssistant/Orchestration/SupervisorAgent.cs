using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Contracts.AiAssistant;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.Rulebook;
using AiChatMessage = OetLearner.Api.Domain.AiAssistant.AiChatMessage;

namespace OetLearner.Api.Services.AiAssistant.Orchestration;

/// <summary>
/// V1 supervisor: single-pass conversation loop. Loads thread history,
/// builds a canonical gateway-grounded chatbot prompt, calls the AI gateway,
/// fans the completion out as a TokenDelta frame, persists the final
/// assistant message + audit event, then emits MessageEnd.
///
/// Tool calling, Planner/Critic loops, and codebase retrieval are deferred
/// to later phases (see docs/AI-ASSISTANT-PLAN.md). The shape of the
/// pipeline below is identical to what those phases will extend.
/// </summary>
public sealed class SupervisorAgent : IAgentOrchestrator
{
    private readonly LearnerDbContext _db;
    private readonly IAiGatewayService _gateway;
    private readonly AiAssistantSettingsService _settings;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly ILogger<SupervisorAgent> _logger;

    public SupervisorAgent(
        LearnerDbContext db,
        IAiGatewayService gateway,
        AiAssistantSettingsService settings,
        IAiUsageRecorder usageRecorder,
        ILogger<SupervisorAgent> logger)
    {
        _db = db;
        _gateway = gateway;
        _settings = settings;
        _usageRecorder = usageRecorder;
        _logger = logger;
    }

    public async Task RunTurnAsync(AgentContext ctx, string userMessage, CancellationToken ct)
    {
        var sink = ctx.FrameSink;
        var assistantMsgId = ctx.MessageId;
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            if (sink is not null)
            {
                await sink.WriteAsync(new MessageStartFrame
                {
                    Type = "message_start",
                    ThreadId = ctx.ThreadId,
                    MessageId = assistantMsgId,
                    Role = "assistant",
                }, ct);
            }

            var settings = _settings.Current;
            if (!settings.GlobalEnabled)
            {
                await EmitErrorAndPersistAsync(ctx, sink, "kill_switch", "The AI Assistant is disabled.",
                    startedAt, providerId: null, model: null, outcome: AiCallOutcome.GatewayRefused, ct);
                return;
            }

            var thread = await _db.AiChatThreads
                .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
                .SingleOrDefaultAsync(t => t.Id == ctx.ThreadId, ct);
            if (thread is null)
            {
                await EmitErrorAndPersistAsync(ctx, sink, "thread_not_found", "Conversation thread not found.",
                    startedAt, providerId: null, model: null, outcome: AiCallOutcome.PlatformError, ct);
                return;
            }

            var prompt = _gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Chatbot,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.AssistAdminCommand,
            });
            var gatewayInput = BuildGatewayInput(thread, userMessage);

            AiGatewayResult result;

            try
            {
                result = await _gateway.CompleteAsync(new AiGatewayRequest
                {
                    Prompt = prompt,
                    UserInput = gatewayInput,
                    Temperature = 0.2,
                    MaxTokens = 2048,
                    UserId = ctx.ActorUserId.ToString(),
                    FeatureCode = AiFeatureCodes.AdminAiChatbot,
                    PromptTemplateId = "admin.ai_assistant.v1",
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("AI Assistant turn cancelled. message={MessageId}", assistantMsgId);
                if (sink is not null)
                {
                    await sink.WriteAsync(new MessageEndFrame
                    {
                        Type = "message_end",
                        ThreadId = ctx.ThreadId,
                        MessageId = assistantMsgId,
                    }, CancellationToken.None);
                }
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI Assistant gateway call failed. message={MessageId}", assistantMsgId);
                await EmitErrorFrameAsync(sink, assistantMsgId, "provider_error",
                    "The language model returned an error. The admin log has details.", ctx.ThreadId, ct);
                return;
            }

            var assistantContent = result.Completion;
            var promptTokens = result.Usage?.PromptTokens;
            var completionTokens = result.Usage?.CompletionTokens;
            if (sink is not null && !string.IsNullOrEmpty(assistantContent))
            {
                await sink.WriteAsync(new TokenDeltaFrame
                {
                    Type = "token_delta",
                    ThreadId = ctx.ThreadId,
                    MessageId = assistantMsgId,
                    Delta = assistantContent,
                }, ct);
            }

            var assistantMsg = new AiChatMessage
            {
                Id = assistantMsgId,
                ThreadId = thread.Id,
                Role = AiChatMessageRole.Assistant,
                Content = assistantContent,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.AiChatMessages.Add(assistantMsg);
            thread.UpdatedAt = DateTimeOffset.UtcNow;

            _db.AiAuditEvents.Add(new AiAuditEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = ctx.ActorUserId,
                Action = AiAuditAction.MessageSent,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    threadId = thread.Id,
                    messageId = assistantMsgId,
                    promptTokens,
                    completionTokens,
                }),
                OccurredAt = DateTimeOffset.UtcNow,
            });

            await _db.SaveChangesAsync(CancellationToken.None);

            if (sink is not null)
            {
                await sink.WriteAsync(new MessageEndFrame
                {
                    Type = "message_end",
                    ThreadId = ctx.ThreadId,
                    MessageId = assistantMsgId,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                }, CancellationToken.None);
            }
        }
        finally
        {
            ctx.FrameSink?.TryComplete();
        }
    }

    private static string BuildGatewayInput(AiChatThread thread, string userMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Conversation history (oldest to newest, truncated):");
        foreach (var m in thread.Messages.TakeLast(20))
        {
            var role = m.Role switch
            {
                AiChatMessageRole.User => "user",
                AiChatMessageRole.Assistant => "assistant",
                AiChatMessageRole.System => "system",
                AiChatMessageRole.Tool => "tool",
                _ => "user",
            };
            sb.AppendLine($"[{role}] {TruncateForPrompt(m.Content, 4000)}");
        }
        sb.AppendLine();
        sb.AppendLine("Current admin message:");
        sb.AppendLine(TruncateForPrompt(userMessage, 8000));
        return sb.ToString();
    }

    private static string TruncateForPrompt(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
            return value;
        return value[..maxChars] + "\n[truncated]";
    }

    private async Task EmitErrorAndPersistAsync(
        AgentContext ctx,
        ChannelWriter<StreamFrame>? sink,
        string code,
        string message,
        DateTimeOffset startedAt,
        string? providerId,
        string? model,
        AiCallOutcome outcome,
        CancellationToken ct)
    {
        if (sink is not null)
        {
            await EmitErrorFrameAsync(sink, ctx.MessageId, code, message, ctx.ThreadId, ct);
        }

        // Canonical AI usage row via IAiUsageRecorder.
        try
        {
            var latencyMs = (int)Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            await _usageRecorder.RecordFailureAsync(
                new AiUsageContext(
                    UserId: ctx.ActorUserId.ToString(),
                    AuthAccountId: null,
                    TenantId: null,
                    FeatureCode: AiFeatureCodes.AdminAiChatbot,
                    RulebookVersion: null,
                    PromptTemplateId: "admin.ai_assistant.v1",
                    SystemPrompt: null,
                    UserPrompt: null,
                    StartedAt: startedAt),
                providerId: providerId,
                model: model,
                keySource: AiKeySource.None,
                outcome: outcome,
                errorCode: code,
                errorMessage: message,
                latencyMs: latencyMs,
                retryCount: 0,
                policyTrace: null,
                ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record AdminAiChatbot failure usage.");
        }
    }

    private static async Task EmitErrorFrameAsync(
        ChannelWriter<StreamFrame>? sink,
        Guid messageId,
        string code,
        string message,
        Guid threadId,
        CancellationToken ct)
    {
        if (sink is null)
        {
            return;
        }

        await sink.WriteAsync(new ErrorFrame
        {
            Type = "error",
            ThreadId = threadId,
            MessageId = messageId,
            Message = message,
            Code = code,
        }, ct.IsCancellationRequested ? CancellationToken.None : ct);
    }
}
