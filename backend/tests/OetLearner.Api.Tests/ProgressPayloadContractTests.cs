using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Progress;

namespace OetLearner.Api.Tests;

/// <summary>
/// Pins the shape of the Progress v2 payload so the frontend mapper can
/// rely on exact property names. Adding new fields is fine; renaming or
/// removing one is a breaking change for clients and MUST be deliberate.
/// </summary>
public class ProgressPayloadContractTests
{
    private static LearnerDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LearnerDbContext(options);
    }

    [Fact]
    public async Task Payload_top_level_shape_is_stable()
    {
        var db = BuildDb();
        db.Users.Add(new LearnerUser { Id = "u", DisplayName = "T", Email = "t@t.test", CreatedAt = DateTimeOffset.UtcNow });
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(), UserId = "u", ProfessionId = "medicine", TargetCountry = "GB",
            TargetWritingScore = 350, StudyHoursPerWeek = 10,
            WeakSubtestsJson = "[]", DraftStateJson = "{}",
            ExamFamilyCode = "oet", ExamTypeCode = "oet",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new ProgressService(db);
        var payload = await svc.GetProgressAsync("u", "90d", default);

        // Every top-level block the frontend contract expects must be present.
        Assert.NotNull(payload.Meta);
        Assert.NotNull(payload.Subtests);
        Assert.NotNull(payload.Trend);
        Assert.NotNull(payload.CriterionTrend);
        Assert.NotNull(payload.Completion);
        Assert.NotNull(payload.SubmissionVolume);
        Assert.NotNull(payload.ReviewUsage);
        Assert.NotNull(payload.Goals);
        // Comparative may be null for learners with no cohort; this is contractual.
        Assert.NotNull(payload.Totals);
        Assert.NotNull(payload.Freshness);

        // Meta fields the frontend consumes for axis config + threshold lines.
        Assert.Equal(0, payload.Meta.ScoreAxisMin);
        Assert.Equal(500, payload.Meta.ScoreAxisMax);
        Assert.Equal(350, payload.Meta.GradeBThreshold);
        Assert.Equal("oet", payload.Meta.ExamFamilyCode);
        Assert.Equal("90d", payload.Meta.Range);
        Assert.NotNull(payload.Meta.ShowScoreGuaranteeStrip);
        Assert.NotNull(payload.Meta.ShowCriterionConfidenceBand);
        Assert.True(payload.Meta.MinEvaluationsForTrend > 0);

        // Every subtest gets an entry.
        Assert.Equal(4, payload.Subtests.Count);
        Assert.Contains(payload.Subtests, s => s.SubtestCode == "writing");
        Assert.Contains(payload.Subtests, s => s.SubtestCode == "speaking");
        Assert.Contains(payload.Subtests, s => s.SubtestCode == "reading");
        Assert.Contains(payload.Subtests, s => s.SubtestCode == "listening");

        // Goals block must include days-to-exam field (may be null).
        _ = payload.Goals.DaysToExam;
        _ = payload.Goals.TargetExamDate;

        // Freshness carries the ETag used for 304 responses.
        Assert.NotNull(payload.Freshness.ETag);
        Assert.StartsWith("W/\"", payload.Freshness.ETag);
    }

    [Fact]
    public async Task Writing_threshold_reason_is_country_required_when_no_country()
    {
        var db = BuildDb();
        db.Users.Add(new LearnerUser { Id = "u", DisplayName = "T", Email = "t@t.test", CreatedAt = DateTimeOffset.UtcNow });
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(), UserId = "u", ProfessionId = "medicine", TargetCountry = null,
            StudyHoursPerWeek = 10, WeakSubtestsJson = "[]", DraftStateJson = "{}",
            ExamFamilyCode = "oet", ExamTypeCode = "oet",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new ProgressService(db);

        var payload = await svc.GetProgressAsync("u", "90d", default);

        Assert.Null(payload.Meta.WritingThreshold);
        Assert.Equal("country_required", payload.Meta.WritingThresholdReason);
        // The matching subtest summary must also surface the same reason code.
        var writing = payload.Subtests.Single(s => s.SubtestCode == "writing");
        Assert.Equal("country_required", writing.ThresholdReason);
    }

    [Fact]
    public async Task Subtests_other_than_writing_always_use_grade_b_threshold()
    {
        var db = BuildDb();
        db.Users.Add(new LearnerUser { Id = "u", DisplayName = "T", Email = "t@t.test", CreatedAt = DateTimeOffset.UtcNow });
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(), UserId = "u", ProfessionId = "medicine", TargetCountry = "US",
            StudyHoursPerWeek = 10, WeakSubtestsJson = "[]", DraftStateJson = "{}",
            ExamFamilyCode = "oet", ExamTypeCode = "oet",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var svc = new ProgressService(db);

        var payload = await svc.GetProgressAsync("u", "90d", default);

        // Writing for US is 300/C+.
        Assert.Equal(300, payload.Meta.WritingThreshold);
        // But listening/reading/speaking remain 350 — threshold does NOT vary by country.
        Assert.Equal(350, payload.Subtests.Single(s => s.SubtestCode == "listening").ThresholdScaled);
        Assert.Equal(350, payload.Subtests.Single(s => s.SubtestCode == "reading").ThresholdScaled);
        Assert.Equal(350, payload.Subtests.Single(s => s.SubtestCode == "speaking").ThresholdScaled);
    }
}
