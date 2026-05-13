using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

public class ListeningV2AdvanceEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ListeningV2AdvanceEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Advance_returns_confirm_payload_on_first_strict_mode_request_and_applies_echoed_token()
    {
        var userId = $"listener-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedStrictAttemptAsync(userId, attemptId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var readinessResponse = await client.PostAsJsonAsync(
            $"/v1/listening/v2/attempts/{attemptId}/tech-readiness",
            new { audioOk = true, durationMs = 1500 });
        readinessResponse.EnsureSuccessStatusCode();

        var firstResponse = await client.PostAsJsonAsync(
            $"/v1/listening/v2/attempts/{attemptId}/advance",
            new { toState = ListeningFsmTransitions.A1Preview, confirmToken = (string?)null });

        Assert.Equal((HttpStatusCode)412, firstResponse.StatusCode);
        var confirm = await firstResponse.Content.ReadFromJsonAsync<AdvanceResultDto>(JsonSupport.Options);
        Assert.NotNull(confirm);
        Assert.Equal("confirm-required", confirm.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(confirm.ConfirmToken));
        Assert.Equal(30000, confirm.ConfirmTokenTtlMs);
        Assert.Null(confirm.State);

        var secondResponse = await client.PostAsJsonAsync(
            $"/v1/listening/v2/attempts/{attemptId}/advance",
            new { toState = ListeningFsmTransitions.A1Preview, confirmToken = confirm.ConfirmToken });

        secondResponse.EnsureSuccessStatusCode();
        var applied = await secondResponse.Content.ReadFromJsonAsync<AdvanceResultDto>(JsonSupport.Options);
        Assert.NotNull(applied);
        Assert.Equal("applied", applied.Outcome);
        Assert.Equal(ListeningFsmTransitions.A1Preview, applied.State?.State);
        Assert.Contains(ListeningFsmTransitions.Intro, applied.State?.Locks ?? []);
    }

    [Fact]
    public async Task Advance_requires_tech_readiness_before_first_strict_preview()
    {
        var userId = $"listener-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedStrictAttemptAsync(userId, attemptId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.PostAsJsonAsync(
            $"/v1/listening/v2/attempts/{attemptId}/advance",
            new { toState = ListeningFsmTransitions.A1Preview, confirmToken = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var rejected = await response.Content.ReadFromJsonAsync<AdvanceResultDto>(JsonSupport.Options);
        Assert.NotNull(rejected);
        Assert.Equal("rejected", rejected.Outcome);
        Assert.Equal("tech-readiness-required", rejected.RejectionReason);
    }

    [Fact]
    public async Task Tech_readiness_endpoint_persists_snapshot_for_owned_attempt()
    {
        var userId = $"listener-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedStrictAttemptAsync(userId, attemptId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.PostAsJsonAsync(
            $"/v1/listening/v2/attempts/{attemptId}/tech-readiness",
            new { audioOk = true, durationMs = 1800 });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TechReadinessDto>(JsonSupport.Options);
        Assert.NotNull(result);
        Assert.True(result.AudioOk);
        Assert.Equal(1800, result.DurationMs);
        Assert.Equal(900000, result.TtlMs);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = await db.ListeningAttempts.FindAsync([attemptId], CancellationToken.None);
        Assert.NotNull(attempt);
        Assert.Contains("audioOk", attempt.TechReadinessJson, StringComparison.Ordinal);
        Assert.Contains("checkedAt", attempt.TechReadinessJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_rejects_expired_tech_readiness_snapshot()
    {
        var userId = $"listener-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedStrictAttemptAsync(
            userId,
            attemptId,
            techReadinessJson: "{\"audioOk\":true,\"durationMs\":1500,\"checkedAt\":\"2000-01-01T00:00:00+00:00\"}");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.PostAsJsonAsync(
            $"/v1/listening/v2/attempts/{attemptId}/advance",
            new { toState = ListeningFsmTransitions.A1Preview, confirmToken = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var rejected = await response.Content.ReadFromJsonAsync<AdvanceResultDto>(JsonSupport.Options);
        Assert.NotNull(rejected);
        Assert.Equal("tech-readiness-required", rejected.RejectionReason);
        Assert.Contains("expired", rejected.RejectionDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task State_repairs_corrupt_navigation_json()
    {
        var userId = $"listener-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedStrictAttemptAsync(userId, attemptId, navigationStateJson: "{not-json");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.GetAsync($"/v1/listening/v2/attempts/{attemptId}/state");
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<SessionStateDto>(JsonSupport.Options);

        Assert.NotNull(state);
        Assert.Equal(ListeningFsmTransitions.Intro, state.State);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var attempt = await db.ListeningAttempts.FindAsync([attemptId], CancellationToken.None);
        Assert.NotNull(attempt);
        Assert.Contains(ListeningFsmTransitions.Intro, attempt.NavigationStateJson, StringComparison.Ordinal);
        Assert.NotNull(attempt.WindowStartedAt);
    }

    [Fact]
    public async Task State_repairs_corrupt_navigation_json_on_submitted_attempt_to_terminal_state()
    {
        var userId = $"listener-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedStrictAttemptAsync(
            userId,
            attemptId,
            navigationStateJson: "{not-json",
            status: ListeningAttemptStatus.Submitted);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.GetAsync($"/v1/listening/v2/attempts/{attemptId}/state");
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<SessionStateDto>(JsonSupport.Options);

        Assert.NotNull(state);
        Assert.Equal(ListeningFsmTransitions.Submitted, state.State);
        Assert.Contains(ListeningFsmTransitions.Intro, state.Locks);
    }

    [Fact]
    public async Task Free_navigation_rejects_unknown_destination_state()
    {
        var userId = $"listener-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        await SeedStrictAttemptAsync(userId, attemptId, mode: ListeningAttemptMode.Paper);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var response = await client.PostAsJsonAsync(
            $"/v1/listening/v2/attempts/{attemptId}/advance",
            new { toState = "not_a_state", confirmToken = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var rejected = await response.Content.ReadFromJsonAsync<AdvanceResultDto>(JsonSupport.Options);
        Assert.NotNull(rejected);
        Assert.Equal("rejected", rejected.Outcome);
        Assert.Equal("invalid-state", rejected.RejectionReason);
    }

    [Fact]
    public async Task Non_owner_attempt_routes_return_not_found()
    {
        var ownerId = $"listener-owner-{Guid.NewGuid():N}";
        var otherId = $"listener-other-{Guid.NewGuid():N}";
        var attemptId = $"att-{Guid.NewGuid():N}";
        await _factory.EnsureLearnerProfileAsync(ownerId, $"{ownerId}@example.test", ownerId);
        await _factory.EnsureLearnerProfileAsync(otherId, $"{otherId}@example.test", otherId);
        await SeedStrictAttemptAsync(ownerId, attemptId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", otherId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");

        var responses = new[]
        {
            await client.GetAsync($"/v1/listening/v2/attempts/{attemptId}/state"),
            await client.PostAsJsonAsync(
                $"/v1/listening/v2/attempts/{attemptId}/advance",
                new { toState = ListeningFsmTransitions.A1Preview, confirmToken = (string?)null }),
            await client.PostAsJsonAsync(
                $"/v1/listening/v2/attempts/{attemptId}/tech-readiness",
                new { audioOk = true, durationMs = 1500 }),
            await client.PostAsJsonAsync(
                $"/v1/listening/v2/attempts/{attemptId}/audio-resume",
                new { cuePointMs = 1000 }),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private async Task SeedStrictAttemptAsync(
        string userId,
        string attemptId,
        ListeningAttemptMode mode = ListeningAttemptMode.Exam,
        string? navigationStateJson = null,
        ListeningAttemptStatus status = ListeningAttemptStatus.InProgress,
        string? techReadinessJson = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        await db.Database.EnsureCreatedAsync();

        var now = DateTimeOffset.UtcNow;
        db.ListeningAttempts.Add(new ListeningAttempt
        {
            Id = attemptId,
            UserId = userId,
            PaperId = $"paper-{Guid.NewGuid():N}",
            StartedAt = now,
            LastActivityAt = now,
            Status = status,
            Mode = mode,
            MaxRawScore = 42,
            PolicySnapshotJson = "{}",
            NavigationStateJson = navigationStateJson,
            TechReadinessJson = techReadinessJson,
            LastQuestionVersionMapJson = "{}",
        });

        await db.SaveChangesAsync();
    }
}
