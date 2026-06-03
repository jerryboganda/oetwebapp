using System.Collections.Immutable;
using OetLearner.Api.Contracts;

namespace OetLearner.Api.Services.Listening;

// ═════════════════════════════════════════════════════════════════════════════
// Listening Pathway Generator — Phase 1 (A11)
//
// Pure-function service that turns a learner profile + diagnostic
// results into a 12-week (or exam-date-scaled) Listening study roadmap.
//
// Mirrors Reading's pathway generator (see ReadingLearnerPathwayService.
// GeneratePathwayAsync) but specialised for Listening's 8 sub-skills (L1..L8)
// and 4 target accents (british / australian / us / non_native), per
// OET_LISTENING_MODULE_PATHWAY.md §4 + §6.5.
//
// Pure function:
//   • No DB access. No file I/O. No HTTP. No randomness.
//   • No DateTimeOffset.UtcNow inside the body — caller passes `Now` so the
//     output is deterministic and unit-testable.
//   • Same input → same output, byte-for-byte.
//
// Algorithm (see GenerateInput / Generate XML doc for the full spec):
//   1. Clamp weeksToExam to [4, 16] (default 12 when no exam date).
//   2. Compute weak skills (score < 6) and strong skills (score >= 8).
//   3. Compute weakest accents (accuracy < 65%) and the BR/AU/US/NN variance.
//   4. Partition weeks into foundation / practice / mastery phases:
//        foundation = clamp(round(weeks * 0.15), 1, 2)
//        mastery    = clamp(round(weeks * 0.20), 1, 4)
//        practice   = remainder
//   5. Foundation weeks → top-3 weakest skills (L2 promoted if in bottom-3
//      because note-taking blocks every Part-A item).
//   6. Practice weeks → rotate through weak skills and weak accents; insert
//      mocks every 4 weeks; if accent variance > 30pp, dedicate one practice
//      week to an "Accent immersion week".
//   7. Mastery weeks → mixed-skill full mocks every week with strategy review.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Pure-function generator that produces a Listening study roadmap
/// from a learner profile + diagnostic snapshot.</summary>
public interface IListeningPathwayGenerator
{
    /// <summary>Generate the multi-week roadmap. Deterministic — never touches
    /// DB, I/O, or wall-clock time. Same input always produces the same output.</summary>
    IReadOnlyList<RoadmapWeekDto> Generate(GenerateInput input);
}

/// <summary>Snapshot of the inputs the generator needs. All time-sensitive
/// fields (<paramref name="Now"/>) are passed in by the caller for testability.</summary>
/// <param name="TargetBand">Learner's target OET band: "B" | "B+" | "A".
/// Currently informational — phase widths are exam-date driven.</param>
/// <param name="ExamDate">Optional exam date. When null, defaults to a 12-week plan.</param>
/// <param name="HoursPerWeek">Onboarding-declared weekly study budget; drives DailyMinutes.</param>
/// <param name="SkillScores">Map of L1..L8 → diagnostic baseline (0..10).
/// Missing keys are treated as score 0 (i.e. weak). Unknown keys are ignored.</param>
/// <param name="AccentScores">Map of accent code → accuracy percentage (0..100).
/// Recognised codes: "british", "australian", "us", "non_native".</param>
/// <param name="Now">The "current time" the generator should treat as today.
/// Always supply <c>DateTimeOffset.UtcNow</c> from the caller — never from inside.</param>
public sealed record GenerateInput(
    string TargetBand,
    DateTimeOffset? ExamDate,
    int HoursPerWeek,
    IReadOnlyDictionary<string, decimal> SkillScores,
    IReadOnlyDictionary<string, decimal> AccentScores,
    DateTimeOffset Now);

/// <summary>Default <see cref="IListeningPathwayGenerator"/> implementation.
/// Stateless and thread-safe — safe to register as a Singleton.</summary>
public sealed class ListeningPathwayGenerator : IListeningPathwayGenerator
{
    // L1..L8 — see Domain/ListeningPathwayEntities.cs::LearnerListeningSkillScore
    // and §2.5 of OET_LISTENING_MODULE_PATHWAY.md.
    private static readonly ImmutableArray<string> AllSkillCodes =
        ImmutableArray.Create("L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8");

    // Display labels — exposed via the static helper so the API layer can echo
    // a learner-friendly name alongside the L-codes without re-defining the map.
    private static readonly ImmutableDictionary<string, string> SkillLabels =
        ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
        {
            KeyValuePair.Create("L1", "Detail capture"),
            KeyValuePair.Create("L2", "Note-taking speed"),
            KeyValuePair.Create("L3", "Spelling accuracy"),
            KeyValuePair.Create("L4", "Gist comprehension"),
            KeyValuePair.Create("L5", "Distractor recognition"),
            KeyValuePair.Create("L6", "Inference"),
            KeyValuePair.Create("L7", "Speaker stance"),
            KeyValuePair.Create("L8", "Accent adaptation"),
        });

    private const decimal WeakSkillThreshold = 6m;
    // Spec also names a StrongSkillThreshold of 8m (scores >= 8 = "strong"), but
    // no downstream phase consumes that bucket today, so the constant lives in
    // the spec comment until a future stage (e.g. predictive band model) needs it.
    private const decimal WeakAccentThreshold = 65m;
    private const decimal AccentVarianceThreshold = 30m;

    private const int MinWeeksToExam = 4;
    private const int MaxWeeksToExam = 16;
    private const int DefaultWeeksWithoutExam = 12;
    private const int MaxFoundationWeeks = 2;
    private const int MaxMasteryWeeks = 4;
    private const int MinDailyMinutes = 20;

    /// <summary>Returns the human-readable display label for an L1..L8 code,
    /// or the code itself if unknown. Used by API layer / tests.</summary>
    public static string GetSkillLabel(string skillCode)
    {
        if (string.IsNullOrWhiteSpace(skillCode)) return skillCode ?? string.Empty;
        return SkillLabels.TryGetValue(skillCode, out var label) ? label : skillCode;
    }

    /// <inheritdoc />
    public IReadOnlyList<RoadmapWeekDto> Generate(GenerateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. Weeks-to-exam — clamped so a learner who booked a week away still
        //    gets a usable plan, and a learner who booked years out doesn't get
        //    a 100-week roadmap.
        var weeksToExam = ComputeWeeksToExam(input.ExamDate, input.Now);

        // 2/3. Bucket skills by weakness/strength.
        //      A missing L-code is treated as score 0 (i.e. very weak) — that
        //      way an empty SkillScores dict still produces a sensible plan
        //      that targets every sub-skill.
        var weakSkills = ComputeWeakSkills(input.SkillScores);

        // 4. Bucket accents by weakness + compute variance for the accent-immersion rule.
        var weakestAccents = ComputeWeakestAccents(input.AccentScores);
        var accentVariance = ComputeAccentVariance(input.AccentScores);

        // 5. Phase boundaries — foundation 15%, mastery 20%, practice = rest.
        var foundationWeeks = Math.Clamp(
            (int)Math.Round(weeksToExam * 0.15, MidpointRounding.AwayFromZero),
            1,
            MaxFoundationWeeks);
        var masteryWeeks = Math.Clamp(
            (int)Math.Round(weeksToExam * 0.20, MidpointRounding.AwayFromZero),
            1,
            MaxMasteryWeeks);
        var practiceWeeks = Math.Max(0, weeksToExam - foundationWeeks - masteryWeeks);

        var dailyMinutes = ComputeDailyMinutes(input.HoursPerWeek);

        var weeks = new List<RoadmapWeekDto>(weeksToExam);

        // 6. Foundation phase — front-load the 3 weakest sub-skills + 1 weak accent.
        //    Special rule: if L2 (note-taking speed) is in the bottom 3, promote it
        //    to position #1. Note-taking is the gating skill for every Part A item;
        //    without it, no amount of detail-capture drilling rescues Part A.
        var foundationFocusSkills = SelectFoundationFocusSkills(weakSkills);
        var foundationFocusAccent = weakestAccents.Length > 0
            ? new[] { weakestAccents[0] }
            : Array.Empty<string>();
        var foundationNotes = "Foundation lessons for " + string.Join(", ", foundationFocusSkills);

        for (var i = 0; i < foundationWeeks; i++)
        {
            weeks.Add(new RoadmapWeekDto
            {
                WeekNumber = weeks.Count + 1,
                Phase = "foundation",
                FocusSkills = foundationFocusSkills,
                FocusAccents = foundationFocusAccent,
                DailyMinutes = dailyMinutes,
                MockAtEndOfWeek = false,
                Notes = foundationNotes,
            });
        }

        // 7. Practice phase — rotate through weak skills / weak accents.
        //    • Mock at end of week every 4 weeks (week index 3, 7, 11, ...).
        //    • If accent variance > 30 pp, the first practice week becomes a
        //      dedicated accent-immersion week using the weakest accent.
        //    • If there are no weak skills, fall back to L1 (detail capture) —
        //      it's the highest-volume sub-skill and benefits anyone.
        var practiceSkillPool = weakSkills.Length > 0
            ? weakSkills
            : ImmutableArray.Create("L1");
        var accentImmersionWeekIndex = accentVariance > AccentVarianceThreshold && weakestAccents.Length > 0
            ? 0
            : -1;

        for (var i = 0; i < practiceWeeks; i++)
        {
            var weekNumber = weeks.Count + 1;
            string[] focusSkills;
            string[] focusAccents;
            string notes;

            if (i == accentImmersionWeekIndex)
            {
                // Dedicated accent immersion — single accent, mixed skill emphasis.
                var accent = weakestAccents[0];
                focusSkills = new[] { "mixed" };
                focusAccents = new[] { accent };
                notes = "Accent immersion week";
            }
            else
            {
                var skill = practiceSkillPool[i % practiceSkillPool.Length];
                focusSkills = new[] { skill };
                focusAccents = weakestAccents.Length > 0
                    ? new[] { weakestAccents[i % weakestAccents.Length] }
                    : Array.Empty<string>();
                notes = "Targeted practice on " + skill;
            }

            var mockThisWeek = (i % 4) == 3;

            weeks.Add(new RoadmapWeekDto
            {
                WeekNumber = weekNumber,
                Phase = "practice",
                FocusSkills = focusSkills,
                FocusAccents = focusAccents,
                DailyMinutes = dailyMinutes,
                MockAtEndOfWeek = mockThisWeek,
                Notes = notes,
            });
        }

        // 8. Mastery phase — weekly mocks + strategy review.
        //    Focus is "mixed" because by this stage we're rehearsing the full
        //    test, not isolating sub-skills.
        for (var i = 0; i < masteryWeeks; i++)
        {
            weeks.Add(new RoadmapWeekDto
            {
                WeekNumber = weeks.Count + 1,
                Phase = "mastery",
                FocusSkills = new[] { "mixed" },
                FocusAccents = Array.Empty<string>(),
                DailyMinutes = dailyMinutes,
                MockAtEndOfWeek = true,
                Notes = "Full mock + review + strategy of the week",
            });
        }

        return weeks;
    }

    // ── Phase helpers ────────────────────────────────────────────────────────

    private static int ComputeWeeksToExam(DateTimeOffset? examDate, DateTimeOffset now)
    {
        if (!examDate.HasValue)
        {
            return DefaultWeeksWithoutExam;
        }

        var totalDays = (examDate.Value - now).TotalDays;
        // Past or same-day exam → snap to the minimum so we still emit a usable plan.
        if (double.IsNaN(totalDays) || totalDays <= 0)
        {
            return MinWeeksToExam;
        }

        var weeks = (int)Math.Ceiling(totalDays / 7.0);
        return Math.Clamp(weeks, MinWeeksToExam, MaxWeeksToExam);
    }

    private static int ComputeDailyMinutes(int hoursPerWeek)
    {
        if (hoursPerWeek <= 0) return MinDailyMinutes;
        var perDay = (hoursPerWeek * 60) / 7;
        return Math.Max(MinDailyMinutes, perDay);
    }

    private static ImmutableArray<string> ComputeWeakSkills(
        IReadOnlyDictionary<string, decimal> skillScores)
    {
        // Walk the canonical L1..L8 set, look up each score (default 0 when
        // missing — treats absence as weak), filter < 6, then sort ascending.
        var builder = ImmutableArray.CreateBuilder<(string Code, decimal Score)>();
        foreach (var code in AllSkillCodes)
        {
            var score = skillScores is not null && skillScores.TryGetValue(code, out var s)
                ? s
                : 0m;
            if (score < WeakSkillThreshold)
            {
                builder.Add((code, score));
            }
        }

        return builder
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .Select(x => x.Code)
            .ToImmutableArray();
    }

    private static string[] SelectFoundationFocusSkills(ImmutableArray<string> weakSkills)
    {
        if (weakSkills.IsDefaultOrEmpty)
        {
            // No weak signal at all — still anchor on L1/L2/L4 because those are
            // the highest-leverage entry-level skills.
            return new[] { "L1", "L2", "L4" };
        }

        // Take the top 3 weakest as the foundation focus.
        var top3 = weakSkills.Length <= 3
            ? weakSkills.ToArray()
            : weakSkills.Take(3).ToArray();

        // Special rule from §6.5: if L2 is in the bottom-3 weakest, promote it
        // to slot #1 regardless of its raw score. Without note-taking the
        // learner cannot capture Part A details, so this is a hard pedagogical
        // dependency.
        var l2Index = Array.IndexOf(top3, "L2");
        if (l2Index > 0)
        {
            var promoted = new string[top3.Length];
            promoted[0] = "L2";
            var w = 1;
            for (var i = 0; i < top3.Length; i++)
            {
                if (i == l2Index) continue;
                promoted[w++] = top3[i];
            }
            return promoted;
        }

        return top3;
    }

    private static string[] ComputeWeakestAccents(
        IReadOnlyDictionary<string, decimal> accentScores)
    {
        if (accentScores is null || accentScores.Count == 0)
        {
            return Array.Empty<string>();
        }

        return accentScores
            .Where(kvp => kvp.Value < WeakAccentThreshold)
            .OrderBy(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => kvp.Key)
            .ToArray();
    }

    private static decimal ComputeAccentVariance(
        IReadOnlyDictionary<string, decimal> accentScores)
    {
        if (accentScores is null || accentScores.Count < 2)
        {
            return 0m;
        }

        decimal max = decimal.MinValue;
        decimal min = decimal.MaxValue;
        foreach (var value in accentScores.Values)
        {
            if (value > max) max = value;
            if (value < min) min = value;
        }
        return max - min;
    }
}


