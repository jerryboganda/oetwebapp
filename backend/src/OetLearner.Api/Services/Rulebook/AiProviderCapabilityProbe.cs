using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Seeding;

namespace OetLearner.Api.Services.Rulebook;

/// <summary>One model's observed abilities.</summary>
public sealed record ModelCapabilityResult(
    string Model,
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsDocuments,
    bool SupportsJsonMode,
    bool SupportsEmbeddings,
    bool SupportsStreaming,
    bool SupportsThinking,
    bool ThinkingCanBeDisabled,
    IReadOnlyList<string> AllowedReasoningEfforts,
    int MaxTokensCeiling,
    int ContextTokens,
    string Status,
    string? Detail);

/// <summary>
/// Probes a provider's REAL capabilities against the LIVE endpoint with the REAL key.
///
/// <para>
/// <b>Why this exists.</b> A routing decision needs to know what a model can do, and for Z.AI the
/// published sources disagree with each other: the OpenAPI spec binds <c>glm-5.3-flash</c> to a
/// vision request schema whose complete key list contains no <c>response_format</c> at all, while
/// the model page advertises Structured Output and recommends <c>tool_stream</c>. Both cannot be
/// true. Documentation cannot settle it; one observed call can. Everything the probe reports is
/// therefore what the endpoint did, not what a page claims.
/// </para>
///
/// <para>
/// <b>Fail closed.</b> A sub-probe that errors records <see langword="false"/> for that ability, and
/// a model whose probe could not reach the endpoint records status <c>network</c>/<c>auth</c> with
/// every flag false. Because unknown and unsupported are treated identically downstream
/// (<see cref="AiFeatureCapabilityRequirements"/>), the worst outcome of a bad probe is "this model
/// is not offered", never "this model is offered and breaks a feature at call time".
/// </para>
///
/// <para>
/// <b>Cost.</b> Each sub-probe is a tiny request. This is an admin action on demand, never a
/// background job and never a CI step — the same owner-approved in-app self-check pattern as the
/// Pipeline self-check and the Writing validator self-check.
/// </para>
/// </summary>
public interface IAiProviderCapabilityProbe
{
    /// <summary>Probe every model requested (or, when null, the row's default model plus its
    /// allow-listed models) and persist the results.</summary>
    Task<IReadOnlyList<ModelCapabilityResult>> ProbeAsync(string providerCode, IReadOnlyList<string>? models, CancellationToken ct);
}

public sealed class AiProviderCapabilityProbe(
    LearnerDbContext db,
    IAiProviderRegistry registry,
    IHttpClientFactory httpClientFactory,
    ILogger<AiProviderCapabilityProbe> logger) : IAiProviderCapabilityProbe
{
    /// <summary>Named client so a probe cannot exhaust or inherit the production call budget.</summary>
    public const string HttpClientName = "AiCapabilityProbe";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Enough for a short tool-call round trip, small enough that a rejected
    /// <c>max_tokens</c> is detected as a 400 rather than by timeout.</summary>
    private const int ProbeMaxTokens = 64;

    public async Task<IReadOnlyList<ModelCapabilityResult>> ProbeAsync(
        string providerCode, IReadOnlyList<string>? models, CancellationToken ct)
    {
        var row = await db.AiProviders.FirstOrDefaultAsync(p => p.Code == providerCode, ct)
                  ?? throw new InvalidOperationException($"Provider '{providerCode}' does not exist.");

        if (string.IsNullOrWhiteSpace(row.EncryptedApiKey))
            throw new InvalidOperationException($"Provider '{providerCode}' has no API key. Paste one on /admin/ai-providers first.");

        var apiKey = await registry.GetPlatformKeyAsync(providerCode, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"The stored key for '{providerCode}' could not be read.");

        var targets = ResolveTargets(row, models);
        if (targets.Count == 0)
            throw new InvalidOperationException("No models to probe: pass at least one model, or set a default model and an allow-list on the row.");

        var results = new List<ModelCapabilityResult>(targets.Count);
        foreach (var model in targets)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ProbeModelAsync(row, apiKey!, model, ct));
        }

        await PersistAsync(row.Code, results, ct);
        return results;
    }

    private static List<string> ResolveTargets(AiProvider row, IReadOnlyList<string>? models)
    {
        if (models is { Count: > 0 })
            return models.Select(m => m.Trim()).Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var fromCsv = (row.AllowedModelsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var targets = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.DefaultModel)) targets.Add(row.DefaultModel.Trim());
        targets.AddRange(fromCsv);
        return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<ModelCapabilityResult> ProbeModelAsync(AiProvider row, string apiKey, string model, CancellationToken ct)
    {
        var notes = new List<string>();
        var status = "ok";

        var chat = await SendAsync(row, apiKey, new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "user", content = "Reply with the single word: ready" } },
            ["max_tokens"] = ProbeMaxTokens,
            ["temperature"] = 1,
        }, ct, acceptSse: false);

        if (chat.Failure is not null)
        {
            return Failed(model, chat.Failure, notes);
        }

        // ── Streaming ─────────────────────────────────────────────────────────
        // Asked for explicitly rather than inferred: whether a vendor accepts stream=true and
        // whether it emits tool-call deltas is exactly the kind of thing docs get wrong.
        var stream = await SendAsync(row, apiKey, new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "user", content = "Reply with the single word: ready" } },
            ["max_tokens"] = ProbeMaxTokens,
            ["temperature"] = 1,
            ["stream"] = true,
        }, ct, acceptSse: true);
        var supportsStreaming = stream.Failure is null && stream.SawEventStream;
        if (!supportsStreaming) notes.Add($"streaming: {stream.Failure ?? "no text/event-stream response"}");

        // ── Tool calling ──────────────────────────────────────────────────────
        var tools = await SendAsync(row, apiKey, new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "user", content = "What is the weather in Paris? Use the tool." } },
            ["max_tokens"] = ProbeMaxTokens,
            ["temperature"] = 1,
            // "auto" is the only value Z.AI accepts; sending anything else is a whole-request 400,
            // so the probe must not be the thing that proves that.
            ["tool_choice"] = ZaiProviderDefaults.RequestLimits.OnlyToolChoice,
            ["tools"] = new[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "get_weather",
                        description = "Get the weather for a city.",
                        parameters = new
                        {
                            type = "object",
                            properties = new { city = new { type = "string" } },
                            required = new[] { "city" },
                        },
                    },
                },
            },
        }, ct, acceptSse: false);
        var supportsTools = tools.Failure is null && tools.SawToolCalls;
        if (!supportsTools) notes.Add($"tools: {tools.Failure ?? "accepted the request but returned no tool call"}");

        // ── JSON mode ─────────────────────────────────────────────────────────
        // The single most load-bearing flag: many strict-JSON call sites depend on it, and Z.AI's
        // published schema for the multimodal GLM models omits response_format entirely.
        var json = await SendAsync(row, apiKey, new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "user", content = "Return a JSON object with the key ok set to true." } },
            ["max_tokens"] = ProbeMaxTokens,
            ["temperature"] = 1,
            ["response_format"] = new { type = "json_object" },
        }, ct, acceptSse: false);
        var supportsJsonMode = json.Failure is null && json.SawJson;
        if (!supportsJsonMode) notes.Add($"json mode: {json.Failure ?? "accepted the request but returned no parseable JSON"}");

        // ── Vision ────────────────────────────────────────────────────────────
        // A 1x1 transparent PNG. A model that cannot take images rejects the content part outright,
        // which is a clean signal.
        const string OnePixelPng =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var vision = await SendAsync(row, apiKey, new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Is this image blank? One word." },
                        new { type = "image_url", image_url = new { url = "data:image/png;base64," + OnePixelPng } },
                    },
                },
            },
            ["max_tokens"] = ProbeMaxTokens,
            ["temperature"] = 1,
        }, ct, acceptSse: false);
        var supportsVision = vision.Failure is null && !string.IsNullOrWhiteSpace(vision.Content);
        if (!supportsVision) notes.Add($"vision: {vision.Failure ?? "accepted the image part but returned no content"}");

        // ── Thinking ──────────────────────────────────────────────────────────
        // Asked twice: once to switch thinking OFF. A model that accepts enabled-only tells us the
        // control must be rendered locked, which is the whole point of not faking the dropdown.
        var thinkingOff = await SendAsync(row, apiKey, new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "user", content = "Reply with the single word: ready" } },
            ["max_tokens"] = ProbeMaxTokens,
            ["temperature"] = 1,
            ["thinking"] = new { type = "disabled" },
        }, ct, acceptSse: false);
        var thinkingAccepted = thinkingOff.Failure is null;
        var supportsThinking = thinkingAccepted || (chat.SawReasoningContent && !thinkingAccepted);
        var thinkingCanBeDisabled = thinkingAccepted;
        if (!thinkingAccepted)
            notes.Add($"thinking: cannot be disabled ({Shorten(thinkingOff.Failure ?? "rejected")})");

        // ── Embeddings ────────────────────────────────────────────────────────
        // Separate endpoint entirely; many chat vendors have none.
        var embeddings = await ProbeEmbeddingsAsync(row, apiKey, model, ct);
        if (!embeddings) notes.Add("embeddings: no vector-embedding endpoint");

        if (notes.Count > 0 && supportsStreaming && supportsTools && supportsJsonMode && supportsVision)
        {
            // Nothing essential failed; per-capability notes remain informational.
            status = "ok";
        }
        else if (supportsStreaming || supportsTools || supportsJsonMode || supportsVision)
        {
            status = "partial";
        }
        else
        {
            status = "partial";
            notes.Add("model did not demonstrate any usable capability");
        }

        var efforts = ResolveReasoningEfforts(supportsThinking, thinkingCanBeDisabled);

        return new ModelCapabilityResult(
            model, supportsTools, supportsVision, false, supportsJsonMode, embeddings,
            supportsStreaming, supportsThinking, thinkingCanBeDisabled,
            efforts, ZaiProviderDefaults.RequestLimits.MaxTokensCeiling, 0,
            status, notes.Count == 0 ? null : Truncate(string.Join("; ", notes)));
    }

    /// <summary>
    /// The reasoning values the UI may offer for this model. Derived from the vendor's documented
    /// ladder for the models we ship, and always filtered to what this model can actually do — a
    /// forced-thinking model gets the ladder minus "off", because offering "off" would be a lie.
    /// </summary>
    private static IReadOnlyList<string> ResolveReasoningEfforts(bool supportsThinking, bool canDisable)
    {
        if (!supportsThinking) return Array.Empty<string>();

        // Z.AI's ladder. Documented on the reasoning_effort parameter; "none" is the closest thing
        // to off and is only honest to offer when the model accepted thinking:disabled.
        var ladder = new List<string> { "low", "medium", "high", "xhigh", "max" };
        if (canDisable) ladder.Add("none");
        return ladder;
    }

    private async Task<bool> ProbeEmbeddingsAsync(AiProvider row, string apiKey, string model, CancellationToken ct)
    {
        if (!Uri.TryCreate(row.BaseUrl, UriKind.Absolute, out var baseUri))
            return false;

        // Embedding models are a different namespace from chat models, so ask for the generic
        // endpoint and accept either an array or an error: the question is only "does the route
        // exist", not "is this chat model an embedding model".
        using var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/api/paas/v4/");
        client.Timeout = ProbeTimeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { input = "probe", model }),
                Encoding.UTF8, "application/json"),
        };

        try
        {
            using var response = await client.SendAsync(request, ct);
            // 404/405 = no such route (the common "vendor has no embeddings" answer). 400 = the route
            // exists but the model was wrong for it, which also proves the route is there.
            return response.StatusCode != HttpStatusCode.NotFound
                   && response.StatusCode != HttpStatusCode.MethodNotAllowed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    private sealed record ProbeResponse(string? Failure, string? Content, bool SawToolCalls, bool SawJson, bool SawReasoningContent, bool SawEventStream);

    private async Task<ProbeResponse> SendAsync(
        AiProvider row, string apiKey, Dictionary<string, object?> payload,
        CancellationToken ct, bool acceptSse)
    {
        ct.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(row.BaseUrl, UriKind.Absolute, out var baseUri))
            return new ProbeResponse($"base URL '{row.BaseUrl}' is not an absolute URL", null, false, false, false, false);

        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(row.BaseUrl);
        if (unsafeReason is not null)
            return new ProbeResponse(unsafeReason, null, false, false, false, false);

        using var client = httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = ProbeTimeout;
        // A provider base URL already carries its version prefix (e.g. .../api/paas/v4), so append
        // the operation rather than treating it as an authority.
        client.BaseAddress = new Uri(baseUri.ToString().TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        if (acceptSse) client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadAsync(response, ct);
                return new ProbeResponse(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Shorten(body)}",
                    null, false, false, false, false);
            }

            if (!acceptSse)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return Interpret(body);
            }

            var isSse = string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
            if (!isSse)
            {
                var body = await SafeReadAsync(response, ct);
                return Interpret(body) with { SawEventStream = false };
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            return InterpretSse(text);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new ProbeResponse($"timed out after {ProbeTimeout.TotalSeconds:0}s", null, false, false, false, false);
        }
        catch (HttpRequestException ex)
        {
            return new ProbeResponse($"network error: {Shorten(ex.Message)}", null, false, false, false, false);
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private static ProbeResponse Interpret(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return new ProbeResponse("empty response body", null, false, false, false, false);

        string? content = null;
        var sawToolCalls = false;
        var sawJson = false;
        var sawReasoning = false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return new ProbeResponse("response had no choices", null, false, false, false, false);

            var message = choices[0].TryGetProperty("message", out var m) ? m : default;
            if (message.ValueKind != JsonValueKind.Object)
                return new ProbeResponse("choice had no message object", null, false, false, false, false);

            if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array && toolCalls.GetArrayLength() > 0)
                sawToolCalls = true;

            if (message.TryGetProperty("reasoning_content", out var reasoning)
                && reasoning.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(reasoning.GetString()))
                sawReasoning = true;

            if (message.TryGetProperty("content", out var contentProp))
            {
                content = contentProp.ValueKind switch
                {
                    JsonValueKind.String => contentProp.GetString(),
                    // Multimodal responses may return content parts rather than a bare string.
                    JsonValueKind.Array => string.Concat(contentProp.EnumerateArray()
                        .Where(p => p.TryGetProperty("text", out _))
                        .Select(p => p.GetProperty("text").GetString() ?? string.Empty)),
                    _ => null,
                };

                var trimmed = content?.TrimStart();
                if (!string.IsNullOrEmpty(trimmed)
                    && (trimmed!.StartsWith('{') || trimmed.StartsWith('[')))
                {
                    try { JsonDocument.Parse(content!); sawJson = true; } catch { sawJson = false; }
                }
            }
        }
        catch (JsonException ex)
        {
            return new ProbeResponse($"unparseable response: {Shorten(ex.Message)}", null, false, false, false, false);
        }

        return new ProbeResponse(null, content, sawToolCalls, sawJson, sawReasoning, false);
    }

    /// <summary>Collapse an SSE body and report what appeared. Tool-call deltas and the usage
    /// chunk are merged by simple substring/text accumulation rather than a full parser: this only
    /// needs to know whether the stream carried text and whether it declared itself as SSE.</summary>
    private static ProbeResponse InterpretSse(string body)
    {
        var content = new StringBuilder();
        var sawTool = body.Contains("\"tool_calls\"", StringComparison.Ordinal);
        var sawReasoning = body.Contains("\"reasoning_content\"", StringComparison.Ordinal);
        var sawJson = false;
        var sawData = false;

        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line["data:".Length..].Trim();
            if (payload.Length == 0 || payload == "[DONE]") continue;
            sawData = true;

            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;
                if (!choices[0].TryGetProperty("delta", out var delta)) continue;
                if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    content.Append(c.GetString());
            }
            catch (JsonException) { /* a partial delta is not a failure */ }
        }

        var text = content.ToString().TrimStart();
        if (text.Length > 0 && (text.StartsWith('{') || text.StartsWith('[')))
        {
            try { JsonDocument.Parse(text); sawJson = true; } catch { sawJson = false; }
        }

        // Declared SSE but carried no data event at all is not a usable stream.
        var usable = sawData && !string.IsNullOrWhiteSpace(content.ToString());
        return new ProbeResponse(null, content.ToString(), sawTool, sawJson, sawReasoning, usable);
    }

    private static ModelCapabilityResult Failed(string model, string failure, List<string> notes)
    {
        notes.Add(failure);
        return new ModelCapabilityResult(
            model, false, false, false, false, false, false, false, false,
            Array.Empty<string>(), 0, 0, Classify(failure), Truncate(string.Join("; ", notes)));
    }

    private static string Classify(string failure)
    {
        if (failure.Contains("401", StringComparison.Ordinal) || failure.Contains("403", StringComparison.Ordinal)
            || failure.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)) return "auth";
        if (failure.Contains("429", StringComparison.Ordinal)) return "rate_limited";
        if (failure.Contains("network error", StringComparison.Ordinal) || failure.Contains("timed out", StringComparison.Ordinal)) return "network";
        if (failure.Contains("HTTP 4", StringComparison.Ordinal)) return "rejected";
        return "unknown";
    }

    private async Task PersistAsync(string providerCode, IReadOnlyList<ModelCapabilityResult> results, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await db.AiProviderModelCapabilities
            .Where(c => c.ProviderCode == providerCode)
            .ToListAsync(ct);

        foreach (var r in results)
        {
            var row = existing.FirstOrDefault(c => string.Equals(c.Model, r.Model, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new AiProviderModelCapability
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ProviderCode = providerCode,
                    Model = r.Model,
                    CreatedAt = now,
                };
                db.AiProviderModelCapabilities.Add(row);
            }

            row.SupportsTools = r.SupportsTools;
            row.SupportsVision = r.SupportsVision;
            row.SupportsDocuments = r.SupportsDocuments;
            row.SupportsJsonMode = r.SupportsJsonMode;
            row.SupportsEmbeddings = r.SupportsEmbeddings;
            row.SupportsStreaming = r.SupportsStreaming;
            row.SupportsThinking = r.SupportsThinking;
            row.ThinkingCanBeDisabled = r.ThinkingCanBeDisabled;
            row.AllowedReasoningEffortsCsv = string.Join(',', r.AllowedReasoningEfforts);
            row.MaxTokensCeiling = r.MaxTokensCeiling;
            row.ContextTokens = r.ContextTokens;
            row.ProbeStatus = r.Status;
            row.ProbeDetail = r.Detail;
            row.ProbedAtUtc = now;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Capability probe for '{ProviderCode}' recorded {Count} model(s): {Summary}",
            providerCode, results.Count,
            string.Join("; ", results.Select(r => $"{r.Model}={r.Status}")));
    }

    private static string Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var oneLine = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 300 ? oneLine : oneLine[..300] + "…";
    }

    private static string? Truncate(string? value)
        => value is null || value.Length <= 512 ? value : value[..512];
}