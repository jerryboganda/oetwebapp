using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>Result of an OCR pass — the concatenated per-page Markdown plus the
/// page count and model the provider reported (for usage analytics).</summary>
public sealed record MistralOcrResult(string Markdown, int PagesProcessed, string Model);

/// <summary>
/// Thin client over Mistral's document OCR endpoint (<c>POST /v1/ocr</c>,
/// model <c>mistral-ocr-latest</c>). Converts a PDF / image into high-fidelity
/// per-page Markdown that preserves headings, bullets, and layout.
///
/// Credentials resolve from the registered <c>mistral-ocr</c> AI provider row
/// (<see cref="IAiProviderRegistry"/>), so admins rotate the key in
/// /admin/ai-providers without a redeploy. SSRF-guarded via
/// <see cref="AiProviderConnectionTester.GetUnsafeBaseUrlReason"/>, exactly like
/// the chat providers. Callers should go through <see cref="IOcrService"/> so
/// every OCR pass is recorded as an <c>AiUsageRecord</c> for full traceability.
/// </summary>
public interface IMistralOcrClient
{
    /// <summary>OCR a document and return the per-page Markdown + page count.</summary>
    Task<MistralOcrResult> OcrToMarkdownAsync(byte[] documentBytes, string mimeType, CancellationToken ct);
}

public sealed class MistralOcrClient(
    IHttpClientFactory httpClientFactory,
    IAiProviderRegistry registry) : IMistralOcrClient
{
    /// <summary>Registry <c>Code</c> admins must use for the Mistral OCR row.</summary>
    public const string ProviderCode = "mistral-ocr";
    private const string DefaultBaseUrl = "https://api.mistral.ai";
    private const string DefaultModel = "mistral-ocr-latest";

    public async Task<MistralOcrResult> OcrToMarkdownAsync(byte[] documentBytes, string mimeType, CancellationToken ct)
    {
        if (documentBytes is null || documentBytes.Length == 0)
            throw new InvalidOperationException("OCR document is empty.");

        var row = await registry.FindByCodeAsync(ProviderCode, ct)
            ?? throw new InvalidOperationException(
                $"Mistral OCR provider '{ProviderCode}' is not registered. Add a row in /admin/ai-providers with Code={ProviderCode}.");

        var baseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(row.BaseUrl) ? DefaultBaseUrl : row.BaseUrl);
        var model = string.IsNullOrWhiteSpace(row.DefaultModel) ? DefaultModel : row.DefaultModel;
        var apiKey = await registry.GetPlatformKeyAsync(ProviderCode, ct)
            ?? throw new InvalidOperationException($"Platform API key missing for provider {ProviderCode}. Paste a key into the '{ProviderCode}' row in /admin/ai-providers.");

        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeReason is not null) throw new InvalidOperationException(unsafeReason);

        var resolvedMime = string.IsNullOrWhiteSpace(mimeType) ? "application/pdf" : mimeType;
        var dataUrl = $"data:{resolvedMime};base64,{Convert.ToBase64String(documentBytes)}";
        var isImage = resolvedMime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var document = isImage
            ? new Dictionary<string, object?> { ["type"] = "image_url", ["image_url"] = dataUrl }
            : new Dictionary<string, object?> { ["type"] = "document_url", ["document_url"] = dataUrl };

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["document"] = document,
            ["include_image_base64"] = false,
        };

        var client = httpClientFactory.CreateClient("MistralOcrClient");
        client.BaseAddress = new Uri(baseUrl + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await client.PostAsync(
            "v1/ocr",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Mistral OCR failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 500)}");

        using var doc = JsonDocument.Parse(body);
        var sb = new StringBuilder();
        var pageCount = 0;
        if (doc.RootElement.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            foreach (var page in pages.EnumerateArray())
            {
                pageCount++;
                if (page.TryGetProperty("markdown", out var md) && md.ValueKind == JsonValueKind.String)
                {
                    sb.AppendLine(md.GetString());
                    sb.AppendLine();
                }
            }
        }

        // Prefer the provider-reported page count when present (usage_info).
        if (doc.RootElement.TryGetProperty("usage_info", out var usageInfo)
            && usageInfo.ValueKind == JsonValueKind.Object
            && usageInfo.TryGetProperty("pages_processed", out var pp)
            && pp.ValueKind == JsonValueKind.Number)
        {
            pageCount = pp.GetInt32();
        }

        var markdown = sb.ToString().Trim();
        if (markdown.Length == 0)
            throw new InvalidOperationException("Mistral OCR returned no extractable text for this document.");
        return new MistralOcrResult(markdown, pageCount, model);
    }

    /// <summary>Strip a trailing <c>/v1</c> (and trailing slash) so the caller can
    /// register the base URL either with or without the version segment.</summary>
    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].TrimEnd('/');
        return trimmed;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
