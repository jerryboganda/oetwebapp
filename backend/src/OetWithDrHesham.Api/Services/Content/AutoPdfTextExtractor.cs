using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetWithDrHesham.Api.Configuration;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Ai;
using OetWithDrHesham.Api.Services.Rulebook;
using OetWithDrHesham.Api.Services.Settings;

namespace OetWithDrHesham.Api.Services.Content;

/// <summary>
/// Composite <see cref="IPdfTextExtractor"/> that runs PdfPig first and falls
/// back to OCR when the embedded text is below
/// <see cref="PdfExtractionOptions.MinTextLengthForSuccess"/>: Azure Document
/// Intelligence (when explicitly configured) then Mistral OCR (the canonical
/// OCR provider — covers scanned PDFs wherever a <c>mistral-ocr</c> key is set).
/// OCR is not an LLM, so per PRD §5 it intentionally does NOT route through
/// <c>IAiGatewayService</c>; the Mistral tier still records an
/// <c>AiUsageRecord</c> via <see cref="IOcrService"/> for traceability.
/// </summary>
public sealed class AutoPdfTextExtractor : IPdfTextExtractor
{
    private const string AzureClientName = "AzureDocIntel";

    private readonly ILogger<AutoPdfTextExtractor> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRuntimeSettingsProvider _runtimeSettings;
    private readonly PdfPigPdfTextExtractor _pdfPig;
    private readonly IServiceScopeFactory _scopeFactory;

    public AutoPdfTextExtractor(
        ILogger<AutoPdfTextExtractor> logger,
        IHttpClientFactory httpClientFactory,
        IRuntimeSettingsProvider runtimeSettings,
        PdfPigPdfTextExtractor pdfPig,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _runtimeSettings = runtimeSettings;
        _pdfPig = pdfPig;
        _scopeFactory = scopeFactory;
    }

    public async Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
    {
        // Wave 4: PDF extraction provider/endpoint/key/min-length are DB-overridable
        // (null DB → env default). The Azure key is decrypted by the provider.
        var options = (await _runtimeSettings.GetAsync(ct)).PdfExtraction;

        if (string.Equals(options.Provider, "noop", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Buffer once — both PdfPig and Azure may need to consume the bytes.
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var azureConfigured =
            !string.IsNullOrWhiteSpace(options.AzureEndpoint) &&
            !string.IsNullOrWhiteSpace(options.AzureApiKey);
        var looksLikePdf = bytes.Length >= 5
            && bytes[0] == 0x25
            && bytes[1] == 0x50
            && bytes[2] == 0x44
            && bytes[3] == 0x46
            && bytes[4] == 0x2D;

        if (string.Equals(options.Provider, "azure", StringComparison.OrdinalIgnoreCase))
        {
            if (!azureConfigured)
            {
                _logger.LogWarning("PdfExtraction Provider=azure but Azure DocIntel not configured; returning empty.");
                return string.Empty;
            }
            return await TryAzureAsync(options, bytes, ct) ?? string.Empty;
        }

        if (!looksLikePdf)
        {
            if (!azureConfigured) return string.Empty;
            return await TryAzureAsync(options, bytes, ct) ?? string.Empty;
        }

        // PdfPig path (covers "pdfpig" and "auto").
        using var pdfPigStream = new MemoryStream(bytes, writable: false);
        var pdfPigText = await _pdfPig.ExtractAsync(pdfPigStream, ct);

        if (string.Equals(options.Provider, "pdfpig", StringComparison.OrdinalIgnoreCase))
        {
            return pdfPigText;
        }

        // auto: only fall back when PdfPig produced too little text (scanned PDF).
        if ((pdfPigText?.Length ?? 0) >= options.MinTextLengthForSuccess) return pdfPigText ?? string.Empty;

        var best = pdfPigText ?? string.Empty;

        // Tier 2 — Azure Document Intelligence (only when explicitly configured).
        if (azureConfigured)
        {
            _logger.LogInformation(
                "{Event} provider={Provider} sizeBytes={SizeBytes} pdfPigChars={PdfPigChars}",
                "pdf.ocr.fallback", "azure-docintel", bytes.LongLength, pdfPigText?.Length ?? 0);
            var azureText = await TryAzureAsync(options, bytes, ct);
            if (!string.IsNullOrWhiteSpace(azureText) && azureText!.Length > best.Length) best = azureText;
            if (best.Length >= options.MinTextLengthForSuccess) return best;
        }

        // Tier 3 — Mistral OCR (canonical OCR provider). Covers scanned PDFs
        // wherever the mistral-ocr row is keyed. Fail-soft: returns the best
        // text gathered so far on any error, never throws from the fallback.
        var mistralText = await TryMistralOcrAsync(bytes, ct);
        if (!string.IsNullOrWhiteSpace(mistralText) && mistralText!.Length > best.Length) best = mistralText;
        return best;
    }

    private async Task<string?> TryMistralOcrAsync(byte[] bytes, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IAiProviderRegistry>();
            // Silent skip when the canonical OCR row has no key — this fallback
            // must be free when OCR is not configured.
            var key = await registry.GetPlatformKeyAsync(MistralOcrClient.ProviderCode, ct);
            if (string.IsNullOrEmpty(key)) return null;

            _logger.LogInformation(
                "{Event} provider={Provider} sizeBytes={SizeBytes}",
                "pdf.ocr.fallback", "mistral-ocr", bytes.LongLength);

            var ocr = scope.ServiceProvider.GetRequiredService<IOcrService>();
            return await ocr.OcrToMarkdownAsync(
                bytes, "application/pdf", AiFeatureCodes.OcrContentPdfFallback, userId: null, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mistral OCR fallback failed; returning best prior text.");
            return null;
        }
    }

    private async Task<string?> TryAzureAsync(PdfExtractionSettings options, byte[] bytes, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(AzureClientName);
            var endpoint = options.AzureEndpoint.TrimEnd('/');
            var analyzeUri = $"{endpoint}/formrecognizer/documentModels/prebuilt-read:analyze?api-version=2023-07-31";

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var req = new HttpRequestMessage(HttpMethod.Post, analyzeUri) { Content = content };
            req.Headers.Add("Ocp-Apim-Subscription-Key", options.AzureApiKey);

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Azure DocIntel analyze failed: {Status}", (int)resp.StatusCode);
                return null;
            }

            if (!resp.Headers.TryGetValues("Operation-Location", out var locValues))
            {
                _logger.LogWarning("Azure DocIntel response missing Operation-Location header.");
                return null;
            }
            var opLocation = locValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(opLocation)) return null;

            // Poll: cap at ~60s (30 * 2s) to avoid hanging an admin extraction.
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                using var pollReq = new HttpRequestMessage(HttpMethod.Get, opLocation);
                pollReq.Headers.Add("Ocp-Apim-Subscription-Key", options.AzureApiKey);
                using var pollResp = await client.SendAsync(pollReq, ct);
                if (!pollResp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Azure DocIntel poll failed: {Status}", (int)pollResp.StatusCode);
                    return null;
                }

                var json = await pollResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    return ExtractTextFromAnalyzeResult(doc.RootElement);
                }
                if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Azure DocIntel analyze status=failed.");
                    return null;
                }
            }

            _logger.LogWarning("Azure DocIntel analyze timed out after polling.");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure DocIntel call failed; falling back to PdfPig result.");
            return null;
        }
    }

    private static string ExtractTextFromAnalyzeResult(JsonElement root)
    {
        if (!root.TryGetProperty("analyzeResult", out var analyze)) return string.Empty;
        if (!analyze.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        bool firstPage = true;
        foreach (var page in pages.EnumerateArray())
        {
            if (!firstPage) sb.Append("\n\n");
            firstPage = false;
            if (!page.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array) continue;
            bool firstLine = true;
            foreach (var line in lines.EnumerateArray())
            {
                if (!firstLine) sb.Append('\n');
                firstLine = false;
                if (line.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    sb.Append(c.GetString());
                }
            }
        }
        return sb.ToString().Trim();
    }
}
