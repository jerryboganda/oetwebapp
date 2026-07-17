using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;

namespace OetWithDrHesham.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Daily Plan Service — Listening Module Pathway Phase 3 (§8, §10)
//
// Generates the learner's daily Listening study plan (cap 4 items/day per spec)
// from learner profile + skill scores + accent scores + pronunciation review
// queue + wrong-review queue. Mirrors the Reading equivalent
// (Services/Reading/DailyPlanService.cs) in shape and conventions so audits
// against Reading translate without rework.
//
// Composition rules (§8.1, §10):
//   1. drill                — always, FocusSkill = weakest L1..L8     ~15–20 min
//   2. accent_drill         — 2–3× per week, FocusAccent = weakest    ~8 min
//   3. dictation            — 2× per week                             ~6 min
//   4. pronunciation_review — daily, if any SM-2 cards due             ~6 min
//   5. wrong_review         — daily, if any review-queue items due     ~5 min
//   6. strategy_read        — 2× per week (optional)                   ~3 min
//
// Generation is idempotent: re-calling on a day with an existing plan is a no-op.
// All four item-mutation endpoints (start/complete/skip) enforce cross-user 404
// protection by filtering on UserId + Id at the database boundary.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Phase 3 listening daily plan engine — composes a learner's
/// up-to-4-item study queue and tracks item lifecycle transitions.</summary>
public interface IListeningDailyPlanService
{
    /// <summary>Return all plan items for the learner on the specified date,
    /// ordered by <see cref="ListeningDailyPlanItem.Ordinal"/>. Does NOT
    /// generate anything — call <see cref="GenerateForTodayIfMissingAsync"/>
    /// first when the dashboard needs a fresh plan.</summary>
    Task<IReadOnlyList<ListeningDailyPlanItem>> GetTodayAsync(string userId, DateOnly today, CancellationToken ct);

    /// <summary>Transition <paramref name="itemId"/> from <c>pending</c> to
    /// <c>in_progress</c> and stamp <see cref="ListeningDailyPlanItem.StartedAt"/>.
    /// Cross-user requests surface as 404 via
    /// <see cref="InvalidOperationException"/>.</summary>
    Task<ListeningDailyPlanItem> StartItemAsync(string userId, Guid itemId, CancellationToken ct);

    /// <summary>Transition <paramref name="itemId"/> to <c>completed</c> and
    /// stamp <see cref="ListeningDailyPlanItem.CompletedAt"/>.</summary>
    Task<ListeningDailyPlanItem> CompleteItemAsync(string userId, Guid itemId, CancellationToken ct);

    /// <summary>Transition <paramref name="itemId"/> to <c>skipped</c> and
    /// embed <paramref name="reason"/> into the payload JSON for analytics.</summary>
    Task<ListeningDailyPlanItem> SkipItemAsync(string userId, Guid itemId, string? reason, CancellationToken ct);

    /// <summary>Generates today's plan items if none exist for this user+date.
    /// Idempotent. Used by the dashboard's first fetch and the nightly worker.</summary>
    Task<IReadOnlyList<ListeningDailyPlanItem>> GenerateForTodayIfMissingAsync(string userId, DateOnly today, CancellationToken ct);
}

public sealed class ListeningDailyPlanService(
    LearnerDbContext db,
    IListeningSkillScoringService skillScoring,
    IListeningPracticeSelectionService practiceSelection) : IListeningDailyPlanService
{
    private const int MaxTasksPerDay = 4;

    // ── Estimated minutes per item type (§10) ────────────────────────────────
    private static int MinutesFor(string itemType) => itemType switch
    {
        "drill"                 => 18,
        "accent_drill"          => 8,
        "dictation"             => 6,
        "pronunciation_review"  => 6,
        "wrong_review"          => 5,
        "strategy_read"         => 3,
        "lesson"                => 25,
        "mock"                  => 45,
        _                       => 10,
    };

    // ── Default fallback target counts when no skill score row exists ─────
    private const string DefaultWeakestSkill = "L1";
    private const string DefaultWeakestAccent = "australian";

    // ═══════════════════════════════════════════════════════════════════════
    // GetTodayAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<ListeningDailyPlanItem>> GetTodayAsync(
        string userId, DateOnly today, CancellationToken ct)
    {
        return await db.ListeningDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == today)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GenerateForTodayIfMissingAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<ListeningDailyPlanItem>> GenerateForTodayIfMissingAsync(
        string userId, DateOnly today, CancellationToken ct)
    {
        // Idempotency: skip if today's plan already exists for this learner.
        var existing = await db.ListeningDailyPlanItems
            .Where(i => i.UserId == userId && i.PlanDate == today)
            .OrderBy(i => i.Ordinal)
            .ToListAsync(ct);
        if (existing.Count > 0) return existing;

        // Learner profile — read but not mutated. Reserved for future
        // target-band / exam-date gating; learners without a profile still
        // receive a minimal "drill" plan via the default-skill fallback.
        _ = await db.LearnerListeningProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var tasks = new List<PendingItem>(MaxTasksPerDay);

        // ── Rule 1: always add a main drill on the weakest sub-skill ─────
        var weakestSkill = await skillScoring.GetWeakestSkillAsync(userId, ct) ?? DefaultWeakestSkill;
        var weakestAccent = await skillScoring.GetWeakestAccentAsync(userId, ct);
        var drillMinutes = MinutesFor("drill");
        var drillQuestionIds = await practiceSelection.SelectAudioForDrillAsync(
            userId, weakestSkill, focusAccent: null, drillMinutes, ct);
        tasks.Add(new PendingItem(
            ItemType: "drill",
            FocusSkill: weakestSkill,
            FocusAccent: null,
            Minutes: drillMinutes,
            PayloadJson: JsonSerializer.Serialize(new
            {
                skill = weakestSkill,
                questionIds = drillQuestionIds,
            })));

        // Day-of-week helpers shared by the cadence-based rules below.
        // Mon (1) / Wed (3) / Fri (5) — accent drills 3× per week.
        // Tue (2) / Thu (4)          — dictation 2× per week.
        // Wed (3) / Sat (6)          — strategy_read 2× per week.
        var dow = (int)today.DayOfWeek;

        // ── Rule 2: accent drill 2–3× per week on the weakest accent ─────
        if (tasks.Count < MaxTasksPerDay &&
            (dow == (int)DayOfWeek.Monday
             || dow == (int)DayOfWeek.Wednesday
             || dow == (int)DayOfWeek.Friday))
        {
            var accent = weakestAccent ?? DefaultWeakestAccent;
            var minutes = MinutesFor("accent_drill");
            var ids = await practiceSelection.SelectAccentDrillAsync(userId, accent, minutes, ct);
            tasks.Add(new PendingItem(
                ItemType: "accent_drill",
                FocusSkill: null,
                FocusAccent: accent,
                Minutes: minutes,
                PayloadJson: JsonSerializer.Serialize(new
                {
                    accent,
                    questionIds = ids,
                })));
        }

        // ── Rule 3: pronunciation_review every day if SM-2 cards are due ──
        if (tasks.Count < MaxTasksPerDay)
        {
            var dueCutoff = new DateTimeOffset(today.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var dueCards = await db.LearnerPronunciationCards
                .AsNoTracking()
                .Where(c => c.UserId == userId && c.NextReviewAt <= dueCutoff)
                .OrderBy(c => c.NextReviewAt)
                .Select(c => c.PronunciationCardId)
                .Take(20)
                .ToListAsync(ct);

            if (dueCards.Count > 0)
            {
                tasks.Add(new PendingItem(
                    ItemType: "pronunciation_review",
                    FocusSkill: null,
                    FocusAccent: null,
                    Minutes: MinutesFor("pronunciation_review"),
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        cardIds = dueCards,
                        dueCount = dueCards.Count,
                    })));
            }
        }

        // ── Rule 4: wrong_review if review queue has due items ───────────
        if (tasks.Count < MaxTasksPerDay)
        {
            var now = DateTimeOffset.UtcNow;
            var dueQuestionIds = await db.ListeningQuestionAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId
                    && a.InReviewQueue
                    && a.NextReviewAt != null
                    && a.NextReviewAt <= now)
                .OrderBy(a => a.NextReviewAt)
                .Select(a => a.ListeningQuestionId)
                .Distinct()
                .Take(15)
                .ToListAsync(ct);

            if (dueQuestionIds.Count > 0)
            {
                tasks.Add(new PendingItem(
                    ItemType: "wrong_review",
                    FocusSkill: null,
                    FocusAccent: null,
                    Minutes: MinutesFor("wrong_review"),
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        questionIds = dueQuestionIds,
                        queueSize = dueQuestionIds.Count,
                    })));
            }
        }

        // ── Rule 5: dictation 2× per week ───────────────────────────────
        if (tasks.Count < MaxTasksPerDay &&
            (dow == (int)DayOfWeek.Tuesday || dow == (int)DayOfWeek.Thursday))
        {
            var minutes = MinutesFor("dictation");
            var targetCount = Math.Max(2, minutes / 2);
            var drillIds = await practiceSelection.SelectDictationSetAsync(userId, targetCount, ct);
            if (drillIds.Count > 0)
            {
                tasks.Add(new PendingItem(
                    ItemType: "dictation",
                    FocusSkill: null,
                    FocusAccent: null,
                    Minutes: minutes,
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        drillIds,
                        count = drillIds.Count,
                    })));
            }
        }

        // ── Rule 6: strategy_read 2× per week (optional, cap-permitting) ─
        if (tasks.Count < MaxTasksPerDay &&
            (dow == (int)DayOfWeek.Wednesday || dow == (int)DayOfWeek.Saturday))
        {
            // Strategies already marked as read this week are skipped so the
            // learner sees a different article each pass.
            var readStrategyIds = await db.LearnerListeningStrategyProgresses
                .AsNoTracking()
                .Where(p => p.UserId == userId && p.MarkedAsRead)
                .Select(p => p.StrategyId)
                .ToListAsync(ct);

            var nextStrategy = await db.ListeningStrategies
                .AsNoTracking()
                .Where(s => s.IsPublished && !readStrategyIds.Contains(s.Id))
                .OrderBy(s => s.Difficulty)
                .ThenBy(s => s.Slug)  // deterministic tie-break (Difficulty + Slug)
                .Select(s => new { s.Id, s.Slug, s.EstimatedReadMinutes })
                .FirstOrDefaultAsync(ct);

            if (nextStrategy is not null)
            {
                tasks.Add(new PendingItem(
                    ItemType: "strategy_read",
                    FocusSkill: null,
                    FocusAccent: null,
                    Minutes: Math.Max(MinutesFor("strategy_read"), nextStrategy.EstimatedReadMinutes),
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        strategyId = nextStrategy.Id,
                        slug = nextStrategy.Slug,
                    })));
            }
        }

        // ── Persist ──────────────────────────────────────────────────────
        var created = new List<ListeningDailyPlanItem>(tasks.Count);
        for (var i = 0; i < tasks.Count && i < MaxTasksPerDay; i++)
        {
            var t = tasks[i];
            var row = new ListeningDailyPlanItem
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlanDate = today,
                Ordinal = i,
                ItemType = t.ItemType,
                FocusSkill = t.FocusSkill,
                FocusAccent = t.FocusAccent,
                EstimatedMinutes = t.Minutes,
                PayloadJson = t.PayloadJson,
                Status = "pending",
            };
            db.ListeningDailyPlanItems.Add(row);
            created.Add(row);
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // StartItemAsync / CompleteItemAsync / SkipItemAsync
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<ListeningDailyPlanItem> StartItemAsync(
        string userId, Guid itemId, CancellationToken ct)
    {
        var item = await LoadOwnedItemOrThrowAsync(userId, itemId, ct);

        // Only flip from pending → in_progress; re-starting a started item
        // is a no-op so the dashboard can call this redundantly on resume.
        if (item.Status == "pending")
        {
            item.Status = "in_progress";
            item.StartedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return item;
    }

    public async Task<ListeningDailyPlanItem> CompleteItemAsync(
        string userId, Guid itemId, CancellationToken ct)
    {
        var item = await LoadOwnedItemOrThrowAsync(userId, itemId, ct);

        if (item.Status != "completed")
        {
            item.Status = "completed";
            item.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return item;
    }

    public async Task<ListeningDailyPlanItem> SkipItemAsync(
        string userId, Guid itemId, string? reason, CancellationToken ct)
    {
        var item = await LoadOwnedItemOrThrowAsync(userId, itemId, ct);

        item.Status = "skipped";

        // Embed skip reason in payload for analytics. Tolerate non-object
        // payloads (older items written as arrays/strings) by re-anchoring on
        // a fresh dictionary in the failure path.
        try
        {
            var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(item.PayloadJson)
                           ?? new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(reason))
            {
                existing["skipReason"] = reason;
            }
            existing["skippedAt"] = DateTimeOffset.UtcNow.ToString("o");
            item.PayloadJson = JsonSerializer.Serialize(existing);
        }
        catch (JsonException)
        {
            item.PayloadJson = JsonSerializer.Serialize(new
            {
                skipReason = reason ?? "user_skip",
                skippedAt = DateTimeOffset.UtcNow.ToString("o"),
            });
        }

        await db.SaveChangesAsync(ct);
        return item;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Load an item that belongs to <paramref name="userId"/>. Throws
    /// <see cref="InvalidOperationException"/> with a "not found" message for
    /// both missing rows and cross-user probes — the endpoint maps both to
    /// HTTP 404 to deny enumeration attacks.</summary>
    private async Task<ListeningDailyPlanItem> LoadOwnedItemOrThrowAsync(
        string userId, Guid itemId, CancellationToken ct)
    {
        var item = await db.ListeningDailyPlanItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct);
        if (item is null)
        {
            throw new InvalidOperationException(
                $"Listening daily plan item {itemId} not found for this user.");
        }
        return item;
    }

    /// <summary>Private record collecting the per-rule inputs before we know
    /// the final ordinal — keeps the generation logic linear and readable.</summary>
    private sealed record PendingItem(
        string ItemType,
        string? FocusSkill,
        string? FocusAccent,
        int Minutes,
        string PayloadJson);
}


