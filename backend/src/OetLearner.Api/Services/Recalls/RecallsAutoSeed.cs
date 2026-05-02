using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Auto-seeds Recalls cards from sources outside the recalls module — most
/// importantly, wrong free-text answers in Listening drills. The seeded
/// <see cref="ReviewItem"/>s land in the same SM-2 queue surfaced by
/// <c>/v1/recalls/queue</c> with <c>Starred=true, StarReason="hearing"</c>.
/// </summary>
public interface IRecallsAutoSeed
{
    Task SeedFromListeningAsync(
        string userId,
        string attemptId,
        IEnumerable<RecallsListeningSeedItem> wrongItems,
        CancellationToken ct);
}

/// <summary>One free-text wrong answer from a listening attempt.</summary>
public sealed record RecallsListeningSeedItem(
    string QuestionId,
    string Type,           // "short_answer" | "fill_blank" | "gap_fill" | … (mcq filtered out by caller)
    string Prompt,
    string LearnerAnswer,
    string CorrectAnswer);

public sealed class RecallsAutoSeed(LearnerDbContext db) : IRecallsAutoSeed
{
    private static readonly HashSet<string> SeedableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "short_answer", "shortanswer", "fill_blank", "fillblank", "fill_in_blank",
        "gap_fill", "gapfill", "note_completion", "form_completion",
    };

    public async Task SeedFromListeningAsync(
        string userId,
        string attemptId,
        IEnumerable<RecallsListeningSeedItem> wrongItems,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(attemptId)) return;

        var seeds = wrongItems
            .Where(i => SeedableTypes.Contains(i.Type) && !string.IsNullOrWhiteSpace(i.CorrectAnswer))
            .ToList();
        if (seeds.Count == 0) return;

        var sourceIds = seeds
            .Select(i => $"listening:{attemptId}:{i.QuestionId}")
            .ToList();

        var existing = await db.ReviewItems
            .Where(r => r.UserId == userId && r.SourceType == "listening" && r.SourceId != null && sourceIds.Contains(r.SourceId!))
            .Select(r => r.SourceId!)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var added = 0;
        foreach (var seed in seeds)
        {
            var sourceId = $"listening:{attemptId}:{seed.QuestionId}";
            if (existingSet.Contains(sourceId)) continue;

            var item = new ReviewItem
            {
                Id = $"ri-{Guid.NewGuid():N}",
                UserId = userId,
                ExamTypeCode = "OET",
                SourceType = "listening",
                SourceId = sourceId,
                SubtestCode = "listening",
                CriterionCode = "detail_capture",
                QuestionJson = JsonSerializer.Serialize(new
                {
                    prompt = seed.Prompt,
                    type = seed.Type,
                    questionId = seed.QuestionId,
                    attemptId,
                }),
                AnswerJson = JsonSerializer.Serialize(new
                {
                    correct = seed.CorrectAnswer,
                    learner = seed.LearnerAnswer,
                }),
                EaseFactor = 2.5,
                IntervalDays = 1,
                ReviewCount = 0,
                ConsecutiveCorrect = 0,
                DueDate = today,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = "active",
                Starred = true,
                StarReason = "hearing",
            };
            db.ReviewItems.Add(item);
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
    }
}

public sealed class NoopRecallsAutoSeed : IRecallsAutoSeed
{
    public Task SeedFromListeningAsync(
        string userId, string attemptId,
        IEnumerable<RecallsListeningSeedItem> wrongItems, CancellationToken ct) => Task.CompletedTask;
}
