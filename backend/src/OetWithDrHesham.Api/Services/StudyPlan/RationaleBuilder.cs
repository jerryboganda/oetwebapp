namespace OetWithDrHesham.Api.Services.Planner;

/// <summary>
/// Produces the short human-readable explanation that appears under each plan
/// item ("why is this task here?"). Combines the template-author's hint with
/// computed gap + weak-skill signals so learners see a tailored sentence.
/// </summary>
public static class RationaleBuilder
{
    public static string Build(
        StudyPlanTemplateSlot slot,
        string? contentTitle,
        IReadOnlyDictionary<string, double> weights,
        IReadOnlyCollection<string> weakSubtests)
    {
        var subtest = slot.Subtest;
        var hint = string.IsNullOrWhiteSpace(slot.RationaleHint) ? null : slot.RationaleHint!.Trim();
        var weight = weights.TryGetValue(subtest, out var w) ? w : 0.0;
        var isWeak = weakSubtests.Contains(subtest, StringComparer.OrdinalIgnoreCase);

        var baseLine = slot.Kind switch
        {
            StudyPlanSlotKinds.SpacedRepReview =>
                "Review items due today — catching them before they decay keeps long-term recall steady.",
            StudyPlanSlotKinds.NextUnattemptedPaper =>
                contentTitle is null
                    ? $"Next unattempted {subtest} paper — builds pacing under real exam conditions."
                    : $"{contentTitle} — next paper in your {subtest} ladder.",
            StudyPlanSlotKinds.DrillByTag =>
                $"Targeted {subtest} drill — narrow focus on one skill at a time.",
            StudyPlanSlotKinds.WeakSkillFocus =>
                $"{subtest} is your highest-gap area right now — concentrating reps here moves the needle fastest.",
            StudyPlanSlotKinds.MiniMock =>
                $"Mini mock — a short timed checkpoint to surface what to fix before the next block.",
            StudyPlanSlotKinds.FullMock =>
                "Full mock — simulates exam conditions end-to-end and refreshes your readiness score.",
            StudyPlanSlotKinds.ExpertReviewSubmission =>
                "Send your latest attempt to a tutor — human feedback finds blind spots AI misses.",
            StudyPlanSlotKinds.PronunciationDrill =>
                "Pronunciation drill — fluency markers improve fastest with daily short reps.",
            StudyPlanSlotKinds.VocabularyFlashcards =>
                "Vocabulary recall — spaced cards keep your active vocabulary growing without cramming.",
            StudyPlanSlotKinds.CustomContent =>
                contentTitle is null ? "Coach-recommended task." : $"Coach-recommended: {contentTitle}.",
            _ => $"{subtest} task."
        };

        if (isWeak && slot.Kind != StudyPlanSlotKinds.WeakSkillFocus)
        {
            baseLine += $" Extra weighting because you flagged {subtest} as a weak area.";
        }
        else if (weight >= 0.35)
        {
            baseLine += " High priority this week based on your readiness gap.";
        }

        if (hint is not null)
        {
            baseLine = $"{hint} — {baseLine}";
        }

        // Hard cap to fit the 1024-char Rationale column with headroom.
        return baseLine.Length <= 900 ? baseLine : baseLine[..900];
    }
}
