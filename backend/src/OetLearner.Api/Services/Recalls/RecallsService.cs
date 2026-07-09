using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Services.Recalls;

/// <summary>
/// Aggregator service for the unified Recalls surface (vocabulary + generic
/// spaced-repetition). Composes <see cref="VocabularyService"/> and
/// <see cref="SpacedRepetitionService"/> — never duplicates SM-2 logic
/// (scheduling stays in <see cref="ISpacedRepetitionScheduler"/>).
///
/// See <c>docs/RECALLS-MODULE-PLAN.md</c> §3 for the API contract.
/// </summary>
public sealed class RecallsService(
    LearnerDbContext db,
    IFileStorage storage,
    IAiGatewayService gateway)
{
    /// <summary>Today snapshot — combines vocab + review counters.</summary>
    public async Task<RecallsTodayResponse> GetTodayAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var vocabDueToday = await db.LearnerVocabularies
            .CountAsync(lv => lv.UserId == userId && lv.NextReviewDate <= today, ct);
        var vocabMastered = await db.LearnerVocabularies
            .CountAsync(lv => lv.UserId == userId && lv.Mastery == "mastered", ct);
        var vocabTotal = await db.LearnerVocabularies
            .CountAsync(lv => lv.UserId == userId, ct);
        var vocabStarred = await db.LearnerVocabularies
            .CountAsync(lv => lv.UserId == userId && lv.Starred, ct);

        var reviewDueToday = await db.ReviewItems
            .CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueDate == today, ct);
        var reviewMastered = await db.ReviewItems
            .CountAsync(r => r.UserId == userId && r.Status == "mastered", ct);
        var reviewTotal = await db.ReviewItems
            .CountAsync(r => r.UserId == userId && r.Status == "active", ct);
        var reviewStarred = await db.ReviewItems
            .CountAsync(r => r.UserId == userId && r.Starred, ct);

        // Top weak categories by error rate (last 30 reviews).
        var recent = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId && lv.LastReviewedAt != null)
            .OrderByDescending(lv => lv.LastReviewedAt)
            .Take(50)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t.Category })
            .ToListAsync(ct);

        var weakTopics = recent
            .GroupBy(x => x.Category)
            .Select(g => new RecallsWeakTopic(
                g.Key,
                g.Count(),
                g.Count(x => x.lv.ReviewCount > 0 && x.lv.CorrectCount * 1.0 / x.lv.ReviewCount < 0.7)))
            .OrderByDescending(t => t.WeakCount)
            .Take(5)
            .ToList();

        var readiness = ComputeReadiness(vocabTotal + reviewTotal, vocabMastered + reviewMastered, weakTopics);

        return new RecallsTodayResponse(
            DueToday: vocabDueToday + reviewDueToday,
            Mastered: vocabMastered + reviewMastered,
            Total: vocabTotal + reviewTotal,
            Starred: vocabStarred + reviewStarred,
            VocabDueToday: vocabDueToday,
            ReviewDueToday: reviewDueToday,
            ReadinessScore: readiness,
            WeakTopics: weakTopics);
    }

    /// <summary>
    /// True when the term is an active, admin-curated free-preview recall word.
    /// Free / unsubscribed learners are allowed full functionality (incl. audio)
    /// on these terms; everything else stays paywalled.
    /// </summary>
    public Task<bool> IsFreePreviewTermAsync(string termId, CancellationToken ct)
        => db.VocabularyTerms.AsNoTracking()
            .AnyAsync(t => t.Id == termId && t.Status == "active" && t.IsFreePreview, ct);

    /// <summary>
    /// Mixed queue of vocab cards + review items, ordered by due date with
    /// starred items prioritised when due. Non-premium learners see only
    /// free-preview vocab cards (locked content never leaks into the queue).
    /// </summary>
    public async Task<List<RecallsQueueItem>> GetQueueAsync(string userId, int limit, bool isPremium, CancellationToken ct)
    {
        if (limit <= 0) limit = 20;
        if (limit > 100) limit = 100;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var vocabQuery = db.LearnerVocabularies
            .Where(lv => lv.UserId == userId && lv.NextReviewDate <= today)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t });
        if (!isPremium) vocabQuery = vocabQuery.Where(x => x.t.IsFreePreview);
        var vocabCards = await vocabQuery
            .OrderByDescending(x => x.lv.Starred)
            .ThenBy(x => x.lv.NextReviewDate)
            .Take(limit)
            .ToListAsync(ct);

        var reviewItems = await db.ReviewItems
            .Where(r => r.UserId == userId && r.Status == "active" && r.DueDate <= today)
            .OrderByDescending(r => r.Starred)
            .ThenBy(r => r.DueDate)
            .Take(limit)
            .ToListAsync(ct);

        var queue = new List<RecallsQueueItem>(vocabCards.Count + reviewItems.Count);
        queue.AddRange(vocabCards.Select(x => new RecallsQueueItem(
            Kind: "vocab",
            Id: x.lv.Id.ToString(),
            TermId: x.t.Id,
            Title: x.t.Term,
            Subtitle: x.t.Definition,
            DueDate: x.lv.NextReviewDate,
            Starred: x.lv.Starred,
            StarReason: x.lv.StarReason,
            Mastery: x.lv.Mastery,
            Ipa: x.t.IpaPronunciation,
            ExtraJson: null)));
        queue.AddRange(reviewItems.Select(r => new RecallsQueueItem(
            Kind: "review",
            Id: r.Id,
            TermId: null,
            Title: r.SourceType,
            Subtitle: null,
            DueDate: r.DueDate,
            Starred: r.Starred,
            StarReason: r.StarReason,
            Mastery: r.Status,
            Ipa: null,
            ExtraJson: r.QuestionJson)));

        return queue
            .OrderByDescending(q => q.Starred)
            .ThenBy(q => q.DueDate)
            .Take(limit)
            .ToList();
    }

    public async Task<object> StarAsync(string userId, RecallsStarRequest request, bool isPremium, CancellationToken ct)
    {
        ValidateReason(request.Reason);
        var changed = 0;

        if (string.Equals(request.Kind, "vocab", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(request.Id, out var lvId))
                throw ApiException.Validation("INVALID_ID", "Vocab id must be a guid.");
            var card = await db.LearnerVocabularies.FirstOrDefaultAsync(lv => lv.Id == lvId && lv.UserId == userId, ct)
                ?? throw ApiException.NotFound("RECALL_NOT_FOUND", "Card not found.");
            card.Starred = request.Starred;
            card.StarReason = request.Starred ? request.Reason : null;
            changed++;
        }
        else if (string.Equals(request.Kind, "term", StringComparison.OrdinalIgnoreCase))
        {
            // Favourite directly from the catalog by vocabulary term id. The
            // learner may not have a personal card yet, so create one on first
            // favourite (a card is the per-user home for a favourited term).
            var term = await db.VocabularyTerms.FirstOrDefaultAsync(t => t.Id == request.Id, ct)
                ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");
            var card = await db.LearnerVocabularies.FirstOrDefaultAsync(
                lv => lv.UserId == userId && lv.TermId == term.Id, ct);
            if (card is null)
            {
                if (!request.Starred)
                {
                    // Nothing to unfavourite — no card exists. No-op.
                    return new { changed = 0, starred = false };
                }
                // Favouriting seeds a personal card. Free learners may only seed
                // cards for curated free-preview terms — mirrors AddToMyVocabularyAsync
                // so the /star path can't pull a locked term into a free user's list.
                if (!isPremium && !term.IsFreePreview)
                {
                    throw ApiException.PaymentRequired("RECALL_PREVIEW_LOCKED",
                        "Subscribe to unlock the full Recall Vocabulary Bank.");
                }
                card = new LearnerVocabulary
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TermId = term.Id,
                    Mastery = "new",
                    NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    AddedAt = DateTimeOffset.UtcNow,
                    SourceRef = "browse",
                };
                db.LearnerVocabularies.Add(card);
            }
            card.Starred = request.Starred;
            card.StarReason = request.Starred ? request.Reason : null;
            changed++;
        }
        else if (string.Equals(request.Kind, "review", StringComparison.OrdinalIgnoreCase))
        {
            var item = await db.ReviewItems.FirstOrDefaultAsync(r => r.Id == request.Id && r.UserId == userId, ct)
                ?? throw ApiException.NotFound("RECALL_NOT_FOUND", "Review item not found.");
            item.Starred = request.Starred;
            item.StarReason = request.Starred ? request.Reason : null;
            changed++;
        }
        else
        {
            throw ApiException.Validation("INVALID_KIND", "Kind must be 'vocab', 'term' or 'review'.");
        }

        await db.SaveChangesAsync(ct);
        return new { changed, starred = request.Starred };
    }

    public async Task<RecallsAudioResponse> EnsureAudioAsync(
        string userId, string termId, string speed, CancellationToken ct)
    {
        var normalizedSpeed = string.IsNullOrWhiteSpace(speed)
            ? "normal"
            : speed.Trim().ToLowerInvariant();
        if (normalizedSpeed is not ("normal" or "slow" or "sentence"))
        {
            throw ApiException.Validation("INVALID_AUDIO_SPEED", "Speed must be normal, slow, or sentence.");
        }

        // Recall vocabulary is shared across professions ("Same for All
        // Professions"), and the learner-facing catalog (VocabularyService)
        // surfaces every active recall term regardless of profession. The audio
        // lookup MUST mirror that scope — otherwise a term that is visible in the
        // catalog (e.g. a `medicine` term shown to a `nursing` learner) would
        // resolve here as TERM_NOT_FOUND and the click-to-hear button would fail
        // with a misleading "Audio not available" error.
        var term = await db.VocabularyTerms.FirstOrDefaultAsync(t =>
                t.Id == termId
                && t.Status == "active"
                && t.RecallSetCodesJson != null
                && t.RecallSetCodesJson != ""
                && t.RecallSetCodesJson != "[]", ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");

        if (normalizedSpeed == "normal" && !string.IsNullOrWhiteSpace(term.AudioMediaAssetId))
        {
            var asset = await db.MediaAssets.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == term.AudioMediaAssetId, ct);
            if (asset is { Status: MediaAssetStatus.Ready } && storage.Exists(asset.StoragePath))
            {
                return new RecallsAudioResponse(asset.StoragePath, term.AudioProvider ?? "stored", ContentTypeFor(asset.StoragePath));
            }

            throw ApiException.NotFound(
                "AUDIO_NOT_READY",
                "Recall pronunciation audio has not been generated yet. Ask an admin to run the recall audio backfill.");
        }

        var existing = normalizedSpeed switch
        {
            "slow" => term.AudioSlowUrl,
            "sentence" => term.AudioSentenceUrl,
            _ => term.AudioUrl,
        };
        if (existing is { Length: > 0 } existingKey && IsStoredAudioKey(existingKey))
        {
            var asset = await db.MediaAssets.AsNoTracking()
                .FirstOrDefaultAsync(m => m.StoragePath == existingKey && m.Status == MediaAssetStatus.Ready, ct);
            if (asset is not null && storage.Exists(asset.StoragePath))
            {
                return new RecallsAudioResponse(asset.StoragePath, term.AudioProvider ?? "stored", ContentTypeFor(asset.StoragePath));
            }
        }

        throw ApiException.NotFound(
            "AUDIO_NOT_READY",
            "Recall pronunciation audio has not been generated yet. Ask an admin to run the recall audio backfill.");
    }

    private static bool IsStoredAudioKey(string value)
        => !string.IsNullOrWhiteSpace(value)
           && !value.StartsWith("/", StringComparison.Ordinal)
           && !Uri.TryCreate(value, UriKind.Absolute, out _);

    private static string ContentTypeFor(string storageKey)
    {
        var extension = Path.GetExtension(storageKey).ToLowerInvariant();
        return extension switch
        {
            ".mp3" => "audio/mpeg",
            ".webm" => "audio/webm",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// Library page payload: starred / weak / mastered / new buckets with rich
    /// term metadata and per-card filters.
    /// </summary>
    public async Task<RecallsLibraryResponse> GetLibraryAsync(
        string userId, string? bucket, string? topic, bool isPremium, CancellationToken ct)
    {
        var q = db.LearnerVocabularies
            .Where(lv => lv.UserId == userId)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t });

        // Non-premium learners only ever see their free-preview cards — locked
        // term content must never surface in the library buckets.
        if (!isPremium) q = q.Where(x => x.t.IsFreePreview);

        if (!string.IsNullOrWhiteSpace(topic))
            q = q.Where(x => x.t.Category == topic);

        switch (bucket)
        {
            case "starred":
                q = q.Where(x => x.lv.Starred);
                break;
            case "weak":
                q = q.Where(x => x.lv.ReviewCount > 0 && x.lv.CorrectCount * 1.0 / x.lv.ReviewCount < 0.7);
                break;
            case "mastered":
                q = q.Where(x => x.lv.Mastery == "mastered");
                break;
            case "new":
                q = q.Where(x => x.lv.Mastery == "new");
                break;
        }

        var rows = await q
            .OrderBy(x => x.t.Term)
            .Take(200)
            .Select(x => new RecallsLibraryItem(
                CardId: x.lv.Id.ToString(),
                TermId: x.t.Id,
                Term: x.t.Term,
                Definition: x.t.Definition ?? string.Empty,
                Category: x.t.Category,
                Mastery: x.lv.Mastery,
                Starred: x.lv.Starred,
                StarReason: x.lv.StarReason,
                LastErrorTypeCode: x.lv.LastErrorTypeCode,
                IntervalDays: x.lv.IntervalDays,
                ReviewCount: x.lv.ReviewCount,
                CorrectCount: x.lv.CorrectCount))
            .ToListAsync(ct);

        return new RecallsLibraryResponse(rows);
    }

    private static string HumanReason(string code) => code switch
    {
        "british_variant" => "Use the British spelling.",
        "missing_letter" => "A letter was dropped.",
        "extra_letter" => "An extra letter slipped in.",
        "transposition" => "Two adjacent letters were swapped.",
        "double_letter" => "A double letter was reduced to a single (or vice versa).",
        "hyphen" => "Hyphenation differs.",
        "homophone" => "Sounds similar — different word.",
        _ => "Spelling does not match the canonical British medical term.",
    };

    /// <summary>
    /// Weekly candidate report (spec §14). Pure SQL aggregation: practised /
    /// mastered / spelling-accuracy / weakest topic / most common error /
    /// average reviews per card. Does not invoke AI.
    /// </summary>
    public async Task<RecallsWeeklyReport> GetWeeklyReportAsync(string userId, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-7);

        var cardsThisWeek = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId && lv.LastReviewedAt != null && lv.LastReviewedAt >= since)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
            .ToListAsync(ct);

        var practised = cardsThisWeek.Count;
        var mastered = cardsThisWeek.Count(x => x.lv.Mastery == "mastered");
        var totalReviews = cardsThisWeek.Sum(x => x.lv.ReviewCount);
        var totalCorrect = cardsThisWeek.Sum(x => x.lv.CorrectCount);
        var spellingAccuracy = totalReviews == 0
            ? 0
            : (int)Math.Round(totalCorrect * 100.0 / totalReviews);

        var topicGroups = cardsThisWeek
            .GroupBy(x => x.t.Category)
            .Select(g => new
            {
                Topic = g.Key,
                Total = g.Count(),
                Weak = g.Count(x => x.lv.ReviewCount > 0 && x.lv.CorrectCount * 1.0 / x.lv.ReviewCount < 0.7),
            })
            .ToList();
        var weakestTopic = topicGroups.OrderByDescending(t => t.Weak).FirstOrDefault();

        var errorCounts = cardsThisWeek
            .Where(x => !string.IsNullOrEmpty(x.lv.LastErrorTypeCode) && x.lv.LastErrorTypeCode != "correct")
            .GroupBy(x => x.lv.LastErrorTypeCode!)
            .Select(g => new { Code = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();
        var topError = errorCounts.FirstOrDefault();

        var avgReviewsPerCard = practised == 0 ? 0.0 : Math.Round(totalReviews * 1.0 / practised, 1);

        return new RecallsWeeklyReport(
            PractisedCount: practised,
            MasteredCount: mastered,
            SpellingAccuracyPct: spellingAccuracy,
            WeakestTopic: weakestTopic?.Topic,
            MostCommonErrorCode: topError?.Code,
            MostCommonErrorLabel: topError == null ? null : HumanReason(topError.Code),
            AverageReviewsPerCard: avgReviewsPerCard);
    }

    /// <summary>
    /// AI-generated personal revision plan (spec §12). Routes through the AI
    /// gateway with <see cref="AiFeatureCodes.RecallsRevisionPlan"/>. Falls
    /// back to a deterministic plan if AI is unavailable.
    /// </summary>
    public async Task<RecallsRevisionPlanResponse> GetRevisionPlanAsync(string userId, CancellationToken ct)
    {
        var today = await GetTodayAsync(userId, ct);
        var weak = today.WeakTopics.OrderByDescending(t => t.WeakCount).Take(3).ToList();
        var weakSummary = weak.Count == 0
            ? "no specific weak topic yet"
            : string.Join(", ", weak.Select(w => $"{w.Topic} ({w.WeakCount}/{w.Total} weak)"));

        // Deterministic plan we can always return (also acts as the AI fallback).
        var deterministic = new RecallsRevisionPlanResponse(
            DueToday: today.DueToday,
            Mastered: today.Mastered,
            ReadinessScore: today.ReadinessScore,
            Headline: $"You have {today.DueToday} cards due today.",
            Steps: BuildDeterministicSteps(today, weak),
            AiNarrative: null);

        try
        {
            var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
            {
                Kind = RuleKind.Vocabulary,
                Profession = ExamProfession.Medicine,
                Task = AiTaskMode.GenerateVocabularyGloss,
            });
            var userMessage =
                $"Today: {today.DueToday} due, {today.Mastered}/{today.Total} mastered, " +
                $"readiness {today.ReadinessScore}%. Weak topics: {weakSummary}. " +
                "Write a 2-3 sentence personal revision plan for an OET candidate. " +
                "Be concrete, brief, and clinical. British English.";
            var ai = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = string.Empty,
                Temperature = 0.3,
                FeatureCode = AiFeatureCodes.RecallsRevisionPlan,
                UserId = userId,
            }, ct);
            return deterministic with { AiNarrative = ai.Completion?.Trim() };
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch
        {
            return deterministic;
        }
    }

    private static IReadOnlyList<string> BuildDeterministicSteps(
        RecallsTodayResponse today, IReadOnlyList<RecallsWeakTopic> weak)
    {
        var steps = new List<string>();
        if (today.DueToday > 0)
            steps.Add($"Clear today’s {today.DueToday} due card{(today.DueToday == 1 ? "" : "s")} first.");
        if (today.VocabDueToday > 0)
            steps.Add($"Review {Math.Min(10, today.VocabDueToday)} due vocabulary words.");
        foreach (var w in weak.Take(2))
            steps.Add($"Focus on {w.Topic} — {w.WeakCount} item(s) below 70% accuracy.");
        if (today.Starred > 0)
            steps.Add($"Revisit {Math.Min(10, today.Starred)} of your favourited word(s).");
        if (steps.Count == 0)
            steps.Add("Nothing critical due. Favourite 3 new high-risk words to seed tomorrow.");
        return steps;
    }

    /// <summary>
    /// Admin CSV bulk upload of vocabulary terms. Idempotent on (Term, ExamType, Profession).
    /// See spec §8 — admin should be able to bulk-add words with topic/difficulty/IPA/example/etc.
    /// </summary>
    public async Task<RecallsBulkUploadResult> BulkUploadAsync(
        IReadOnlyList<RecallsBulkUploadRow> rows, CancellationToken ct)
    {
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var (row, idx) in rows.Select((r, i) => (r, i)))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(row.Term) || string.IsNullOrWhiteSpace(row.Definition))
                {
                    skipped++;
                    errors.Add($"Row {idx + 1}: term + definition required.");
                    continue;
                }

                var examType = string.IsNullOrWhiteSpace(row.ExamTypeCode) ? "OET" : row.ExamTypeCode!;
                var existing = await db.VocabularyTerms.FirstOrDefaultAsync(
                    t => t.Term == row.Term && t.ExamTypeCode == examType && t.ProfessionId == row.ProfessionId, ct);

                var synonyms = string.IsNullOrWhiteSpace(row.SynonymsCsv)
                    ? "[]"
                    : System.Text.Json.JsonSerializer.Serialize(
                        row.SynonymsCsv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray());

                if (existing is null)
                {
                    db.VocabularyTerms.Add(new VocabularyTerm
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Term = row.Term,
                        Definition = row.Definition,
                        ExampleSentence = row.ExampleSentence ?? string.Empty,
                        Category = row.Category ?? "general",
                        IpaPronunciation = row.Ipa,
                        AmericanSpelling = row.AmericanSpelling,
                        SynonymsJson = synonyms,
                        ExamTypeCode = examType,
                        ProfessionId = row.ProfessionId,
                        Status = "active",
                        SourceProvenance = "admin:bulk-csv",
                    });
                    inserted++;
                }
                else
                {
                    existing.Definition = row.Definition;
                    if (!string.IsNullOrWhiteSpace(row.ExampleSentence)) existing.ExampleSentence = row.ExampleSentence!;
                    if (!string.IsNullOrWhiteSpace(row.Category)) existing.Category = row.Category!;
                    if (!string.IsNullOrWhiteSpace(row.Ipa)) existing.IpaPronunciation = row.Ipa;
                    if (!string.IsNullOrWhiteSpace(row.AmericanSpelling)) existing.AmericanSpelling = row.AmericanSpelling;
                    if (!string.IsNullOrWhiteSpace(row.SynonymsCsv)) existing.SynonymsJson = synonyms;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                }
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"Row {idx + 1}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return new RecallsBulkUploadResult(inserted, updated, skipped, errors);
    }

    private static int ComputeReadiness(int total, int mastered, IReadOnlyCollection<RecallsWeakTopic> weak)
    {
        if (total <= 0) return 0;
        var masteryPct = (int)Math.Round(mastered * 100.0 / Math.Max(1, total));
        // Penalise up to 15 points for highly weak topic mix.
        var weakPenalty = weak.Sum(t => t.WeakCount) > 0
            ? Math.Min(15, weak.Sum(t => t.WeakCount))
            : 0;
        return Math.Clamp(masteryPct - weakPenalty, 0, 100);
    }

    private static void ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return;
        var allowed = new[] { "spelling", "pronunciation", "meaning", "hearing", "confused" };
        if (!allowed.Contains(reason))
            throw ApiException.Validation("INVALID_REASON", $"Reason must be one of: {string.Join(',', allowed)}.");
    }

}

// ── DTOs ────────────────────────────────────────────────────────────────

public record RecallsTodayResponse(
    int DueToday,
    int Mastered,
    int Total,
    int Starred,
    int VocabDueToday,
    int ReviewDueToday,
    int ReadinessScore,
    IReadOnlyList<RecallsWeakTopic> WeakTopics);

public record RecallsWeakTopic(string Topic, int Total, int WeakCount);

public record RecallsQueueItem(
    string Kind,
    string Id,
    string? TermId,
    string Title,
    string? Subtitle,
    DateOnly? DueDate,
    bool Starred,
    string? StarReason,
    string Mastery,
    string? Ipa,
    string? ExtraJson);

public record RecallsStarRequest(string Kind, string Id, bool Starred, string? Reason);

public record RecallsAudioResponse(string StorageKey, string Provider, string ContentType);

public record RecallsLibraryResponse(IReadOnlyList<RecallsLibraryItem> Items);

public record RecallsLibraryItem(
    string CardId,
    string TermId,
    string Term,
    string Definition,
    string Category,
    string Mastery,
    bool Starred,
    string? StarReason,
    string? LastErrorTypeCode,
    int IntervalDays,
    int ReviewCount,
    int CorrectCount);

public record RecallsBulkUploadRow(
    string Term,
    string Definition,
    string? ExampleSentence,
    string? Category,
    string? Difficulty,
    string? Ipa,
    string? AmericanSpelling,
    string? SynonymsCsv,
    string? ExamTypeCode,
    string? ProfessionId);

public record RecallsBulkUploadResult(int Inserted, int Updated, int Skipped, IReadOnlyList<string> Errors);

public record RecallsWeeklyReport(
    int PractisedCount,
    int MasteredCount,
    int SpellingAccuracyPct,
    string? WeakestTopic,
    string? MostCommonErrorCode,
    string? MostCommonErrorLabel,
    double AverageReviewsPerCard);

public record RecallsRevisionPlanResponse(
    int DueToday,
    int Mastered,
    int ReadinessScore,
    string Headline,
    IReadOnlyList<string> Steps,
    string? AiNarrative);
