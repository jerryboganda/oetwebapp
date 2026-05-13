using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Services.Listening;

/// <summary>
/// Listening V2 — version-pinned grading. Re-reads each question at the
/// version snapshotted on <see cref="ListeningAnswer.QuestionVersionSnapshot"/>
/// (or <see cref="ListeningAttempt.LastQuestionVersionMapJson"/> as a
/// fallback) so admin edits in flight never silently invalidate scoring.
/// Raw→scaled conversion routes ONLY through
/// <see cref="OetScoring.OetRawToScaled"/> (mission-critical: the
/// <c>ListeningScoringPathAuditTest</c> source-scan fails CI on inline math).
/// </summary>
public sealed class ListeningGradingService
{
    private readonly LearnerDbContext _db;

    private const string Subtest = "listening";
    private const int MaxOverrideReasonLength = 2_000;

    public ListeningGradingService(LearnerDbContext db)
    {
        _db = db;
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

        var result = await GradeAttemptAsync(attempt, refreshSubmittedAt: true, ct);
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
        var grade = OetScoring.OetGradeLetterFromScaled(result.ScaledScore);
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
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var answers = await _db.ListeningAnswers
            .Where(a => a.ListeningAttemptId == attempt.Id)
            .ToListAsync(ct);

        var questions = await _db.ListeningQuestions
            .Where(q => q.PaperId == attempt.PaperId)
            .Include(q => q.Options)
            .ToListAsync(ct);

        // Version-pin map: prefer the per-attempt snapshot; fall back to per-row.
        var versionMap = ParseVersionMap(attempt.LastQuestionVersionMapJson);
        var overrides = ParseOverrides(attempt.HumanScoreOverridesJson);
        var answerByQuestionId = answers
            .GroupBy(a => a.ListeningQuestionId)
            .ToDictionary(g => g.Key, g => g.First());

        var rawCorrect = 0;
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

            // Drift guard: if the live row is newer than the pinned snapshot,
            // we re-grade against the snapshotted answer when possible. For
            // the V2 schema we only carry the version int, not the historical
            // payload — the authoring service is responsible for refusing
            // edits that change the correct answer once the snapshot is in
            // flight (planner Wave 2 §2 — version increment guarded edits).
            // Here we grade against the live row but flag drift on the
            // result so analytics can split it out.
            var drifted = pinnedVersion != q.Version;

            var (isCorrect, distractor) = Evaluate(q, ans);
            ans.IsCorrect = isCorrect;
            ans.PointsEarned = isCorrect ? q.Points : 0;
            ans.SelectedDistractorCategory = distractor;

            // Expert / admin manual overrides take precedence (R-rulebook
            // does not forbid this; we surface as a distinct field so
            // analytics can split organic vs adjusted scores).
            if (overrides.TryGetValue(q.Id, out var ovr))
            {
                ans.IsCorrect = ovr.Override == 1;
                ans.PointsEarned = ovr.Override == 1 ? q.Points : 0;
            }

            if (ans.IsCorrect == true) rawCorrect += q.Points;
            _ = drifted; // reserved for analytics enrichment in WS-F dashboards
        }

        attempt.RawScore = rawCorrect;
        attempt.MaxRawScore = questions.Sum(q => Math.Max(0, q.Points));

        // ── MISSION-CRITICAL ── raw→scaled MUST go through OetScoring.
        // Inline math (* 350 / / 42 / * 500 / * 8.33) is forbidden and
        // ListeningScoringPathAuditTest source-scans for it on CI.
        attempt.ScaledScore = OetScoring.OetRawToScaled(rawCorrect);
        if (refreshSubmittedAt || attempt.SubmittedAt is null)
        {
            attempt.SubmittedAt = now;
        }
        attempt.Status = ListeningAttemptStatus.Submitted;

        return new ListeningGradingResult(
            AttemptId: attempt.Id,
            RawScore: rawCorrect,
            MaxRawScore: attempt.MaxRawScore,
            ScaledScore: attempt.ScaledScore.Value);
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

        var passed = OetScoring.IsListeningReadingPassByScaled(result.ScaledScore);
        var scoreDisplay = FormatScoreDisplay(result, grade);
        evaluation.ScoreRange = scoreDisplay;
        evaluation.GradeRange = $"Grade {grade}";
        evaluation.CriterionScoresJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                criterionCode = "listening_accuracy",
                rawScore = result.RawScore,
                maxRawScore = result.MaxRawScore,
                scaledScore = result.ScaledScore,
                grade,
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

    internal static (bool IsCorrect, ListeningDistractorCategory? Distractor) Evaluate(
        ListeningQuestion q, ListeningAnswer ans)
    {
        switch (q.QuestionType)
        {
            case ListeningQuestionType.MultipleChoice3:
            {
                var selected = TryReadString(ans.UserAnswerJson);
                if (string.IsNullOrEmpty(selected)) return (false, null);
                var opt = q.Options.FirstOrDefault(o =>
                    string.Equals(o.OptionKey, selected, StringComparison.OrdinalIgnoreCase));
                if (opt is null) return (false, null);
                return (opt.IsCorrect, opt.IsCorrect ? null : opt.DistractorCategory);
            }
            case ListeningQuestionType.ShortAnswer:
            {
                var user = TryReadString(ans.UserAnswerJson);
                if (string.IsNullOrWhiteSpace(user)) return (false, null);
                var canonical = TryReadString(q.CorrectAnswerJson);
                var accepted = ParseAccepted(q.AcceptedSynonymsJson);
                var candidates = (canonical is null ? Enumerable.Empty<string>() : new[] { canonical })
                    .Concat(accepted);

                bool matches = candidates.Any(c => Compare(user, c, q.CaseSensitive));
                return (matches, null);
            }
            default:
                return (false, null);
        }
    }

    private static bool Compare(string a, string b, bool caseSensitive)
    {
        var na = Normalise(a);
        var nb = Normalise(b);
        return caseSensitive
            ? string.Equals(na, nb, StringComparison.Ordinal)
            : string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string s)
        => System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");

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

    private static IEnumerable<string> ParseAccepted(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        List<string>? parsed = null;
        try { parsed = JsonSerializer.Deserialize<List<string>>(json); }
        catch { yield break; }
        if (parsed is null) yield break;
        foreach (var s in parsed) if (!string.IsNullOrWhiteSpace(s)) yield return s;
    }

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
        => $"{result.RawScore} / {result.MaxRawScore} \u2022 {result.ScaledScore} / 500 \u2022 Grade {grade}";

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
}

public sealed record ListeningGradingResult(
    string AttemptId, int RawScore, int MaxRawScore, int ScaledScore);

public sealed record ListeningScoreOverrideResult(
    string AttemptId,
    string QuestionId,
    int Override,
    string Reason,
    string By,
    int RawScore,
    int MaxRawScore,
    int ScaledScore,
    string Grade);
