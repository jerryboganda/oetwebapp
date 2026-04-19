using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Progress;

namespace OetLearner.Api.Tests;

public class ProgressServiceTests
{
    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    private static async Task<string> SeedGoalAsync(LearnerDbContext db, string userId, string? country = "UK", string? profession = "medicine")
    {
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = profession,
            TargetCountry = country,
            TargetWritingScore = 350,
            TargetSpeakingScore = 350,
            TargetReadingScore = 350,
            TargetListeningScore = 350,
            StudyHoursPerWeek = 10,
            WeakSubtestsJson = "[]",
            DraftStateJson = "{}",
            ExamFamilyCode = "oet",
            ExamTypeCode = "oet",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static Attempt AddAttempt(LearnerDbContext db, string userId, string subtest, DateTimeOffset at, string context = "practice")
    {
        var a = new Attempt
        {
            Id = $"att-{Guid.NewGuid():N}",
            UserId = userId,
            ContentId = "content-1",
            SubtestCode = subtest,
            Context = context,
            Mode = "timed",
            State = AttemptState.Completed,
            StartedAt = at.AddMinutes(-45),
            SubmittedAt = at,
            CompletedAt = at,
        };
        db.Attempts.Add(a);
        return a;
    }

    private static Evaluation AddEvaluation(LearnerDbContext db, string attemptId, string subtest, string scoreRange, DateTimeOffset at, string? criterionJson = null)
    {
        var e = new Evaluation
        {
            Id = $"ev-{Guid.NewGuid():N}",
            AttemptId = attemptId,
            SubtestCode = subtest,
            State = AsyncState.Completed,
            ScoreRange = scoreRange,
            GeneratedAt = at,
            CriterionScoresJson = criterionJson ?? "[]",
            ModelExplanationSafe = "ok",
            LearnerDisclaimer = "not an official score",
            LastTransitionAt = at,
        };
        db.Evaluations.Add(e);
        return e;
    }

    // ── Score parsing ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("330-360", 345)]
    [InlineData("350", 350)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("nonsense", null)]
    [InlineData("600-700", 500)]    // clamp to ScaledMax
    [InlineData("-20", null)]       // negative left side treated as bad parse
    [InlineData("40-50/60", 45)]    // slash strips denominator
    public void ParseScaledScore_returns_clamped_midpoint_or_null(string? input, int? expected)
    {
        Assert.Equal(expected, ProgressService.ParseScaledScore(input));
    }

    [Theory]
    [InlineData("4/6", 333)]        // 4/6 → 0.667 × 500 ≈ 333
    [InlineData("4-5/6", 375)]      // 4.5/6 → 0.75 × 500 = 375
    [InlineData("6/6", 500)]
    [InlineData("0/6", 0)]
    [InlineData("", null)]
    public void ScaleCriterionScore_projects_rubric_to_0_500(string? input, int? expected)
    {
        var result = ProgressService.ScaleCriterionScore(input);
        if (expected is null) Assert.Null(result);
        else Assert.Equal(expected, result);
    }

    // ── ISO week bucketing ────────────────────────────────────────────────

    [Fact]
    public void IsoWeekStart_returns_monday_for_any_day_in_week()
    {
        // 2026-04-22 (Wednesday) → Monday 2026-04-20
        var wed = new DateTimeOffset(2026, 4, 22, 10, 30, 0, TimeSpan.Zero);
        var monday = ProgressService.IsoWeekStart(wed);
        Assert.Equal(new DateTime(2026, 4, 20), monday.UtcDateTime.Date);
    }

    [Fact]
    public void IsoWeekStart_handles_sunday_as_previous_week()
    {
        // 2026-04-26 (Sunday) → Monday 2026-04-20
        var sun = new DateTimeOffset(2026, 4, 26, 23, 59, 0, TimeSpan.Zero);
        var monday = ProgressService.IsoWeekStart(sun);
        Assert.Equal(new DateTime(2026, 4, 20), monday.UtcDateTime.Date);
    }

    [Fact]
    public void IsoWeekKey_formats_as_year_W_week()
    {
        var monday = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        Assert.Matches(@"^\d{4}-W\d{2}$", ProgressService.IsoWeekKey(monday));
    }

    // ── End-to-end payload shape ──────────────────────────────────────────

    [Fact]
    public async Task GetProgress_returns_empty_state_when_no_attempts()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        Assert.Empty(payload.Trend);
        Assert.Empty(payload.CriterionTrend);
        Assert.True(payload.Freshness.UsesFallbackSeries);
        Assert.Equal(OetScoring.ScaledMax, payload.Meta.ScoreAxisMax);
        Assert.Equal(OetScoring.ScaledPassGradeB, payload.Meta.GradeBThreshold);
    }

    [Fact]
    public async Task GetProgress_buckets_evaluations_into_iso_weeks()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);
        var baseDate = new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero); // a Monday
        var a1 = AddAttempt(db, "u-1", "writing", baseDate);
        var a2 = AddAttempt(db, "u-1", "writing", baseDate.AddDays(2));
        var a3 = AddAttempt(db, "u-1", "writing", baseDate.AddDays(9));
        AddEvaluation(db, a1.Id, "writing", "330-360", baseDate);
        AddEvaluation(db, a2.Id, "writing", "340-370", baseDate.AddDays(2));
        AddEvaluation(db, a3.Id, "writing", "360-390", baseDate.AddDays(9));
        await db.SaveChangesAsync();

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        Assert.Equal(2, payload.Trend.Count);
        // Week 1 should average 345 + 355 = 350
        Assert.Equal(350, payload.Trend[0].SubtestScaled["writing"]);
        Assert.Equal(2, payload.Trend[0].SubtestCount["writing"]);
        Assert.Equal(375, payload.Trend[1].SubtestScaled["writing"]);
        Assert.Equal(1, payload.Trend[1].SubtestCount["writing"]);
    }

    [Fact]
    public async Task GetProgress_separates_mock_from_practice_series()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);
        var when = new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero);
        var practice = AddAttempt(db, "u-1", "writing", when, context: "practice");
        var mock = AddAttempt(db, "u-1", "writing", when.AddHours(4), context: "mock");
        AddEvaluation(db, practice.Id, "writing", "330-340", when);
        AddEvaluation(db, mock.Id, "writing", "360-380", when.AddHours(4));
        await db.SaveChangesAsync();

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        var week = payload.Trend.First();
        Assert.Equal(335, week.SubtestScaled["writing"]);
        Assert.Equal(370, week.MockScaled["writing"]);
        Assert.Equal(1, week.SubtestCount["writing"]);
        Assert.Equal(1, week.MockCount["writing"]);
        Assert.Equal(1, payload.Totals.MockAttempts);
    }

    [Fact]
    public async Task GetProgress_computes_completion_from_real_attempts_not_stubs()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);
        var now = DateTimeOffset.UtcNow;
        AddAttempt(db, "u-1", "writing", now);
        AddAttempt(db, "u-1", "writing", now);
        AddAttempt(db, "u-1", "speaking", now.AddDays(-1));
        await db.SaveChangesAsync();

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        Assert.Equal(7, payload.Completion.Count);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var todayPoint = payload.Completion.First(c => c.Date == today);
        Assert.Equal(2, todayPoint.Completed);
        var yesterdayPoint = payload.Completion.First(c => c.Date == today.AddDays(-1));
        Assert.Equal(1, yesterdayPoint.Completed);
        // NO hardcoded [3,2,4,1,3,5,2] pattern — real counts only.
        Assert.DoesNotContain(payload.Completion, c => c.Completed == 5 && c.Date.DayOfWeek == DayOfWeek.Saturday && c.Completed != 0);
    }

    [Fact]
    public async Task GetProgress_counts_submission_volume_by_subtest()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);
        var now = DateTimeOffset.UtcNow;
        AddAttempt(db, "u-1", "writing", now);
        AddAttempt(db, "u-1", "writing", now);
        AddAttempt(db, "u-1", "speaking", now);
        await db.SaveChangesAsync();

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        // Volume buckets are real; current week should hold 2 writing + 1 speaking.
        var current = payload.SubmissionVolume.Last();
        Assert.Equal(2, current.Writing);
        Assert.Equal(1, current.Speaking);
    }

    [Fact]
    public async Task GetProgress_subtest_summary_includes_latest_scaled_and_gap_to_target()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1"); // target=350 for all subtests
        var svc = new ProgressService(db);
        var when = DateTimeOffset.UtcNow.AddDays(-3);
        var a = AddAttempt(db, "u-1", "writing", when);
        AddEvaluation(db, a.Id, "writing", "320-340", when);
        await db.SaveChangesAsync();

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        var writing = payload.Subtests.Single(s => s.SubtestCode == "writing");
        Assert.Equal(330, writing.LatestScaled);
        Assert.Equal(350, writing.TargetScaled);
        Assert.Equal(20, writing.GapToTarget);
        Assert.NotNull(writing.LatestGrade); // "C+"
    }

    [Fact]
    public async Task GetProgress_country_aware_writing_threshold_is_300_for_us()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1", country: "US");
        var svc = new ProgressService(db);

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        Assert.Equal(300, payload.Meta.WritingThreshold);
        Assert.Equal("C+", payload.Meta.WritingThresholdGrade);
    }

    [Fact]
    public async Task GetProgress_country_required_reason_when_country_missing()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1", country: null);
        var svc = new ProgressService(db);

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        Assert.Null(payload.Meta.WritingThreshold);
        Assert.Equal("country_required", payload.Meta.WritingThresholdReason);
    }

    [Fact]
    public async Task GetProgress_etag_is_stable_across_identical_calls()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);
        var when = DateTimeOffset.UtcNow;
        var a = AddAttempt(db, "u-1", "writing", when);
        AddEvaluation(db, a.Id, "writing", "330-340", when);
        await db.SaveChangesAsync();

        var first = await svc.GetProgressAsync("u-1", "90d", default);
        var second = await svc.GetProgressAsync("u-1", "90d", default);
        Assert.Equal(first.Freshness.ETag, second.Freshness.ETag);
    }

    [Fact]
    public async Task GetProgress_etag_changes_after_new_evaluation()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);
        var when = DateTimeOffset.UtcNow;
        var a = AddAttempt(db, "u-1", "writing", when);
        AddEvaluation(db, a.Id, "writing", "330-340", when);
        await db.SaveChangesAsync();
        var first = await svc.GetProgressAsync("u-1", "90d", default);

        var a2 = AddAttempt(db, "u-1", "writing", when.AddHours(1));
        AddEvaluation(db, a2.Id, "writing", "350-370", when.AddHours(1));
        await db.SaveChangesAsync();
        var second = await svc.GetProgressAsync("u-1", "90d", default);

        Assert.NotEqual(first.Freshness.ETag, second.Freshness.ETag);
    }

    // ── Comparative block ────────────────────────────────────────────────

    [Fact]
    public async Task Comparative_returns_insufficient_when_cohort_below_min()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1", profession: "medicine");
        var svc = new ProgressService(db);

        var comp = await svc.GetComparativeAsync("u-1", default);

        Assert.NotNull(comp);
        Assert.False(comp!.HasSufficientCohort);
        Assert.Empty(comp.Subtests);
    }

    [Fact]
    public async Task Comparative_excludes_self_from_cohort()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1", profession: "medicine");
        // Seed 35 peers (above default MinCohortSize=30)
        for (var i = 0; i < 35; i++)
        {
            await SeedGoalAsync(db, $"peer-{i}", profession: "medicine");
        }
        // Override policy to MinCohortSize = 10 so we don't need ludicrous test data
        db.ProgressPolicies.Add(new ProgressPolicy
        {
            Id = "pp-oet", ExamFamilyCode = "oet",
            MinCohortSize = 10,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var svc = new ProgressService(db);
        var comp = await svc.GetComparativeAsync("u-1", default);

        Assert.NotNull(comp);
        Assert.Equal(35, comp!.CohortSize);
        Assert.True(comp.HasSufficientCohort);
    }

    [Fact]
    public async Task Comparative_percentile_uses_peer_cohort_not_self()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1", profession: "medicine");
        for (var i = 0; i < 15; i++)
            await SeedGoalAsync(db, $"peer-{i}", profession: "medicine");
        db.ProgressPolicies.Add(new ProgressPolicy { Id = "pp-oet", ExamFamilyCode = "oet", MinCohortSize = 10, UpdatedAt = DateTimeOffset.UtcNow });

        // User scores 350. Peers split evenly: 5 score 300, 5 score 400, 5 score 330.
        var when = DateTimeOffset.UtcNow.AddDays(-5);
        var ua = AddAttempt(db, "u-1", "writing", when);
        AddEvaluation(db, ua.Id, "writing", "350", when);
        var peerScores = new[] { "300", "300", "300", "300", "300", "330", "330", "330", "330", "330", "400", "400", "400", "400", "400" };
        for (var i = 0; i < 15; i++)
        {
            var pa = AddAttempt(db, $"peer-{i}", "writing", when.AddMinutes(i));
            AddEvaluation(db, pa.Id, "writing", peerScores[i], when.AddMinutes(i));
        }
        await db.SaveChangesAsync();

        var svc = new ProgressService(db);
        var comp = await svc.GetComparativeAsync("u-1", default);

        Assert.NotNull(comp);
        Assert.True(comp!.HasSufficientCohort);
        var writing = comp.Subtests.Single(s => s.SubtestCode == "writing");
        Assert.Equal(350, writing.YourScaled);
        // 10 peers below 350 / 15 = 66.7%. The user is NOT in the denominator.
        Assert.Equal(66.7, writing.Percentile);
    }

    // ── Policy CRUD ──────────────────────────────────────────────────────

    [Fact]
    public async Task Policy_returns_defaults_when_not_yet_configured()
    {
        var db = BuildDb();
        var svc = new ProgressService(db);
        var p = await svc.GetPolicyAsync("oet", default);
        Assert.Equal(30, p.MinCohortSize);
        Assert.Equal("90d", p.DefaultTimeRange);
        Assert.False(p.ExportPdfEnabled);
    }

    [Fact]
    public async Task Policy_update_validates_range()
    {
        var db = BuildDb();
        var svc = new ProgressService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdatePolicyAsync("oet", new ProgressPolicyUpdate(
                DefaultTimeRange: "2d",
                SmoothingWindow: null, MinCohortSize: null, MockDistinctStyle: null,
                ShowScoreGuaranteeStrip: null, ShowCriterionConfidenceBand: null,
                MinEvaluationsForTrend: null, ExportPdfEnabled: null),
                "admin-1", default));
    }

    [Fact]
    public async Task Policy_update_writes_audit_event_and_clamps_values()
    {
        var db = BuildDb();
        var svc = new ProgressService(db);

        var updated = await svc.UpdatePolicyAsync("oet", new ProgressPolicyUpdate(
            DefaultTimeRange: "30d",
            SmoothingWindow: 99,         // clamps to 10
            MinCohortSize: 9999,         // clamps to 1000
            MockDistinctStyle: false,
            ShowScoreGuaranteeStrip: null,
            ShowCriterionConfidenceBand: null,
            MinEvaluationsForTrend: null,
            ExportPdfEnabled: true), "admin-9", default);

        Assert.Equal("30d", updated.DefaultTimeRange);
        Assert.Equal(10, updated.SmoothingWindow);
        Assert.Equal(1000, updated.MinCohortSize);
        Assert.True(updated.ExportPdfEnabled);

        var audit = await db.AuditEvents.FirstAsync(a => a.ResourceType == "ProgressPolicy");
        Assert.Equal("admin-9", audit.ActorId);
        Assert.Equal("Updated", audit.Action);
    }

    // ── Regression tests against the old stubs ──────────────────────────

    [Fact]
    public async Task GetProgress_does_not_emit_hardcoded_submission_volume()
    {
        var db = BuildDb();
        await SeedGoalAsync(db, "u-1");
        var svc = new ProgressService(db);

        var payload = await svc.GetProgressAsync("u-1", "90d", default);

        // Old stub was { W1:4, W2:6, W3:5, W4:8, W5:7 }. A learner with no
        // attempts must see zeros, not those magic numbers.
        Assert.Equal(5, payload.SubmissionVolume.Count);
        Assert.All(payload.SubmissionVolume, p => Assert.Equal(0, p.Writing));
        Assert.All(payload.SubmissionVolume, p => Assert.Equal(0, p.Speaking));
    }

    [Fact]
    public void Iso_bucketing_invariant_days_of_week_count_to_same_bucket()
    {
        var monday = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 4, 26, 23, 59, 59, TimeSpan.Zero);
        Assert.Equal(ProgressService.IsoWeekStart(monday), ProgressService.IsoWeekStart(sunday));
    }
}
