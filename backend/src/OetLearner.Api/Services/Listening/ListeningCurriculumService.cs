using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Phase 10 of LISTENING-MODULE-PLAN.md — curriculum metadata.
//
// Returns the 12-stage Listening curriculum from the spec (numbers / units /
// names → spelling → workplace extracts → presentations → mocks) annotated
// with the learner's progress derived from completed attempts and best
// scaled score. This is read-only static structure for now; future work
// will tie each stage to a concrete drill or learning-path entry.
// ═════════════════════════════════════════════════════════════════════════════

public interface IListeningCurriculumService
{
    Task<ListeningCurriculumDto> GetCurriculumAsync(string userId, CancellationToken ct);
}

public sealed record ListeningCurriculumStageDto(
    int Order,
    string Code,
    string Title,
    string Focus,
    string PartHint,                  // "Part A" | "Part B" | "Part C" | "Mock"
    int EstimatedMinutes,
    bool Locked,
    bool Completed);

public sealed record ListeningCurriculumDto(
    string Headline,
    int CompletedStages,
    int TotalStages,
    IReadOnlyList<ListeningCurriculumStageDto> Stages);

public sealed class ListeningCurriculumService(LearnerDbContext db) : IListeningCurriculumService
{
    private const string Subtest = "listening";

    private static readonly (string Code, string Title, string Focus, string PartHint, int Minutes)[] Catalog =
    {
        ("numbers_units",          "Numbers, dosages and units",      "Decoding fast-spoken numbers (5 mg vs 50 mg, BPM, %).", "Part A", 25),
        ("names_spelling",         "Patient names and spelling",      "Capturing names letter-by-letter without losing pace.", "Part A", 20),
        ("times_dates",            "Times, dates and frequencies",    "Distinguishing 'every 4 hours' vs '4 a.m.' vs '4-hourly'.", "Part A", 20),
        ("symptoms_meds",          "Symptoms and medications",        "Hearing the exact medical term, not a paraphrase.", "Part A", 30),
        ("paraphrase_traps",       "Paraphrase traps",                "When the speaker re-words the prompt — write THEIR word.", "Part A", 25),
        ("plurals_articles",       "Plurals, articles and number",    "Plural / singular and a / the only when meaning changes.", "Part A", 15),
        ("mcq_too_strong_too_weak", "MCQ — too strong vs too weak",   "Reading qualifiers (always / often / rarely) under pressure.", "Part B", 25),
        ("mcq_wrong_speaker",      "MCQ — wrong speaker",             "Tracking which speaker held an opinion, not just what was said.", "Part B", 20),
        ("mcq_opposite_meaning",   "MCQ — opposite meaning",          "Negation flips ('didn't refuse' = 'agreed').", "Part C", 20),
        ("attitude_tags",          "Speaker attitude (Part C)",       "Concerned / optimistic / doubtful / critical / neutral.", "Part C", 25),
        ("listening_loop",         "The post-attempt listening loop", "Re-listen with the transcript to fix one weakness.", "Part A/B/C", 30),
        ("full_mock",              "Full Listening mock",             "End-to-end one-play simulation under exam conditions.", "Mock", 50),
    };

    public async Task<ListeningCurriculumDto> GetCurriculumAsync(string userId, CancellationToken ct)
    {
        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.SubtestCode == Subtest && a.State == AttemptState.Submitted)
            .CountAsync(ct);

        var stages = new List<ListeningCurriculumStageDto>(Catalog.Length);
        for (var i = 0; i < Catalog.Length; i++)
        {
            var c = Catalog[i];
            // Heuristic progress until the relational schema lands: each
            // submitted attempt unlocks one stage as "completed".
            var completed = attempts > i;
            // Lock stages that come after the next not-completed one + 1.
            var locked = i > attempts + 1;
            stages.Add(new ListeningCurriculumStageDto(
                Order: i + 1,
                Code: c.Code,
                Title: c.Title,
                Focus: c.Focus,
                PartHint: c.PartHint,
                EstimatedMinutes: c.Minutes,
                Locked: locked,
                Completed: completed));
        }

        return new ListeningCurriculumDto(
            Headline: attempts == 0
                ? "Start at stage 1 — Numbers, dosages and units."
                : $"You've completed {attempts} Listening attempt{(attempts == 1 ? string.Empty : "s")}. Keep moving through the curriculum.",
            CompletedStages: stages.Count(s => s.Completed),
            TotalStages: stages.Count,
            Stages: stages);
    }
}
