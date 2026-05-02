using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

/// <summary>
/// Learner-facing vocabulary service. Source of truth: docs/VOCABULARY-MODULE.md.
/// All SM-2 scheduling is delegated to <see cref="ISpacedRepetitionScheduler"/>.
/// All AI calls are delegated to <see cref="VocabularyGlossService"/> (gateway-bound).
/// </summary>
public class VocabularyService(
    LearnerDbContext db,
    ISpacedRepetitionScheduler scheduler,
    GamificationService? gamification = null)
{
    private const int FreeListSizeCap = 500;

    // ── Browse terms ─────────────────────────────────────────────────────

    public async Task<VocabularyTermsPageResponse> GetTermsAsync(
        string? examTypeCode,
        string? category,
        string? profession,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = db.VocabularyTerms.Where(t => t.Status == "active");
        if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(t => t.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrEmpty(category)) query = query.Where(t => t.Category == category);
        if (!string.IsNullOrEmpty(profession)) query = query.Where(t => t.ProfessionId == profession);
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.Trim();
            query = query.Where(t => t.Term.Contains(s) || t.Definition.Contains(s));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.Term)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var mapped = items.Select(Map).ToList();
        return new VocabularyTermsPageResponse(total, page, pageSize, mapped, mapped);
    }

    public async Task<VocabularyTermResponse> GetTermAsync(string termId, CancellationToken ct)
    {
        var term = await db.VocabularyTerms.FindAsync([termId], ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Vocabulary term not found.");
        return Map(term);
    }

    public async Task<VocabularyLookupResult> LookupAsync(string query, string? examTypeCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new VocabularyLookupResult(false, null, Array.Empty<VocabularyTermSummary>());

        var normalised = query.Trim().ToLowerInvariant();
        var baseQuery = db.VocabularyTerms.Where(t => t.Status == "active");
        if (!string.IsNullOrEmpty(examTypeCode)) baseQuery = baseQuery.Where(t => t.ExamTypeCode == examTypeCode);

        // Exact match first (case-insensitive).
        var exact = await baseQuery.FirstOrDefaultAsync(t => t.Term.ToLower() == normalised, ct);
        if (exact is not null)
            return new VocabularyLookupResult(true, Map(exact), Array.Empty<VocabularyTermSummary>());

        // Prefix + contains suggestions (top 10 by prefix match first).
        var suggestions = await baseQuery
            .Where(t => t.Term.StartsWith(normalised) || t.Term.Contains(normalised))
            .OrderBy(t => t.Term)
            .Take(10)
            .ToListAsync(ct);

        return new VocabularyLookupResult(
            Found: false,
            Term: null,
            Suggestions: suggestions.Select(MapSummary).ToList());
    }

    public async Task<VocabularyCategoriesResponse> GetCategoriesAsync(
        string? examTypeCode,
        string? profession,
        CancellationToken ct)
    {
        var query = db.VocabularyTerms.Where(t => t.Status == "active");
        if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(t => t.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrEmpty(profession)) query = query.Where(t => t.ProfessionId == profession);

        // Use a two-step query to stay compatible with EF InMemory provider in tests.
        var rawCategories = await query.Select(t => t.Category).ToListAsync(ct);
        var grouped = rawCategories
            .GroupBy(c => c)
            .Select(g => new VocabularyCategoryItem(g.Key, g.Count()))
            .OrderByDescending(x => x.TermCount)
            .ThenBy(x => x.Category)
            .ToList();

        return new VocabularyCategoriesResponse(
            ExamTypeCode: examTypeCode ?? "oet",
            ProfessionId: profession,
            Categories: grouped);
    }

    // ── Learner vocabulary list ───────────────────────────────────────────

    public async Task<IReadOnlyList<MyVocabularyItem>> GetMyVocabularyAsync(
        string userId,
        string? mastery,
        CancellationToken ct)
    {
        var query = db.LearnerVocabularies
            .Where(lv => lv.UserId == userId)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t });

        if (!string.IsNullOrEmpty(mastery))
            query = query.Where(x => x.lv.Mastery == mastery);

        var items = await query.OrderBy(x => x.lv.NextReviewDate).ToListAsync(ct);
        return items.Select(x => new MyVocabularyItem(
            Id: x.lv.Id,
            TermId: x.lv.TermId,
            Term: x.t.Term,
            Definition: x.t.Definition,
            Mastery: x.lv.Mastery,
            EaseFactor: x.lv.EaseFactor,
            IntervalDays: x.lv.IntervalDays,
            ReviewCount: x.lv.ReviewCount,
            CorrectCount: x.lv.CorrectCount,
            NextReviewDate: x.lv.NextReviewDate,
            DueAt: x.lv.NextReviewDate?.ToString("yyyy-MM-dd"),
            LastReviewedAt: x.lv.LastReviewedAt,
            AddedAt: x.lv.AddedAt,
            SourceRef: x.lv.SourceRef)).ToList();
    }

    public async Task<MyVocabularyAddResponse> AddToMyVocabularyAsync(
        string userId,
        string termId,
        string? sourceRef,
        bool isPremium,
        CancellationToken ct)
    {
        var term = await db.VocabularyTerms.FindAsync([termId], ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");

        var existing = await db.LearnerVocabularies.FirstOrDefaultAsync(
            lv => lv.UserId == userId && lv.TermId == termId, ct);
        if (existing != null)
        {
            return new MyVocabularyAddResponse(false, ToMyItem(existing, term));
        }

        if (!isPremium)
        {
            var currentCount = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId, ct);
            if (currentCount >= FreeListSizeCap)
            {
                throw ApiException.PaymentRequired("VOCAB_FREE_CAP_REACHED",
                    $"Your free word bank is full ({FreeListSizeCap} terms). Upgrade to unlock unlimited capacity.");
            }
        }

        var lv = new LearnerVocabulary
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TermId = termId,
            Mastery = "new",
            EaseFactor = 2.5,
            IntervalDays = 1,
            ReviewCount = 0,
            CorrectCount = 0,
            NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AddedAt = DateTimeOffset.UtcNow,
            SourceRef = string.IsNullOrWhiteSpace(sourceRef) ? null : sourceRef.Trim(),
        };
        db.LearnerVocabularies.Add(lv);
        await db.SaveChangesAsync(ct);
        await TryEvaluateAchievementsAsync(userId, "vocab_added", ct);
        return new MyVocabularyAddResponse(true, ToMyItem(lv, term));
    }

    public async Task<object> RemoveFromMyVocabularyAsync(string userId, string termId, CancellationToken ct)
    {
        var lv = await db.LearnerVocabularies.FirstOrDefaultAsync(x => x.UserId == userId && x.TermId == termId, ct)
            ?? throw ApiException.NotFound("NOT_IN_VOCABULARY", "Term is not in vocabulary.");
        db.LearnerVocabularies.Remove(lv);
        await db.SaveChangesAsync(ct);
        return new { removed = true };
    }

    // ── Flashcards ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<VocabularyFlashcardDto>> GetDueFlashcardsAsync(
        string userId,
        int limit,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var due = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId
                && lv.Mastery != "mastered"
                && (lv.NextReviewDate == null || lv.NextReviewDate <= today))
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
            .OrderBy(x => x.lv.NextReviewDate)
            .Take(limit)
            .ToListAsync(ct);

        return due.Select(x => ToFlashcard(x.lv, x.t)).ToList();
    }

    public async Task<FlashcardReviewResponse> SubmitFlashcardReviewAsync(
        string userId,
        Guid lvId,
        int quality,
        CancellationToken ct)
    {
        if (quality < 0 || quality > 5)
            throw ApiException.Validation("INVALID_QUALITY", "Quality must be 0-5.");

        var lv = await db.LearnerVocabularies.FirstOrDefaultAsync(x => x.Id == lvId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("NOT_FOUND", "Flashcard not found.");

        var update = scheduler.Schedule(
            new Sm2State(lv.EaseFactor, lv.IntervalDays, lv.ReviewCount, lv.CorrectCount),
            quality);

        lv.EaseFactor = update.EaseFactor;
        lv.IntervalDays = update.IntervalDays;
        lv.ReviewCount = update.ReviewCount;
        lv.CorrectCount = update.CorrectCount;
        lv.NextReviewDate = update.NextReviewDate;
        lv.Mastery = update.DeriveMastery();
        lv.LastReviewedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        if (lv.Mastery == "mastered")
            await TryEvaluateAchievementsAsync(userId, "vocab_mastered", ct);

        return new FlashcardReviewResponse(
            Id: lv.Id,
            Mastery: lv.Mastery,
            NextReviewDate: lv.NextReviewDate!.Value,
            IntervalDays: lv.IntervalDays,
            EaseFactor: lv.EaseFactor,
            ReviewCount: lv.ReviewCount);
    }

    // ── Daily set ───────────────────────────────────────────────────────

    public async Task<VocabularyDailySetResponse> GetDailySetAsync(
        string userId,
        int count,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Due cards first.
        var due = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId
                && lv.Mastery != "mastered"
                && (lv.NextReviewDate == null || lv.NextReviewDate <= today))
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
            .OrderBy(x => x.lv.NextReviewDate)
            .Take(count)
            .ToListAsync(ct);

        var cards = new List<VocabularyFlashcardDto>(due.Select(x => ToFlashcard(x.lv, x.t)));

        // Fill remainder with brand-new learner vocab cards (never reviewed) if count not reached.
        if (cards.Count < count)
        {
            var remainder = count - cards.Count;
            var newCards = await db.LearnerVocabularies
                .Where(lv => lv.UserId == userId && lv.ReviewCount == 0 && lv.Mastery == "new")
                .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
                .Where(x => !due.Select(d => d.lv.Id).Contains(x.lv.Id))
                .OrderBy(x => x.lv.AddedAt)
                .Take(remainder)
                .ToListAsync(ct);
            cards.AddRange(newCards.Select(x => ToFlashcard(x.lv, x.t)));
        }

        return new VocabularyDailySetResponse(
            Date: today,
            NewCount: cards.Count(c => c.Mastery == "new"),
            DueCount: cards.Count(c => c.Mastery != "new"),
            Cards: cards);
    }

    // ── Stats ───────────────────────────────────────────────────────────

    public async Task<VocabularyStatsResponse> GetStatsAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekEnd = today.AddDays(7);

        var totalInList = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId, ct);
        var mastered = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery == "mastered", ct);
        var reviewing = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery == "reviewing", ct);
        var learning = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery == "learning", ct);
        var newCount = await db.LearnerVocabularies.CountAsync(lv => lv.UserId == userId && lv.Mastery == "new", ct);

        var dueToday = await db.LearnerVocabularies.CountAsync(
            lv => lv.UserId == userId
              && lv.Mastery != "mastered"
              && (lv.NextReviewDate == null || lv.NextReviewDate <= today),
            ct);
        var dueThisWeek = await db.LearnerVocabularies.CountAsync(
            lv => lv.UserId == userId
              && lv.Mastery != "mastered"
              && lv.NextReviewDate != null
              && lv.NextReviewDate >= today
              && lv.NextReviewDate <= weekEnd,
            ct);

        var streakDays = await ComputeStreakAsync(userId, today, ct);

        var totalTermsInCatalog = await db.VocabularyTerms.CountAsync(t => t.Status == "active", ct);

        return new VocabularyStatsResponse(
            TotalInList: totalInList,
            Mastered: mastered,
            Reviewing: reviewing,
            Learning: learning,
            New: newCount,
            DueToday: dueToday,
            DueThisWeek: dueThisWeek,
            StreakDays: streakDays,
            TotalTermsInCatalog: totalTermsInCatalog);
    }

    private async Task<int> ComputeStreakAsync(string userId, DateOnly today, CancellationToken ct)
    {
        // Consecutive-day streak from today back, where each day has at least one
        // flashcard review OR a completed quiz.
        // Note: EF Core (PostgreSQL) cannot translate `.UtcDateTime.Date` on a
        // DateTimeOffset projection — coercion DateTimeOffset → DateTime? throws.
        // Project the raw DateTimeOffset values and convert client-side.
        var reviewTimestamps = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId && lv.LastReviewedAt != null)
            .Select(lv => lv.LastReviewedAt!.Value)
            .ToListAsync(ct);
        var quizTimestamps = await db.VocabularyQuizResults
            .Where(r => r.UserId == userId)
            .Select(r => r.CompletedAt)
            .ToListAsync(ct);

        var activeDays = new HashSet<DateTime>();
        foreach (var ts in reviewTimestamps) activeDays.Add(ts.UtcDateTime.Date);
        foreach (var ts in quizTimestamps) activeDays.Add(ts.UtcDateTime.Date);

        var streak = 0;
        var cursor = today;
        while (activeDays.Contains(cursor.ToDateTime(TimeOnly.MinValue)))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }

    // ── Quiz ────────────────────────────────────────────────────────────

    public async Task<VocabularyQuizResponse> GetQuizAsync(
        string userId,
        int count,
        string format,
        CancellationToken ct)
    {
        var normalisedFormat = NormaliseFormat(format);

        var allTerms = await db.VocabularyTerms
            .Where(t => t.Status == "active")
            .ToListAsync(ct);

        if (allTerms.Count == 0)
        {
            return new VocabularyQuizResponse(normalisedFormat, Array.Empty<VocabularyQuizQuestionDto>());
        }

        var myTermIds = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId)
            .Select(lv => lv.TermId)
            .ToListAsync(ct);

        var candidateTerms = myTermIds.Count >= count
            ? allTerms.Where(t => myTermIds.Contains(t.Id)).ToList()
            : allTerms;

        // Deterministic but unpredictable shuffle using Random.Shared seeded with
        // (userId, utcNowTicks) — avoids the OrderBy(Guid.NewGuid()) pattern.
        var rng = Random.Shared;
        var selected = candidateTerms
            .OrderBy(_ => rng.Next())
            .Take(Math.Min(count, candidateTerms.Count))
            .ToList();

        var questions = selected.Select(term => BuildQuizQuestion(term, allTerms, normalisedFormat, rng))
            .Where(q => q is not null)
            .Cast<VocabularyQuizQuestionDto>()
            .ToList();

        return new VocabularyQuizResponse(normalisedFormat, questions);
    }

    private static string NormaliseFormat(string? format)
    {
        return format?.ToLowerInvariant() switch
        {
            "fill_blank" or "fill-the-blank" or "fill" => "fill_blank",
            "synonym_match" or "synonym" => "synonym_match",
            "context_usage" or "context" => "context_usage",
            "audio_recognition" or "audio" => "audio_recognition",
            _ => "definition_match",
        };
    }

    private static VocabularyQuizQuestionDto? BuildQuizQuestion(
        VocabularyTerm term,
        List<VocabularyTerm> allTerms,
        string format,
        Random rng)
    {
        switch (format)
        {
            case "definition_match":
            {
                var distractors = allTerms
                    .Where(t => t.Id != term.Id && !string.IsNullOrWhiteSpace(t.Definition) && t.Definition != term.Definition)
                    .Select(t => t.Definition)
                    .Distinct()
                    .OrderBy(_ => rng.Next())
                    .Take(3)
                    .ToList();
                while (distractors.Count < 3) distractors.Add($"(no distractor {distractors.Count + 1})");
                var options = distractors.Append(term.Definition).OrderBy(_ => rng.Next()).ToList();
                return new VocabularyQuizQuestionDto(
                    TermId: term.Id,
                    Term: term.Term,
                    Format: format,
                    Prompt: term.Term,
                    Options: options,
                    CorrectIndex: options.FindIndex(o => o == term.Definition),
                    CorrectAnswer: term.Definition,
                    ExampleSentence: term.ExampleSentence,
                    AudioUrl: null);
            }

            case "fill_blank":
            {
                if (string.IsNullOrWhiteSpace(term.ExampleSentence)) return null;
                // Replace the first occurrence of the term (case-insensitive) with ____.
                var prompt = System.Text.RegularExpressions.Regex.Replace(
                    term.ExampleSentence,
                    System.Text.RegularExpressions.Regex.Escape(term.Term),
                    "______",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (prompt == term.ExampleSentence) return null;
                return new VocabularyQuizQuestionDto(
                    TermId: term.Id,
                    Term: term.Term,
                    Format: format,
                    Prompt: prompt,
                    Options: Array.Empty<string>(),
                    CorrectIndex: -1,
                    CorrectAnswer: term.Term,
                    ExampleSentence: term.ExampleSentence,
                    AudioUrl: null);
            }

            case "synonym_match":
            {
                var synonyms = ParseStringArray(term.SynonymsJson);
                if (synonyms.Count == 0) return null;
                var correct = synonyms[rng.Next(synonyms.Count)];

                var distractorPool = allTerms
                    .Where(t => t.Id != term.Id)
                    .SelectMany(t => ParseStringArray(t.SynonymsJson))
                    .Where(s => !string.IsNullOrWhiteSpace(s) && !synonyms.Contains(s, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(_ => rng.Next())
                    .Take(3)
                    .ToList();
                while (distractorPool.Count < 3)
                {
                    var fillerTerm = allTerms[rng.Next(allTerms.Count)];
                    if (!distractorPool.Contains(fillerTerm.Term, StringComparer.OrdinalIgnoreCase)
                        && !synonyms.Contains(fillerTerm.Term, StringComparer.OrdinalIgnoreCase))
                        distractorPool.Add(fillerTerm.Term);
                    else
                        distractorPool.Add($"(filler {distractorPool.Count + 1})");
                }
                var options = distractorPool.Append(correct).OrderBy(_ => rng.Next()).ToList();
                return new VocabularyQuizQuestionDto(
                    TermId: term.Id,
                    Term: term.Term,
                    Format: format,
                    Prompt: term.Term,
                    Options: options,
                    CorrectIndex: options.FindIndex(o => string.Equals(o, correct, StringComparison.OrdinalIgnoreCase)),
                    CorrectAnswer: correct,
                    ExampleSentence: term.ExampleSentence,
                    AudioUrl: null);
            }

            case "context_usage":
            {
                if (string.IsNullOrWhiteSpace(term.ExampleSentence)) return null;
                // Correct: sentence using THIS term.
                // Distractors: sentences from other terms.
                var distractors = allTerms
                    .Where(t => t.Id != term.Id && !string.IsNullOrWhiteSpace(t.ExampleSentence))
                    .OrderBy(_ => rng.Next())
                    .Take(3)
                    .Select(t => t.ExampleSentence)
                    .ToList();
                if (distractors.Count < 3) return null;
                var options = distractors.Append(term.ExampleSentence).OrderBy(_ => rng.Next()).ToList();
                return new VocabularyQuizQuestionDto(
                    TermId: term.Id,
                    Term: term.Term,
                    Format: format,
                    Prompt: $"Which sentence uses '{term.Term}' correctly?",
                    Options: options,
                    CorrectIndex: options.FindIndex(o => o == term.ExampleSentence),
                    CorrectAnswer: term.ExampleSentence,
                    ExampleSentence: term.ExampleSentence,
                    AudioUrl: null);
            }

            case "audio_recognition":
            {
                return new VocabularyQuizQuestionDto(
                    TermId: term.Id,
                    Term: term.Term,
                    Format: format,
                    Prompt: "Listen and type the word you hear.",
                    Options: Array.Empty<string>(),
                    CorrectIndex: -1,
                    CorrectAnswer: term.Term,
                    ExampleSentence: term.ExampleSentence,
                        AudioUrl: null);
            }

            default:
                return null;
        }
    }

    public async Task<VocabularyQuizSubmissionResponse> SubmitQuizAsync(
        string userId,
        VocabQuizSubmissionV2 submission,
        CancellationToken ct)
    {
        if (submission.Answers is null || submission.Answers.Count == 0)
            throw ApiException.Validation("EMPTY_QUIZ", "Submission must contain at least one answer.");

        var format = NormaliseFormat(submission.Format);
        var duration = Math.Max(0, submission.DurationSeconds ?? 0);

        var result = new VocabularyQuizResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TermsQuizzed = submission.Answers.Count,
            CorrectCount = submission.Answers.Count(a => a.Correct),
            DurationSeconds = duration,
            Format = format,
            ResultsJson = JsonSerializer.Serialize(submission.Answers),
            CompletedAt = DateTimeOffset.UtcNow,
        };
        db.VocabularyQuizResults.Add(result);

        var newlyMastered = new List<string>();
        foreach (var ans in submission.Answers)
        {
            var lv = await db.LearnerVocabularies.FirstOrDefaultAsync(
                x => x.UserId == userId && x.TermId == ans.TermId, ct);
            if (lv == null) continue;

            var quality = ans.Correct ? 4 : 2;
            var prevMastery = lv.Mastery;
            var update = scheduler.Schedule(
                new Sm2State(lv.EaseFactor, lv.IntervalDays, lv.ReviewCount, lv.CorrectCount),
                quality);
            lv.EaseFactor = update.EaseFactor;
            lv.IntervalDays = update.IntervalDays;
            lv.ReviewCount = update.ReviewCount;
            lv.CorrectCount = update.CorrectCount;
            lv.NextReviewDate = update.NextReviewDate;
            lv.Mastery = update.DeriveMastery();
            lv.LastReviewedAt = DateTimeOffset.UtcNow;

            if (prevMastery != "mastered" && lv.Mastery == "mastered")
                newlyMastered.Add(lv.TermId);
        }

        await db.SaveChangesAsync(ct);

        if (newlyMastered.Count > 0)
            await TryEvaluateAchievementsAsync(userId, "vocab_mastered", ct);

        // XP: 10 XP per correct answer + 25 XP completion bonus.
        var xp = result.CorrectCount * 10 + 25;
        var score = result.TermsQuizzed > 0
            ? Math.Round(100.0 * result.CorrectCount / result.TermsQuizzed, 1)
            : 0;

        return new VocabularyQuizSubmissionResponse(
            Id: result.Id,
            Format: result.Format,
            TermsQuizzed: result.TermsQuizzed,
            CorrectCount: result.CorrectCount,
            Score: score,
            DurationSeconds: result.DurationSeconds,
            XpAwarded: xp,
            CompletedAt: result.CompletedAt,
            NewlyMasteredTermIds: newlyMastered);
    }

    public async Task<VocabularyQuizHistoryResponse> GetQuizHistoryAsync(
        string userId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = db.VocabularyQuizResults.Where(r => r.UserId == userId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CompletedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new VocabularyQuizHistoryItem(
                r.Id,
                r.Format,
                r.TermsQuizzed,
                r.CorrectCount,
                r.TermsQuizzed > 0 ? Math.Round(100.0 * r.CorrectCount / r.TermsQuizzed, 1) : 0,
                r.DurationSeconds,
                r.CompletedAt))
            .ToListAsync(ct);

        return new VocabularyQuizHistoryResponse(total, page, pageSize, items);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Best-effort achievement re-evaluation. Never fails the caller — if the
    /// gamification service is unavailable or throws, the vocabulary action
    /// is still considered successful.
    /// </summary>
    private async Task TryEvaluateAchievementsAsync(string userId, string trigger, CancellationToken ct)
    {
        if (gamification is null) return;
        try
        {
            await gamification.CheckAndAwardAchievementsAsync(userId, trigger, ct);
        }
        catch
        {
            // Gamification must never break a vocabulary flow.
        }
    }

    private static VocabularyTermResponse Map(VocabularyTerm t) => new(
        Id: t.Id,
        Term: t.Term,
        Definition: t.Definition,
        ExampleSentence: t.ExampleSentence,
        ContextNotes: t.ContextNotes,
        ExamTypeCode: t.ExamTypeCode,
        ProfessionId: t.ProfessionId,
        Category: t.Category,
        Difficulty: t.Difficulty,
        IpaPronunciation: t.IpaPronunciation,
        AudioUrl: null,
        AudioMediaAssetId: null,
        ImageUrl: t.ImageUrl,
        Synonyms: ParseStringArray(t.SynonymsJson).ToArray(),
        Collocations: ParseStringArray(t.CollocationsJson).ToArray(),
        RelatedTerms: ParseStringArray(t.RelatedTermsJson).ToArray(),
        SourceProvenance: t.SourceProvenance,
        Status: t.Status);

    private static VocabularyTermSummary MapSummary(VocabularyTerm t) => new(
        Id: t.Id,
        Term: t.Term,
        Definition: t.Definition,
        Category: t.Category,
        Difficulty: t.Difficulty,
        IpaPronunciation: t.IpaPronunciation,
        AudioUrl: null,
        ExampleSentence: t.ExampleSentence);

    private static MyVocabularyItem ToMyItem(LearnerVocabulary lv, VocabularyTerm term) => new(
        Id: lv.Id,
        TermId: lv.TermId,
        Term: term.Term,
        Definition: term.Definition,
        Mastery: lv.Mastery,
        EaseFactor: lv.EaseFactor,
        IntervalDays: lv.IntervalDays,
        ReviewCount: lv.ReviewCount,
        CorrectCount: lv.CorrectCount,
        NextReviewDate: lv.NextReviewDate,
        DueAt: lv.NextReviewDate?.ToString("yyyy-MM-dd"),
        LastReviewedAt: lv.LastReviewedAt,
        AddedAt: lv.AddedAt,
        SourceRef: lv.SourceRef);

    private static VocabularyFlashcardDto ToFlashcard(LearnerVocabulary lv, VocabularyTerm term) => new(
        Id: lv.Id,
        TermId: term.Id,
        Term: term.Term,
        Definition: term.Definition,
        ExampleSentence: term.ExampleSentence,
        ContextNotes: term.ContextNotes,
        IpaPronunciation: term.IpaPronunciation,
        AudioUrl: null,
        Synonyms: ParseStringArray(term.SynonymsJson).ToArray(),
        Mastery: lv.Mastery);

    private static List<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json!) ?? new();
        }
        catch
        {
            return new();
        }
    }
}

// Legacy record kept for backward compatibility with older call sites.
public record VocabQuizSubmission(
    List<VocabQuizAnswer> Answers,
    int DurationSeconds);

public record VocabQuizAnswer(
    string TermId,
    bool Correct,
    string? UserAnswer);
