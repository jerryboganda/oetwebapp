using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;

namespace OetLearner.Api.Services.Content;

/// <summary>
/// Composite <see cref="IPdfTextExtractor"/> that runs PdfPig first and
/// (optionally) falls back to Azure Document Intelligence prebuilt-read when
/// the embedded text is below <see cref="PdfExtractionOptions.MinTextLengthForSuccess"/>.
/// Azure DocIntel is OCR (not an LLM), so per PRD §5 it intentionally does NOT
/// route through <c>IAiGatewayService</c>. Plain HttpClient is used to avoid
/// pulling in the Azure SDK.
/// </summary>
public sealed class AutoPdfTextExtractor : IPdfTextExtractor
{
    private const string AzureClientName = "AzureDocIntel";

    private readonly ILogger<AutoPdfTextExtractor> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PdfExtractionOptions _options;
    private readonly PdfPigPdfTextExtractor _pdfPig;

    public AutoPdfTextExtractor(
        ILogger<AutoPdfTextExtractor> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<PdfExtractionOptions> options,
        PdfPigPdfTextExtractor pdfPig)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _pdfPig = pdfPig;
    }

    public async Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct)
    {
        if (string.Equals(_options.Provider, "noop", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Buffer once — both PdfPig and Azure may need to consume the bytes.
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var azureConfigured =
            !string.IsNullOrWhiteSpace(_options.AzureEndpoint) &&
            !string.IsNullOrWhiteSpace(_options.AzureApiKey);

        if (string.Equals(_options.Provider, "azure", StringComparison.OrdinalIgnoreCase))
        {
            if (!azureConfigured)
            {
                _logger.LogWarning("PdfExtraction Provider=azure but Azure DocIntel not configured; returning empty.");
                return string.Empty;
            }
            return await TryAzureAsync(bytes, ct) ?? string.Empty;
        }

        // PdfPig path (covers "pdfpig" and "auto").
        using var pdfPigStream = new MemoryStream(bytes, writable: false);
        var pdfPigText = await _pdfPig.ExtractAsync(pdfPigStream, ct);

        if (string.Equals(_options.Provider, "pdfpig", StringComparison.OrdinalIgnoreCase))
        {
            return pdfPigText;
        }

        // auto: only fall back when PdfPig produced too little AND Azure is configured.
        if ((pdfPigText?.Length ?? 0) >= _options.MinTextLengthForSuccess) return pdfPigText ?? string.Empty;
        if (!azureConfigured) return pdfPigText ?? string.Empty;

        _logger.LogInformation(
            "{Event} provider={Provider} sizeBytes={SizeBytes} pdfPigChars={PdfPigChars}",
            "pdf.ocr.fallback", "azure-docintel", bytes.LongLength, pdfPigText?.Length ?? 0);

        var azureText = await TryAzureAsync(bytes, ct);
        return !string.IsNullOrWhiteSpace(azureText) ? azureText! : (pdfPigText ?? string.Empty);
    }

    private async Task<string?> TryAzureAsync(byte[] bytes, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(AzureClientName);
            var endpoint = _options.AzureEndpoint.TrimEnd('/');
            var analyzeUri = $"{endpoint}/formrecognizer/documentModels/prebuilt-read:analyze?api-version=2023-07-31";

            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var req = new HttpRequestMessage(HttpMethod.Post, analyzeUri) { Content = content };
            req.Headers.Add("Ocp-Apim-Subscription-Key", _options.AzureApiKey);

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
                pollReq.Headers.Add("Ocp-Apim-Subscription-Key", _options.AzureApiKey);
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
