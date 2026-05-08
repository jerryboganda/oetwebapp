using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    Task<AiProviderTestResult> TestProviderAsync(string providerCode, CancellationToken ct);
    Task<AiProviderTestResult> TestAccountAsync(string providerId, string accountId, CancellationToken ct);
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

    public async Task<AiProviderTestResult> TestProviderAsync(string providerCode, CancellationToken ct)
    {
        var provider = await db.AiProviders
            .FirstOrDefaultAsync(p => p.Code == providerCode, ct)
            ?? throw new InvalidOperationException($"Unknown AI provider code '{providerCode}'.");

        var protector = dataProtection.CreateProtector("AiProvider.PlatformKey.v1");
        var apiKey = string.IsNullOrEmpty(provider.EncryptedApiKey)
            ? string.Empty
            : protector.Unprotect(provider.EncryptedApiKey);

        var result = await ProbeAsync(provider, apiKey, ct);
        provider.LastTestedAt = result.TestedAt;
        provider.LastTestStatus = result.Status;
        provider.LastTestError = result.ErrorMessage;
        provider.UpdatedAt = result.TestedAt;
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<AiProviderTestResult> TestAccountAsync(string providerId, string accountId, CancellationToken ct)
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

        var result = await ProbeAsync(provider, apiKey, ct);
        account.LastTestedAt = result.TestedAt;
        account.LastTestStatus = result.Status;
        account.LastTestError = result.ErrorMessage;
        account.UpdatedAt = result.TestedAt;
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<AiProviderTestResult> ProbeAsync(AiProvider provider, string apiKey, CancellationToken ct)
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

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(AiProviderConnectionTester));
            client.Timeout = ProbeTimeout;

            // Both Copilot (Azure AI Inference REST) and OpenAI-compatible
            // dialects accept POST {baseUrl}/chat/completions with a Bearer
            // token + a minimal messages payload. Keep tokens to 1 to make
            // the probe as cheap as possible.
            var baseUrl = provider.BaseUrl.TrimEnd('/');
            var url = baseUrl + "/chat/completions";
            var model = string.IsNullOrWhiteSpace(provider.DefaultModel)
                ? "openai/gpt-4o-mini"
                : provider.DefaultModel;

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            // Some Azure-style endpoints prefer api-key header; harmless to set both.
            req.Headers.TryAddWithoutValidation("api-key", apiKey);
            req.Content = JsonContent.Create(new
            {
                model,
                max_tokens = 1,
                messages = new[]
                {
                    new { role = "user", content = "ping" },
                },
            });

            using var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            stopwatch.Stop();
            return ClassifyResponse(response, stopwatch.ElapsedMilliseconds, startedAt);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogInformation(ex, "AI provider probe network failure for {Provider}", provider.Code);
            return new AiProviderTestResult(
                AiProviderTestStatuses.Network,
                Truncate(ex.Message, 512),
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
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
                Truncate(ex.Message, 512),
                (int)stopwatch.ElapsedMilliseconds,
                startedAt);
        }
    }

    internal static AiProviderTestResult ClassifyResponse(HttpResponseMessage response, long latencyMs, DateTimeOffset startedAt)
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
        string? message = null;
        try
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(body))
            {
                message = TryExtractErrorMessage(body) ?? Truncate(body, 256);
            }
        }
        catch { /* fail-soft; status code is enough */ }

        return new AiProviderTestResult(status, $"HTTP {(int)response.StatusCode}: {message ?? response.ReasonPhrase}", (int)latencyMs, startedAt);
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
}
