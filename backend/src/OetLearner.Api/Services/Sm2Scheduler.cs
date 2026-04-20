namespace OetLearner.Api.Services;

/// <summary>
/// SM-2 spaced repetition scheduler — the SINGLE implementation of the Piotr
/// Woźniak algorithm for this codebase. Both <see cref="VocabularyService"/>
/// and <see cref="SpacedRepetitionService"/> delegate here.
///
/// Contract:
///   quality ∈ [0,5]; 0-2 means "forgot/hard" (interval resets), 3-5 means "pass".
///   easeFactor clamped at 1.3.
///   intervals = 1 → 6 → round(prevInterval × EF)
///
/// See docs/VOCABULARY-MODULE.md §4 for the canonical formula.
/// </summary>
public interface ISpacedRepetitionScheduler
{
    Sm2Update Schedule(Sm2State state, int quality, DateOnly? today = null);
}

public sealed record Sm2State(
    double EaseFactor,
    int IntervalDays,
    int ReviewCount,
    int CorrectCount);

public sealed record Sm2Update(
    double EaseFactor,
    int IntervalDays,
    int ReviewCount,
    int CorrectCount,
    DateOnly NextReviewDate,
    bool CorrectAnswer)
{
    /// <summary>
    /// Derives the mastery tier for a LearnerVocabulary card from the updated
    /// counters. Returns "mastered" if ≥10 reviews & ≥8 correct, "reviewing"
    /// if ≥4 reviews, "learning" if review count &gt; 0, else "new".
    ///
    /// Kept in sync with the four-tier enum documented in VOCABULARY-MODULE.md §3.
    /// </summary>
    public string DeriveMastery()
        => ReviewCount >= 10 && CorrectCount >= 8 ? "mastered"
         : ReviewCount >= 4 ? "reviewing"
         : ReviewCount > 0 ? "learning"
         : "new";
}

public sealed class Sm2Scheduler : ISpacedRepetitionScheduler
{
    public Sm2Update Schedule(Sm2State state, int quality, DateOnly? today = null)
    {
        if (quality < 0 || quality > 5)
            throw new ArgumentOutOfRangeException(nameof(quality), "Quality must be 0-5 on the SM-2 scale.");

        var todayDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var reviewCount = state.ReviewCount + 1;
        var correct = quality >= 3;
        var correctCount = state.CorrectCount + (correct ? 1 : 0);

        int intervalDays;
        double easeFactor = state.EaseFactor;

        if (correct)
        {
            intervalDays = reviewCount switch
            {
                1 => 1,
                2 => 6,
                _ => Math.Max(1, (int)Math.Round(state.IntervalDays * easeFactor)),
            };
            easeFactor = Math.Max(1.3, state.EaseFactor + 0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02));
        }
        else
        {
            intervalDays = 1;
        }

        return new Sm2Update(
            EaseFactor: easeFactor,
            IntervalDays: intervalDays,
            ReviewCount: reviewCount,
            CorrectCount: correctCount,
            NextReviewDate: todayDate.AddDays(intervalDays),
            CorrectAnswer: correct);
    }
}
