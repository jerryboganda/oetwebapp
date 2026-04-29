using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class SocialEndpointsTests
{
    [Fact]
    public async Task BookTutoringSession_IgnoresClientPriceAndStoresSessionRate()
    {
        using var factory = new TestWebApplicationFactory();
        await SeedCompletedOnboardingRatesAsync(factory, "expert-001", hourlyRateMinorUnits: 12_000, sessionRateMinorUnits: 4_500);
        using var client = CreateLearnerClient(factory);

        var lowPriceSessionId = await BookTutoringSessionAsync(client, clientPrice: 0.01m);
        var highPriceSessionId = await BookTutoringSessionAsync(client, clientPrice: 999_999.99m);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storedPrices = await db.TutoringSessions
            .Where(session => session.Id == lowPriceSessionId || session.Id == highPriceSessionId)
            .OrderBy(session => session.Id)
            .Select(session => session.Price)
            .ToArrayAsync();

        Assert.Equal([45m, 45m], storedPrices);
    }

    [Fact]
    public async Task BookTutoringSession_UsesHourlyRateWhenSessionRateIsMissing()
    {
        using var factory = new TestWebApplicationFactory();
        await SeedCompletedOnboardingRatesAsync(factory, "expert-001", hourlyRateMinorUnits: 12_000, sessionRateMinorUnits: 0);
        using var client = CreateLearnerClient(factory);

        var sessionId = await BookTutoringSessionAsync(client, durationMinutes: 45, clientPrice: 1m);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var storedPrice = await db.TutoringSessions
            .Where(session => session.Id == sessionId)
            .Select(session => session.Price)
            .SingleAsync();

        Assert.Equal(90m, storedPrice);
    }

    [Fact]
    public async Task BookTutoringSession_RejectsExpertWithoutCompletedRates()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateLearnerClient(factory);

        var response = await client.PostAsJsonAsync("/v1/tutoring/sessions", CreateBookRequest(clientPrice: 25m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("expert_rates_unavailable", await ReadErrorCodeAsync(response));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.False(await db.TutoringSessions.AnyAsync());
    }

    private static HttpClient CreateLearnerClient(TestWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", "mock-user-001");
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", "learner@oet-prep.dev");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Test Learner");
        return client;
    }

    private static async Task<string> BookTutoringSessionAsync(HttpClient client, int durationMinutes = 60, decimal clientPrice = 25m)
    {
        var response = await client.PostAsJsonAsync("/v1/tutoring/sessions", CreateBookRequest(durationMinutes, clientPrice));
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);

        using var json = JsonDocument.Parse(payload);
        return json.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Expected session id in booking response.");
    }

    private static object CreateBookRequest(int durationMinutes = 60, decimal clientPrice = 25m) => new
    {
        expertUserId = "expert-001",
        examTypeCode = "oet",
        subtestFocus = "writing",
        scheduledAt = DateTimeOffset.UtcNow.AddDays(3),
        durationMinutes,
        learnerNotes = "Please focus on writing structure.",
        price = clientPrice
    };

    private static async Task SeedCompletedOnboardingRatesAsync(
        TestWebApplicationFactory factory,
        string expertUserId,
        long hourlyRateMinorUnits,
        long sessionRateMinorUnits)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var progress = await db.ExpertOnboardingProgresses.FirstOrDefaultAsync(row => row.ExpertUserId == expertUserId);
        if (progress is null)
        {
            progress = new ExpertOnboardingProgress { ExpertUserId = expertUserId };
            db.ExpertOnboardingProgresses.Add(progress);
        }

        progress.ProfileJson = JsonSupport.Serialize(new ExpertOnboardingProfileDto("Tutor Reviewer", "Experienced OET writing tutor.", null));
        progress.QualificationsJson = JsonSupport.Serialize(new ExpertOnboardingQualificationsDto("Registered educator", "OET assessor training", 8));
        progress.RatesJson = JsonSupport.Serialize(new ExpertOnboardingRatesDto(hourlyRateMinorUnits, sessionRateMinorUnits, "GBP"));
        progress.CompletedStepsJson = JsonSupport.Serialize(new[] { "profile", "qualifications", "rates", "review" });
        progress.IsComplete = true;
        progress.CompletedAt = now;
        progress.UpdatedAt = now;
        await db.SaveChangesAsync();
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : null;
    }
}
