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
    IAiProviderRegistry registry,
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.AiProviderOptions> options) : IAiModelProvider
{
    public string Name => "registry";

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var (baseUrl, apiKey, reasoningEffort) = await ResolveCredentialsAsync(request, ct);
        return await CallOpenAiCompatibleAsync(baseUrl, apiKey, reasoningEffort, request, ct);
    }

    private async Task<(string baseUrl, string apiKey, string? reasoningEffort)> ResolveCredentialsAsync(AiProviderRequest request, CancellationToken ct)
    {
        var baseUrl = request.BaseUrlOverride;
        var apiKey = request.ApiKeyOverride;
        string? reasoningEffort = null;

        // Filter by OpenAI-compatible dialect so a Cloudflare or Anthropic row
        // sitting at a lower failover priority does not get called via the
        // OpenAI dispatch path with a request shape it cannot understand.
        var providers = (await registry.ListActiveAsync(ct))
            .Where(p => p.Dialect == AiProviderDialect.OpenAiCompatible)
            .ToList();
        var first = providers.FirstOrDefault();
        if (first is not null)
        {
            // Per-provider ReasoningEffort overrides env default when set.
            if (!string.IsNullOrWhiteSpace(first.ReasoningEffort))
                reasoningEffort = first.ReasoningEffort!.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey))
            return (baseUrl, apiKey, reasoningEffort ?? options.Value.ReasoningEffort);

        if (first is null)
            throw new InvalidOperationException("No active OpenAI-compatible AI provider registered.");

        baseUrl ??= first.BaseUrl;
        apiKey ??= await registry.GetPlatformKeyAsync(first.Code, ct)
            ?? throw new InvalidOperationException($"Platform API key missing for provider {first.Code}.");
        return (baseUrl, apiKey, reasoningEffort ?? options.Value.ReasoningEffort);
    }

    private async Task<AiProviderCompletion> CallOpenAiCompatibleAsync(
        string baseUrl, string apiKey, string? reasoningEffort, AiProviderRequest request, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("AiRegistryClient");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var model = request.Model;
        var maxTokens = request.MaxTokens ?? 4096;
        var effort = string.IsNullOrWhiteSpace(reasoningEffort) ? "high" : reasoningEffort!.ToLowerInvariant();
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
                reasoning_effort = effort,
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

/// <summary>
/// Cloudflare Workers AI native API adapter.
/// <para>
/// Calls <c>POST {BaseUrl}/run/{model}</c> where <c>BaseUrl</c> is stored as
/// <c>https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/ai</c>. The
/// account id is part of the BaseUrl rather than a separate column so the
/// existing <see cref="AiProvider"/> schema does not need to grow a new field.
/// </para>
/// <para>
/// Auth is a standard Bearer token (Cloudflare API token with Workers AI
/// permission). Models use the CF-prefixed naming convention
/// (e.g. <c>@cf/meta/llama-3.1-8b-instruct</c>).
/// </para>
/// </summary>
public sealed class CloudflareWorkersAiProvider(
    IHttpClientFactory httpClientFactory,
    IAiProviderRegistry registry) : IAiModelProvider
{
    public string Name => "cloudflare";

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var (baseUrl, apiKey) = await ResolveCredentialsAsync(request, ct);

        var model = request.Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Cloudflare Workers AI requires an explicit model (e.g. @cf/meta/llama-3.1-8b-instruct).");

        // CF model ids commonly start with "@cf/...". The path segment is
        // appended verbatim — the leading "@" is valid in URL paths and
        // CF's routing accepts it without escaping.
        var client = httpClientFactory.CreateClient("AiRegistryClient");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            messages = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt },
            },
            temperature = request.Temperature,
            max_tokens = request.MaxTokens ?? 1024,
            stream = false,
        };

        using var response = await client.PostAsync(
            $"run/{model}",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Cloudflare Workers AI call failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // CF response shape:
        // { "result": { "response": "...", "usage": { "prompt_tokens": N, "completion_tokens": N } },
        //   "success": true, "errors": [], "messages": [] }
        if (root.TryGetProperty("success", out var successEl) && !successEl.GetBoolean())
        {
            var errMsg = root.TryGetProperty("errors", out var errs) ? errs.GetRawText() : body;
            throw new InvalidOperationException($"Cloudflare Workers AI returned success=false: {errMsg}");
        }

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"Cloudflare Workers AI response missing 'result'. Body: {body}");

        // Most chat models return { response: "..." }; some streaming-style
        // models also return choices[]. Handle both shapes defensively.
        string text = string.Empty;
        if (result.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.String)
        {
            text = resp.GetString() ?? string.Empty;
        }
        else if (result.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var msg = choices[0].GetProperty("message");
            text = msg.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
        }

        AiUsage? usage = null;
        if (result.TryGetProperty("usage", out var usageEl))
        {
            usage = new AiUsage
            {
                PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ctEl) ? ctEl.GetInt32() : 0,
            };
        }

        return new AiProviderCompletion { Text = text, Usage = usage };
    }

    private async Task<(string baseUrl, string apiKey)> ResolveCredentialsAsync(AiProviderRequest request, CancellationToken ct)
    {
        var baseUrl = request.BaseUrlOverride;
        var apiKey = request.ApiKeyOverride;
        if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(apiKey))
            return (baseUrl, apiKey);

        // Pick the highest-priority active CF row. Filtering by Dialect keeps
        // this provider tightly bound to its registry rows and prevents an
        // OpenAI-compat row from being fed Cloudflare's request shape.
        var registered = (await registry.ListActiveAsync(ct))
            .FirstOrDefault(p => p.Dialect == AiProviderDialect.Cloudflare)
            ?? throw new InvalidOperationException("Cloudflare Workers AI provider is not registered. Add a row in /admin/ai-providers with Dialect=Cloudflare.");

        baseUrl ??= registered.BaseUrl;
        apiKey ??= await registry.GetPlatformKeyAsync(registered.Code, ct)
            ?? throw new InvalidOperationException("Platform API key missing for Cloudflare Workers AI.");
        return (baseUrl, apiKey);
    }
}
