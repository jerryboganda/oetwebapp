using System.Diagnostics;
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
            // OCR has no token concept — record pages in the policy trace and
            // zero tokens. Cost stays 0 (the mistral-ocr row prices are per page,
            // not per token, and OCR billing is tracked via the page count).
            await recorder.RecordSuccessAsync(
                context,
                providerId: MistralOcrClient.ProviderCode,
                model: result.Model,
                usage: null,
                latencyMs: (int)sw.ElapsedMilliseconds,
                policyTrace: $"ocr.pages={result.PagesProcessed}",
                costEstimateUsd: 0m,
                ct: ct);
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
                ct: ct);
            throw;
        }
    }
}
