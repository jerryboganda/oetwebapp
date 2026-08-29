using System.Diagnostics;
using System.Security.Cryptography;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Central OCR entry point — the single place every OCR pass flows through so
/// each one is recorded as an <see cref="AiUsageRecord"/> (full traceability in
/// /admin/ai-usage and ai-analytics). Wraps <see cref="IMistralOcrClient"/>
/// (Mistral document OCR, the canonical OCR provider) and stamps the call with
/// the supplied feature code (e.g. <c>ocr.listening.parta</c>,
/// <c>ocr.content.pdf_fallback</c>, <c>ocr.writing.handwriting</c>).
/// </summary>
public interface IOcrService
{
    /// <summary>OCR a document to Markdown, recording one usage row.</summary>
    /// <param name="featureCode">An <c>ocr.*</c> feature code identifying the call site.</param>
    /// <param name="userId">Learner/admin id for attribution, or null for platform jobs.</param>
    Task<string> OcrToMarkdownAsync(
        byte[] documentBytes,
        string mimeType,
        string featureCode,
        string? userId,
        CancellationToken ct);
}

public sealed class OcrService(
    IMistralOcrClient client,
    IDirectAiCallRecorder recorder,
    TimeProvider clock) : IOcrService
{
    public async Task<string> OcrToMarkdownAsync(
        byte[] documentBytes,
        string mimeType,
        string featureCode,
        string? userId,
        CancellationToken ct)
    {
        var requestHash = Convert.ToHexString(SHA256.HashData(documentBytes));
        var lease = await recorder.BeginOperationAsync(new DirectAiOperationRequest
        {
            FeatureCode = featureCode,
            Module = "ocr",
            UserId = userId,
            ResourceId = $"{featureCode}:{requestHash}",
            ResourceType = "ocr_document",
            RequestHash = requestHash,
            OperationClass = AiOperationClass.InteractiveLearning,
            AllowRetryAfterFailure = true,
        }, ct);

        if (!lease.CanProceed)
            throw new InvalidOperationException($"OCR is unavailable ({lease.Reason}).");

        return await DirectAiOperationReconciler.RunAsync(
            recorder,
            lease,
            MistralOcrClient.ProviderCode,
            async () =>
            {
                var startedAt = clock.GetUtcNow();
                var sw = Stopwatch.StartNew();
                var context = new AiUsageContext(
                    UserId: userId,
                    AuthAccountId: null,
                    TenantId: null,
                    FeatureCode: featureCode,
                    RulebookVersion: null,
                    PromptTemplateId: null,
                    SystemPrompt: null,
                    UserPrompt: null,
                    StartedAt: startedAt);

                try
                {
                    var result = await client.OcrToMarkdownAsync(documentBytes, mimeType, ct);
                    sw.Stop();
                    // Mistral OCR is priced per page. $0.001/page is the
                    // conservative estimate used to meter the W3 budget until
                    // AiModelPrices carries an OCR row.
                    var costUsd = Math.Max(0.001m, result.PagesProcessed * 0.001m);
                    var usageId = await recorder.RecordSuccessAsync(
                        context,
                        providerId: MistralOcrClient.ProviderCode,
                        model: result.Model,
                        usage: null,
                        latencyMs: (int)sw.ElapsedMilliseconds,
                        policyTrace: $"ocr.pages={result.PagesProcessed}",
                        costEstimateUsd: costUsd,
                        ct: ct,
                        operationId: lease.OperationId,
                        attemptNumber: lease.AttemptNumber);
                    await recorder.CompleteOperationAsync(
                        lease.OperationId!,
                        AiOperationState.Completed,
                        usageId,
                        MistralOcrClient.ProviderCode,
                        result.Model,
                        CancellationToken.None,
                        lease.BudgetReservation);
                    return result.Markdown;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    await recorder.RecordFailureAsync(
                        context,
                        providerId: MistralOcrClient.ProviderCode,
                        model: null,
                        outcome: AiCallOutcome.ProviderError,
                        errorCode: "ocr_failed",
                        errorMessage: ex.Message,
                        latencyMs: (int)sw.ElapsedMilliseconds,
                        policyTrace: "ocr.failed",
                        ct: ct,
                        operationId: lease.OperationId,
                        attemptNumber: lease.AttemptNumber);
                    throw;
                }
            },
            ct);
    }
}
