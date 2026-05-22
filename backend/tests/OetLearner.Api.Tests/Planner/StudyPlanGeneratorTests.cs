using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Planner;
using Xunit;

namespace OetLearner.Api.Tests.Planner;

public class StudyPlanGeneratorTests
{
    private static LearnerDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase($"plan-test-{Guid.NewGuid():N}")
            .Options;
        return new LearnerDbContext(options);
    }

    private static StudyPlanGenerator BuildGenerator(LearnerDbContext db, string tier = StudyPlanEntitlementResolver.FreeTier)
    {
        var resolver = new StubEntitlementResolver(tier);
        var contentPicker = new ContentPicker(db);
        var reviewItemInjector = new ReviewItemInjector(db);
        var templateSelector = new StudyPlanTemplateSelector(db);
        return new StudyPlanGenerator(db, templateSelector, contentPicker, reviewItemInjector, resolver, NullLogger<StudyPlanGenerator>.Instance);
    }

    private static async Task SeedLearnerAsync(LearnerDbContext db, string userId, DateOnly? examDate, int hoursPerWeek = 7, string? professionId = null, params string[] weakSubtests)
    {
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = "Test Learner",
            Email = $"{userId}@example.test",
            ActiveProfessionId = professionId,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        });
        db.Goals.Add(new LearnerGoal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProfessionId = professionId ?? "nurse",
            TargetExamDate = examDate,
            TargetWritingScore = 350,
            TargetSpeakingScore = 350,
            TargetReadingScore = 350,
            TargetListeningScore = 350,
            StudyHoursPerWeek = hoursPerWeek,
            WeakSubtestsJson = System.Text.Json.JsonSerializer.Serialize(weakSubtests),
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedFreeTemplateAsync(LearnerDbContext db, int minWeeks = 1, int maxWeeks = 99)
    {
        var seeder = new StudyPlanTemplateSeeder(db, NullLogger<StudyPlanTemplateSeeder>.Instance);
        await seeder.SeedIfEmptyAsync(CancellationToken.None);

        // Override default seeded weeks-window so test inputs reliably match.
        var free = await db.StudyPlanTemplates.FirstAsync(t => t.Slug == "free-8wk-standard");
        free.MinWeeks = minWeeks;
        free.MaxWeeks = maxWeeks;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Generate_FreeTier_With_8WeekExam_ProducesPopulatedPlan()
    {
        using var db = NewDb();
        var userId = "user-1";
        await SeedLearnerAsync(db, userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(56)), hoursPerWeek: 7);
        await SeedFreeTemplateAsync(db);

        var generator = BuildGenerator(db, StudyPlanEntitlementResolver.FreeTier);
        var result = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.OnboardingComplete, CancellationToken.None);

        Assert.False(result.SkippedBecauseUnchanged);
        Assert.NotNull(result.PlanId);
        Assert.True(result.ItemsCreated > 0, $"Expected items to be created, got {result.ItemsCreated}.");
        Assert.Equal("tmpl-free-8wk-standard", result.TemplateId);

        var plan = await db.StudyPlans.FirstAsync(p => p.Id == result.PlanId);
        Assert.True(plan.IsActive);
        Assert.Equal(StudyPlanEntitlementResolver.FreeTier, plan.EntitlementTierAtGeneration);
        Assert.True(plan.MinutesPerDayBudget >= 15);
        Assert.NotEmpty(plan.GenerationInputsHash ?? string.Empty);

        var items = await db.StudyPlanItems.Where(i => i.StudyPlanId == result.PlanId).ToListAsync();
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.ContentRoute), $"Item {i.Id} has no ContentRoute"));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Rationale)));
    }

    [Fact]
    public async Task Generate_SameInputs_Twice_SecondCallSkips()
    {
        using var db = NewDb();
        var userId = "user-determinism";
        await SeedLearnerAsync(db, userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(56)));
        await SeedFreeTemplateAsync(db);

        var generator = BuildGenerator(db);
        var first = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.OnboardingComplete, CancellationToken.None);
        Assert.False(first.SkippedBecauseUnchanged);

        var second = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.WeeklyCadence, CancellationToken.None);
        Assert.True(second.SkippedBecauseUnchanged, "Second generation with same inputs should skip.");
        Assert.Equal(first.InputsHash, second.InputsHash);
    }

    [Fact]
    public async Task Generate_Manual_Trigger_Always_Regenerates_Even_When_Unchanged()
    {
        using var db = NewDb();
        var userId = "user-manual";
        await SeedLearnerAsync(db, userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(56)));
        await SeedFreeTemplateAsync(db);

        var generator = BuildGenerator(db);
        var first = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.OnboardingComplete, CancellationToken.None);
        var second = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.Manual, CancellationToken.None);

        Assert.False(second.SkippedBecauseUnchanged);
        Assert.True(second.Version > first.Version, "Manual trigger should produce a new version.");
    }

    [Fact]
    public async Task Generate_NoTemplates_FallsBackToEmptyPlan_NeverThrows()
    {
        using var db = NewDb();
        var userId = "user-no-templates";
        await SeedLearnerAsync(db, userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(56)));

        var generator = BuildGenerator(db);
        var result = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.OnboardingComplete, CancellationToken.None);

        Assert.NotNull(result.PlanId);
        Assert.Equal(0, result.ItemsCreated);
        var plan = await db.StudyPlans.FirstAsync(p => p.Id == result.PlanId);
        Assert.True(plan.IsActive);
    }

    [Fact]
    public async Task Generate_PreservesCompletedItems_AcrossRegeneration()
    {
        using var db = NewDb();
        var userId = "user-preserve";
        await SeedLearnerAsync(db, userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(56)));
        await SeedFreeTemplateAsync(db);

        var generator = BuildGenerator(db);
        var first = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.OnboardingComplete, CancellationToken.None);
        Assert.True(first.ItemsCreated > 0);

        var anItem = await db.StudyPlanItems.FirstAsync(i => i.StudyPlanId == first.PlanId);
        anItem.Status = StudyPlanItemStatus.Completed;
        anItem.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var preservedId = anItem.Id;

        // Manual regen — forces churn but should keep the completed item.
        var second = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.Manual, CancellationToken.None);
        Assert.False(second.SkippedBecauseUnchanged);
        Assert.True(second.ItemsPreservedFromPrior >= 1, $"Expected preserved items, got {second.ItemsPreservedFromPrior}.");

        var stillThere = await db.StudyPlanItems.FirstOrDefaultAsync(i => i.Id == preservedId);
        Assert.NotNull(stillThere);
        Assert.Equal(StudyPlanItemStatus.Completed, stillThere!.Status);
    }

    [Fact]
    public async Task Generate_WeakSubtest_RaisesWeightForThatSubtest()
    {
        using var db = NewDb();
        var userId = "user-weak";
        await SeedLearnerAsync(db, userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(56)),
            weakSubtests: new[] { "writing" });
        await SeedFreeTemplateAsync(db);

        var generator = BuildGenerator(db);
        var result = await generator.GenerateAsync(userId, StudyPlanGenerationTrigger.OnboardingComplete, CancellationToken.None);
        Assert.False(result.SkippedBecauseUnchanged);

        var plan = await db.StudyPlans.FirstAsync(p => p.Id == result.PlanId);
        var weights = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(plan.SubtestWeightsJson)!;
        Assert.True(weights["writing"] >= weights["reading"], "Writing weight should be >= reading when writing is flagged weak.");
        Assert.True(weights["writing"] >= weights["listening"], "Writing weight should be >= listening when writing is flagged weak.");
    }

    private sealed class StubEntitlementResolver(string tier) : IStudyPlanEntitlementResolver
    {
        public Task<string> ResolveTierAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult(tier);
    }
}
