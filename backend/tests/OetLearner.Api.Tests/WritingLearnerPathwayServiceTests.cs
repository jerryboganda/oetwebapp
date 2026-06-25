using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests;

public class WritingLearnerPathwayServiceTests
{
    private const string DefaultUser = "learner-1";

    private static (LearnerDbContext Db, WritingLearnerPathwayService Service, FixedClock Clock) Build()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 27, 8, 0, 0, TimeSpan.Zero));
        return (db, new WritingLearnerPathwayService(db, clock, new RulebookLoader()), clock);
    }

    [Fact]
    public async Task SaveOnboarding_CreatesProfileAndPathwayWithoutDuplicatingWritingCoreTables()
    {
        var (db, service, _) = Build();

        var profile = await service.SaveOnboardingAsync(DefaultUser, new WritingStartOnboardingRequest(
            "Medicine",
            "B+",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            5,
            45,
            "uk",
            ["routine_referral", "discharge"]), CancellationToken.None);

        Assert.Equal("foundation", profile.CurrentStage);
        Assert.Equal("medicine", profile.Profession);
        Assert.Equal("B+", profile.TargetBand);
        Assert.Equal("GB", profile.TargetCountry);
        Assert.Contains("LT-RR", profile.LetterTypeFocus);
        Assert.Contains("LT-DG", profile.LetterTypeFocus);
        Assert.Single(await db.LearnerWritingProfiles.ToListAsync());
        Assert.Single(await db.LearnerWritingPathways.ToListAsync());
        Assert.Empty(await db.Attempts.ToListAsync());
        Assert.Empty(await db.Evaluations.ToListAsync());

        await db.DisposeAsync();
    }

    [Fact]
    public async Task TodayPlan_AfterEvaluationTargetsRecentWeakness()
    {
        var (db, service, clock) = Build();
        db.ContentItems.Add(PublishedWritingTask("task-2", "routine_referral"));
        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding(), CancellationToken.None);
        db.Attempts.Add(new Attempt
        {
            Id = "attempt-1",
            UserId = DefaultUser,
            ContentId = "task-2",
            SubtestCode = "writing",
            Context = "diagnostic",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = clock.GetUtcNow().AddDays(-1),
            SubmittedAt = clock.GetUtcNow().AddDays(-1),
            CompletedAt = clock.GetUtcNow().AddDays(-1),
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = "eval-1",
            AttemptId = "attempt-1",
            SubtestCode = "writing",
            State = AsyncState.Completed,
            ScoreRange = "360",
            GradeRange = "B",
            LearnerDisclaimer = "Practice estimate only.",
            ModelExplanationSafe = "Safe explanation.",
            CreatedAt = clock.GetUtcNow().AddDays(-1),
            GeneratedAt = clock.GetUtcNow().AddDays(-1),
        });
        db.WritingRuleViolations.Add(Violation("missing_key_content", clock.GetUtcNow().AddHours(-2)));
        await db.SaveChangesAsync();

        var plan = await service.GetTodayPlanAsync(DefaultUser, CancellationToken.None);

        Assert.Equal(3, plan.Items.Count);
        Assert.Contains(plan.Items, item => item.ItemType == "sentence_drill" && item.FocusSkill == "W3");
        Assert.Contains(plan.Items, item => item.ItemType == "full_letter" && item.ContentId == "task-2");
        Assert.Contains(plan.Items, item => item.ItemType == "canon_review");

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Profile_DoesNotTreatNormalWritingEvaluationAsDiagnostic()
    {
        var (db, service, clock) = Build();
        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding(), CancellationToken.None);
        db.Attempts.Add(new Attempt
        {
            Id = "attempt-normal",
            UserId = DefaultUser,
            ContentId = "task-normal",
            SubtestCode = "writing",
            Context = "exam",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = clock.GetUtcNow().AddDays(-1),
            SubmittedAt = clock.GetUtcNow().AddDays(-1),
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = "eval-normal",
            AttemptId = "attempt-normal",
            SubtestCode = "writing",
            State = AsyncState.Completed,
            ScoreRange = "420",
            GradeRange = "B",
            LearnerDisclaimer = "Practice estimate only.",
            ModelExplanationSafe = "Safe explanation.",
            CreatedAt = clock.GetUtcNow().AddDays(-1),
            GeneratedAt = clock.GetUtcNow().AddDays(-1),
        });
        await db.SaveChangesAsync();

        var profile = await service.GetProfileAsync(DefaultUser, CancellationToken.None);

        Assert.Equal("foundation", profile.CurrentStage);
        Assert.False(profile.DiagnosticCompleted);
        Assert.Null(profile.LastDiagnosticEvaluationId);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task TodayPlan_RegeneratesWhenOnboardingCreatesAStaleDashboardPlan()
    {
        var (db, service, _) = Build();

        var beforeOnboarding = await service.GetTodayPlanAsync(DefaultUser, CancellationToken.None);
        Assert.Equal("onboarding", Assert.Single(beforeOnboarding.Items).ItemType);

        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding(), CancellationToken.None);
        var afterOnboarding = await service.GetTodayPlanAsync(DefaultUser, CancellationToken.None);

        // After onboarding the stale "onboarding" item is replaced by the
        // foundation-stage plan (diagnostic stage removed).
        Assert.NotEmpty(afterOnboarding.Items);
        Assert.DoesNotContain(afterOnboarding.Items, item => item.ItemType == "onboarding");
        Assert.DoesNotContain(await db.WritingDailyPlanItems.ToListAsync(), item => item.ItemType == "onboarding");

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Profile_DoesNotAdvanceFromPendingEvaluation()
    {
        var (db, service, clock) = Build();
        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding(), CancellationToken.None);
        db.Attempts.Add(new Attempt
        {
            Id = "attempt-pending",
            UserId = DefaultUser,
            ContentId = "task-pending",
            SubtestCode = "writing",
            Context = "{}",
            Mode = "exam",
            State = AttemptState.Submitted,
            StartedAt = clock.GetUtcNow().AddDays(-1),
            SubmittedAt = clock.GetUtcNow().AddDays(-1),
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = "eval-pending",
            AttemptId = "attempt-pending",
            SubtestCode = "writing",
            State = AsyncState.Queued,
            ScoreRange = "pending",
            GradeRange = null,
            LearnerDisclaimer = "Practice estimate only.",
            ModelExplanationSafe = "Pending.",
            CreatedAt = clock.GetUtcNow().AddDays(-1),
        });
        await db.SaveChangesAsync();

        var profile = await service.GetProfileAsync(DefaultUser, CancellationToken.None);

        Assert.Equal("foundation", profile.CurrentStage);
        Assert.False(profile.DiagnosticCompleted);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Pathway_UsesCountryAwareWritingPassForMasteryReadiness()
    {
        var (db, service, clock) = Build();
        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding() with { TargetCountry = "US" }, CancellationToken.None);
        for (var index = 0; index < 3; index++)
        {
            var attemptId = $"attempt-us-{index}";
            db.Attempts.Add(new Attempt
            {
                Id = attemptId,
                UserId = DefaultUser,
                ContentId = "task-us",
                SubtestCode = "writing",
                Context = index == 0 ? "diagnostic" : "exam",
                Mode = "exam",
                State = AttemptState.Completed,
                StartedAt = clock.GetUtcNow().AddDays(-index - 1),
                SubmittedAt = clock.GetUtcNow().AddDays(-index - 1),
            });
            db.Evaluations.Add(new Evaluation
            {
                Id = $"eval-us-{index}",
                AttemptId = attemptId,
                SubtestCode = "writing",
                State = AsyncState.Completed,
                ScoreRange = "310-330",
                GradeRange = "C+",
                LearnerDisclaimer = "Practice estimate only.",
                ModelExplanationSafe = "Safe explanation.",
                CreatedAt = clock.GetUtcNow().AddDays(-index - 1),
                GeneratedAt = clock.GetUtcNow().AddDays(-index - 1),
            });
        }
        await db.SaveChangesAsync();

        var profile = await service.GetProfileAsync(DefaultUser, CancellationToken.None);

        Assert.Equal("mastery", profile.CurrentStage);
        Assert.Equal(320, profile.PredictedScore);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Pathway_WeekCountTracksExamDatePlanLength()
    {
        var (db, service, clock) = Build();

        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding() with { ExamDate = clock.GetUtcNow().AddDays(28) }, CancellationToken.None);
        var pathway = await service.GetPathwayAsync(DefaultUser, CancellationToken.None);

        Assert.Equal(4, pathway.TotalWeeks);
        Assert.Equal(4, pathway.Weeks.Count);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Profile_ParsesSingleScaledScoreWithoutAveragingScaleMaximum()
    {
        var (db, service, clock) = Build();
        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding(), CancellationToken.None);
        db.Attempts.Add(new Attempt
        {
            Id = "attempt-single-score",
            UserId = DefaultUser,
            ContentId = "task-single-score",
            SubtestCode = "writing",
            Context = "diagnostic",
            Mode = "exam",
            State = AttemptState.Completed,
            StartedAt = clock.GetUtcNow().AddDays(-1),
            SubmittedAt = clock.GetUtcNow().AddDays(-1),
        });
        db.Evaluations.Add(new Evaluation
        {
            Id = "eval-single-score",
            AttemptId = "attempt-single-score",
            SubtestCode = "writing",
            State = AsyncState.Completed,
            ScoreRange = "350/500",
            GradeRange = "B",
            LearnerDisclaimer = "Practice estimate only.",
            ModelExplanationSafe = "Safe explanation.",
            CreatedAt = clock.GetUtcNow().AddDays(-1),
            GeneratedAt = clock.GetUtcNow().AddDays(-1),
        });
        await db.SaveChangesAsync();

        var profile = await service.GetProfileAsync(DefaultUser, CancellationToken.None);

        Assert.Equal(350, profile.PredictedScore);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task Canon_IncludesOnlyTheLearnersRecentViolationStats()
    {
        var (db, service, clock) = Build();
        db.WritingRuleViolations.AddRange(
            Violation("R13.2", clock.GetUtcNow().AddHours(-4), DefaultUser),
            Violation("R13.2", clock.GetUtcNow().AddHours(-3), DefaultUser),
            Violation("R13.2", clock.GetUtcNow().AddHours(-2), "other-user"));
        await db.SaveChangesAsync();

        var canon = await service.GetCanonAsync(DefaultUser, "urgent", "critical", CancellationToken.None);

        Assert.Contains(canon.Rules, rule => rule.RuleId == "R13.2");
        var stat = Assert.Single(canon.RecentViolations);
        Assert.Equal("R13.2", stat.RuleId);
        Assert.Equal(2, stat.Count);
        Assert.Equal(2, canon.TotalRecentViolations);

        await db.DisposeAsync();
    }

    [Fact]
    public async Task PlanItemMutations_StayScopedToOwningUser()
    {
        var (db, service, _) = Build();
        await service.SaveOnboardingAsync(DefaultUser, DefaultOnboarding(), CancellationToken.None);
        var plan = await service.GetTodayPlanAsync(DefaultUser, CancellationToken.None);
        var itemId = plan.Items[0].Id;

        var ex = await Assert.ThrowsAsync<ApiException>(() => service.CompletePlanItemAsync("other-user", itemId, CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("writing_plan_item_not_found", ex.ErrorCode);

        await service.CompletePlanItemAsync(DefaultUser, itemId, CancellationToken.None);
        var item = await db.WritingDailyPlanItems.SingleAsync(i => i.Id == itemId);
        Assert.Equal("completed", item.Status);
        Assert.NotNull(item.CompletedAt);

        await db.DisposeAsync();
    }

    private static WritingStartOnboardingRequest DefaultOnboarding() => new(
        "medicine",
        "B",
        null,
        5,
        45,
        "GB",
        ["LT-RR", "LT-DG"]);

    private static ContentItem PublishedWritingTask(string id, string letterType) => new()
    {
        Id = id,
        ContentType = "task",
        SubtestCode = "writing",
        ProfessionId = "medicine",
        Title = "Referral writing task",
        Difficulty = "intermediate",
        EstimatedDurationMinutes = 45,
        ScenarioType = letterType,
        PublishedRevisionId = $"{id}-rev-1",
        Status = ContentStatus.Published,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        PublishedAt = DateTimeOffset.UtcNow,
    };

    private static WritingRuleViolation Violation(string ruleId, DateTimeOffset at, string userId = DefaultUser) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        AttemptId = "attempt-1",
        EvaluationId = "eval-1",
        UserId = userId,
        Profession = "medicine",
        LetterType = "routine_referral",
        RuleId = ruleId,
        Severity = "high",
        Source = "rulebook",
        Message = $"{ruleId} fired",
        GeneratedAt = at,
    };

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = start;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}