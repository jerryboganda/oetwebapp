using System.Text;
using System.Text.Json;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part A advisory review - Anthropic transport half.
//
// Split out of ListeningPartAAiScoringService.cs so both halves stay under the
// repo 500-line rule (same partial convention as AdminService.ContentAdmin.cs).
// This half owns provider resolution, the forced-tool request, HTTP outcome
// classification and response parsing. It never writes to the database and
// never persists a raw provider body.
// ═════════════════════════════════════════════════════════════════════════════

public sealed partial class ListeningPartAAiScoringService
{
    private const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
    private const string DefaultModel = "claude-sonnet-5";
    private const string ToolName = "emit_part_a_verdicts";

    // ── Claude call (forced tool) ───────────────────────────────────────────────

    private sealed record Provider(string BaseUrl, string Model, string ApiKey, AiProvider Row);

    private async Task<Provider?> ResolveProviderAsync(CancellationToken ct)
    {
        var row = await registry.FindByCodeAsync(AnthropicProviderCode, ct);
        if (row is null) return null;
        var apiKey = await registry.GetPlatformKeyAsync(AnthropicProviderCode, ct);
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var baseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(row.BaseUrl) ? DefaultAnthropicBaseUrl : row.BaseUrl);
        if (AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl) is not null) return null;
        var model = string.IsNullOrWhiteSpace(row.DefaultModel) ? DefaultModel : row.DefaultModel;
        return new Provider(baseUrl, model, apiKey, row);
    }

    private async Task<ProviderCallOutcome> CallClaudeVerdictsAsync(
        IReadOnlyList<GapItem> items, string learnerId, Provider provider, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Judge each candidate gap answer for OET Listening Part A note-completion.");
        foreach (var grp in items.GroupBy(i => i.Context))
        {
            sb.AppendLine();
            sb.AppendLine("CONSULTATION NOTE (blanks shown as ____):");
            sb.AppendLine(string.IsNullOrWhiteSpace(grp.Key) ? "(note text unavailable)" : grp.Key);
            sb.AppendLine("GAPS:");
            foreach (var it in grp.OrderBy(i => i.Number))
            {
                var accepted = it.Accepted.Count > 0 ? " | also accepted: " + string.Join(", ", it.Accepted) : string.Empty;
                sb.AppendLine($"({it.Number}) candidate: \"{it.UserAnswer}\" | official answer: \"{it.Canonical}\"{accepted}");
                sb.AppendLine($"    approved rationale: {it.ApprovedRationale}");
            }
        }
        var userText = sb.ToString();

        var startedAt = clock.GetUtcNow();
        var usageContext = new AiUsageContext(
            UserId: learnerId,
            AuthAccountId: null,
            TenantId: null,
            FeatureCode: AiFeatureCodes.ListeningPartAScore,
            RulebookVersion: null,
            PromptTemplateId: ToolName,
            SystemPrompt: SystemPrompt,
            UserPrompt: userText,
            StartedAt: startedAt);
        int LatencyMs() => (int)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        var payload = new Dictionary<string, object?>
        {
            ["model"] = provider.Model,
            ["max_tokens"] = 4000,
            ["system"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = SystemPrompt,
                    ["cache_control"] = new Dictionary<string, object?> { ["type"] = "ephemeral" },
                },
            },
            ["messages"] = new object[]
            {
                new Dictionary<string, object?> { ["role"] = "user", ["content"] = userText },
            },
            ["tools"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = ToolName,
                    ["description"] = "Emit one post-submit advisory review per provided gap number; never change the deterministic mark.",
                    ["input_schema"] = JsonSerializer.Deserialize<JsonElement>(ToolSchemaJson),
                },
            },
            ["tool_choice"] = new Dictionary<string, object?> { ["type"] = "tool", ["name"] = ToolName },
        };

        var client = httpClientFactory.CreateClient("ListeningPartAScoringAnthropic");
        client.BaseAddress = new Uri(provider.BaseUrl + "/");
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Add("x-api-key", provider.ApiKey);
        client.DefaultRequestHeaders.Remove("anthropic-version");
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        // One response object, one owner: every exit path below runs through the
        // finally, so no HttpResponseMessage (and no pooled connection) can leak,
        // including the caller-cancellation rethrow.
        HttpResponseMessage? response = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            try
            {
                // ResponseHeadersRead keeps "failed while sending" separable from
                // "failed while reading the body of a request the provider already
                // accepted" — only the first is safe to repeat.
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Genuine shutdown / caller cancellation: no bookkeeping, no state
                // change, nothing persisted. The finally still disposes.
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient timeout. The request may already have been accepted
                // and billed, so the outcome is ambiguous — never auto-repeat it.
                await RecordFailureAsync(AiCallOutcome.Timeout, "anthropic_timeout",
                    "Anthropic request timed out before response headers were read.");
                return ProviderCallOutcome.Terminal("anthropic_timeout", ListeningPartAAiSkipReasons.IndeterminateTimeout);
            }
            catch (Exception ex) when (IsSafePreSendFailure(ex))
            {
                // DNS never resolved / the proxy refused the CONNECT tunnel: the
                // provider cannot have received the request, so nothing was
                // billed and a retry is safe inside the attempt cap. Only the
                // exception TYPE is recorded — messages can carry URLs and PII.
                await RecordFailureAsync(AiCallOutcome.ProviderError, "anthropic_connect",
                    $"Anthropic connection failure before send ({ex.GetType().Name}).");
                logger.LogWarning(ex, "Part A AI advisory review: Anthropic pre-send connection failure.");
                return ProviderCallOutcome.Retry("anthropic_connect", null);
            }
            catch (Exception ex)
            {
                // Anything else that escapes SendAsync — including connection and
                // TLS failures, which .NET also raises for mid-flight resets —
                // may have happened at or after the point the request was on the
                // wire: ambiguous, possibly billed, therefore terminal. Never
                // re-called automatically.
                await RecordFailureAsync(AiCallOutcome.ProviderError, "anthropic_indeterminate",
                    $"Anthropic transport failure with an unknown send outcome ({ex.GetType().Name}).");
                logger.LogError(ex, "Part A AI advisory review: Anthropic transport failure with an indeterminate outcome.");
                return ProviderCallOutcome.Terminal("anthropic_indeterminate", ListeningPartAAiSkipReasons.IndeterminateTimeout);
            }

            var statusCode = (int)response.StatusCode;
            var retryAfter = ReadRetryAfter(response, clock.GetUtcNow());

            if (!response.IsSuccessStatusCode)
            {
                var errorClass = $"http_{statusCode}";
                // The raw provider body is deliberately NOT read or persisted: it
                // can echo candidate text and, on some gateways, credential
                // fragments. The status line alone drives the classification.
                await RecordFailureAsync(AiCallOutcome.ProviderError, errorClass,
                    $"Anthropic returned HTTP {statusCode} for {AiFeatureCodes.ListeningPartAScore}.");

                var terminalReason = ListeningPartAAiRetryPolicy.TerminalSkipReasonForStatus(statusCode);
                return terminalReason is null
                    ? ProviderCallOutcome.Retry(errorClass, retryAfter)
                    : ProviderCallOutcome.Terminal(errorClass, terminalReason);
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The provider answered 2xx — the call is spent and may be billed —
                // but the body was lost. Repeating it would risk a duplicate
                // charge for the same evidence, so this is terminal.
                await RecordFailureAsync(AiCallOutcome.Timeout, "anthropic_body_read",
                    $"Anthropic 2xx response body could not be read ({ex.GetType().Name}).");
                logger.LogError(ex, "Part A AI advisory review: Anthropic response body read failed after a 2xx.");
                return ProviderCallOutcome.Terminal("anthropic_body_read", ListeningPartAAiSkipReasons.IndeterminateTimeout);
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                await RecordFailureAsync(AiCallOutcome.ProviderError, "invalid_json",
                    "Anthropic returned a 2xx body that was not valid JSON.");
                return ProviderCallOutcome.Terminal("invalid_json", ListeningPartAAiSkipReasons.NoMatchingVerdicts);
            }

            using (doc)
            {
                var usage = ParseAnthropicUsage(doc.RootElement);
                if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in content.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var t)
                            && string.Equals(t.GetString(), "tool_use", StringComparison.Ordinal)
                            && block.TryGetProperty("input", out var input))
                        {
                            var cost = usage is null
                                ? 0m
                                : provider.Row.PricePer1kPromptTokens * usage.PromptTokens / 1000m
                                  + provider.Row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
                            await usageRecorder.RecordSuccessAsync(
                                usageContext, AnthropicProviderCode, provider.Model, usage,
                                LatencyMs(), AiFeatureCodes.ListeningPartAScore, cost, ct);
                            return ProviderCallOutcome.Succeeded(ParseVerdicts(input));
                        }
                    }
                }
            }

            // Locally valid, schema-shaped response with no verdicts tool block. The
            // call was already paid for; repeating it with identical evidence would
            // only buy the same unusable answer, so this is terminal, not retryable.
            await RecordFailureAsync(AiCallOutcome.ProviderError, "no_tool_use",
                "Anthropic returned no verdicts tool_use block.");
            return ProviderCallOutcome.Terminal("no_tool_use", ListeningPartAAiSkipReasons.NoMatchingVerdicts);
        }
        finally
        {
            response?.Dispose();
        }

        Task RecordFailureAsync(AiCallOutcome outcome, string errorClass, string sanitizedMessage)
            => usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, provider.Model, outcome,
                errorClass, sanitizedMessage, LatencyMs(), AiFeatureCodes.ListeningPartAScore, ct);
    }

    /// <summary>
    /// True only for transport failures the runtime raises <b>before</b> any byte
    /// of the request can reach the provider, so no call can have been accepted
    /// or billed and a retry inside the attempt cap carries no duplicate-charge
    /// risk:
    /// <list type="bullet">
    ///   <item><c>NameResolutionError</c> — DNS never resolved, so no socket was
    ///   ever opened to the provider.</item>
    ///   <item><c>ProxyTunnelError</c> — the forward proxy refused to establish
    ///   the CONNECT tunnel, so the request was never tunnelled.</item>
    /// </list>
    /// Everything else is treated as indeterminate and terminal. In particular
    /// <c>ConnectionError</c> and <c>SecureConnectionError</c> are deliberately
    /// NOT in this list: .NET raises both for failures that can occur at any
    /// point in the connection's life — a mid-flight reset after the request was
    /// written, or a TLS failure on a renegotiation/HTTP2 stream — and
    /// <c>HttpRequestError</c> does not tell us which. Until the runtime
    /// guarantees the request had not left the process, the only safe reading is
    /// "may already have been accepted and billed", so those failures close the
    /// answer as <c>indeterminate_timeout</c> rather than buying a possible
    /// second charge for the same evidence.
    /// </summary>
    private static bool IsSafePreSendFailure(Exception ex)
        => ex is HttpRequestException
        {
            HttpRequestError: HttpRequestError.NameResolutionError
                or HttpRequestError.ProxyTunnelError,
        };

    /// <summary>Honour an explicit <c>Retry-After</c> (delta or HTTP date).</summary>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;
        if (header.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (header.Date is { } date && date > now) return date - now;
        return null;
    }

    private static List<Verdict> ParseVerdicts(JsonElement input)
    {
        var result = new List<Verdict>();
        if (!input.TryGetProperty("verdicts", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var v in arr.EnumerateArray())
        {
            var number = v.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : -1;
            if (number < 0) continue;
            var verdict = v.TryGetProperty("verdict", out var vd) ? vd.GetString() : null;
            var rationale = v.TryGetProperty("rationale", out var r) ? r.GetString() : null;
            result.Add(new Verdict(number, verdict, rationale));
        }
        return result;
    }

    private static AiUsage? ParseAnthropicUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return null;
        var input = u.TryGetProperty("input_tokens", out var it) && it.ValueKind == JsonValueKind.Number ? it.GetInt32() : 0;
        var output = u.TryGetProperty("output_tokens", out var ot) && ot.ValueKind == JsonValueKind.Number ? ot.GetInt32() : 0;
        return new AiUsage { PromptTokens = input, CompletionTokens = output };
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].TrimEnd('/');
        return trimmed;
    }

    private const string SystemPrompt = """
You provide a post-submit advisory review of OET (Occupational English Test) Listening Part A
note-completion answers. The server's deterministic mark is authoritative and is never changed by
this call. You must not award partial credit, approve a synonym, excuse a misspelling, or infer an
accepted variant. Do not claim to provide an official OET result.

For each numbered gap you receive: the consultation note for context, the candidate's typed answer,
the canonical answer, explicitly authorised variants, and the author-approved rationale. Use only
that stored evidence. If the evidence is incomplete, say so in the rationale and mark the advisory
verdict "incorrect"; do not invent an explanation.

Call the emit_part_a_verdicts tool exactly once with one advisory verdict per gap:
  - "correct": only when the stored deterministic answer/variant evidence shows an exact match.
  - "incorrect": when it does not show an exact match, the answer is blank, or evidence is missing.
  - "acceptable" is forbidden and must never be emitted.

Give a concise evidence-based rationale per gap and return EXACTLY one verdict object per gap number
you were given. This advisory output is tutor-facing only and must not be presented as a score.
""";

    private const string ToolSchemaJson = """
{
  "type": "object",
  "properties": {
    "verdicts": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "number": { "type": "integer" },
          "verdict": { "type": "string", "enum": ["correct", "incorrect"] },
          "rationale": { "type": "string" }
        },
        "required": ["number", "verdict"]
      }
    }
  },
  "required": ["verdicts"]
}
""";
}
