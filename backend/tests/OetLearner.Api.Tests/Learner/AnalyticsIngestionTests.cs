using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Learner;

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
    public async Task AnalyticsBatch_PersistsEveryEventForTheAuthenticatedUser()
    {
        var response = await _client.PostAsJsonAsync("/v1/analytics/events/batch", new
        {
            events = new object[]
            {
                new { eventName = "batch_probe_one", properties = new Dictionary<string, object?> { ["subtest"] = "writing", ["step"] = 1 } },
                new { eventName = "batch_probe_two", properties = new Dictionary<string, object?> { ["subtest"] = "reading", ["step"] = 2 } },
                new { eventName = "batch_probe_three", properties = (Dictionary<string, object?>?)null },
            }
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var stored = await db.AnalyticsEvents
            .Where(x => x.UserId == "analytics-user-001" && x.EventName.StartsWith("batch_probe_"))
            .ToListAsync();

        Assert.Equal(3, stored.Count);
        Assert.Equal(
            new[] { "batch_probe_one", "batch_probe_three", "batch_probe_two" },
            stored.Select(x => x.EventName).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        using var payload = JsonDocument.Parse(stored.Single(x => x.EventName == "batch_probe_two").PayloadJson);
        Assert.Equal("reading", payload.RootElement.GetProperty("subtest").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("step").GetInt32());
    }

    [Fact]
    public async Task AnalyticsBatch_SkipsInvalidEntries_WithoutLosingTheValidOnes()
    {
        var response = await _client.PostAsJsonAsync("/v1/analytics/events/batch", new
        {
            events = new object?[]
            {
                new { eventName = "batch_skip_valid", properties = new Dictionary<string, object?>() },
                new { eventName = " ", properties = new Dictionary<string, object?>() },
                null,
                new { eventName = new string('x', 80), properties = new Dictionary<string, object?>() },
            }
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var stored = await db.AnalyticsEvents
            .Where(x => x.UserId == "analytics-user-001" && x.EventName.StartsWith("batch_skip_"))
            .ToListAsync();

        Assert.Equal("batch_skip_valid", Assert.Single(stored).EventName);
        Assert.False(await db.AnalyticsEvents.AnyAsync(x => x.UserId == "analytics-user-001" && x.EventName.StartsWith("xxxxxxxx")));
    }

    [Fact]
    public async Task AnalyticsBatch_SanitizesUnsafePropertiesLikeASingleEvent()
    {
        var response = await _client.PostAsJsonAsync("/v1/analytics/events/batch", new
        {
            events = new object[]
            {
                new
                {
                    eventName = "batch_sanitize_probe",
                    properties = new Dictionary<string, object?>
                    {
                        ["email"] = "learner@example.test",
                        ["token"] = "secret-token",
                        ["notes"] = new string('x', 400),
                        ["nested"] = new { unsafeText = "do not store" },
                        ["subtest"] = "reading",
                    },
                },
            }
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var eventRecord = await db.AnalyticsEvents.SingleAsync(x => x.UserId == "analytics-user-001" && x.EventName == "batch_sanitize_probe");

        using var payload = JsonDocument.Parse(eventRecord.PayloadJson);
        Assert.Equal("[redacted]", payload.RootElement.GetProperty("email").GetString());
        Assert.Equal("[redacted]", payload.RootElement.GetProperty("token").GetString());
        Assert.Equal(256, payload.RootElement.GetProperty("notes").GetString()!.Length);
        Assert.False(payload.RootElement.TryGetProperty("nested", out _));
        Assert.Equal("reading", payload.RootElement.GetProperty("subtest").GetString());
    }

    [Fact]
    public async Task AnalyticsBatch_StoresAtMostFiftyEventsPerRequest()
    {
        var events = Enumerable.Range(0, 60)
            .Select(index => new { eventName = $"batch_cap_{index:D2}", properties = new Dictionary<string, object?>() })
            .ToArray();

        var response = await _client.PostAsJsonAsync("/v1/analytics/events/batch", new { events });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var names = await db.AnalyticsEvents
            .Where(x => x.UserId == "analytics-user-001" && x.EventName.StartsWith("batch_cap_"))
            .Select(x => x.EventName)
            .ToListAsync();

        Assert.Equal(50, names.Count);
        Assert.DoesNotContain("batch_cap_50", names);
        Assert.Contains("batch_cap_49", names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"events\":[]}")]
    [InlineData("{\"events\":null}")]
    public async Task AnalyticsBatch_AcknowledgesEmptyOrMalformedBodiesWithoutStoringAnything(string rawBody)
    {
        await using var beforeScope = _factory.Services.CreateAsyncScope();
        var beforeCount = await beforeScope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        using var content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/analytics/events/batch", content);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var afterScope = _factory.Services.CreateAsyncScope();
        var afterCount = await afterScope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");
        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    public async Task AnalyticsBatch_IgnoresOversizedBodies()
    {
        await using var beforeScope = _factory.Services.CreateAsyncScope();
        var beforeCount = await beforeScope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");

        using var content = new StringContent(
            $"{{\"events\":[{{\"eventName\":\"batch_too_big\",\"properties\":{{\"blob\":\"{new string('x', 70_000)}\"}}}}]}}",
            Encoding.UTF8,
            "application/json");
        var response = await _client.PostAsync("/v1/analytics/events/batch", content);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var afterScope = _factory.Services.CreateAsyncScope();
        var afterCount = await afterScope.ServiceProvider.GetRequiredService<LearnerDbContext>()
            .AnalyticsEvents.CountAsync(x => x.UserId == "analytics-user-001");
        Assert.Equal(beforeCount, afterCount);
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
