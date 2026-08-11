using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Assessment;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 — version-pinned grading. A submitted attempt is graded only
/// while the published question versions captured at start still match the
/// authored rows. If an authoring edit races an attempt, grading fails closed
/// for controlled re-marking rather than silently using a different key.
/// Raw→scaled conversion routes ONLY through
/// versioned score-conversion table is the sole scaled-score source (the
/// <c>ListeningScoringPathAuditTest</c> source-scan fails CI on inline math).
/// </summary>
public sealed class ListeningGradingService
{
    private readonly LearnerDbContext _db;
    private readonly IAssessmentScoreConversionService _scoreConversion;

    private const string Subtest = "listening";
    private const int MaxOverrideReasonLength = 2_000;

    public ListeningGradingService(
        LearnerDbContext db,
        IAssessmentScoreConversionService? scoreConversion = null)
    {
        _db = db;
        _scoreConversion = scoreConversion ?? new AssessmentScoreConversionService(db);
    }

    public async Task<ListeningGradingResult> GradeAsync(
        string attemptId, CancellationToken ct)
        => await GradeAsync(attemptId, userId: null, ct);

    public async Task<ListeningGradingResult> GradeAsync(
        string attemptId, string? userId, CancellationToken ct)
    {
        var attempt = await _db.ListeningAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new KeyNotFoundException($"Attempt {attemptId} not found.");

        if (userId is not null && !string.Equals(attempt.UserId, userId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Listening attempt does not belong to the current user.");
        }

        await EnsurePublishedPaperRevisionUnchangedAsync(attempt, ct);
        var result = await GradeAttemptAsync(attempt, refreshSubmittedAt: true, ct);
        await _db.SaveChangesAsync(ct);

        return result;
    }

    private async Task EnsurePublishedPaperRevisionUnchangedAsync(
        ListeningAttempt attempt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(attempt.PaperRevisionId)) return;

        var currentRevision = await _db.ContentPapers.AsNoTracking()
            .Where(paper => paper.Id == attempt.PaperId)
            .Select(paper => paper.PublishedRevisionId)
            .SingleOrDefaultAsync(ct);
        if (!string.Equals(attempt.PaperRevisionId, currentRevision, StringComparison.Ordinal))
        {
            throw ApiException.Conflict(
                "listening_paper_revision_changed",
                "The published Listening paper revision changed after this attempt started. The attempt is held for controlled re-marking.");
        }
    }

    /// <summary>Execute an approved single-question answer-key correction
    /// without mutating the published Listening question row.</summary>
    public async Task<ListeningGradingResult> RegradeWithKeyAsync(
        string attemptId,
        string questionRevisionId,
        string newKeySnapshotJson,
        CancellationToken ct)
    {
        var attempt = await _db.ListeningAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw ApiException.NotFound("listening_attempt_not_found", "Listening attempt not found.");
        if (attempt.Status != ListeningAttemptStatus.Submitted)
            throw ApiException.Conflict("listening_remark_requires_submitted", "Controlled re-mark requires a submitted attempt.");
        if (string.IsNullOrWhiteSpace(questionRevisionId))
            throw ApiException.Validation("remark_question_revision_required", "A question revision is required.");

        var result = await GradeAttemptAsync(
            attempt,
            refreshSubmittedAt: false,
            ct,
            new KeyCorrection(questionRevisionId.Trim(), newKeySnapshotJson));
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<ListeningScoreOverrideResult> ApplyScoreOverrideAsync(
        string attemptId,
        string questionId,
        int overrideValue,
        string actorId,
        string? actorName,
        string? reason,
        CancellationToken ct)
    {
        if (overrideValue is not 0 and not 1)
        {
            throw ApiException.Validation(
                "listening_override_invalid_value",
                "Listening score override must be 0 for incorrect or 1 for correct.");
        }

        var normalizedReason = NormalizeOverrideReason(reason);
        if (normalizedReason is null)
        {
            throw ApiException.Validation(
                "listening_override_reason_required",
                "A human score override requires an audit reason.");
        }

        var attempt = await _db.ListeningAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw ApiException.NotFound("listening_attempt_not_found", "Listening attempt not found.");

        if (attempt.Status != ListeningAttemptStatus.Submitted)
        {
            throw ApiException.Conflict(
                "listening_override_requires_submitted_attempt",
                "Listening score overrides are available only after the attempt has been submitted.");
        }

        await EnsureReviewerAssignedToAttemptAsync(actorId, attempt.Id, ct);

        var question = await _db.ListeningQuestions.AsNoTracking()
            .Where(q => q.Id == questionId && q.PaperId == attempt.PaperId)
            .Select(q => new { q.Id })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound(
                "listening_question_not_found",
                "This Listening question does not belong to the submitted attempt.");

        var existing = ParseOverrides(attempt.HumanScoreOverridesJson)
            .Values
            .Where(o => !string.Equals(o.QuestionId, question.Id, StringComparison.Ordinal))
            .ToList();
        existing.Add(new ScoreOverride(question.Id, overrideValue, actorId, normalizedReason));
        attempt.HumanScoreOverridesJson = JsonSerializer.Serialize(existing, OverrideJsonOptions);

        var result = await GradeAttemptAsync(attempt, refreshSubmittedAt: false, ct);
        var grade = result.ScoreConversionGrade ?? "—";
        await RefreshLatestEvaluationAsync(result, grade, actorId, normalizedReason, ct);

        _db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? actorId : actorName.Trim(),
            Action = "listening.score.override",
            ResourceType = "ListeningAttempt",
            ResourceId = attempt.Id,
            Details = JsonSerializer.Serialize(new
            {
                attemptId = attempt.Id,
                attempt.UserId,
                attempt.PaperId,
                questionId = question.Id,
                overrideValue,
                reason = normalizedReason,
                rawScore = result.RawScore,
                maxRawScore = result.MaxRawScore,
                scaledScore = result.ScaledScore
            })
        });

        await _db.SaveChangesAsync(ct);

        return new ListeningScoreOverrideResult(
            AttemptId: attempt.Id,
            QuestionId: question.Id,
            Override: overrideValue,
            Reason: normalizedReason,
            By: actorId,
            RawScore: result.RawScore,
            MaxRawScore: result.MaxRawScore,
            ScaledScore: result.ScaledScore,
            ScoreConversionErrorCode: result.ScoreConversionErrorCode,
            Grade: grade);
    }

    private async Task EnsureReviewerAssignedToAttemptAsync(
        string reviewerId,
        string attemptId,
        CancellationToken ct)
    {
        var hasAssignment = await (
            from review in _db.ReviewRequests.AsNoTracking()
            join assignment in _db.ExpertReviewAssignments.AsNoTracking()
                on review.Id equals assignment.ReviewRequestId
            where review.AttemptId == attemptId
                && review.SubtestCode == Subtest
                && review.State != ReviewRequestState.Cancelled
                && review.State != ReviewRequestState.Failed
                && assignment.AssignedReviewerId == reviewerId
                && (assignment.ClaimState == ExpertAssignmentState.Assigned
                    || assignment.ClaimState == ExpertAssignmentState.Claimed)
            select review.Id)
            .AnyAsync(ct);

        if (!hasAssignment)
        {
            throw ApiException.Forbidden(
                "listening_override_reviewer_not_assigned",
                "Only the assigned expert reviewer can override this Listening attempt score.");
        }
    }

    private async Task<ListeningGradingResult> GradeAttemptAsync(
        ListeningAttempt attempt,
        bool refreshSubmittedAt,
        CancellationToken ct,
        KeyCorrection? keyCorrection = null)
    {
        var now = DateTimeOffset.UtcNow;

        var answers = await _db.ListeningAnswers
            .Where(a => a.ListeningAttemptId == attempt.Id)
            .ToListAsync(ct);

        var questions = await _db.ListeningQuestions
            .Where(q => q.PaperId == attempt.PaperId)
            .Include(q => q.Options)
            .ToListAsync(ct);

        // Resolve the captured policy once per grade pass. The owner-approved
        // profile controls whether internal whitespace may be collapsed; the
        // grader never enables fuzzy or synonym inference implicitly.
        var markingPolicy = await ResolveMarkingPolicyAsync(attempt, ct);
        var normalisation = !markingPolicy.TrimLeadingTrailingWhitespace
            ? "exact"
            : markingPolicy.CollapseInternalWhitespace ? "trim_collapse" : "trim_only";

        // Build a paper-wide map: normalisedAnswer → questionId. Powers the
        // WrongSection heuristic — if a learner's answer matches another
        // question's canonical/variant on the same paper, the miss class
        // becomes WrongSection rather than Paraphrase.
        var gradingQuestions = questions
            .Select(q => keyCorrection?.QuestionRevisionId == q.Id
                ? ApplyKeyCorrection(q, keyCorrection.NewKeySnapshotJson)
                : q)
            .ToList();
        if (keyCorrection is not null && !questions.Any(q => q.Id == keyCorrection.QuestionRevisionId))
            throw ApiException.NotFound("remark_question_revision_not_found", "The re-mark question is not on this Listening paper.");
        var paperAnswerMap = BuildPaperAnswerMap(gradingQuestions, normalisation);

        // Version-pin map: the relational start path captures every question
        // version. Refuse to grade against a changed or missing live row: the
        // candidate must never receive a result produced from a key different
        // from the one captured at attempt start.
        var versionMap = ParseVersionMap(attempt.LastQuestionVersionMapJson);
        if (versionMap.Count > 0)
        {
            var currentQuestionIds = questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
            var missing = versionMap.Keys.Where(id => !currentQuestionIds.Contains(id)).ToArray();
            var changed = questions
                .Where(q => versionMap.TryGetValue(q.Id, out var version) && version != q.Version)
                .Select(q => q.Id)
                .ToArray();
            var unauthorisedChanged = keyCorrection is null
                ? changed
                : changed.Where(id => !string.Equals(id, keyCorrection.QuestionRevisionId, StringComparison.Ordinal)).ToArray();
            if (missing.Length > 0 || unauthorisedChanged.Length > 0 || currentQuestionIds.Count != versionMap.Count)
            {
                throw ApiException.Conflict(
                    "listening_question_revision_changed",
                    "The published Listening question revision changed after this attempt started. The attempt is held for controlled re-marking.");
            }
        }
        var overrides = ParseOverrides(attempt.HumanScoreOverridesJson);
        var answerByQuestionId = answers
            .GroupBy(a => a.ListeningQuestionId)
            .ToDictionary(g => g.Key, g => g.First());

        var rawCorrect = 0;
        var driftedQuestionIds = new List<string>();
        var multipleSelectionIssues = new List<MultipleSelectionIntegrityIssue>();
        foreach (var q in questions.OrderBy(q => q.QuestionNumber).ThenBy(q => q.DisplayOrder))
        {
            if (!answerByQuestionId.TryGetValue(q.Id, out var ans))
            {
                ans = new ListeningAnswer
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ListeningAttemptId = attempt.Id,
                    ListeningQuestionId = q.Id,
                    UserAnswerJson = JsonSerializer.Serialize(string.Empty),
                    QuestionVersionSnapshot = versionMap.TryGetValue(q.Id, out var snapshot) ? snapshot : q.Version,
                    AnsweredAt = now,
                };
                _db.ListeningAnswers.Add(ans);
            }

            var pinnedVersion = ans.QuestionVersionSnapshot
                ?? (versionMap.TryGetValue(q.Id, out var v) ? v : q.Version);
            if (pinnedVersion != q.Version
                && !string.Equals(q.Id, keyCorrection?.QuestionRevisionId, StringComparison.Ordinal))
            {
                throw ApiException.Conflict(
                    "listening_question_revision_changed",
                    "The published Listening question revision changed after this attempt started. The attempt is held for controlled re-marking.");
            }

            var gradingQuestion = gradingQuestions.First(candidate => candidate.Id == q.Id);
            if (gradingQuestion.QuestionType == ListeningQuestionType.MultipleChoice3
                && TryReadMultipleSelections(ans.UserAnswerJson, out var selections))
            {
                multipleSelectionIssues.Add(new MultipleSelectionIntegrityIssue(
                    q.Id,
                    q.QuestionNumber,
                    selections));
            }
            var evaluation = Evaluate(gradingQuestion, ans, paperAnswerMap, normalisation, gradingQuestion.CaseSensitive && markingPolicy.CaseSensitive);
            var isCorrect = evaluation.IsCorrect;
            var distractor = evaluation.Distractor;
            var missReason = evaluation.MissReason;
            ans.IsCorrect = isCorrect;
            ans.PointsEarned = isCorrect ? gradingQuestion.Points : 0;
            ans.SelectedDistractorCategory = distractor;
            ans.MissReason = missReason;

            // Expert / admin manual overrides take precedence (R-rulebook
            // does not forbid this; we surface as a distinct field so
            // analytics can split organic vs adjusted scores).
            if (overrides.TryGetValue(q.Id, out var ovr))
            {
                ans.IsCorrect = ovr.Override == 1;
                ans.PointsEarned = ovr.Override == 1 ? gradingQuestion.Points : 0;
            }

            if (ans.IsCorrect == true) rawCorrect += gradingQuestion.Points;

            var acceptedVariant = isCorrect
                ? FindAcceptedVariant(gradingQuestion, ans, normalisation, gradingQuestion.CaseSensitive && markingPolicy.CaseSensitive)
                : null;
            if (acceptedVariant is not null)
            {
                _db.AuditEvents.Add(new AuditEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    OccurredAt = now,
                    ActorId = attempt.UserId,
                    ActorName = "ListeningGradingService",
                    Action = "listening.marking.accepted_variant_used",
                    ResourceType = "ListeningAttemptAnswer",
                    ResourceId = $"{attempt.Id}:{q.Id}",
                    Details = JsonSerializer.Serialize(new
                    {
                        attemptId = attempt.Id,
                        questionId = q.Id,
                        questionNumber = q.QuestionNumber,
                        acceptedVariant,
                        policyNormalisation = normalisation,
                        caseSensitive = q.CaseSensitive && markingPolicy.CaseSensitive,
                    }),
                });
            }

            if (pinnedVersion != q.Version) driftedQuestionIds.Add(q.Id);
        }

        if (multipleSelectionIssues.Count > 0)
        {
            _db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = attempt.UserId,
                ActorName = "ListeningGradingService",
                Action = "listening.mcq.multiple_selection_review_required",
                ResourceType = "ListeningAttempt",
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

        // H9: Emit audit event when version drift is detected so the
        // analytics pipeline and reviewers can identify potentially
        // impacted grades.
        if (driftedQuestionIds.Count > 0)
        {
            _db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                OccurredAt = now,
                ActorId = "system",
                ActorName = "ListeningGradingService",
                Action = "listening.grading.version_drift_detected",
                ResourceType = "ListeningAttempt",
                ResourceId = attempt.Id,
                Details = JsonSerializer.Serialize(new
                {
                    message = "Question version drift detected during grading. The candidate may have seen a different version than the current authored state.",
                    driftedQuestionIds,
                    driftedCount = driftedQuestionIds.Count,
                    totalQuestions = questions.Count,
                    attemptVersionMap = attempt.LastQuestionVersionMapJson,
                }),
            });
        }

        attempt.RawScore = rawCorrect;
        attempt.MaxRawScore = questions.Sum(q => Math.Max(0, q.Points));

        // H12: Defensive guard — a paper with zero gradable questions must
        // not silently produce a 0/500 floor score. This can happen if the
        // relational store has no questions (empty projection / backfill needed).
        if (attempt.MaxRawScore <= 0)
        {
            throw ApiException.Conflict(
                "listening_no_questions_to_grade",
                "Cannot grade this attempt: the paper has no structured questions in the relational store. Run backfill first.");
        }

        // ── MISSION-CRITICAL ── raw→scaled MUST go through the owner-approved
        // versioned lookup table. Inline math and interpolation are forbidden.
        // ListeningScoringPathAuditTest source-scans for it on CI.
        var conversion = await AssessmentScoreConversionSnapshotResolver.ResolveAsync(
            _scoreConversion,
            Subtest,
            rawCorrect,
            attempt.ScoreConversionSnapshotJson,
            attempt.ScoreConversionTableId,
            "default",
            ct);
        if (conversion.TableId is not null && conversion.IsAvailable)
        {
            await _scoreConversion.MarkUsedAsync(conversion.TableId, ct);
        }
        attempt.ScoreConversionTableId = conversion.TableId;
        var hasApprovedConversion = conversion.ConvertedScore.HasValue
            && !string.IsNullOrWhiteSpace(conversion.TableVersionKey)
            && conversion.Passed.HasValue;
        attempt.ScoreConversionTableVersionKey = hasApprovedConversion ? conversion.TableVersionKey : null;
        attempt.ScoreConversionGrade = hasApprovedConversion ? conversion.Grade : null;
        attempt.ScoreConversionPassed = hasApprovedConversion ? conversion.Passed : null;
        attempt.ScaledScore = hasApprovedConversion ? conversion.ConvertedScore : null;
        if (refreshSubmittedAt || attempt.SubmittedAt is null)
        {
            attempt.SubmittedAt = now;
        }
        attempt.LastActivityAt = now;
        attempt.Status = ListeningAttemptStatus.Submitted;
        attempt.RowVersion++;

        return new ListeningGradingResult(
            AttemptId: attempt.Id,
            RawScore: rawCorrect,
            MaxRawScore: attempt.MaxRawScore,
            ScaledScore: attempt.ScaledScore,
            ScoreConversionTableVersionKey: hasApprovedConversion ? attempt.ScoreConversionTableVersionKey : null,
            ScoreConversionErrorCode: conversion.ErrorCode,
            ScoreConversionGrade: hasApprovedConversion ? conversion.Grade : null,
            ScoreConversionPassed: hasApprovedConversion ? conversion.Passed : null);
    }

    private async Task RefreshLatestEvaluationAsync(
        ListeningGradingResult result,
        string grade,
        string actorId,
        string reason,
        CancellationToken ct)
    {
        var evaluation = await _db.Evaluations
            .Where(e => e.AttemptId == result.AttemptId && e.SubtestCode == Subtest)
            .OrderByDescending(e => e.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (evaluation is null) return;

        var passed = result.ScoreConversionPassed;
        var scoreDisplay = FormatScoreDisplay(result, grade);
        evaluation.ScoreRange = scoreDisplay;
        evaluation.GradeRange = result.ScaledScore.HasValue ? $"Grade {grade}" : "Practice score unavailable";
        evaluation.RawScore = result.RawScore;
        evaluation.MaxRawScore = result.MaxRawScore;
        evaluation.ScaledScore = result.ScaledScore;
        evaluation.ScoreConversionTableVersionKey = result.ScoreConversionTableVersionKey;
        evaluation.ScoreConversionGrade = result.ScoreConversionGrade;
        evaluation.ScoreConversionPassed = result.ScoreConversionPassed;
        evaluation.CriterionScoresJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                criterionCode = "listening_accuracy",
                rawScore = result.RawScore,
                maxRawScore = result.MaxRawScore,
                scaledScore = result.ScaledScore,
                grade = result.ScaledScore.HasValue ? grade : "—",
                passed,
                scoreDisplay,
                humanOverride = new { by = actorId, reason }
            }
        });
        evaluation.StatusReasonCode = "human_override_applied";
        evaluation.StatusMessage = "Listening score adjusted by human reviewer.";
        var now = DateTimeOffset.UtcNow;
        evaluation.GeneratedAt = now;
        evaluation.LastTransitionAt = now;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Pure evaluation helpers — no DB access, fully unit-testable.
    // ─────────────────────────────────────────────────────────────────────

    public const string DefaultNormalisation = "trim_only";

    public static (bool IsCorrect, ListeningDistractorCategory? Distractor, ListeningMissReason? MissReason) Evaluate(
        ListeningQuestion q,
        ListeningAnswer ans,
        IReadOnlyDictionary<string, string>? paperAnswerMap = null,
        string normalisation = DefaultNormalisation,
        bool? caseSensitiveOverride = null)
    {
        switch (q.QuestionType)
        {
            case ListeningQuestionType.MultipleChoice3:
            {
                var selected = TryReadString(ans.UserAnswerJson);
                if (string.IsNullOrEmpty(selected)) return (false, null, null);

                var ordered = q.Options.OrderBy(o => o.DisplayOrder).ToList();

                // Canonical forward path: the learner submits the option KEY
                // (the positional letter A/B/C). Match it directly — this is
                // independent of the option display text, so real option prose
                // can never change a score.
                var opt = ordered.FirstOrDefault(o =>
                    string.Equals(o.OptionKey, selected, StringComparison.OrdinalIgnoreCase));

                // Legacy / in-flight answers may carry the option TEXT or a bare
                // numeric INDEX (older clients submitted the displayed option
                // text). Resolve those to a stable option position so historical
                // attempts keep grading correctly after the text becomes real prose.
                if (opt is null)
                {
                    var resolvedId = ListeningOptionIdHelper.ResolveLegacyAnswer(
                        selected, q.Id, ordered.Select(o => o.Text).ToList());
                    var index = resolvedId is null
                        ? null
                        : ListeningOptionIdHelper.ExtractOptionIndex(resolvedId);
                    if (index is int i && i >= 0 && i < ordered.Count) opt = ordered[i];
                }

                if (opt is null) return (false, null, null);
                return (opt.IsCorrect, opt.IsCorrect ? null : opt.DistractorCategory, null);
            }
            // FillInBlank grades identically to ShortAnswer (canonical +
            // accepted-variants string compare). It is a distinct authored type
            // so admins can pick it, but the grading rubric is the same.
            case ListeningQuestionType.ShortAnswer:
            case ListeningQuestionType.FillInBlank:
            {
                var user = TryReadString(ans.UserAnswerJson) ?? string.Empty;
                var canonical = TryReadString(q.CorrectAnswerJson);
                var accepted = ParseAccepted(q.AcceptedSynonymsJson).ToList();
                var candidates = (canonical is null ? Enumerable.Empty<string>() : new[] { canonical })
                    .Concat(accepted)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                var caseSensitive = caseSensitiveOverride ?? q.CaseSensitive;
                bool matches = candidates.Any(c => StringsMatch(user, c, caseSensitive, normalisation));
                if (matches) return (true, null, ListeningMissReason.Match);

                var miss = ClassifyMiss(user, candidates, q, paperAnswerMap, normalisation);
                return (false, null, miss);
            }
            default:
                return (false, null, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Pure matching + classification — ported from ReadingGradingService so
    // Reading and Listening Part A share a single accuracy floor. Future
    // bumps to either grader should be mirrored across both modules.
    // ─────────────────────────────────────────────────────────────────────

    public static bool StringsMatch(string a, string b, bool caseSensitive, string normalisation)
    {
        if (a is null || b is null) return false;
        var na = NormaliseFor(a, normalisation);
        var nb = NormaliseFor(b, normalisation);

        // Decision 4 — hard-lock strictness everywhere: NO fuzzy/Levenshtein
        // ACCEPTANCE in any mode or policy. A stale `fuzzy_levenshtein_1`
        // normalisation degrades to exact match. (Levenshtein survives only in
        // ClassifyMiss for analytics labelling, never for grading.)
        return caseSensitive
            ? string.Equals(na, nb, StringComparison.Ordinal)
            : string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormaliseFor(string s, string strategy) => strategy switch
    {
        "exact" => s,
        "trim_only" => s.Trim(),
        "trim_collapse" => CollapseWhitespace(s.Trim()),
        // Legacy fuzzy policy names are deliberately reduced to exact matching
        // with trimming only; fuzzy acceptance is never permitted.
        "fuzzy_levenshtein_1" => s.Trim(),
        _ => s.Trim(),
    };

    private static bool LevenshteinDistanceAtMostOne(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (Math.Abs(a.Length - b.Length) > 1) return false;

        var edits = 0;
        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j]) { i++; j++; continue; }

            edits++;
            if (edits > 1) return false;

            if (a.Length == b.Length) { i++; j++; }
            else if (a.Length > b.Length) { i++; }
            else { j++; }
        }
        if (i < a.Length || j < b.Length) edits++;
        return edits <= 1;
    }

    private static int LevenshteinDistance(string a, string b, int cap)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return 0;
        if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
        // Two-row DP, early-exit when min(row) > cap.
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                if (curr[j] < rowMin) rowMin = curr[j];
            }
            if (rowMin > cap) return cap + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
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

    private static IReadOnlyList<string> Tokens(string s)
        => string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

    private static bool HasDigits(string s)
    {
        for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i])) return true;
        return false;
    }

    private static string DigitsOnly(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i])) sb.Append(s[i]);
        return sb.ToString();
    }

    public static ListeningMissReason ClassifyMiss(
        string user,
        IReadOnlyList<string> candidates,
        ListeningQuestion q,
        IReadOnlyDictionary<string, string>? paperAnswerMap,
        string normalisation)
    {
        if (string.IsNullOrWhiteSpace(user)) return ListeningMissReason.Empty;
        if (candidates.Count == 0) return ListeningMissReason.Other;

        var nuser = NormaliseFor(user, normalisation);
        var nuserCmp = q.CaseSensitive ? nuser : nuser.ToUpperInvariant();

        // 1) WrongNumber — checked first so a digit-bearing candidate doesn't
        //    fall into SpellingError via a small Levenshtein gap (e.g. "5"
        //    vs "12" is only 2 edits but the real failure is a number swap).
        var userDigits = DigitsOnly(nuser);
        var anyCandidateHasDigits = false;
        var anyDigitMatch = false;
        foreach (var c in candidates)
        {
            var nc = NormaliseFor(c, normalisation);
            if (!HasDigits(nc)) continue;
            anyCandidateHasDigits = true;
            if (DigitsOnly(nc) == userDigits) { anyDigitMatch = true; break; }
        }
        if (anyCandidateHasDigits && !anyDigitMatch)
            return ListeningMissReason.WrongNumber;

        // 2) Spelling — Levenshtein ≤2 to any candidate (wider than the
        //    grader's pass threshold of 1 so authors don't have to add
        //    every typo to AcceptedSynonymsJson).
        foreach (var c in candidates)
        {
            var nc = NormaliseFor(c, normalisation);
            var ncCmp = q.CaseSensitive ? nc : nc.ToUpperInvariant();
            if (LevenshteinDistance(nuserCmp, ncCmp, cap: 2) <= 2)
                return ListeningMissReason.SpellingError;
        }

        // 3) ExtraInfo — learner answer contains every token of some
        //    candidate plus at least 2 extras.
        var userTokens = Tokens(nuserCmp).ToHashSet();
        foreach (var c in candidates)
        {
            var nc = NormaliseFor(c, normalisation);
            var ncCmp = q.CaseSensitive ? nc : nc.ToUpperInvariant();
            var candTokens = Tokens(ncCmp).ToHashSet();
            if (candTokens.Count == 0) continue;
            if (candTokens.IsSubsetOf(userTokens) && userTokens.Count - candTokens.Count >= 2)
                return ListeningMissReason.ExtraInfo;
        }

        // 4) WrongSection — learner's normalised answer matches a canonical
        //    or variant of a DIFFERENT question on the same paper.
        if (paperAnswerMap is not null && paperAnswerMap.TryGetValue(nuserCmp, out var owner)
            && !string.Equals(owner, q.Id, StringComparison.Ordinal))
        {
            return ListeningMissReason.WrongSection;
        }

        // 5) Paraphrase — none of the structural heuristics fired.
        return ListeningMissReason.Paraphrase;
    }

    public static IReadOnlyDictionary<string, string> BuildPaperAnswerMap(
        IEnumerable<ListeningQuestion> questions,
        string normalisation)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var q in questions)
        {
            // FillInBlank is text-graded like ShortAnswer, so both contribute to
            // the cross-question answer map driving the WrongSection heuristic.
            if (q.QuestionType is not (ListeningQuestionType.ShortAnswer or ListeningQuestionType.FillInBlank)) continue;
            var canonical = TryReadString(q.CorrectAnswerJson);
            var accepted = ParseAccepted(q.AcceptedSynonymsJson);
            foreach (var raw in (canonical is null ? Enumerable.Empty<string>() : new[] { canonical }).Concat(accepted))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var norm = NormaliseFor(raw, normalisation);
                var key = q.CaseSensitive ? norm : norm.ToUpperInvariant();
                if (!map.ContainsKey(key)) map[key] = q.Id;
            }
        }
        return map;
    }

    private static Task<AssessmentMarkingPolicyDocument> ResolveMarkingPolicyAsync(
        ListeningAttempt attempt,
        CancellationToken ct)
    {
        _ = ct;
        var requiresGovernedSnapshot = !string.IsNullOrWhiteSpace(attempt.MarkingPolicyVersionId);
        if (string.IsNullOrWhiteSpace(attempt.PolicySnapshotJson))
        {
            if (requiresGovernedSnapshot)
                throw new InvalidOperationException("assessment_marking_policy_snapshot_missing");

            return Task.FromResult(new AssessmentMarkingPolicyDocument());
        }

        try
        {
            using var document = JsonDocument.Parse(attempt.PolicySnapshotJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("markingPolicy", out var policyElement))
            {
                var policyJson = policyElement.ValueKind == JsonValueKind.String
                    ? policyElement.GetString()
                    : policyElement.GetRawText();
                return Task.FromResult(AssessmentMarkingPolicyDocument.Parse(policyJson));
            }

            if (requiresGovernedSnapshot)
                throw new InvalidOperationException("assessment_marking_policy_snapshot_missing");
        }
        catch (JsonException)
        {
            if (requiresGovernedSnapshot)
                throw new InvalidOperationException("assessment_marking_policy_snapshot_invalid_json");
        }

        // Legacy attempts created before governed policy versioning retain the
        // conservative historical defaults. Governed attempts fail closed.
        return Task.FromResult(new AssessmentMarkingPolicyDocument());
    }

    private static string? TryReadString(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString(),
                JsonValueKind.Null => null,
                _ => doc.RootElement.GetRawText().Trim('"'),
            };
        }
        catch { return null; }
    }

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

    private static IEnumerable<string> ParseAccepted(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        List<string>? parsed = null;
        try { parsed = JsonSerializer.Deserialize<List<string>>(json); }
        catch { yield break; }
        if (parsed is null) yield break;
        foreach (var s in parsed) if (!string.IsNullOrWhiteSpace(s)) yield return s;
    }

    private static string? FindAcceptedVariant(
        ListeningQuestion question,
        ListeningAnswer answer,
        string normalisation,
        bool caseSensitive)
    {
        if (question.QuestionType is not (ListeningQuestionType.ShortAnswer or ListeningQuestionType.FillInBlank))
            return null;

        var user = TryReadString(answer.UserAnswerJson) ?? string.Empty;
        var canonical = TryReadString(question.CorrectAnswerJson);
        foreach (var variant in ParseAccepted(question.AcceptedSynonymsJson))
        {
            if (StringsMatch(user, variant, caseSensitive, normalisation)
                && (canonical is null || !StringsMatch(user, canonical, caseSensitive, normalisation)))
                return variant;
        }
        return null;
    }

    private static ListeningQuestion ApplyKeyCorrection(ListeningQuestion question, string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            throw ApiException.Validation("remark_key_snapshot_required", "A new answer-key snapshot is required.");

        using var document = JsonDocument.Parse(snapshotJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw ApiException.Validation("remark_key_snapshot_invalid_json", "The new answer-key snapshot must be a JSON object.");
        var root = document.RootElement;
        var hasCorrectJson = root.TryGetProperty("correctAnswerJson", out var correctJson);
        var hasCorrect = hasCorrectJson || root.TryGetProperty("correctAnswer", out correctJson);
        var hasAcceptedJson = root.TryGetProperty("acceptedSynonymsJson", out var acceptedJson);
        var hasAccepted = hasAcceptedJson || root.TryGetProperty("acceptedVariants", out acceptedJson);
        if (!hasCorrect && !hasAccepted)
            throw ApiException.Validation("remark_key_snapshot_missing_key", "The new answer-key snapshot contains no key or explicit variant.");

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

        return new ListeningQuestion
        {
            Id = question.Id,
            ListeningPartId = question.ListeningPartId,
            ListeningExtractId = question.ListeningExtractId,
            QuestionNumber = question.QuestionNumber,
            DisplayOrder = question.DisplayOrder,
            Points = question.Points,
            QuestionType = question.QuestionType,
            Stem = question.Stem,
            CorrectAnswerJson = correctedAnswer,
            AcceptedSynonymsJson = correctedVariants,
            CaseSensitive = question.CaseSensitive,
            Options = question.Options,
            ValidationStatus = question.ValidationStatus,
            ValidationNote = question.ValidationNote,
            Version = question.Version,
        };
    }

    private sealed record KeyCorrection(string QuestionRevisionId, string NewKeySnapshotJson);

    private static Dictionary<string, int> ParseVersionMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new(); }
        catch { return new(); }
    }

    private static readonly JsonSerializerOptions OverrideJsonOptions =
        new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string? NormalizeOverrideReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;
        var normalized = reason.Trim();
        return normalized.Length <= MaxOverrideReasonLength
            ? normalized
            : normalized[..MaxOverrideReasonLength];
    }

    private static string FormatScoreDisplay(ListeningGradingResult result, string grade)
        => result.ScaledScore is int scaled
            ? $"{result.RawScore} / {result.MaxRawScore} \u2022 {scaled} / 500 \u2022 Grade {grade}"
            : $"{result.RawScore} / {result.MaxRawScore} \u2022 scaled score unavailable";

    private static Dictionary<string, ScoreOverride> ParseOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var arr = JsonSerializer.Deserialize<List<ScoreOverride>>(json, OverrideJsonOptions) ?? new();
            return arr
                .Where(o => !string.IsNullOrEmpty(o.QuestionId))
                .GroupBy(o => o.QuestionId)
                .ToDictionary(g => g.Key, g => g.Last());
        }
        catch { return new(); }
    }

    private sealed record ScoreOverride(string QuestionId, int Override, string? By, string? Reason);

    private sealed record MultipleSelectionIntegrityIssue(
        string QuestionId,
        int QuestionNumber,
        IReadOnlyList<string> Selections);
}

public sealed record ListeningGradingResult(
    string AttemptId,
    int RawScore,
    int MaxRawScore,
    int? ScaledScore,
    string? ScoreConversionTableVersionKey,
    string? ScoreConversionErrorCode,
    string? ScoreConversionGrade,
    bool? ScoreConversionPassed);

public sealed record ListeningScoreOverrideResult(
    string AttemptId,
    string QuestionId,
    int Override,
    string Reason,
    string By,
    int RawScore,
    int MaxRawScore,
    int? ScaledScore,
    string? ScoreConversionErrorCode,
    string Grade);
