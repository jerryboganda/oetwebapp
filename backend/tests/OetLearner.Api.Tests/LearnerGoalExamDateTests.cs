using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Mandatory exam-date plan (2026-07-22), Task A2 — the lazy `LearnerGoal`
/// creation path (<c>LearnerService.CreateDefaultGoal</c> and its callers)
/// must honor a real registered `LearnerRegistrationProfile.TargetExamDate`
/// instead of always stamping the "+3 months" placeholder.
/// </summary>
public class LearnerGoalExamDateTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LearnerGoalExamDateTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LazyGoalCreation_UsesRegisteredExamDate_WhenPresent()
    {
        var userId = "examdate-registered-user";
        var registeredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90));
        await SeedLearnerWithRegistrationProfileAsync(userId, registeredDate);

        using var client = CreateDebugClient(userId);
        var response = await client.GetAsync("/v1/settings");
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var goal = await db.Goals.SingleAsync(g => g.UserId == userId);
        Assert.Equal(registeredDate, goal.TargetExamDate);
        Assert.True(goal.TargetExamDateSetByUser);
    }

    [Fact]
    public async Task LazyGoalCreation_FallsBackToPlaceholder_WhenNoRegistrationProfile()
    {
        var userId = "examdate-unregistered-user";
        await SeedLearnerWithoutRegistrationProfileAsync(userId);

        using var client = CreateDebugClient(userId);
        var response = await client.GetAsync("/v1/settings");
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var goal = await db.Goals.SingleAsync(g => g.UserId == userId);
        Assert.False(goal.TargetExamDateSetByUser);
    }

    private async Task SeedLearnerWithRegistrationProfileAsync(string userId, DateOnly targetExamDate)
    {
        await _factory.EnsureCatalogSeededAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            Timezone = "UTC",
            Locale = "en-AU",
            ActiveProfessionId = "medicine",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active"
        });
        db.LearnerRegistrationProfiles.Add(new LearnerRegistrationProfile
        {
            Id = $"signup_{Guid.NewGuid():N}",
            ApplicationUserAccountId = $"auth_{Guid.NewGuid():N}",
            LearnerUserId = userId,
            FirstName = "Test",
            LastName = "User",
            ExamTypeId = "oet",
            ProfessionId = "medicine",
            SessionId = string.Empty,
            CountryTarget = "Australia",
            TargetExamDate = targetExamDate,
            MobileNumber = "+610000000",
            AgreeToTerms = true,
            AgreeToPrivacy = true,
            MarketingOptIn = false,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedLearnerWithoutRegistrationProfileAsync(string userId)
    {
        await _factory.EnsureCatalogSeededAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new LearnerUser
        {
            Id = userId,
            DisplayName = userId,
            Email = $"{userId}@example.test",
            Timezone = "UTC",
            Locale = "en-AU",
            ActiveProfessionId = "medicine",
            CreatedAt = now,
            LastActiveAt = now,
            AccountStatus = "active"
        });
        await db.SaveChangesAsync();
    }

    private HttpClient CreateDebugClient(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }
}
