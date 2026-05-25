using System.Runtime.CompilerServices;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.AiAssistant;

/// <summary>
/// Streaming gateway adapter for the AI Assistant. Calls through to
/// the existing provider infrastructure with tool definitions but without
/// the gateway's internal tool-calling loop (the orchestrator owns that loop).
/// </summary>
public interface IAiAssistantGateway
{
    IAsyncEnumerable<LlmStreamChunk> StreamCompleteWithToolsAsync(
        string featureCode,
        string? userId,
        List<LlmMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        string? modelOverride,
        CancellationToken ct);
}

public sealed class AiAssistantGateway(
    IAiFeatureRouteResolver routeResolver,
    IAiProviderRegistry providerRegistry,
    IEnumerable<IAiModelProvider> providers,
    ILogger<AiAssistantGateway> logger,
    IAiUsageRecorder? usageRecorder = null,
    IAiQuotaService? quotaService = null,
    IAiCreditService? creditService = null,
    IHostEnvironment? hostEnvironment = null) : IAiAssistantGateway
{
    public async IAsyncEnumerable<LlmStreamChunk> StreamCompleteWithToolsAsync(
        string featureCode,
        string? userId,
        List<LlmMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        string? modelOverride,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Resolve provider + model via feature routing
        var route = await routeResolver.ResolveAsync(featureCode, ct);
        var requestedProviderCode = route?.ProviderCode;
        var requestedModel = modelOverride ?? route?.Model;
        var resolvedProvider = await ResolveProviderAsync(requestedProviderCode, requestedModel, ct);
        var providerCode = resolvedProvider?.ProviderCode ?? requestedProviderCode ?? string.Empty;
        var model = resolvedProvider?.Model ?? requestedModel ?? string.Empty;
        var provider = resolvedProvider?.Provider;

        if (provider == null)
        {
            await RecordFailureAsync(
                featureCode,
                userId,
                providerCode,
                model,
                AiCallOutcome.GatewayRefused,
                "no_provider",
                "No AI provider is configured.",
                requestSystemPrompt: null,
                requestUserPrompt: messages.LastOrDefault(m => m.Role == "user")?.Content,
                startedAt,
                stopwatch,
                CancellationToken.None);
            yield return new LlmTextChunk("No AI provider is configured. Please contact an administrator.");
            yield break;
        }

        AiQuotaDecision? quotaDecision = null;
        if (quotaService is not null)
        {
            quotaDecision = await quotaService.TryReserveAsync(userId, featureCode, AiKeySource.Platform, ct);
            if (!quotaDecision.Allowed)
            {
                await RecordFailureAsync(
                    featureCode,
                    userId,
                    providerCode,
                    model,
                    AiCallOutcome.GatewayRefused,
                    quotaDecision.ErrorCode ?? "quota_denied",
                    quotaDecision.ErrorMessage,
                    requestSystemPrompt: null,
                    requestUserPrompt: messages.LastOrDefault(m => m.Role == "user")?.Content,
                    startedAt,
                    stopwatch,
                    CancellationToken.None,
                    policyTrace: quotaDecision.PolicyTrace);

                yield return new LlmTextChunk(quotaDecision.ErrorMessage ?? "This AI assistant request is temporarily unavailable.");
                yield break;
            }
        }

        var shouldDebitLearnerCredit = ShouldDebitAiCredit(featureCode)
            && !string.IsNullOrWhiteSpace(userId);
        if (shouldDebitLearnerCredit)
        {
            if (usageRecorder is null || creditService is null)
            {
                logger.LogError("AI Assistant refused paid feature {FeatureCode} for user {UserId} because AI credit accounting is not configured.", featureCode, userId);
                if (usageRecorder is not null)
                {
                    await RecordFailureAsync(
                        featureCode,
                        userId,
                        providerCode,
                        model,
                        AiCallOutcome.GatewayRefused,
                        "ai_credit_accounting_unavailable",
                        "AI credit accounting is not configured.",
                        requestSystemPrompt: null,
                        requestUserPrompt: messages.LastOrDefault(m => m.Role == "user")?.Content,
                        startedAt,
                        stopwatch,
                        CancellationToken.None,
                        policyTrace: quotaDecision?.PolicyTrace);
                }
                yield return new LlmTextChunk("AI credit accounting is not configured for this paid assistant feature. Please contact an administrator.");
                yield break;
            }

            var balance = await creditService!.GetBalanceAsync(userId!, ct);
            if (balance.TokensAvailable < 1)
            {
                await RecordFailureAsync(
                    featureCode,
                    userId,
                    providerCode,
                    model,
                    AiCallOutcome.GatewayRefused,
                    "ai_credits_insufficient",
                    "AI grading credits are exhausted.",
                    requestSystemPrompt: null,
                    requestUserPrompt: messages.LastOrDefault(m => m.Role == "user")?.Content,
                    startedAt,
                    stopwatch,
                    CancellationToken.None,
                    policyTrace: quotaDecision?.PolicyTrace);
                yield return new LlmTextChunk("AI grading credits are exhausted. Purchase an AI package or upgrade your plan to continue.");
                yield break;
            }
        }

        // Convert our message format to the provider's AiChatMessage format
        List<AiChatMessage>? chatMessages = null;
        bool malformedToolCallHistory = false;
        try
        {
            chatMessages = messages.Select(m =>
            {
                var msg = new AiChatMessage
                {
                    Role = m.Role,
                    Content = m.Content,
                    ToolCallId = m.ToolCallId,
                };
                if (m.ToolCallsJson != null)
                {
                    msg = new AiChatMessage
                    {
                        Role = m.Role,
                        Content = m.Content,
                        ToolCallId = m.ToolCallId,
                        ToolCalls = ParseToolCalls(m.ToolCallsJson),
                    };
                }
                return msg;
            }).ToList();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "AI Assistant refused malformed tool-call history for {FeatureCode}", featureCode);
            malformedToolCallHistory = true;
        }

        if (malformedToolCallHistory)
        {
            await RecordFailureAsync(
                featureCode,
                userId,
                providerCode,
                model,
                AiCallOutcome.GatewayRefused,
                "malformed_tool_call_history",
                "Assistant tool-call history could not be parsed.",
                requestSystemPrompt: null,
                requestUserPrompt: messages.LastOrDefault(m => m.Role == "user")?.Content,
                startedAt,
                stopwatch,
                CancellationToken.None,
                policyTrace: quotaDecision?.PolicyTrace);
            yield return new LlmTextChunk("This assistant conversation state could not be resumed. Please start a new assistant turn.");
            yield break;
        }

        // Build provider request with explicit messages + tools (skip gateway tool loop)
        var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
        var lastUser = messages.LastOrDefault(m => m.Role == "user");

        var request = new AiProviderRequest
        {
            ProviderCode = providerCode,
            Model = model,
            SystemPrompt = systemMsg?.Content ?? "",
            UserPrompt = lastUser?.Content ?? "",
            Temperature = 0.7,
            MaxTokens = 4096,
            Messages = chatMessages,
            Tools = tools,
            ToolChoice = tools.Count > 0 ? "auto" : null,
        };

        AiProviderCompletion? completion = null;
        string? errorMessage = null;
        try
        {
            completion = await provider.CompleteAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await RecordFailureAsync(
                featureCode,
                userId,
                providerCode,
                model,
                AiCallOutcome.Cancelled,
                "cancelled",
                "Assistant request was cancelled.",
                request.SystemPrompt,
                request.UserPrompt,
                startedAt,
                stopwatch,
                CancellationToken.None,
                policyTrace: quotaDecision?.PolicyTrace);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI Assistant provider call failed for {FeatureCode}", featureCode);
            await RecordFailureAsync(
                featureCode,
                userId,
                providerCode,
                model,
                AiCallOutcome.ProviderError,
                "provider_error",
                "Provider request failed.",
                request.SystemPrompt,
                request.UserPrompt,
                startedAt,
                stopwatch,
                CancellationToken.None,
                policyTrace: quotaDecision?.PolicyTrace);
            errorMessage = "I encountered an error communicating with the AI service. Please try again.";
        }

        if (errorMessage != null)
        {
            yield return new LlmTextChunk(errorMessage);
            yield break;
        }

        var costEstimate = completion!.Usage is not null
            ? await ComputeCostEstimateAsync(providerCode, completion.Usage, CancellationToken.None)
            : 0m;

        var usageRecordId = $"aiu_{Guid.NewGuid():N}";
        string? persistedUsageRecordId = null;

        // Record usage
        if (usageRecorder != null)
        {
            try
            {
                var usageCtx = new AiUsageContext(
                    UserId: userId,
                    AuthAccountId: null,
                    TenantId: null,
                    FeatureCode: featureCode,
                    RulebookVersion: null,
                    PromptTemplateId: null,
                    SystemPrompt: request.SystemPrompt,
                    UserPrompt: request.UserPrompt,
                    StartedAt: startedAt);

                persistedUsageRecordId = await usageRecorder.RecordSuccessAsync(
                    usageCtx,
                    providerCode,
                    model,
                    AiKeySource.Platform,
                    completion!.Usage,
                    latencyMs: (int)stopwatch.ElapsedMilliseconds,
                    retryCount: 0,
                    policyTrace: quotaDecision?.PolicyTrace,
                    ct: CancellationToken.None,
                    costEstimateUsd: costEstimate,
                    usageRecordId: usageRecordId);

            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record AI usage for assistant call");
            }
        }

        if (quotaService is not null && completion.Usage is not null && !string.IsNullOrWhiteSpace(userId))
        {
            try
            {
                await quotaService.CommitAsync(
                    userId,
                    featureCode,
                    completion.Usage.PromptTokens,
                    completion.Usage.CompletionTokens,
                    costEstimate,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to commit AI quota for assistant call");
            }
        }

        if (shouldDebitLearnerCredit)
        {
            if (string.IsNullOrWhiteSpace(persistedUsageRecordId))
            {
                logger.LogWarning("AI assistant usage accounting failed before learner credit debit could be posted for user {UserId}, feature {FeatureCode}, usage record {UsageRecordId}.", userId, featureCode, usageRecordId);
            }
            else
            {
                try
                {
                    var debited = await creditService!.DebitUsageAsync(
                        new AiCreditUsageDebitRequest(
                            UserId: userId!,
                            UsageRecordId: persistedUsageRecordId,
                            FeatureCode: featureCode,
                            Credits: 1,
                            CostUsd: costEstimate,
                            OccurredAt: startedAt),
                        CancellationToken.None);
                    if (!debited)
                    {
                        logger.LogWarning("AI assistant credit debit was not posted for user {UserId}, feature {FeatureCode}, usage record {UsageRecordId}.", userId, featureCode, persistedUsageRecordId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to debit AI credit for assistant call by user {UserId}, feature {FeatureCode}, usage record {UsageRecordId}.", userId, featureCode, persistedUsageRecordId);
                }
            }
        }

        // Check for tool calls in the response
        if (completion!.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in completion.ToolCalls)
            {
                yield return new LlmToolCallChunk(tc.Id, tc.ToolCode, tc.ArgsJson);
            }
            yield break;
        }

        // Yield text response in chunks for streaming feel
        var content = completion!.Text ?? "";
        if (content.Length > 0)
        {
            const int chunkSize = 80;
            for (int i = 0; i < content.Length; i += chunkSize)
            {
                var chunk = content[i..Math.Min(i + chunkSize, content.Length)];
                yield return new LlmTextChunk(chunk);
            }
        }
    }

    private async Task RecordFailureAsync(
        string featureCode,
        string? userId,
        string? providerCode,
        string? model,
        AiCallOutcome outcome,
        string errorCode,
        string? errorMessage,
        string? requestSystemPrompt,
        string? requestUserPrompt,
        DateTimeOffset startedAt,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken ct,
        string? policyTrace = null)
    {
        if (usageRecorder is null) return;
        try
        {
            await usageRecorder.RecordFailureAsync(
                new AiUsageContext(
                    UserId: userId,
                    AuthAccountId: null,
                    TenantId: null,
                    FeatureCode: featureCode,
                    RulebookVersion: null,
                    PromptTemplateId: null,
                    SystemPrompt: requestSystemPrompt,
                    UserPrompt: requestUserPrompt,
                    StartedAt: startedAt),
                providerCode,
                model,
                AiKeySource.Platform,
                outcome,
                errorCode,
                errorMessage,
                (int)stopwatch.ElapsedMilliseconds,
                retryCount: 0,
                policyTrace: policyTrace,
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record AI assistant failure usage for {FeatureCode}", featureCode);
        }
    }

    private async Task<decimal> ComputeCostEstimateAsync(string providerCode, AiUsage usage, CancellationToken ct)
    {
        try
        {
            var row = await providerRegistry.FindByCodeAsync(providerCode, ct);
            if (row is null) return 0m;
            var promptCost = row.PricePer1kPromptTokens * usage.PromptTokens / 1000m;
            var completionCost = row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
            return promptCost + completionCost;
        }
        catch
        {
            return 0m;
        }
    }

    // Conversation opening/reply turns are intentionally not debited here:
    // the paid deliverable is the post-session ConversationEvaluation.
    private static bool ShouldDebitAiCredit(string featureCode)
        => string.Equals(featureCode, AiFeatureCodes.WritingGrade, StringComparison.OrdinalIgnoreCase)
           || string.Equals(featureCode, AiFeatureCodes.WritingSampleScore, StringComparison.OrdinalIgnoreCase)
           || string.Equals(featureCode, AiFeatureCodes.SpeakingGrade, StringComparison.OrdinalIgnoreCase)
           || string.Equals(featureCode, AiFeatureCodes.MockFullGrade, StringComparison.OrdinalIgnoreCase)
           || string.Equals(featureCode, AiFeatureCodes.PronunciationScore, StringComparison.OrdinalIgnoreCase)
           || string.Equals(featureCode, AiFeatureCodes.PronunciationFeedback, StringComparison.OrdinalIgnoreCase)
           || string.Equals(featureCode, AiFeatureCodes.ConversationEvaluation, StringComparison.OrdinalIgnoreCase)
           || string.Equals(featureCode, SpeakingAiFeatureCodes.SpeakingScoreV2, StringComparison.OrdinalIgnoreCase);
    private async Task<ResolvedAssistantProvider?> ResolveProviderAsync(string? providerCode, string? requestedModel, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            var defaultRow = await FirstCredentialedTextChatRowAsync(ct);
            if (defaultRow is not null)
            {
                var providerName = ProviderNameForDialect(defaultRow.Dialect);
                var provider = providerName is null
                    ? null
                    : providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
                if (provider is not null)
                {
                    return new ResolvedAssistantProvider(
                        provider,
                        defaultRow.Code,
                        ResolveModel(requestedModel, defaultRow.DefaultModel, provider.Name));
                }
            }

            if (hostEnvironment?.IsProduction() == true) return null;
            var fallbackMock = providers.FirstOrDefault(p => p.Name.Equals("mock", StringComparison.OrdinalIgnoreCase));
            return fallbackMock is null
                ? null
                : new ResolvedAssistantProvider(fallbackMock, fallbackMock.Name, ResolveModel(requestedModel, null, fallbackMock.Name));
        }

        var routeRow = await providerRegistry.FindByCodeAsync(providerCode, ct);
        if (routeRow is not null)
        {
            var preferredName = ProviderNameForDialect(routeRow.Dialect);
            if (preferredName is not null)
            {
                var provider = providers.FirstOrDefault(p => p.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase));
                if (provider is not null)
                {
                    return new ResolvedAssistantProvider(
                        provider,
                        routeRow.Code,
                        ResolveModel(requestedModel, routeRow.DefaultModel, provider.Name));
                }
            }
            return null;
        }

        var direct = providers.FirstOrDefault(p => p.Name.Equals(providerCode, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return new ResolvedAssistantProvider(direct, providerCode, ResolveModel(requestedModel, null, direct.Name));
        }

        if (string.Equals(providerCode, "openai", StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerCode, "openai-compatible", StringComparison.OrdinalIgnoreCase))
        {
            var openAiCompatible = providers.FirstOrDefault(p => p.Name.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase));
            if (openAiCompatible is not null)
            {
                return new ResolvedAssistantProvider(openAiCompatible, providerCode, ResolveModel(requestedModel, null, openAiCompatible.Name));
            }

            var registryProvider = providers.FirstOrDefault(p => p.Name.Equals("registry", StringComparison.OrdinalIgnoreCase));
            var registryRow = await FirstCredentialedOpenAiCompatibleRowAsync(ct);
            if (registryProvider is not null && registryRow is not null)
            {
                var model = IsLegacyOpenAiModel(requestedModel) && !string.IsNullOrWhiteSpace(registryRow.DefaultModel)
                    ? registryRow.DefaultModel
                    : requestedModel;
                return new ResolvedAssistantProvider(
                    registryProvider,
                    registryRow.Code,
                    ResolveModel(model, registryRow.DefaultModel, registryProvider.Name));
            }
        }

        if (hostEnvironment?.IsProduction() == true) return null;
        var mock = providers.FirstOrDefault(p => p.Name.Equals("mock", StringComparison.OrdinalIgnoreCase));
        return mock is null
            ? null
            : new ResolvedAssistantProvider(mock, mock.Name, ResolveModel(requestedModel, null, mock.Name));
    }

    private static string? ProviderNameForDialect(AiProviderDialect dialect) => dialect switch
    {
        AiProviderDialect.Cloudflare => "cloudflare",
        AiProviderDialect.Anthropic => "anthropic",
        AiProviderDialect.Copilot => "copilot",
        AiProviderDialect.OpenAiCompatible => "registry",
        _ => null,
    };

    private async Task<AiProvider?> FirstCredentialedOpenAiCompatibleRowAsync(CancellationToken ct)
    {
        var rows = await providerRegistry.ListByCategoryAsync(AiProviderCategory.TextChat, ct);
        return rows.FirstOrDefault(row => row.IsActive
                                          && row.Dialect == AiProviderDialect.OpenAiCompatible
                                          && !string.IsNullOrWhiteSpace(row.EncryptedApiKey));
    }

    private async Task<AiProvider?> FirstCredentialedTextChatRowAsync(CancellationToken ct)
    {
        var rows = await providerRegistry.ListByCategoryAsync(AiProviderCategory.TextChat, ct);
        return rows.FirstOrDefault(row => row.IsActive && !string.IsNullOrWhiteSpace(row.EncryptedApiKey));
    }

    private static string ResolveModel(string? requestedModel, string? providerDefaultModel, string providerName)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel)) return requestedModel;
        if (!string.IsNullOrWhiteSpace(providerDefaultModel)) return providerDefaultModel;
        return providerName.Equals("mock", StringComparison.OrdinalIgnoreCase) ? "mock" : string.Empty;
    }

    private static List<AiToolCall> ParseToolCalls(string toolCallsJson)
    {
        using var doc = JsonDocument.Parse(toolCallsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("ToolCallsJson must be an array.");
        }

        var output = new List<AiToolCall>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(element, "Id") ?? ReadString(element, "id") ?? Guid.NewGuid().ToString("N");
            var toolCode = ReadString(element, "ToolCode")
                           ?? ReadString(element, "toolCode")
                           ?? ReadString(element, "Name")
                           ?? ReadString(element, "name")
                           ?? ReadNestedString(element, "function", "name")
                           ?? string.Empty;
            if (string.IsNullOrWhiteSpace(toolCode))
            {
                throw new JsonException("ToolCallsJson contains a tool call without a tool name.");
            }

            var args = ReadString(element, "ArgsJson")
                       ?? ReadString(element, "argsJson")
                       ?? ReadString(element, "Arguments")
                       ?? ReadString(element, "arguments")
                       ?? ReadNestedString(element, "function", "arguments")
                       ?? "{}";
            output.Add(new AiToolCall
            {
                Id = id,
                ToolCode = toolCode,
                ArgsJson = string.IsNullOrWhiteSpace(args) ? "{}" : args,
            });
        }
        return output;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ReadNestedString(JsonElement element, string objectPropertyName, string nestedPropertyName)
        => element.TryGetProperty(objectPropertyName, out var nested)
           && nested.ValueKind == JsonValueKind.Object
           && nested.TryGetProperty(nestedPropertyName, out var property)
           && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool IsLegacyOpenAiModel(string? model)
        => !string.IsNullOrWhiteSpace(model)
           && (model.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase)
               || model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase));

    private sealed record ResolvedAssistantProvider(IAiModelProvider Provider, string ProviderCode, string Model);
}

