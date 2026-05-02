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
    IRecallsTtsService tts,
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
    /// Mixed queue of vocab cards + review items, ordered by due date with
    /// starred items prioritised when due.
    /// </summary>
    public async Task<List<RecallsQueueItem>> GetQueueAsync(string userId, int limit, CancellationToken ct)
    {
        if (limit <= 0) limit = 20;
        if (limit > 100) limit = 100;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var vocabCards = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId && lv.NextReviewDate <= today)
            .OrderByDescending(lv => lv.Starred)
            .ThenBy(lv => lv.NextReviewDate)
            .Take(limit)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
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

    public async Task<object> StarAsync(string userId, RecallsStarRequest request, CancellationToken ct)
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
            throw ApiException.Validation("INVALID_KIND", "Kind must be 'vocab' or 'review'.");
        }

        await db.SaveChangesAsync(ct);
        return new { changed, starred = request.Starred };
    }

    /// <summary>Type-to-spell server-side classifier. Persists `LastErrorTypeCode`.</summary>
    public async Task<RecallsListenTypeResponse> ListenAndTypeAsync(
        string userId, RecallsListenTypeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TermId))
            throw ApiException.Validation("TERM_REQUIRED", "TermId is required.");
        if (request.Typed is null)
            throw ApiException.Validation("TYPED_REQUIRED", "Typed is required.");

        var term = await db.VocabularyTerms.FirstOrDefaultAsync(t => t.Id == request.TermId, ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");

        var similar = ParseStringArray(term.SynonymsJson)
            .Concat(ParseStringArray(term.RelatedTermsJson))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var result = SpellingDiff.Classify(
            canonical: term.Term,
            typed: request.Typed,
            americanSpelling: term.AmericanSpelling,
            similarSounding: similar);

        // Persist the last error code on the user's card if one exists.
        var card = await db.LearnerVocabularies
            .FirstOrDefaultAsync(lv => lv.UserId == userId && lv.TermId == term.Id, ct);
        if (card is not null)
        {
            card.LastErrorTypeCode = result.Code;
            await db.SaveChangesAsync(ct);
        }

        return new RecallsListenTypeResponse(
            Code: result.Code,
            IsCorrect: result.IsCorrect,
            Distance: result.Distance,
            Canonical: term.Term,
            Typed: request.Typed,
            AmericanSpelling: term.AmericanSpelling,
            Segments: result.Segments
                .Select(s => new RecallsDiffSegment(s.Kind, s.Text))
                .ToList());
    }

    public async Task<RecallsAudioResponse> EnsureAudioAsync(
        string termId, string speed, CancellationToken ct)
    {
        var normalizedSpeed = string.IsNullOrWhiteSpace(speed)
            ? "normal"
            : speed.Trim().ToLowerInvariant();
        if (normalizedSpeed is not ("normal" or "slow" or "sentence"))
        {
            throw ApiException.Validation("INVALID_AUDIO_SPEED", "Speed must be normal, slow, or sentence.");
        }

        var term = await db.VocabularyTerms.FirstOrDefaultAsync(t => t.Id == termId, ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");

        var existing = normalizedSpeed switch
        {
            "slow" => term.AudioSlowUrl,
            "sentence" => term.AudioSentenceUrl,
            _ => term.AudioUrl,
        };
        if (existing is { Length: > 0 } existingKey && IsStoredAudioKey(existingKey) && storage.Exists(existingKey))
            return new RecallsAudioResponse(existingKey, "cached", ContentTypeFor(existingKey));

        RecallsTtsResult result;
        if (normalizedSpeed == "sentence")
        {
            result = await tts.GenerateSentenceAsync(
                term.ExampleSentence ?? term.Term,
                new RecallsTtsOptions(Speed: "normal"), ct);
            term.AudioSentenceUrl = result.Url;
        }
        else
        {
            result = await tts.GenerateWordAsync(
                term.Term,
                new RecallsTtsOptions(Speed: normalizedSpeed),
                ct);
            if (normalizedSpeed == "slow") term.AudioSlowUrl = result.Url;
            else term.AudioUrl = result.Url;
        }
        term.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new RecallsAudioResponse(result.Url, result.Provider, result.ContentType);
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
        string userId, string? bucket, string? topic, CancellationToken ct)
    {
        var q = db.LearnerVocabularies
            .Where(lv => lv.UserId == userId)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t });

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
                Definition: x.t.Definition,
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

    /// <summary>
    /// AI-grounded mistake explanation. Routes through the AI Gateway with
    /// <see cref="AiFeatureCodes.RecallsMistakeExplain"/> so quota and audit
    /// stay consistent with other learner-facing AI calls. The classifier
    /// runs first; the AI is only invoked when there is a real mistake to
    /// explain (skips on `correct` and `case_only`).
    /// </summary>
    public async Task<RecallsExplainResponse> ExplainMistakeAsync(
        string userId, RecallsExplainRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TermId))
            throw ApiException.Validation("TERM_REQUIRED", "TermId is required.");
        if (string.IsNullOrWhiteSpace(request.Typed))
            throw ApiException.Validation("TYPED_REQUIRED", "Typed is required.");

        var term = await db.VocabularyTerms.FirstOrDefaultAsync(t => t.Id == request.TermId, ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");

        var diff = SpellingDiff.Classify(
            canonical: term.Term,
            typed: request.Typed,
            americanSpelling: term.AmericanSpelling,
            similarSounding: ParseStringArray(term.SynonymsJson));

        if (diff.Code is "correct" or "case_only")
        {
            return new RecallsExplainResponse(
                Code: diff.Code,
                ShortReason: diff.Code == "correct" ? "Spot on." : "Correct (case differs).",
                LongExplanation: null,
                MnemonicHint: null);
        }

        var prompt = gateway.BuildGroundedPrompt(new AiGroundingContext
        {
            Kind = RuleKind.Vocabulary,
            Profession = ExamProfession.Medicine,
            Task = AiTaskMode.GenerateVocabularyGloss,
        });

        var userMessage =
            $"A learner attempted to spell the British medical term '{term.Term}' as '{request.Typed}'. " +
            $"Classifier says: {diff.Code}. " +
            "In 2-3 sentences, explain the mistake plainly, then give one short mnemonic hint. " +
            "Do not coach beyond clinical English. Use British spelling.";

        try
        {
            var ai = await gateway.CompleteAsync(new AiGatewayRequest
            {
                Prompt = prompt,
                UserInput = userMessage,
                Model = string.Empty,
                Temperature = 0.2,
                FeatureCode = AiFeatureCodes.RecallsMistakeExplain,
                UserId = userId,
            }, ct);

            return new RecallsExplainResponse(
                Code: diff.Code,
                ShortReason: HumanReason(diff.Code),
                LongExplanation: ai.Completion?.Trim(),
                MnemonicHint: null);
        }
        catch (PromptNotGroundedException)
        {
            throw;
        }
        catch
        {
            return new RecallsExplainResponse(
                Code: diff.Code,
                ShortReason: HumanReason(diff.Code),
                LongExplanation: null,
                MnemonicHint: null);
        }
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
    /// Quiz session payload. Returns mode-shaped items for the 6 Recalls quiz
    /// modes (docs/RECALLS-MODULE-PLAN.md §6 / spec §4). Distractors and
    /// definition options are precomputed server-side so the client never
    /// reveals the canonical answer in network responses except as needed.
    /// </summary>
    public async Task<RecallsQuizSession> GetQuizAsync(
        string userId, string mode, int limit, CancellationToken ct)
    {
        if (limit <= 0) limit = 10;
        if (limit > 50) limit = 50;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var q = db.LearnerVocabularies
            .Where(lv => lv.UserId == userId)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t });

        switch (mode)
        {
            case "starred_only":
                q = q.Where(x => x.lv.Starred);
                break;
            case "high_risk_spelling":
                // Heuristic: terms with an American variant are high-risk by definition;
                // additionally flag classic British-spelling minefield categories.
                q = q.Where(x => x.t.AmericanSpelling != null || x.t.Category == "spelling");
                break;
            case "clinical_sentence":
                q = q.Where(x => x.t.ExampleSentence != null && x.t.ExampleSentence != "");
                break;
            default:
                q = q.Where(x => x.lv.NextReviewDate <= today);
                break;
        }

        var rows = await q
            .OrderByDescending(x => x.lv.Starred)
            .ThenBy(x => x.lv.NextReviewDate)
            .Take(limit)
            .ToListAsync(ct);

        // Precompute a small distractor pool from other terms in the same exam type.
        var termIds = rows.Select(r => r.t.Id).ToHashSet();
        var pool = await db.VocabularyTerms
            .Where(t => !termIds.Contains(t.Id))
            .OrderBy(t => t.Term)
            .Take(60)
            .Select(t => new { t.Term, t.Definition })
            .ToListAsync(ct);

        var rng = new Random(unchecked(userId.GetHashCode() ^ DateTime.UtcNow.DayOfYear));
        string[] PickDistractTerms(string canonical, IReadOnlyList<string> similar)
        {
            var bag = new List<string>(similar.Where(s => !string.Equals(s, canonical, StringComparison.OrdinalIgnoreCase)));
            while (bag.Count < 3 && pool.Count > 0)
            {
                var pick = pool[rng.Next(pool.Count)].Term;
                if (!bag.Contains(pick) && !string.Equals(pick, canonical, StringComparison.OrdinalIgnoreCase))
                    bag.Add(pick);
            }
            return bag.Take(3).ToArray();
        }

        string[] PickDistractDefs(string canonicalDef)
        {
            var bag = new List<string>();
            while (bag.Count < 3 && pool.Count > 0)
            {
                var pick = pool[rng.Next(pool.Count)].Definition;
                if (!string.IsNullOrWhiteSpace(pick) && !bag.Contains(pick) && pick != canonicalDef)
                    bag.Add(pick);
            }
            return bag.Take(3).ToArray();
        }

        static string BlankSentence(string sentence, string term)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return string.Empty;
            var idx = sentence.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return sentence; // fallback: caller treats as no-blank
            var blank = new string('_', Math.Max(6, term.Length));
            return string.Concat(sentence.AsSpan(0, idx), blank, sentence.AsSpan(idx + term.Length));
        }

        var items = rows.Select(x =>
        {
            var similar = ParseStringArray(x.t.SynonymsJson)
                .Concat(ParseStringArray(x.t.RelatedTermsJson))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new RecallsQuizItem(
                CardId: x.lv.Id.ToString(),
                TermId: x.t.Id,
                Term: x.t.Term,
                Definition: x.t.Definition,
                ExampleSentence: x.t.ExampleSentence,
                BlankedSentence: BlankSentence(x.t.ExampleSentence, x.t.Term),
                Ipa: x.t.IpaPronunciation,
                AmericanSpelling: x.t.AmericanSpelling,
                Starred: x.lv.Starred,
                Mastery: x.lv.Mastery,
                TermDistractors: PickDistractTerms(x.t.Term, similar),
                DefinitionDistractors: PickDistractDefs(x.t.Definition));
        }).ToList();

        return new RecallsQuizSession(mode, items);
    }

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
            steps.Add($"Drill {Math.Min(10, today.VocabDueToday)} listen-and-type cards.");
        foreach (var w in weak.Take(2))
            steps.Add($"Focus on {w.Topic} — {w.WeakCount} item(s) below 70% accuracy.");
        if (today.Starred > 0)
            steps.Add($"Run a Starred-only round for {Math.Min(10, today.Starred)} item(s).");
        if (steps.Count == 0)
            steps.Add("Nothing critical due. Star 3 new high-risk-spelling words to seed tomorrow.");
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
                        Difficulty = row.Difficulty ?? "medium",
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
                    if (!string.IsNullOrWhiteSpace(row.Difficulty)) existing.Difficulty = row.Difficulty!;
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

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
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

public record RecallsListenTypeRequest(string TermId, string Typed);

public record RecallsListenTypeResponse(
    string Code,
    bool IsCorrect,
    int Distance,
    string Canonical,
    string Typed,
    string? AmericanSpelling,
    IReadOnlyList<RecallsDiffSegment> Segments);

public record RecallsDiffSegment(string Kind, string Text);

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

public record RecallsExplainRequest(string TermId, string Typed);

public record RecallsExplainResponse(
    string Code,
    string ShortReason,
    string? LongExplanation,
    string? MnemonicHint);

public record RecallsQuizSession(string Mode, IReadOnlyList<RecallsQuizItem> Items);

public record RecallsQuizItem(
    string CardId,
    string TermId,
    string Term,
    string Definition,
    string? ExampleSentence,
    string? BlankedSentence,
    string? Ipa,
    string? AmericanSpelling,
    bool Starred,
    string Mastery,
    IReadOnlyList<string> TermDistractors,
    IReadOnlyList<string> DefinitionDistractors);

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
