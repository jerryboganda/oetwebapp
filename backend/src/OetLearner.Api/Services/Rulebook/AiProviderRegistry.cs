using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Rulebook;

// ═════════════════════════════════════════════════════════════════════════════
// Registry-backed providers (Slice 5). Resolve config from the DB at call
// time so admins can rotate keys, change base URLs, and add providers
// without a redeploy.
//
// Two concrete dialects are supported out of the box:
//  • OpenAI-compatible (DigitalOcean Serverless, OpenAI Platform, OpenRouter,
//    DeepSeek, Groq, Together, Gemini OpenAI-compat endpoint, …).
//  • Anthropic (native Messages API).
// ═════════════════════════════════════════════════════════════════════════════

public interface IAiProviderRegistry
{
    Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct);
    Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct);
    /// <summary>Return platform-held API key plaintext, decrypted.</summary>
    Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct);
}

public sealed class AiProviderRegistry(LearnerDbContext db, IDataProtectionProvider dpProvider)
    : IAiProviderRegistry
{
    private const string ProtectorPurpose = "AiProvider.PlatformKey.v1";
    private readonly IDataProtector _protector = dpProvider.CreateProtector(ProtectorPurpose);

    public async Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim().ToLowerInvariant();
        return await db.AiProviders.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code && p.IsActive, ct);
    }

    public async Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
        => await db.AiProviders.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.FailoverPriority)
            .ToListAsync(ct);

    public async Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct)
    {
        var p = await FindByCodeAsync(providerCode, ct);
        if (p is null || string.IsNullOrEmpty(p.EncryptedApiKey)) return null;
        try { return _protector.Unprotect(p.EncryptedApiKey); }
        catch { return null; }
    }
}

/// <summary>
/// Universal provider: chooses dispatch by the <see cref="AiProvider.Dialect"/>
/// column so one registered row can target any OpenAI-compatible endpoint
/// (DO Serverless, OpenAI, OpenRouter, …) without adding a new class.
/// </summary>
public sealed class RegistryBackedProvider(
    IHttpClientFactory httpClientFactory,
    IAiProviderRegistry registry) : IAiModelProvider
{
    public string Name => "registry";

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        // When the caller did not supply a provider-specific override, we
        // default to whatever the global policy names. The gateway hands us
        // that resolution via BaseUrlOverride / ApiKeyOverride already —
        // but if neither is provided we also need a sensible default.
        var (baseUrl, apiKey) = await ResolveCredentialsAsync(request, ct);

        // Dialect dispatch. Only two paths today; adding a new dialect =
        // one switch-arm + one method.
        // For now we default to OpenAI-compatible; Anthropic takes a
        // different request/response shape handled separately.
        return await CallOpenAiCompatibleAsync(baseUrl, apiKey, request, ct);
    }

    private async Task<(string baseUrl, string apiKey)> ResolveCredentialsAsync(AiProviderRequest request, CancellationToken ct)
    {
        var baseUrl = request.BaseUrlOverride;
        var apiKey = request.ApiKeyOverride;
        if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey))
            return (baseUrl, apiKey);

        // No BYOK override — fall back to the registry row matching the
        // model's implicit provider. Without extra hints we pick the first
        // active provider, which is what Slice 2's global policy says to do.
        var providers = await registry.ListActiveAsync(ct);
        var first = providers.FirstOrDefault();
        if (first is null)
            throw new InvalidOperationException("No active AI provider registered.");

        baseUrl ??= first.BaseUrl;
        apiKey ??= await registry.GetPlatformKeyAsync(first.Code, ct)
            ?? throw new InvalidOperationException($"Platform API key missing for provider {first.Code}.");
        return (baseUrl, apiKey);
    }

    private async Task<AiProviderCompletion> CallOpenAiCompatibleAsync(
        string baseUrl, string apiKey, AiProviderRequest request, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("AiRegistryClient");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var model = request.Model;
        var maxTokens = request.MaxTokens ?? 4096;
        var sendReasoning = IsReasoningCapable(model);

        object payload = sendReasoning
            ? new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt },
                },
                temperature = request.Temperature,
                max_tokens = maxTokens,
                reasoning_effort = "high",
                stream = false,
            }
            : new
            {
                model,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt },
                },
                temperature = request.Temperature,
                max_tokens = maxTokens,
                stream = false,
            };

        using var response = await client.PostAsync(
            "chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"AI provider call failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var usage = root.TryGetProperty("usage", out var usageEl)
            ? new AiUsage
            {
                PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ctEl) ? ctEl.GetInt32() : 0,
            }
            : null;

        return new AiProviderCompletion { Text = text, Usage = usage };
    }

    private static bool IsReasoningCapable(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var m = model.ToLowerInvariant();
        if (m.Contains("claude-opus") || m.Contains("claude-4") || m.Contains("claude-5")) return true;
        if (m.Contains("opus-4")) return true;
        if (m.Contains("openai-o1") || m.Contains("openai-o3") || m.Contains("openai-o4")) return true;
        if (m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4")) return true;
        if (m.Contains("gpt-5")) return true;
        if (m.Contains("thinking")) return true;
        return false;
    }
}

/// <summary>
/// Anthropic Messages API adapter. Separate from the OpenAI-compatible
/// path because Anthropic uses <c>x-api-key</c> header, a different
/// request body shape, and a different response structure.
/// </summary>
public sealed class AnthropicProvider(
    IHttpClientFactory httpClientFactory,
    IAiProviderRegistry registry) : IAiModelProvider
{
    public string Name => "anthropic";

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var baseUrl = request.BaseUrlOverride;
        var apiKey = request.ApiKeyOverride;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            var registered = await registry.FindByCodeAsync("anthropic", ct);
            if (registered is null)
                throw new InvalidOperationException("Anthropic provider is not registered.");
            baseUrl ??= registered.BaseUrl;
            apiKey ??= await registry.GetPlatformKeyAsync("anthropic", ct)
                ?? throw new InvalidOperationException("Platform API key missing for Anthropic.");
        }

        var client = httpClientFactory.CreateClient("AiRegistryClient");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Remove("anthropic-version");
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var payload = new
        {
            model = request.Model,
            system = request.SystemPrompt,
            messages = new object[]
            {
                new { role = "user", content = request.UserPrompt },
            },
            temperature = request.Temperature,
            max_tokens = request.MaxTokens ?? 1024,
        };

        using var response = await client.PostAsync(
            "messages",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Anthropic call failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var contentParts = root.GetProperty("content");
        var sb = new StringBuilder();
        foreach (var part in contentParts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }

        AiUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageEl))
        {
            usage = new AiUsage
            {
                PromptTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
            };
        }

        return new AiProviderCompletion { Text = sb.ToString(), Usage = usage };
    }
}
