using System.Text;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.AiTools;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// ============================================================================
/// AI Gateway — SINGLE ENTRY POINT for every AI call in the .NET backend
/// ============================================================================
///
/// MISSION CRITICAL. Every AI invocation — OpenAI, Anthropic, Google Gemini,
/// any future provider — MUST flow through this gateway. The gateway refuses
/// to hand a request to a model unless the system prompt was assembled by
/// <see cref="RulebookPromptBuilder"/> and therefore embeds:
///
///   1. The active OET rulebook (Writing or Speaking, per profession).
///   2. The canonical OET scoring rules (from OetScoring), including the
///      country-aware Writing pass mark.
///   3. Strict guardrails ("do not invent rules", "advisory output only", etc.).
///   4. A structured reply-format contract for the task at hand.
///
/// Any attempt to send raw, unbounded prompts to a model raises
/// <see cref="PromptNotGroundedException"/>. This is the structural defence
/// that keeps the platform consistent, defensible, and aligned with Dr.
/// Hesham's authoritative content.
///
/// The gateway itself is provider-agnostic: provider implementations
/// (OpenAI, Anthropic, Gemini, …) implement <see cref="IAiModelProvider"/>
/// and are selected based on the configured AIConfigVersion in the admin
/// CMS. Replacing providers never touches this grounding code.
/// ============================================================================
/// </summary>
public interface IAiGatewayService
{
    Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default);

    AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context);
}

public sealed class AiGatewayService(
    IRulebookLoader loader,
    IEnumerable<IAiModelProvider> providers,
    IAiUsageRecorder? usageRecorder = null,
    IAiQuotaService? quotaService = null,
    IAiCredentialResolver? credentialResolver = null,
    IAiProviderRegistry? providerRegistry = null,
    IAiFeatureRouteResolver? featureRouteResolver = null,
    IAiToolRegistry? toolRegistry = null,
    IAiToolInvoker? toolInvoker = null,
    Microsoft.Extensions.Options.IOptions<AiToolOptions>? toolOptions = null)
    : IAiGatewayService
{
    private readonly RulebookPromptBuilder _promptBuilder = new(loader);

    public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
        => _promptBuilder.Build(context);

    public async Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var featureCode = string.IsNullOrWhiteSpace(request.FeatureCode)
            ? AiFeatureCodes.Unclassified
            : request.FeatureCode!;

        // ── Grounding invariant: physically refuse ungrounded prompts ────────
        // The throw still happens (contract preserved), but we also record
        // the refusal so the admin explorer can surface "ungrounded attempts"
        // as a signal of a bug or an unsafe caller.
        if (request.Prompt is null)
        {
            await RecordRefusalAsync(request, featureCode, stopwatch, startedAt,
                errorCode: "ungrounded",
                errorMessage: "AiGatewayRequest.Prompt is null.",
                ct);
            throw new PromptNotGroundedException("AiGatewayRequest.Prompt is null. Always build a prompt via BuildGroundedPrompt first.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt.SystemPrompt))
        {
            await RecordRefusalAsync(request, featureCode, stopwatch, startedAt,
                errorCode: "ungrounded",
                errorMessage: "SystemPrompt is empty.",
                ct);
            throw new PromptNotGroundedException("SystemPrompt is empty. The gateway refuses to call a model without rulebook grounding.");
        }

        if (!request.Prompt.SystemPrompt.Contains("OET AI — Rulebook-Grounded System Prompt", StringComparison.Ordinal))
        {
            await RecordRefusalAsync(request, featureCode, stopwatch, startedAt,
                errorCode: "ungrounded",
                errorMessage: "Missing rulebook grounding header.",
                ct);
            throw new PromptNotGroundedException(
                "SystemPrompt does not carry the rulebook grounding header. Build it via AiGatewayService.BuildGroundedPrompt.");
        }

        // ── Provider selection ───────────────────────────────────────────────
        IAiModelProvider? provider = null;
        string? routedModel = null;
        string? selectedProviderCode = null;
        string? selectedProviderDefaultModel = null;
        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            provider = providers.FirstOrDefault(p => string.Equals(p.Name, request.Provider, StringComparison.OrdinalIgnoreCase));
            selectedProviderCode = request.Provider;
        }

        // Phase 7: per-feature override. Consulted only when the caller did
        // not pin a provider — explicit pins always win so feature codes can
        // still be routed ad-hoc from tests and ops.
        if (provider is null && featureRouteResolver is not null && providerRegistry is not null)
        {
            try
            {
                var route = await featureRouteResolver.ResolveAsync(featureCode, ct);
                if (route is not null)
                {
                    var routeRow = await providerRegistry.FindByCodeAsync(route.ProviderCode, ct);
                    if (routeRow is not null)
                    {
                        var preferredName = ProviderNameForDialect(routeRow.Dialect);
                        if (preferredName is not null)
                        {
                            var routedProvider = providers.FirstOrDefault(p => string.Equals(p.Name, preferredName, StringComparison.OrdinalIgnoreCase));
                            if (routedProvider is not null)
                            {
                                provider = routedProvider;
                                selectedProviderCode = routeRow.Code;
                                selectedProviderDefaultModel = routeRow.DefaultModel;
                                routedModel = string.IsNullOrWhiteSpace(route.Model) ? routeRow.DefaultModel : route.Model;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Route resolution failure must not block the call — fall back
                // to the registry-default path immediately below.
            }
        }

        // No explicit pin → consult the text-chat provider registry to honor
        // the active highest-priority row's dialect. Voice/OCR rows share the
        // registry but cannot service grounded chat completions.
        if (provider is null && providerRegistry is not null)
        {
            try
            {
                var topRow = (await providerRegistry.ListByCategoryAsync(AiProviderCategory.TextChat, ct))
                    .Where(row => row.IsActive && row.Category == AiProviderCategory.TextChat)
                    .OrderBy(row => row.FailoverPriority)
                    .FirstOrDefault();
                if (topRow is not null)
                {
                    var preferredName = ProviderNameForDialect(topRow.Dialect);
                    if (preferredName is not null)
                    {
                        provider = providers.FirstOrDefault(p => string.Equals(p.Name, preferredName, StringComparison.OrdinalIgnoreCase));
                        if (provider is not null)
                        {
                            selectedProviderCode = topRow.Code;
                            selectedProviderDefaultModel = topRow.DefaultModel;
                        }
                    }
                }
            }
            catch
            {
                // Registry lookup failure must not block the call — fall back
                // to the legacy first-non-mock selection below.
            }
        }

        // If the caller did not pin a provider, prefer the first real provider
        // over the mock fallback so configured production deployments use the
        // actual model path by default.
        provider ??= providers.FirstOrDefault(p => !string.Equals(p.Name, "mock", StringComparison.OrdinalIgnoreCase));
        provider ??= providers.FirstOrDefault();
        if (provider is null)
        {
            await RecordRefusalAsync(request, featureCode, stopwatch, startedAt,
                errorCode: "no_provider",
                errorMessage: "No AI model provider registered.",
                ct);
            throw new InvalidOperationException("No AI model provider registered.");
        }

        var effectiveModel = !string.IsNullOrWhiteSpace(request.Model)
            ? request.Model
            : routedModel ?? selectedProviderDefaultModel ?? string.Empty;

        // ── Credential resolution (Slice 4) ──────────────────────────────────
        // Decide BYOK vs platform before quota enforcement — BYOK short-circuits
        // the quota check, so the resolver's decision must come first. Feature
        // routes are resolved above so BYOK matching is constrained to the
        // same provider the gateway will actually dispatch to.
        AiCredentialResolution? resolution = null;
        if (credentialResolver is not null)
        {
            resolution = await credentialResolver.ResolveAsync(
                request.UserId, featureCode, selectedProviderCode ?? request.Provider, ct);
        }
        var prospectiveKeySource = resolution?.KeySource ?? AiKeySource.Platform;

        // ── Quota / policy enforcement (Slice 2) ─────────────────────────────
        // Skipped when the gateway is constructed without a quota service
        // (backward compatibility + pure-rulebook tests).
        AiQuotaDecision? quotaDecision = null;
        if (quotaService is not null)
        {
            quotaDecision = await quotaService.TryReserveAsync(
                request.UserId, featureCode, prospectiveKeySource, ct);

            if (!quotaDecision.Allowed)
            {
                stopwatch.Stop();
                if (usageRecorder is not null)
                {
                    var ctx = BuildUsageContext(request, featureCode, startedAt, systemPrompt: null, userPrompt: null);
                    try
                    {
                        await usageRecorder.RecordFailureAsync(
                            ctx,
                            providerId: null,
                            model: null,
                            keySource: prospectiveKeySource,
                            outcome: AiCallOutcome.GatewayRefused,
                            errorCode: quotaDecision.ErrorCode ?? "quota_denied",
                            errorMessage: quotaDecision.ErrorMessage,
                            latencyMs: (int)stopwatch.ElapsedMilliseconds,
                            retryCount: 0,
                            policyTrace: quotaDecision.PolicyTrace,
                            ct: CancellationToken.None);
                    }
                    catch { /* fail-soft */ }
                }
                throw new AiQuotaDeniedException(
                    quotaDecision.ErrorCode ?? "quota_denied",
                    quotaDecision.ErrorMessage ?? "AI quota exceeded.");
            }
        }

        // ── Provider call + outcome recording ────────────────────────────────
        var userPrompt = BuildUserMessage(request);
        var context = BuildUsageContext(request, featureCode, startedAt, request.Prompt.SystemPrompt, userPrompt);

        // Phase 5 — Tool calling. When the feature is granted any tools, run
        // a bounded multi-turn loop: model → tool call(s) → tool result(s) →
        // model → … → final text. Each turn is one provider call but all
        // turns aggregate into ONE AiUsageRecord (token sum, max latency
        // across turns). The grounding header is verified once on turn 0;
        // once the system prompt is in the message list it is preserved.
        IReadOnlyList<AiToolDefinition> tools = Array.Empty<AiToolDefinition>();
        if (toolRegistry is not null && toolInvoker is not null)
        {
            try { tools = await toolRegistry.ResolveForFeatureAsync(featureCode, ct); }
            catch { tools = Array.Empty<AiToolDefinition>(); }
        }

        var maxTurns = Math.Max(1, toolOptions?.Value.MaxToolCallsPerCompletion ?? 4);
        var messages = new List<AiChatMessage>(capacity: 4 + maxTurns * 2)
        {
            new() { Role = "system", Content = request.Prompt.SystemPrompt },
            new() { Role = "user", Content = userPrompt },
        };

        AiProviderCompletion completion = null!;
        var aggregatePromptTokens = 0;
        var aggregateCompletionTokens = 0;
        string? loopTrace = null;
        var aiUsageRecordIdForTools = Guid.NewGuid().ToString("N"); // placeholder; tools record their own row

        try
        {
            for (var turn = 0; turn < maxTurns; turn++)
            {
                completion = await provider.CompleteAsync(new AiProviderRequest
                {
                    ProviderCode = selectedProviderCode,
                    Model = effectiveModel,
                    SystemPrompt = request.Prompt.SystemPrompt,
                    UserPrompt = userPrompt,
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxTokens,
                    ApiKeyOverride = resolution?.ApiKeyPlaintext,
                    BaseUrlOverride = resolution?.BaseUrlOverride,
                    Messages = messages,
                    Tools = tools.Count == 0 ? null : tools,
                    ToolChoice = tools.Count == 0 ? null : "auto",
                }, ct);

                if (completion.Usage is not null)
                {
                    aggregatePromptTokens += completion.Usage.PromptTokens;
                    aggregateCompletionTokens += completion.Usage.CompletionTokens;
                }

                var calls = completion.ToolCalls;
                if (calls is null || calls.Count == 0)
                {
                    break; // final text turn
                }

                // Append assistant turn that requested tool execution.
                messages.Add(new AiChatMessage
                {
                    Role = "assistant",
                    Content = completion.Text,
                    ToolCalls = calls,
                });

                // Execute every requested tool sequentially (parallel safe but
                // deterministic ordering keeps the audit log easy to read).
                foreach (var call in calls)
                {
                    var toolCtx = new AiToolContext(
                        FeatureCode: featureCode,
                        UserId: request.UserId,
                        AuthAccountId: request.AuthAccountId,
                        AiUsageRecordId: aiUsageRecordIdForTools,
                        TurnIndex: turn);

                    var invocation = await toolInvoker!.InvokeAsync(call, toolCtx, ct);

                    var toolPayload = invocation.ResultJson?.GetRawText()
                        ?? JsonSerializer.Serialize(new
                        {
                            outcome = invocation.Outcome.ToString(),
                            error_code = invocation.ErrorCode,
                            error_message = invocation.ErrorMessage,
                        });

                    messages.Add(new AiChatMessage
                    {
                        Role = "tool",
                        ToolCallId = call.Id,
                        Content = toolPayload,
                    });
                }

                if (turn == maxTurns - 1)
                {
                    loopTrace = "tool_loop_truncated";
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            if (usageRecorder is not null)
            {
                await usageRecorder.RecordFailureAsync(
                    context,
                    providerId: provider.Name,
                    model: effectiveModel,
                    keySource: prospectiveKeySource,
                    outcome: AiCallOutcome.Cancelled,
                    errorCode: "cancelled",
                    errorMessage: "Call cancelled by caller.",
                    latencyMs: (int)stopwatch.ElapsedMilliseconds,
                    retryCount: 0,
                    policyTrace: null,
                    ct: CancellationToken.None);
            }
            throw;
        }
        catch (TimeoutException tex)
        {
            stopwatch.Stop();
            if (usageRecorder is not null)
            {
                await usageRecorder.RecordFailureAsync(
                    context,
                    providerId: provider.Name,
                    model: effectiveModel,
                    keySource: prospectiveKeySource,
                    outcome: AiCallOutcome.Timeout,
                    errorCode: "timeout",
                    errorMessage: tex.Message,
                    latencyMs: (int)stopwatch.ElapsedMilliseconds,
                    retryCount: 0,
                    policyTrace: null,
                    ct: CancellationToken.None);
            }
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorCode = ClassifyError(ex);

            // BYOK auth failure: invalidate the credential so the resolver
            // will skip it until the configured cooldown expires. Non-fatal
            // if the vault is not wired.
            if (resolution is { KeySource: AiKeySource.Byok, CredentialId: not null }
                && errorCode == "provider_auth"
                && credentialResolver is not null)
            {
                try
                {
                    if (quotaService is not null)
                    {
                        var global = await quotaService.GetGlobalPolicyAsync(CancellationToken.None);
                        var cooldownHours = Math.Max(1, global.ByokErrorCooldownHours);
                        // Resolver doesn't own vault invalidation; that's a
                        // detail of IAiCredentialVault. Use DI via a quick
                        // scope-local lookup through the service provider
                        // if we had one — for now, log and move on; the
                        // resolver's cooldown handling is exercised when
                        // MarkInvalidAsync is called by the vault directly
                        // during key rotation. A standalone marker service
                        // is Slice 5.
                        _ = cooldownHours;
                    }
                }
                catch { /* best effort */ }
            }

            if (usageRecorder is not null)
            {
                var failover = ex as AiProviderFailoverException;
                await usageRecorder.RecordFailureAsync(
                    context,
                    providerId: provider.Name,
                    model: effectiveModel,
                    keySource: prospectiveKeySource,
                    outcome: AiCallOutcome.ProviderError,
                    errorCode: errorCode,
                    errorMessage: ex.Message,
                    latencyMs: (int)stopwatch.ElapsedMilliseconds,
                    retryCount: 0,
                    policyTrace: resolution?.PolicyTrace,
                    ct: CancellationToken.None,
                    accountId: failover?.LastAccountId,
                    failoverTrace: failover?.FailoverTrace);
            }
            throw;
        }

        stopwatch.Stop();

        // Phase 5 — when the multi-turn tool loop ran, expose the aggregated
        // prompt+completion token totals (sum across turns) on the SINGLE
        // usage record. Single-turn calls fall through with completion.Usage
        // unchanged.
        var aggregatedUsage = (aggregatePromptTokens > 0 || aggregateCompletionTokens > 0)
            ? new AiUsage { PromptTokens = aggregatePromptTokens, CompletionTokens = aggregateCompletionTokens }
            : completion.Usage;

        if (usageRecorder is not null)
        {
            await usageRecorder.RecordSuccessAsync(
                context,
                providerId: provider.Name,
                model: effectiveModel,
                keySource: prospectiveKeySource,
                usage: aggregatedUsage,
                latencyMs: (int)stopwatch.ElapsedMilliseconds,
                retryCount: 0,
                policyTrace: ComposeTrace(ComposeTrace(resolution?.PolicyTrace, quotaDecision?.PolicyTrace), loopTrace),
                ct: CancellationToken.None,
                accountId: completion.AccountId,
                failoverTrace: completion.FailoverTrace);
        }

        // Commit token usage against the per-user counters. BYOK calls are
        // never metered (user pays their own provider bill); anonymous calls
        // have no user to attribute to. Degrade / platform-fallback calls DO
        // count against platform quota.
        var shouldCommit = quotaService is not null
            && aggregatedUsage is not null
            && !string.IsNullOrWhiteSpace(request.UserId)
            && prospectiveKeySource != AiKeySource.Byok
            && prospectiveKeySource != AiKeySource.None;

        if (shouldCommit)
        {
            try
            {
                // Cost estimate: rate card × token counts, if the provider
                // exposes it. Falls back to 0 so the counter still moves.
                var costEstimate = await ComputeCostEstimateAsync(
                    selectedProviderCode ?? provider.Name, aggregatedUsage!, CancellationToken.None);

                await quotaService!.CommitAsync(
                    request.UserId,
                    featureCode,
                    aggregatedUsage!.PromptTokens,
                    aggregatedUsage.CompletionTokens,
                    costEstimateUsd: costEstimate,
                    CancellationToken.None);
            }
            catch
            {
                // Commit must never break the caller. The provider returned
                // a successful response; the user deserves it.
            }
        }

        return new AiGatewayResult
        {
            Completion = completion.Text,
            Usage = aggregatedUsage,
            Metadata = request.Prompt.Metadata,
            RulebookVersion = request.Prompt.Metadata.RulebookVersion,
            AppliedRuleIds = request.Prompt.Metadata.AppliedRuleIds,
        };
    }

    private async Task RecordRefusalAsync(
        AiGatewayRequest request,
        string featureCode,
        System.Diagnostics.Stopwatch stopwatch,
        DateTimeOffset startedAt,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        if (usageRecorder is null) return;

        stopwatch.Stop();
        var context = BuildUsageContext(request, featureCode, startedAt, systemPrompt: null, userPrompt: null);
        try
        {
            await usageRecorder.RecordFailureAsync(
                context,
                providerId: null,
                model: null,
                keySource: AiKeySource.None,
                outcome: AiCallOutcome.GatewayRefused,
                errorCode: errorCode,
                errorMessage: errorMessage,
                latencyMs: (int)stopwatch.ElapsedMilliseconds,
                retryCount: 0,
                policyTrace: "gateway.refused",
                ct: CancellationToken.None);
        }
        catch
        {
            // Recorder is fail-soft per its contract; swallow any exception so
            // the caller receives the original PromptNotGroundedException intact.
        }
    }

    private static AiUsageContext BuildUsageContext(
        AiGatewayRequest request,
        string featureCode,
        DateTimeOffset startedAt,
        string? systemPrompt,
        string? userPrompt)
        => new(
            UserId: request.UserId,
            AuthAccountId: request.AuthAccountId,
            TenantId: request.TenantId,
            FeatureCode: featureCode,
            RulebookVersion: request.Prompt?.Metadata.RulebookVersion,
            PromptTemplateId: request.PromptTemplateId,
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            StartedAt: startedAt);

    private static string? ComposeTrace(string? first, string? second)
    {
        if (string.IsNullOrEmpty(first)) return second;
        if (string.IsNullOrEmpty(second)) return first;
        var combined = $"{first} | {second}";
        return combined.Length <= 256 ? combined : combined[..256];
    }

    private static string? ProviderNameForDialect(AiProviderDialect dialect) => dialect switch
    {
        AiProviderDialect.Cloudflare => "cloudflare",
        AiProviderDialect.Anthropic => "anthropic",
        AiProviderDialect.Copilot => "copilot",
        AiProviderDialect.OpenAiCompatible => "registry",
        _ => null,
    };

    /// <summary>
    /// USD cost estimate = (prompt_tokens / 1k × prompt_rate) +
    /// (completion_tokens / 1k × completion_rate). Registry-sourced rate card.
    /// Returns 0 when no registry row exists (so platform-only tests still work).
    /// </summary>
    private async Task<decimal> ComputeCostEstimateAsync(string providerName, AiUsage usage, CancellationToken ct)
    {
        if (providerRegistry is null) return 0m;
        try
        {
            var row = await providerRegistry.FindByCodeAsync(providerName, ct);
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

    private static string ClassifyError(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        if (message.Contains("401", StringComparison.Ordinal) || message.Contains("403", StringComparison.Ordinal))
            return "provider_auth";
        if (message.Contains("429", StringComparison.Ordinal))
            return "provider_429";
        if (message.Contains("5", StringComparison.Ordinal) && (message.Contains("500", StringComparison.Ordinal) || message.Contains("502", StringComparison.Ordinal) || message.Contains("503", StringComparison.Ordinal) || message.Contains("504", StringComparison.Ordinal)))
            return "provider_5xx";
        return "provider_error";
    }

    private static string BuildUserMessage(AiGatewayRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.Prompt!.TaskInstruction);
        if (!string.IsNullOrWhiteSpace(request.UserInput))
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine(request.UserInput);
        }
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------
// Grounded prompt builder (mirror of lib/rulebook/ai-prompt.ts)
// ---------------------------------------------------------------------------

public sealed class RulebookPromptBuilder(IRulebookLoader loader)
{
    public AiGroundedPrompt Build(AiGroundingContext ctx)
    {
        var book = loader.Load(ctx.Kind, ctx.Profession);

        var (passMark, passGrade) = ResolvePassMark(ctx);
        var applicable = SelectApplicableRules(book, ctx);
        var systemPrompt = RenderSystemPrompt(book, applicable, ctx, passMark, passGrade);
        var taskInstruction = RenderTaskInstruction(ctx, passMark, passGrade);

        return new AiGroundedPrompt
        {
            SystemPrompt = systemPrompt,
            TaskInstruction = taskInstruction,
            Metadata = new AiGroundedPromptMetadata
            {
                RulebookVersion = book.Version,
                RulebookKind = book.Kind,
                Profession = book.Profession,
                ScoringPassMark = passMark,
                ScoringGrade = passGrade,
                AppliedRulesCount = applicable.Count,
                AppliedRuleIds = applicable.Select(r => r.Id).ToArray(),
            },
        };
    }

    private static (int passMark, string passGrade) ResolvePassMark(AiGroundingContext ctx)
    {
        if (ctx.Kind == RuleKind.Speaking)
            return (OetScoring.ScaledPassGradeB, "B");

        if (ctx.Kind == RuleKind.Grammar)
            return (OetScoring.ScaledPassGradeB, "B");

        if (ctx.Kind == RuleKind.Pronunciation)
            return (OetScoring.ScaledPassGradeB, "B");

        if (ctx.Kind == RuleKind.Conversation)
            return (OetScoring.ScaledPassGradeB, "B");

        if (ctx.Kind == RuleKind.Vocabulary)
            return (0, "N/A"); // Vocabulary never projects to a scaled OET score.

        if (ctx.Kind == RuleKind.Listening)
            return (OetScoring.ScaledPassGradeB, "B"); // Listening: 30/42 ≡ 350/500 anchor; admin authoring tasks reference but never produce scaled scores.

        var t = OetScoring.GetWritingPassThreshold(ctx.CandidateCountry);
        if (t is null) return (OetScoring.ScaledPassGradeB, "B");
        return (t.Threshold, t.Grade);
    }

    private static List<OetRule> SelectApplicableRules(OetRulebook book, AiGroundingContext ctx)
    {
        var context = ctx.LetterType ?? ctx.CardType;
        return book.Rules.Where(rule =>
        {
            if (rule.AppliesTo is null) return true;
            var el = rule.AppliesTo.Value;
            if (el.ValueKind == JsonValueKind.String && string.Equals(el.GetString(), "all", StringComparison.OrdinalIgnoreCase))
                return true;
            if (context is null) return true;
            if (el.ValueKind != JsonValueKind.Array) return true;
            foreach (var v in el.EnumerateArray())
                if (string.Equals(v.GetString(), context, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }).ToList();
    }

    private static string RenderSystemPrompt(OetRulebook book, List<OetRule> applicable, AiGroundingContext ctx, int passMark, string passGrade)
    {
        var critical = applicable.Where(r => r.Severity == RuleSeverity.Critical).ToList();
        var major = applicable.Where(r => r.Severity == RuleSeverity.Major).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("# OET AI — Rulebook-Grounded System Prompt");
        sb.AppendLine();
        sb.AppendLine("You are the AI assistant for the OET Preparation platform by Dr. Ahmed Hesham. Your knowledge about OET exam rules, grading, and feedback comes EXCLUSIVELY from the authoritative rulebook and scoring system reproduced below. Do not invent, extrapolate, or rely on outside opinions about OET.");
        sb.AppendLine();
        sb.AppendLine($"Rulebook: {book.Kind.ToString().ToUpperInvariant()} / {book.Profession.ToString().ToUpperInvariant()} / v{book.Version}");
        if (!string.IsNullOrWhiteSpace(book.AuthoritySource)) sb.AppendLine($"Authority: {book.AuthoritySource}");
        sb.AppendLine($"Task mode: {ctx.Task}");
        if (!string.IsNullOrWhiteSpace(ctx.CandidateCountry)) sb.AppendLine($"Candidate target country: {ctx.CandidateCountry}");
        sb.AppendLine($"Applied pass mark: {passMark}/500 (Grade {passGrade})");
        sb.AppendLine();
        AppendScoringSection(sb, ctx);
        AppendRulesBlock(sb, critical, major, applicable.Count);
        AppendConversationContext(sb, ctx);
        AppendGuardrails(sb, ctx);
        AppendReplyFormat(sb, ctx);
        return sb.ToString();
    }

    private static void AppendConversationContext(StringBuilder sb, AiGroundingContext ctx)
    {
        if (ctx.Kind != RuleKind.Conversation) return;
        sb.AppendLine("## Conversation Session Context");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(ctx.ConversationTaskTypeCode))
            sb.AppendLine($"Task type: `{ctx.ConversationTaskTypeCode}` (oet-roleplay = 5-minute clinical role-play; oet-handover = ISBAR handover).");
        if (ctx.ConversationTurnIndex is not null)
            sb.AppendLine($"Current turn index: {ctx.ConversationTurnIndex}.");
        if (ctx.ConversationElapsedSeconds is not null)
            sb.AppendLine($"Elapsed time: {ctx.ConversationElapsedSeconds}s.");
        if (ctx.ConversationRemainingSeconds is not null)
            sb.AppendLine($"Remaining time: {ctx.ConversationRemainingSeconds}s.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(ctx.ConversationScenarioJson))
        {
            sb.AppendLine("### Scenario card (role-play brief)");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(ctx.ConversationScenarioJson);
            sb.AppendLine("```");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(ctx.ConversationTranscriptJson))
        {
            sb.AppendLine("### Conversation transcript so far");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(ctx.ConversationTranscriptJson);
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    private static void AppendScoringSection(StringBuilder sb, AiGroundingContext ctx)
    {
        sb.AppendLine("## Canonical OET Scoring (non-negotiable)");
        sb.AppendLine();
        sb.AppendLine("- LISTENING: Grade B at 350/500; raw 30/42 ≡ 350/500 EXACTLY.");
        sb.AppendLine("- READING: Grade B at 350/500; raw 30/42 ≡ 350/500 EXACTLY.");
        sb.AppendLine($"- WRITING (country-aware): Grade B at {OetScoring.ScaledPassGradeB}/500 for UK/IE/AU/NZ/CA and broad signup categories until a specific regulator is captured; Grade C+ at {OetScoring.ScaledPassGradeCPlus}/500 for US/QA.");
        sb.AppendLine("- SPEAKING: Grade B at 350/500, universal (no country variation).");
        sb.AppendLine();
        sb.AppendLine(ctx.Kind switch
        {
            RuleKind.Writing => "**This call concerns WRITING** — apply the country-aware pass mark above. Never use the universal 350 threshold for Writing without verifying the country.",
            RuleKind.Speaking => "**This call concerns SPEAKING** — apply the universal 350/500 pass mark regardless of country.",
            RuleKind.Grammar => "**This call concerns GRAMMAR authoring** — the scoring table is background context only. Do NOT produce a candidate score; you are producing teaching content grounded in the rulebook.",
            RuleKind.Pronunciation => "**This call concerns PRONUNCIATION** — overall 0-100 scores map to the universal Speaking 0-500 scale using the anchor table in /rulebooks/pronunciation/common/assessment-criteria.json (60=300, 70=350=pass, 80=400, 90=450, 100=500). Never produce a grade that contradicts the Speaking pass at 350.",
            RuleKind.Vocabulary => "**This call concerns VOCABULARY authoring** — the scoring table is background context only. Do NOT produce a candidate scaled score. You are producing teaching content (terms, glosses) grounded in the vocabulary rulebook. Vocabulary quiz percentages (0–100) are pedagogical metrics and NEVER equivalent to an OET scaled score.",
            RuleKind.Conversation => "**This call concerns CONVERSATION (OET Speaking practice)** — conversation advisory criteria (0–6 each: Intelligibility, Fluency, Appropriateness, Grammar & Expression) project to the universal Speaking 0–500 scale. PASS anchor: mean 4.2/6 ≡ 350/500 (Grade B). Never produce a grade that contradicts the Speaking pass at 350.",
            RuleKind.Listening => "**This call concerns LISTENING authoring** — the 30/42 ≡ 350/500 anchor is background context only. You are producing an authored 42-item structure (Part A 24 short-answer items + Part B 6 MCQ + Part C 12 MCQ). Do NOT produce a candidate score; admins grade learners after publish via the canonical OetScoring service.",
            _ => ""
        });
        sb.AppendLine();

        if (ctx.Kind == RuleKind.Speaking)
        {
            sb.AppendLine("### Formal OET Speaking Rubric — 9 criteria (scored SEPARATELY)");
            sb.AppendLine();
            sb.AppendLine("The OET Assessor (NOT the interlocutor) scores the audio recording after the exam against these 9 criteria. You MUST produce one score per criterion — never aggregate Clinical Communication into a single number.");
            sb.AppendLine();
            sb.AppendLine("**Linguistic Criteria (4, each scored 0–6):**");
            sb.AppendLine("1. `intelligibility` — Intelligibility (pronunciation, stress, intonation, rhythm; L1 accent effect on clarity)");
            sb.AppendLine("2. `fluency` — Fluency (speed, hesitation, self-correction, sustained utterances)");
            sb.AppendLine("3. `appropriateness` — Appropriateness of Language (register, tone, lexis; explaining technical matters in lay terms)");
            sb.AppendLine("4. `grammar` — Resources of Grammar & Expression (range, accuracy, flexibility of grammar and vocabulary)");
            sb.AppendLine();
            sb.AppendLine("**Clinical Communication Criteria (5, each scored 0–3 — level descriptors: 3=Adept, 2=Competent, 1=Partially effective, 0=Ineffective):**");
            sb.AppendLine("5. `relationshipBuilding` — Relationship Building (greeting/introductions, attentive respectful attitude, non-judgemental approach, empathy)");
            sb.AppendLine("6. `patientPerspective` — Understanding & Incorporating the Patient's Perspective (eliciting ideas/concerns/expectations, picking up cues, relating explanations back)");
            sb.AppendLine("7. `providingStructure` — Providing Structure (sequencing the interview purposefully, signposting changes in topic, organising explanations)");
            sb.AppendLine("8. `informationGathering` — Information Gathering (facilitating narrative, open-then-closed questions, avoiding compound/leading questions, clarifying, summarising)");
            sb.AppendLine("9. `informationGiving` — Information Giving (establishing prior knowledge, pausing, encouraging reactions, checking understanding, discovering further needs)");
            sb.AppendLine();
            sb.AppendLine("Every feedback item MUST cite (a) the criterion code from this list AND (b) at least one rule ID from the active rulebook. Do NOT emit the legacy aggregate key `clinicalCommunication` — it is deprecated and will be rejected.");
            sb.AppendLine();
            sb.AppendLine("### Architectural rule — Interlocutor vs Assessor");
            sb.AppendLine();
            sb.AppendLine("- **Interlocutor** (actor in the role play): facilitates the role play, reads warm-up questions, plays the patient/carer. The interlocutor NEVER grades.");
            sb.AppendLine("- **OET Assessor** (this AI pipeline in advisory form; human expert in final form): listens to the audio recording AFTER the exam and scores against all 9 criteria above.");
            sb.AppendLine("- Your evaluation MUST be recording-based (post-hoc). Do NOT assess live interaction dynamics that are not audible on the recording.");
            sb.AppendLine();
        }

        sb.AppendLine("Always reference pass/fail using the exact OET grade letters: A, B, C+, C, D, E.");
        sb.AppendLine();
    }

    private static void AppendRulesBlock(StringBuilder sb, List<OetRule> critical, List<OetRule> major, int appliedTotal)
    {
        sb.AppendLine("## Active Rulebook");
        sb.AppendLine();
        sb.AppendLine($"Applied rules for this task: {appliedTotal} (critical: {critical.Count}, major: {major.Count}).");
        sb.AppendLine();
        sb.AppendLine("### CRITICAL rules (violations are auto-mark-deductions; flag them first)");
        sb.AppendLine();
        foreach (var rule in critical) sb.AppendLine(FormatRule(rule));
        sb.AppendLine();
        sb.AppendLine("### MAJOR rules (significant feedback items)");
        sb.AppendLine();
        foreach (var rule in major.Take(60)) sb.AppendLine(FormatRule(rule));
        if (major.Count > 60) sb.AppendLine($"… and {major.Count - 60} more major rules.");
        sb.AppendLine();
    }

    private static string FormatRule(OetRule rule)
    {
        var exemplar = rule.ExemplarPhrases is { Count: > 0 } ? $" · ex: \"{rule.ExemplarPhrases[0]}\"" : "";
        return $"- **{rule.Id}** ({rule.Severity.ToString().ToLowerInvariant()}) — {rule.Title}: {rule.Body}{exemplar}";
    }

    private static void AppendGuardrails(StringBuilder sb, AiGroundingContext ctx)
    {
        sb.AppendLine("## Guardrails (STRICT)");
        sb.AppendLine();
        sb.AppendLine("1. Cite rule IDs explicitly in every feedback finding (e.g. \"R03.4\", \"RULE_27\").");
        sb.AppendLine("2. Do NOT invent, rename, or extend rules. If a concern falls outside the rulebook, say so plainly.");
        sb.AppendLine("3. Do NOT produce a numeric grade that contradicts the country-aware scoring table above.");
        sb.AppendLine("4. Do NOT replace expert grading — your output is advisory. Mark it clearly as AI-generated.");
        sb.AppendLine("5. Never request the candidate's OET score from them; derive grades from the rulebook + inputs.");
        sb.AppendLine("6. Be concise, clinical, and direct. No filler praise. No motivational platitudes.");
        sb.AppendLine("7. Use the same tone Dr. Hesham uses: professional, specific, example-driven.");
        sb.AppendLine(ctx.Kind switch
        {
            RuleKind.Speaking => "8. For speaking: respect the 13-stage consultation state machine and the Breaking Bad News 7-step protocol when analysing transcripts.",
            RuleKind.Grammar => "8. For grammar authoring: every exercise you emit must cite at least one grammar rule ID (e.g. \"G02.1\") in appliedRuleIds. If a concept falls outside the rulebook, omit it rather than invent.",
            RuleKind.Pronunciation => "8. For pronunciation: every finding MUST cite a rule ID from the pronunciation rulebook (e.g. \"P01.1\", \"P04.1\"). Never invent a phoneme or stress-pattern rule. If the input shows issues outside the rulebook, describe them as observations rather than scored findings.",
            RuleKind.Vocabulary => "8. For vocabulary authoring: every term MUST cite at least one vocabulary rule ID (e.g. \"V02.1\") in appliedRuleIds. Definitions must be clinically accurate, concise (≤ 25 words), and written in formal healthcare register. Example sentences must mirror OET letter register. Never include brand names, trademarks, or colloquialisms. Never invent a rule ID.",
            RuleKind.Conversation => "8. For conversation: STAY IN ROLE as the patient/colleague specified in the scenario. Do NOT break character. Do NOT dispense real medical advice to the learner. Do NOT score or grade the learner mid-conversation (evaluation is a separate task). Keep replies 1–3 sentences, natural spoken register (contractions allowed in speech). When evaluating (EvaluateConversation task), every turnAnnotation MUST cite at least one C-rule ID (e.g. \"C01.1\"). Never invent a rule.",
            _ => "8. For writing: respect the letter structure order (Address → Date → Salutation → Re: line → Body → Yours sincerely/faithfully → Doctor) and flag layout violations."
        });
        sb.AppendLine();
    }

    private static void AppendReplyFormat(StringBuilder sb, AiGroundingContext ctx)
    {
        sb.AppendLine("## Reply format");
        sb.AppendLine();
        switch (ctx.Task)
        {
            case AiTaskMode.Score:
                sb.AppendLine("Return a SINGLE JSON object:");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"findings\": [ { \"ruleId\": \"R03.4\", \"severity\": \"critical\", \"quote\": \"...\", \"message\": \"...\", \"fixSuggestion\": \"...\" } ],");
                sb.AppendLine("  \"criteriaScores\": { \"purpose\": 0, \"content\": 0, \"conciseness_clarity\": 0, \"genre_style\": 0, \"organisation_layout\": 0, \"language\": 0 },");
                sb.AppendLine("  \"estimatedScaledScore\": 0,");
                sb.AppendLine("  \"estimatedGrade\": \"B\",");
                sb.AppendLine("  \"passed\": true,");
                sb.AppendLine("  \"passRequires\": { \"scaled\": 0, \"grade\": \"B\" },");
                sb.AppendLine("  \"advisory\": \"AI-generated — pending tutor review\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;
            case AiTaskMode.Coach:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"findings\": [...], \"nextBestAction\": \"...\", \"encouragement\": \"...\" }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.Correct:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"findings\": [...], \"revisedText\": \"...\", \"changesSummary\": \"...\" }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateFeedback:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"sections\": [ { \"title\": \"...\", \"bullets\": [\"...\"] } ], \"ruleCitations\": [\"R03.4\"] }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateContent:
                sb.AppendLine("```json");
                sb.AppendLine("{ \"content\": \"...\", \"appliedRuleIds\": [\"R03.4\"], \"selfCheckNotes\": \"...\" }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateGrammarLesson:
                sb.AppendLine("Return a SINGLE JSON object. Every exercise MUST cite one or more grammar rule IDs (e.g. \"G02.1\") that exist in the rulebook above. Never invent a rule ID.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"title\": \"...\",");
                sb.AppendLine("  \"topicSlug\": \"present_perfect_vs_past_simple\",");
                sb.AppendLine("  \"level\": \"beginner|intermediate|advanced\",");
                sb.AppendLine("  \"estimatedMinutes\": 12,");
                sb.AppendLine("  \"contentBlocks\": [ { \"type\": \"callout|prose|example|note\", \"contentMarkdown\": \"...\" } ],");
                sb.AppendLine("  \"exercises\": [");
                sb.AppendLine("    {");
                sb.AppendLine("      \"type\": \"mcq|fill_blank|error_correction|sentence_transformation|matching\",");
                sb.AppendLine("      \"promptMarkdown\": \"...\",");
                sb.AppendLine("      \"options\": [],");
                sb.AppendLine("      \"correctAnswer\": \"...\",");
                sb.AppendLine("      \"acceptedAnswers\": [],");
                sb.AppendLine("      \"explanationMarkdown\": \"... (cite G-rule IDs)\",");
                sb.AppendLine("      \"difficulty\": \"beginner|intermediate|advanced\",");
                sb.AppendLine("      \"points\": 1,");
                sb.AppendLine("      \"appliedRuleIds\": [\"G02.1\"]");
                sb.AppendLine("    }");
                sb.AppendLine("  ],");
                sb.AppendLine("  \"appliedRuleIds\": [\"G02.1\"],");
                sb.AppendLine("  \"selfCheckNotes\": \"...\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                sb.AppendLine("Hard requirements: 3–12 exercises, ≥1 content block, every exercise has non-empty explanationMarkdown, every appliedRuleIds value appears in the rulebook.");
                break;
            case AiTaskMode.ScorePronunciationAttempt:
                sb.AppendLine("Return a SINGLE JSON object scoring the learner's pronunciation attempt against the reference text. Cite ONE OR MORE pronunciation rule IDs (e.g. \"P01.1\", \"P04.2\") in `appliedRuleIds` and in each finding's `ruleId`. Never invent a rule.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"accuracyScore\": 0,");
                sb.AppendLine("  \"fluencyScore\": 0,");
                sb.AppendLine("  \"completenessScore\": 0,");
                sb.AppendLine("  \"prosodyScore\": 0,");
                sb.AppendLine("  \"overallScore\": 0,");
                sb.AppendLine("  \"wordScores\": [ { \"word\": \"...\", \"accuracyScore\": 0, \"errorType\": \"None|Mispronunciation|Omission|Insertion\" } ],");
                sb.AppendLine("  \"problematicPhonemes\": [ { \"phoneme\": \"θ\", \"score\": 0, \"occurrences\": 0, \"ruleId\": \"P01.1\" } ],");
                sb.AppendLine("  \"fluencyMarkers\": { \"speechRateWpm\": 0, \"pauseCount\": 0, \"averagePauseDurationMs\": 0 },");
                sb.AppendLine("  \"findings\": [ { \"ruleId\": \"P01.1\", \"severity\": \"critical|major|minor\", \"message\": \"...\", \"fixSuggestion\": \"...\" } ],");
                sb.AppendLine("  \"appliedRuleIds\": [\"P01.1\"],");
                sb.AppendLine("  \"projectedSpeakingBand\": { \"scaled\": 0, \"grade\": \"B\", \"passed\": true },");
                sb.AppendLine("  \"advisory\": \"AI-generated — advisory only, not a CBLA result.\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                sb.AppendLine("Scoring anchors (overall → scaled): 0→0, 60→300, 70→350 (PASS), 80→400, 90→450, 100→500. Use linear interpolation.");
                break;
            case AiTaskMode.GeneratePronunciationDrill:
                sb.AppendLine("Return a SINGLE JSON object describing a new pronunciation drill. Every field below is mandatory. Cite exactly one primary rule ID.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"targetPhoneme\": \"θ\",");
                sb.AppendLine("  \"label\": \"... (≤ 80 chars, e.g. 'th (voiceless) — as in think')\",");
                sb.AppendLine("  \"difficulty\": \"easy|medium|hard\",");
                sb.AppendLine("  \"focus\": \"phoneme|cluster|stress|intonation|prosody\",");
                sb.AppendLine("  \"exampleWords\": [\"...\"] ,      // 6–10 medical-context words");
                sb.AppendLine("  \"minimalPairs\": [ { \"a\": \"think\", \"b\": \"sink\" } ],  // 2–5 pairs");
                sb.AppendLine("  \"sentences\": [\"...\"] ,          // 3–5 practice sentences, OET-clinical");
                sb.AppendLine("  \"tipsHtml\": \"<p>...</p>\",       // articulation guidance, safe HTML only");
                sb.AppendLine("  \"appliedRuleIds\": [\"P01.1\"],    // every ID must exist in the loaded rulebook");
                sb.AppendLine("  \"selfCheckNotes\": \"...\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GeneratePronunciationFeedback:
                sb.AppendLine("Return a SINGLE JSON object with learner-facing coaching copy grounded in the scored attempt. Do not re-score; only explain.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"summary\": \"... (≤ 60 words)\",");
                sb.AppendLine("  \"strengths\": [\"...\"],");
                sb.AppendLine("  \"improvements\": [ { \"ruleId\": \"P01.1\", \"message\": \"...\", \"drillSuggestion\": \"...\" } ],");
                sb.AppendLine("  \"appliedRuleIds\": [\"P01.1\"],");
                sb.AppendLine("  \"nextDrillTargetPhoneme\": \"θ|ð|v|...\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateVocabularyTerm:
                sb.AppendLine("Return a SINGLE JSON object describing ONE OR MORE medical vocabulary terms. Each term MUST cite at least one vocabulary rule ID (e.g. \"V02.1\") in appliedRuleIds. Definitions ≤ 25 words, clinically accurate, healthcare-register. Example sentences mirror OET letter register.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"terms\": [");
                sb.AppendLine("    {");
                sb.AppendLine("      \"term\": \"...\",");
                sb.AppendLine("      \"ipaPronunciation\": \"/ˈ.../\",   // IPA string; optional but recommended");
                sb.AppendLine("      \"definition\": \"... (≤ 25 words, clinical register)\",");
                sb.AppendLine("      \"exampleSentence\": \"... (single sentence, OET-register)\",");
                sb.AppendLine("      \"contextNotes\": \"... (optional)\",");
                sb.AppendLine("      \"category\": \"medical|anatomy|symptoms|procedures|pharmacology|conditions|clinical_communication\",");
                sb.AppendLine("      \"difficulty\": \"easy|medium|hard\",");
                sb.AppendLine("      \"synonyms\": [\"...\"],");
                sb.AppendLine("      \"collocations\": [\"...\"],");
                sb.AppendLine("      \"relatedTerms\": [\"...\"],");
                sb.AppendLine("      \"appliedRuleIds\": [\"V02.1\"]");
                sb.AppendLine("    }");
                sb.AppendLine("  ],");
                sb.AppendLine("  \"selfCheckNotes\": \"... (optional)\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                sb.AppendLine("Hard requirements: every term must have `term`, `definition`, `exampleSentence`, `category`, and at least one `appliedRuleIds` entry that exists in the rulebook above. No brand names. No colloquialisms.");
                break;
            case AiTaskMode.GenerateVocabularyGloss:
                sb.AppendLine("Return a SINGLE JSON object giving a concise learner-facing gloss of the requested word in its medical context. Cite ≥1 rule ID.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"term\": \"...\",");
                sb.AppendLine("  \"ipaPronunciation\": \"/ˈ.../\",      // optional");
                sb.AppendLine("  \"shortDefinition\": \"... (≤ 20 words)\",");
                sb.AppendLine("  \"exampleSentence\": \"... (OET-register)\",");
                sb.AppendLine("  \"contextNotes\": \"... (how the word is used in the supplied context, ≤ 40 words)\",");
                sb.AppendLine("  \"synonyms\": [\"...\"],");
                sb.AppendLine("  \"register\": \"formal|clinical|colloquial\",");
                sb.AppendLine("  \"appliedRuleIds\": [\"V02.1\"]");
                sb.AppendLine("}");
                sb.AppendLine("```");
                sb.AppendLine("If the word is not medically meaningful, set `shortDefinition` to the general English sense and note that in `contextNotes`. Never invent a rule ID.");
                break;
            case AiTaskMode.GenerateConversationOpening:
                sb.AppendLine("Return a SINGLE JSON object with the AI partner's first spoken utterance. Stay in the patient/colleague role. 1–3 sentences, natural spoken register. Cite ≥1 conversation rule ID.");
                sb.AppendLine("```json");
                sb.AppendLine("{ \"text\": \"...\", \"emotionHint\": \"neutral|worried|frustrated|calm|in-pain\", \"appliedRuleIds\": [\"C01.1\"] }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateConversationReply:
                sb.AppendLine("Return a SINGLE JSON object with the AI partner's next spoken reply, grounded in the scenario and transcript above. 1–3 sentences, stay in role. If scenario objectives are met or time is nearly out, set shouldEnd=true. Cite ≥1 conversation rule ID.");
                sb.AppendLine("```json");
                sb.AppendLine("{ \"text\": \"...\", \"emotionHint\": \"neutral|worried|calm\", \"shouldEnd\": false, \"appliedRuleIds\": [\"C01.1\"] }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.EvaluateConversation:
                sb.AppendLine("Return a SINGLE JSON object evaluating the learner against the 4-criterion OET Speaking rubric. Each score 0-6. Every turnAnnotation MUST cite a conversation rule ID. Advisory only.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"criteria\": [");
                sb.AppendLine("    { \"id\": \"intelligibility\", \"score06\": 0, \"evidence\": \"...\", \"quotes\": [\"...\"] },");
                sb.AppendLine("    { \"id\": \"fluency\", \"score06\": 0, \"evidence\": \"...\", \"quotes\": [\"...\"] },");
                sb.AppendLine("    { \"id\": \"appropriateness\", \"score06\": 0, \"evidence\": \"...\", \"quotes\": [\"...\"] },");
                sb.AppendLine("    { \"id\": \"grammar_expression\", \"score06\": 0, \"evidence\": \"...\", \"quotes\": [\"...\"] }");
                sb.AppendLine("  ],");
                sb.AppendLine("  \"turnAnnotations\": [ { \"turnNumber\": 1, \"type\": \"strength|error|improvement\", \"category\": \"...\", \"ruleId\": \"C01.1\", \"evidence\": \"...\", \"suggestion\": \"...\" } ],");
                sb.AppendLine("  \"strengths\": [\"...\"], \"improvements\": [\"...\"], \"suggestedPractice\": [\"...\"],");
                sb.AppendLine("  \"appliedRuleIds\": [\"C01.1\"],");
                sb.AppendLine("  \"advisory\": \"AI-generated — advisory only.\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                sb.AppendLine("Projection: mean of 4 criteria → 0-500 via (mean * 500/6). 4.2/6 ≡ 350/500 PASS anchor.");
                break;
            case AiTaskMode.GenerateConversationScenario:
                sb.AppendLine("Return a SINGLE JSON object describing a new OET role-play scenario. Cite ≥1 conversation rule ID.");
                sb.AppendLine("```json");
                sb.AppendLine("{ \"title\": \"...\", \"taskTypeCode\": \"oet-roleplay|oet-handover\", \"difficulty\": \"easy|medium|hard\", \"setting\": \"...\", \"patientRole\": \"...\", \"clinicianRole\": \"...\", \"context\": \"...\", \"objectives\": [\"...\"], \"timeLimitSeconds\": 300, \"appliedRuleIds\": [\"C01.1\"] }");
                sb.AppendLine("```");
                break;
            case AiTaskMode.GenerateListeningStructure:
                sb.AppendLine("Return a SINGLE JSON object containing a 42-item OET Listening authored structure. Canonical shape: Part A (24 short-answer / fill-in-the-blank) + Part B (6 three-option MCQ) + Part C (12 three-option MCQ). Every question MUST cite ≥1 listening rule ID in `appliedRuleIds`. Never invent a rule ID.");
                sb.AppendLine("```json");
                sb.AppendLine("{");
                sb.AppendLine("  \"questions\": [");
                sb.AppendLine("    {");
                sb.AppendLine("      \"number\": 1,                      // 1..42, contiguous");
                sb.AppendLine("      \"partCode\": \"A1|A2|B|C1|C2\",   // A1 = Q1-12, A2 = Q13-24, B = Q25-30, C1 = Q31-36, C2 = Q37-42");
                sb.AppendLine("      \"type\": \"short_answer|multiple_choice_3\",");
                sb.AppendLine("      \"stem\": \"...\",                    // exact wording from the question paper");
                sb.AppendLine("      \"options\": [\"A\",\"B\",\"C\"],     // 3 options for MCQ; [] for short_answer");
                sb.AppendLine("      \"correctAnswer\": \"...\",           // letter (A/B/C) for MCQ; canonical text for short_answer");
                sb.AppendLine("      \"acceptedAnswers\": [\"...\"] ,      // alt spellings/synonyms for short_answer; [] for MCQ");
                sb.AppendLine("      \"transcriptExcerpt\": \"...\",       // quote from the audio script supporting the answer");
                sb.AppendLine("      \"distractorExplanation\": \"...\",   // why wrong options are tempting (MCQ only)");
                sb.AppendLine("      \"optionDistractorWhy\": [\"...\",\"...\",\"...\"],  // per-option (MCQ only); same length as options");
                sb.AppendLine("      \"optionDistractorCategory\": [\"too_strong|too_weak|wrong_speaker|opposite_meaning|reused_keyword\"],");
                sb.AppendLine("      \"speakerAttitude\": \"concerned|optimistic|doubtful|critical|neutral|other\", // Part C only");
                sb.AppendLine("      \"transcriptEvidenceStartMs\": 0,    // optional, ms in section audio");
                sb.AppendLine("      \"transcriptEvidenceEndMs\": 0,      // optional, ms in section audio");
                sb.AppendLine("      \"points\": 1,");
                sb.AppendLine("      \"appliedRuleIds\": [\"L01.1\"]");
                sb.AppendLine("    }");
                sb.AppendLine("  ],");
                sb.AppendLine("  \"appliedRuleIds\": [\"L01.1\"],");
                sb.AppendLine("  \"selfCheckNotes\": \"... (optional)\"");
                sb.AppendLine("}");
                sb.AppendLine("```");
                sb.AppendLine("Hard requirements: exactly 42 questions, contiguous numbers 1..42, exactly 24 in Part A (12 in A1 + 12 in A2), exactly 6 in Part B, exactly 12 in Part C (6 in C1 + 6 in C2), every MCQ has exactly 3 options + correctAnswer in {A,B,C}, every appliedRuleIds value exists in the rulebook above. No answer keys outside the structure above.");
                break;
            default:
                sb.AppendLine("Plain text, concise, ≤ 200 words. Cite rule IDs in parentheses when invoking rules.");
                break;
        }
    }

    // H2: Require explicit letter/card type at grounded-prompt build time. The
    // prior code emitted a placeholder letter/card type
    // when the field was missing, which meant a downstream AI would produce
    // generic, non-scenario-specific feedback the learner could not act on.
    // Failing fast here surfaces the authoring bug at the source instead of
    // letting it cross the provider boundary.
    private static string RequireLetterType(AiGroundingContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.LetterType))
        {
            throw new PromptNotGroundedException(
                "AiGroundingContext.LetterType is required for RuleKind.Writing prompts. "
                + "Populate it from the ContentPaper or WritingAttempt being graded before calling BuildGroundedPrompt.");
        }
        return ctx.LetterType.Trim().ToLowerInvariant();
    }

    private static string RequireCardType(AiGroundingContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.CardType))
        {
            throw new PromptNotGroundedException(
                "AiGroundingContext.CardType is required for RuleKind.Speaking prompts. "
                + "Populate it from the ContentPaper or SpeakingAttempt being graded before calling BuildGroundedPrompt.");
        }
        return ctx.CardType.Trim().ToLowerInvariant();
    }

    private static string RenderTaskInstruction(AiGroundingContext ctx, int passMark, string passGrade)
    {
        var baseText = ctx.Kind switch
        {
            RuleKind.Writing => $"Task: analyse the candidate's OET Writing letter ({RequireLetterType(ctx)}) against the active rulebook, and produce rule-cited feedback.",
            RuleKind.Speaking => $"Task: analyse the candidate's OET Speaking transcript ({RequireCardType(ctx)}) against the active rulebook, and produce rule-cited feedback.",
            RuleKind.Grammar => "Task: produce a grammar teaching draft (title, content blocks, exercises) grounded in the grammar rulebook. Every exercise must cite ≥1 grammar rule ID in appliedRuleIds.",
            RuleKind.Pronunciation => ctx.Task switch
            {
                AiTaskMode.ScorePronunciationAttempt => "Task: score the learner's pronunciation attempt against the reference text and the active pronunciation rulebook. Every finding and problematic phoneme must cite a P-rule ID.",
                AiTaskMode.GeneratePronunciationDrill => "Task: author one new pronunciation drill grounded in the active pronunciation rulebook. Every appliedRuleIds value must exist in the rulebook.",
                AiTaskMode.GeneratePronunciationFeedback => "Task: produce learner-facing coaching text for a scored pronunciation attempt. Do not re-score. Every improvement must cite a P-rule ID.",
                _ => "Task: respond according to the reply format above."
            },
            RuleKind.Vocabulary => ctx.Task switch
            {
                AiTaskMode.GenerateVocabularyTerm => "Task: author one or more OET medical vocabulary terms grounded in the active vocabulary rulebook. Each term must have term, definition, exampleSentence, category, and ≥1 appliedRuleIds entry.",
                AiTaskMode.GenerateVocabularyGloss => "Task: produce a concise, learner-facing gloss of a single word in its supplied medical context. Do not invent meanings. Do not re-score.",
                _ => "Task: respond according to the reply format above."
            },
            RuleKind.Conversation => ctx.Task switch
            {
                AiTaskMode.GenerateConversationOpening => "Task: deliver the AI partner's first spoken utterance in role, grounded in the scenario above. Stay in character.",
                AiTaskMode.GenerateConversationReply => "Task: deliver the AI partner's next in-role reply, grounded in scenario + transcript.",
                AiTaskMode.EvaluateConversation => "Task: evaluate the completed transcript against the 4-criterion OET Speaking rubric. Rule-cited findings. Do not invent rules.",
                AiTaskMode.GenerateConversationScenario => "Task: author one new OET role-play or handover scenario grounded in the conversation rulebook.",
                _ => "Task: respond according to the reply format above."
            },
            RuleKind.Listening => ctx.Task switch
            {
                AiTaskMode.GenerateListeningStructure => "Task: extract the 42-item OET Listening authored structure (Part A 24 short-answer + Part B 6 MCQ + Part C 12 MCQ) from the supplied Question-Paper text + Audio-Script + Answer-Key. Every question must cite ≥1 listening rule ID. Never invent rule IDs. Validate the canonical shape before responding.",
                _ => "Task: respond according to the reply format above."
            },
            _ => "Task: respond according to the reply format above."
        };
        if (ctx.Kind == RuleKind.Grammar)
            return $"{baseText} Respond strictly in the reply format above.";
        if (ctx.Kind == RuleKind.Pronunciation)
            return $"{baseText} Respond strictly in the reply format above.";
        if (ctx.Kind == RuleKind.Vocabulary)
            return $"{baseText} Respond strictly in the reply format above.";
        if (ctx.Kind == RuleKind.Conversation)
            return $"{baseText} Respond strictly in the reply format above.";
        if (ctx.Kind == RuleKind.Listening)
            return $"{baseText} Respond strictly in the reply format above.";
        return $"{baseText} Apply the {passMark}/500 (Grade {passGrade}) pass mark for this {ctx.Kind.ToString().ToLowerInvariant()} call. Respond strictly in the reply format above.";
    }
}

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

public enum AiTaskMode { Score, Coach, Correct, Summarise, GenerateFeedback, GenerateContent, GenerateGrammarLesson, ScorePronunciationAttempt, GeneratePronunciationDrill, GeneratePronunciationFeedback, GenerateVocabularyTerm, GenerateVocabularyGloss, GenerateConversationOpening, GenerateConversationReply, EvaluateConversation, GenerateConversationScenario, GenerateListeningStructure }

public sealed class AiGroundingContext
{
    public RuleKind Kind { get; init; }
    public ExamProfession Profession { get; init; } = ExamProfession.Medicine;
    public string? LetterType { get; init; }
    public string? CardType { get; init; }
    public AiTaskMode Task { get; init; } = AiTaskMode.Score;
    public string? CandidateCountry { get; init; }

    // Conversation-specific context.
    public string? ConversationScenarioJson { get; init; }
    public string? ConversationTranscriptJson { get; init; }
    public string? ConversationTaskTypeCode { get; init; }
    public int? ConversationTurnIndex { get; init; }
    public int? ConversationElapsedSeconds { get; init; }
    public int? ConversationRemainingSeconds { get; init; }
}

public sealed class AiGroundedPrompt
{
    public string SystemPrompt { get; init; } = "";
    public string TaskInstruction { get; init; } = "";
    public AiGroundedPromptMetadata Metadata { get; init; } = new();
}

public sealed class AiGroundedPromptMetadata
{
    public string RulebookVersion { get; init; } = "";
    public RuleKind RulebookKind { get; init; }
    public ExamProfession Profession { get; init; }
    public int ScoringPassMark { get; init; }
    public string ScoringGrade { get; init; } = "B";
    public int AppliedRulesCount { get; init; }
    public IReadOnlyList<string> AppliedRuleIds { get; init; } = Array.Empty<string>();
}

public sealed class AiGatewayRequest
{
    public AiGroundedPrompt? Prompt { get; init; }
    public string? UserInput { get; init; }
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public double Temperature { get; init; } = 0.2;
    public int? MaxTokens { get; init; }

    // --- Slice 1 additions: usage accounting context ---
    // These are optional for backward compatibility. Call sites are expected
    // to fill them in so admin explorer / cost dashboards can attribute the
    // call correctly. See docs/AI-USAGE-POLICY.md §5 for feature codes.

    /// <summary>Learner / admin / system user this call is on behalf of.</summary>
    public string? UserId { get; init; }

    /// <summary>Auth-account FK if known. Enables efficient joins from the
    /// admin usage explorer.</summary>
    public string? AuthAccountId { get; init; }

    /// <summary>Sponsor / organisation scope, if applicable.</summary>
    public string? TenantId { get; init; }

    /// <summary>Canonical feature code from <c>AiFeatureCodes</c>. If omitted,
    /// the recorder will stamp <c>unclassified</c> — tolerated during Slice 1
    /// rollout, to be enforced in a later slice.</summary>
    public string? FeatureCode { get; init; }

    /// <summary>Optional prompt-template identifier for A/B analysis.</summary>
    public string? PromptTemplateId { get; init; }
}

public sealed class AiGatewayResult
{
    public string Completion { get; init; } = "";
    public AiUsage? Usage { get; init; }
    public AiGroundedPromptMetadata Metadata { get; init; } = new();
    public string RulebookVersion { get; init; } = "";
    public IReadOnlyList<string> AppliedRuleIds { get; init; } = Array.Empty<string>();
}

public sealed class AiUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
}

public sealed class PromptNotGroundedException(string message) : InvalidOperationException(message);

// ---------------------------------------------------------------------------
// Provider contract — implement per model vendor
// ---------------------------------------------------------------------------

public interface IAiModelProvider
{
    string Name { get; }
    Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct);
}

public sealed class AiProviderRequest
{
    public string? ProviderCode { get; init; }
    public string Model { get; init; } = "";
    public string SystemPrompt { get; init; } = "";
    public string UserPrompt { get; init; } = "";
    public double Temperature { get; init; } = 0.2;
    public int? MaxTokens { get; init; }

    /// <summary>Optional override for the API key. When non-null, providers
    /// use this key instead of their configured/default credential. Supplied
    /// by the credential resolver for BYOK calls. Slice 4+.</summary>
    public string? ApiKeyOverride { get; init; }

    /// <summary>Optional override for the base URL. Used when the admin has
    /// registered a provider with a non-default endpoint (Slice 5+).</summary>
    public string? BaseUrlOverride { get; init; }

    /// <summary>Phase 5 — explicit message history for tool-calling multi-turn
    /// loops. When supplied, providers MUST use this in place of building a
    /// pair from <see cref="SystemPrompt"/>/<see cref="UserPrompt"/>. Includes
    /// assistant messages with <c>tool_calls</c> and tool result messages.
    /// Providers that don't support tool calling (mock, anthropic adapter,
    /// registry adapter) ignore this and fall back to the legacy two-message
    /// shape from <see cref="SystemPrompt"/>+<see cref="UserPrompt"/>.</summary>
    public IReadOnlyList<AiChatMessage>? Messages { get; init; }

    /// <summary>Phase 5 — tool definitions exposed to the model on this turn.
    /// When non-empty, providers that support function calling MUST attach
    /// these to the underlying chat-completions request. Providers that do
    /// not support tool calling MUST ignore this and return a normal text
    /// completion with <see cref="AiProviderCompletion.ToolCalls"/> = null.</summary>
    public IReadOnlyList<AiToolDefinition>? Tools { get; init; }

    /// <summary>Phase 5 — <c>"auto"</c> (default), <c>"none"</c>, or a specific
    /// tool code. Providers MAY ignore this if their backing API does not
    /// support tool_choice; the gateway only sets <c>"auto"</c>.</summary>
    public string? ToolChoice { get; init; }
}

public sealed class AiProviderCompletion
{
    public string Text { get; init; } = "";
    public AiUsage? Usage { get; init; }

    /// <summary>Multi-account pool: the <c>AiProviderAccount.Id</c> that
    /// served this completion. Null when the provider is single-credential.
    /// Phase 3.</summary>
    public string? AccountId { get; init; }

    /// <summary>Multi-account pool: compact failover trail when the provider
    /// retried across accounts before succeeding. Format
    /// <c>"primary:429 → backup:success"</c>. Null on single-shot calls.
    /// Phase 3.</summary>
    public string? FailoverTrace { get; init; }

    /// <summary>Phase 5 — tool calls emitted by the model on this turn. When
    /// non-empty the gateway invokes each tool, appends an assistant message
    /// + tool result messages, and re-calls the provider. Null/empty means
    /// the model returned a final text answer.</summary>
    public IReadOnlyList<AiToolCall>? ToolCalls { get; init; }

    /// <summary>Phase 5 — provider's stop reason for the turn. Common values:
    /// <c>"stop"</c>, <c>"tool_calls"</c>, <c>"length"</c>, <c>"content_filter"</c>.
    /// Used only for diagnostics and gateway loop bookkeeping.</summary>
    public string? FinishReason { get; init; }
}

/// <summary>Phase 5 — chat-message envelope used by tool-calling providers.
/// Mirrors the OpenAI ChatCompletionMessage shape. <c>Role</c> is one of
/// <c>"system"</c>, <c>"user"</c>, <c>"assistant"</c>, <c>"tool"</c>.</summary>
public sealed class AiChatMessage
{
    public required string Role { get; init; }
    public string? Content { get; init; }
    /// <summary>Set on assistant turns that requested tool execution.</summary>
    public IReadOnlyList<AiToolCall>? ToolCalls { get; init; }
    /// <summary>Set on <c>"tool"</c> messages — matches an
    /// <see cref="AiToolCall.Id"/> from the previous assistant turn.</summary>
    public string? ToolCallId { get; init; }
}

/// <summary>Phase 5 — single tool call requested by the model.
/// <c>ArgsJson</c> is the raw JSON arguments string emitted by the provider;
/// the invoker validates it against the tool's JSON Schema before dispatch.</summary>
public sealed class AiToolCall
{
    public required string Id { get; init; }
    public required string ToolCode { get; init; }
    public required string ArgsJson { get; init; }
}

/// <summary>
/// Provider-level exception thrown when a multi-account pool is exhausted.
/// Carries the same trail data as <see cref="AiProviderCompletion.FailoverTrace"/>
/// so the gateway can persist it on the failure record. Internal to the
/// provider implementation; gateway is the only catcher.
/// </summary>
public sealed class AiProviderFailoverException : InvalidOperationException
{
    public AiProviderFailoverException(string message, string failoverTrace, string? lastAccountId)
        : base(message)
    {
        FailoverTrace = failoverTrace;
        LastAccountId = lastAccountId;
    }

    public string FailoverTrace { get; }
    public string? LastAccountId { get; }
}

/// <summary>
/// Default provider — echoes the grounding metadata back without calling any
/// external model. Installed as the DI fallback so every environment has a
/// working gateway even without AI API keys configured. Production pods
/// swap in <c>OpenAiProvider</c> / <c>AnthropicProvider</c> / <c>GeminiProvider</c>
/// at DI registration time.
/// </summary>
public sealed class MockAiProvider : IAiModelProvider
{
    public string Name => "mock";

    public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        // NON-PRODUCTION ONLY. This provider exists to keep the 500+ unit
        // tests green without an internet connection. Production MUST route
        // through the registry-backed provider (DigitalOcean Serverless
        // Claude Opus 4.7) — the gateway prefers non-mock providers by name
        // whenever more than one is registered.
        var text = "{\"findings\":[],\"advisory\":\"mock AI provider — no external model call was made\"}";
        return Task.FromResult(new AiProviderCompletion { Text = text, Usage = new AiUsage() });
    }
}
