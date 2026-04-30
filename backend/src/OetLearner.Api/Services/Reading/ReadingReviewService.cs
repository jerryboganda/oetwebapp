using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Reading Review Service — Phase 4
//
// Handles the two authoring extensions added in Phase 4:
//   1. Distractor metadata for MCQ questions
//      (admin tags each non-correct option with a ReadingDistractorCategory)
//   2. Per-question review-state lifecycle
//      (Draft → AcademicReview → MedicalReview → LanguageReview → Pilot
//       → Published → Retired)
//
// Both surfaces write AuditEvents and (for review states) also append to
// ReadingQuestionReviewLog so admin can render a full history pane.
// ═════════════════════════════════════════════════════════════════════════════

public interface IReadingReviewService
{
    /// <summary>Replace the distractor map for a question.</summary>
    Task<ReadingQuestion> SetDistractorsAsync(
        string questionId,
        IReadOnlyDictionary<string, ReadingDistractorCategory> distractors,
        string adminId,
        CancellationToken ct);

    /// <summary>Advance a question to a new review state. Validates the
    /// transition. Appends a log entry. Persists the latest reviewer note
    /// on the question for fast list rendering.</summary>
    Task<ReadingReviewTransitionResult> TransitionStateAsync(
        ReadingReviewTransitionArgs args,
        CancellationToken ct);

    /// <summary>Full transition history (newest first).</summary>
    Task<IReadOnlyList<ReadingReviewLogEntry>> GetHistoryAsync(string questionId, CancellationToken ct);
}

public sealed record ReadingReviewTransitionArgs(
    string QuestionId,
    ReadingReviewState ToState,
    string ReviewerUserId,
    string? ReviewerDisplayName,
    string? Note,
    bool IsAdminOverride = false);

public sealed record ReadingReviewTransitionResult(
    string QuestionId,
    ReadingReviewState FromState,
    ReadingReviewState ToState,
    DateTimeOffset TransitionedAt);

public sealed record ReadingReviewLogEntry(
    string Id,
    ReadingReviewState FromState,
    ReadingReviewState ToState,
    string ReviewerUserId,
    string? ReviewerDisplayName,
    string? Note,
    DateTimeOffset TransitionedAt);

public sealed class ReadingReviewService(LearnerDbContext db) : IReadingReviewService
{
    /// <summary>
    /// Linear forward state machine. Any state can also retire (terminal)
    /// or be force-rolled back to Draft when <c>IsAdminOverride</c> is set.
    /// </summary>
    private static readonly IReadOnlyDictionary<ReadingReviewState, ReadingReviewState[]> AllowedTransitions =
        new Dictionary<ReadingReviewState, ReadingReviewState[]>
        {
            [ReadingReviewState.Draft] = new[] { ReadingReviewState.AcademicReview },
            [ReadingReviewState.AcademicReview] = new[] { ReadingReviewState.MedicalReview, ReadingReviewState.Draft },
            [ReadingReviewState.MedicalReview] = new[] { ReadingReviewState.LanguageReview, ReadingReviewState.AcademicReview },
            [ReadingReviewState.LanguageReview] = new[] { ReadingReviewState.Pilot, ReadingReviewState.MedicalReview },
            [ReadingReviewState.Pilot] = new[] { ReadingReviewState.Published, ReadingReviewState.LanguageReview },
            [ReadingReviewState.Published] = new[] { ReadingReviewState.Retired },
            [ReadingReviewState.Retired] = Array.Empty<ReadingReviewState>(),
        };

    public async Task<ReadingQuestion> SetDistractorsAsync(
        string questionId,
        IReadOnlyDictionary<string, ReadingDistractorCategory> distractors,
        string adminId,
        CancellationToken ct)
    {
        var q = await db.ReadingQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct)
            ?? throw new InvalidOperationException("Question not found.");

        if (q.QuestionType != ReadingQuestionType.MultipleChoice3
            && q.QuestionType != ReadingQuestionType.MultipleChoice4)
            throw new InvalidOperationException("Distractor metadata is only supported on multiple-choice questions.");

        // Normalise keys to single uppercase letters; reject anything that
        // doesn't match the question's option count.
        var allowedKeys = q.QuestionType == ReadingQuestionType.MultipleChoice3
            ? new[] { "A", "B", "C" }
            : new[] { "A", "B", "C", "D" };
        var correctKey = JsonSerializer.Deserialize<string>(q.CorrectAnswerJson)?.Trim().ToUpperInvariant();

        var normalised = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawKey, cat) in distractors)
        {
            var key = rawKey?.Trim().ToUpperInvariant() ?? "";
            if (!allowedKeys.Contains(key))
                throw new InvalidOperationException($"Distractor key '{rawKey}' is not valid for this question.");
            if (string.Equals(key, correctKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Cannot tag the correct answer with a distractor category.");
            normalised[key] = cat.ToString();
        }
        q.OptionDistractorsJson = normalised.Count == 0
            ? null
            : JsonSerializer.Serialize(normalised);
        q.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorId = adminId,
            ActorName = adminId,
            Action = "ReadingQuestionDistractorsUpdated",
            ResourceType = "ReadingQuestion",
            ResourceId = q.Id,
            Details = $"keys={normalised.Count}",
        });
        await db.SaveChangesAsync(ct);
        return q;
    }

    public async Task<ReadingReviewTransitionResult> TransitionStateAsync(
        ReadingReviewTransitionArgs args,
        CancellationToken ct)
    {
        var q = await db.ReadingQuestions.FirstOrDefaultAsync(x => x.Id == args.QuestionId, ct)
            ?? throw new InvalidOperationException("Question not found.");

        var from = q.ReviewState;
        var to = args.ToState;
        if (from == to)
            throw new InvalidOperationException($"Question is already in {to}.");

        var allowed = AllowedTransitions.TryGetValue(from, out var nexts) ? nexts : Array.Empty<ReadingReviewState>();
        var isExplicitlyAllowed = allowed.Contains(to);
        var isAdminRollback = args.IsAdminOverride && to == ReadingReviewState.Draft;
        if (!isExplicitlyAllowed && !isAdminRollback)
        {
            throw new InvalidOperationException(
                $"Cannot transition Reading question from {from} to {to}.");
        }

        var now = DateTimeOffset.UtcNow;
        q.ReviewState = to;
        q.LatestReviewNote = args.Note;
        q.UpdatedAt = now;

        var log = new ReadingQuestionReviewLog
        {
            Id = Guid.NewGuid().ToString("N"),
            ReadingQuestionId = q.Id,
            FromState = from,
            ToState = to,
            ReviewerUserId = args.ReviewerUserId,
            ReviewerDisplayName = args.ReviewerDisplayName,
            Note = args.Note,
            TransitionedAt = now,
        };
        db.ReadingQuestionReviewLogs.Add(log);

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAt = now,
            ActorId = args.ReviewerUserId,
            ActorName = args.ReviewerDisplayName ?? args.ReviewerUserId,
            Action = "ReadingQuestionReviewTransitioned",
            ResourceType = "ReadingQuestion",
            ResourceId = q.Id,
            Details = $"{from}->{to} override={args.IsAdminOverride}",
        });
        await db.SaveChangesAsync(ct);

        return new ReadingReviewTransitionResult(q.Id, from, to, now);
    }

    public async Task<IReadOnlyList<ReadingReviewLogEntry>> GetHistoryAsync(string questionId, CancellationToken ct)
    {
        var rows = await db.ReadingQuestionReviewLogs.AsNoTracking()
            .Where(l => l.ReadingQuestionId == questionId)
            .OrderByDescending(l => l.TransitionedAt)
            .ToListAsync(ct);
        return rows.Select(l => new ReadingReviewLogEntry(
            l.Id, l.FromState, l.ToState, l.ReviewerUserId, l.ReviewerDisplayName,
            l.Note, l.TransitionedAt)).ToList();
    }
}
