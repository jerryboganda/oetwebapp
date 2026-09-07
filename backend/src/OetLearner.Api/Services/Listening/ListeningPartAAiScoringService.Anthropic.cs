using System.Text;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.AiTools;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part A advisory review - Anthropic transport half.
//
// Split out of ListeningPartAAiScoringService.cs so both halves stay under the
// repo 500-line rule (same partial convention as AdminService.ContentAdmin.cs).
// This half owns provider resolution, the forced-tool request, HTTP outcome
// classification and response parsing. It never writes to the database and
// never persists a raw provider body.
// ═════════════════════════════════════════════════════════════════════════════

public sealed partial class ListeningPartAAiScoringService
{
    // Single source of truth: CoreAiProviderSeeder.AnthropicDefaultModel.
    private const string DefaultModel = CoreAiProviderSeeder.AnthropicDefaultModel;
    private const string ToolName = "emit_part_a_verdicts";

    // ── Claude call (forced tool) ───────────────────────────────────────────────

    private sealed record Provider(string BaseUrl, string Model, string ApiKey, AiProvider Row);

    private async Task<Provider?> ResolveProviderAsync(CancellationToken ct)
    {
        var row = await registry.FindByCodeAsync(AnthropicProviderCode, ct);
        if (row is null) return null;
        var apiKey = await registry.GetPlatformKeyAsync(AnthropicProviderCode, ct);
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var baseUrl = AnthropicProvider.NormalizeBaseUrl(row.BaseUrl);
        if (!string.IsNullOrWhiteSpace(baseUrl)
            && AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl) is not null)
        {
            return null;
        }
        var model = string.IsNullOrWhiteSpace(row.DefaultModel) ? DefaultModel : row.DefaultModel;
        return new Provider(baseUrl, model, apiKey, row);
    }

    private async Task<ProviderCallOutcome> CallClaudeVerdictsAsync(
        IReadOnlyList<GapItem> items, string learnerId, Provider provider,
        DirectAiOperationLease lease, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Judge each candidate gap answer for OET Listening Part A note-completion.");
        foreach (var grp in items.GroupBy(i => i.Context))
        {
            sb.AppendLine();
            sb.AppendLine("CONSULTATION NOTE (blanks shown as ____):");
            sb.AppendLine(string.IsNullOrWhiteSpace(grp.Key) ? "(note text unavailable)" : grp.Key);
            sb.AppendLine("GAPS:");
            foreach (var it in grp.OrderBy(i => i.Number))
            {
                var accepted = it.Accepted.Count > 0 ? " | also accepted: " + string.Join(", ", it.Accepted) : string.Empty;
                sb.AppendLine($"({it.Number}) candidate: \"{it.UserAnswer}\" | official answer: \"{it.Canonical}\"{accepted}");
                sb.AppendLine($"    approved rationale: {it.ApprovedRationale}");
            }
        }
        var userText = sb.ToString();

        var startedAt = clock.GetUtcNow();
        var usageContext = new AiUsageContext(
            UserId: learnerId,
            AuthAccountId: null,
            TenantId: null,
            FeatureCode: AiFeatureCodes.ListeningPartAScore,
            RulebookVersion: null,
            PromptTemplateId: ToolName,
            SystemPrompt: SystemPrompt,
            UserPrompt: userText,
            StartedAt: startedAt);
        int LatencyMs() => (int)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        var request = new AiProviderRequest
        {
            ProviderCode = AnthropicProviderCode,
            Model = provider.Model,
            SystemPrompt = SystemPrompt,
            UserPrompt = userText,
            MaxTokens = 4000,
            ApiKeyOverride = provider.ApiKey,
            BaseUrlOverride = string.IsNullOrWhiteSpace(provider.BaseUrl) ? null : provider.BaseUrl,
            Tools =
            [
                new AiToolDefinition(
                    ToolName,
                    ToolName,
                    "Emit one post-submit advisory review per provided gap number; never change the deterministic mark.",
                    AiToolCategory.Read,
                    ToolSchemaJson),
            ],
            ToolChoice = ToolName,
        };

        AiProviderCompletion completion;
        try
        {
            completion = await new AnthropicProvider(httpClientFactory, registry).CompleteAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await RecordFailureAsync(AiCallOutcome.Timeout, "anthropic_timeout",
                "Anthropic request timed out before response headers were read.");
            return ProviderCallOutcome.Terminal("anthropic_timeout", ListeningPartAAiSkipReasons.IndeterminateTimeout);
        }
        catch (AiProviderBodyReadException ex)
        {
            await RecordFailureAsync(AiCallOutcome.Timeout, "anthropic_body_read",
                $"Anthropic 2xx response body could not be read ({ex.InnerException?.GetType().Name ?? ex.GetType().Name}).");
            logger.LogError(ex, "Part A AI advisory review: Anthropic response body read failed after a 2xx.");
            return ProviderCallOutcome.Terminal("anthropic_body_read", ListeningPartAAiSkipReasons.IndeterminateTimeout);
        }
        catch (AiProviderHttpException ex)
        {
            var errorClass = $"http_{ex.StatusCode}";
            await RecordFailureAsync(AiCallOutcome.ProviderError, errorClass,
                $"Anthropic returned HTTP {ex.StatusCode} for {AiFeatureCodes.ListeningPartAScore}.");
            var terminalReason = ListeningPartAAiRetryPolicy.TerminalSkipReasonForStatus(ex.StatusCode);
            return terminalReason is null
                ? ProviderCallOutcome.Retry(errorClass, ex.RetryAfter)
                : ProviderCallOutcome.Terminal(errorClass, terminalReason);
        }
        catch (Exception ex) when (IsSafePreSendFailure(ex))
        {
            await RecordFailureAsync(AiCallOutcome.ProviderError, "anthropic_connect",
                $"Anthropic connection failure before send ({ex.GetType().Name}).");
            logger.LogWarning(ex, "Part A AI advisory review: Anthropic pre-send connection failure.");
            return ProviderCallOutcome.Retry("anthropic_connect", null);
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(AiCallOutcome.ProviderError, "anthropic_indeterminate",
                $"Anthropic transport failure with an unknown send outcome ({ex.GetType().Name}).");
            logger.LogError(ex, "Part A AI advisory review: Anthropic transport failure with an indeterminate outcome.");
            return ProviderCallOutcome.Terminal("anthropic_indeterminate", ListeningPartAAiSkipReasons.IndeterminateTimeout);
        }

        var toolCall = completion.ToolCalls?.FirstOrDefault(call =>
            string.Equals(call.ToolCode, ToolName, StringComparison.Ordinal));
        if (toolCall is null || string.IsNullOrWhiteSpace(toolCall.ArgsJson))
        {
            await RecordFailureAsync(AiCallOutcome.ProviderError, "no_tool_use",
                "Anthropic returned no verdicts tool_use block.");
            return ProviderCallOutcome.Terminal("no_tool_use", ListeningPartAAiSkipReasons.NoMatchingVerdicts);
        }

        JsonDocument argsDoc;
        try
        {
            argsDoc = JsonDocument.Parse(toolCall.ArgsJson);
        }
        catch (JsonException)
        {
            await RecordFailureAsync(AiCallOutcome.ProviderError, "invalid_json",
                "Anthropic returned a 2xx body that was not valid JSON.");
            return ProviderCallOutcome.Terminal("invalid_json", ListeningPartAAiSkipReasons.NoMatchingVerdicts);
        }

        using (argsDoc)
        {
            var usage = completion.Usage;
            var (cost, cacheBreakdown) = await ComputeCostAsync(
                provider, usage, usage?.CacheWriteTokens ?? 0, usage?.CacheReadTokens ?? 0);
            var usageRecordId = await usageRecorder.RecordSuccessAsync(
                usageContext, AnthropicProviderCode, provider.Model, usage,
                LatencyMs(), AiFeatureCodes.ListeningPartAScore, cost, CancellationToken.None,
                cacheTokens: cacheBreakdown,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            return ProviderCallOutcome.Succeeded(ParseVerdicts(argsDoc.RootElement), usageRecordId);
        }

        Task RecordFailureAsync(AiCallOutcome outcome, string errorClass, string sanitizedMessage)
            // CancellationToken.None on purpose: every caller of this local
            // helper runs AFTER a send attempt, so the physical call may already
            // have been accepted and billed. A cancelled caller must never cost
            // us the durable record of that spend.
            => usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, provider.Model, outcome,
                errorClass, sanitizedMessage, LatencyMs(), AiFeatureCodes.ListeningPartAScore,
                CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
    }

    /// <summary>
    /// True only for transport failures the runtime raises <b>before</b> any byte
    /// of the request can reach the provider, so no call can have been accepted
    /// or billed and a retry inside the attempt cap carries no duplicate-charge
    /// risk:
    /// <list type="bullet">
    ///   <item><c>NameResolutionError</c> — DNS never resolved, so no socket was
    ///   ever opened to the provider.</item>
    ///   <item><c>ProxyTunnelError</c> — the forward proxy refused to establish
    ///   the CONNECT tunnel, so the request was never tunnelled.</item>
    /// </list>
    /// Everything else is treated as indeterminate and terminal. In particular
    /// <c>ConnectionError</c> and <c>SecureConnectionError</c> are deliberately
    /// NOT in this list: .NET raises both for failures that can occur at any
    /// point in the connection's life — a mid-flight reset after the request was
    /// written, or a TLS failure on a renegotiation/HTTP2 stream — and
    /// <c>HttpRequestError</c> does not tell us which. Until the runtime
    /// guarantees the request had not left the process, the only safe reading is
    /// "may already have been accepted and billed", so those failures close the
    /// answer as <c>indeterminate_timeout</c> rather than buying a possible
    /// second charge for the same evidence.
    /// </summary>
    private static bool IsSafePreSendFailure(Exception ex)
        => ex is HttpRequestException
        {
            HttpRequestError: HttpRequestError.NameResolutionError
                or HttpRequestError.ProxyTunnelError,
        };

    private static List<Verdict> ParseVerdicts(JsonElement input)
    {
        var result = new List<Verdict>();
        if (!input.TryGetProperty("verdicts", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var v in arr.EnumerateArray())
        {
            var number = v.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : -1;
            if (number < 0) continue;
            var verdict = v.TryGetProperty("verdict", out var vd) ? vd.GetString() : null;
            var rationale = v.TryGetProperty("rationale", out var r) ? r.GetString() : null;
            result.Add(new Verdict(number, verdict, rationale));
        }
        return result;
    }

    /// <summary>UBAG route for the Part A verdicts call. Same gap evidence,
    /// forced emit_part_a_verdicts tool definition plus response_format
    /// json_object through the registry (forced-tool emulation surfaces the
    /// JSON as ArgsJson). Response parsing, retry classification, and cost
    /// accounting mirror the Anthropic path; usage is recorded against ubag.
    /// The caller owns lease reconciliation and advisory persistence, so this
    /// method only classifies the physical invocation.</summary>
    private async Task<ProviderCallOutcome> CallUbagVerdictsAsync(
        IReadOnlyList<GapItem> items, string learnerId, string? routeModel,
        DirectAiOperationLease lease, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Judge each candidate gap answer for OET Listening Part A note-completion.");
        foreach (var grp in items.GroupBy(i => i.Context))
        {
            sb.AppendLine();
            sb.AppendLine("CONSULTATION NOTE (blanks shown as ____):");
            sb.AppendLine(string.IsNullOrWhiteSpace(grp.Key) ? "(note text unavailable)" : grp.Key);
            sb.AppendLine("GAPS:");
            foreach (var it in grp.OrderBy(i => i.Number))
            {
                var accepted = it.Accepted.Count > 0 ? " | also accepted: " + string.Join(", ", it.Accepted) : string.Empty;
                sb.AppendLine($"({it.Number}) candidate: \"{it.UserAnswer}\" | official answer: \"{it.Canonical}\"{accepted}");
                sb.AppendLine($"    approved rationale: {it.ApprovedRationale}");
            }
        }
        var userText = sb.ToString();

        var startedAt = clock.GetUtcNow();
        var usageContext = new AiUsageContext(
            UserId: learnerId,
            AuthAccountId: null,
            TenantId: null,
            FeatureCode: AiFeatureCodes.ListeningPartAScore,
            RulebookVersion: null,
            PromptTemplateId: ToolName,
            SystemPrompt: SystemPrompt,
            UserPrompt: userText,
            StartedAt: startedAt);
        int LatencyMs() => (int)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        async Task<ProviderCallOutcome> FailAsync(AiProvider? resolvedRow, string? resolvedModel, string errorClass, string message)
        {
            await usageRecorder.RecordFailureAsync(
                usageContext, UbagProviderCode, resolvedModel ?? "chatgpt_web", AiCallOutcome.ProviderError,
                errorClass, message, LatencyMs(), AiFeatureCodes.ListeningPartAScore,
                CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            return ProviderCallOutcome.Terminal(errorClass, ListeningPartAAiSkipReasons.IndeterminateTimeout);
        }

        var row = await registry.FindByCodeAsync(UbagProviderCode, ct);
        if (row is null)
        {
            return await FailAsync(null, null, "ubag_unconfigured", "UBAG provider is not registered.");
        }
        var apiKey = await registry.GetPlatformKeyAsync(UbagProviderCode, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return await FailAsync(row, null, "ubag_unconfigured", "UBAG provider key is missing.");
        }
        var baseUrl = string.IsNullOrWhiteSpace(row.BaseUrl) ? null : row.BaseUrl.Trim().TrimEnd('/');
        if (baseUrl is not null
            && AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl) is not null)
        {
            return await FailAsync(row, null, "ubag_unconfigured", "UBAG provider endpoint is not allowed.");
        }
        var model = !string.IsNullOrWhiteSpace(routeModel)
            ? routeModel.Trim()
            : string.IsNullOrWhiteSpace(row.DefaultModel) ? "chatgpt_web" : row.DefaultModel;

        var request = new AiProviderRequest
        {
            ProviderCode = UbagProviderCode,
            Model = model,
            SystemPrompt = SystemPrompt,
            UserPrompt = userText,
            MaxTokens = 4000,
            ApiKeyOverride = apiKey,
            BaseUrlOverride = baseUrl,
            ResponseFormatJson = "json_object",
            Tools =
            [
                new AiToolDefinition(
                    ToolName,
                    ToolName,
                    "Emit one post-submit advisory review per provided gap number; never change the deterministic mark.",
                    AiToolCategory.Read,
                    ToolSchemaJson),
            ],
            ToolChoice = ToolName,
        };

        AiProviderCompletion completion;
        try
        {
            completion = await new RegistryBackedProvider(httpClientFactory, registry, providerOptions).CompleteAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await RecordFailureAsync(AiCallOutcome.Timeout, "ubag_timeout",
                "UBAG request timed out before response headers were read.");
            return ProviderCallOutcome.Terminal("ubag_timeout", ListeningPartAAiSkipReasons.IndeterminateTimeout);
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(AiCallOutcome.ProviderError, "ubag_transport",
                $"UBAG transport failure ({ex.GetType().Name}).");
            logger.LogWarning(ex, "Part A AI advisory review: UBAG transport failure.");
            return ProviderCallOutcome.Terminal("ubag_transport", ListeningPartAAiSkipReasons.IndeterminateTimeout);
        }

        var toolCall = completion.ToolCalls?.FirstOrDefault(call =>
            string.Equals(call.ToolCode, ToolName, StringComparison.Ordinal));
        if (toolCall is null || string.IsNullOrWhiteSpace(toolCall.ArgsJson))
        {
            await RecordFailureAsync(AiCallOutcome.ProviderError, "no_tool_use",
                "UBAG returned no verdicts JSON block.");
            return ProviderCallOutcome.Terminal("no_tool_use", ListeningPartAAiSkipReasons.NoMatchingVerdicts);
        }

        JsonDocument argsDoc;
        try
        {
            argsDoc = JsonDocument.Parse(toolCall.ArgsJson);
        }
        catch (JsonException)
        {
            await RecordFailureAsync(AiCallOutcome.ProviderError, "invalid_json",
                "UBAG returned a 2xx body that was not valid JSON.");
            return ProviderCallOutcome.Terminal("invalid_json", ListeningPartAAiSkipReasons.NoMatchingVerdicts);
        }

        using (argsDoc)
        {
            var usage = completion.Usage;
            var cost = usage is null
                ? 0m
                : row.PricePer1kPromptTokens * usage.PromptTokens / 1000m
                  + row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
            var usageRecordId = await usageRecorder.RecordSuccessAsync(
                usageContext, UbagProviderCode, model, usage,
                LatencyMs(), AiFeatureCodes.ListeningPartAScore, cost, CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
            return ProviderCallOutcome.Succeeded(ParseVerdicts(argsDoc.RootElement), usageRecordId);
        }

        Task RecordFailureAsync(AiCallOutcome outcome, string errorClass, string sanitizedMessage)
            => usageRecorder.RecordFailureAsync(
                usageContext, UbagProviderCode, model, outcome,
                errorClass, sanitizedMessage, LatencyMs(), AiFeatureCodes.ListeningPartAScore,
                CancellationToken.None,
                operationId: lease.OperationId, attemptNumber: lease.AttemptNumber);
    }

    /// <summary>
    /// Resolves an effective-dated <see cref="AiPricingResolution"/> when a
    /// resolver is wired (W2+) and prices normal + cache tokens without
    /// double-counting; falls back to the pre-W2 flat <see cref="AiProvider"/>
    /// rate card (no cache-aware pricing) when no resolver is configured, no
    /// effective row exists yet, <b>or the resolver itself fails</b>, so cost is
    /// never silently zeroed by a missing migration/seed — and, critically, a
    /// pricing outage can never relabel a paid, successful 2xx as a provider
    /// failure. This runs only after the provider has already answered, so it
    /// deliberately ignores caller cancellation: the call is spent either way.
    /// </summary>
    private async Task<(decimal CostUsd, AiCacheTokenBreakdown? CacheTokens)> ComputeCostAsync(
        Provider provider, AiUsage? usage, int cacheWriteTokens, int cacheReadTokens)
    {
        AiPricingResolution? pricing = null;
        if (pricingResolver is not null)
        {
            try
            {
                pricing = await pricingResolver.ResolveAsync(
                    AnthropicProviderCode, provider.Model, clock.GetUtcNow(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Part A AI advisory review: effective pricing lookup failed for {Model}; falling back to the provider rate card.",
                    provider.Model);
            }
        }

        if (pricing is null)
        {
            var legacyCost = usage is null
                ? 0m
                : provider.Row.PricePer1kPromptTokens * usage.PromptTokens / 1000m
                  + provider.Row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
            return (legacyCost, null);
        }

        var cost = pricing.ComputeCostUsd(
            normalInputTokens: usage?.PromptTokens ?? 0,
            normalOutputTokens: usage?.CompletionTokens ?? 0,
            cacheWriteTokens: cacheWriteTokens,
            cacheReadTokens: cacheReadTokens);

        var billedClass = cacheWriteTokens > 0 ? "cache_write" : cacheReadTokens > 0 ? "cache_read" : "normal";
        var breakdown = new AiCacheTokenBreakdown(cacheWriteTokens, cacheReadTokens, pricing.PricingVersion, cost, billedClass);
        return (cost, breakdown);
    }

    private const string SystemPrompt = """
You provide a post-submit advisory review of OET (Occupational English Test) Listening Part A
note-completion answers. The server's deterministic mark is authoritative and is never changed by
this call. You must not award partial credit, approve a synonym, excuse a misspelling, or infer an
accepted variant. Do not claim to provide an official OET result.

For each numbered gap you receive: the consultation note for context, the candidate's typed answer,
the canonical answer, explicitly authorised variants, and the author-approved rationale. Use only
that stored evidence. If the evidence is incomplete, say so in the rationale and mark the advisory
verdict "incorrect"; do not invent an explanation.

Call the emit_part_a_verdicts tool exactly once with one advisory verdict per gap:
  - "correct": only when the stored deterministic answer/variant evidence shows an exact match.
  - "incorrect": when it does not show an exact match, the answer is blank, or evidence is missing.
  - "acceptable" is forbidden and must never be emitted.

Give a concise evidence-based rationale per gap and return EXACTLY one verdict object per gap number
you were given. This advisory output is tutor-facing only and must not be presented as a score.
""";

    private const string ToolSchemaJson = """
{
  "type": "object",
  "properties": {
    "verdicts": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "number": { "type": "integer" },
          "verdict": { "type": "string", "enum": ["correct", "incorrect"] },
          "rationale": { "type": "string" }
        },
        "required": ["number", "verdict"]
      }
    }
  },
  "required": ["verdicts"]
}
""";
}
