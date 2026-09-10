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

    /// <summary>
    /// Runs a full-pipeline chat completion for a specific model through the
    /// provider, rather than the cheap 1-token auth probe used by
    /// <see cref="TestProviderAsync"/>. Exercises the whole chain
    /// (connectivity → auth → model routing → a real completion with a
    /// meaningful response), which is how the UBAG admin board verifies a
    /// single facade model end-to-end. The result is persisted on the
    /// provider row just like the standard probe.</summary>
    Task<AiProviderModelTestResult> TestProviderModelAsync(string providerCode, string model, CancellationToken ct);
}

public sealed record AiProviderTestResult(
    string Status,
    string? ErrorMessage,
    int LatencyMs,
    DateTimeOffset TestedAt);

/// <summary>
/// Result of a full-pipeline model test. Mirrors <see cref="AiProviderTestResult"/>
/// plus a <c>model</c> echo and a small ordered list of pipeline steps so the
/// admin UI can show exactly which hop succeeded or failed.
/// see </summary>
public sealed record AiProviderModelTestResult(
    string Status,
    string? ErrorMessage,
    int LatencyMs,
    DateTimeOffset TestedAt,
    string Model,
    IReadOnlyList<AiModelTestStep> Steps);

public sealed record AiModelTestStep(string Step, string Detail, bool Ok);

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

    /// <summary>Budget for a full end-to-end model test. Real browser-backed
    /// pipelines (UBAG facade → worker → browser session → model) legitimately
    /// take 10–60s (live chatgpt_web/gemini_web probes measured 48–50s), and
    /// the facade itself waits up to 240s per call. The 300s API budget stays
    /// above the browser's 270s request budget and below a truly hung client,
    /// so a browser-job completion is never reported as "Request timed out".</summary>
    private static readonly TimeSpan ModelProbeTimeout = TimeSpan.FromSeconds(300);

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

    public async Task<AiProviderModelTestResult> TestProviderModelAsync(string providerCode, string model, CancellationToken ct)
    {
        var provider = await db.AiProviders
            .FirstOrDefaultAsync(p => p.Code == providerCode, ct)
            ?? throw new InvalidOperationException($"Unknown AI provider code '{providerCode}'.");

        async Task<AiProviderModelTestResult> CompleteAsync(AiProviderModelTestResult result)
        {
            provider.LastTestedAt = result.TestedAt;
            provider.LastTestStatus = result.Status;
            provider.LastTestError = result.ErrorMessage;
            provider.UpdatedAt = result.TestedAt;
            await db.SaveChangesAsync(CancellationToken.None);
            return result;
        }

        var steps = new List<AiModelTestStep>();
        var startedAt = clock.GetUtcNow();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var protector = dataProtection.CreateProtector("AiProvider.PlatformKey.v1");
        var apiKey = string.IsNullOrEmpty(provider.EncryptedApiKey)
            ? string.Empty
            : protector.Unprotect(provider.EncryptedApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            steps.Add(new AiModelTestStep("credential", "No API key configured.", false));
            stopwatch.Stop();
            return await CompleteAsync(new AiProviderModelTestResult(
                AiProviderTestStatuses.Auth,
                "No API key configured.",
                (int)stopwatch.ElapsedMilliseconds,
                startedAt, model, steps));
        }

        var unsafeReason = GetUnsafeBaseUrlReason(provider.BaseUrl);
        if (unsafeReason is not null)
        {
            steps.Add(new AiModelTestStep("endpoint", unsafeReason, false));
            stopwatch.Stop();
            return await CompleteAsync(new AiProviderModelTestResult(
                AiProviderTestStatuses.Unknown,
                unsafeReason,
                (int)stopwatch.ElapsedMilliseconds,
                startedAt, model, steps));
        }

        var targetModel = string.IsNullOrWhiteSpace(model)
            ? (string.IsNullOrWhiteSpace(provider.DefaultModel) ? "mock" : provider.DefaultModel)
            : model.Trim();

        if (provider.Category != AiProviderCategory.TextChat
            || provider.Dialect is not (AiProviderDialect.OpenAiCompatible or AiProviderDialect.Cloudflare or AiProviderDialect.Copilot or AiProviderDialect.Anthropic))
        {
            steps.Add(new AiModelTestStep("pipeline",
                $"Full-pipeline model test is only available for text-chat OpenAI-compatible/Copilot/Anthropic providers.", false));
            stopwatch.Stop();
            return await CompleteAsync(new AiProviderModelTestResult(
                AiProviderTestStatuses.Unknown,
                "Full-pipeline model test is not available for this provider category/dialect.",
                (int)stopwatch.ElapsedMilliseconds,
                startedAt, targetModel, steps));
        }

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(AiProviderConnectionTester));
            client.Timeout = ModelProbeTimeout;
            steps.Add(new AiModelTestStep("connectivity", provider.BaseUrl, true));

            var req = BuildChatCompletionsProbe(provider.BaseUrl, apiKey, targetModel, fullPipeline: true);
            using var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            stopwatch.Stop();
            var latencyMs = (int)stopwatch.ElapsedMilliseconds;

            if (!response.IsSuccessStatusCode)
            {
                var relResult = await ClassifyResponseAsync(response, latencyMs, startedAt, apiKey, ct);
                var failedStep = relResult.Status == AiProviderTestStatuses.Auth
                    ? "auth"
                    : relResult.Status == AiProviderTestStatuses.RateLimited
                        ? "provider"
                        : "completion";
                steps.Add(new AiModelTestStep(
                    failedStep,
                    $"HTTP {(int)response.StatusCode}: {relResult.ErrorMessage ?? response.ReasonPhrase}",
                    false));
                return await CompleteAsync(new AiProviderModelTestResult(
                    relResult.Status, relResult.ErrorMessage, latencyMs, startedAt, targetModel, steps));
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var emptyFinish = TryReadEmptyCompletionText(body);
            if (emptyFinish is not null)
            {
                steps.Add(new AiModelTestStep("completion", "chat completion returned a 2xx response", true));
                var emptyMessage = $"The provider returned an empty completion for '{targetModel}' (finish_reason={emptyFinish}). The browser job finished but produced no text — retry the test.";
                steps.Add(new AiModelTestStep("model", emptyMessage, false));
                return await CompleteAsync(new AiProviderModelTestResult(
                    AiProviderTestStatuses.RateLimited,
                    emptyMessage,
                    latencyMs,
                    startedAt, targetModel, steps));
            }
            steps.Add(new AiModelTestStep("completion", "chat completion returned a 2xx response", true));

            // Surface a model-routing warning when the configured model is
            // absent from the facade model list whenever the list is available.
            var warning = await TryBuildModelWarningFromBodyAsync(body, targetModel);
            if (warning is not null)
                steps.Add(new AiModelTestStep("model", warning, false));
            else
                steps.Add(new AiModelTestStep("model", $"{targetModel} acknowledged", true));

            var result = new AiProviderModelTestResult(
                AiProviderTestStatuses.Ok,
                warning,
                latencyMs,
                startedAt,
                targetModel,
                steps);

            return await CompleteAsync(result);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogInformation(ex, "AI provider model test network failure for {Provider}/{Model}", provider.Code, targetModel);
            var message = Truncate(RedactSecrets(ex.Message, apiKey), 512);
            steps.Add(new AiModelTestStep("completion", message, false));
            return await CompleteAsync(new AiProviderModelTestResult(
                AiProviderTestStatuses.Network,
                message,
                (int)stopwatch.ElapsedMilliseconds,
                startedAt, targetModel, steps));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            steps.Add(new AiModelTestStep("completion", "Request timed out.", false));
            return await CompleteAsync(new AiProviderModelTestResult(
                AiProviderTestStatuses.Network,
                "Request timed out.",
                (int)stopwatch.ElapsedMilliseconds,
                startedAt, targetModel, steps));
        }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "AI provider model test unknown failure for {Provider}/{Model}", provider.Code, targetModel);
            var message = Truncate(RedactSecrets(ex.Message, apiKey), 512);
            steps.Add(new AiModelTestStep("completion", message, false));
            return await CompleteAsync(new AiProviderModelTestResult(
                AiProviderTestStatuses.Unknown,
                message,
                (int)stopwatch.ElapsedMilliseconds,
                startedAt, targetModel, steps));
        }
    }

    /// <summary>Reads a 2xx OpenAI-style completion body and returns the
    /// finish reason when the assistant message carries no text (null/empty
    /// string content). A 200 with no text is a real facade failure mode
    /// (browser job finished, nothing extracted) — the caller must not report
    /// it as green. Returns null when the body has text or is unparseable
    /// (fail-open: the normal path decides).</summary>
    internal static string? TryReadEmptyCompletionText(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return null;
            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object)
                return null;
            if (message.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(content.GetString()))
                    return null;
                if (content.ValueKind != JsonValueKind.String
                    && content.ValueKind != JsonValueKind.Null)
                    return null;
            }
            var finish = "stop";
            if (choice.TryGetProperty("finish_reason", out var finishEl)
                && finishEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(finishEl.GetString()))
                finish = finishEl.GetString()!.Trim();
            return finish;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses an OpenAI-style <c>{ data: [{ id }] }</c> model list from
    /// a completion body when the response happens to include one, and returns
    /// a warning when the target model is absent. Fail-soft: returns null on
    /// any parse miss, unknown shape, or non-JSON body.</summary>
    private static async Task<string?> TryBuildModelWarningFromBodyAsync(string body, string expectedModel)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(expectedModel)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            // A true completion body has `choices`; a model-list/metadata body
            // has `data`. Only the data array informs a model-routing warning.
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                var found = false;
                foreach (var m in data.EnumerateArray())
                {
                    if (m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                        && string.Equals(id.GetString(), expectedModel, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return $"The model '{expectedModel}' was not in the provider's advertised model list.";
            }
        }
        catch { /* not JSON */ }
        return null;
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
            var result = await ClassifyResponseAsync(
                response,
                stopwatch.ElapsedMilliseconds,
                startedAt,
                apiKey,
                ct);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    internal static async Task<AiProviderTestResult> ClassifyResponseAsync(
        HttpResponseMessage response,
        long latencyMs,
        DateTimeOffset startedAt,
        string? apiKey = null,
        CancellationToken ct = default)
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
            // The UBAG facade reports terminal browser-job failures as 503
            // (provider_transient / provider_login_required) and wait-budget
            // overruns as 504: the pipeline is reachable + authenticated, so
            // this is a provider-side condition, not an OET network fault.
            // Surface the facade's own detail (login drift, wait timeout)
            // instead of a bare HTTP status.
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => AiProviderTestStatuses.RateLimited,
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
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var redactedBody = RedactSecrets(body, apiKey) ?? string.Empty;
                message = TryExtractErrorMessage(redactedBody) ?? Truncate(redactedBody, 256);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    /// <summary>
    /// Exact hostnames allowed to serve provider traffic over plain HTTP on the
    /// internal compose network (e.g. UBAG gateway, agent-gateway). Configured
    /// via <c>OET_INTERNAL_AI_HOSTS</c> (comma-separated, e.g.
    /// <c>"oet-agent-gateway,ubag-vps-gateway-1"</c>). Exact match only — no
    /// suffixes, no IP literals. Read per call (cheap) so container env changes
    /// apply without a restart. Empty/unset = no exception, prior behaviour.
    /// NOTE: allowlisting a host also makes any existing row pointing at it
    /// (e.g. the seeded <c>antigravity-gateway</c> row) callable through this
    /// guarded path for the first time — verify its routes deliberately.
    /// </summary>
    internal static bool IsInternalAiHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;
        var configured = Environment.GetEnvironmentVariable("OET_INTERNAL_AI_HOSTS");
        if (string.IsNullOrWhiteSpace(configured))
            return false;
        var candidate = host.Trim().TrimEnd('.');
        return configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(h => h.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    internal static string? GetUnsafeBaseUrlReason(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "Provider BaseUrl is not configured.";

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return "Provider BaseUrl must be an absolute https:// URL.";

        var host = (uri.Host ?? string.Empty).Trim().TrimEnd('.');
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            // Trusted container-network peer: skip the public-internet checks
            // below (scheme, IP-literal, and DNS-resolution rules) for this
            // exact configured hostname only.
            if (uri.Scheme == Uri.UriSchemeHttp && IsInternalAiHost(host))
                return null;
            return "Provider BaseUrl must use https://.";
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
            return "Provider BaseUrl host is required.";

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

    private static HttpRequestMessage BuildChatCompletionsProbe(string baseUrl, string apiKey, string? defaultModel, bool fullPipeline = false)
    {
        var url = new Uri(new Uri(TrimBase(baseUrl) + "/", UriKind.Absolute), "chat/completions");
        var model = string.IsNullOrWhiteSpace(defaultModel) ? "openai/gpt-4o-mini" : defaultModel;
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        // Some Azure-style endpoints prefer api-key header; harmless to set both.
        req.Headers.TryAddWithoutValidation("api-key", apiKey);
        // Unique nonce per probe so the UBAG facade's derived idempotency key
        // can never collide with a previous probe's record: without it, a
        // retest of the same model replays the SAME native job ID, and after
        // any fingerprint-scheme change the store answers CONFLICT instead.
        // Harmless to every other OpenAI-compatible provider (extra field
        // ignored), and it keeps each admin Test click an independent run.
        req.Content = JsonContent.Create(new
        {
            model,
            max_tokens = fullPipeline ? 16 : 1,
            messages = new[]
            {
                new { role = "system", content = "Reply with the single word OK." },
                new { role = "user", content = fullPipeline ? "ping — reply with a single word." : "ping" },
            },
            ubag_nonce = Guid.NewGuid().ToString("N"),
        });
        return req;
    }

    private static HttpRequestMessage BuildAnthropicProbe(string baseUrl, string apiKey, string? defaultModel)
    {
        // Anthropic native: x-api-key + anthropic-version, POST /v1/messages.
        // Share the runtime provider's base-URL normalization so the probe
        // predicts runtime behavior exactly (bare host or /v1-suffixed).
        var url = new Uri(AnthropicProvider.NormalizeBaseUrl(baseUrl) + "/v1/messages", UriKind.Absolute);
        var model = string.IsNullOrWhiteSpace(defaultModel) ? "claude-sonnet-5" : defaultModel;
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
