using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Assessment;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Explanation Service — WS4
//
// Generates or retrieves per-question "why was the correct answer right /
// why was my selected option wrong" explanations for Reading Module questions.
//
// AI-generated only after submission: call the grounded gateway with the
// Reading rulebook. Author-approved rationale and source evidence are required
// before the call; the generated explanation is never written to shared
// question content.
//
// Feature code: reading.explanation.v1 (registered in AiFeatureCodes).
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingExplanationService
{
    /// <summary>
    /// Generate a learner-facing explanation only for an answer stored on the
    /// caller's submitted attempt. The selected answer is read from the
    /// server-side answer row; callers cannot substitute arbitrary evidence.
    /// </summary>
    Task<ExplanationDto> GetSubmittedAttemptExplanationAsync(
        string userId,
        string attemptId,
        string questionId,
        string language,
        CancellationToken ct);
}

public sealed class ReadingGroundedExplanationUnavailableException(string message)
    : Exception(message);

public sealed record ExplanationDto(
    string WhyCorrect,
    string WhyWrong,
    string TrapName,
    string AvoidTip,
    string Language,
    bool Cached = false);   // "en" | "ar"

public sealed class ReadingExplanationService(
    LearnerDbContext db,
    IRulebookLoader rulebookLoader,
    IAiGatewayService gateway,
    ILogger<ReadingExplanationService>? logger = null,
    IAiExplanationCacheService? explanationCache = null,
    IAiResultCacheService? resultCache = null)
    : IReadingExplanationService
{
    // v2: trapName vocabulary is now the canonical ReadingDistractorCategory names. The version is part of
    // both cache keys, so explanations cached under the old v1 vocabulary are never served again.
    private const string PromptTemplateId = "reading.explanation.v2";
    private const string Module = "reading";

    // Key variant for the learner's own copy of a Jev-held explanation. It must NOT share the normal key:
    // AiResultCaches.CacheKey is unique and never purged, so an expired short-lived row under the normal
    // key would silently block every later normal store (StoreAsync swallows the unique violation).
    private const string HeldPromptVersion = PromptTemplateId + ".jev-held";

    private static readonly string TrapNameVocabulary = string.Join('|', Enum.GetNames<ReadingDistractorCategory>());

    // ── AI generation ───────────────────────────────────────────────────────

    private async Task<ExplanationDto> GenerateExplanationAsync(
        ReadingQuestion question,
        string attemptId,
        string rationaleId,
        string correctAnswer,
        string wrongOption,
        string language,
        string approvedRationale,
        string sourceSentence,
        string? sourcePassage,
        string? userId,
        CancellationToken ct)
    {
        // W5 AiResultCache first (attempt + question + answer + language +
        // prompt/rulebook versions), then the W3 cross-learner explanation cache.
        string? resultCacheKey = null;
        string? heldCacheKey = null;
        if (resultCache is not null)
        {
            resultCacheKey = resultCache.BuildCacheKey(
                AiFeatureCodes.ReadingExplanation,
                Module,
                attemptId,
                question.Id,
                wrongOption,
                language,
                PromptTemplateId,
                rationaleId);
            var cachedJson = await resultCache.TryGetAsync(resultCacheKey, ct);
            if (cachedJson is not null
                && TryDeserializeExplanation(cachedJson, language, out var fromResultCache)
                && fromResultCache is not null)
            {
                return fromResultCache with { Cached = true };
            }

            // This learner's own re-view of an explanation Jev held back (see the gate below).
            heldCacheKey = resultCache.BuildCacheKey(
                AiFeatureCodes.ReadingExplanation,
                Module,
                attemptId,
                question.Id,
                wrongOption,
                language,
                HeldPromptVersion,
                rationaleId);
            var heldJson = await resultCache.TryGetAsync(heldCacheKey, ct);
            if (heldJson is not null
                && TryDeserializeExplanation(heldJson, language, out var fromHeld)
                && fromHeld is not null)
            {
                return fromHeld with { Cached = true };
            }
        }

        string? cacheKey = null;
        if (explanationCache is not null)
        {
            // The shared key has no prompt-version slot, so the template id rides in extraEvidence.
            cacheKey = explanationCache.BuildCacheKey(
                Module, question.Id, questionVersion: null,
                normalizedSelectedAnswer: wrongOption, language,
                approvedRationale, sourceSentence, extraEvidence: PromptTemplateId + "\n" + sourcePassage);

            var cached = await TryReadCacheAsync(cacheKey, language, ct);
            if (cached is not null) return cached with { Cached = true };
        }

        var (generated, advisory) = await CallGatewayAsync(
            question, correctAnswer, wrongOption, language, approvedRationale, sourceSentence, sourcePassage, userId, ct);

        var payload = System.Text.Json.JsonSerializer.Serialize(generated);

        // Jev flagged this explanation: the learner still gets it, but it must not be replayed from the 30-day
        // result cache or the cross-learner cache. The one exception is this learner's own re-view inside the
        // AI replay window: the coordinator refuses an identical gateway call there (the re-view would fail),
        // so the learner keeps their copy under the held key for exactly that window.
        // ponytail: ttl pinned to the default window; read Ai:Coordination:ReplayWindowSeconds here if ops ever raises it.
        if (JevHoldsBackFromCache(advisory, question.Id))
        {
            if (heldCacheKey is not null)
            {
                await resultCache!.StoreAsync(
                    heldCacheKey,
                    AiFeatureCodes.ReadingExplanation,
                    Module,
                    payload,
                    HeldPromptVersion,
                    rationaleId,
                    question.Id,
                    ttl: AiOperationReplayPolicy.DefaultReplayWindow,
                    CancellationToken.None);
            }

            return generated with { Cached = false };
        }

        if (resultCacheKey is not null)
        {
            await resultCache!.StoreAsync(
                resultCacheKey,
                AiFeatureCodes.ReadingExplanation,
                Module,
                payload,
                PromptTemplateId,
                rationaleId,
                question.Id,
                ttl: TimeSpan.FromDays(30),
                CancellationToken.None);
        }

        if (cacheKey is not null)
        {
            await explanationCache!.StoreAsync(
                Module, question.Id, language, cacheKey, payload, CancellationToken.None);
        }

        return generated with { Cached = false };
    }

    /// <summary>
    /// True only for a real Jev judgment (<c>review_required</c>) that wants a human look. A null,
    /// <c>ok</c> or <c>unavailable</c> advisory (flag off, outage, bad config) never blocks caching.
    /// Logs the question id and the label/score fields only, never the explanation text.
    /// </summary>
    private bool JevHoldsBackFromCache(JevResponseAdvisory? advisory, string questionId)
    {
        if (advisory is not { Status: "review_required", RequiresHumanReview: true }) return false;
        logger?.LogWarning(
            "ReadingExplanationService — Jev advisory held question '{QuestionId}' out of the shared explanation caches (relation={Relation}, evidenceConfidence={EvidenceConfidence}, taskRelevance={TaskRelevance}, safetyConcern={SafetyConcern}); served to this learner only, kept for their re-view inside the AI replay window.",
            questionId, advisory.EvidenceRelation, advisory.EvidenceConfidence, advisory.TaskRelevanceProbability, advisory.SafetyConcernProbability);
        return true;
    }

    private static bool TryDeserializeExplanation(string json, string language, out ExplanationDto? dto)
    {
        dto = null;
        try
        {
            var cached = System.Text.Json.JsonSerializer.Deserialize<ExplanationDto>(json);
            if (cached is null) return false;
            dto = cached with { Language = language };
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private async Task<ExplanationDto?> TryReadCacheAsync(string cacheKey, string language, CancellationToken ct)
    {
        var cachedJson = await explanationCache!.TryGetAsync(cacheKey, ct);
        if (cachedJson is null) return null;
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<ExplanationDto>(cachedJson);
            // Re-stamp Language: the cache key already includes language, so
            // this is always the same value, but never trust a deserialized
            // value over the caller's own request for the field that decides
            // which language the client renders.
            return dto is null ? null : dto with { Language = language };
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger?.LogWarning(ex, "ReadingExplanationService — cached explanation for key {CacheKey} failed to deserialize; generating fresh.", cacheKey);
            return null;
        }
    }

    private async Task<(ExplanationDto Explanation, JevResponseAdvisory? Advisory)> CallGatewayAsync(
        ReadingQuestion question,
        string correctAnswer,
        string wrongOption,
        string language,
        string approvedRationale,
        string sourceSentence,
        string? sourcePassage,
        string? userId,
        CancellationToken ct)
    {
        try
        {
            // Resolve the approved rulebook before building the grounded
            // prompt. A synthetic fallback would make an AI explanation look
            // grounded even when the owner-approved rulebook is unavailable.
            _ = rulebookLoader.Load(RuleKind.Reading, ExamProfession.Medicine);
        }
        catch (RulebookNotFoundException)
        {
            throw new ReadingGroundedExplanationUnavailableException(
                "The Reading rulebook is unavailable; grounded explanation is blocked.");
        }

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Reading,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateReadingExplanation,
        });

        var userMessage = BuildExplanationPrompt(
            question,
            correctAnswer,
            wrongOption,
            language,
            approvedRationale,
            sourceSentence,
            sourcePassage);

        try
        {
            var result = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = string.Empty,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.ReadingExplanation,
                PromptTemplateId = PromptTemplateId,
                UserId = userId,
            }, ct);

            var explanation = TryParseExplanation(result.Completion, language)
                ?? throw new ReadingGroundedExplanationUnavailableException(
                    "The grounded gateway returned no usable explanation for this question.");
            return (explanation, result.JevAdvisory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "ReadingExplanationService — AI call failed for question '{QuestionId}'; explanation unavailable.",
                question.Id);
            throw new ReadingGroundedExplanationUnavailableException(
                "The grounded explanation is unavailable because the gateway failed.");
        }
    }

    public async Task<ExplanationDto> GetSubmittedAttemptExplanationAsync(
        string userId,
        string attemptId,
        string questionId,
        string language,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId must not be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(attemptId))
            throw new ArgumentException("attemptId must not be empty.", nameof(attemptId));
        if (string.IsNullOrWhiteSpace(questionId))
            throw new ArgumentException("questionId must not be empty.", nameof(questionId));

        var attempt = await db.ReadingAttempts.AsNoTracking()
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Reading attempt not found.");
        if (attempt.Status != ReadingAttemptStatus.Submitted)
            throw new InvalidOperationException("Grounded explanations are available only after submission.");

        var currentPaperRevision = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Id == attempt.PaperId && p.SubtestCode == "reading")
            .Select(p => p.PublishedRevisionId)
            .SingleOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(attempt.PaperRevisionId)
            || string.IsNullOrWhiteSpace(currentPaperRevision)
            || !string.Equals(attempt.PaperRevisionId, currentPaperRevision, StringComparison.Ordinal))
        {
            throw new ReadingGroundedExplanationUnavailableException(
                "The submitted attempt is not pinned to the current published Reading revision.");
        }

        var answer = attempt.Answers.FirstOrDefault(a => a.ReadingQuestionId == questionId)
            ?? throw new KeyNotFoundException("The question has no stored answer on this attempt.");
        var question = await db.ReadingQuestions.AsNoTracking()
            .Where(q => q.Id == questionId)
            .Where(q => db.ReadingParts.Any(p => p.Id == q.ReadingPartId && p.PaperId == attempt.PaperId))
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Reading question not found for this attempt.");

        var approvedRationale = await db.AssessmentRationales.AsNoTracking()
            .Where(r => r.Assessment == "reading"
                && r.QuestionRevisionId == question.Id
                && r.Status == AssessmentGovernanceStatus.Effective)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (approvedRationale is null
            || string.IsNullOrWhiteSpace(approvedRationale.RationaleText)
            || string.IsNullOrWhiteSpace(approvedRationale.SourceSentence))
        {
            throw new ReadingGroundedExplanationUnavailableException(
                "No effective author-approved rationale is available for this question.");
        }

        var sourcePassage = question.ReadingTextId is null
            ? null
            : await db.ReadingTexts.AsNoTracking()
                .Where(t => t.Id == question.ReadingTextId)
                .Select(t => t.BodyHtml)
                .SingleOrDefaultAsync(ct);
        var selectedAnswer = ResolveStoredAnswer(answer.UserAnswerJson);
        if (string.IsNullOrWhiteSpace(selectedAnswer) || selectedAnswer == "(unanswered)")
            throw new ReadingGroundedExplanationUnavailableException(
                "A grounded explanation requires a non-empty stored response.");
        var lang = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

        return await GenerateExplanationAsync(
            question,
            attemptId,
            approvedRationale.Id,
            ResolveCorrectAnswer(question),
            selectedAnswer,
            lang,
            approvedRationale.RationaleText,
            approvedRationale.SourceSentence,
            sourcePassage,
            userId,
            ct);
    }

    // ── Prompt builder ──────────────────────────────────────────────────────

    private static string BuildExplanationPrompt(
        ReadingQuestion question,
        string correctAnswer,
        string wrongOption,
        string language,
        string approvedRationale,
        string sourceSentence,
        string? sourcePassage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("For an OET Reading question:");
        sb.AppendLine();
        sb.AppendLine($"Question: {question.Stem}");
        sb.AppendLine($"Correct answer: {correctAnswer}");
        sb.AppendLine($"Student selected: {wrongOption}");
        sb.AppendLine($"Author-approved rationale: {TruncatePromptEvidence(approvedRationale)}");
        sb.AppendLine($"Author-approved source sentence: {TruncatePromptEvidence(sourceSentence)}");
        if (!string.IsNullOrWhiteSpace(sourcePassage))
            sb.AppendLine($"Stored passage evidence: {TruncatePromptEvidence(sourcePassage)}");

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
        sb.AppendLine($"  \"trapName\": \"one of: {TrapNameVocabulary}\",");
        sb.AppendLine("  \"avoidTip\": \"one actionable tip (≤ 25 words) to avoid this trap in future\"");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string TruncatePromptEvidence(string value)
        => value.Length <= 8_000 ? value : value[..8_000];

    // ── JSON parsing ────────────────────────────────────────────────────────

    private static ExplanationDto? TryParseExplanation(string completion, string language)
    {
        if (string.IsNullOrWhiteSpace(completion)) return null;
        var json = AiReplyParsing.ExtractFencedJsonObject(completion);
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

    private static string ResolveStoredAnswer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "(unanswered)";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString() ?? "(unanswered)",
                JsonValueKind.Array => string.Join(", ", doc.RootElement.EnumerateArray()
                    .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())),
                _ => doc.RootElement.ToString(),
            };
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string? SafeString(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
