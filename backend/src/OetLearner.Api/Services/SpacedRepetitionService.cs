using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Unified cross-skill spaced repetition service. See docs/REVIEW-MODULE.md.
///
/// MISSION CRITICAL:
///   - SM-2 maths comes exclusively from <see cref="ISpacedRepetitionScheduler"/>.
///   - Vocabulary cards are <b>projected</b> into the unified queue; writes for
///     <c>ri-v-*</c> ids are routed to <see cref="VocabularyService"/>.
///   - New <see cref="ReviewItem"/> rows come exclusively from
///     <see cref="IReviewItemSeeder"/>; this service never constructs one from
///     domain data.
/// </summary>
public class SpacedRepetitionService(
    LearnerDbContext db,
    ISpacedRepetitionScheduler scheduler,
    VocabularyService vocabularyService,
    GamificationService? gamification = null)
{
    private const string VocabularyProjectedPrefix = "ri-v-";

    // ── Queue ───────────────────────────────────────────────────────────

    public async Task<object> GetDueItemsAsync(
        string userId,
        int limit,
        string? source,
        string? subtest,
        bool includeVocabulary,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveLimit = limit <= 0 ? 20 : Math.Min(limit, 200);

        // Native ReviewItems
        var query = db.ReviewItems
            .Where(r => r.UserId == userId && r.Status == "active" && r.DueDate <= today);

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(r => r.SourceType == source);
        if (!string.IsNullOrWhiteSpace(subtest))
            query = query.Where(r => r.SubtestCode == subtest);

        var native = await query
            .OrderBy(r => r.DueDate)
            .ThenBy(r => r.CreatedAt)
            .Take(effectiveLimit)
            .ToListAsync(ct);

        var results = native.Select(MapItem).ToList();

        // Optional vocabulary projection
        if (includeVocabulary && string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(subtest) && results.Count < effectiveLimit)
        {
            var remaining = effectiveLimit - results.Count;
            var vocabDue = await (from lv in db.LearnerVocabularies
                                  join term in db.VocabularyTerms on lv.TermId equals term.Id
                                  where lv.UserId == userId
                                        && lv.Mastery != "mastered"
                                        && lv.NextReviewDate != null
                                        && lv.NextReviewDate <= today
                                  orderby lv.NextReviewDate
                                  select new { lv, term })
                .Take(remaining)
                .ToListAsync(ct);

            foreach (var row in vocabDue)
            {
                results.Add(MapVocabularyProjection(row.lv, row.term));
            }
        }

        return results;
    }

    // ── Summary ─────────────────────────────────────────────────────────

    public async Task<object> GetReviewSummaryAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totalNative = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active", ct);
        var dueNative = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueDate <= today, ct);
        var dueTodayNative = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueDate == today, ct);
        var masteredNative = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "mastered", ct);
        var upcomingNative = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueDate > today && r.DueDate <= today.AddDays(7), ct);
        var suspended = await db.ReviewItems.CountAsync(r => r.UserId == userId && r.Status == "suspended", ct);

        // Vocabulary contribution (projection)
        var vocabTotal = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery != "mastered", ct);
        var vocabDue = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery != "mastered" && lv.NextReviewDate != null && lv.NextReviewDate <= today, ct);
        var vocabDueToday = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery != "mastered" && lv.NextReviewDate == today, ct);
        var vocabMastered = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery == "mastered", ct);
        var vocabUpcoming = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery != "mastered" && lv.NextReviewDate != null && lv.NextReviewDate > today && lv.NextReviewDate <= today.AddDays(7), ct);

        // By-source breakdown (native only; vocabulary counted separately)
        var bySource = await db.ReviewItems
            .Where(r => r.UserId == userId && r.Status == "active")
            .GroupBy(r => r.SourceType)
            .Select(g => new { sourceType = g.Key, active = g.Count(), due = g.Count(r => r.DueDate <= today) })
            .ToListAsync(ct);

        var byPromptKind = await db.ReviewItems
            .Where(r => r.UserId == userId && r.Status == "active")
            .GroupBy(r => r.PromptKind ?? "generic")
            .Select(g => new { promptKind = g.Key, count = g.Count() })
            .ToListAsync(ct);

        // Enrich bySource with vocabulary bucket
        var bySourceList = bySource.Select(s => new { sourceType = s.sourceType, active = s.active, due = s.due }).ToList<object>();
        if (vocabTotal > 0)
        {
            bySourceList.Add(new { sourceType = ReviewSourceTypes.Vocabulary, active = vocabTotal, due = vocabDue });
        }

        return new
        {
            total = totalNative + vocabTotal,
            due = dueNative + vocabDue,
            dueToday = dueTodayNative + vocabDueToday,
            mastered = masteredNative + vocabMastered,
            upcoming = upcomingNative + vocabUpcoming,
            suspended,
            native = new { total = totalNative, due = dueNative, dueToday = dueTodayNative, mastered = masteredNative, upcoming = upcomingNative },
            vocabulary = new { total = vocabTotal, due = vocabDue, dueToday = vocabDueToday, mastered = vocabMastered, upcoming = vocabUpcoming },
            bySource = bySourceList,
            byPromptKind,
        };
    }

    // ── Create (legacy; prefer IReviewItemSeeder on the server) ────────

    public async Task<object> CreateReviewItemAsync(string userId, CreateReviewItemRequest request, CancellationToken ct)
    {
        var existing = await db.ReviewItems.FirstOrDefaultAsync(
            r => r.UserId == userId && r.SourceType == request.SourceType && r.SourceId == request.SourceId, ct);

        if (existing != null)
            return MapItem(existing);

        var item = new ReviewItem
        {
            Id = $"ri-{Guid.NewGuid():N}",
            UserId = userId,
            ExamTypeCode = request.ExamTypeCode,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SubtestCode = request.SubtestCode ?? string.Empty,
            CriterionCode = request.CriterionCode,
            PromptKind = ReviewSourceTypes.PromptKindFor(request.SourceType),
            QuestionJson = request.QuestionJson,
            AnswerJson = request.AnswerJson,
            EaseFactor = 2.5,
            IntervalDays = 1,
            ReviewCount = 0,
            ConsecutiveCorrect = 0,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "active"
        };
        db.ReviewItems.Add(item);
        await db.SaveChangesAsync(ct);
        return MapItem(item);
    }

    // ── Submit (unified; routes vocab projections transparently) ───────

    public async Task<object> SubmitReviewAsync(string userId, string itemId, int quality, CancellationToken ct)
    {
        if (quality < 0 || quality > 5)
            throw ApiException.Validation("INVALID_QUALITY", "Quality must be 0-5.");

        // Route vocabulary projections to the vocab silo.
        if (itemId.StartsWith(VocabularyProjectedPrefix, StringComparison.Ordinal))
        {
            var raw = itemId[VocabularyProjectedPrefix.Length..];
            if (!Guid.TryParse(raw, out var lvId))
                throw ApiException.Validation("INVALID_REVIEW_ID", "Invalid vocabulary review id.");
            var response = await vocabularyService.SubmitFlashcardReviewAsync(userId, lvId, quality, ct);
            return new
            {
                itemId,
                routed = "vocabulary",
                dueDate = response.NextReviewDate,
                intervalDays = response.IntervalDays,
                easeFactor = response.EaseFactor,
                mastery = response.Mastery,
            };
        }

        var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("REVIEW_ITEM_NOT_FOUND", "Review item not found.");

        if (item.Status == "suspended")
            throw ApiException.Validation("REVIEW_ITEM_SUSPENDED", "Cannot review a suspended item. Resume first.");

        // Snapshot pre-state for undo.
        var snapshot = new ReviewItemTransition
        {
            Id = $"rit-{Guid.NewGuid():N}",
            ReviewItemId = item.Id,
            UserId = item.UserId,
            PrevEaseFactor = item.EaseFactor,
            PrevIntervalDays = item.IntervalDays,
            PrevReviewCount = item.ReviewCount,
            PrevConsecutiveCorrect = item.ConsecutiveCorrect,
            PrevDueDate = item.DueDate,
            PrevLastReviewedAt = item.LastReviewedAt,
            PrevLastQuality = item.LastQuality,
            PrevStatus = item.Status,
            AppliedQuality = quality,
            AppliedAt = DateTimeOffset.UtcNow,
        };
        db.ReviewItemTransitions.Add(snapshot);

        // Prune older transitions (keep latest 3 per item).
        var stale = await db.ReviewItemTransitions
            .Where(t => t.ReviewItemId == item.Id)
            .OrderByDescending(t => t.AppliedAt)
            .Skip(2)
            .ToListAsync(ct);
        if (stale.Count > 0) db.ReviewItemTransitions.RemoveRange(stale);

        ApplySm2(item, quality);
        item.LastReviewedAt = DateTimeOffset.UtcNow;
        item.LastQuality = quality;

        // Mastery graduation rule (mirrors vocabulary): >=10 reviews, >=8 correct, interval >= 60d.
        var becameMastered = false;
        if (item.Status == "active"
            && item.ReviewCount >= 10
            && item.ConsecutiveCorrect >= 8
            && item.IntervalDays >= 60)
        {
            item.Status = "mastered";
            becameMastered = true;
        }

        await db.SaveChangesAsync(ct);

        if (becameMastered && gamification is not null)
        {
            try { await gamification.AwardXpAsync(userId, 25, $"Mastered review item: {item.Title ?? item.SourceType}", ct); }
            catch { /* gamification is non-critical */ }
        }

        return new
        {
            itemId,
            dueDate = item.DueDate,
            intervalDays = item.IntervalDays,
            easeFactor = item.EaseFactor,
            status = item.Status,
            masteredJustNow = becameMastered,
        };
    }

    // ── Lifecycle: suspend / resume / undo ──────────────────────────────

    public async Task<object> SuspendReviewItemAsync(string userId, string itemId, string? reason, CancellationToken ct)
    {
        if (itemId.StartsWith(VocabularyProjectedPrefix, StringComparison.Ordinal))
            throw ApiException.Validation("UNSUPPORTED", "Vocabulary cards are managed in the vocabulary surface.");

        var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("REVIEW_ITEM_NOT_FOUND", "Review item not found.");

        item.Status = "suspended";
        item.SuspendedAt = DateTimeOffset.UtcNow;
        item.SuspendedReason = reason is null ? null : (reason.Length > 120 ? reason[..120] : reason);
        await db.SaveChangesAsync(ct);
        return new { itemId, status = item.Status, suspendedAt = item.SuspendedAt };
    }

    public async Task<object> ResumeReviewItemAsync(string userId, string itemId, CancellationToken ct)
    {
        if (itemId.StartsWith(VocabularyProjectedPrefix, StringComparison.Ordinal))
            throw ApiException.Validation("UNSUPPORTED", "Vocabulary cards are managed in the vocabulary surface.");

        var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("REVIEW_ITEM_NOT_FOUND", "Review item not found.");

        if (item.Status != "suspended")
            return new { itemId, status = item.Status, resumed = false };

        item.Status = "active";
        item.SuspendedAt = null;
        item.SuspendedReason = null;
        // Move DueDate to today so the resumed item lands in the queue.
        item.DueDate = DateOnly.FromDateTime(DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return new { itemId, status = item.Status, resumed = true, dueDate = item.DueDate };
    }

    public async Task<object> UndoLastAsync(string userId, string itemId, CancellationToken ct)
    {
        if (itemId.StartsWith(VocabularyProjectedPrefix, StringComparison.Ordinal))
            throw ApiException.Validation("UNSUPPORTED", "Vocabulary undo is managed in the vocabulary surface.");

        var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("REVIEW_ITEM_NOT_FOUND", "Review item not found.");

        // Fetch latest snapshot.
        List<ReviewItemTransition> transitions;
        if (db.Database.IsSqlite())
        {
            var all = await db.ReviewItemTransitions
                .Where(t => t.ReviewItemId == item.Id && t.UserId == userId)
                .ToListAsync(ct);
            transitions = all.OrderByDescending(t => t.AppliedAt).ToList();
        }
        else
        {
            transitions = await db.ReviewItemTransitions
                .Where(t => t.ReviewItemId == item.Id && t.UserId == userId)
                .OrderByDescending(t => t.AppliedAt)
                .ToListAsync(ct);
        }
        var latest = transitions.FirstOrDefault()
            ?? throw ApiException.Validation("NO_TRANSITION", "No review to undo.");

        item.EaseFactor = latest.PrevEaseFactor;
        item.IntervalDays = latest.PrevIntervalDays;
        item.ReviewCount = latest.PrevReviewCount;
        item.ConsecutiveCorrect = latest.PrevConsecutiveCorrect;
        item.DueDate = latest.PrevDueDate;
        item.LastReviewedAt = latest.PrevLastReviewedAt;
        item.LastQuality = latest.PrevLastQuality;
        item.Status = latest.PrevStatus;

        db.ReviewItemTransitions.Remove(latest);
        await db.SaveChangesAsync(ct);

        return new { itemId, status = item.Status, dueDate = item.DueDate, intervalDays = item.IntervalDays };
    }

    public async Task<object> DeleteReviewItemAsync(string userId, string itemId, CancellationToken ct)
    {
        var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == itemId && r.UserId == userId, ct)
            ?? throw ApiException.NotFound("REVIEW_ITEM_NOT_FOUND", "Review item not found.");

        db.ReviewItems.Remove(item);
        var transitions = await db.ReviewItemTransitions.Where(t => t.ReviewItemId == item.Id).ToListAsync(ct);
        if (transitions.Count > 0) db.ReviewItemTransitions.RemoveRange(transitions);

        await db.SaveChangesAsync(ct);
        return new { deleted = true };
    }

    // ── Retention + heatmap ─────────────────────────────────────────────

    public async Task<object> GetRetentionAsync(string userId, int days, CancellationToken ct)
    {
        var window = Math.Clamp(days, 7, 90);
        var from = DateTimeOffset.UtcNow.AddDays(-window);

        var rows = await db.ReviewItemTransitions
            .Where(t => t.UserId == userId && t.AppliedAt >= from)
            .Select(t => new { t.AppliedAt, t.AppliedQuality })
            .ToListAsync(ct);

        var byDay = rows
            .GroupBy(r => DateOnly.FromDateTime(r.AppliedAt.UtcDateTime))
            .ToDictionary(g => g.Key, g => new
            {
                reviewed = g.Count(),
                correct = g.Count(x => x.AppliedQuality >= 3),
            });

        var series = Enumerable.Range(0, window).Select(i =>
        {
            var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-(window - 1 - i)));
            if (byDay.TryGetValue(day, out var v))
            {
                var accuracy = v.reviewed == 0 ? 0 : (int)Math.Round(100.0 * v.correct / v.reviewed);
                return new { date = day, reviewed = v.reviewed, correct = v.correct, accuracy };
            }
            return new { date = day, reviewed = 0, correct = 0, accuracy = 0 };
        }).ToList();

        return new { days = window, series };
    }

    public async Task<object> GetHeatmapAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var grouped = await db.ReviewItems
            .Where(r => r.UserId == userId)
            .GroupBy(r => new { r.SourceType, r.SubtestCode, r.CriterionCode })
            .Select(g => new
            {
                sourceType = g.Key.SourceType,
                subtest = g.Key.SubtestCode,
                criterion = g.Key.CriterionCode,
                active = g.Count(x => x.Status == "active"),
                mastered = g.Count(x => x.Status == "mastered"),
                suspended = g.Count(x => x.Status == "suspended"),
                due = g.Count(x => x.Status == "active" && x.DueDate <= today),
            })
            .ToListAsync(ct);

        return new { cells = grouped };
    }

    // ── SM-2 (delegated) ────────────────────────────────────────────────

    private void ApplySm2(ReviewItem item, int quality)
    {
        var update = scheduler.Schedule(
            new Sm2State(item.EaseFactor, item.IntervalDays, item.ReviewCount, item.ConsecutiveCorrect),
            quality);

        item.EaseFactor = update.EaseFactor;
        item.IntervalDays = update.IntervalDays;
        item.ReviewCount = update.ReviewCount;
        item.ConsecutiveCorrect = update.CorrectAnswer ? item.ConsecutiveCorrect + 1 : 0;
        item.DueDate = update.NextReviewDate;
    }

    // ── Mapping ─────────────────────────────────────────────────────────

    private static object MapItem(ReviewItem r) => new
    {
        id = r.Id,
        examTypeCode = r.ExamTypeCode,
        sourceType = r.SourceType,
        sourceId = r.SourceId,
        subtestCode = r.SubtestCode,
        criterionCode = r.CriterionCode,
        title = r.Title,
        promptKind = r.PromptKind ?? ReviewSourceTypes.PromptKindFor(r.SourceType),
        questionJson = r.QuestionJson,
        answerJson = r.AnswerJson,
        richContentJson = r.RichContentJson,
        easeFactor = r.EaseFactor,
        intervalDays = r.IntervalDays,
        reviewCount = r.ReviewCount,
        consecutiveCorrect = r.ConsecutiveCorrect,
        dueDate = r.DueDate,
        lastReviewedAt = r.LastReviewedAt,
        lastQuality = r.LastQuality,
        status = r.Status,
        suspendedAt = r.SuspendedAt,
        suspendedReason = r.SuspendedReason,
    };

    private static object MapVocabularyProjection(LearnerVocabulary lv, VocabularyTerm term)
    {
        var richContent = System.Text.Json.JsonSerializer.Serialize(new
        {
            termId = term.Id,
            term = term.Term,
            definition = term.Definition,
            exampleSentence = term.ExampleSentence,
            ipa = term.IpaPronunciation,
            audioUrl = term.AudioUrl,
            category = term.Category,
        });

        return new
        {
            id = $"{VocabularyProjectedPrefix}{lv.Id:N}",
            examTypeCode = term.ExamTypeCode,
            sourceType = ReviewSourceTypes.Vocabulary,
            sourceId = term.Id,
            subtestCode = "vocabulary",
            criterionCode = (string?)term.Category,
            title = term.Term,
            promptKind = "vocabulary",
            questionJson = System.Text.Json.JsonSerializer.Serialize(new { text = term.Term, termId = term.Id }),
            answerJson = System.Text.Json.JsonSerializer.Serialize(new { text = term.Definition, example = term.ExampleSentence }),
            richContentJson = richContent,
            easeFactor = lv.EaseFactor,
            intervalDays = lv.IntervalDays,
            reviewCount = lv.ReviewCount,
            consecutiveCorrect = lv.CorrectCount,
            dueDate = lv.NextReviewDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            lastReviewedAt = lv.LastReviewedAt,
            lastQuality = (int?)null,
            status = lv.Mastery == "mastered" ? "mastered" : "active",
            suspendedAt = (DateTimeOffset?)null,
            suspendedReason = (string?)null,
        };
    }
}

public record CreateReviewItemRequest(
    string ExamTypeCode,
    string SourceType,
    string SourceId,
    string? SubtestCode,
    string? CriterionCode,
    string QuestionJson,
    string AnswerJson);

public record ReviewItemConfigResponse(int NewCardsPerDay, int ReviewsPerDay);
