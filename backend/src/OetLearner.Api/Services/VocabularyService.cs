using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public class VocabularyService(LearnerDbContext db)
{
    // ── Browse terms ─────────────────────────────────────────────────────

    public async Task<object> GetTermsAsync(string? examTypeCode, string? category, string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.VocabularyTerms.Where(t => t.Status == "active");
        if (!string.IsNullOrEmpty(examTypeCode)) query = query.Where(t => t.ExamTypeCode == examTypeCode);
        if (!string.IsNullOrEmpty(category)) query = query.Where(t => t.Category == category);
        if (!string.IsNullOrEmpty(search)) query = query.Where(t => t.Term.Contains(search) || t.Definition.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(t => t.Term)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var mapped = items.Select(MapTerm).ToList();
        return new { total, page, pageSize, terms = mapped, items = mapped };
    }

    public async Task<object> GetTermAsync(string termId, CancellationToken ct)
    {
        var term = await db.VocabularyTerms.FindAsync([termId], ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Vocabulary term not found.");
        return MapTerm(term);
    }

    // ── Learner vocabulary list ───────────────────────────────────────────

    public async Task<object> GetMyVocabularyAsync(string userId, string? mastery, CancellationToken ct)
    {
        var query = db.LearnerVocabularies
            .Where(lv => lv.UserId == userId)
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t });

        if (!string.IsNullOrEmpty(mastery))
            query = query.Where(x => x.lv.Mastery == mastery);

        var items = await query.OrderBy(x => x.lv.NextReviewDate).ToListAsync(ct);
        return items.Select(x => new
        {
            id = x.lv.Id,
            termId = x.lv.TermId,
            term = x.t.Term,
            word = x.t.Term,
            definition = x.t.Definition,
            mastery = x.lv.Mastery,
            easeFactor = x.lv.EaseFactor,
            intervalDays = x.lv.IntervalDays,
            reviewCount = x.lv.ReviewCount,
            correctCount = x.lv.CorrectCount,
            nextReviewDate = x.lv.NextReviewDate,
            dueAt = x.lv.NextReviewDate?.ToString("yyyy-MM-dd"),
            lastReviewedAt = x.lv.LastReviewedAt,
            addedAt = x.lv.AddedAt
        }).ToList();
    }

    public async Task<object> AddToMyVocabularyAsync(string userId, string termId, CancellationToken ct)
    {
        var term = await db.VocabularyTerms.FindAsync([termId], ct)
            ?? throw ApiException.NotFound("TERM_NOT_FOUND", "Term not found.");

        var existing = await db.LearnerVocabularies.FirstOrDefaultAsync(
            lv => lv.UserId == userId && lv.TermId == termId, ct);
        if (existing != null) return new { added = false, item = MapLearnerVocab(existing, term.Term) };

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
            AddedAt = DateTimeOffset.UtcNow
        };
        db.LearnerVocabularies.Add(lv);
        await db.SaveChangesAsync(ct);
        return new { added = true, item = MapLearnerVocab(lv, term.Term) };
    }

    public async Task<object> RemoveFromMyVocabularyAsync(string userId, string termId, CancellationToken ct)
    {
        var lv = await db.LearnerVocabularies.FirstOrDefaultAsync(x => x.UserId == userId && x.TermId == termId, ct)
            ?? throw ApiException.NotFound("NOT_IN_VOCABULARY", "Term is not in vocabulary.");
        db.LearnerVocabularies.Remove(lv);
        await db.SaveChangesAsync(ct);
        return new { removed = true };
    }

    // ── Flashcard review ─────────────────────────────────────────────────

    public async Task<object> GetDueFlashcardsAsync(string userId, int limit, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var due = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId && lv.Mastery != "mastered" && (lv.NextReviewDate == null || lv.NextReviewDate <= today))
            .Join(db.VocabularyTerms, lv => lv.TermId, t => t.Id, (lv, t) => new { lv, t })
            .OrderBy(x => x.lv.NextReviewDate)
            .Take(limit)
            .ToListAsync(ct);

        return due.Select(x => new
        {
            id = x.lv.Id,
            termId = x.t.Id,
            term = x.t.Term,
            word = x.t.Term,
            definition = x.t.Definition,
            exampleSentence = x.t.ExampleSentence,
            contextNotes = x.t.ContextNotes,
            pronunciation = (string?)null,
            audioUrl = x.t.AudioUrl,
            synonymsJson = x.t.SynonymsJson,
            mastery = x.lv.Mastery
        }).ToList();
    }

    public async Task<object> SubmitFlashcardReviewAsync(string userId, Guid lvId, int quality, CancellationToken ct)
    {
        if (quality < 0 || quality > 5)
            throw ApiException.Validation("INVALID_QUALITY", "Quality must be 0-5.");

        var lv = await db.LearnerVocabularies.FirstOrDefaultAsync(x => x.Id == lvId && x.UserId == userId, ct)
            ?? throw ApiException.NotFound("NOT_FOUND", "Flashcard not found.");

        ApplySm2Vocab(lv, quality);
        lv.LastReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new { id = lv.Id, mastery = lv.Mastery, nextReviewDate = lv.NextReviewDate };
    }

    // ── Quiz ─────────────────────────────────────────────────────────────

    public async Task<object> GetQuizAsync(string userId, int count, CancellationToken ct)
    {
        var allTerms = await db.VocabularyTerms
            .Where(t => t.Status == "active")
            .ToListAsync(ct);

        if (allTerms.Count == 0)
        {
            return new { questions = Array.Empty<object>() };
        }

        var myTermIds = await db.LearnerVocabularies
            .Where(lv => lv.UserId == userId)
            .Select(lv => lv.TermId)
            .ToListAsync(ct);

        var candidateTerms = myTermIds.Count >= count
            ? allTerms.Where(t => myTermIds.Contains(t.Id)).ToList()
            : allTerms;

        var terms = candidateTerms
            .OrderBy(_ => Guid.NewGuid())
            .Take(Math.Min(count, candidateTerms.Count))
            .ToList();

        var questions = terms.Select(term =>
        {
            var distractorPool = allTerms
                .Where(other => other.Id != term.Id)
                .Select(other => other.Definition)
                .Distinct()
                .OrderBy(_ => Guid.NewGuid())
                .Take(3)
                .ToList();

            while (distractorPool.Count < 3)
            {
                distractorPool.Add(term.Definition);
            }

            var options = distractorPool
                .Append(term.Definition)
                .Distinct()
                .Take(4)
                .OrderBy(_ => Guid.NewGuid())
                .ToList();

            var correctIndex = options.FindIndex(option => option == term.Definition);

            return new
            {
                termId = term.Id,
                term = term.Term,
                word = term.Term,
                definition = term.Definition,
                exampleSentence = term.ExampleSentence,
                options,
                correctIndex
            };
        }).ToList();

        return new { questions };
    }

    public async Task<object> SubmitQuizAsync(string userId, VocabQuizSubmission submission, CancellationToken ct)
    {
        var result = new VocabularyQuizResult
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TermsQuizzed = submission.Answers.Count,
            CorrectCount = submission.Answers.Count(a => a.Correct),
            DurationSeconds = submission.DurationSeconds,
            ResultsJson = JsonSupport.Serialize(submission.Answers),
            CompletedAt = DateTimeOffset.UtcNow
        };
        db.VocabularyQuizResults.Add(result);

        // Update mastery for each answered term
        foreach (var ans in submission.Answers)
        {
            var lv = await db.LearnerVocabularies.FirstOrDefaultAsync(x => x.UserId == userId && x.TermId == ans.TermId, ct);
            if (lv == null) continue;
            ApplySm2Vocab(lv, ans.Correct ? 4 : 2);
            lv.LastReviewedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return new
        {
            id = result.Id,
            termsQuizzed = result.TermsQuizzed,
            correctCount = result.CorrectCount,
            score = result.TermsQuizzed > 0 ? Math.Round(100.0 * result.CorrectCount / result.TermsQuizzed, 1) : 0,
            durationSeconds = result.DurationSeconds,
            completedAt = result.CompletedAt
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static void ApplySm2Vocab(LearnerVocabulary lv, int quality)
    {
        lv.ReviewCount++;
        if (quality >= 3)
        {
            lv.CorrectCount++;
            var interval = lv.ReviewCount == 1 ? 1
                : lv.ReviewCount == 2 ? 6
                : (int)Math.Round(lv.IntervalDays * lv.EaseFactor);
            lv.IntervalDays = interval;
            lv.EaseFactor = Math.Max(1.3, lv.EaseFactor + 0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02));
            lv.Mastery = lv.ReviewCount >= 10 && lv.CorrectCount >= 8 ? "mastered"
                : lv.ReviewCount >= 4 ? "reviewing"
                : "learning";
        }
        else
        {
            lv.IntervalDays = 1;
            lv.Mastery = "learning";
        }
        lv.NextReviewDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(lv.IntervalDays);
    }

    private static object MapTerm(VocabularyTerm t) => new
    {
        id = t.Id,
        term = t.Term,
        word = t.Term,
        definition = t.Definition,
        exampleSentence = t.ExampleSentence,
        contextNotes = t.ContextNotes,
        examTypeCode = t.ExamTypeCode,
        professionId = t.ProfessionId,
        category = t.Category,
        difficulty = t.Difficulty,
        difficultyLevel = t.Difficulty,
        pronunciation = (string?)null,
        audioUrl = t.AudioUrl,
        imageUrl = t.ImageUrl,
        synonymsJson = t.SynonymsJson,
        collocationsJson = t.CollocationsJson,
        relatedTermsJson = t.RelatedTermsJson
    };

    private static object MapLearnerVocab(LearnerVocabulary lv, string term) => new
    {
        id = lv.Id,
        termId = lv.TermId,
        term,
        mastery = lv.Mastery,
        reviewCount = lv.ReviewCount,
        nextReviewDate = lv.NextReviewDate,
        addedAt = lv.AddedAt
    };
}

public record VocabQuizSubmission(
    List<VocabQuizAnswer> Answers,
    int DurationSeconds);

public record VocabQuizAnswer(
    string TermId,
    bool Correct,
    string? UserAnswer);
