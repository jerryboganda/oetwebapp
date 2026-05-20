using System.Runtime.CompilerServices;
using System.Text.Json;
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
    IAiUsageRecorder? usageRecorder,
    ILogger<AiAssistantGateway> logger) : IAiAssistantGateway
{
    public async IAsyncEnumerable<LlmStreamChunk> StreamCompleteWithToolsAsync(
        string featureCode,
        string? userId,
        List<LlmMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        string? modelOverride,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Resolve provider + model via feature routing
        var route = await routeResolver.ResolveAsync(featureCode, ct);
        var providerCode = route?.ProviderCode ?? "openai";
        var model = modelOverride ?? route?.Model ?? "gpt-4o";

        // Find the provider implementation by name
        var provider = providers.FirstOrDefault(p =>
            p.Name.Equals(providerCode, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            // Fallback to first available
            provider = providers.FirstOrDefault();
        }

        if (provider == null)
        {
            yield return new LlmTextChunk("No AI provider is configured. Please contact an administrator.");
            yield break;
        }

        // Resolve platform API key for the provider
        var apiKey = await providerRegistry.GetPlatformKeyAsync(providerCode, ct);

        // Convert our message format to the provider's AiChatMessage format
        var chatMessages = messages.Select(m =>
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
                    ToolCalls = JsonSerializer.Deserialize<List<AiToolCall>>(m.ToolCallsJson)
                        ?? new List<AiToolCall>(),
                };
            }
            return msg;
        }).ToList();

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
            ApiKeyOverride = apiKey,
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
        catch (Exception ex)
        {
            logger.LogError(ex, "AI Assistant provider call failed for {FeatureCode}", featureCode);
            errorMessage = "I encountered an error communicating with the AI service. Please try again.";
        }

        if (errorMessage != null)
        {
            yield return new LlmTextChunk(errorMessage);
            yield break;
        }

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
                    StartedAt: DateTimeOffset.UtcNow);

                await usageRecorder.RecordSuccessAsync(
                    usageCtx,
                    providerCode,
                    model,
                    Domain.AiKeySource.Platform,
                    completion!.Usage,
                    latencyMs: 0,
                    retryCount: 0,
                    policyTrace: null,
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to record AI usage for assistant call");
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
}

