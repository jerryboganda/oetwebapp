using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Grammar;

/// <summary>
/// Fans wrong grammar answers out to the spaced-repetition queue so the
/// learner is re-exposed to the same concept at growing intervals.
/// </summary>
public interface IGrammarReviewFanOut
{
    Task<int> CreateReviewItemsAsync(string userId, GrammarLesson lesson, GrammarGradingResult result, CancellationToken ct);
}

public sealed class GrammarReviewFanOut(LearnerDbContext db) : IGrammarReviewFanOut
{
    public async Task<int> CreateReviewItemsAsync(string userId, GrammarLesson lesson, GrammarGradingResult result, CancellationToken ct)
    {
        int created = 0;
        foreach (var ex in result.Exercises)
        {
            if (ex.IsCorrect) continue;

            // Idempotent: skip if we've already created a ReviewItem for this exercise.
            var existing = await db.ReviewItems.FirstOrDefaultAsync(
                r => r.UserId == userId && r.SourceType == "grammar_error" && r.SourceId == ex.ExerciseId, ct);
            if (existing is not null)
            {
                // Reset to due today if mastered → backslide.
                existing.Status = "active";
                existing.DueDate = DateOnly.FromDateTime(DateTime.UtcNow);
                existing.ConsecutiveCorrect = 0;
                continue;
            }

            var question = new
            {
                lessonId = lesson.Id,
                lessonTitle = lesson.Title,
                exerciseId = ex.ExerciseId,
                type = ex.Type,
                userAnswer = ex.UserAnswer,
            };
            var answer = new
            {
                correctAnswer = ex.CorrectAnswer,
                explanationMarkdown = ex.ExplanationMarkdown,
            };

            db.ReviewItems.Add(new ReviewItem
            {
                Id = $"ri-{Guid.NewGuid():N}",
                UserId = userId,
                ExamTypeCode = lesson.ExamTypeCode,
                SourceType = "grammar_error",
                SourceId = ex.ExerciseId,
                SubtestCode = "grammar",
                CriterionCode = lesson.Category,
                QuestionJson = JsonSerializer.Serialize(question),
                AnswerJson = JsonSerializer.Serialize(answer),
                EaseFactor = 2.5,
                IntervalDays = 1,
                ReviewCount = 0,
                ConsecutiveCorrect = 0,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
                CreatedAt = DateTimeOffset.UtcNow,
                Status = "active",
            });
            created++;
        }

        if (created > 0) await db.SaveChangesAsync(ct);
        return created;
    }
}
