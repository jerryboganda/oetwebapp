using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Common;

namespace OetLearner.Api.Services;

/// <summary>
/// Learner-facing vocabulary service. Source of truth: docs/VOCABULARY-MODULE.md.
/// All SM-2 scheduling is delegated to <see cref="ISpacedRepetitionScheduler"/>.
/// All AI calls are delegated to <see cref="VocabularyGlossService"/> (gateway-bound).
/// </summary>
public class VocabularyService(
    LearnerDbContext db,
    ISpacedRepetitionScheduler scheduler,
    GamificationService? gamification = null,
    OetLearner.Api.Services.Readiness.ReadinessComputationService? readinessComputation = null)
{
    private const int FreeListSizeCap = 500;
    private const int QuizMaxQuestionCount = 25;
    private const int QuizMaterializationCap = 200;
    private const int StreakDateScanCap = 3_660;

    // ── Browse terms ─────────────────────────────────────────────────────

    public async Task<VocabularyTermsPageResponse> GetTermsAsync(
        string? examTypeCode,
        string? category,
        string? profession,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct,
        string? recallSet = null,
        bool isPremium = true,
        bool freePreviewOnly = false,
        string? userId = null)
    {
        examTypeCode = ExamCodes.NormalizeOrNull(examTypeCode);
        var query = db.VocabularyTerms.AsNoTracking().Where(t => t.Status == "active");
        query = ApplyExamTypeFilter(query, examTypeCode);
        if (!string.IsNullOrEmpty(category)) query = query.Where(t => t.Category == category);
        if (!string.IsNullOrEmpty(profession)) query = query.Where(t => t.ProfessionId == profession);

        // Free-preview filter — the "Free Preview Recalls" chip. Returns only the
        // admin-curated preview subset (accessible in full to every logged-in
        // learner regardless of subscription; see Map(isLocked) below, which never
        // locks a preview term). Uses IX_VocabularyTerms_ExamTypeCode_Status_IsFreePreview.
        if (freePreviewOnly) query = query.Where(t => t.IsFreePreview);
        if (!string.IsNullOrEmpty(search))
        {
            var s = search.Trim();
            query = query.Where(t => t.Term.Contains(s) || t.Definition.Contains(s));
        }

        // Recall-set filter — string-contains over the JSON array column. Cheap
        // first-cut; if hot we move it to a normalised join table later.
        // Accept ANY recall-set code, not just the 3 canonical RecallSetCodes
        // values — admins can tag terms with custom recall-set codes, and
        // restricting this filter to the canonical set would silently return
        // everything unfiltered for any custom code.
        if (!string.IsNullOrWhiteSpace(recallSet))
        {
            var needle = $"\"{recallSet.Trim().ToLowerInvariant()}\"";
            query = query.Where(t => t.RecallSetCodesJson.Contains(needle));
        }

        // UserRecallSetAccess is the normalized allow-list relation. Keep the
        // unrestricted "no rows" contract, but perform the quoted JSON-token
        // intersection in SQL so restricted users never materialize the catalog.
        // RecallSetTag.Code and the persisted JSON codes are lowercase by schema
        // contract; ToLower also preserves the old case-insensitive behavior for
        // legacy rows.
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var allowedRecallSets = db.UserRecallSetAccesses
                .AsNoTracking()
                .Where(x => x.UserId == userId);

            query = query.Where(t =>
                !allowedRecallSets.Any()
                || allowedRecallSets.Any(access =>
                    t.RecallSetCodesJson.ToLower().Contains(
                        "\"" + access.RecallSetCode.ToLower() + "\"")));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.Term)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        // Free learners only get full content for curated free-preview terms.
        // All other terms are returned locked (content redacted) so the UI can
        // render a blurred placeholder + subscribe prompt without leaking data.
        var mapped = items.Select(t => Map(t, isLocked: !isPremium && !t.IsFreePreview)).ToList();
        return new VocabularyTermsPageResponse(total, page, pageSize, mapped, mapped);
    }

    /// <summary>
    /// Recall-set registry with live counts. Returns the canonical set list from
    /// <see cref="OetLearner.Api.Domain.RecallSetCodes"/> (so the UI is stable
    /// even before any term is tagged) joined with the per-set count of active
    /// vocabulary terms carrying that code.
    /// </summary>
    public async Task<RecallSetsListResponse> GetRecallSetsAsync(
        string? examTypeCode,
        string? profession,
        CancellationToken ct)
    {
        examTypeCode = ExamCodes.NormalizeOrNull(examTypeCode);
        var resolvedExam = examTypeCode ?? ExamCodes.DefaultCode;

        var query = db.VocabularyTerms.AsNoTracking().Where(t => t.Status == "active");
        query = ApplyExamTypeFilter(query, examTypeCode);
        if (!string.IsNullOrWhiteSpace(profession)) query = query.Where(t => t.ProfessionId == profession);

        // Aggregate identical tag payloads in SQL. This returns one row per
        // distinct tag combination (plus its multiplicity), rather than pulling
        // one JSON payload for every active term on every registry request.
        var tallyRows = await query
            .GroupBy(t => t.RecallSetCodesJson)
            .Select(g => new
            {
                CodesJson = g.Key,
                TermCount = g.Count(),
                FreePreviewCount = g.Count(t => t.IsFreePreview),
            })
            .ToListAsync(ct);

        var freePreviewCount = tallyRows.Sum(x => x.FreePreviewCount);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in tallyRows)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(row.CodesJson) ?? new List<string>();
                foreach (var raw in list)
                {
                    var c = raw?.Trim();
                    if (string.IsNullOrEmpty(c)) continue;
                    counts[c] = counts.TryGetValue(c, out var n) ? n + row.TermCount : row.TermCount;
                }
            }
            catch { /* malformed row ignored */ }
        }

        // Read DB-managed tags (seeded with canonical codes on first boot;
        // admins can add more from /admin/content/vocabulary/recall-set-tags).
        // Fall back to the static metadata only if the table is empty.
        var tagRows = await db.RecallSetTags.AsNoTracking()
            .Where(t => t.IsActive)
            .Where(t => t.ExamTypeCode == null
                        || t.ExamTypeCode.ToUpper() == resolvedExam)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.DisplayName)
            .ToListAsync(ct);

        IEnumerable<RecallSetSummaryResponse> sets;
        if (tagRows.Count > 0)
        {
            sets = tagRows.Select(t => new RecallSetSummaryResponse(
                Code: t.Code,
                DisplayName: t.DisplayName,
                ShortLabel: t.ShortLabel ?? t.Code,
                Description: t.Description ?? string.Empty,
                SortOrder: t.SortOrder,
                TermCount: counts.TryGetValue(t.Code, out var n) ? n : 0));
        }
        else
        {
            sets = OetLearner.Api.Domain.RecallSetCodes.Metadata
                .OrderBy(m => m.SortOrder)
                .Select(m => new RecallSetSummaryResponse(
                    Code: m.Code,
                    DisplayName: m.DisplayName,
                    ShortLabel: m.ShortLabel,
                    Description: m.Description,
                    SortOrder: m.SortOrder,
                    TermCount: counts.TryGetValue(m.Code, out var n) ? n : 0));
        }

        return new RecallSetsListResponse(resolvedExam, profession, sets.ToList(), freePreviewCount);
    }

    public async Task<VocabularyTermResponse> GetTermAsync(string termId, CancellationToken ct, bool isPremium = true)
    {
        var term = await db.VocabularyTerms.FindAsync([termId], ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Vocabulary term not found.");
        return Map(term, isLocked: !isPremium && !term.IsFreePreview);
    }

    public async Task<VocabularyLookupResult> LookupAsync(string query, string? examTypeCode, CancellationToken ct, bool isPremium = true)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new VocabularyLookupResult(false, null, Array.Empty<VocabularyTermSummary>());

        var normalised = query.Trim().ToLowerInvariant();
        examTypeCode = ExamCodes.NormalizeOrNull(examTypeCode);
        var baseQuery = db.VocabularyTerms.Where(t => t.Status == "active");
        baseQuery = ApplyExamTypeFilter(baseQuery, examTypeCode);

        // Exact match first (case-insensitive).
        var exact = await baseQuery.FirstOrDefaultAsync(t => t.Term.ToLower() == normalised, ct);
        if (exact is not null)
            return new VocabularyLookupResult(
                true,
                Map(exact, isLocked: !isPremium && !exact.IsFreePreview),
                Array.Empty<VocabularyTermSummary>());

        // Prefix + contains suggestions (top 10 by prefix match first).
        var suggestionsQuery = baseQuery;
        if (!isPremium)
        {
            suggestionsQuery = suggestionsQuery.Where(t => t.IsFreePreview);
        }

        var suggestions = await suggestionsQuery
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
        examTypeCode = ExamCodes.NormalizeOrNull(examTypeCode);
        var query = db.VocabularyTerms.Where(t => t.Status == "active");
        query = ApplyExamTypeFilter(query, examTypeCode);
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
            ExamTypeCode: examTypeCode ?? ExamCodes.DefaultCode,
            ProfessionId: profession,
            Categories: grouped);
    }

    // ── Learner vocabulary list ───────────────────────────────────────────

    public async Task<IReadOnlyList<MyVocabularyItem>> GetMyVocabularyAsync(
        string userId,
        string? mastery,
        CancellationToken ct,
        bool isPremium = true,
        string? termId = null)
    {
        var rows = await BuildMyVocabularyQuery(userId, mastery, isPremium, termId)
            .ToListAsync(ct);
        return rows.Select(ToMyItem).ToList();
    }

    public async Task<MyVocabularyPageResponse> GetMyVocabularyPageAsync(
        string userId,
        string? mastery,
        int page,
        int pageSize,
        CancellationToken ct,
        bool isPremium = true,
        string? termId = null)
    {
        var query = BuildMyVocabularyQuery(userId, mastery, isPremium, termId);
        var total = await query.CountAsync(ct);
        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new MyVocabularyPageResponse(
            Total: total,
            Page: page,
            PageSize: pageSize,
            Items: rows.Select(ToMyItem).ToList());
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

        // Free learners may only add curated free-preview terms. Locked terms are
        // gated until they subscribe.
        if (!isPremium && !term.IsFreePreview)
        {
            throw ApiException.PaymentRequired("RECALL_PREVIEW_LOCKED",
                "Subscribe to unlock the full Recall Vocabulary Bank.");
        }

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
        CancellationToken ct,
        bool isPremium = true)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var due = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId
                && lv.Mastery != "mastered"
                && (lv.NextReviewDate == null || lv.NextReviewDate <= today))
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
            .Where(x => isPremium || x.t.IsFreePreview)
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
        CancellationToken ct,
        bool isPremium = true)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Due cards first.
        var due = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId
                && lv.Mastery != "mastered"
                && (lv.NextReviewDate == null || lv.NextReviewDate <= today))
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
            .Where(x => isPremium || x.t.IsFreePreview)
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
                .Where(x => isPremium || x.t.IsFreePreview)
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

        var stats = await db.LearnerVocabularies
            .AsNoTracking()
            .Where(lv => lv.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalInList = g.Count(),
                Mastered = g.Count(lv => lv.Mastery == "mastered"),
                Reviewing = g.Count(lv => lv.Mastery == "reviewing"),
                Learning = g.Count(lv => lv.Mastery == "learning"),
                NewCount = g.Count(lv => lv.Mastery == "new"),
                DueToday = g.Count(lv =>
                    lv.Mastery != "mastered"
                    && (lv.NextReviewDate == null || lv.NextReviewDate <= today)),
                DueThisWeek = g.Count(lv =>
                    lv.Mastery != "mastered"
                    && lv.NextReviewDate != null
                    && lv.NextReviewDate >= today
                    && lv.NextReviewDate <= weekEnd),
            })
            .SingleOrDefaultAsync(ct);

        var streakDays = await ComputeStreakAsync(userId, today, ct);
        var totalTermsInCatalog = await db.VocabularyTerms
            .AsNoTracking()
            .CountAsync(t => t.Status == "active", ct);

        return new VocabularyStatsResponse(
            TotalInList: stats?.TotalInList ?? 0,
            Mastered: stats?.Mastered ?? 0,
            Reviewing: stats?.Reviewing ?? 0,
            Learning: stats?.Learning ?? 0,
            New: stats?.NewCount ?? 0,
            DueToday: stats?.DueToday ?? 0,
            DueThisWeek: stats?.DueThisWeek ?? 0,
            StreakDays: streakDays,
            TotalTermsInCatalog: totalTermsInCatalog);
    }

    private async Task<int> ComputeStreakAsync(string userId, DateOnly today, CancellationToken ct)
    {
        // Consecutive-day streak from today back, where each day has at least one
        // flashcard review OR a completed quiz. All service writes use UTC
        // timestamps, so Date preserves the prior UtcDateTime.Date outcome while
        // allowing relational providers to deduplicate dates in SQL. The exact
        // scan cap is 3,660 calendar days (ten years plus leap-day headroom).
        var firstDay = today.AddDays(-(StreakDateScanCap - 1))
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var lastDay = today.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var reviewDays = db.LearnerVocabularies
            .AsNoTracking()
            .Where(lv => lv.UserId == userId && lv.LastReviewedAt != null)
            .Select(lv => lv.LastReviewedAt!.Value.Date);
        var quizDays = db.VocabularyQuizResults
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.CompletedAt.Date);

        var activeDays = await reviewDays
            .Concat(quizDays)
            .Where(day => day >= firstDay && day <= lastDay)
            .Distinct()
            .OrderByDescending(day => day)
            .Take(StreakDateScanCap)
            .ToListAsync(ct);

        var activeDaySet = activeDays.ToHashSet();

        var streak = 0;
        var cursor = today;
        while (activeDaySet.Contains(cursor.ToDateTime(TimeOnly.MinValue)))
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
        CancellationToken ct,
        bool isPremium = true)
    {
        var normalisedFormat = NormaliseFormat(format);
        var questionCount = Math.Clamp(count, 0, QuizMaxQuestionCount);

        var eligibleTerms = db.VocabularyTerms
            .AsNoTracking()
            .Where(t => t.Status == "active")
            .Where(t => isPremium || t.IsFreePreview);

        var eligibleCount = await eligibleTerms.CountAsync(ct);
        if (eligibleCount == 0 || questionCount == 0)
        {
            return new VocabularyQuizResponse(normalisedFormat, Array.Empty<VocabularyQuizQuestionDto>());
        }

        var myTermIds = db.LearnerVocabularies
            .AsNoTracking()
            .Where(lv => lv.UserId == userId)
            .Select(lv => lv.TermId);

        var ownedEligibleCount = await eligibleTerms.CountAsync(t => myTermIds.Contains(t.Id), ct);
        var candidateQuery = ownedEligibleCount >= questionCount
            ? eligibleTerms.Where(t => myTermIds.Contains(t.Id))
            : eligibleTerms;
        var candidateCount = ownedEligibleCount >= questionCount
            ? ownedEligibleCount
            : eligibleCount;

        var rng = Random.Shared;
        var selected = await LoadBoundedQuizWindowAsync(
            candidateQuery,
            candidateCount,
            Math.Min(questionCount, candidateCount),
            rng,
            ct);
        ShuffleInPlace(selected, rng);

        // Keep the complete candidate+distractor materialization at or below 200
        // rows (175 distractors for the endpoint maximum of 25 questions). A
        // random indexed window avoids a full-catalog random ORDER BY.
        var selectedIds = selected.Select(t => t.Id).ToArray();
        var remainingCount = eligibleCount - selected.Count;
        var distractorTake = Math.Min(QuizMaterializationCap - selected.Count, remainingCount);
        var distractors = await LoadBoundedQuizWindowAsync(
            eligibleTerms.Where(t => !selectedIds.Contains(t.Id)),
            remainingCount,
            distractorTake,
            rng,
            ct);
        var quizPool = selected.Concat(distractors).ToList();

        var questions = selected.Select(term => BuildQuizQuestion(term, quizPool, normalisedFormat, rng))
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
                    .ToList();
                distractors = SampleDistinct(distractors, 3, rng);
                while (distractors.Count < 3) distractors.Add($"(no distractor {distractors.Count + 1})");
                var options = distractors.Append(term.Definition).ToList();
                ShuffleInPlace(options, rng);
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
                    .ToList();
                distractorPool = SampleDistinct(distractorPool, 3, rng);
                while (distractorPool.Count < 3)
                {
                    var fillerTerm = allTerms[rng.Next(allTerms.Count)];
                    if (!distractorPool.Contains(fillerTerm.Term, StringComparer.OrdinalIgnoreCase)
                        && !synonyms.Contains(fillerTerm.Term, StringComparer.OrdinalIgnoreCase))
                        distractorPool.Add(fillerTerm.Term);
                    else
                        distractorPool.Add($"(filler {distractorPool.Count + 1})");
                }
                var options = distractorPool.Append(correct).ToList();
                ShuffleInPlace(options, rng);
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
                    .ToList();
                distractors = SampleDistinct(distractors, 3, rng);
                if (distractors.Count < 3) return null;
                var options = distractors
                    .Select(t => t.ExampleSentence)
                    .Append(term.ExampleSentence)
                    .ToList();
                ShuffleInPlace(options, rng);
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
        var submittedTermIds = submission.Answers
            .Select(answer => answer.TermId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var learnerVocabularyByTermId = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId && submittedTermIds.Contains(lv.TermId))
            .ToDictionaryAsync(lv => lv.TermId, StringComparer.Ordinal, ct);

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
            if (!learnerVocabularyByTermId.TryGetValue(ans.TermId, out var lv)) continue;

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

        // Refresh readiness snapshot so vocabulary readiness dimension reflects this quiz.
        if (readinessComputation is not null)
        {
            try
            {
                await readinessComputation.ComputeAsync(userId, ct);
            }
            catch
            {
                // Readiness recompute failures must not break quiz submission.
            }
        }

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

    private IQueryable<MyVocabularyRow> BuildMyVocabularyQuery(
        string userId,
        string? mastery,
        bool isPremium,
        string? termId)
    {
        var query = db.LearnerVocabularies
            .AsNoTracking()
            .Where(lv => lv.UserId == userId)
            .Join(
                db.VocabularyTerms.AsNoTracking(),
                lv => lv.TermId,
                term => term.Id,
                (lv, term) => new { lv, term });

        if (!string.IsNullOrEmpty(mastery))
            query = query.Where(x => x.lv.Mastery == mastery);
        if (!string.IsNullOrWhiteSpace(termId))
            query = query.Where(x => x.lv.TermId == termId);
        if (!isPremium)
            query = query.Where(x => x.term.IsFreePreview);

        return query
            .OrderBy(x => x.lv.NextReviewDate)
            .ThenBy(x => x.lv.Id)
            .Select(x => new MyVocabularyRow(
                x.lv.Id,
                x.lv.TermId,
                x.term.Term,
                x.term.Definition,
                x.lv.Mastery,
                x.lv.EaseFactor,
                x.lv.IntervalDays,
                x.lv.ReviewCount,
                x.lv.CorrectCount,
                x.lv.NextReviewDate,
                x.lv.LastReviewedAt,
                x.lv.AddedAt,
                x.lv.SourceRef));
    }

    private static async Task<List<VocabularyTerm>> LoadBoundedQuizWindowAsync(
        IQueryable<VocabularyTerm> query,
        int sourceCount,
        int take,
        Random rng,
        CancellationToken ct)
    {
        take = Math.Min(Math.Max(0, take), sourceCount);
        if (take == 0) return [];

        // Pick an indexed contiguous window that always fits in one query. The
        // window is shuffled client-side after materialization; no random DB sort.
        var maxStart = sourceCount - take;
        var start = maxStart == 0 ? 0 : rng.Next(maxStart + 1);
        return await query
            .OrderBy(term => term.Id)
            .Skip(start)
            .Take(take)
            .ToListAsync(ct);
    }

    private static List<T> SampleDistinct<T>(IReadOnlyList<T> source, int count, Random rng)
    {
        var sample = source.ToList();
        var take = Math.Min(count, sample.Count);
        for (var i = 0; i < take; i++)
        {
            var swapIndex = rng.Next(i, sample.Count);
            (sample[i], sample[swapIndex]) = (sample[swapIndex], sample[i]);
        }
        if (sample.Count > take) sample.RemoveRange(take, sample.Count - take);
        return sample;
    }

    private static void ShuffleInPlace<T>(IList<T> items, Random rng)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var swapIndex = rng.Next(i + 1);
            (items[i], items[swapIndex]) = (items[swapIndex], items[i]);
        }
    }

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

    private static VocabularyTermResponse Map(VocabularyTerm t, bool isLocked = false)
    {
        if (isLocked)
        {
            // Redacted projection for free learners on non-preview terms. Only
            // non-sensitive scaffolding is returned so the UI can render a
            // blurred placeholder + subscribe prompt. No definition, example,
            // audio, IPA, synonyms or provenance ever leaves the server.
            return new VocabularyTermResponse(
                Id: t.Id,
                Term: t.Term,
                Definition: null,
                ExampleSentence: string.Empty,
                ContextNotes: null,
                ExamTypeCode: t.ExamTypeCode,
                ProfessionId: t.ProfessionId,
                Category: t.Category,
                IpaPronunciation: null,
                AmericanSpelling: null,
                AudioUrl: null,
                AudioSlowUrl: null,
                AudioSentenceUrl: null,
                AudioMediaAssetId: null,
                ImageUrl: null,
                Synonyms: Array.Empty<string>(),
                Collocations: Array.Empty<string>(),
                RelatedTerms: Array.Empty<string>(),
                SourceProvenance: null,
                Status: t.Status,
                RecallSetCodes: ParseStringArray(t.RecallSetCodesJson).ToArray(),
                ExamFrequencyCount: t.ExamFrequencyCount,
                IsFreePreview: false,
                IsLocked: true);
        }

        return new(
            Id: t.Id,
            Term: t.Term,
            Definition: t.Definition,
            ExampleSentence: t.ExampleSentence,
            ContextNotes: t.ContextNotes,
            ExamTypeCode: t.ExamTypeCode,
            ProfessionId: t.ProfessionId,
            Category: t.Category,
            IpaPronunciation: t.IpaPronunciation,
            AmericanSpelling: t.AmericanSpelling,
            AudioUrl: IsInternalAudioReference(t.AudioUrl) ? null : t.AudioUrl,
            AudioSlowUrl: IsInternalAudioReference(t.AudioSlowUrl) ? null : t.AudioSlowUrl,
            AudioSentenceUrl: IsInternalAudioReference(t.AudioSentenceUrl) ? null : t.AudioSentenceUrl,
            AudioMediaAssetId: null,
            ImageUrl: t.ImageUrl,
            Synonyms: ParseStringArray(t.SynonymsJson).ToArray(),
            Collocations: ParseStringArray(t.CollocationsJson).ToArray(),
            RelatedTerms: ParseStringArray(t.RelatedTermsJson).ToArray(),
            SourceProvenance: t.SourceProvenance,
            Status: t.Status,
            RecallSetCodes: ParseStringArray(t.RecallSetCodesJson).ToArray(),
            ExamFrequencyCount: t.ExamFrequencyCount,
            IsFreePreview: t.IsFreePreview,
            IsLocked: false,
            RecallSetOccurrences: ParseRecallSetOccurrences(t.RecallSetOccurrencesJson));
    }

    private static bool IsInternalAudioReference(string? audioUrl)
        => !string.IsNullOrWhiteSpace(audioUrl)
           && (audioUrl.StartsWith("/", StringComparison.Ordinal)
               || !Uri.TryCreate(audioUrl, UriKind.Absolute, out _));

    private static IQueryable<VocabularyTerm> ApplyExamTypeFilter(IQueryable<VocabularyTerm> query, string? examTypeCode)
    {
        var normalised = NormaliseExamTypeCode(examTypeCode);
        return normalised is null
            ? query
            : query.Where(t => t.ExamTypeCode.ToLower() == normalised);
    }

    private static string? NormaliseExamTypeCode(string? examTypeCode)
        => string.IsNullOrWhiteSpace(examTypeCode)
            ? null
            : examTypeCode.Trim().ToLowerInvariant();

    private static VocabularyTermSummary MapSummary(VocabularyTerm t) => new(
        Id: t.Id,
        Term: t.Term,
        Definition: t.Definition,
        Category: t.Category,
        IpaPronunciation: t.IpaPronunciation,
        AmericanSpelling: t.AmericanSpelling,
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

    private static MyVocabularyItem ToMyItem(MyVocabularyRow row) => new(
        Id: row.Id,
        TermId: row.TermId,
        Term: row.Term,
        Definition: row.Definition!,
        Mastery: row.Mastery,
        EaseFactor: row.EaseFactor,
        IntervalDays: row.IntervalDays,
        ReviewCount: row.ReviewCount,
        CorrectCount: row.CorrectCount,
        NextReviewDate: row.NextReviewDate,
        DueAt: row.NextReviewDate?.ToString("yyyy-MM-dd"),
        LastReviewedAt: row.LastReviewedAt,
        AddedAt: row.AddedAt,
        SourceRef: row.SourceRef);

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
        Mastery: lv.Mastery,
        ExamFrequencyCount: term.ExamFrequencyCount,
        RecallSetOccurrences: ParseRecallSetOccurrences(term.RecallSetOccurrencesJson));

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

    /// <summary>
    /// Parses the per-set occurrence map (set code → count) for the ×N badge
    /// breakdown. Returns null for an empty/malformed map so the DTO omits it.
    /// </summary>
    private static IReadOnlyDictionary<string, int>? ParseRecallSetOccurrences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(json!);
            return map is { Count: > 0 } ? map : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record MyVocabularyRow(
        Guid Id,
        string TermId,
        string Term,
        string? Definition,
        string Mastery,
        double EaseFactor,
        int IntervalDays,
        int ReviewCount,
        int CorrectCount,
        DateOnly? NextReviewDate,
        DateTimeOffset? LastReviewedAt,
        DateTimeOffset AddedAt,
        string? SourceRef);
}

// Legacy record kept for backward compatibility with older call sites.
public record VocabQuizSubmission(
    List<VocabQuizAnswer> Answers,
    int DurationSeconds);

public record VocabQuizAnswer(
    string TermId,
    bool Correct,
    string? UserAnswer);
