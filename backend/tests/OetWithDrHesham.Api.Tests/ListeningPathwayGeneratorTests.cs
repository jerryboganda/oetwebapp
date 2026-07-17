using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Listening;

namespace OetWithDrHesham.Api.Tests;

// ═════════════════════════════════════════════════════════════════════════════
// ListeningPathwayGeneratorTests — pure-function tests for A4
//
// Validates the algorithm in ListeningPathwayGenerator without spinning up the
// WebApplicationFactory or hitting EF Core. The generator is pure: same input
// → same output, no DB / I/O / wall-clock access. Each test fixes `Now` so the
// derived weeks-to-exam window is deterministic.
//
// Covers the rules called out in OET_LISTENING_MODULE_PATHWAY.md §6.5:
//   • Default 12-week plan when no exam date is supplied
//   • L2 (note-taking speed) is promoted to foundation slot #1 when in bottom-3
//   • Accent variance > 30pp injects a dedicated "Accent immersion week"
//   • Exam ≤ 4 weeks away still produces a usable plan (clamps to minimum)
//   • Determinism: same input twice produces identical output
// ═════════════════════════════════════════════════════════════════════════════

public sealed class ListeningPathwayGeneratorTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly IListeningPathwayGenerator _gen = new ListeningPathwayGenerator();

    [Fact]
    public void Generate_NoExamDate_Returns12WeekPlan()
    {
        var result = _gen.Generate(new GenerateInput(
            TargetBand: "B",
            ExamDate: null,
            HoursPerWeek: 8,
            SkillScores: new Dictionary<string, decimal>(),
            AccentScores: new Dictionary<string, decimal>(),
            Now: FixedNow));

        Assert.Equal(12, result.Count);
        Assert.All(result, w => Assert.InRange(w.WeekNumber, 1, 12));
        // Phases should partition cleanly across the 12 weeks: 2 foundation,
        // some practice, and ~2 mastery (12 * 0.20 ≈ 2). Confirm we hit all
        // three phases at least once.
        Assert.Contains(result, w => w.Phase == "foundation");
        Assert.Contains(result, w => w.Phase == "practice");
        Assert.Contains(result, w => w.Phase == "mastery");
    }

    [Fact]
    public void Generate_L2WeakestInBottom3_PromotesL2ToFirstFoundationFocus()
    {
        // Mark 5 skills "strong" (>= 6) so the bottom-3-weakest set is exactly
        // { L2, L5, L8 }. L5 is the weakest by score, so the natural ordering
        // would put L5 in slot #0. The §6.5 promotion rule says: if L2 is in
        // the bottom-3 weakest, lift L2 to slot #0 regardless of raw score —
        // note-taking is a gating skill for Part A.
        var scores = new Dictionary<string, decimal>
        {
            ["L1"] = 8m,
            ["L2"] = 3m,   // weak (middle of the 3)
            ["L3"] = 8m,
            ["L4"] = 8m,
            ["L5"] = 1m,   // weak (lowest)
            ["L6"] = 8m,
            ["L7"] = 8m,
            ["L8"] = 5m,   // weak (highest of the 3)
        };

        var result = _gen.Generate(new GenerateInput(
            TargetBand: "B",
            ExamDate: null,
            HoursPerWeek: 8,
            SkillScores: scores,
            AccentScores: new Dictionary<string, decimal>(),
            Now: FixedNow));

        var foundationWeek = result.First(w => w.Phase == "foundation");
        Assert.Equal("L2", foundationWeek.FocusSkills[0]);
        // L2 was middle by raw score, so the promotion must have explicitly
        // moved it to slot #0; assert the other two still appear after it.
        Assert.Contains("L5", foundationWeek.FocusSkills);
        Assert.Contains("L8", foundationWeek.FocusSkills);
    }

    [Fact]
    public void Generate_AccentVarianceOver30_InsertsImmersionWeek()
    {
        // 50 pp variance (90 vs 40) exceeds the 30 pp threshold — generator
        // must dedicate one practice week to accent immersion using the
        // weakest accent (us at 40%).
        var accents = new Dictionary<string, decimal>
        {
            ["british"] = 90m,
            ["us"] = 40m,
        };

        var result = _gen.Generate(new GenerateInput(
            TargetBand: "B",
            ExamDate: null,
            HoursPerWeek: 8,
            SkillScores: new Dictionary<string, decimal>(),
            AccentScores: accents,
            Now: FixedNow));

        var immersionWeek = Assert.Single(
            result,
            w => !string.IsNullOrEmpty(w.Notes)
                && w.Notes.Contains("immersion", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("practice", immersionWeek.Phase);
        Assert.Equal(new[] { "mixed" }, immersionWeek.FocusSkills);
        Assert.Equal(new[] { "us" }, immersionWeek.FocusAccents);
    }

    [Fact]
    public void Generate_ExamIn3Weeks_ClampsToMinimum4Weeks()
    {
        // 21 days = 3 weeks. The generator's MinWeeksToExam is 4 — even with
        // a closer exam, learners should still receive a 4-week plan rather
        // than a useless 3-week stub.
        var result = _gen.Generate(new GenerateInput(
            TargetBand: "B",
            ExamDate: FixedNow.AddDays(21),
            HoursPerWeek: 8,
            SkillScores: new Dictionary<string, decimal>(),
            AccentScores: new Dictionary<string, decimal>(),
            Now: FixedNow));

        // Ceiling(21 / 7) = 3, clamped up to 4. The implementation uses
        // Math.Ceiling so a learner mid-day may see 4 weeks too — accept the
        // common 4-5 band.
        Assert.InRange(result.Count, 4, 5);
        Assert.Contains(result, w => w.Phase == "foundation");
        Assert.Contains(result, w => w.Phase == "mastery");
    }

    [Fact]
    public void Generate_DeterministicGivenSameInput()
    {
        var input = new GenerateInput(
            TargetBand: "B",
            ExamDate: null,
            HoursPerWeek: 8,
            SkillScores: new Dictionary<string, decimal>
            {
                ["L1"] = 4m,
                ["L2"] = 3m,
                ["L5"] = 5m,
            },
            AccentScores: new Dictionary<string, decimal>
            {
                ["british"] = 70m,
                ["us"] = 50m,
            },
            Now: FixedNow);

        var first = _gen.Generate(input);
        var second = _gen.Generate(input);

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].WeekNumber, second[i].WeekNumber);
            Assert.Equal(first[i].Phase, second[i].Phase);
            Assert.Equal(first[i].FocusSkills, second[i].FocusSkills);
            Assert.Equal(first[i].FocusAccents, second[i].FocusAccents);
            Assert.Equal(first[i].DailyMinutes, second[i].DailyMinutes);
            Assert.Equal(first[i].MockAtEndOfWeek, second[i].MockAtEndOfWeek);
            Assert.Equal(first[i].Notes, second[i].Notes);
        }
    }
}
