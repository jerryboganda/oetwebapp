using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Part A — AI marking (Claude Sonnet 4.6).
//
// ADDITIVE + NON-BLOCKING. The deterministic grader (ListeningGradingService)
// stays the score of record. This service adds a SEPARATE per-gap AI judgement
// (lenient on paraphrase / word-form / spelling, the way a human OET marker is)
// onto each Part A fill-in-the-blank answer, surfaced to the learner review and
// the tutor checking flow. It runs after submit (via the background worker), is
// idempotent (only touches answers where AiScoredAt is null), and NEVER throws
// into the submit/grade path — a provider failure just leaves the answer
// unscored for the next worker pass.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningPartAAiScoringService
{
    /// <summary>Score every not-yet-AI-scored Part A short-answer answer on a
    /// submitted attempt in one batched Claude call. Idempotent + best-effort.</summary>
    Task ScoreAttemptAsync(string attemptId, CancellationToken ct);
}

public sealed class ListeningPartAAiScoringService(
    LearnerDbContext db,
    IAiProviderRegistry registry,
    IHttpClientFactory httpClientFactory,
    IDirectAiCallRecorder usageRecorder,
    TimeProvider clock,
    ILogger<ListeningPartAAiScoringService> logger) : IListeningPartAAiScoringService
{
    public const string AnthropicProviderCode = "anthropic";
    private const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
    private const string DefaultModel = "claude-sonnet-4-6";
    private const string ToolName = "emit_part_a_verdicts";

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record GapItem(int Number, string Context, string UserAnswer, string Canonical, IReadOnlyList<string> Accepted);
    private sealed record Verdict(int Number, string? Verdict_, string? Rationale);

    public async Task ScoreAttemptAsync(string attemptId, CancellationToken ct)
    {
        var attempt = await db.ListeningAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Status != ListeningAttemptStatus.Submitted) return;

        var answers = await db.ListeningAnswers
            .Where(a => a.ListeningAttemptId == attemptId && a.AiScoredAt == null)
            .ToListAsync(ct);
        if (answers.Count == 0) return;

        var questionIds = answers.Select(a => a.ListeningQuestionId).Distinct().ToList();
        var questions = await db.ListeningQuestions
            .Where(q => questionIds.Contains(q.Id)
                && (q.QuestionType == ListeningQuestionType.ShortAnswer
                    || q.QuestionType == ListeningQuestionType.FillInBlank))
            .ToListAsync(ct);
        if (questions.Count == 0) return;
        var qById = questions.ToDictionary(q => q.Id);

        var partAAnswers = answers.Where(a => qById.ContainsKey(a.ListeningQuestionId)).ToList();
        if (partAAnswers.Count == 0) return;

        var extractIds = questions
            .Where(q => !string.IsNullOrEmpty(q.ListeningExtractId))
            .Select(q => q.ListeningExtractId!)
            .Distinct()
            .ToList();
        var notesByExtract = await db.ListeningExtracts
            .Where(e => extractIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.NotesBodyMarkdown ?? string.Empty, ct);

        var items = partAAnswers
            .Select(a => (a, q: qById[a.ListeningQuestionId]))
            .OrderBy(x => x.q.QuestionNumber)
            .Select(x => new GapItem(
                Number: x.q.QuestionNumber,
                Context: x.q.ListeningExtractId is { } eid && notesByExtract.TryGetValue(eid, out var nb) ? nb : string.Empty,
                UserAnswer: TryReadString(x.a.UserAnswerJson) ?? string.Empty,
                Canonical: TryReadString(x.q.CorrectAnswerJson) ?? string.Empty,
                Accepted: ParseAccepted(x.q.AcceptedSynonymsJson)))
            .ToList();

        var provider = await ResolveProviderAsync(ct);
        if (provider is null)
        {
            logger.LogDebug("Part A AI scoring skipped for attempt {AttemptId}: anthropic provider/key not configured.", attemptId);
            return;
        }

        List<Verdict> verdicts;
        try
        {
            verdicts = await CallClaudeVerdictsAsync(items, attempt.UserId, provider, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Part A AI scoring call failed for attempt {AttemptId}; leaving unscored for retry.", attemptId);
            return;
        }

        var verdictByNumber = verdicts
            .GroupBy(v => v.Number)
            .ToDictionary(g => g.Key, g => g.First());

        var now = clock.GetUtcNow();
        var scored = 0;
        foreach (var a in partAAnswers)
        {
            var q = qById[a.ListeningQuestionId];
            if (!verdictByNumber.TryGetValue(q.QuestionNumber, out var v)) continue;
            a.AiVerdict = NormalizeVerdict(v.Verdict_);
            a.AiRationale = Truncate(v.Rationale ?? string.Empty, 1024);
            a.AiScoredAt = now;
            a.AiModel = provider.Model;
            scored++;
        }
        if (scored > 0) await db.SaveChangesAsync(ct);
        logger.LogInformation("Part A AI scoring: stamped {Scored}/{Total} answers on attempt {AttemptId}.", scored, partAAnswers.Count, attemptId);
    }

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

    private async Task<List<Verdict>> CallClaudeVerdictsAsync(
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
                    ["description"] = "Emit one verdict per provided gap number.",
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

        HttpResponseMessage response;
        string body;
        try
        {
            response = await client.PostAsync(
                "v1/messages",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, provider.Model, AiCallOutcome.ProviderError,
                "anthropic_network", ex.Message, LatencyMs(), "listening.parta.score", ct);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            await usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, provider.Model, AiCallOutcome.ProviderError,
                $"http_{(int)response.StatusCode}", Truncate(body, 500), LatencyMs(), "listening.parta.score", ct);
            throw new InvalidOperationException($"Claude scoring failed: HTTP {(int)response.StatusCode}. {Truncate(body, 300)}");
        }
        response.Dispose();

        using var doc = JsonDocument.Parse(body);
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
                        LatencyMs(), "listening.parta.score", cost, ct);
                    return ParseVerdicts(input);
                }
            }
        }

        await usageRecorder.RecordFailureAsync(
            usageContext, AnthropicProviderCode, provider.Model, AiCallOutcome.ProviderError,
            "no_tool_use", "Claude did not return a verdicts tool_use block.", LatencyMs(), "listening.parta.score", ct);
        throw new InvalidOperationException("Claude did not return a verdicts tool_use block.");
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

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string? TryReadString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var el = JsonSerializer.Deserialize<JsonElement>(json);
            return el.ValueKind == JsonValueKind.String ? el.GetString() : json;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static IReadOnlyList<string> ParseAccepted(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeVerdict(string? raw)
    {
        var v = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "correct" => "correct",
            "acceptable" => "acceptable",
            _ => "incorrect",
        };
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].TrimEnd('/');
        return trimmed;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private const string SystemPrompt = """
You are a fair, experienced OET (Occupational English Test) Listening examiner marking Part A
note-completion gaps. For each numbered gap you receive: the consultation note for context, the
candidate's typed answer, the official answer, and any officially accepted variants.

Mark each gap with one verdict by calling the emit_part_a_verdicts tool:
  - "correct": matches the official answer (or an accepted variant) exactly or trivially
    (case, surrounding whitespace).
  - "acceptable": a human OET marker would award the mark — minor misspelling that is
    unambiguous, a clear word-form/plural/tense variant, a synonym or short paraphrase that
    carries the same required meaning, or correct content with harmless extra words.
  - "incorrect": wrong meaning, the wrong piece of information, blank, or unintelligible.

Be reasonably lenient on spelling/word-form (OET awards the mark when the answer is
recognisable and unambiguous) but strict on meaning. Give a one-line rationale per gap.
Return EXACTLY one verdict object per gap number you were given.
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
          "verdict": { "type": "string", "enum": ["correct", "acceptable", "incorrect"] },
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
