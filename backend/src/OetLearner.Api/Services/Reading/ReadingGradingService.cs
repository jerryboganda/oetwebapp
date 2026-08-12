using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Grading Service — Slice R5
//
// Single grading pipeline:
//   1. Load attempt + answers + questions
//   2. Grade each answer via type-specific strategy
//   3. Sum PointsEarned → RawScore
//   4. Scaled = owner-approved versioned lookup row, when configured
//   5. Persist on the attempt
//
// MISSION CRITICAL: raw→scaled conversion happens ONLY through the complete
// owner-approved lookup table. No formula, interpolation, or fallback exists.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingGradingService
{
    Task<ReadingGradingResult> GradeAttemptAsync(string attemptId, CancellationToken ct);

    /// <summary>
    /// Wave 2 — re-grade an already-Submitted attempt in place from its
    /// stored answers (used by accepted-answer recalculation after an admin
    /// edits a question's correct answer / accepted synonyms). Re-derives
    /// raw + scaled via the owner-approved conversion table, persists, and bumps
    /// RowVersion WITHOUT changing <see cref="ReadingAttempt.SubmittedAt"/>.
    /// Returns null when the attempt is missing or not Submitted.
    /// </summary>
    Task<ReadingGradingResult?> RegradeSubmittedAsync(string attemptId, CancellationToken ct);

    /// <summary>Execute an approved single-question key correction without
    /// mutating the published question row.</summary>
    Task<ReadingGradingResult?> RegradeSubmittedAsync(
        string attemptId,
        string questionRevisionId,
        string newKeySnapshotJson,
        CancellationToken ct);
}

public sealed record ReadingGradingResult(
    int RawScore,
    int MaxRawScore,
    int? ScaledScore,
    string GradeLetter,
    int CorrectCount,
    int IncorrectCount,
    int UnansweredCount,
    IReadOnlyList<ReadingAnswerResult> Answers,
    string? ScoreConversionTableVersionKey = null,
    string? ScoreConversionErrorCode = null,
    string? ScoreConversionGrade = null,
    bool? ScoreConversionPassed = null,
    int InvalidCount = 0);

public sealed record ReadingAnswerResult(
    string QuestionId,
    string QuestionType,
    bool IsCorrect,
    int PointsEarned,
    int MaxPoints,
    string? MissReason = null,
    bool IsInvalid = false);

public sealed class ReadingGradingService(
    LearnerDbContext db,
    IReadingPolicyService policyService,
    ILogger<ReadingGradingService> logger,
    IAssessmentScoreConversionService? scoreConversion = null) : IReadingGradingService
{
    public const string MultipleSelectionReviewReason = "multiple_selection_review_required";
    public const string QuestionIntegrityReviewReason = "question_integrity_review_required";

    public static bool IsIntegrityReviewReason(string? reason)
        => reason is MultipleSelectionReviewReason or QuestionIntegrityReviewReason;

    private readonly IAssessmentScoreConversionService _scoreConversion =
        scoreConversion ?? new AssessmentScoreConversionService(db);
    public async Task<ReadingGradingResult> GradeAttemptAsync(string attemptId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException("Attempt not found.");

        // Idempotent: if already graded, return existing result.
        if (attempt.Status == ReadingAttemptStatus.Submitted && attempt.RawScore is int existingRaw)
        {
            var shouldRegradeSubmittedSubset = IsSubsetPracticeMode(attempt.Mode)
                && (attempt.ScaledScore is not null
                    || attempt.MaxRawScore == ReadingStructureService.CanonicalMaxRawScore);
            if (!shouldRegradeSubmittedSubset)
            {
                return await BuildResultFromExistingAsync(attempt, existingRaw, ct);
            }
        }

        return await GradeAndPersistAsync(attempt, "ReadingAttemptGraded", ct, keyCorrection: null);
    }

    /// <summary>
    /// Wave 2 — force a re-grade of an already-Submitted attempt in place.
    /// Bypasses the idempotent short-circuit in <see cref="GradeAttemptAsync"/>
    /// so an accepted-answer edit can be propagated to historical attempts.
    /// Writes no audit row of its own — the caller (ReadingTutorService)
    /// records the recalculation audit so the action is logged once with
    /// full context.
    /// </summary>
    public async Task<ReadingGradingResult?> RegradeSubmittedAsync(string attemptId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Status != ReadingAttemptStatus.Submitted)
            return null;
        return await GradeAndPersistAsync(attempt, auditAction: null, ct, keyCorrection: null);
    }

    public async Task<ReadingGradingResult?> RegradeSubmittedAsync(
        string attemptId,
        string questionRevisionId,
        string newKeySnapshotJson,
        CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null || attempt.Status != ReadingAttemptStatus.Submitted)
            return null;
        if (string.IsNullOrWhiteSpace(questionRevisionId))
            throw new InvalidOperationException("remark_question_revision_required");
        return await GradeAndPersistAsync(
            attempt,
            auditAction: null,
            ct,
            new KeyCorrection(questionRevisionId.Trim(), newKeySnapshotJson));
    }

    private async Task<ReadingGradingResult> GradeAndPersistAsync(
        ReadingAttempt attempt,
        string? auditAction,
        CancellationToken ct,
        KeyCorrection? keyCorrection)
    {
        var currentPaperRevision = await db.ContentPapers.AsNoTracking()
            .Where(p => p.Id == attempt.PaperId)
            .Select(p => p.PublishedRevisionId)
            .SingleOrDefaultAsync(ct);
        if (keyCorrection is null
            && attempt.PaperRevisionId is not null
            && !string.Equals(attempt.PaperRevisionId, currentPaperRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The published Reading question revision changed after this attempt started. The attempt is held for controlled re-marking.");
        }

        // Load all questions for the paper (single round-trip)
        var partIds = await db.ReadingParts
            .Where(p => p.PaperId == attempt.PaperId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var partCodeByPartId = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == attempt.PaperId)
            .ToDictionaryAsync(p => p.Id, p => p.PartCode, ct);
        var questions = await db.ReadingQuestions
            .Where(q => partIds.Contains(q.ReadingPartId))
            .ToListAsync(ct);

        var questionById = questions.ToDictionary(q => q.Id);
        if (keyCorrection is not null && !questionById.ContainsKey(keyCorrection.QuestionRevisionId))
            throw new InvalidOperationException("remark_question_revision_not_found");
        var answersByQuestionId = attempt.Answers.ToDictionary(a => a.ReadingQuestionId);

        // Phase 3b: subset modes (Drill / MiniTest / ErrorBank) only score
        // their in-scope questions; full-paper modes (Exam / Learning) keep
        // the canonical 42-question denominator and OET 0-500 conversion.
        var isSubsetPracticeMode = IsSubsetPracticeMode(attempt.Mode);
        var scopeQuestionIds = ParseScopeQuestionIds(attempt);
        var gradedQuestions = isSubsetPracticeMode
            ? questions.Where(q => scopeQuestionIds?.Contains(q.Id) == true).ToList()
            : questions;

        var policy = await ResolvePolicyForAttemptAsync(attempt, ct);
        var details = new List<ReadingAnswerResult>(gradedQuestions.Count);
        var multipleSelectionIssues = new List<MultipleSelectionIntegrityIssue>();
        var questionIntegrityIssues = new List<QuestionIntegrityIssue>();

        int raw = 0, correctCount = 0, incorrectCount = 0, invalidCount = 0, unanswered = 0;
        long maxRawTotal = 0;
        foreach (var q in gradedQuestions)
        {
            maxRawTotal += q.Points;
            ReadingAnswer? answer = answersByQuestionId.GetValueOrDefault(q.Id);
            var gradingQuestion = keyCorrection?.QuestionRevisionId == q.Id
                ? ApplyKeyCorrection(q, keyCorrection.NewKeySnapshotJson)
                : q;
            var questionIntegrityReason = GetQuestionIntegrityReason(gradingQuestion);
            if (questionIntegrityReason is not null)
            {
                if (answer is not null)
                {
                    answer.IsCorrect = null;
                    answer.PointsEarned = 0;
                    answer.SelectedDistractorCategory = null;
                    answer.MissReason = QuestionIntegrityReviewReason;
                }

                questionIntegrityIssues.Add(new(
                    q.Id,
                    q.DisplayOrder,
                    questionIntegrityReason));
                invalidCount++;
                details.Add(new(
                    q.Id,
                    q.QuestionType.ToString(),
                    false,
                    0,
                    q.Points,
                    QuestionIntegrityReviewReason,
                    IsInvalid: true));
                continue;
            }

            if (answer is null)
            {
                unanswered++;
                details.Add(new(q.Id, q.QuestionType.ToString(), false, 0, q.Points));
                continue;
            }

            var partCode = partCodeByPartId.GetValueOrDefault(q.ReadingPartId, ReadingPartCode.A);
            if (IsMultipleChoice(gradingQuestion.QuestionType)
                && TryReadMultipleSelections(answer.UserAnswerJson, out var selections))
            {
                multipleSelectionIssues.Add(new MultipleSelectionIntegrityIssue(
                    q.Id,
                    q.DisplayOrder,
                    partCode.ToString(),
                    selections));

                // A single-answer MCQ with multiple persisted selections is
                // invalid for automated scoring. Preserve the raw payload for
                // the administrator, but do not persist a normal incorrect
                // mark or a distractor/miss classification for the item.
                answer.IsCorrect = null;
                answer.PointsEarned = 0;
                answer.SelectedDistractorCategory = null;
                answer.MissReason = MultipleSelectionReviewReason;
                invalidCount++;
                details.Add(new(
                    q.Id,
                    q.QuestionType.ToString(),
                    false,
                    0,
                    q.Points,
                    answer.MissReason,
                    IsInvalid: true));
                continue;
            }
            var (isCorrect, pts) = GradeOne(gradingQuestion, answer, policy, attempt.Mode, partCode);
            answer.IsCorrect = isCorrect;
            answer.PointsEarned = pts;
            answer.SelectedDistractorCategory = isCorrect
                ? null
                : ResolveSelectedDistractor(gradingQuestion, answer);
            answer.MissReason = ClassifyMiss(gradingQuestion, answer, policy, partCode, isCorrect);
            var acceptedVariant = isCorrect
                ? FindAcceptedVariant(gradingQuestion, answer, policy)
                : null;
            if (acceptedVariant is not null)
            {
                db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OccurredAt = DateTimeOffset.UtcNow,
                    ActorId = attempt.UserId,
                    ActorName = "ReadingGradingService",
                    Action = "reading.marking.accepted_variant_used",
                    ResourceType = "ReadingAttemptAnswer",
                    ResourceId = $"{attempt.Id}:{q.Id}",
                    Details = JsonSerializer.Serialize(new
                    {
                        attemptId = attempt.Id,
                        questionId = q.Id,
                        questionNumber = q.DisplayOrder,
                        acceptedVariant,
                        policyNormalisation = policy.ShortAnswerNormalisation,
                        caseSensitive = EffectiveCaseSensitive(q, policy),
                        policyVersionId = attempt.MarkingPolicyVersionId,
                    }),
                });
            }
            raw += pts;
            if (isCorrect) correctCount++; else incorrectCount++;
            details.Add(new(q.Id, q.QuestionType.ToString(), isCorrect, pts, q.Points, answer.MissReason));
        }

        var maxRaw = maxRawTotal > int.MaxValue
            ? int.MaxValue
            : maxRawTotal < int.MinValue
                ? int.MinValue
                : (int)maxRawTotal;
        attempt.RawScore = raw;
        attempt.MaxRawScore = maxRaw;

        var requiresAdminReview = multipleSelectionIssues.Count > 0
            || questionIntegrityIssues.Count > 0;
        if (requiresAdminReview)
        {
            attempt.RequiresAdminReview = true;
            attempt.AdminReviewReason ??= multipleSelectionIssues.Count > 0
                ? "multiple_selections_for_single_answer_mcq"
                : QuestionIntegrityReviewReason;
            attempt.AdminReviewFlaggedAt ??= DateTimeOffset.UtcNow;
        }

        var conversion = isSubsetPracticeMode
            ? AssessmentScoreConversionResult.Unavailable(
                "reading",
                "default",
                raw,
                "subset_practice_no_conversion")
            : requiresAdminReview
                ? AssessmentScoreConversionResult.Unavailable(
                    "reading",
                    "default",
                    raw,
                    multipleSelectionIssues.Count > 0
                        ? MultipleSelectionReviewReason
                        : QuestionIntegrityReviewReason)
            : await AssessmentScoreConversionSnapshotResolver.ResolveAsync(
                _scoreConversion,
                "reading",
                raw,
                attempt.ScoreConversionSnapshotJson,
                attempt.ScoreConversionTableId,
                "default",
                ct);
        var hasApprovedConversion = !requiresAdminReview
            && !isSubsetPracticeMode
            && attempt.MaxRawScore == ReadingStructureService.CanonicalMaxRawScore
            && conversion.ConvertedScore.HasValue
            && !string.IsNullOrWhiteSpace(conversion.TableVersionKey)
            && conversion.Passed.HasValue;
        if (hasApprovedConversion && conversion.TableId is not null && conversion.IsAvailable)
        {
            await _scoreConversion.MarkUsedAsync(conversion.TableId, ct);
        }
        attempt.ScoreConversionTableId = hasApprovedConversion ? conversion.TableId : null;
        attempt.ScoreConversionTableVersionKey = hasApprovedConversion ? conversion.TableVersionKey : null;
        attempt.ScoreConversionGrade = hasApprovedConversion ? conversion.Grade : null;
        attempt.ScoreConversionPassed = hasApprovedConversion ? conversion.Passed : null;
        attempt.ScaledScore = hasApprovedConversion ? conversion.ConvertedScore : null;
        attempt.Status = ReadingAttemptStatus.Submitted;
        attempt.SubmittedAt ??= DateTimeOffset.UtcNow;
        attempt.LastActivityAt = DateTimeOffset.UtcNow;

        if (multipleSelectionIssues.Count > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = attempt.UserId,
                ActorName = "ReadingGradingService",
                Action = "reading.mcq.multiple_selection_review_required",
                ResourceType = "ReadingAttempt",
                ResourceId = attempt.Id,
                Details = JsonSerializer.Serialize(new
                {
                    requiresAdminReview = true,
                    reason = "multiple_selections_for_single_answer_mcq",
                    attemptId = attempt.Id,
                    issues = multipleSelectionIssues,
                }),
            });
        }

        if (questionIntegrityIssues.Count > 0)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString(),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = attempt.UserId,
                ActorName = "ReadingGradingService",
                Action = "reading.question.integrity_review_required",
                ResourceType = "ReadingAttempt",
                ResourceId = attempt.Id,
                Details = JsonSerializer.Serialize(new
                {
                    requiresAdminReview = true,
                    reason = QuestionIntegrityReviewReason,
                    attemptId = attempt.Id,
                    issues = questionIntegrityIssues,
                }),
            });
        }

        // P0-F 2026-05 hardening: bump the optimistic-concurrency token so
        // that a concurrent grader running against the same row hits a
        // DbUpdateConcurrencyException on save instead of silently
        // overwriting our final score.
        attempt.RowVersion++;

        await UpdateErrorBankAsync(attempt, questionById, ct);

        if (auditAction is not null)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTimeOffset.UtcNow,
                ActorId = attempt.UserId,
                ActorName = attempt.UserId,
                Action = auditAction,
                ResourceType = "ReadingAttempt",
                ResourceId = attempt.Id,
                Details = $"raw={raw}/{attempt.MaxRawScore} scaled={attempt.ScaledScore?.ToString() ?? "n/a"} mode={attempt.Mode}",
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another submit raced us to graded state. Reload the canonical
            // result the winner persisted instead of overwriting it.
            logger.LogInformation(
                "Reading attempt {AttemptId} grade lost concurrency race; returning winner's stored result.",
                attempt.Id);
            db.ChangeTracker.Clear();
            var winner = await db.ReadingAttempts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == attempt.Id, ct)
                ?? throw new InvalidOperationException("Attempt vanished after concurrency conflict.");
            if (winner.RawScore is int winnerRaw)
            {
                return await BuildResultFromExistingAsync(winner, winnerRaw, ct);
            }
            // Winner not yet committed - rare. Fall through with our values.
        }

        return new ReadingGradingResult(
            RawScore: raw,
            MaxRawScore: attempt.MaxRawScore,
            ScaledScore: attempt.ScaledScore,
            GradeLetter: hasApprovedConversion ? conversion.Grade ?? "—" : "—",
            CorrectCount: correctCount,
            IncorrectCount: incorrectCount,
            UnansweredCount: unanswered,
            Answers: details,
            ScoreConversionTableVersionKey: hasApprovedConversion ? conversion.TableVersionKey : null,
            ScoreConversionErrorCode: hasApprovedConversion
                ? conversion.ErrorCode
                : requiresAdminReview
                    ? multipleSelectionIssues.Count > 0
                        ? MultipleSelectionReviewReason
                        : QuestionIntegrityReviewReason
                    : "score_conversion_unavailable",
            ScoreConversionGrade: hasApprovedConversion ? conversion.Grade : null,
            ScoreConversionPassed: hasApprovedConversion ? conversion.Passed : null,
            InvalidCount: invalidCount);
    }

    private static HashSet<string>? ParseScopeQuestionIds(ReadingAttempt attempt)
    {
        if (!IsSubsetPracticeMode(attempt.Mode))
            return null;
        if (string.IsNullOrWhiteSpace(attempt.ScopeJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(attempt.ScopeJson);
            if (!doc.RootElement.TryGetProperty("questionIds", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s)
                    ids.Add(s);
            }
            return ids.Count > 0 ? ids : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsSubsetPracticeMode(ReadingAttemptMode mode)
        => mode is ReadingAttemptMode.Drill or ReadingAttemptMode.MiniTest or ReadingAttemptMode.ErrorBank;

    private static string? GetQuestionIntegrityReason(ReadingQuestion question)
    {
        if (question.Points != 1)
            return "question_points_not_one";

        try
        {
            using var correctDocument = JsonDocument.Parse(question.CorrectAnswerJson ?? string.Empty);
            var correct = correctDocument.RootElement;
            switch (question.QuestionType)
            {
                case ReadingQuestionType.MultipleChoice3:
                case ReadingQuestionType.MultipleChoice4:
                case ReadingQuestionType.MultipleChoiceFlexible:
                    using (var optionsDocument = JsonDocument.Parse(question.OptionsJson ?? string.Empty))
                    {
                        if (optionsDocument.RootElement.ValueKind != JsonValueKind.Array)
                            return "question_options_invalid";

                        var optionCount = optionsDocument.RootElement.GetArrayLength();
                        var validCount = question.QuestionType switch
                        {
                            ReadingQuestionType.MultipleChoice3 => optionCount == 3,
                            ReadingQuestionType.MultipleChoice4 => optionCount == 4,
                            _ => optionCount is >= 2 and <= 6,
                        };
                        if (!validCount)
                            return "question_option_count_invalid";

                        if (correct.ValueKind != JsonValueKind.String
                            || !IsValidMcqLetter(correct.GetString(), optionCount))
                            return "question_correct_answer_invalid";

                        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var option in optionsDocument.RootElement.EnumerateArray())
                        {
                            var label = ReadOptionLabel(option);
                            if (string.IsNullOrWhiteSpace(label) || !labels.Add(label))
                                return "question_options_invalid";
                        }
                    }
                    return null;

                case ReadingQuestionType.MatchingTextReference:
                    return correct.ValueKind == JsonValueKind.String
                        && correct.GetString() is ("A" or "B" or "C" or "D")
                        ? null
                        : "question_correct_answer_invalid";

                case ReadingQuestionType.ShortAnswer:
                case ReadingQuestionType.SentenceCompletion:
                case ReadingQuestionType.FillInBlank:
                    if (correct.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(correct.GetString()))
                        return "question_correct_answer_invalid";
                    return HasValidAcceptedSynonyms(question.AcceptedSynonymsJson, allowObject: false)
                        ? null
                        : "question_accepted_variants_invalid";

                case ReadingQuestionType.ShortAnswerLabeled:
                {
                    if (correct.ValueKind != JsonValueKind.Object)
                        return "question_correct_answer_invalid";
                    var labeledAnswers = correct.EnumerateObject().ToList();
                    if (labeledAnswers.Count == 0
                        || labeledAnswers.Any(prop =>
                            string.IsNullOrWhiteSpace(prop.Name)
                            || prop.Value.ValueKind != JsonValueKind.String
                            || string.IsNullOrWhiteSpace(prop.Value.GetString())))
                        return "question_correct_answer_invalid";
                    return HasValidAcceptedSynonyms(question.AcceptedSynonymsJson, allowObject: true)
                        ? null
                        : "question_accepted_variants_invalid";
                }

                default:
                    return "question_type_unknown";
            }
        }
        catch (JsonException)
        {
            return "question_answer_key_invalid";
        }
    }

    private static bool IsValidMcqLetter(string? value, int optionCount)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != 1)
            return false;
        var index = char.ToUpperInvariant(value.Trim()[0]) - 'A';
        return index >= 0 && index < optionCount;
    }

    private static string? ReadOptionLabel(JsonElement option)
    {
        if (option.ValueKind == JsonValueKind.String)
            return option.GetString()?.Trim();
        if (option.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in option.EnumerateObject())
        {
            if (property.Name is not ("id" or "value" or "label" or "text" or "title" or "letter" or "key")
                || property.Value.ValueKind != JsonValueKind.String)
                return null;
        }

        foreach (var property in option.EnumerateObject())
        {
            if (property.Name is not ("label" or "text" or "title" or "value" or "letter" or "key")
                || property.Value.ValueKind != JsonValueKind.String)
                continue;
            var value = property.Value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static bool HasValidAcceptedSynonyms(string? json, bool allowObject)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement.EnumerateArray().All(item =>
                    item.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(item.GetString()));
            }

            if (!allowObject || document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            return document.RootElement.EnumerateObject().All(property =>
                !string.IsNullOrWhiteSpace(property.Name)
                && property.Value.ValueKind == JsonValueKind.Array
                && property.Value.EnumerateArray().All(item =>
                    item.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(item.GetString())));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsMultipleChoice(ReadingQuestionType type)
        => type is ReadingQuestionType.MultipleChoice3
            or ReadingQuestionType.MultipleChoice4
            or ReadingQuestionType.MultipleChoiceFlexible;

    private static bool TryReadMultipleSelections(
        string? userAnswerJson,
        out IReadOnlyList<string> selections)
    {
        selections = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(userAnswerJson)) return false;

        try
        {
            using var document = JsonDocument.Parse(userAnswerJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;

            var values = document.RootElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.GetRawText())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray();
            selections = values;
            return values.Length > 1;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ── Grader strategies ────────────────────────────────────────────────

    private (bool isCorrect, int points) GradeOne(
        ReadingQuestion q,
        ReadingAnswer a,
        ReadingResolvedPolicy policy,
        ReadingAttemptMode mode,
        ReadingPartCode partCode)
    {
        try
        {
            var strictPartA = partCode == ReadingPartCode.A;
            return q.QuestionType switch
            {
                ReadingQuestionType.MultipleChoice3 or
                ReadingQuestionType.MultipleChoice4 or
                ReadingQuestionType.MultipleChoiceFlexible => GradeMcq(q, a),

                ReadingQuestionType.MatchingTextReference => strictPartA
                    ? GradeStrictSingleLetter(q, a)
                    : GradeMatching(q, a, policy),

                ReadingQuestionType.ShortAnswer or
                ReadingQuestionType.SentenceCompletion => strictPartA
                    ? GradeStrictTextAnswer(q, a, policy)
                    : GradeShortAnswer(q, a, policy),

                ReadingQuestionType.FillInBlank => GradeShortAnswer(q, a, policy),

                ReadingQuestionType.ShortAnswerLabeled => GradeLabeledShortAnswer(q, a, policy),

                _ => ApplyUnknownFallback(q, a, policy),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Grading exception for question {QuestionId} type {Type} — applying fallback policy.",
                q.Id, q.QuestionType);
            return ApplyUnknownFallback(q, a, policy);
        }
    }

    private static (bool, int) GradeMcq(ReadingQuestion q, ReadingAnswer a)
    {
        var correct = JsonSerializer.Deserialize<string>(q.CorrectAnswerJson)?.Trim().ToUpperInvariant();
        string user;
        try { user = JsonSerializer.Deserialize<string>(a.UserAnswerJson)?.Trim().ToUpperInvariant() ?? ""; }
        catch (JsonException) { user = (a.UserAnswerJson ?? "").Trim().ToUpperInvariant(); }

        // Wave 1.1.1 dual-read: if user answer looks like an option ID, resolve to letter
        if (user.StartsWith("OPT-", StringComparison.OrdinalIgnoreCase))
            user = ResolveOptionIdToLetter(q.OptionsJson, user) ?? user;

        var ok = !string.IsNullOrEmpty(correct) && correct == user;
        return (ok, ok ? q.Points : 0);
    }

    private static (bool, int) GradeLabeledShortAnswer(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        if (!TryParseStringMap(q.CorrectAnswerJson, out var correctAnswers)
            || !TryParseStringMap(a.UserAnswerJson, out var userAnswers))
        {
            return GradeShortAnswer(q, a, policy);
        }

        if (correctAnswers.Count == 0 || userAnswers.Count != correctAnswers.Count)
            return (false, 0);

        ParseLabeledSynonyms(q.AcceptedSynonymsJson, out var globalSynonyms, out var labeledSynonyms);

        foreach (var (label, correctValue) in correctAnswers)
        {
            if (!userAnswers.TryGetValue(label, out var userValue))
                return (false, 0);

            var candidates = new List<string> { correctValue };
            if (labeledSynonyms.TryGetValue(label, out var labelSyns))
            {
                candidates.AddRange(labelSyns);
            }
            else if (globalSynonyms is not null)
            {
                candidates.AddRange(globalSynonyms);
            }

            var matched = candidates.Any(candidate =>
                StringsMatch(
                    ApplyTextNormalization(userValue, policy),
                    ApplyTextNormalization(candidate, policy),
                    EffectiveCaseSensitive(q, policy),
                    policy.ShortAnswerNormalisation));
            if (!matched)
                return (false, 0);
        }

        return (true, q.Points);
    }

    /// <summary>
    /// Resolves an option ID (e.g. "opt-abc123def456") to its letter (e.g. "A")
    /// by searching the enriched OptionsJson. Returns null if not found.
    /// </summary>
    private static string? ResolveOptionIdToLetter(string optionsJson, string optionId)
    {
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var opt in doc.RootElement.EnumerateArray())
            {
                if (opt.ValueKind != JsonValueKind.Object) continue;
                if (opt.TryGetProperty("id", out var idProp)
                    && string.Equals(idProp.GetString(), optionId, StringComparison.OrdinalIgnoreCase)
                    && opt.TryGetProperty("letter", out var letterProp))
                {
                    return letterProp.GetString()?.Trim().ToUpperInvariant();
                }
            }
        }
        catch (JsonException) { /* malformed options — cannot resolve */ }
        return null;
    }

    /// <summary>
    /// Phase 4 — when a learner picks a wrong MCQ option that the author
    /// tagged with a distractor category, cache it on the answer for fast
    /// analytics. Silent on parse errors / missing metadata: this is purely
    /// informational and must never block grading.
    /// </summary>
    private static ReadingDistractorCategory? ResolveSelectedDistractor(ReadingQuestion q, ReadingAnswer a)
    {
        if (q.QuestionType != ReadingQuestionType.MultipleChoice3
            && q.QuestionType != ReadingQuestionType.MultipleChoice4
            && q.QuestionType != ReadingQuestionType.MultipleChoiceFlexible)
            return null;
        if (string.IsNullOrWhiteSpace(q.OptionDistractorsJson)) return null;

        string user;
        try { user = JsonSerializer.Deserialize<string>(a.UserAnswerJson)?.Trim().ToUpperInvariant() ?? ""; }
        catch (JsonException) { user = (a.UserAnswerJson ?? "").Trim().ToUpperInvariant(); }
        if (user.Length == 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(q.OptionDistractorsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name.Trim(), user, StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String
                    && Enum.TryParse<ReadingDistractorCategory>(prop.Value.GetString(), ignoreCase: true, out var cat))
                {
                    return cat;
                }
            }
        }
        catch (JsonException) { /* malformed metadata — drop silently */ }
        return null;
    }

    private static (bool, int) GradeMatching(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        var correct = ParseStringSet(q.CorrectAnswerJson);
        var user = ParseStringSet(a.UserAnswerJson);
        if (correct.Count == 0) return (false, 0);

        // Listening/Reading v1.1 does not award partial credit for a Part A
        // matching response. The exact set must match the versioned key.
        var allRight = correct.SetEquals(user);
        return (allRight, allRight ? q.Points : 0);
    }

    private static (bool, int) GradeStrictSingleLetter(ReadingQuestion q, ReadingAnswer a)
    {
        var correct = ParseJsonString(q.CorrectAnswerJson)?.Trim();
        var user = ParseJsonString(a.UserAnswerJson)?.Trim();
        var ok = correct is "A" or "B" or "C" or "D"
            && string.Equals(user, correct, StringComparison.Ordinal);
        return (ok, ok ? q.Points : 0);
    }

    private static (bool, int) GradeStrictTextAnswer(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        var correct = ParseJsonString(q.CorrectAnswerJson);
        var user = ParseJsonString(a.UserAnswerJson);
        if (correct is null || user is null) return (false, 0);

        // STRICT spelling: apply only the captured named normalization profile,
        // then compare. NO punctuation/unit folding, Levenshtein, or inferred
        // synonyms are allowed. Explicitly authored variants are the only
        // additional accepted answers, and the owner policy controls whether
        // those variants are active for this attempt.
        var candidates = new List<string> { correct };
        if (policy.ShortAnswerAcceptSynonyms)
        {
            ParseLabeledSynonyms(q.AcceptedSynonymsJson, out var globalSynonyms, out _);
            if (globalSynonyms is not null)
                candidates.AddRange(globalSynonyms);
        }

        var nu = Normalise(ApplyTextNormalization(user, policy), policy.ShortAnswerNormalisation);
        var caseInsensitive = !EffectiveCaseSensitive(q, policy);
        var ok = candidates.Any(candidate =>
        {
            var nc = Normalise(ApplyTextNormalization(candidate, policy), policy.ShortAnswerNormalisation);
            return caseInsensitive
                ? string.Equals(nu, nc, StringComparison.OrdinalIgnoreCase)
                : string.Equals(nu, nc, StringComparison.Ordinal);
        });
        return (ok, ok ? q.Points : 0);
    }

    private static (bool, int) GradeShortAnswer(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        string correct;
        try { correct = JsonSerializer.Deserialize<string>(q.CorrectAnswerJson) ?? ""; }
        catch (JsonException) { correct = q.CorrectAnswerJson; }

        string user;
        try { user = JsonSerializer.Deserialize<string>(a.UserAnswerJson) ?? ""; }
        catch (JsonException) { user = a.UserAnswerJson ?? ""; }

        var candidates = new List<string> { correct };
        if (policy.ShortAnswerAcceptSynonyms && !string.IsNullOrWhiteSpace(q.AcceptedSynonymsJson))
        {
            try
            {
                var syns = JsonSerializer.Deserialize<string[]>(q.AcceptedSynonymsJson!);
                if (syns is not null) candidates.AddRange(syns);
            }
            catch (JsonException) { /* malformed — fall through with primary only */ }
        }

        foreach (var c in candidates)
        {
            if (StringsMatch(
                    ApplyTextNormalization(user, policy),
                    ApplyTextNormalization(c, policy),
                    EffectiveCaseSensitive(q, policy),
                    policy.ShortAnswerNormalisation))
                return (true, q.Points);
        }
        return (false, 0);
    }

    private static string? FindAcceptedVariant(
        ReadingQuestion q,
        ReadingAnswer a,
        ReadingResolvedPolicy policy)
    {
        if (!policy.ShortAnswerAcceptSynonyms
            || q.QuestionType is not (ReadingQuestionType.ShortAnswer
                or ReadingQuestionType.SentenceCompletion
                or ReadingQuestionType.FillInBlank
                or ReadingQuestionType.ShortAnswerLabeled))
        {
            return null;
        }

        if (q.QuestionType == ReadingQuestionType.ShortAnswerLabeled
            && TryParseStringMap(q.CorrectAnswerJson, out var correctMap)
            && TryParseStringMap(a.UserAnswerJson, out var userMap))
        {
            ParseLabeledSynonyms(q.AcceptedSynonymsJson, out var globalSynonyms, out var labeledSynonyms);
            foreach (var (label, correctValue) in correctMap)
            {
                if (!userMap.TryGetValue(label, out var userValue)) continue;
                var variants = labeledSynonyms.TryGetValue(label, out var labelVariants)
                    ? labelVariants
                    : globalSynonyms ?? Array.Empty<string>();
                foreach (var variant in variants)
                {
                    if (StringsMatch(
                            ApplyTextNormalization(userValue, policy),
                            ApplyTextNormalization(variant, policy),
                            EffectiveCaseSensitive(q, policy),
                            policy.ShortAnswerNormalisation)
                        && !StringsMatch(
                            ApplyTextNormalization(userValue, policy),
                            ApplyTextNormalization(correctValue, policy),
                            EffectiveCaseSensitive(q, policy),
                            policy.ShortAnswerNormalisation))
                    {
                        return variant.Trim();
                    }
                }
            }

            return null;
        }

        var correct = ParseJsonString(q.CorrectAnswerJson);
        var user = ParseJsonString(a.UserAnswerJson);
        if (correct is null || user is null) return null;

        ParseLabeledSynonyms(q.AcceptedSynonymsJson, out var synonyms, out _);
        foreach (var variant in synonyms ?? Array.Empty<string>())
        {
            if (StringsMatch(
                    ApplyTextNormalization(user, policy),
                    ApplyTextNormalization(variant, policy),
                    EffectiveCaseSensitive(q, policy),
                    policy.ShortAnswerNormalisation)
                && !StringsMatch(
                    ApplyTextNormalization(user, policy),
                    ApplyTextNormalization(correct, policy),
                    EffectiveCaseSensitive(q, policy),
                    policy.ShortAnswerNormalisation))
            {
                return variant.Trim();
            }
        }

        return null;
    }

    private static (bool, int) ApplyUnknownFallback(ReadingQuestion q, ReadingAnswer a, ReadingResolvedPolicy policy)
    {
        return policy.UnknownTypeFallbackPolicy switch
        {
            // Listening/Reading v1.1 is fail-closed: an unknown or corrupt
            // question type must never receive credit from a permissive legacy
            // policy value. Keep the legacy setting readable for snapshot
            // compatibility, but treat it as zero-credit at the grader.
            "grade_as_correct" => (false, 0),
            "fail_grading" => throw new InvalidOperationException(
                $"Grader refused for question {q.Id} type {q.QuestionType} per fallback policy."),
            _ /* skip_with_zero */ => (false, 0),
        };
    }

    private static bool StringsMatch(string a, string b, bool caseSensitive, string normalisation)
    {
        if (a is null || b is null) return false;
        var na = Normalise(a, normalisation);
        var nb = Normalise(b, normalisation);

        // Decision 4 — hard-lock strictness everywhere: NO fuzzy/Levenshtein
        // ACCEPTANCE in any mode/policy. A stale `fuzzy_levenshtein_1`
        // normalisation degrades to exact match. (Levenshtein survives only in
        // ClassifyMiss for analytics labelling, never for grading.)
        return caseSensitive
            ? string.Equals(na, nb, StringComparison.Ordinal)
            : string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string s, string strategy) => strategy switch
    {
        "exact" => s,
        "trim_only" => s.Trim(),
        "trim_collapse" => CollapseWhitespace(s.Trim()),
        "trim_collapse_case_insensitive" => CollapseWhitespace(s.Trim()),
        // Legacy fuzzy and unknown policy names fail closed to exact matching;
        // neither stale configuration nor malformed snapshots may grant
        // additional normalization or fuzzy acceptance.
        _ => s,
    };

    private static bool EffectiveCaseSensitive(
        ReadingQuestion question,
        ReadingResolvedPolicy policy)
        => question.CaseSensitive
            && !string.Equals(
                policy.ShortAnswerNormalisation,
                "trim_collapse_case_insensitive",
                StringComparison.OrdinalIgnoreCase);

    // R04.2: true when the two normalised answers differ ONLY by a
    // singular/plural inflection (trailing s/es, or y -> ies). Analytics
    // labelling only — such an answer is still graded WRONG.
    private static bool IsNumberFormDifference(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var x = a.ToUpperInvariant();
        var y = b.ToUpperInvariant();
        if (string.Equals(x, y, StringComparison.Ordinal)) return false;
        foreach (var (s, p) in new[] { (x, y), (y, x) })
        {
            if (p == s + "S" || p == s + "ES") return true;
            if (s.Length > 1 && s.EndsWith("Y", StringComparison.Ordinal) && p == s[..^1] + "IES") return true;
        }
        return false;
    }

    private static bool LevenshteinDistanceAtMostOne(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (Math.Abs(a.Length - b.Length) > 1) return false;

        var edits = 0;
        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                i++;
                j++;
                continue;
            }

            edits++;
            if (edits > 1) return false;

            if (a.Length == b.Length)
            {
                i++;
                j++;
            }
            else if (a.Length > b.Length)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        if (i < a.Length || j < b.Length) edits++;
        return edits <= 1;
    }

    private static string CollapseWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        var prevSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else { sb.Append(ch); prevSpace = false; }
        }
        return sb.ToString();
    }

    // ── Wave 1 — text normalisation shared by Part A and B/C grading ─────

    /// <summary>
    /// Legacy text-normalisation hook. v1.1 strict marking deliberately leaves
    /// punctuation, hyphenation, and number/unit forms untouched; allowed
    /// behavior is controlled only by <see cref="ReadingResolvedPolicy.ShortAnswerNormalisation"/>
    /// and explicit authored variants.
    /// </summary>
    private static string ApplyTextNormalization(string s, ReadingResolvedPolicy policy)
    {
        _ = policy;
        return s ?? string.Empty;
    }

    /// <summary>
    /// Wave 1 — classify why a graded answer was missed. Returns null for a
    /// correct answer. Priority order mirrors the documented decision list:
    /// blank → spelling → distractor → wrong_text → incomplete → wrong.
    /// </summary>
    private static string? ClassifyMiss(
        ReadingQuestion q,
        ReadingAnswer a,
        ReadingResolvedPolicy policy,
        ReadingPartCode partCode,
        bool isCorrect)
    {
        if (isCorrect) return null;

        var userRaw = ParseJsonString(a.UserAnswerJson) ?? a.UserAnswerJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userRaw)) return "blank";

        var isPartATyped = partCode == ReadingPartCode.A
            && q.QuestionType is ReadingQuestionType.ShortAnswer
                or ReadingQuestionType.SentenceCompletion;

        string? nu = null, nc = null;
        if (isPartATyped)
        {
            var correctRaw = ParseJsonString(q.CorrectAnswerJson) ?? string.Empty;
            nu = CollapseWhitespace(ApplyTextNormalization(userRaw.Trim(), policy));
            nc = CollapseWhitespace(ApplyTextNormalization(correctRaw.Trim(), policy));

            // R04.2 singular/plural — still WRONG, but labelled distinctly for
            // analytics. Checked before "spelling" so a plural inflection is not
            // mislabelled as a one-edit typo.
            if (!string.IsNullOrEmpty(nc) && IsNumberFormDifference(nu, nc))
            {
                return "number_form";
            }

            // "spelling": would have been right but for case/spelling — a
            // case-insensitive normalised match or a single-edit typo.
            if (!string.IsNullOrEmpty(nc)
                && (string.Equals(nu, nc, StringComparison.OrdinalIgnoreCase)
                    || LevenshteinDistanceAtMostOne(nu.ToUpperInvariant(), nc.ToUpperInvariant())))
            {
                return "spelling";
            }
        }

        if ((q.QuestionType is ReadingQuestionType.MultipleChoice3
                or ReadingQuestionType.MultipleChoice4
                or ReadingQuestionType.MultipleChoiceFlexible)
            && a.SelectedDistractorCategory is not null)
        {
            return "distractor";
        }

        if (q.QuestionType == ReadingQuestionType.MatchingTextReference)
        {
            return "wrong_text";
        }

        if (isPartATyped && nu is not null && nc is not null
            && nu.Length > 0 && nc.Length > nu.Length
            && nc.Contains(nu, StringComparison.OrdinalIgnoreCase))
        {
            return "incomplete";
        }

        return "wrong";
    }

    private static HashSet<string> ParseStringSet(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray()
                    .Select(e => (e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()) ?? "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpperInvariant())
                    .ToHashSet(),
                JsonValueKind.String => new HashSet<string> { root.GetString()!.Trim().ToUpperInvariant() },
                _ => new()
            };
        }
        catch (JsonException) { return new(); }
    }

    private static string? ParseJsonString(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<string>(json); }
        catch (JsonException) { return null; }
    }

    private static bool TryParseStringMap(string? json, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                    return false;

                var key = prop.Name.Trim();
                if (key.Length == 0)
                    return false;

                values[key] = prop.Value.GetString() ?? string.Empty;
            }

            return values.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ParseLabeledSynonyms(
        string? json,
        out string[]? globalSynonyms,
        out Dictionary<string, string[]> labeledSynonyms)
    {
        globalSynonyms = null;
        labeledSynonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var global = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                        global.Add(s);
                }

                if (global.Count > 0)
                    globalSynonyms = global.ToArray();
                return;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(prop.Name) || prop.Value.ValueKind != JsonValueKind.Array)
                    continue;

                var list = new List<string>();
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                        list.Add(s);
                }

                if (list.Count > 0)
                    labeledSynonyms[prop.Name.Trim()] = list.ToArray();
            }
        }
        catch (JsonException)
        {
            // Malformed synonyms are ignored; grading still falls back to the primary answer.
        }
    }

    /// <summary>
    /// Phase 3 Error Bank — keep <see cref="ReadingErrorBankEntry"/> rows
    /// in sync with this attempt's grading. Wrong answers upsert an entry
    /// (incrementing TimesWrong + LastSeenWrongAt). Correct answers on a
    /// previously-missed question resolve the entry.
    /// </summary>
    private async Task UpdateErrorBankAsync(
        ReadingAttempt attempt,
        IReadOnlyDictionary<string, ReadingQuestion> questionById,
        CancellationToken ct)
    {
        if (attempt.Answers.Count == 0) return;

        var questionIds = attempt.Answers.Select(a => a.ReadingQuestionId).ToList();
        var existing = await db.ReadingErrorBankEntries
            .Where(e => e.UserId == attempt.UserId && questionIds.Contains(e.ReadingQuestionId))
            .ToListAsync(ct);
        var byQ = existing.ToDictionary(e => e.ReadingQuestionId);
        var now = DateTimeOffset.UtcNow;

        foreach (var ans in attempt.Answers)
        {
            if (!questionById.TryGetValue(ans.ReadingQuestionId, out var q)) continue;
            var partCode = await ResolvePartCodeAsync(q, ct);
            byQ.TryGetValue(ans.ReadingQuestionId, out var entry);

            // A corrupted single-answer MCQ is held for administrator review,
            // not treated as a learner error. Do not seed or mutate the
            // ordinary Error Bank from an indeterminate mark.
            if (IsIntegrityReviewReason(ans.MissReason))
            {
                continue;
            }

            if (ans.IsCorrect == true)
            {
                if (entry is { IsResolved: false })
                {
                    entry.IsResolved = true;
                    entry.ResolvedAt = now;
                    entry.ResolvedReason = "answered_correctly";
                }
                continue;
            }

            if (entry is null)
            {
                entry = new ReadingErrorBankEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    UserId = attempt.UserId,
                    ReadingQuestionId = ans.ReadingQuestionId,
                    PaperId = attempt.PaperId,
                    PartCode = partCode,
                    LastWrongAttemptId = attempt.Id,
                    FirstSeenWrongAt = now,
                    LastSeenWrongAt = now,
                    TimesWrong = 1,
                    IsResolved = false,
                };
                db.ReadingErrorBankEntries.Add(entry);
                byQ[ans.ReadingQuestionId] = entry;
            }
            else
            {
                entry.LastWrongAttemptId = attempt.Id;
                entry.LastSeenWrongAt = now;
                entry.TimesWrong += 1;
                entry.PartCode = partCode;
                entry.PaperId = attempt.PaperId;
                if (entry.IsResolved)
                {
                    entry.IsResolved = false;
                    entry.ResolvedAt = null;
                    entry.ResolvedReason = null;
                }
            }
        }
    }

    private async Task<ReadingPartCode> ResolvePartCodeAsync(ReadingQuestion q, CancellationToken ct)
    {
        if (q.Part is not null) return q.Part.PartCode;
        var code = await db.ReadingParts.AsNoTracking()
            .Where(p => p.Id == q.ReadingPartId)
            .Select(p => (ReadingPartCode?)p.PartCode)
            .FirstOrDefaultAsync(ct);
        return code ?? ReadingPartCode.A;
    }

    private async Task<ReadingGradingResult> BuildResultFromExistingAsync(
        ReadingAttempt attempt, int raw, CancellationToken ct)
    {
        var ansMap = attempt.Answers.ToDictionary(x => x.ReadingQuestionId);
        var partIds = await db.ReadingParts
            .Where(p => p.PaperId == attempt.PaperId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var questions = await db.ReadingQuestions
            .Where(q => partIds.Contains(q.ReadingPartId))
            .ToListAsync(ct);

        // Subset modes only include their in-scope questions in the
        // returned details + counts.
        var isSubsetPracticeMode = IsSubsetPracticeMode(attempt.Mode);
        var scopeIds = ParseScopeQuestionIds(attempt);
        if (isSubsetPracticeMode)
        {
            questions = questions.Where(q => scopeIds?.Contains(q.Id) == true).ToList();
        }

        int correct = 0, wrong = 0, invalid = 0, unans = 0;
        var details = new List<ReadingAnswerResult>(questions.Count);
        foreach (var q in questions)
        {
            if (!ansMap.TryGetValue(q.Id, out var a))
            {
                unans++;
                details.Add(new(q.Id, q.QuestionType.ToString(), false, 0, q.Points));
                continue;
            }
            var isInvalid = IsIntegrityReviewReason(a.MissReason);
            var ok = a.IsCorrect ?? false;
            if (isInvalid) invalid++;
            else if (ok) correct++;
            else wrong++;
            details.Add(new(q.Id, q.QuestionType.ToString(), ok, a.PointsEarned, q.Points, a.MissReason, isInvalid));
        }

        var hasApprovedConversion = !attempt.RequiresAdminReview
            && !IsSubsetPracticeMode(attempt.Mode)
            && attempt.MaxRawScore == ReadingStructureService.CanonicalMaxRawScore
            && attempt.ScaledScore.HasValue
            && !string.IsNullOrWhiteSpace(attempt.ScoreConversionTableVersionKey)
            && attempt.ScoreConversionPassed.HasValue;
        var scaled = hasApprovedConversion ? attempt.ScaledScore : null;
        var grade = hasApprovedConversion ? attempt.ScoreConversionGrade ?? "—" : "—";
        return new ReadingGradingResult(
            raw, attempt.MaxRawScore, scaled, grade,
            correct, wrong, unans, details,
            hasApprovedConversion ? attempt.ScoreConversionTableVersionKey : null,
            hasApprovedConversion
                ? null
                : attempt.RequiresAdminReview
                    ? ResolveReviewReason(attempt)
                    : "score_conversion_unavailable",
            hasApprovedConversion ? attempt.ScoreConversionGrade : null,
            hasApprovedConversion ? attempt.ScoreConversionPassed : null,
            InvalidCount: invalid);
    }

    private static string ResolveReviewReason(ReadingAttempt attempt)
    {
        if (attempt.Answers.Any(a => a.MissReason == MultipleSelectionReviewReason))
            return MultipleSelectionReviewReason;
        if (attempt.Answers.Any(a => a.MissReason == QuestionIntegrityReviewReason))
            return QuestionIntegrityReviewReason;
        return attempt.AdminReviewReason ?? QuestionIntegrityReviewReason;
    }

    private async Task<ReadingResolvedPolicy> ResolvePolicyForAttemptAsync(ReadingAttempt attempt, CancellationToken ct)
    {
        var requiresGovernedSnapshot = !string.IsNullOrWhiteSpace(attempt.MarkingPolicyVersionId);
        if (string.IsNullOrWhiteSpace(attempt.PolicySnapshotJson))
        {
            if (requiresGovernedSnapshot)
                throw new InvalidOperationException("assessment_marking_policy_snapshot_missing");

            return await policyService.ResolveForUserAsync(attempt.UserId, ct);
        }

        try
        {
            using var document = JsonDocument.Parse(attempt.PolicySnapshotJson);
            if (!document.RootElement.TryGetProperty("markingPolicy", out var markingPolicyElement)
                || markingPolicyElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (requiresGovernedSnapshot)
                    throw new InvalidOperationException("assessment_marking_policy_snapshot_missing");

                return await policyService.ResolveForUserAsync(attempt.UserId, ct);
            }

            if (requiresGovernedSnapshot)
            {
                var versionKey = document.RootElement.TryGetProperty("markingPolicyVersionKey", out var versionElement)
                    && versionElement.ValueKind == JsonValueKind.String
                    ? versionElement.GetString()?.Trim()
                    : null;
                if (string.IsNullOrWhiteSpace(versionKey))
                    throw new InvalidOperationException("assessment_marking_policy_snapshot_missing");

                var storedVersionKey = await db.AssessmentMarkingPolicyVersions
                    .AsNoTracking()
                    .Where(policy => policy.Id == attempt.MarkingPolicyVersionId)
                    .Select(policy => policy.VersionKey)
                    .SingleOrDefaultAsync(ct);
                if (string.IsNullOrWhiteSpace(storedVersionKey))
                    throw new InvalidOperationException("assessment_marking_policy_snapshot_invalid");
                if (!string.Equals(storedVersionKey, versionKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("assessment_marking_policy_snapshot_version_mismatch");
            }

            var snapshot = JsonSerializer.Deserialize<ReadingResolvedPolicy>(attempt.PolicySnapshotJson);
            if (snapshot is not null
                && !string.IsNullOrWhiteSpace(snapshot.PartATimerStrictness)
                && !string.IsNullOrWhiteSpace(snapshot.ShortAnswerNormalisation)
                && !string.IsNullOrWhiteSpace(snapshot.UnknownTypeFallbackPolicy))
            {
                return ApplyGovernedMarkingPolicy(snapshot, attempt.PolicySnapshotJson);
            }

            if (requiresGovernedSnapshot)
                throw new InvalidOperationException("assessment_marking_policy_snapshot_invalid");
        }
        catch (JsonException)
        {
            if (requiresGovernedSnapshot)
                throw new InvalidOperationException("assessment_marking_policy_snapshot_invalid_json");
        }

        // Legacy attempts created before governed policy versioning remain
        // readable. New governed attempts never fall back to mutable policy.
        return await policyService.ResolveForUserAsync(attempt.UserId, ct);
    }

    private static ReadingQuestion ApplyKeyCorrection(ReadingQuestion question, string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            throw new InvalidOperationException("remark_key_snapshot_required");

        using var document = JsonDocument.Parse(snapshotJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("remark_key_snapshot_invalid_json");
        var root = document.RootElement;
        var hasCorrectJson = root.TryGetProperty("correctAnswerJson", out var correctJson);
        var hasCorrect = hasCorrectJson || root.TryGetProperty("correctAnswer", out correctJson);
        var hasAcceptedJson = root.TryGetProperty("acceptedSynonymsJson", out var acceptedJson);
        var hasAccepted = hasAcceptedJson || root.TryGetProperty("acceptedVariants", out acceptedJson);
        if (!hasCorrect && !hasAccepted)
            throw new InvalidOperationException("remark_key_snapshot_missing_key");

        var correctedAnswer = hasCorrect
            ? hasCorrectJson && correctJson.ValueKind == JsonValueKind.String
                ? correctJson.GetString() ?? "null"
                : correctJson.ValueKind == JsonValueKind.String
                    ? JsonSerializer.Serialize(correctJson.GetString())
                    : correctJson.GetRawText()
            : question.CorrectAnswerJson;
        var correctedVariants = hasAccepted
            ? hasAcceptedJson && acceptedJson.ValueKind == JsonValueKind.String
                ? acceptedJson.GetString()
                : acceptedJson.ValueKind == JsonValueKind.Null ? null : acceptedJson.GetRawText()
            : question.AcceptedSynonymsJson;

        return new ReadingQuestion
        {
            Id = question.Id,
            ReadingPartId = question.ReadingPartId,
            ReadingSectionId = question.ReadingSectionId,
            ReadingTextId = question.ReadingTextId,
            DisplayOrder = question.DisplayOrder,
            Points = question.Points,
            QuestionType = question.QuestionType,
            Stem = question.Stem,
            OptionsJson = question.OptionsJson,
            ParagraphIndex = question.ParagraphIndex,
            CorrectAnswerJson = correctedAnswer,
            AcceptedSynonymsJson = correctedVariants,
            CaseSensitive = question.CaseSensitive,
            ExplanationMarkdown = question.ExplanationMarkdown,
            SkillTag = question.SkillTag,
            Difficulty = question.Difficulty,
            EvidenceSentence = question.EvidenceSentence,
            DistractorRationaleJson = question.DistractorRationaleJson,
            OptionDistractorsJson = question.OptionDistractorsJson,
            BoxExplanationsJson = question.BoxExplanationsJson,
            ReviewState = question.ReviewState,
            LatestReviewNote = question.LatestReviewNote,
            CreatedAt = question.CreatedAt,
            UpdatedAt = question.UpdatedAt,
        };
    }

    private sealed record KeyCorrection(string QuestionRevisionId, string NewKeySnapshotJson);

    private sealed record MultipleSelectionIntegrityIssue(
        string QuestionId,
        int QuestionNumber,
        string PartCode,
        IReadOnlyList<string> Selections);

    private sealed record QuestionIntegrityIssue(
        string QuestionId,
        int QuestionNumber,
        string Reason);

    private static ReadingResolvedPolicy ApplyGovernedMarkingPolicy(
        ReadingResolvedPolicy policy,
        string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return policy;
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            if (!document.RootElement.TryGetProperty("markingPolicy", out var element))
                return policy;
            var policyJson = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();
            var marking = AssessmentMarkingPolicyDocument.Parse(policyJson);
            return policy with
            {
                MatchingAllowPartialCredit = marking.ReadingPartAMatchingPartialCredit,
                PartACaseInsensitive = !marking.CaseSensitive,
            };
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("assessment_marking_policy_snapshot_invalid_json");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException("assessment_marking_policy_snapshot_invalid");
        }
}
}
