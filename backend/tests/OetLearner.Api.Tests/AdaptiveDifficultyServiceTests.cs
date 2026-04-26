using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class AdaptiveDifficultyServiceTests
{
    private static (LearnerDbContext db, AdaptiveDifficultyService svc) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        return (db, new AdaptiveDifficultyService(db));
    }

    private static ContentItem MakeContent(
        string id,
        string subtest,
        ContentStatus status = ContentStatus.Published,
        int difficulty = 1500,
        string examType = "oet")
        => new()
        {
            Id = id,
            ContentType = "passage",
            SubtestCode = subtest,
            Title = $"Item {id}",
            Difficulty = "intermediate",
            EstimatedDurationMinutes = 10,
            ModeSupportJson = "[]",
            CriteriaFocusJson = "[]",
            PublishedRevisionId = $"rev-{id}",
            Status = status,
            DetailJson = "{}",
            ModelAnswerJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExamFamilyCode = "oet",
            ExamTypeCode = examType,
            DifficultyRating = difficulty,
            SourceType = "manual",
            QaStatus = "approved",
        };

    // ── UpdateSkillRatingAsync ────────────────────────────────────────────

    [Fact]
    public async Task UpdateSkillRatingAsync_creates_profile_on_first_call_with_default_rating()
    {
        var (db, svc) = Build();

        await svc.UpdateSkillRatingAsync("u1", "oet", "reading", criterionCode: null, correct: true, default);

        var profile = await db.LearnerSkillProfiles.SingleAsync();
        Assert.Equal("u1", profile.UserId);
        Assert.Equal("oet", profile.ExamTypeCode);
        Assert.Equal("reading", profile.SubtestCode);
        Assert.Equal(string.Empty, profile.CriterionCode);
        Assert.Equal(1, profile.EvidenceCount);
        // Correct outcome with expected probability 0.5 (rating == default) → +K/2 = +16.
        Assert.InRange(profile.CurrentRating, 1515.99, 1516.01);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_decreases_rating_on_incorrect_answer()
    {
        var (db, svc) = Build();

        await svc.UpdateSkillRatingAsync("u1", "oet", "listening", null, correct: false, default);

        var profile = await db.LearnerSkillProfiles.SingleAsync();
        Assert.InRange(profile.CurrentRating, 1483.99, 1484.01); // -16 from 1500
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_clamps_rating_to_minimum_floor()
    {
        var (db, svc) = Build();
        db.LearnerSkillProfiles.Add(new LearnerSkillProfile
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ExamTypeCode = "oet",
            SubtestCode = "reading",
            CriterionCode = string.Empty,
            CurrentRating = 110,
            ConfidenceLevel = 0,
            EvidenceCount = 0,
            RecentScoresJson = "[]",
            LastUpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // A wrong answer at extremely low rating should not push below 100.
        await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, correct: false, default);

        var profile = await db.LearnerSkillProfiles.SingleAsync();
        Assert.True(profile.CurrentRating >= 100);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_clamps_rating_to_maximum_ceiling()
    {
        var (db, svc) = Build();
        db.LearnerSkillProfiles.Add(new LearnerSkillProfile
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            ExamTypeCode = "oet",
            SubtestCode = "reading",
            CriterionCode = string.Empty,
            CurrentRating = 2990,
            ConfidenceLevel = 0,
            EvidenceCount = 0,
            RecentScoresJson = "[]",
            LastUpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, correct: true, default);

        var profile = await db.LearnerSkillProfiles.SingleAsync();
        Assert.True(profile.CurrentRating <= 3000);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_increments_evidence_and_confidence()
    {
        var (db, svc) = Build();

        for (var i = 0; i < 5; i++)
        {
            await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, correct: true, default);
        }

        var profile = await db.LearnerSkillProfiles.SingleAsync();
        Assert.Equal(5, profile.EvidenceCount);
        Assert.Equal(25, profile.ConfidenceLevel); // 5 * 5
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_caps_confidence_at_100()
    {
        var (db, svc) = Build();

        for (var i = 0; i < 25; i++)
        {
            await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, correct: true, default);
        }

        var profile = await db.LearnerSkillProfiles.SingleAsync();
        Assert.Equal(100, profile.ConfidenceLevel);
        Assert.Equal(25, profile.EvidenceCount);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_keeps_only_last_20_recent_scores()
    {
        var (db, svc) = Build();

        // Alternate correct/incorrect 25 times.
        for (var i = 0; i < 25; i++)
        {
            await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, correct: i % 2 == 0, default);
        }

        var profile = await db.LearnerSkillProfiles.SingleAsync();
        var scores = JsonSerializer.Deserialize<List<int>>(profile.RecentScoresJson)!;
        Assert.Equal(20, scores.Count);
        // Last value should reflect the most recent push (i=24, correct=true → 1).
        Assert.Equal(1, scores[^1]);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_isolates_profiles_by_criterion_code()
    {
        var (db, svc) = Build();

        await svc.UpdateSkillRatingAsync("u1", "oet", "writing", "task_response", correct: true, default);
        await svc.UpdateSkillRatingAsync("u1", "oet", "writing", "linguistic_resources", correct: false, default);
        await svc.UpdateSkillRatingAsync("u1", "oet", "writing", criterionCode: null, correct: true, default);

        var profiles = await db.LearnerSkillProfiles.ToListAsync();
        Assert.Equal(3, profiles.Count);
        Assert.Contains(profiles, p => p.CriterionCode == "task_response");
        Assert.Contains(profiles, p => p.CriterionCode == "linguistic_resources");
        Assert.Contains(profiles, p => p.CriterionCode == string.Empty); // null → ""
        await db.DisposeAsync();
    }

    // ── GetSkillProfileAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetSkillProfileAsync_returns_only_requested_user_profiles()
    {
        var (db, svc) = Build();
        await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, true, default);
        await svc.UpdateSkillRatingAsync("u2", "oet", "reading", null, true, default);

        var result = (System.Collections.IEnumerable)(await svc.GetSkillProfileAsync("u1", null, default));
        var count = 0;
        foreach (var _ in result) count++;
        Assert.Equal(1, count);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetSkillProfileAsync_filters_by_exam_type_when_supplied()
    {
        var (db, svc) = Build();
        await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, true, default);
        await svc.UpdateSkillRatingAsync("u1", "ielts", "reading", null, true, default);

        var result = (System.Collections.IEnumerable)(await svc.GetSkillProfileAsync("u1", "oet", default));
        var count = 0;
        foreach (var _ in result) count++;
        Assert.Equal(1, count);
        await db.DisposeAsync();
    }

    // ── GetAdaptiveContentAsync ───────────────────────────────────────────

    [Fact]
    public async Task GetAdaptiveContentAsync_prefers_items_within_tolerance_window()
    {
        var (db, svc) = Build();
        await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, true, default);

        // After one correct answer rating ≈ 1516, tolerance ±200, so 1316..1716 is in-band.
        db.ContentItems.Add(MakeContent("c-near", "reading", difficulty: 1500));
        db.ContentItems.Add(MakeContent("c-far-low", "reading", difficulty: 800));
        db.ContentItems.Add(MakeContent("c-far-high", "reading", difficulty: 2500));
        db.ContentItems.Add(MakeContent("c-other-subtest", "listening", difficulty: 1500));
        db.ContentItems.Add(MakeContent("c-not-published", "reading", difficulty: 1500, status: ContentStatus.Draft));
        await db.SaveChangesAsync();

        var raw = await svc.GetAdaptiveContentAsync("u1", "oet", "reading", count: 5, default);
        var ids = ExtractIds(raw);

        Assert.Contains("c-near", ids);
        Assert.DoesNotContain("c-other-subtest", ids);
        Assert.DoesNotContain("c-not-published", ids);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetAdaptiveContentAsync_falls_back_when_in_band_pool_is_short()
    {
        var (db, svc) = Build();
        await svc.UpdateSkillRatingAsync("u1", "oet", "reading", null, true, default);

        // One in-band, two out-of-band — fallback should fill the requested count.
        db.ContentItems.Add(MakeContent("c-in", "reading", difficulty: 1500));
        db.ContentItems.Add(MakeContent("c-out-1", "reading", difficulty: 800));
        db.ContentItems.Add(MakeContent("c-out-2", "reading", difficulty: 2500));
        await db.SaveChangesAsync();

        var raw = await svc.GetAdaptiveContentAsync("u1", "oet", "reading", count: 3, default);
        var ids = ExtractIds(raw);

        Assert.Equal(3, ids.Count);
        Assert.Contains("c-in", ids);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task GetAdaptiveContentAsync_uses_default_rating_when_profile_missing()
    {
        var (db, svc) = Build();
        db.ContentItems.Add(MakeContent("c1", "speaking", difficulty: 1500));
        await db.SaveChangesAsync();

        var raw = await svc.GetAdaptiveContentAsync("nobody", "oet", "speaking", count: 1, default);
        var ids = ExtractIds(raw);
        Assert.Single(ids);
        await db.DisposeAsync();
    }

    // ── UpdateContentDifficultyAsync ──────────────────────────────────────

    [Fact]
    public async Task UpdateContentDifficultyAsync_lowers_difficulty_when_learner_succeeds()
    {
        var (db, svc) = Build();
        db.ContentItems.Add(MakeContent("c1", "reading", difficulty: 1500));
        await db.SaveChangesAsync();

        await svc.UpdateContentDifficultyAsync("c1", learnerSucceeded: true, learnerRating: 1500, default);

        var item = await db.ContentItems.FindAsync("c1");
        Assert.NotNull(item);
        Assert.True(item!.DifficultyRating < 1500);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateContentDifficultyAsync_raises_difficulty_when_learner_fails()
    {
        var (db, svc) = Build();
        db.ContentItems.Add(MakeContent("c1", "reading", difficulty: 1500));
        await db.SaveChangesAsync();

        await svc.UpdateContentDifficultyAsync("c1", learnerSucceeded: false, learnerRating: 1500, default);

        var item = await db.ContentItems.FindAsync("c1");
        Assert.NotNull(item);
        Assert.True(item!.DifficultyRating > 1500);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task UpdateContentDifficultyAsync_silently_returns_when_content_missing()
    {
        var (_, svc) = Build();
        // Should not throw.
        await svc.UpdateContentDifficultyAsync("nope", learnerSucceeded: true, learnerRating: 1500, default);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static List<string> ExtractIds(object raw)
    {
        var list = new List<string>();
        var enumerable = (System.Collections.IEnumerable)raw;
        foreach (var item in enumerable)
        {
            var prop = item!.GetType().GetProperty("id");
            Assert.NotNull(prop);
            var value = (string)prop!.GetValue(item)!;
            list.Add(value);
        }
        return list;
    }
}
