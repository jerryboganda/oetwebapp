using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Ai;

/// <summary>
/// Streaming chat-completions call for any OpenAI-SSE provider whose LIVE PROBE said it streams.
///
/// <para>
/// Origin: the OpenCode gateway (SAMI UAT finding, 2026-10-07) — max-effort reasoning turns
/// legitimately exceed the ~100s non-streamed HttpClient timeout and the gateway's ~120s edge read
/// cap on non-streamed responses. Streaming keeps the connection alive with deltas, so reasoning of
/// any length completes.
/// </para>
///
/// <para>
/// Now used by any provider, because the same wall applies to any long reasoning turn. The
/// previous gate was the OpenCode HOST, which meant every other provider silently took the buffered
/// path — and the assistant then <em>faked</em> streaming by slicing the buffered answer into 80-char
/// bursts, so a learner watched a wall of text arrive at once after a visible stall. Gating on the
/// probed <c>SupportsStreaming</c> flag instead means the behaviour follows what the endpoint was
/// observed to do.
/// </para>
///
/// <para>
/// Parses OpenAI SSE: content deltas, tool-call deltas and finish_reason. A failed or incomplete
/// stream is never replayed.
/// </para>
/// </summary>
public static class StreamingChatCompletionsCall
{
    /// <summary>Master switch, retained under its original name for existing deployments.</summary>
    public static bool IsStreamingEnabled() =>
        Environment.GetEnvironmentVariable("AiOpenAiCompatible__OpenCodeStreaming") is not "false";

    /// <param name="providerLabel">Provider name used in error messages, so a failure names the
    /// vendor that actually failed rather than always saying "OpenCode".</param>
    /// <param name="sendStreamOptions">
    /// Whether to send <c>stream_options.include_usage</c>. Not universal: Z.AI has no
    /// <c>stream_options</c> parameter at all, and sending one is at best ignored and at worst a
    /// 400. Usage is read from the last chunk regardless, which is how Z.AI delivers it, so this can
    /// safely be false.
    /// </param>
    public static async Task<AiProviderCompletion?> CompleteStreamingAsync(
        HttpClient client,
        Dictionary<string, object?> payload,
        AiProviderRequest request,
        string providerLabel,
        bool sendStreamOptions,
        CancellationToken ct)
    {
        var streamPayload = new Dictionary<string, object?>(payload)
        {
            ["stream"] = true,
        };
        if (sendStreamOptions) streamPayload["stream_options"] = new { include_usage = true };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(streamPayload), Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var error = AiProviderErrorParser.Parse(AiProviderErrorDialect.OpenAi, (int)response.StatusCode,
                body, response.Headers, client.DefaultRequestHeaders.Authorization?.Parameter, retainProviderText: true);
            throw new AiProviderHttpException(providerLabel, (int)response.StatusCode, response.ReasonPhrase, error.RetryAfter, error);
        }
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{providerLabel} did not return a completion stream. No automatic replay was attempted.");

        var contentBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        JsonElement? finishElement = null;
        int usagePrompt = 0, usageCompletion = 0;
        string? servedModel = null;
        Dictionary<string, ToolCallAccumulator>? toolAccumulator = null;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        using var streamDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamDeadline.CancelAfter(TimeSpan.FromMinutes(10));
        while (await reader.ReadLineAsync(streamDeadline.Token) is { } line)
        {
            if (line is { Length: > 1048576 }) throw new InvalidOperationException($"{providerLabel} stream event exceeded its limit.");
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            JsonElement chunk;
            try
            {
                using var document = JsonDocument.Parse(data);
                chunk = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new InvalidOperationException($"{providerLabel} returned a malformed completion stream.");
            }
            if (chunk.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            {
                servedModel = modelEl.GetString();
            }
            if (chunk.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
            {
                usagePrompt = usageEl.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number ? pt.GetInt32() : usagePrompt;
                usageCompletion = usageEl.TryGetProperty("completion_tokens", out var ctk) && ctk.ValueKind == JsonValueKind.Number ? ctk.GetInt32() : usageCompletion;
            }
            if (!chunk.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                {
                    var text = contentEl.GetString()!;
                    contentBuffer.Append(text);
                    if (request.OnTextDelta is { } onText) await onText(text, ct);
                }
                if (delta.TryGetProperty("reasoning_content", out var reasoningEl) && reasoningEl.ValueKind == JsonValueKind.String)
                    reasoningBuffer.Append(reasoningEl.GetString());
                if (delta.TryGetProperty("tool_calls", out var toolDeltas) && toolDeltas.ValueKind == JsonValueKind.Array)
                {
                    toolAccumulator ??= new Dictionary<string, ToolCallAccumulator>();
                    foreach (var tc in toolDeltas.EnumerateArray())
                    {
                        var indexKey = tc.TryGetProperty("index", out var idxEl) && idxEl.ValueKind == JsonValueKind.Number
                            ? idxEl.GetInt32().ToString()
                            : "0";
                        if (!toolAccumulator.TryGetValue(indexKey, out var call))
                        {
                            call = new ToolCallAccumulator();
                            toolAccumulator[indexKey] = call;
                        }
                        if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(idEl.GetString()))
                        {
                            call.Id = idEl.GetString()!;
                        }
                        if (tc.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            {
                                call.ToolCode += nameEl.GetString();
                            }
                            if (fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                            {
                                call.ArgsJson += argsEl.GetString();
                            }
                        }
                    }
                }
            }
            if (choice.TryGetProperty("finish_reason", out var finishEl) && finishEl.ValueKind == JsonValueKind.String)
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { finish_reason = finishEl.GetString() }));
                finishElement = doc.RootElement.Clone();
            }
            if (contentBuffer.Length > 262144 || reasoningBuffer.Length > 262144
                || (toolAccumulator?.Values.Sum(c => c.ArgsJson.Length) ?? 0) > 262144
                || (toolAccumulator?.Count ?? 0) > 16)
                throw new InvalidOperationException($"{providerLabel} completion exceeded its bounded output limit.");
        }

        if (toolAccumulator?.Values.Any(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.ToolCode)
            || string.IsNullOrWhiteSpace(c.ArgsJson)) == true)
            throw new InvalidOperationException($"{providerLabel} returned an incomplete tool call.");
        if (toolAccumulator is not null)
            foreach (var call in toolAccumulator.Values)
            {
                using var args = JsonDocument.Parse(call.ArgsJson);
                if (args.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"{providerLabel} tool arguments must be an object.");
            }
        var toolCalls = toolAccumulator is null
            ? null
            : toolAccumulator.Values
                .Where(c => !string.IsNullOrWhiteSpace(c.ToolCode))
                .Select(c => new AiToolCall
                {
                    Id = string.IsNullOrWhiteSpace(c.Id) ? $"call_{Guid.NewGuid():N}"[..24] : c.Id,
                    ToolCode = c.ToolCode,
                    ArgsJson = string.IsNullOrWhiteSpace(c.ArgsJson) ? "{}" : c.ArgsJson,
                })
                .ToList();

        ct.ThrowIfCancellationRequested();
        if (finishElement is null) throw new InvalidOperationException($"{providerLabel} stream ended before completion.");
        if (finishElement.Value.GetProperty("finish_reason").GetString() == "length")
            throw new InvalidOperationException($"{providerLabel} reached its output limit. The incomplete response was not replayed.");
        if (toolCalls is { Count: 0 }) toolCalls = null;
        if (toolCalls is not null && toolCalls.Select(c => c.Id).Distinct().Count() != toolCalls.Count)
            throw new InvalidOperationException($"{providerLabel} returned duplicate tool-call identifiers.");
        if (contentBuffer.Length == 0 && toolCalls is null)
        {
            throw new InvalidOperationException($"{providerLabel} returned no answer or tool calls.");
        }

        JsonElement finish = finishElement
            ?? JsonDocument.Parse(JsonSerializer.Serialize(new { finish_reason = (string?)"stop" })).RootElement.Clone();
        return new AiProviderCompletion
        {
            Text = contentBuffer.ToString(),
            ReasoningContent = reasoningBuffer.ToString(),
            Usage = usagePrompt > 0 || usageCompletion > 0 ? new AiUsage { PromptTokens = usagePrompt, CompletionTokens = usageCompletion } : null,
            ToolCalls = toolCalls,
            FinishReason = finish.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String ? fr.GetString() : null,
            ServedModel = servedModel,
        };
    }
}

/// <summary>Mutable accumulator for streamed tool-call deltas.</summary>
internal sealed class ToolCallAccumulator
{
    public string Id { get; set; } = string.Empty;
    public string ToolCode { get; set; } = string.Empty;
    public string ArgsJson { get; set; } = string.Empty;
}
