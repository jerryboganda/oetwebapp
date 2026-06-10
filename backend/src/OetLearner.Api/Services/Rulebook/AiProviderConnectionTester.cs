using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>
/// Phase 4 — admin-initiated connectivity probe. Sends a minimal chat
/// completion request to the provider so the admin UI can show
/// "✅ ok / 🔒 auth / ⏱️ rate-limited / 🌐 network / ❓ unknown" pills next
/// to each <see cref="AiProvider"/> and <see cref="AiProviderAccount"/>.
///
/// <para>
/// <b>Deliberately bypasses</b> <see cref="IAiGatewayService"/>: we do
/// not want a connectivity check to consume per-user quota, write an
/// <c>AiUsageRecord</c>, or apply the rulebook grounding policy. This is
/// pure ops plumbing — the gateway invariants only apply to learner
/// traffic. A failed probe never mutates account quarantine state either;
/// admins can clear those explicitly via the existing reset endpoint.
/// </para>
/// </summary>
public interface IAiProviderConnectionTester
{
    /// <param name="deep">When true and the provider supports it (Mistral OCR),
    /// run a functional probe that exercises the real endpoint with a tiny
    /// embedded document rather than a cheap auth-only metadata read.</param>
    Task<AiProviderTestResult> TestProviderAsync(string providerCode, CancellationToken ct, bool deep = false);
    Task<AiProviderTestResult> TestAccountAsync(string providerId, string accountId, CancellationToken ct, bool deep = false);
}

public sealed record AiProviderTestResult(
    string Status,
    string? ErrorMessage,
    int LatencyMs,
    DateTimeOffset TestedAt);

public static class AiProviderTestStatuses
{
    public const string Ok = "ok";
    public const string Auth = "auth";
    public const string RateLimited = "rate_limited";
    public const string Network = "network";
    public const string Unknown = "unknown";
}

public sealed class AiProviderConnectionTester(
    LearnerDbContext db,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpClientFactory,
    TimeProvider clock,
    ILogger<AiProviderConnectionTester> logger) : IAiProviderConnectionTester
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public async Task<AiProviderTestResult> TestProviderAsync(string providerCode, CancellationToken ct, bool deep = false)
    {
        var provider = await db.AiProviders
            .FirstOrDefaultAsync(p => p.Code == providerCode, ct)
            ?? throw new InvalidOperationException($"Unknown AI provider code '{providerCode}'.");

        var protector = dataProtection.CreateProtector("AiProvider.PlatformKey.v1");
        var apiKey = string.IsNullOrEmpty(provider.EncryptedApiKey)
            ? string.Empty
            : protector.Unprotect(provider.EncryptedApiKey);

        var result = await ProbeAsync(provider, apiKey, ct, deep);
        provider.LastTestedAt = result.TestedAt;
        provider.LastTestStatus = result.Status;
        provider.LastTestError = result.ErrorMessage;
        provider.UpdatedAt = result.TestedAt;
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AiProviderTestResult> TestAccountAsync(string providerId, string accountId, CancellationToken ct, bool deep = false)
    {
        var provider = await db.AiProviders.FirstOrDefaultAsync(p => p.Id == providerId, ct)
            ?? throw new InvalidOperationException($"Unknown AI provider id '{providerId}'.");
        var account = await db.AiProviderAccounts
            .FirstOrDefaultAsync(a => a.Id == accountId && a.ProviderId == providerId, ct)
            ?? throw new InvalidOperationException($"Unknown AI provider account '{accountId}'.");

        var protector = dataProtection.CreateProtector("AiProvider.PlatformKey.v1");
        var apiKey = string.IsNullOrEmpty(account.EncryptedApiKey)
            ? string.Empty
            : protector.Unprotect(account.EncryptedApiKey);

        var result = await ProbeAsync(provider, apiKey, ct, deep);
        account.LastTestedAt = result.TestedAt;
        account.LastTestStatus = result.Status;
        account.LastTestError = result.ErrorMessage;
        account.UpdatedAt = result.TestedAt;
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<AiProviderTestResult> ProbeAsync(AiProvider provider, string apiKey, CancellationToken ct, bool deep)
    {
        var startedAt = clock.GetUtcNow();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiProviderTestResult(
                AiProviderTestStatuses.Auth,
                "No API key configured.",
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }

        var unsafeReason = GetUnsafeBaseUrlReason(provider.BaseUrl);
        if (unsafeReason is not null)
        {
            return new AiProviderTestResult(
                AiProviderTestStatuses.Unknown,
                unsafeReason,
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }

        var plan = BuildProbeRequest(provider, apiKey, deep);
        if (plan is null)
        {
            return new AiProviderTestResult(
                AiProviderTestStatuses.Unknown,
                $"Connection test is not available for {provider.Category}/{provider.Dialect} providers yet.",
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(AiProviderConnectionTester));
            client.Timeout = ProbeTimeout;

            using var req = plan.Request;
            using var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            stopwatch.Stop();
            var result = ClassifyResponse(response, stopwatch.ElapsedMilliseconds, startedAt, apiKey);
            // On a successful auth/metadata probe, optionally surface a
            // non-fatal warning when the configured DefaultModel is absent
            // from the provider's advertised model list. Status stays "ok"
            // (green pill); the message is informational only.
            if (result.Status == AiProviderTestStatuses.Ok && plan.VerifyModel is not null)
            {
                var warning = await TryBuildModelWarningAsync(response, plan.VerifyModel!, ct);
                if (warning is not null)
                    result = result with { ErrorMessage = warning };
            }
            return result;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogInformation(ex, "AI provider probe network failure for {Provider}", provider.Code);
            return new AiProviderTestResult(
                AiProviderTestStatuses.Network,
                Truncate(RedactSecrets(ex.Message, apiKey), 512),
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new AiProviderTestResult(
                AiProviderTestStatuses.Network,
                "Request timed out.",
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "AI provider probe unknown failure for {Provider}", provider.Code);
            return new AiProviderTestResult(
                AiProviderTestStatuses.Unknown,
                Truncate(RedactSecrets(ex.Message, apiKey), 512),
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }
    }

    internal static AiProviderTestResult ClassifyResponse(HttpResponseMessage response, long latencyMs, DateTimeOffset startedAt, string? apiKey = null)
    {
        if ((int)response.StatusCode is >= 200 and < 300)
        {
            return new AiProviderTestResult(AiProviderTestStatuses.Ok, null, (int)latencyMs, startedAt);
        }

        var status = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => AiProviderTestStatuses.Auth,
            HttpStatusCode.Forbidden => AiProviderTestStatuses.Auth,
            HttpStatusCode.TooManyRequests => AiProviderTestStatuses.RateLimited,
            >= HttpStatusCode.InternalServerError => AiProviderTestStatuses.Network,
            _ => AiProviderTestStatuses.Unknown,
        };

        // Best-effort body sniff for a friendlier message; never blocking.
        // Redact BEFORE truncating so a secret straddling the 256-byte
        // body cutoff cannot survive as a partial token (matches the
        // exception-path ordering above).
        string? message = null;
        try
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(body))
            {
                var redactedBody = RedactSecrets(body, apiKey) ?? string.Empty;
                message = TryExtractErrorMessage(redactedBody) ?? Truncate(redactedBody, 256);
            }
        }
        catch { /* fail-soft; status code is enough */ }

        var raw = $"HTTP {(int)response.StatusCode}: {message ?? response.ReasonPhrase}";
        return new AiProviderTestResult(status, RedactSecrets(raw, apiKey), (int)latencyMs, startedAt);
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return Truncate(err.GetString(), 256);
                if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    return Truncate(m.GetString(), 256);
            }
        }
        catch { /* not JSON */ }
        return null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }

    // RW-019 redaction: providers can echo the offending Authorization
    // header back in error bodies. We persist LastTestError into the
    // database AND return it in the admin "test connection" payload, so
    // anything that lands here must have the live secret + any common
    // PAT/key patterns scrubbed BEFORE persistence/serialisation. This
    // is the only place these strings are produced inside the tester.
    internal static string? RedactSecrets(string? value, string? apiKey)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var result = value;
        if (!string.IsNullOrWhiteSpace(apiKey) && apiKey.Length >= 8)
        {
            // Replace the literal decrypted key first — covers the case
            // where the provider echoed the exact Authorization value.
            result = result.Replace(apiKey, "***REDACTED***", StringComparison.Ordinal);
        }

        // Generic PAT/key shapes (case-insensitive). Patterns are anchored
        // on the documented prefixes for the providers we currently support
        // plus the most common third-party prefixes so admins do not see
        // raw secret material in error messages even if the offending key
        // belongs to a different provider whose body got mirrored back.
        result = SecretPatterns.Replace(result, "***REDACTED***");
        return result;
    }

    internal static string? GetUnsafeBaseUrlReason(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "Provider BaseUrl is not configured.";

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return "Provider BaseUrl must be an absolute https:// URL.";

        if (uri.Scheme != Uri.UriSchemeHttps)
            return "Provider BaseUrl must use https://.";

        if (string.IsNullOrWhiteSpace(uri.Host))
            return "Provider BaseUrl host is required.";

        var host = uri.Host.Trim().TrimEnd('.');
        if (uri.IsLoopback
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return "Provider BaseUrl host is not allowed.";
        }

        if (IPAddress.TryParse(host, out _))
            return "Provider BaseUrl must use an external DNS hostname, not an IP address literal.";

        if (!host.Contains('.', StringComparison.Ordinal))
            return "Provider BaseUrl must use a fully qualified external DNS hostname.";

        try
        {
            foreach (var address in Dns.GetHostAddresses(host))
            {
                if (IsUnsafeIpAddress(address))
                    return "Provider BaseUrl host resolves to a non-public address.";
            }
        }
        catch (SocketException)
        {
            // Save-time validation should not require the host to resolve from
            // every admin workstation/test environment. The outbound call path
            // repeats this guard immediately before sending provider traffic.
        }

        return null;
    }

    private static bool IsUnsafeIpAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast;

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return true;

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
               || bytes[0] == 10
               || bytes[0] == 127
               || (bytes[0] == 169 && bytes[1] == 254)
               || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
               || (bytes[0] == 192 && bytes[1] == 168)
               || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
               || (bytes[0] == 198 && bytes[1] is 18 or 19)
               || address.Equals(IPAddress.Broadcast);
    }

    private sealed record ProbePlan(HttpRequestMessage Request, string? VerifyModel);

    /// <summary>
    /// Builds the cheapest credential-validating request for the provider's
    /// (Category, Dialect). Returns null when no probe is defined for the pair —
    /// the caller then surfaces the explicit "not available yet" status so the
    /// admin sees a neutral pill rather than a false failure. All requests are
    /// classified by the shared <see cref="ClassifyResponse"/> (ok/auth/
    /// rate_limited/network/unknown) with RW-019 secret redaction applied.
    /// </summary>
    private static ProbePlan? BuildProbeRequest(AiProvider provider, string apiKey, bool deep)
    {
        var baseUrl = provider.BaseUrl ?? string.Empty;

        // ── Text chat / contextual LLM ──────────────────────────────────────
        if (provider.Category == AiProviderCategory.TextChat)
        {
            return provider.Dialect switch
            {
                AiProviderDialect.Anthropic =>
                    new ProbePlan(BuildAnthropicProbe(baseUrl, apiKey, provider.DefaultModel), null),
                AiProviderDialect.OpenAiCompatible or AiProviderDialect.Cloudflare or AiProviderDialect.Copilot =>
                    new ProbePlan(BuildChatCompletionsProbe(baseUrl, apiKey, provider.DefaultModel), null),
                AiProviderDialect.GeminiNative =>
                    new ProbePlan(BuildGeminiModelsProbe(baseUrl, apiKey), null),
                _ => null,
            };
        }

        // ── OCR / structured PDF extraction (Mistral document OCR) ──────────
        if (provider.Category is AiProviderCategory.Ocr or AiProviderCategory.PdfExtraction)
        {
            return deep
                ? new ProbePlan(BuildOcrDeepProbe(baseUrl, apiKey, provider.DefaultModel), null)
                : new ProbePlan(BuildBearerGetProbe(StripTrailingV1(baseUrl) + "/v1/models", apiKey), null);
        }

        // ── Speech-to-text / TTS / phoneme ──────────────────────────────────
        return provider.Dialect switch
        {
            // OpenAI-compatible transcription hosts expose GET {base}/models.
            // Free auth+connectivity check; sub-100ms silent-WAV POSTs are
            // rejected by several compatible hosts (false "unknown").
            AiProviderDialect.WhisperAsr =>
                new ProbePlan(BuildBearerGetProbe(TrimBase(baseUrl) + "/models", apiKey), provider.DefaultModel),
            AiProviderDialect.ElevenLabsTts =>
                new ProbePlan(BuildHeaderGetProbe(TrimBase(baseUrl) + "/voices", "xi-api-key", apiKey), null),
            AiProviderDialect.ElevenLabsStt =>
                new ProbePlan(BuildHeaderGetProbe(TrimBase(baseUrl) + "/user", "xi-api-key", apiKey), null),
            AiProviderDialect.AzureAsr or AiProviderDialect.AzureTts or AiProviderDialect.AzurePhoneme =>
                BuildAzureTokenProbe(baseUrl, apiKey) is { } azureReq ? new ProbePlan(azureReq, null) : null,
            AiProviderDialect.GeminiNative =>
                new ProbePlan(BuildGeminiModelsProbe(baseUrl, apiKey), null),
            _ => null,
        };
    }

    private static HttpRequestMessage BuildChatCompletionsProbe(string baseUrl, string apiKey, string? defaultModel)
    {
        var url = new Uri(new Uri(TrimBase(baseUrl) + "/", UriKind.Absolute), "chat/completions");
        var model = string.IsNullOrWhiteSpace(defaultModel) ? "openai/gpt-4o-mini" : defaultModel;
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        // Some Azure-style endpoints prefer api-key header; harmless to set both.
        req.Headers.TryAddWithoutValidation("api-key", apiKey);
        req.Content = JsonContent.Create(new
        {
            model,
            max_tokens = 1,
            messages = new[] { new { role = "user", content = "ping" } },
        });
        return req;
    }

    private static HttpRequestMessage BuildAnthropicProbe(string baseUrl, string apiKey, string? defaultModel)
    {
        // Anthropic native: x-api-key + anthropic-version, POST /v1/messages.
        // Share the runtime provider's base-URL normalization so the probe
        // predicts runtime behavior exactly (bare host or /v1-suffixed).
        var url = new Uri(AnthropicProvider.NormalizeBaseUrl(baseUrl) + "/v1/messages", UriKind.Absolute);
        var model = string.IsNullOrWhiteSpace(defaultModel) ? "claude-sonnet-4-6" : defaultModel;
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        req.Content = JsonContent.Create(new
        {
            model,
            max_tokens = 1,
            messages = new[] { new { role = "user", content = "ping" } },
        });
        return req;
    }

    private static HttpRequestMessage BuildBearerGetProbe(string url, string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Absolute));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Headers.TryAddWithoutValidation("api-key", apiKey);
        return req;
    }

    private static HttpRequestMessage BuildHeaderGetProbe(string url, string headerName, string apiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Absolute));
        req.Headers.TryAddWithoutValidation(headerName, apiKey);
        return req;
    }

    private static HttpRequestMessage BuildGeminiModelsProbe(string baseUrl, string apiKey)
    {
        var trimmed = TrimBase(baseUrl);
        var url = trimmed.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase)
                  || trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed + "/models"
            : trimmed + "/v1beta/models";
        var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Absolute));
        req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        return req;
    }

    private static HttpRequestMessage? BuildAzureTokenProbe(string baseUrl, string apiKey)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return null;
        var region = uri.Host.Split('.', 2)[0];
        if (string.IsNullOrWhiteSpace(region)) return null;
        var url = new Uri($"https://{region}.api.cognitive.microsoft.com/sts/v1.0/issueToken", UriKind.Absolute);
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", apiKey);
        req.Content = new StringContent(string.Empty);
        return req;
    }

    private static HttpRequestMessage BuildOcrDeepProbe(string baseUrl, string apiKey, string? defaultModel)
    {
        var url = new Uri(StripTrailingV1(baseUrl) + "/v1/ocr", UriKind.Absolute);
        var model = string.IsNullOrWhiteSpace(defaultModel) ? "mistral-ocr-latest" : defaultModel;
        var dataUrl = "data:application/pdf;base64," + Convert.ToBase64String(BuildTinyProbePdf());
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = JsonContent.Create(new
        {
            model,
            document = new { type = "document_url", document_url = dataUrl },
            include_image_base64 = false,
        });
        return req;
    }

    private static string TrimBase(string baseUrl) => (baseUrl ?? string.Empty).Trim().TrimEnd('/');

    private static string StripTrailingV1(string baseUrl)
    {
        var t = TrimBase(baseUrl);
        if (t.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) t = t[..^3].TrimEnd('/');
        return t;
    }

    /// <summary>Assembles a valid minimal 1-page PDF (with correct xref
    /// offsets computed at build time) for the deep OCR probe — ~400 bytes,
    /// one line of text, so Mistral OCR returns a real 2xx page result.</summary>
    private static byte[] BuildTinyProbePdf()
    {
        const string streamContent = "BT /F1 18 Tf 20 100 Td (OET OCR TEST 123) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(streamContent)} >>\nstream\n{streamContent}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int>(objects.Length);
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length); // ASCII-only → byte count == char count
            sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        var xrefOffset = sb.Length;
        sb.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        foreach (var off in offsets)
            sb.Append(off.ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(objects.Length + 1)
          .Append(" /Root 1 0 R >>\nstartxref\n").Append(xrefOffset).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Best-effort: parse an OpenAI-style <c>{ data: [{ id }] }</c>
    /// model list and warn when the configured model is absent. Fail-soft —
    /// any parse issue or unknown shape returns null (no warning).</summary>
    private static async Task<string?> TryBuildModelWarningAsync(HttpResponseMessage response, string expectedModel, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedModel)) return null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;
            var found = false;
            var any = false;
            foreach (var m in data.EnumerateArray())
            {
                if (!m.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                any = true;
                if (string.Equals(id.GetString(), expectedModel, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            }
            if (!any || found) return null;
            return $"Key works, but the configured model '{expectedModel}' was not in the provider's model list.";
        }
        catch { return null; }
    }

    private static readonly Regex SecretPatterns = new(
        @"(?:github_pat_[A-Za-z0-9_]{20,}|ghp_[A-Za-z0-9]{20,}|gho_[A-Za-z0-9]{20,}|ghu_[A-Za-z0-9]{20,}|ghs_[A-Za-z0-9]{20,}|ghr_[A-Za-z0-9]{20,}|sk-ant-[A-Za-z0-9_\-]{20,}|sk-proj-[A-Za-z0-9_\-]{20,}|sk-[A-Za-z0-9_\-]{20,}|AIza[0-9A-Za-z_\-]{20,}|xox[baprs]-[A-Za-z0-9\-]{20,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(50));
}
