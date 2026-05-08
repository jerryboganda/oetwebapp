using Azure;
using Azure.AI.Inference;
using Azure.Core.Pipeline;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// GitHub Copilot / GitHub Models provider. Calls the chat-completions
/// endpoint at the registered base URL (default
/// <c>https://models.github.ai/inference</c>) using the official
/// <see cref="ChatCompletionsClient"/> from the
/// <c>Azure.AI.Inference</c> NuGet package, with a GitHub PAT (scope
/// <c>models:read</c>) supplied via <see cref="AzureKeyCredential"/>.
///
/// Model ids use the <c>{publisher}/{model}</c> form (e.g.
/// <c>openai/gpt-4o-mini</c>, <c>meta/llama-3.1-70b-instruct</c>).
///
/// This provider does NOT touch grounding or usage recording — both live
/// in <see cref="AiGatewayService"/> one layer above. The provider only
/// owns the wire format. Phase 1: non-streaming, no tool calling.
///
/// Auth model: server-side platform PAT only (or admin BYOK pasted
/// through <see cref="AiProviderRequest.ApiKeyOverride"/>). Phase 2 will
/// extend this to a multi-account pool with auto-failover; the
/// credential-resolution helper is kept private here so that swap is
/// localised to this file.
/// </summary>
public sealed class CopilotAiModelProvider : IAiModelProvider
{
    private readonly IAiProviderRegistry _registry;
    private readonly IAiProviderAccountRegistry? _accountRegistry;
    private readonly AzureAIInferenceClientOptions? _clientOptionsOverride;

    public CopilotAiModelProvider(
        IAiProviderRegistry registry,
        IAiProviderAccountRegistry? accountRegistry = null)
    {
        _registry = registry;
        _accountRegistry = accountRegistry;
    }

    /// <summary>Test-only constructor that lets tests inject a custom
    /// <see cref="HttpPipelineTransport"/> (typically a stubbed
    /// <see cref="HttpClientTransport"/>) so no network call is made.
    /// Production DI uses the constructor above.</summary>
    public CopilotAiModelProvider(
        IAiProviderRegistry registry,
        AzureAIInferenceClientOptions clientOptions,
        IAiProviderAccountRegistry? accountRegistry = null)
    {
        _registry = registry;
        _accountRegistry = accountRegistry;
        _clientOptionsOverride = clientOptions;
    }

    public string Name => "copilot";

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        // Phase 2: when a multi-account pool exists, walk it with failover.
        // When the pool is empty (or no override) fall back to the
        // single-row credentials on the AiProvider row.
        if (_accountRegistry is not null
            && string.IsNullOrWhiteSpace(request.ApiKeyOverride))
        {
            var failover = await TryCompleteWithAccountFailoverAsync(request, ct);
            if (failover is not null) return failover;
            // No account in the pool — drop through to single-row mode so
            // existing single-PAT setups continue to work unchanged.
        }

        var (baseUrl, apiKey, defaultModel) = await ResolveCredentialsAsync(request, ct);
        return await CallOnceAsync(baseUrl, apiKey, defaultModel, request, ct);
    }

    /// <summary>
    /// Walk <see cref="IAiProviderAccountRegistry"/> in priority order,
    /// retrying on rate-limit / unauthorized failures against the next
    /// account. Returns null when no account is registered (caller falls
    /// back to single-row mode); throws when accounts exist but every
    /// one has been tried and failed (last exception wins, with all prior
    /// account ids in its message so the gateway's PolicyTrace can render
    /// the chain).
    /// </summary>
    private async Task<AiProviderCompletion?> TryCompleteWithAccountFailoverAsync(
        AiProviderRequest request, CancellationToken ct)
    {
        var skip = new HashSet<string>(StringComparer.Ordinal);
        var trail = new List<string>();
        Exception? last = null;
        string? lastAccountId = null;

        // Hard ceiling — protects against pathological pool sizes.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var slot = await _accountRegistry!.PickAndReserveAsync("copilot", skip, ct);
            if (slot is null)
            {
                if (attempt == 0) return null; // pool empty → fall back
                break; // pool exhausted mid-failover
            }

            var (baseUrl, defaultModel) = await ResolveMetadataAsync(request, ct);

            try
            {
                var completion = await CallOnceAsync(baseUrl, slot.ApiKey, defaultModel, request, ct);
                await _accountRegistry.RecordOutcomeAsync(
                    slot.AccountId, AiProviderAccountOutcome.Success, null, ct);
                trail.Add($"{slot.Label}:success");
                return new AiProviderCompletion
                {
                    Text = completion.Text,
                    Usage = completion.Usage,
                    AccountId = slot.AccountId,
                    FailoverTrace = trail.Count > 1 ? string.Join(" → ", trail) : null,
                };
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(" 429"))
            {
                await _accountRegistry.RecordOutcomeAsync(
                    slot.AccountId, AiProviderAccountOutcome.RateLimited,
                    quarantineFor: TimeSpan.FromMinutes(15), ct);
                skip.Add(slot.AccountId);
                trail.Add($"{slot.Label}:429");
                last = ex;
                lastAccountId = slot.AccountId;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(" 401") || ex.Message.Contains(" 403"))
            {
                await _accountRegistry.RecordOutcomeAsync(
                    slot.AccountId, AiProviderAccountOutcome.Unauthorized,
                    quarantineFor: null, ct);
                skip.Add(slot.AccountId);
                trail.Add($"{slot.Label}:auth");
                last = ex;
                lastAccountId = slot.AccountId;
            }
            // Other errors: the gateway's outer retry policy decides what to
            // do; we surface the exception so it can classify and bail.
        }

        var trace = string.Join(" → ", trail);
        throw new AiProviderFailoverException(
            $"GitHub Copilot multi-account failover exhausted [{trace}]. " +
            (last?.Message ?? "no further detail"),
            failoverTrace: trace,
            lastAccountId: lastAccountId);
    }

    private async Task<AiProviderCompletion> CallOnceAsync(
        string baseUrl, string apiKey, string defaultModel,
        AiProviderRequest request, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? defaultModel : request.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(
                "GitHub Copilot provider requires an explicit model (e.g. openai/gpt-4o-mini). " +
                "Set DefaultModel on the AiProviders row with Code=\"copilot\" or pass request.Model.");

        var endpoint = new Uri(baseUrl);
        var credential = new AzureKeyCredential(apiKey);
        var client = _clientOptionsOverride is null
            ? new ChatCompletionsClient(endpoint, credential)
            : new ChatCompletionsClient(endpoint, credential, _clientOptionsOverride);

        var options = new ChatCompletionsOptions
        {
            Messages =
            {
                new ChatRequestSystemMessage(request.SystemPrompt),
                new ChatRequestUserMessage(request.UserPrompt),
            },
            Model = model,
            Temperature = (float)request.Temperature,
            MaxTokens = request.MaxTokens ?? 4096,
        };

        try
        {
            Response<ChatCompletions> response = await client.CompleteAsync(options, ct);
            var completions = response.Value;

            var text = completions.Content ?? string.Empty;

            var usage = completions.Usage is null
                ? null
                : new AiUsage
                {
                    PromptTokens = completions.Usage.PromptTokens,
                    CompletionTokens = completions.Usage.CompletionTokens,
                };

            return new AiProviderCompletion { Text = text, Usage = usage };
        }
        catch (RequestFailedException ex)
        {
            // The gateway's outcome classifier maps exception messages
            // containing the HTTP status code to provider_auth / rate_limited
            // / provider_error buckets, so include the status code in the
            // message text. ex.ErrorCode comes from the GitHub error envelope
            // (e.g. "unauthorized", "rate_limited") when present.
            var code = string.IsNullOrEmpty(ex.ErrorCode) ? "" : $" {ex.ErrorCode}";
            throw new InvalidOperationException(
                $"GitHub Copilot AI provider call failed: {ex.Status}{code}. {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Resolve <c>(baseUrl, defaultModel)</c> only, without touching the
    /// platform PAT. Used by the failover loop, which already picked a
    /// per-account PAT atomically and must not also require the legacy
    /// single-row key on the AiProvider record.
    /// </summary>
    private async Task<(string baseUrl, string defaultModel)> ResolveMetadataAsync(
        AiProviderRequest request, CancellationToken ct)
    {
        var registered = (await _registry.ListActiveAsync(ct))
            .FirstOrDefault(p => p.Dialect == AiProviderDialect.Copilot);

        var baseUrl = !string.IsNullOrWhiteSpace(request.BaseUrlOverride)
            ? request.BaseUrlOverride!
            : registered?.BaseUrl
              ?? throw new InvalidOperationException(
                  "GitHub Copilot provider is not registered. " +
                  "Add a row in /admin/ai-providers with Code=\"copilot\" and Dialect=Copilot.");

        return (baseUrl, registered?.DefaultModel ?? string.Empty);
    }

    /// <summary>
    /// Resolve <c>(baseUrl, apiKey, defaultModel)</c> from the
    /// per-request override first, falling back to the active
    /// <c>copilot</c>-coded provider row. Filters by
    /// <see cref="AiProviderDialect.Copilot"/> so an OpenAI-compatible row
    /// at higher failover priority is not accidentally fed Copilot's
    /// request shape.
    ///
    /// When called from the failover loop, <paramref name="ignoreRequestKeyOverride"/>
    /// is true so the loop's PAT (already picked atomically) wins over
    /// any caller override.
    /// </summary>
    private async Task<(string baseUrl, string apiKey, string defaultModel)> ResolveCredentialsAsync(
        AiProviderRequest request, CancellationToken ct, bool ignoreRequestKeyOverride = false)
    {
        var registered = (await _registry.ListActiveAsync(ct))
            .FirstOrDefault(p => p.Dialect == AiProviderDialect.Copilot);

        var baseUrl = !string.IsNullOrWhiteSpace(request.BaseUrlOverride)
            ? request.BaseUrlOverride!
            : registered?.BaseUrl
              ?? throw new InvalidOperationException(
                  "GitHub Copilot provider is not registered. " +
                  "Add a row in /admin/ai-providers with Code=\"copilot\" and Dialect=Copilot.");

        var apiKey = (!ignoreRequestKeyOverride && !string.IsNullOrWhiteSpace(request.ApiKeyOverride))
            ? request.ApiKeyOverride!
            : (registered is null
                ? throw new InvalidOperationException(
                    "GitHub Copilot provider is not registered. " +
                    "Add a row in /admin/ai-providers with Code=\"copilot\" and Dialect=Copilot.")
                : await _registry.GetPlatformKeyAsync(registered.Code, ct))
              ?? throw new InvalidOperationException(
                  "Platform API key missing for GitHub Copilot. " +
                  "Re-save the row in /admin/ai-providers with a fresh PAT (scope: models:read).");

        var defaultModel = registered?.DefaultModel ?? string.Empty;
        return (baseUrl, apiKey, defaultModel);
    }
}
