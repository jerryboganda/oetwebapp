using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class AnalyticsIngestionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public AnalyticsIngestionTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Debug-UserId", "analytics-user-001");
        _client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        _client.DefaultRequestHeaders.Add("X-Debug-Email", "analytics-user-001@example.test");
        _client.DefaultRequestHeaders.Add("X-Debug-Name", "Analytics User");
    }

    [Fact]
    public async Task AnalyticsEvents_ArePersistedForAuthenticatedUsers()
    {
        var response = await _client.PostAsJsonAsync("/v1/analytics/events", new
        {
            eventName = "task_started",
            properties = new Dictionary<string, object?>
            {
                ["subtest"] = "writing",
                ["taskId"] = "wt-001",
                ["deviceType"] = "desktop"
            }
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var eventRecord = await db.AnalyticsEvents.SingleAsync(x => x.UserId == "analytics-user-001" && x.EventName == "task_started");

        using var payload = JsonDocument.Parse(eventRecord.PayloadJson);
        Assert.Equal("writing", payload.RootElement.GetProperty("subtest").GetString());
        Assert.Equal("wt-001", payload.RootElement.GetProperty("taskId").GetString());
    }

    [Fact]
    public async Task AnalyticsEvents_RejectBlankEventNames()
    {
        var response = await _client.PostAsJsonAsync("/v1/analytics/events", new
        {
            eventName = " ",
            properties = new Dictionary<string, object?>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnalyticsEvents_IgnoreEmptyBodies()
    {
        await using var beforeScope = _factory.Services.CreateAsyncScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var beforeCount = await beforeDb.AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        var response = await _client.PostAsync("/v1/analytics/events", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var afterScope = _factory.Services.CreateAsyncScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var afterCount = await afterDb.AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    public async Task AnalyticsEvents_IgnoreMalformedBodies()
    {
        await using var beforeScope = _factory.Services.CreateAsyncScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var beforeCount = await beforeDb.AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        using var content = new StringContent("{", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/analytics/events", content);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var afterScope = _factory.Services.CreateAsyncScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var afterCount = await afterDb.AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    public async Task AnalyticsEvents_SanitizeUnsafePropertiesBeforePersisting()
    {
        var longValue = new string('x', 400);
        var response = await _client.PostAsJsonAsync("/v1/analytics/events", new
        {
            eventName = "task_completed",
            properties = new Dictionary<string, object?>
            {
                ["email"] = "learner@example.test",
                ["token"] = "secret-token",
                ["notes"] = longValue,
                ["nested"] = new { unsafeText = "do not store" },
                ["attempts"] = new[] { 1, 2, 3 },
                ["subtest"] = "reading"
            }
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var eventRecord = await db.AnalyticsEvents.SingleAsync(x => x.UserId == "analytics-user-001" && x.EventName == "task_completed");

        using var payload = JsonDocument.Parse(eventRecord.PayloadJson);
        Assert.Equal("[redacted]", payload.RootElement.GetProperty("email").GetString());
        Assert.Equal("[redacted]", payload.RootElement.GetProperty("token").GetString());
        Assert.Equal(256, payload.RootElement.GetProperty("notes").GetString()!.Length);
        Assert.False(payload.RootElement.TryGetProperty("nested", out _));
        Assert.Equal(3, payload.RootElement.GetProperty("attempts").GetArrayLength());
        Assert.Equal("reading", payload.RootElement.GetProperty("subtest").GetString());
    }

    [Fact]
    public async Task AnalyticsEvents_IgnoreOversizedBodies()
    {
        await using var beforeScope = _factory.Services.CreateAsyncScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var beforeCount = await beforeDb.AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        using var content = new StringContent($"{{\"eventName\":\"too_big\",\"properties\":{{\"blob\":\"{new string('x', 20_000)}\"}}}}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/analytics/events", content);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var afterScope = _factory.Services.CreateAsyncScope();
        var afterDb = afterScope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var afterCount = await afterDb.AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        Assert.Equal(beforeCount, afterCount);
    }
}
