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
    /// <summary>Phase 6b — list active rows for a single capability category
    /// (e.g. <see cref="AiProviderCategory.Tts"/>). Used by voice selectors
    /// and the admin UI to surface the per-category provider pool without
    /// loading every text-chat row.</summary>
    Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct);
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

    public async Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
        => await db.AiProviders.AsNoTracking()
            .Where(p => p.IsActive && p.Category == category)
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
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.AiProviderOptions> options,
    OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate? platformGate = null) : IAiModelProvider
{
    public string Name => "registry";

    private static readonly TimeSpan StandardProviderTimeout = TimeSpan.FromSeconds(100);

    private static readonly TimeSpan UbagFacadeTimeout = TimeSpan.FromSeconds(300);

    private static bool IsUbagFacadeRequest(string baseUrl, AiProviderRequest request)
        => string.Equals(request.ProviderCode, "ubag", StringComparison.OrdinalIgnoreCase)
            || (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                && string.Equals(uri.Host, "ubag-vps-gateway-1", StringComparison.OrdinalIgnoreCase));

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var (baseUrl, apiKey, reasoningEffort) = await ResolveCredentialsAsync(request, ct);
        Task<AiProviderCompletion> Invoke() => CallOpenAiCompatibleAsync(baseUrl, apiKey, reasoningEffort, request, ct);
        if (platformGate is null || !string.IsNullOrWhiteSpace(request.ApiKeyOverride))
            return await Invoke();
        return await platformGate.RunAsync(_ => Invoke(), ct);
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
        var explicitProviderCode = !string.IsNullOrWhiteSpace(request.ProviderCode);
        var first = explicitProviderCode
            ? providers.FirstOrDefault(p => string.Equals(p.Code, request.ProviderCode, StringComparison.OrdinalIgnoreCase))
            : providers.FirstOrDefault();
        if (explicitProviderCode && first is null)
        {
            throw new InvalidOperationException($"Requested OpenAI-compatible AI provider '{request.ProviderCode}' is not active or is not OpenAI-compatible.");
        }
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
        var unsafeBaseUrlReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeBaseUrlReason is not null)
            throw new InvalidOperationException(unsafeBaseUrlReason);

        // UBAG facade transcription divert: the facade has no Whisper weights —
        // it serves audio/transcriptions by attaching the clip to a native job
        // and letting the provider web UI listen. Route here (instead of the
        // Whisper-only OpenAiCompatibleProvider branch) when the resolved
        // provider is the ubag row, so every STT caller keeps working with
        // zero call-site changes.
        if (request.AudioAttachments is { Count: > 0 }
            && string.Equals(request.ProviderCode, "ubag", StringComparison.OrdinalIgnoreCase))
        {
            return await CallUbagTranscriptionAsync(baseUrl, apiKey, request, ct);
        }

        var client = httpClientFactory.CreateClient("AiRegistryClient");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.Timeout = IsUbagFacadeRequest(baseUrl, request) ? UbagFacadeTimeout : StandardProviderTimeout;

        var model = request.Model;
        var maxTokens = request.MaxTokens ?? 4096;
        var effort = string.IsNullOrWhiteSpace(reasoningEffort) ? "high" : reasoningEffort!.ToLowerInvariant();
        var sendReasoning = IsReasoningCapable(model);

        var ubagFacade = IsUbagFacadeRequest(baseUrl, request);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = AiProviderPayloadBuilder.BuildOpenAiMessages(request),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = false,
        };
        if (ubagFacade)
        {
            payload["ubag_nonce"] = Guid.NewGuid().ToString("N");
        }
        var responseFormat = AiProviderPayloadBuilder.BuildOpenAiResponseFormat(request.ResponseFormatJson);
        if (responseFormat is not null)
        {
            payload["response_format"] = responseFormat;
        }
        if (sendReasoning)
        {
            payload["reasoning_effort"] = effort;
        }
        var tools = AiProviderPayloadBuilder.BuildOpenAiTools(request.Tools);
        if (ubagFacade && tools.Count > 0)
        {
            throw new InvalidOperationException(
                "UBAG provider call failed: the UBAG browser facade does not support native function calling (tools/tool_choice). " +
                "Route tool-using features to an Anthropic/OpenAI row, or call a UBAG text-only feature without tools.");
        }
        if (tools.Count > 0)
        {
            payload["tools"] = tools;
            if (!string.IsNullOrWhiteSpace(request.ToolChoice)) payload["tool_choice"] = request.ToolChoice;
        }

        using var response = await client.PostAsync(
            "chat/completions",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AiProviderErrorMessages.HttpFailure(
                IsUbagFacadeRequest(baseUrl, request) ? "UBAG provider" : "AI provider",
                (int)response.StatusCode,
                response.ReasonPhrase,
                ExtractUbagErrorDetail(body)));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        AiProviderPayloadBuilder.ReadOpenAiChoiceMessage(root, "UBAG provider", out var choice, out var message);
        var text = AiProviderPayloadBuilder.ReadOpenAiMessageContent(message);
        if (string.IsNullOrWhiteSpace(text))
        {
            var finish = choice.TryGetProperty("finish_reason", out var finishEl) ? finishEl.GetString() : null;
            throw new InvalidOperationException(
                $"UBAG provider call failed: the browser job finished but returned no text (finish_reason={finish ?? "stop"}). Retry the request.");
        }
        var servedModel = root.TryGetProperty("model", out var servedEl) && servedEl.ValueKind == JsonValueKind.String
            ? servedEl.GetString()
            : null;
        var toolCalls = AiProviderPayloadBuilder.ReadOpenAiToolCalls(message);
        if (toolCalls is null)
        {
            toolCalls = AiProviderPayloadBuilder.CoerceToolCallsFromJsonText(
                text, request.Tools, request.ToolChoice);
        }
        var usage = root.TryGetProperty("usage", out var usageEl)
            ? new AiUsage
            {
                PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ctEl) ? ctEl.GetInt32() : 0,
            }
            : null;

        var finishReason = choice.TryGetProperty("finish_reason", out var finish) ? finish.GetString() : null;
        return new AiProviderCompletion { Text = text, Usage = usage, ToolCalls = toolCalls, FinishReason = finishReason, ServedModel = servedModel };
    }

    /// <summary>Pulls the facade's machine-readable error detail out of a
    /// non-2xx OpenAI-facade body (<c>error.message</c>, else <c>error.code</c>,
    /// else the raw body truncated). Lets learners/admins see WHY the browser
    /// job failed (login drift, wait timeout, cancelled) instead of a bare
    /// "HTTP 503" with the reason phrase.</summary>
    public static string? ExtractUbagErrorDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(message.GetString()))
                {
                    var text = message.GetString()!.Trim();
                    return text.Length <= 512 ? text : text[..512];
                }
                if (error.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(code.GetString()))
                {
                    return $"UBAG error code: {code.GetString()!.Trim()}";
                }
            }
        }
        catch (JsonException)
        {
        }
        var trimmed = body.Trim();
        return trimmed.Length <= 512 ? trimmed : trimmed[..512];
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

    /// <summary>POSTs one audio attachment to the UBAG facade's
    /// <c>audio/transcriptions</c> endpoint (multipart <c>file</c> + model +
    /// language) and returns the transcript as the completion text. The
    /// facade answers <c>{text, ubag_job_id}</c>; verbose_json segment detail
    /// is unavailable by design, so downstream confidence falls back to the
    /// callers' existing defaults.</summary>
    private async Task<AiProviderCompletion> CallUbagTranscriptionAsync(
        string baseUrl, string apiKey, AiProviderRequest request, CancellationToken ct)
    {
        var audio = request.AudioAttachments!
            .FirstOrDefault(attachment => attachment.Data is { Length: > 0 })
            ?? throw new InvalidOperationException("UBAG transcription requires a non-empty audio attachment.");

        var client = httpClientFactory.CreateClient("AiRegistryClient");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio.Data);
        audioContent.Headers.ContentType =
            new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(audio.MimeType) ? "audio/webm" : audio.MimeType);
        form.Add(audioContent, "file", FileNameForAudioMimeType(audio.MimeType));
        form.Add(new StringContent(string.IsNullOrWhiteSpace(request.Model) ? "whisper-1" : request.Model), "model");
        if (!string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            form.Add(new StringContent(request.UserPrompt), "prompt");
        }

        using var response = await client.PostAsync("audio/transcriptions", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AiProviderErrorMessages.HttpFailure("UBAG transcription", (int)response.StatusCode, response.ReasonPhrase));

        var text = body.Trim();
        if (text.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("text", out var textElement))
            {
                text = textElement.GetString()?.Trim() ?? string.Empty;
            }
        }

        return new AiProviderCompletion { Text = text };
    }

    private static string FileNameForAudioMimeType(string? mimeType)
        => mimeType?.ToLowerInvariant() switch
        {
            "audio/mpeg" => "recording.mp3",
            "audio/mp4" => "recording.m4a",
            "video/mp4" => "recording.mp4",
            "audio/ogg" => "recording.ogg",
            "audio/wav" or "audio/x-wav" => "recording.wav",
            "audio/webm" => "recording.webm",
            _ => "recording.bin",
        };
}

/// <summary>
/// Anthropic Messages API adapter. Separate from the OpenAI-compatible
/// path because Anthropic uses <c>x-api-key</c> header, a different
/// request body shape, and a different response structure.
/// </summary>
public sealed class AnthropicProvider(
    IHttpClientFactory httpClientFactory,
    IAiProviderRegistry registry,
    OetLearner.Api.Services.Ai.AiPlatformConcurrencyGate? platformGate = null) : IAiModelProvider
{
    public string Name => "anthropic";

    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const string ApiKeyHeaderName = "x-api-key";
    public const string VersionHeaderName = "anthropic-version";
    public const string ApiVersion = "2023-06-01";

    /// <summary>
    /// Normalizes an Anthropic base URL so callers can register it either as
    /// the bare host (<c>https://api.anthropic.com</c>) or with the version
    /// segment (<c>https://api.anthropic.com/v1</c>). Strips a trailing
    /// <c>/v1</c> and any trailing slash; the caller then appends
    /// <c>v1/messages</c>. Shared with the connection tester so the probe
    /// predicts runtime behavior exactly.
    /// </summary>
    public static string NormalizeBaseUrl(string? baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].TrimEnd('/');
        return trimmed;
    }

    /// <summary>
    /// Claude 5-family models (and Opus 4.8+) reject the `temperature`
    /// parameter with HTTP 400 "`temperature` is deprecated for this model."
    /// Known-affected ids are skipped up front; unknown future models are
    /// covered by the strip-and-retry in <see cref="CompleteAsync"/>.
    /// </summary>
    internal static bool ModelRejectsTemperature(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var m = model.Trim().ToLowerInvariant();
        return m.StartsWith("claude-sonnet-5", StringComparison.Ordinal)
            || m.StartsWith("claude-fable-5", StringComparison.Ordinal)
            || m.StartsWith("claude-haiku-5", StringComparison.Ordinal)
            || m.StartsWith("claude-opus-4-8", StringComparison.Ordinal);
    }

    /// <summary>
    /// Maps <see cref="AiProviderRequest.ToolChoice"/> onto Anthropic's
    /// <c>tool_choice</c> object. Empty/<c>auto</c> stays auto; <c>none</c>
    /// and <c>any</c> are honoured; any other value is a forced named tool.
    /// </summary>
    public static Dictionary<string, object?> ResolveAnthropicToolChoice(string? toolChoice)
    {
        var choice = (toolChoice ?? string.Empty).Trim();
        if (choice.Length == 0 || string.Equals(choice, "auto", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?> { ["type"] = "auto" };
        if (string.Equals(choice, "none", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?> { ["type"] = "none" };
        if (string.Equals(choice, "any", StringComparison.OrdinalIgnoreCase))
            return new Dictionary<string, object?> { ["type"] = "any" };
        return new Dictionary<string, object?> { ["type"] = "tool", ["name"] = choice };
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return wait;
        }
        return null;
    }

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var held = false;
        if (platformGate is not null && string.IsNullOrWhiteSpace(request.ApiKeyOverride))
        {
            await platformGate.WaitAsync(ct);
            held = true;
        }

        try
        {
        var baseUrl = request.BaseUrlOverride;
        var apiKey = request.ApiKeyOverride;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            var providerCode = string.IsNullOrWhiteSpace(request.ProviderCode) ? "anthropic" : request.ProviderCode.Trim().ToLowerInvariant();
            var registered = await registry.FindByCodeAsync(providerCode, ct);
            if (registered is null)
                throw new InvalidOperationException($"Anthropic provider {providerCode} is not registered.");
            baseUrl ??= registered.BaseUrl;
            apiKey ??= await registry.GetPlatformKeyAsync(providerCode, ct)
                ?? throw new InvalidOperationException($"Platform API key missing for Anthropic provider {providerCode}.");
        }

        var unsafeBaseUrlReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeBaseUrlReason is not null)
            throw new InvalidOperationException(unsafeBaseUrlReason);

        var client = httpClientFactory.CreateClient("AiRegistryClient");
        // Normalize so a bare-host BaseUrl (no /v1) still resolves correctly;
        // we always POST the version-qualified "v1/messages" path below.
        client.BaseAddress = new Uri(NormalizeBaseUrl(baseUrl) + "/");
        client.DefaultRequestHeaders.Remove(ApiKeyHeaderName);
        client.DefaultRequestHeaders.Add(ApiKeyHeaderName, apiKey);
        client.DefaultRequestHeaders.Remove(VersionHeaderName);
        client.DefaultRequestHeaders.Add(VersionHeaderName, ApiVersion);
        client.DefaultRequestHeaders.Remove("anthropic-beta");
        client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31");

        // Claude 5-family models reject `temperature` with an HTTP 400
        // ("`temperature` is deprecated for this model."). Omit it for them and
        // keep sending it to older models that still honour it. Extended
        // thinking additionally requires temperature to stay at the API
        // default (1) for every model, so omit it whenever thinking is on.
        var supportsTemperature = !ModelRejectsTemperature(request.Model) && !request.EnableExtendedThinking;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = AiProviderPayloadBuilder.BuildAnthropicMessages(request),
            ["max_tokens"] = request.MaxTokens ?? 1024,
        };
        if (supportsTemperature)
        {
            payload["temperature"] = request.Temperature;
        }
        if (request.EnableExtendedThinking)
        {
            // Claude 5-family models use adaptive thinking, not the older
            // manual token-budget scheme (confirmed live against the
            // Anthropic API: `{"type":"enabled","budget_tokens":N}` is
            // rejected with "not supported for this model" on
            // claude-sonnet-5 — it wants `{"type":"adaptive"}` plus a
            // top-level `output_config.effort`). Extended thinking also
            // requires temperature left at the API default (1) — already
            // satisfied above since supportsTemperature is false whenever
            // EnableExtendedThinking is set.
            payload["thinking"] = new Dictionary<string, object?> { ["type"] = "adaptive" };
            var effort = string.IsNullOrWhiteSpace(request.ThinkingEffort)
                ? "high"
                : request.ThinkingEffort!.Trim().ToLowerInvariant();
            payload["output_config"] = new Dictionary<string, object?> { ["effort"] = effort };
        }
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            payload["system"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = request.SystemPrompt,
                    ["cache_control"] = new Dictionary<string, object?> { ["type"] = "ephemeral" },
                },
            };
        }
        var anthropicTools = AiProviderPayloadBuilder.BuildAnthropicTools(request.Tools);
        if (anthropicTools.Count > 0)
        {
            payload["tools"] = anthropicTools;
            payload["tool_choice"] = ResolveAnthropicToolChoice(request.ToolChoice);
        }

        async Task<(HttpResponseMessage Response, string Body)> SendAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            // ResponseHeadersRead keeps a 2xx-with-unreadable-body separable from
            // a send failure: HttpClient.PostAsync buffers the body first.
            var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            string resBody;
            try
            {
                resBody = await res.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                var status = (int)res.StatusCode;
                res.Dispose();
                if (status is >= 200 and < 300)
                    throw new AiProviderBodyReadException("Anthropic", ex);
                throw;
            }

            return (res, resBody);
        }

        var (response, body) = await SendAsync();

        // Self-healing for future models: if Anthropic rejects `temperature` as
        // deprecated, strip it and retry once instead of failing the learner turn.
        if (!response.IsSuccessStatusCode
            && payload.ContainsKey("temperature")
            && body.Contains("temperature", StringComparison.OrdinalIgnoreCase)
            && body.Contains("deprecated", StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            payload.Remove("temperature");
            (response, body) = await SendAsync();
        }

        using var _response = response;
        if (!response.IsSuccessStatusCode)
        {
            throw new AiProviderHttpException(
                "Anthropic",
                (int)response.StatusCode,
                response.ReasonPhrase,
                ReadRetryAfter(response));
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var contentParts = root.GetProperty("content");
        var sb = new StringBuilder();
        foreach (var part in contentParts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }
        var toolCalls = AiProviderPayloadBuilder.ReadAnthropicToolCalls(contentParts);

        AiUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageEl))
        {
            usage = new AiUsage
            {
                PromptTokens = usageEl.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
                CacheWriteTokens = usageEl.TryGetProperty("cache_creation_input_tokens", out var cwt) ? cwt.GetInt32() : 0,
                CacheReadTokens = usageEl.TryGetProperty("cache_read_input_tokens", out var crt) ? crt.GetInt32() : 0,
            };
        }

        var finishReason = root.TryGetProperty("stop_reason", out var stopReason) ? stopReason.GetString() : null;
        return new AiProviderCompletion { Text = sb.ToString(), Usage = usage, ToolCalls = toolCalls, FinishReason = finishReason };
        }
        finally
        {
            if (held)
                platformGate!.Release();
        }
    }
}

/// <summary>
/// Google Gemini native GenerateContent adapter used for pronunciation
/// linguistic scoring where the model must inspect the raw learner audio.
/// Text-only providers stay on the existing chat-completions path; this
/// adapter consumes <see cref="AiProviderRequest.AudioAttachments"/>.
/// </summary>
public sealed class GeminiNativeProvider(
    IHttpClientFactory httpClientFactory,
    IAiProviderRegistry registry,
    Microsoft.Extensions.Options.IOptions<OetLearner.Api.Configuration.PronunciationOptions> options) : IAiModelProvider
{
    private const string DefaultProviderCode = "gemini-pronunciation-audio";
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public string Name => DefaultProviderCode;

    public async Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
    {
        var (baseUrl, apiKey, model) = await ResolveCredentialsAsync(request, ct);

        if (request.AudioAttachments is not { Count: > 0 })
            throw new InvalidOperationException("Gemini native-audio provider requires at least one audio attachment.");

        var unsafeBaseUrlReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeBaseUrlReason is not null)
            throw new InvalidOperationException(unsafeBaseUrlReason);

        var client = httpClientFactory.CreateClient("GeminiNativeClient");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

        var textPrompt = string.Join("\n\n", new[] { request.SystemPrompt, request.UserPrompt }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new List<Dictionary<string, object?>>
        {
            new() { ["text"] = textPrompt },
        };
        var attachedAudioParts = 0;
        foreach (var audio in request.AudioAttachments)
        {
            if (audio.Data.Length == 0) continue;
            parts.Add(new Dictionary<string, object?>
            {
                ["inline_data"] = new Dictionary<string, object?>
                {
                    ["mime_type"] = string.IsNullOrWhiteSpace(audio.MimeType) ? "audio/webm" : audio.MimeType,
                    ["data"] = Convert.ToBase64String(audio.Data),
                },
            });
            attachedAudioParts++;
        }
        if (attachedAudioParts == 0)
            throw new InvalidOperationException("Gemini native-audio provider requires at least one non-empty audio attachment.");

        var payload = new Dictionary<string, object?>
        {
            ["contents"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["parts"] = parts,
                },
            },
            ["generationConfig"] = new Dictionary<string, object?>
            {
                ["temperature"] = request.Temperature,
                ["maxOutputTokens"] = request.MaxTokens ?? 1024,
            },
        };

        var endpoint = $"{ToGeminiModelPath(model)}:generateContent";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        requestMessage.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        using var response = await client.SendAsync(requestMessage, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(AiProviderErrorMessages.HttpFailure("Gemini", (int)response.StatusCode, response.ReasonPhrase));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var text = ReadGeminiText(root);
        var usage = ReadGeminiUsage(root);
        var finishReason = root.TryGetProperty("candidates", out var candidates)
                           && candidates.ValueKind == JsonValueKind.Array
                           && candidates.GetArrayLength() > 0
                           && candidates[0].TryGetProperty("finishReason", out var finish)
            ? finish.GetString()
            : null;
        return new AiProviderCompletion { Text = text, Usage = usage, FinishReason = finishReason };
    }

    private async Task<(string baseUrl, string apiKey, string model)> ResolveCredentialsAsync(AiProviderRequest request, CancellationToken ct)
    {
        var configured = options.Value;
        var providerCode = string.IsNullOrWhiteSpace(request.ProviderCode)
            ? DefaultProviderCode
            : request.ProviderCode.Trim().ToLowerInvariant();
        var row = await registry.FindByCodeAsync(providerCode, ct);
        if (row is not null && row.Dialect != AiProviderDialect.GeminiNative)
            throw new InvalidOperationException($"Provider {providerCode} is not a Gemini native provider.");

        var baseUrl = request.BaseUrlOverride ?? row?.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = configured.GeminiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = DefaultBaseUrl;

        var apiKey = request.ApiKeyOverride;
        if (string.IsNullOrWhiteSpace(apiKey) && row is not null)
        {
            apiKey = await registry.GetPlatformKeyAsync(row.Code, ct);
        }
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = configured.GeminiApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Platform API key missing for Gemini provider {providerCode}.");

        var model = request.Model;
        if (string.IsNullOrWhiteSpace(model)) model = row?.DefaultModel;
        if (string.IsNullOrWhiteSpace(model)) model = configured.GeminiModel;
        if (string.IsNullOrWhiteSpace(model)) model = "gemini-3.5-flash";

        return (baseUrl!, apiKey!, model!);
    }

    private static string ToGeminiModelPath(string model)
    {
        var trimmed = model.Trim().Trim('/');
        return trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"models/{trimmed}";
    }

    private static string ReadGeminiText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(AiProviderErrorMessages.InvalidResponse("Gemini", "missing candidates[0]"));
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(AiProviderErrorMessages.InvalidResponse("Gemini", "missing candidates[0].content.parts"));
        }

        var output = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                output.Append(text.GetString());
            }
        }
        return output.ToString();
    }

    private static AiUsage? ReadGeminiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage)) return null;
        return new AiUsage
        {
            PromptTokens = usage.TryGetProperty("promptTokenCount", out var prompt) ? prompt.GetInt32() : 0,
            CompletionTokens = usage.TryGetProperty("candidatesTokenCount", out var completion) ? completion.GetInt32() : 0,
        };
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
        var unsafeBaseUrlReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeBaseUrlReason is not null)
            throw new InvalidOperationException(unsafeBaseUrlReason);

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
            throw new InvalidOperationException(AiProviderErrorMessages.HttpFailure("Cloudflare Workers AI", (int)response.StatusCode, response.ReasonPhrase));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // CF response shape:
        // { "result": { "response": "...", "usage": { "prompt_tokens": N, "completion_tokens": N } },
        //   "success": true, "errors": [], "messages": [] }
        if (root.TryGetProperty("success", out var successEl) && !successEl.GetBoolean())
        {
            throw new InvalidOperationException(AiProviderErrorMessages.InvalidResponse("Cloudflare Workers AI", "success=false"));
        }

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException(AiProviderErrorMessages.InvalidResponse("Cloudflare Workers AI", "missing result"));

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
