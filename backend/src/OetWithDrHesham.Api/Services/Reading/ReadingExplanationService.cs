using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Rulebook;

namespace OetWithDrHesham.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Explanation Service — WS4
//
// Generates or retrieves per-question "why was the correct answer right /
// why was my selected option wrong" explanations for Reading Module questions.
//
// Two-tier strategy:
//   1. Pre-generated: if the question's ExplanationMarkdown already contains
//      a JSON payload (prefixed with ":::json"), deserialise and return it.
//   2. AI-generated: call Claude via the grounded gateway with the Reading
//      rulebook; cache the result back onto ExplanationMarkdown so subsequent
//      requests for the same question + wrong option are instant.
//
// Feature code: reading.explanation.v1 (registered in AiFeatureCodes).
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingExplanationService
{
    /// <summary>Return a structured explanation for why the correct answer is
    /// correct and why the learner's <paramref name="wrongOption"/> was wrong.</summary>
    /// <param name="questionId">String PK of the <see cref="ReadingQuestion"/>.</param>
    /// <param name="wrongOption">The option key the learner selected (e.g. "A").</param>
    /// <param name="language">"en" or "ar"; defaults to "en".</param>
    Task<ExplanationDto> GetExplanationAsync(
        string questionId,
        string wrongOption,
        string language,
        CancellationToken ct);
}

public sealed record ExplanationDto(
    string WhyCorrect,
    string WhyWrong,
    string TrapName,
    string AvoidTip,
    string Language);   // "en" | "ar"

public sealed class ReadingExplanationService(
    LearnerDbContext db,
    IRulebookLoader rulebookLoader,
    IAiGatewayService gateway,
    ILogger<ReadingExplanationService>? logger = null)
    : IReadingExplanationService
{
    /// <summary>Prefix written into ExplanationMarkdown to signal cached JSON.</summary>
    private const string CachePrefix = ":::json\n";

    public async Task<ExplanationDto> GetExplanationAsync(
        string questionId,
        string wrongOption,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(questionId))
            throw new ArgumentException("questionId must not be empty.", nameof(questionId));

        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

        var question = await db.ReadingQuestions
            .FirstOrDefaultAsync(q => q.Id == questionId, ct)
            ?? throw new KeyNotFoundException($"ReadingQuestion '{questionId}' not found.");

        // 1. Try pre-generated cache stored in ExplanationMarkdown.
        if (!string.IsNullOrWhiteSpace(question.ExplanationMarkdown)
            && question.ExplanationMarkdown.StartsWith(CachePrefix, StringComparison.Ordinal))
        {
            var cached = TryDeserializeCachedExplanations(
                question.ExplanationMarkdown[CachePrefix.Length..]);
            if (cached is not null)
            {
                var key = $"{wrongOption.Trim().ToUpperInvariant()}:{lang}";
                if (cached.TryGetValue(key, out var hit))
                    return hit;
            }
        }

        // 2. AI-generate the explanation.
        var correctAnswer = ResolveCorrectAnswer(question);
        var generated = await GenerateExplanationAsync(question, correctAnswer, wrongOption, lang, ct);

        // 3. Cache back onto the entity (append to existing cache blob).
        await PersistCacheAsync(question, wrongOption, lang, generated, ct);

        return generated;
    }

    // ── AI generation ───────────────────────────────────────────────────────

    private async Task<ExplanationDto> GenerateExplanationAsync(
        ReadingQuestion question,
        string correctAnswer,
        string wrongOption,
        string language,
        CancellationToken ct)
    {
        OetRulebook rulebook;
        try
        {
            rulebook = rulebookLoader.Load(RuleKind.Reading, ExamProfession.Medicine);
        }
        catch (RulebookNotFoundException)
        {
            rulebook = new OetRulebook { Version = "fallback", Kind = RuleKind.Reading };
        }

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Reading,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateReadingExplanation,
        });

        var userMessage = BuildExplanationPrompt(question, correctAnswer, wrongOption, language);

        ExplanationDto? parsed = null;
        try
        {
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = string.Empty,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.ReadingExplanation,
                UserId = null,
            }, ct);

            parsed = TryParseExplanation(result.Completion, language);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "ReadingExplanationService — AI call failed for question '{QuestionId}'; returning fallback.",
                question.Id);
        }

        return parsed ?? BuildFallbackExplanation(question, correctAnswer, wrongOption, language);
    }

    // ── Prompt builder ──────────────────────────────────────────────────────

    private static string BuildExplanationPrompt(
        ReadingQuestion question,
        string correctAnswer,
        string wrongOption,
        string language)
    {
        var sb = new StringBuilder();
        sb.AppendLine("For an OET Reading question:");
        sb.AppendLine();
        sb.AppendLine($"Question: {question.Stem}");
        sb.AppendLine($"Correct answer: {correctAnswer}");
        sb.AppendLine($"Student selected: {wrongOption}");

        if (language == "ar")
        {
            sb.AppendLine();
            sb.AppendLine("Respond in Arabic (العربية). Use clear, accessible language for an OET candidate.");
        }

        sb.AppendLine();
        sb.AppendLine("Return a SINGLE JSON object (no extra text):");
        sb.AppendLine("{");
        sb.AppendLine("  \"whyCorrect\": \"explain in ≤ 40 words why the correct answer is right\",");
        sb.AppendLine("  \"whyWrong\": \"explain in ≤ 40 words why the student's chosen option is a trap\",");
        sb.AppendLine("  \"trapName\": \"one of: Opposite|DistortedDetail|NotInText|TooGeneral|TooSpecific|ReusedKeyword|WrongSpeaker\",");
        sb.AppendLine("  \"avoidTip\": \"one actionable tip (≤ 25 words) to avoid this trap in future\"");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── JSON parsing ────────────────────────────────────────────────────────

    private static ExplanationDto? TryParseExplanation(string completion, string language)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var json = ExtractJsonBlock(completion);
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var whyCorrect = SafeString(root, "whyCorrect");
            var whyWrong = SafeString(root, "whyWrong");
            var trapName = SafeString(root, "trapName");
            var avoidTip = SafeString(root, "avoidTip");

            if (string.IsNullOrWhiteSpace(whyCorrect) || string.IsNullOrWhiteSpace(whyWrong))
                return null;

            return new ExplanationDto(
                WhyCorrect: whyCorrect!.Trim(),
                WhyWrong: whyWrong!.Trim(),
                TrapName: (trapName ?? "Unknown").Trim(),
                AvoidTip: (avoidTip ?? "").Trim(),
                Language: language);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExplanationDto BuildFallbackExplanation(
        ReadingQuestion question,
        string correctAnswer,
        string wrongOption,
        string language)
        => new(
            WhyCorrect: $"The correct answer is '{correctAnswer}' based on the text provided.",
            WhyWrong: $"Option '{wrongOption}' is a distractor; review the passage again carefully.",
            TrapName: "Unknown",
            AvoidTip: "Re-read the relevant paragraph and locate the evidence directly.",
            Language: language);

    // ── Cache persistence ───────────────────────────────────────────────────

    private async Task PersistCacheAsync(
        ReadingQuestion question,
        string wrongOption,
        string language,
        ExplanationDto dto,
        CancellationToken ct)
    {
        try
        {
            // Deserialise existing cache (if any) and add / overwrite the new entry.
            Dictionary<string, ExplanationDto> cache;
            if (!string.IsNullOrWhiteSpace(question.ExplanationMarkdown)
                && question.ExplanationMarkdown.StartsWith(CachePrefix, StringComparison.Ordinal))
            {
                cache = TryDeserializeCachedExplanations(
                    question.ExplanationMarkdown[CachePrefix.Length..])
                    ?? new Dictionary<string, ExplanationDto>(StringComparer.Ordinal);
            }
            else
            {
                cache = new Dictionary<string, ExplanationDto>(StringComparer.Ordinal);
            }

            var key = $"{wrongOption.Trim().ToUpperInvariant()}:{language}";
            cache[key] = dto;

            var newPayload = CachePrefix + JsonSerializer.Serialize(cache);

            // Cap at 4096 chars to match the field constraint. If the serialised
            // cache would exceed the limit, skip persisting (no data loss — the
            // original ExplanationMarkdown content is preserved).
            if (newPayload.Length <= 4096)
            {
                question.ExplanationMarkdown = newPayload;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Cache write failure is non-fatal; log and continue.
            logger?.LogWarning(ex,
                "ReadingExplanationService — failed to persist explanation cache for question '{QuestionId}'.",
                question.Id);
        }
    }

    private static Dictionary<string, ExplanationDto>? TryDeserializeCachedExplanations(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, ExplanationDto>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Extract the human-readable correct answer from CorrectAnswerJson.
    /// For MCQ questions this is the option key string (e.g. "A");
    /// for short-answer it is the canonical answer text.</summary>
    private static string ResolveCorrectAnswer(ReadingQuestion question)
    {
        if (string.IsNullOrWhiteSpace(question.CorrectAnswerJson))
            return "(not set)";

        var raw = question.CorrectAnswerJson.Trim();

        // Quoted string: "A" → A
        if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length >= 2)
            return raw[1..^1];

        // Array: ["1","3"] → 1, 3
        if (raw.StartsWith("["))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(raw) ?? new();
                return string.Join(", ", arr);
            }
            catch (JsonException) { }
        }

        return raw;
    }

    private static string? ExtractJsonBlock(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}")) return trimmed;
        var fenceStart = trimmed.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (fenceStart < 0) fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart < 0) return null;
        var afterFence = trimmed.IndexOf('\n', fenceStart);
        if (afterFence < 0) return null;
        var closeFence = trimmed.IndexOf("```", afterFence + 1, StringComparison.Ordinal);
        if (closeFence < 0) return null;
        var inner = trimmed[(afterFence + 1)..closeFence].Trim();
        return inner.StartsWith("{") && inner.EndsWith("}") ? inner : null;
    }

    private static string? SafeString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
