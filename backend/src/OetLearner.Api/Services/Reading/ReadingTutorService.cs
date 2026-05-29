using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Tutor Service — Wave 2
//
// Privileged (admin / expert) tooling on top of graded Reading attempts:
//   • Manual score override (apply / clear)
//   • Accepted-answer recalculation (re-grade stored answers in place)
//   • Privileged (non-redacted) attempt review
//   • Attempt feedback CRUD
//   • Assignment workflow (assign retake / drill, list, cancel)
//
// MISSION CRITICAL: every raw→scaled conversion routes through OetScoring
// (anchor 30/42 ≡ 350/500). Every mutation writes an AuditEvent. Attempts
// carrying a manual override are never silently re-graded by recalculation.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingTutorService
{
    Task<ReadingPrivilegedAttemptReview?> ApplyScoreOverrideAsync(
        string attemptId, ReadingScoreOverrideRequest request, string actorUserId, CancellationToken ct);

    Task<ReadingPrivilegedAttemptReview?> ClearScoreOverrideAsync(
        string attemptId, string actorUserId, CancellationToken ct);

    Task<ReadingRecalcResult> RecalcAsync(
        string paperId, ReadingRecalcRequest request, string actorUserId, CancellationToken ct);

    Task<ReadingPrivilegedAttemptReview?> GetPrivilegedReviewAsync(
        string attemptId, CancellationToken ct);

    Task<IReadOnlyList<ReadingFeedbackDto>> ListFeedbackAsync(string attemptId, CancellationToken ct);

    Task<ReadingFeedbackDto?> CreateFeedbackAsync(
        string attemptId, ReadingFeedbackRequest request, string actorUserId, CancellationToken ct);

    Task<ReadingFeedbackDto?> UpdateFeedbackAsync(
        string attemptId, string feedbackId, ReadingFeedbackRequest request, string actorUserId, CancellationToken ct);

    Task<bool> DeleteFeedbackAsync(string attemptId, string feedbackId, string actorUserId, CancellationToken ct);

    Task<ReadingAssignmentDto?> CreateAssignmentAsync(
        ReadingAssignmentCreateRequest request, string actorUserId, CancellationToken ct);

    Task<IReadOnlyList<ReadingAssignmentDto>> ListAssignmentsAsync(string? assignedToUserId, CancellationToken ct);

    Task<IReadOnlyList<ReadingAssignmentDto>> ListAssignmentsForExpertAsync(
        string expertUserId, string? assignedToUserId, CancellationToken ct);

    Task<bool> CancelAssignmentAsync(string assignmentId, string actorUserId, CancellationToken ct);

    Task<IReadOnlyList<ReadingAssignmentDto>> ListActiveAssignmentsForLearnerAsync(string userId, CancellationToken ct);

    Task<bool> CanExpertAccessAttemptAsync(string attemptId, string expertUserId, CancellationToken ct);
}

public sealed record ReadingScoreOverrideRequest(int? RawScore, int? ScaledScore, string Reason);

public sealed record ReadingRecalcRequest(string Scope, string? AttemptId);

public sealed record ReadingRecalcResult(int RecalculatedCount, int SkippedOverrideCount, int TotalConsidered);

public sealed record ReadingFeedbackRequest(string Scope, string? TargetRef, string FeedbackText);

/// <summary>
/// Granularity a piece of expert / admin attempt feedback is attached to.
/// Persisted as the lowercase enum name in
/// <see cref="OetLearner.Api.Domain.ReadingAttemptFeedback.Scope"/> (a
/// string column — no integer values reach the DB), and round-tripped via
/// <see cref="ReadingFeedbackScopeExtensions"/>.
/// </summary>
public enum ReadingFeedbackScope
{
    Test,
    Section,
    Question,
    Skill,
}

internal static class ReadingFeedbackScopeExtensions
{
    /// <summary>Parse a client-supplied scope case-insensitively against
    /// <see cref="ReadingFeedbackScope"/>, returning the normalized lowercase
    /// canonical name (e.g. <c>"test"</c>, <c>"section"</c>). Throws
    /// <see cref="ApiException"/> (400, code <c>reading_feedback_scope_invalid</c>)
    /// for any unknown value, so the persisted column can only ever hold one
    /// of the four known names.</summary>
    public static string NormalizeScope(string? scope)
    {
        if (TryNormalize(scope, out var normalized))
            return normalized;

        throw ApiException.Validation(
            "reading_feedback_scope_invalid",
            "Feedback scope must be one of: test, section, question, skill.",
            new[] { new ApiFieldError("scope", "reading_feedback_scope_invalid", "Unknown feedback scope.") });
    }

    /// <summary>Boundary check used by the endpoint layer: true when
    /// <paramref name="scope"/> names a known <see cref="ReadingFeedbackScope"/>
    /// (case-insensitive).</summary>
    public static bool IsValidScope(string? scope) => TryNormalize(scope, out _);

    /// <summary>Case-insensitive parse against <see cref="ReadingFeedbackScope"/>.
    /// Only the four scope <em>names</em> are accepted — numeric strings such as
    /// <c>"0"</c> (which <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// would otherwise bind to the underlying value) are rejected, so the stored
    /// column only ever holds <c>test</c> / <c>section</c> / <c>question</c> /
    /// <c>skill</c>.</summary>
    private static bool TryNormalize(string? scope, out string normalized)
    {
        var raw = (scope ?? string.Empty).Trim();
        if (raw.Length > 0
            && char.IsLetter(raw[0])
            && Enum.TryParse<ReadingFeedbackScope>(raw, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            normalized = parsed.ToString().ToLowerInvariant();
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}

public sealed record ReadingFeedbackDto(
    string Id,
    string ReadingAttemptId,
    string Scope,
    string? TargetRef,
    string AuthorUserId,
    string FeedbackText,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ReadingAssignmentCreateRequest(
    string AssignedToUserId,
    string PaperId,
    string Kind,
    string? ScopeJson,
    string? Note,
    DateTimeOffset? DueAt);

public sealed record ReadingAssignmentDto(
    string Id,
    string AssignedByUserId,
    string AssignedToUserId,
    string PaperId,
    string Kind,
    string? ScopeJson,
    string? Note,
    DateTimeOffset? DueAt,
    string? CompletedAttemptId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ReadingPrivilegedAttemptReview(
    string AttemptId,
    string PaperId,
    string PaperTitle,
    string UserId,
    string Status,
    string Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? SubmittedAt,
    int? GradedRawScore,
    int? GradedScaledScore,
    string GradedGradeLetter,
    int? EffectiveRawScore,
    int? EffectiveScaledScore,
    string EffectiveGradeLetter,
    bool HasOverride,
    int? OverrideRaw,
    int? OverrideScaled,
    string? OverrideReason,
    string? OverriddenByUserId,
    DateTimeOffset? OverriddenAt,
    int MaxRawScore,
    IReadOnlyList<ReadingPrivilegedSection> Sections,
    IReadOnlyList<ReadingPrivilegedQuestion> Questions,
    IReadOnlyList<string> FlaggedQuestionIds);

public sealed record ReadingPrivilegedSection(
    string PartCode,
    int RawScore,
    int MaxRawScore,
    double? AccuracyPercent,
    int CorrectCount,
    int IncorrectCount,
    int UnansweredCount);

public sealed record ReadingPrivilegedQuestion(
    string QuestionId,
    string PartCode,
    int DisplayOrder,
    string QuestionType,
    string Stem,
    string? SkillTag,
    object? UserAnswer,
    bool? IsCorrect,
    int PointsEarned,
    int MaxPoints,
    object? CorrectAnswer,
    string? ExplanationMarkdown,
    IReadOnlyList<string> AcceptedSynonyms,
    string? SelectedDistractorCategory,
    object? DistractorRationale,
    string? MissReason,
    bool FlaggedForReview,
    int? ElapsedMs,
    int? TotalElapsedMs,
    int AnswerRevisionCount);

public sealed class ReadingTutorService(
    LearnerDbContext db,
    IReadingGradingService grader,
    ILogger<ReadingTutorService> logger) : IReadingTutorService
{
    // ── Manual override ────────────────────────────────────────────────────

    public async Task<ReadingPrivilegedAttemptReview?> ApplyScoreOverrideAsync(
        string attemptId, ReadingScoreOverrideRequest request, string actorUserId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return null;

        var oldRaw = attempt.ScoreOverrideRaw;
        var oldScaled = attempt.ScoreOverrideScaled;

        // Scaled resolution: an explicit scaled value is range-checked but
        // stored as given; otherwise derive it from the raw score via the
        // canonical OET conversion (never inline a threshold here).
        int? scaledToStore;
        if (request.ScaledScore.HasValue)
            scaledToStore = Math.Clamp(request.ScaledScore.Value, 0, 500);
        else if (request.RawScore.HasValue)
            scaledToStore = OetScoring.OetRawToScaled(request.RawScore.Value);
        else
            scaledToStore = null;

        attempt.ScoreOverrideRaw = request.RawScore;
        attempt.ScoreOverrideScaled = scaledToStore;
        attempt.ScoreOverrideReason = request.Reason;
        attempt.OverriddenByUserId = actorUserId;
        attempt.OverriddenAt = DateTimeOffset.UtcNow;
        attempt.RowVersion++;

        AddAudit(actorUserId, "ReadingAttemptScoreOverridden", "ReadingAttempt", attempt.Id,
            $"oldRaw={Fmt(oldRaw)} oldScaled={Fmt(oldScaled)} newRaw={Fmt(request.RawScore)} newScaled={Fmt(scaledToStore)}");

        await db.SaveChangesAsync(ct);
        return await GetPrivilegedReviewAsync(attemptId, ct);
    }

    public async Task<ReadingPrivilegedAttemptReview?> ClearScoreOverrideAsync(
        string attemptId, string actorUserId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return null;

        var oldRaw = attempt.ScoreOverrideRaw;
        var oldScaled = attempt.ScoreOverrideScaled;

        attempt.ScoreOverrideRaw = null;
        attempt.ScoreOverrideScaled = null;
        attempt.ScoreOverrideReason = null;
        attempt.OverriddenByUserId = null;
        attempt.OverriddenAt = null;
        attempt.RowVersion++;

        AddAudit(actorUserId, "ReadingAttemptScoreOverrideCleared", "ReadingAttempt", attempt.Id,
            $"oldRaw={Fmt(oldRaw)} oldScaled={Fmt(oldScaled)}");

        await db.SaveChangesAsync(ct);
        return await GetPrivilegedReviewAsync(attemptId, ct);
    }

    // ── Accepted-answer recalculation ──────────────────────────────────────

    public async Task<ReadingRecalcResult> RecalcAsync(
        string paperId, ReadingRecalcRequest request, string actorUserId, CancellationToken ct)
    {
        var scope = (request.Scope ?? string.Empty).Trim();
        List<ReadingAttempt> targets;
        if (string.Equals(scope, "thisAttempt", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.AttemptId))
                throw new InvalidOperationException("attemptId is required when scope is 'thisAttempt'.");
            targets = await db.ReadingAttempts
                .Where(a => a.Id == request.AttemptId
                    && a.PaperId == paperId
                    && a.Status == ReadingAttemptStatus.Submitted)
                .ToListAsync(ct);
        }
        else if (string.Equals(scope, "allAttemptsForPaper", StringComparison.OrdinalIgnoreCase))
        {
            targets = await db.ReadingAttempts
                .Where(a => a.PaperId == paperId && a.Status == ReadingAttemptStatus.Submitted)
                .ToListAsync(ct);
        }
        else
        {
            throw new InvalidOperationException("scope must be 'thisAttempt' or 'allAttemptsForPaper'.");
        }

        var recalculated = 0;
        var skipped = 0;
        foreach (var attempt in targets)
        {
            // Never clobber a human override.
            if (attempt.ScoreOverrideRaw.HasValue || attempt.ScoreOverrideScaled.HasValue)
            {
                skipped++;
                continue;
            }

            var result = await grader.RegradeSubmittedAsync(attempt.Id, ct);
            if (result is null) continue;
            recalculated++;
            AddAudit(actorUserId, "ReadingAttemptRecalculated", "ReadingAttempt", attempt.Id,
                $"paperId={paperId} raw={result.RawScore}/{result.MaxRawScore} scaled={Fmt(result.ScaledScore)}");
        }

        AddAudit(actorUserId, "ReadingPaperRecalculated", "ContentPaper", paperId,
            $"scope={scope} recalculated={recalculated} skippedOverride={skipped} considered={targets.Count}");
        await db.SaveChangesAsync(ct);

        return new ReadingRecalcResult(recalculated, skipped, targets.Count);
    }

    // ── Privileged (non-redacted) attempt review ───────────────────────────

    public async Task<ReadingPrivilegedAttemptReview?> GetPrivilegedReviewAsync(
        string attemptId, CancellationToken ct)
    {
        var attempt = await db.ReadingAttempts.AsNoTracking()
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return null;

        var paper = await db.ContentPapers.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == attempt.PaperId, ct);

        var parts = await db.ReadingParts.AsNoTracking()
            .Where(p => p.PaperId == attempt.PaperId)
            .Include(p => p.Questions.OrderBy(q => q.DisplayOrder))
            .OrderBy(p => p.PartCode)
            .ToListAsync(ct);

        var answersByQuestion = attempt.Answers.ToDictionary(a => a.ReadingQuestionId, StringComparer.Ordinal);

        var revisionCounts = await db.ReadingAnswerRevisions.AsNoTracking()
            .Where(r => r.ReadingAttemptId == attemptId)
            .GroupBy(r => r.ReadingQuestionId)
            .Select(g => new { QuestionId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var revisionCountByQuestion = revisionCounts.ToDictionary(x => x.QuestionId, x => x.Count, StringComparer.Ordinal);

        var questions = new List<ReadingPrivilegedQuestion>();
        var sections = new List<ReadingPrivilegedSection>();
        var flagged = new List<string>();

        foreach (var part in parts)
        {
            var partCode = part.PartCode.ToString();
            int rawScore = 0, maxRaw = 0, correct = 0, incorrect = 0, unanswered = 0;

            foreach (var q in part.Questions.OrderBy(q => q.DisplayOrder))
            {
                maxRaw += q.Points;
                answersByQuestion.TryGetValue(q.Id, out var answer);

                if (answer is null) unanswered++;
                else if (answer.IsCorrect == true) { correct++; rawScore += answer.PointsEarned; }
                else incorrect++;

                if (answer?.FlaggedForReview == true) flagged.Add(q.Id);

                questions.Add(new ReadingPrivilegedQuestion(
                    QuestionId: q.Id,
                    PartCode: partCode,
                    DisplayOrder: q.DisplayOrder,
                    QuestionType: q.QuestionType.ToString(),
                    Stem: q.Stem,
                    SkillTag: q.SkillTag,
                    UserAnswer: ParseJson(answer?.UserAnswerJson),
                    IsCorrect: answer?.IsCorrect,
                    PointsEarned: answer?.PointsEarned ?? 0,
                    MaxPoints: q.Points,
                    CorrectAnswer: ParseJson(q.CorrectAnswerJson),
                    ExplanationMarkdown: q.ExplanationMarkdown,
                    AcceptedSynonyms: ParseStringArray(q.AcceptedSynonymsJson),
                    SelectedDistractorCategory: answer?.SelectedDistractorCategory?.ToString(),
                    DistractorRationale: ParseJson(q.DistractorRationaleJson),
                    MissReason: answer?.MissReason,
                    FlaggedForReview: answer?.FlaggedForReview ?? false,
                    ElapsedMs: answer?.ElapsedMs,
                    TotalElapsedMs: answer?.TotalElapsedMs,
                    AnswerRevisionCount: revisionCountByQuestion.GetValueOrDefault(q.Id, 0)));
            }

            sections.Add(new ReadingPrivilegedSection(
                PartCode: partCode,
                RawScore: rawScore,
                MaxRawScore: maxRaw,
                AccuracyPercent: maxRaw > 0
                    ? Math.Round(100.0 * correct / part.Questions.Count, 1, MidpointRounding.AwayFromZero)
                    : null,
                CorrectCount: correct,
                IncorrectCount: incorrect,
                UnansweredCount: unanswered));
        }

        var hasOverride = attempt.ScoreOverrideRaw.HasValue || attempt.ScoreOverrideScaled.HasValue;
        var effectiveRaw = hasOverride ? attempt.ScoreOverrideRaw : attempt.RawScore;
        var effectiveScaled = hasOverride ? attempt.ScoreOverrideScaled : attempt.ScaledScore;

        return new ReadingPrivilegedAttemptReview(
            AttemptId: attempt.Id,
            PaperId: attempt.PaperId,
            PaperTitle: paper?.Title ?? "Unknown paper",
            UserId: attempt.UserId,
            Status: attempt.Status.ToString(),
            Mode: attempt.Mode.ToString(),
            StartedAt: attempt.StartedAt,
            SubmittedAt: attempt.SubmittedAt,
            GradedRawScore: attempt.RawScore,
            GradedScaledScore: attempt.ScaledScore,
            GradedGradeLetter: attempt.ScaledScore is int gs ? OetScoring.OetGradeLetterFromScaled(gs) : "—",
            EffectiveRawScore: effectiveRaw,
            EffectiveScaledScore: effectiveScaled,
            EffectiveGradeLetter: effectiveScaled is int es ? OetScoring.OetGradeLetterFromScaled(es) : "—",
            HasOverride: hasOverride,
            OverrideRaw: attempt.ScoreOverrideRaw,
            OverrideScaled: attempt.ScoreOverrideScaled,
            OverrideReason: attempt.ScoreOverrideReason,
            OverriddenByUserId: attempt.OverriddenByUserId,
            OverriddenAt: attempt.OverriddenAt,
            MaxRawScore: attempt.MaxRawScore,
            Sections: sections,
            Questions: questions,
            FlaggedQuestionIds: flagged);
    }

    // ── Feedback CRUD ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ReadingFeedbackDto>> ListFeedbackAsync(string attemptId, CancellationToken ct)
    {
        return await db.ReadingAttemptFeedbacks.AsNoTracking()
            .Where(f => f.ReadingAttemptId == attemptId)
            .OrderBy(f => f.CreatedAt)
            .Select(f => new ReadingFeedbackDto(
                f.Id, f.ReadingAttemptId, f.Scope, f.TargetRef, f.AuthorUserId, f.FeedbackText, f.CreatedAt, f.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<ReadingFeedbackDto?> CreateFeedbackAsync(
        string attemptId, ReadingFeedbackRequest request, string actorUserId, CancellationToken ct)
    {
        var attemptExists = await db.ReadingAttempts.AsNoTracking().AnyAsync(a => a.Id == attemptId, ct);
        if (!attemptExists) return null;

        var now = DateTimeOffset.UtcNow;
        var feedback = new ReadingAttemptFeedback
        {
            Id = Guid.NewGuid().ToString("N"),
            ReadingAttemptId = attemptId,
            Scope = ReadingFeedbackScopeExtensions.NormalizeScope(request.Scope),
            TargetRef = request.TargetRef,
            AuthorUserId = actorUserId,
            FeedbackText = request.FeedbackText,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ReadingAttemptFeedbacks.Add(feedback);
        AddAudit(actorUserId, "ReadingAttemptFeedbackCreated", "ReadingAttemptFeedback", feedback.Id,
            $"attemptId={attemptId} scope={feedback.Scope}");
        await db.SaveChangesAsync(ct);

        return new ReadingFeedbackDto(
            feedback.Id, feedback.ReadingAttemptId, feedback.Scope, feedback.TargetRef,
            feedback.AuthorUserId, feedback.FeedbackText, feedback.CreatedAt, feedback.UpdatedAt);
    }

    public async Task<ReadingFeedbackDto?> UpdateFeedbackAsync(
        string attemptId, string feedbackId, ReadingFeedbackRequest request, string actorUserId, CancellationToken ct)
    {
        var feedback = await db.ReadingAttemptFeedbacks
            .FirstOrDefaultAsync(f => f.Id == feedbackId && f.ReadingAttemptId == attemptId, ct);
        if (feedback is null) return null;

        feedback.Scope = ReadingFeedbackScopeExtensions.NormalizeScope(request.Scope);
        feedback.TargetRef = request.TargetRef;
        feedback.FeedbackText = request.FeedbackText;
        feedback.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "ReadingAttemptFeedbackUpdated", "ReadingAttemptFeedback", feedback.Id,
            $"attemptId={attemptId} scope={feedback.Scope}");
        await db.SaveChangesAsync(ct);

        return new ReadingFeedbackDto(
            feedback.Id, feedback.ReadingAttemptId, feedback.Scope, feedback.TargetRef,
            feedback.AuthorUserId, feedback.FeedbackText, feedback.CreatedAt, feedback.UpdatedAt);
    }

    public async Task<bool> DeleteFeedbackAsync(string attemptId, string feedbackId, string actorUserId, CancellationToken ct)
    {
        var feedback = await db.ReadingAttemptFeedbacks
            .FirstOrDefaultAsync(f => f.Id == feedbackId && f.ReadingAttemptId == attemptId, ct);
        if (feedback is null) return false;

        db.ReadingAttemptFeedbacks.Remove(feedback);
        AddAudit(actorUserId, "ReadingAttemptFeedbackDeleted", "ReadingAttemptFeedback", feedback.Id,
            $"attemptId={attemptId}");
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Assignment workflow ────────────────────────────────────────────────

    public async Task<ReadingAssignmentDto?> CreateAssignmentAsync(
        ReadingAssignmentCreateRequest request, string actorUserId, CancellationToken ct)
    {
        var paperExists = await db.ContentPapers.AsNoTracking()
            .AnyAsync(p => p.Id == request.PaperId && p.SubtestCode == "reading", ct);
        if (!paperExists)
            throw new InvalidOperationException("Reading paper not found.");

        var now = DateTimeOffset.UtcNow;
        var assignment = new ReadingAssignment
        {
            Id = Guid.NewGuid().ToString("N"),
            AssignedByUserId = actorUserId,
            AssignedToUserId = request.AssignedToUserId,
            PaperId = request.PaperId,
            Kind = string.IsNullOrWhiteSpace(request.Kind) ? "full" : request.Kind.Trim(),
            ScopeJson = request.ScopeJson,
            Note = request.Note,
            DueAt = request.DueAt,
            Status = "assigned",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ReadingAssignments.Add(assignment);
        AddAudit(actorUserId, "ReadingAssignmentCreated", "ReadingAssignment", assignment.Id,
            $"assignedTo={request.AssignedToUserId} paperId={request.PaperId} kind={assignment.Kind}");
        await db.SaveChangesAsync(ct);

        return ToDto(assignment);
    }

    public async Task<IReadOnlyList<ReadingAssignmentDto>> ListAssignmentsAsync(string? assignedToUserId, CancellationToken ct)
    {
        var query = db.ReadingAssignments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(assignedToUserId))
            query = query.Where(a => a.AssignedToUserId == assignedToUserId);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ReadingAssignmentDto(
                a.Id, a.AssignedByUserId, a.AssignedToUserId, a.PaperId, a.Kind, a.ScopeJson,
                a.Note, a.DueAt, a.CompletedAttemptId, a.Status, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReadingAssignmentDto>> ListAssignmentsForExpertAsync(
        string expertUserId, string? assignedToUserId, CancellationToken ct)
    {
        var query = db.ReadingAssignments.AsNoTracking()
            .Where(a => a.AssignedByUserId == expertUserId)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(assignedToUserId))
            query = query.Where(a => a.AssignedToUserId == assignedToUserId);

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ReadingAssignmentDto(
                a.Id, a.AssignedByUserId, a.AssignedToUserId, a.PaperId, a.Kind, a.ScopeJson,
                a.Note, a.DueAt, a.CompletedAttemptId, a.Status, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> CancelAssignmentAsync(string assignmentId, string actorUserId, CancellationToken ct)
    {
        var assignment = await db.ReadingAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
        if (assignment is null) return false;

        assignment.Status = "cancelled";
        assignment.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(actorUserId, "ReadingAssignmentCancelled", "ReadingAssignment", assignment.Id,
            $"assignedTo={assignment.AssignedToUserId} paperId={assignment.PaperId}");
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ReadingAssignmentDto>> ListActiveAssignmentsForLearnerAsync(string userId, CancellationToken ct)
    {
        return await db.ReadingAssignments.AsNoTracking()
            .Where(a => a.AssignedToUserId == userId && a.Status == "assigned")
            .OrderBy(a => a.DueAt == null)
            .ThenBy(a => a.DueAt)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new ReadingAssignmentDto(
                a.Id, a.AssignedByUserId, a.AssignedToUserId, a.PaperId, a.Kind, a.ScopeJson,
                a.Note, a.DueAt, a.CompletedAttemptId, a.Status, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> CanExpertAccessAttemptAsync(string attemptId, string expertUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expertUserId)) return false;

        return await (
            from attempt in db.ReadingAttempts.AsNoTracking()
            join assignment in db.ReadingAssignments.AsNoTracking()
                on new { attempt.UserId, attempt.PaperId }
                equals new { UserId = assignment.AssignedToUserId, assignment.PaperId }
            where attempt.Id == attemptId
                && attempt.Status == ReadingAttemptStatus.Submitted
                && assignment.AssignedByUserId == expertUserId
                && assignment.CompletedAttemptId == attempt.Id
                && assignment.Status != "cancelled"
            select attempt.Id)
            .AnyAsync(ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ReadingAssignmentDto ToDto(ReadingAssignment a) => new(
        a.Id, a.AssignedByUserId, a.AssignedToUserId, a.PaperId, a.Kind, a.ScopeJson,
        a.Note, a.DueAt, a.CompletedAttemptId, a.Status, a.CreatedAt, a.UpdatedAt);

    private static string Fmt(int? value) => value?.ToString() ?? "null";

    private void AddAudit(string actorUserId, string action, string resourceType, string resourceId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = actorUserId,
            ActorName = actorUserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
        });
    }

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
