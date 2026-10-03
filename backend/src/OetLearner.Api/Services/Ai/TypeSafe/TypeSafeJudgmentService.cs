using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai.TypeSafe;

/// <summary>
/// Governed entry point for TypeSafe SystemOne (Jev) judgment calls — the
/// direct-call analogue of <see cref="IAiGatewayService"/> for judgments
/// instead of generated text. Every call:
///
/// <list type="number">
/// <item>is gated by the master switch (<c>TypeSafe:Enabled</c> + key) —
/// off means a <see cref="JevCallStatus.Disabled"/> result and zero infra;</item>
/// <item>leases a control-plane <c>AiOperation</c> via
/// <see cref="IDirectAiCallRecorder"/> (feature policy + budget hold BEFORE
/// any transport, exactly like OCR/STT);</item>
/// <item>writes exactly one <see cref="AiUsageRecord"/> (success or failure)
/// with real token counts and the input-token cost estimate;</item>
/// <item>is fail-soft: any failure returns
/// <see cref="JevCallStatus.Unavailable"/>. A judgment layer must never turn
/// into a learner-visible outage — callers carry on without the judgment.</item>
/// </list>
///
/// <para>
/// Jev never grades: callers use it for guards, routing, verification, and
/// advisory signals. It must never override the rulebook-grounded gateway
/// verdicts, the deterministic WritingRuleEngine, or the GEPA placement
/// engine, and its results must never gate a pass/fail decision on their own.
/// </para>
/// </summary>
public interface ITypeSafeJudgmentService
{
    /// <summary>Ask one batch of independent questions over one state.
    /// Never throws for caller-cancel only; every other failure is
    /// <see cref="JevCallStatus.Unavailable"/>.</summary>
    Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct);
}

public sealed class TypeSafeJudgmentService(
    ITypeSafeJudgmentClient client,
    IDirectAiCallRecorder recorder,
    IOptions<TypeSafeOptions> options,
    TimeProvider clock,
    ILogger<TypeSafeJudgmentService> logger,
    IAiProviderRegistry? registry = null) : ITypeSafeJudgmentService
{
    private const string Module = "jev";
    private const int MaxLoggedErrorMessageChars = 300;

    /// <summary>Floor for the platform budget hold, so a tiny request never
    /// holds a zero amount.</summary>
    private const decimal MinReservationEstimateUsd = 0.000001m;

    public async Task<JevJudgmentResult> AskAsync(JevJudgmentRequest request, JevCallMetadata call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(call);

        var opts = options.Value;
        if (!opts.Enabled)
            return JevJudgmentResult.Disabled("typesafe_disabled");

        var apiKey = opts.ApiKey;
        if (registry is not null)
        {
            try
            {
                apiKey = await registry.GetPlatformKeyAsync(TypeSafeOptions.ProviderCode, ct) ?? apiKey;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning("TypeSafe credential resolution unavailable for {FeatureCode}.", call.FeatureCode);
                return JevJudgmentResult.Unavailable("jev_credentials_unavailable");
            }
        }
        if (string.IsNullOrWhiteSpace(apiKey))
            return JevJudgmentResult.Disabled("typesafe_key_missing");

        // Hash the exact wire bytes — the control plane sees a digest of the
        // judgment request, never the payload itself (prompt-hash policy).
        var payload = TypeSafeRequestBuilder.BuildPayload(request, opts.Model);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        // Hold what this call can really cost, not the flat default hold
        // (~250x a Jev call, enough to exhaust the InteractiveLearning caps
        // in a handful of calls): input tokens ~ wire chars / 4, doubled for
        // tokenizer slack. Jev bills input tokens only.
        var reservationEstimateUsd = Math.Max(
            MinReservationEstimateUsd,
            Math.Max(1, payload.Length / 4) * opts.CostPerInputTokenUsd * 2m);

        var lease = await recorder.BeginOperationAsync(new DirectAiOperationRequest
        {
            FeatureCode = call.FeatureCode,
            Module = Module,
            UserId = call.UserId,
            ResourceId = call.ResourceId,
            ResourceType = call.ResourceType,
            ResourceVersion = call.ResourceVersion,
            RequestHash = requestHash,
            OperationClass = AiFeaturePolicyDefaults.All.TryGetValue(call.FeatureCode, out var defaultPolicy)
                ? defaultPolicy.OperationClass
                : AiOperationClass.InteractiveLearning,
            AllowRetryAfterFailure = true,
        }, ct, reservationEstimateUsd);

        // A refusal here includes the owner emergency levers (kill switch /
        // per-feature kill list): fail-soft Unavailable, never an error.
        if (!lease.CanProceed)
            return JevJudgmentResult.Unavailable($"jev_lease_{lease.Disposition}:{lease.Reason}");

        try
        {
            return await DirectAiOperationReconciler.RunAsync(
                recorder,
                lease,
                TypeSafeOptions.ProviderCode,
                async () =>
                {
                    var context = new AiUsageContext(
                        UserId: call.UserId,
                        AuthAccountId: null,
                        TenantId: null,
                        FeatureCode: call.FeatureCode,
                        RulebookVersion: null,
                        PromptTemplateId: null,
                        SystemPrompt: null,
                        UserPrompt: null,
                        StartedAt: clock.GetUtcNow());

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var sendStarted = false;
                    var usageRecorded = false;
                    try
                    {
                        // Cancelled before dispatch = no provider call, so no
                        // usage row; the reconciler closes the lease Cancelled.
                        ct.ThrowIfCancellationRequested();
                        sendStarted = true;
                        var raw = await client.SendAsync(payload, ct, apiKey);
                        sw.Stop();

                        // Jev bills input tokens only; output tokens are free
                        // but still recorded so usage analytics stay truthful.
                        var usage = new AiUsage
                        {
                            PromptTokens = raw.InputTokens,
                            CompletionTokens = raw.OutputTokens,
                        };
                        var costUsd = raw.InputTokens * opts.CostPerInputTokenUsd;

                        var usageId = await recorder.RecordSuccessAsync(
                            context,
                            providerId: TypeSafeOptions.ProviderCode,
                            model: raw.Model,
                            usage: usage,
                            latencyMs: (int)sw.ElapsedMilliseconds,
                            policyTrace: $"jev.questions={request.Questions.Count}",
                            costEstimateUsd: costUsd,
                            // The response was received (and billed): the row
                            // must survive a caller cancelling right now.
                            ct: CancellationToken.None,
                            operationId: lease.OperationId,
                            attemptNumber: lease.AttemptNumber);
                        usageRecorded = true;

                        await recorder.CompleteOperationAsync(
                            lease.OperationId!,
                            AiOperationState.Completed,
                            usageId,
                            TypeSafeOptions.ProviderCode,
                            raw.Model,
                            CancellationToken.None,
                            lease.BudgetReservation);

                        return new JevJudgmentResult(
                            JevCallStatus.Ok, raw.Model, raw.Answers, raw.InputTokens, raw.OutputTokens, null);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // The caller walked away after the request was sent: it
                        // may already be billed, so it must not go unrecorded.
                        // CancellationToken.None because ct is already cancelled.
                        if (sendStarted && !usageRecorded)
                        {
                            sw.Stop();
                            await recorder.RecordFailureAsync(
                                context,
                                providerId: TypeSafeOptions.ProviderCode,
                                model: null,
                                outcome: AiCallOutcome.Cancelled,
                                errorCode: "jev_cancelled",
                                errorMessage: "Caller cancelled the judgment after the request was sent.",
                                latencyMs: (int)sw.ElapsedMilliseconds,
                                policyTrace: "jev.cancelled",
                                ct: CancellationToken.None,
                                operationId: lease.OperationId,
                                attemptNumber: lease.AttemptNumber);
                        }
                        throw;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        await recorder.RecordFailureAsync(
                            context,
                            providerId: TypeSafeOptions.ProviderCode,
                            model: null,
                            outcome: AiCallOutcome.ProviderError,
                            errorCode: "jev_failed",
                            errorMessage: Truncate(ex.Message),
                            latencyMs: (int)sw.ElapsedMilliseconds,
                            policyTrace: "jev.failed",
                            ct: CancellationToken.None,
                            operationId: lease.OperationId,
                            attemptNumber: lease.AttemptNumber);
                        throw;
                    }
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-soft by contract: the judgment layer never becomes a
            // learner-visible outage. The usage row above carries the detail.
            logger.LogWarning(ex, "TypeSafe judgment unavailable for {FeatureCode}.", call.FeatureCode);
            return JevJudgmentResult.Unavailable("jev_unavailable");
        }
    }

    private static string Truncate(string s) => s.Length <= MaxLoggedErrorMessageChars ? s : s[..MaxLoggedErrorMessageChars];
}
