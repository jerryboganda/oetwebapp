using System.Net.Http.Headers;
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
// Listening Part B / Part C — AI-assisted answer-key entry.
//
// Part B/C are PDF-backed: the learner reads the printed MCQ on the question
// paper; the only authored data per question is the correct option letter
// (A/B/C) + an optional "why correct" rationale. This service automates that:
//
//   1. Mistral OCR the QuestionPaper PDF(s) → Markdown (Part C has two extracts:
//      C1 + C2, uploaded as two documents).
//   2. Mistral OCR the AnswerKey PDF        → Markdown.
//   3. Claude (forced emit_part_bc_answers tool) reads the correct option per
//      question from the answer key, cross-checks the question paper, and drafts
//      a one-sentence rationale.
//   4. Deterministic server-side validation (numbers in the part's range, letter
//      ∈ {A,B,C}).
//
// Unlike Part A, this returns a PROJECTION only — the admin reviews the answers
// in the answer-sheet UI and persists them through the normal
// replaceListeningStructure path (which carries its own publish/attempt guards).
// ═════════════════════════════════════════════════════════════════════════════

public sealed record ListeningPartBCAnswer(int Number, string CorrectAnswer, string? Rationale);

public sealed record ListeningPartBCImportResult(
    string Part,
    bool IsStub,
    string? StubReason,
    string Summary,
    IReadOnlyList<ListeningPartBCAnswer> Answers);

public interface IListeningPartBCExtractionService
{
    /// <summary>Run the OCR + Claude pipeline against AD-HOC uploaded question
    /// paper(s) + answer-key file for Listening Part B or Part C, and return the
    /// projected per-question correct option (A/B/C) + rationale for the admin to
    /// review then Save. Fast-fails (409) if learner attempts already exist.</summary>
    Task<ListeningPartBCImportResult> ExtractFromUploadAsync(
        string paperId, string part,
        IReadOnlyList<(byte[] Bytes, string Mime)> questionDocs,
        byte[] answerBytes, string answerMime,
        string adminId, CancellationToken ct);
}

public sealed class ListeningPartBCExtractionService(
    LearnerDbContext db,
    IOcrService ocr,
    IAiProviderRegistry registry,
    IHttpClientFactory httpClientFactory,
    IDirectAiCallRecorder usageRecorder,
    TimeProvider clock,
    ILogger<ListeningPartBCExtractionService> logger) : IListeningPartBCExtractionService
{
    private const string AnthropicProviderCode = "anthropic";
    private const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
    private const string DefaultModel = "claude-sonnet-4-6";
    private const string ToolName = "emit_part_bc_answers";

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ListeningPartBCImportResult> ExtractFromUploadAsync(
        string paperId, string part,
        IReadOnlyList<(byte[] Bytes, string Mime)> questionDocs,
        byte[] answerBytes, string answerMime,
        string adminId, CancellationToken ct)
    {
        part = (part ?? string.Empty).Trim().ToUpperInvariant();
        if (part != "B" && part != "C")
            throw ApiException.Validation("listening_partbc_invalid_part", "Part must be 'B' or 'C'.");

        var paper = await db.ContentPapers.FirstOrDefaultAsync(p => p.Id == paperId, ct)
            ?? throw ApiException.NotFound("listening_paper_not_found", "Paper not found.");

        // Never silently replace a structure that learners have already attempted —
        // fast-fail before spending OCR / LLM budget (the Save would be rejected too).
        if (await db.ListeningAttempts.AnyAsync(a => a.PaperId == paperId, ct))
        {
            throw ApiException.Conflict(
                "listening_manifest_attempts_exist",
                "Learner attempts already exist for this paper, so its structure can't be replaced. Create a new revision instead.");
        }

        if (questionDocs is null || questionDocs.Count == 0 || questionDocs.All(d => d.Bytes.Length == 0))
            throw ApiException.Validation("listening_partbc_missing_question_paper",
                $"Upload the Part {part} question paper to extract.");
        if (answerBytes is null || answerBytes.Length == 0)
            throw ApiException.Validation("listening_partbc_missing_answer_key",
                "Upload the answer-key PDF — Part B/C correct options are read from it.");

        // OCR each question document (Part C ships two extracts: C1 + C2) and the key.
        var questionMarkdownParts = new List<string>();
        for (var i = 0; i < questionDocs.Count; i++)
        {
            var (bytes, mime) = questionDocs[i];
            if (bytes.Length == 0) continue;
            var md = await ocr.OcrToMarkdownAsync(bytes, mime, AiFeatureCodes.OcrListeningPartBC, adminId, ct);
            questionMarkdownParts.Add(questionDocs.Count > 1 ? $"--- QUESTION DOCUMENT {i + 1} ---\n{md}" : md);
        }
        var questionMarkdown = string.Join("\n\n", questionMarkdownParts);
        var answerMarkdown = await ocr.OcrToMarkdownAsync(answerBytes, answerMime, AiFeatureCodes.OcrListeningPartBC, adminId, ct);

        var rawJson = await CallClaudeAnswersAsync(part, questionMarkdown, answerMarkdown, adminId, ct);

        BcToolOutput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BcToolOutput>(rawJson, CamelJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Claude returned non-deserialisable Part B/C answers for paper {PaperId}", paperId);
            throw ApiException.Validation("listening_partbc_bad_output",
                "The AI returned answers that could not be parsed. Try again.");
        }

        var (answers, warnings) = ValidateAndProject(part, parsed?.Answers ?? new List<BcToolAnswer>());
        var isStub = warnings.Count > 0;
        var summary = isStub
            ? $"AI extraction for Part {part} with {warnings.Count} issue(s) to review — {answers.Count} answer(s)."
            : $"AI extraction OK for Part {part} — {answers.Count} answer(s).";

        return new ListeningPartBCImportResult(
            part, isStub, isStub ? Truncate(string.Join("; ", warnings), 512) : null, summary, answers);
    }

    // ── Validation (deterministic; never trust the model) ───────────────────────

    private static (IReadOnlyList<ListeningPartBCAnswer> Answers, IReadOnlyList<string> Warnings)
        ValidateAndProject(string part, IReadOnlyList<BcToolAnswer> raw)
    {
        var (lo, hi) = part == "B" ? (25, 30) : (31, 42);
        var warnings = new List<string>();
        var byNumber = new Dictionary<int, ListeningPartBCAnswer>();

        foreach (var a in raw)
        {
            if (a.Number < lo || a.Number > hi)
            {
                warnings.Add($"Ignored out-of-range question {a.Number} (Part {part} is {lo}–{hi}).");
                continue;
            }
            var letter = (a.CorrectAnswer ?? string.Empty).Trim().ToUpperInvariant();
            if (letter is not ("A" or "B" or "C"))
            {
                warnings.Add($"Q{a.Number} has an invalid correct option '{a.CorrectAnswer}'.");
                continue;
            }
            var rationale = string.IsNullOrWhiteSpace(a.Rationale) ? null : Truncate(a.Rationale.Trim(), 1024);
            byNumber[a.Number] = new ListeningPartBCAnswer(a.Number, letter, rationale);
        }

        for (var n = lo; n <= hi; n++)
        {
            if (!byNumber.ContainsKey(n))
                warnings.Add($"No answer extracted for Q{n}.");
        }

        var answers = byNumber.Values.OrderBy(a => a.Number).ToList();
        return (answers, warnings);
    }

    private sealed record BcToolOutput(List<BcToolAnswer>? Answers);
    private sealed record BcToolAnswer(int Number, string? CorrectAnswer, string? Rationale);

    // ── Claude call (forced tool, no temperature for Opus 4.7/4.8) ──────────────

    private async Task<string> CallClaudeAnswersAsync(string part, string questionMarkdown, string answerMarkdown, string adminId, CancellationToken ct)
    {
        var row = await registry.FindByCodeAsync(AnthropicProviderCode, ct)
            ?? throw new InvalidOperationException(
                $"Anthropic provider '{AnthropicProviderCode}' is not registered. Add a row in /admin/ai-providers with Code={AnthropicProviderCode}.");

        var baseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(row.BaseUrl) ? DefaultAnthropicBaseUrl : row.BaseUrl);
        var model = string.IsNullOrWhiteSpace(row.DefaultModel) ? DefaultModel : row.DefaultModel;
        var apiKey = await registry.GetPlatformKeyAsync(AnthropicProviderCode, ct)
            ?? throw new InvalidOperationException($"Platform API key missing for provider {AnthropicProviderCode}.");

        var unsafeReason = AiProviderConnectionTester.GetUnsafeBaseUrlReason(baseUrl);
        if (unsafeReason is not null) throw new InvalidOperationException(unsafeReason);

        var range = part == "B" ? "25-30 (six 3-option MCQs)" : "31-42 (twelve 3-option MCQs)";
        var userText =
            $"PART: {part} — questions {range}.\n\n" +
            "QUESTION PAPER (OCR Markdown):\n\n" + questionMarkdown +
            "\n\n=====\n\nANSWER KEY (OCR Markdown):\n\n" + answerMarkdown;

        var startedAt = clock.GetUtcNow();
        var usageContext = new AiUsageContext(
            UserId: adminId,
            AuthAccountId: null,
            TenantId: null,
            FeatureCode: AiFeatureCodes.ListeningPartBCExtract,
            RulebookVersion: null,
            PromptTemplateId: ToolName,
            SystemPrompt: SystemPrompt,
            UserPrompt: userText,
            StartedAt: startedAt);
        int LatencyMs() => (int)(clock.GetUtcNow() - startedAt).TotalMilliseconds;

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
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
                    ["description"] = "Emit the OET Listening Part B/C answer key (per-question correct option + rationale).",
                    ["input_schema"] = JsonSerializer.Deserialize<JsonElement>(ToolSchemaJson),
                },
            },
            ["tool_choice"] = new Dictionary<string, object?> { ["type"] = "tool", ["name"] = ToolName },
        };

        var client = httpClientFactory.CreateClient("ListeningExtractionAnthropic");
        client.BaseAddress = new Uri(baseUrl + "/");
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
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
                usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
                "anthropic_network", ex.Message, LatencyMs(), "listening.partbc.extract", ct);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            await usageRecorder.RecordFailureAsync(
                usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
                $"http_{(int)response.StatusCode}", Truncate(body, 500), LatencyMs(), "listening.partbc.extract", ct);
            throw new InvalidOperationException(
                $"Claude extraction failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body, 500)}");
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
                        : row.PricePer1kPromptTokens * usage.PromptTokens / 1000m
                          + row.PricePer1kCompletionTokens * usage.CompletionTokens / 1000m;
                    await usageRecorder.RecordSuccessAsync(
                        usageContext, AnthropicProviderCode, model, usage,
                        LatencyMs(), "listening.partbc.extract", cost, ct);
                    return input.GetRawText();
                }
            }
        }

        await usageRecorder.RecordFailureAsync(
            usageContext, AnthropicProviderCode, model, AiCallOutcome.ProviderError,
            "no_tool_use", "Claude did not return a tool_use answers block.", LatencyMs(), "listening.partbc.extract", ct);
        throw new InvalidOperationException("Claude did not return a tool_use answers block.");
    }

    private static AiUsage? ParseAnthropicUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return null;
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    // ── Prompt + tool schema ─────────────────────────────────────────────────

    private const string SystemPrompt = """
You are an expert OET (Occupational English Test) content engineer. Read OCR'd OET
Listening Part B or Part C material and emit the answer key by calling the
emit_part_bc_answers tool.

OET Listening Part B = questions 25-30: six short 3-option multiple-choice questions
(options A, B, C), each about a brief workplace extract.
OET Listening Part C = questions 31-42: twelve 3-option multiple-choice questions
(options A, B, C) across two extracts (31-36, then 37-42).

You receive a QUESTION PAPER (the printed MCQs with their A/B/C options) and an
ANSWER KEY (the official correct option per question). You are told which PART to emit.

For EVERY question in the requested part's range, emit one entry with:
  - number: the printed question number.
  - correctAnswer: the correct option LETTER — exactly one of "A", "B", or "C" — taken
    from the ANSWER KEY (cross-check it against the question paper).
  - rationale: ONE concise sentence (< 240 chars) explaining why that option is correct,
    grounded in the printed options. This is shown to the learner after they submit.

HARD REQUIREMENTS:
  - Emit ONLY questions in the requested part's range (Part B → 25-30; Part C → 31-42).
    Never invent numbers outside that range.
  - correctAnswer MUST be a single uppercase letter: A, B, or C.
  - Provide EVERY question in the range. If the answer key is unclear for one, use your
    best reading of the question paper and still choose a letter.
  - Do not fabricate. Base each rationale on the actual printed options.
""";

    private const string ToolSchemaJson = """
{
  "type": "object",
  "properties": {
    "answers": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "number": { "type": "integer" },
          "correctAnswer": { "type": "string", "enum": ["A", "B", "C"] },
          "rationale": { "type": "string" }
        },
        "required": ["number", "correctAnswer"]
      }
    }
  },
  "required": ["answers"]
}
""";
}
