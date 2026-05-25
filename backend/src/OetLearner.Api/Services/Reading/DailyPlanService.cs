using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Reading;

// ═════════════════════════════════════════════════════════════════════════════
// Daily Plan Service — WS3
//
// Generates the learner's daily study plan (up to 4 tasks/day) by applying
// priority rules from spec §10.3:
//   1. drill       — always, targeting the weakest sub-skill
//   2. vocab_review — if vocab cards are due today
//   3. wrong_review — if wrong-answer review queue ≥ 5 items
//   4. strategy_read — 2× per week max
//   5. lesson      — foundation stage + incomplete lesson
//   6. mock        — mastery stage + weekly mock not yet done
//
// Generation is idempotent: re-calling on a day with an existing plan is a no-op.
// CarryOverIncompleteAsync merges yesterday's pending items into today (cap 6).
// ═════════════════════════════════════════════════════════════════════════════

public interface IDailyPlanService
{
    Task GenerateTodayAsync(string userId, CancellationToken ct);
    Task<List<ReadingDailyPlanItem>> GetTodayPlanAsync(string userId, CancellationToken ct);
    Task MarkItemCompletedAsync(Guid itemId, string userId, CancellationToken ct);
    Task SkipItemAsync(Guid itemId, string userId, string reason, CancellationToken ct);
    Task CarryOverIncompleteAsync(string userId, CancellationToken ct);
}

public sealed class DailyPlanService(
    LearnerDbContext db,
    ISkillScoringService skillScoring,
    IStreakService streak) : IDailyPlanService
{
    private const int MaxTasksPerDay = 4;
    private const int CarryOverCap = 6;
    private static readonly int[] StrategyReadWeeklyMax = { 2 }; // effectively constant

    // ── Estimated minutes per item type ──────────────────────────────────────
    private static int MinutesFor(string itemType) => itemType switch
    {
        "drill"          => 15,
        "vocab_review"   => 10,
        "wrong_review"   => 10,
        "strategy_read"  => 5,
        "lesson"         => 30,
        "mock"           => 60,
        _                => 15,
    };

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateTodayAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task GenerateTodayAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Idempotency: skip if today's plan already exists
        var alreadyExists = await db.ReadingDailyPlanItems
            .AnyAsync(i => i.UserId == userId && i.PlanDate == today, ct);
        if (alreadyExists) return;

        // Learner profile — needed for stage-specific rules
        var profile = await db.LearnerReadingProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var stage = profile?.CurrentStage ?? "foundation";

        var tasks = new List<(string ItemType, string? FocusSkill, string PayloadJson)>();

        // ── Rule 1: always add a drill targeting the weakest sub-skill ────
        var weakestSkill = await skillScoring.GetWeakestSkillAsync(userId, ct);
        tasks.Add(("drill", weakestSkill, BuildDrillPayload(weakestSkill)));

        // ── Rule 2: vocab_review if cards are due today ───────────────────
        if (tasks.Count < MaxTasksPerDay)
        {
            var endOfToday = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var vocabDueCount = await db.LearnerVocabularyItems
                .CountAsync(v => v.UserId == userId
                    && v.NextReviewAt <= endOfToday, ct);
            if (vocabDueCount > 0)
                tasks.Add(("vocab_review", null, JsonSerializer.Serialize(new { count = vocabDueCount })));
        }

        // ── Rule 3: wrong_review if review queue ≥ 5 items ───────────────
        if (tasks.Count < MaxTasksPerDay)
        {
            var reviewQueueSize = await db.ReadingQuestionAttempts
                .CountAsync(a => a.UserId == userId
                    && a.InReviewQueue
                    && a.NextReviewAt != null
                    && a.NextReviewAt <= DateTimeOffset.UtcNow, ct);
            if (reviewQueueSize >= 5)
                tasks.Add(("wrong_review", null, JsonSerializer.Serialize(new { queueSize = reviewQueueSize })));
        }

        // ── Rule 4: strategy_read at most 2× per week ────────────────────
        if (tasks.Count < MaxTasksPerDay)
        {
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var weekEnd = weekStart.AddDays(7);
            var strategyReadsThisWeek = await db.ReadingDailyPlanItems
                .CountAsync(i => i.UserId == userId
                    && i.ItemType == "strategy_read"
                    && i.PlanDate >= weekStart
                    && i.PlanDate < weekEnd
                    && (i.Status == "completed" || i.Status == "in_progress"), ct);

            if (strategyReadsThisWeek < StrategyReadWeeklyMax[0])
            {
                // Pick the next unread published strategy
                var readStrategyIds = await db.ReadingStrategyProgresses
                    .Where(sp => sp.UserId == userId && sp.MarkedAsRead)
                    .Select(sp => sp.StrategyId)
                    .ToListAsync(ct);

                var nextStrategy = await db.ReadingStrategies
                    .AsNoTracking()
                    .Where(s => s.IsPublished && !readStrategyIds.Contains(s.Id))
                    .OrderBy(s => s.Difficulty)
                    .Select(s => new { s.Id, s.Slug, s.EstimatedReadMinutes })
                    .FirstOrDefaultAsync(ct);

                if (nextStrategy is not null)
                    tasks.Add(("strategy_read", null,
                        JsonSerializer.Serialize(new { strategyId = nextStrategy.Id, slug = nextStrategy.Slug })));
            }
        }

        // ── Rule 5: lesson if stage = "foundation" and incomplete lesson ──
        if (tasks.Count < MaxTasksPerDay && stage == "foundation")
        {
            var completedLessonIds = await db.LearnerLessonProgresses
                .Where(lp => lp.UserId == userId && lp.CompletedAt != null)
                .Select(lp => lp.LessonId)
                .ToListAsync(ct);

            var inProgressLesson = await db.LearnerLessonProgresses
                .AsNoTracking()
                .Where(lp => lp.UserId == userId && lp.CompletedAt == null)
                .OrderBy(lp => lp.LessonId)
                .Select(lp => lp.LessonId)
                .FirstOrDefaultAsync(ct);

            Guid? lessonId = inProgressLesson != default ? inProgressLesson : null;

            if (lessonId is null)
            {
                // Pick the first published lesson not yet started
                var nextLesson = await db.ReadingLessons
                    .AsNoTracking()
                    .Where(l => l.IsPublished && !completedLessonIds.Contains(l.Id))
                    .OrderBy(l => l.OrderIndex)
                    .Select(l => new { l.Id, l.SkillCode, l.Slug })
                    .FirstOrDefaultAsync(ct);

                if (nextLesson is not null)
                {
                    lessonId = nextLesson.Id;
                    tasks.Add(("lesson", nextLesson.SkillCode,
                        JsonSerializer.Serialize(new { lessonId = nextLesson.Id, slug = nextLesson.Slug })));
                }
            }
            else
            {
                var lesson = await db.ReadingLessons.AsNoTracking()
                    .Where(l => l.Id == lessonId.Value)
                    .Select(l => new { l.Id, l.SkillCode, l.Slug })
                    .FirstOrDefaultAsync(ct);
                if (lesson is not null)
                    tasks.Add(("lesson", lesson.SkillCode,
                        JsonSerializer.Serialize(new { lessonId = lesson.Id, slug = lesson.Slug })));
            }
        }

        // ── Rule 6: mock if stage = "mastery" and no mock done this week ──
        if (tasks.Count < MaxTasksPerDay && stage == "mastery")
        {
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var weekEnd = weekStart.AddDays(7);
            var mockDoneThisWeek = await db.ReadingDailyPlanItems
                .AnyAsync(i => i.UserId == userId
                    && i.ItemType == "mock"
                    && i.PlanDate >= weekStart
                    && i.PlanDate < weekEnd
                    && i.Status == "completed", ct);

            if (!mockDoneThisWeek)
            {
                var template = await db.ReadingMockTemplates
                    .AsNoTracking()
                    .Where(t => t.IsPublished)
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => new { t.Id, t.Title })
                    .FirstOrDefaultAsync(ct);

                if (template is not null)
                    tasks.Add(("mock", null,
                        JsonSerializer.Serialize(new { mockTemplateId = template.Id, title = template.Title })));
            }
        }

        // ── Write plan items ──────────────────────────────────────────────
        for (var i = 0; i < tasks.Count && i < MaxTasksPerDay; i++)
        {
            var (itemType, focusSkill, payloadJson) = tasks[i];
            db.ReadingDailyPlanItems.Add(new ReadingDailyPlanItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanDate = today,
                Ordinal = i,
                ItemType = itemType,
                FocusSkill = focusSkill,
                EstimatedMinutes = MinutesFor(itemType),
                PayloadJson = payloadJson,
                Status = "pending",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetTodayPlanAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<List<ReadingDailyPlanItem>> GetTodayPlanAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.ReadingDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == today)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MarkItemCompletedAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task MarkItemCompletedAsync(Guid itemId, string userId, CancellationToken ct)
    {
        var item = await db.ReadingDailyPlanItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct);
        if (item is null) return;

        item.Status = "completed";
        item.CompletedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        // Record streak activity on completion (0 questions here — streak counting is
        // driven by practice-session completions; this call advances the daily activity
        // record so the streak worker sees the learner was active today).
        await streak.RecordActivityAsync(userId, questionsAnswered: 0, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SkipItemAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task SkipItemAsync(Guid itemId, string userId, string reason, CancellationToken ct)
    {
        var item = await db.ReadingDailyPlanItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct);
        if (item is null) return;

        item.Status = "skipped";

        // Embed skip reason in payload
        try
        {
            var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(item.PayloadJson)
                           ?? new Dictionary<string, object>();
            existing["skipReason"] = reason;
            item.PayloadJson = JsonSerializer.Serialize(existing);
        }
        catch (JsonException)
        {
            item.PayloadJson = JsonSerializer.Serialize(new { skipReason = reason });
        }

        await db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CarryOverIncompleteAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task CarryOverIncompleteAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        // Load yesterday's pending items
        var yesterdayPending = await db.ReadingDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == yesterday && i.Status == "pending")
            .ToListAsync(ct);

        if (yesterdayPending.Count == 0) return;

        // Load today's existing items (to avoid duplicates and enforce cap)
        var todayItems = await db.ReadingDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == today)
            .ToListAsync(ct);

        var existingTypes = todayItems
            .Select(i => i.ItemType + "|" + (i.FocusSkill ?? ""))
            .ToHashSet();

        var nextOrdinal = todayItems.Count > 0 ? todayItems.Max(i => i.Ordinal) + 1 : 0;
        var combinedCount = todayItems.Count;

        foreach (var old in yesterdayPending)
        {
            if (combinedCount >= CarryOverCap) break;

            // Deduplicate by type + focus skill
            var key = old.ItemType + "|" + (old.FocusSkill ?? "");
            if (existingTypes.Contains(key)) continue;

            db.ReadingDailyPlanItems.Add(new ReadingDailyPlanItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanDate = today,
                Ordinal = nextOrdinal++,
                ItemType = old.ItemType,
                FocusSkill = old.FocusSkill,
                EstimatedMinutes = old.EstimatedMinutes,
                PayloadJson = old.PayloadJson,
                Status = "pending",
            });

            existingTypes.Add(key);
            combinedCount++;
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildDrillPayload(string? skillCode)
        => JsonSerializer.Serialize(new { skill = skillCode ?? "S1" });
}
