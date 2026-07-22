using System.Net.Http.Json;
using System.Text.Json;
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

    [Fact]
    public async Task OnboardingState_ExamDateRequired_WhenNoConfirmedGoal()
    {
        var userId = "examdate-onboarding-required";
        await SeedLearnerWithoutRegistrationProfileAsync(userId);

        using var client = CreateDebugClient(userId);
        var response = await client.GetAsync("/v1/learner/onboarding/state");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("examDateRequired").GetBoolean());
    }

    [Fact]
    public async Task OnboardingState_ExamDateNotRequired_WhenGoalConfirmed()
    {
        var userId = "examdate-onboarding-confirmed";
        var registeredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60));
        await SeedLearnerWithRegistrationProfileAsync(userId, registeredDate);

        using var client = CreateDebugClient(userId);
        // Trigger lazy goal creation first (mirrors real traffic: /v1/settings
        // is hit long before onboarding state on a fresh session).
        (await client.GetAsync("/v1/settings")).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/v1/learner/onboarding/state");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("examDateRequired").GetBoolean());
    }

    [Fact]
    public async Task PatchGoals_SetsExamDateSetByUser_WhenTargetExamDateProvided()
    {
        var userId = "examdate-patch-goals";
        await SeedLearnerWithoutRegistrationProfileAsync(userId);

        using var client = CreateDebugClient(userId);
        // Establish the lazy placeholder goal first (TargetExamDateSetByUser=false).
        (await client.GetAsync("/v1/settings")).EnsureSuccessStatusCode();

        var newExamDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(120));
        var patchResponse = await client.PatchAsJsonAsync("/v1/learner/goals/", new { targetExamDate = newExamDate });
        patchResponse.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var goal = await db.Goals.SingleAsync(g => g.UserId == userId);
        Assert.Equal(newExamDate, goal.TargetExamDate);
        Assert.True(goal.TargetExamDateSetByUser);
    }

    [Fact]
    public async Task SpeakingAccess_ReturnsRequiresAiOnlyTrue_WhenExamUnder7Days()
    {
        var userId = "examdate-speaking-access-soon";
        await SeedLearnerWithRegistrationProfileAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));

        using var client = CreateDebugClient(userId);
        (await client.GetAsync("/v1/settings")).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/v1/mocks/speaking-access");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("requiresAiOnly").GetBoolean());
    }

    [Fact]
    public async Task SpeakingAccess_ReturnsRequiresAiOnlyFalse_WhenExam7OrMoreDaysAway()
    {
        var userId = "examdate-speaking-access-later";
        await SeedLearnerWithRegistrationProfileAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)));

        using var client = CreateDebugClient(userId);
        (await client.GetAsync("/v1/settings")).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/v1/mocks/speaking-access");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("requiresAiOnly").GetBoolean());
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
