using System.Net;
using System.Net.Http.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// <c>POST /v1/listening-papers/attempts/{id}/integrity-events/batch</c> carries the attempt-event
/// stream (answer changes, highlights, buffering, reading time) in one request instead of one per
/// click. It applies each event through the same service call as the single-event route, so the
/// per-event behaviour is covered by <see cref="ListeningAttemptEventLoggingTests"/>; these tests
/// cover the batch envelope: validation, auth and that events do reach that service.
/// </summary>
public class ListeningIntegrityEventBatchEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private const string BatchRoute = "/v1/listening-papers/attempts/lat-batch-none/integrity-events/batch";

    private readonly TestWebApplicationFactory _factory;

    public ListeningIntegrityEventBatchEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EmptyBatch_IsAcknowledged()
    {
        using var client = CreateClient("learner");

        var response = await client.PostAsJsonAsync(BatchRoute, new { events = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task MissingEvents_IsAcknowledged()
    {
        using var client = CreateClient("learner");

        var response = await client.PostAsJsonAsync(BatchRoute, new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task OversizedBatch_IsRejectedRatherThanTruncated()
    {
        using var client = CreateClient("learner");
        var events = Enumerable.Range(0, 51)
            .Select(_ => new { eventType = "answer_changed", details = (string?)null, occurredAt = DateTimeOffset.UtcNow })
            .ToArray();

        var response = await client.PostAsJsonAsync(BatchRoute, new { events });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("listening_integrity_batch_too_large", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Events_AreHandedToTheSameServiceAsTheSingleRoute_SoAnUnknownAttemptIsNotFound()
    {
        using var client = CreateClient("learner");
        var events = new[]
        {
            new { eventType = "answer_changed", details = (string?)"{\"questionId\":\"q1\"}", occurredAt = DateTimeOffset.UtcNow },
            new { eventType = "highlight", details = (string?)null, occurredAt = DateTimeOffset.UtcNow },
        };

        var batch = await client.PostAsJsonAsync(BatchRoute, new { events });
        var single = await client.PostAsJsonAsync(
            "/v1/listening-papers/attempts/lat-batch-none/integrity-events",
            events[0]);

        // Whatever the single route answers for this learner and attempt, the batch answers too.
        Assert.Equal(HttpStatusCode.NotFound, single.StatusCode);
        Assert.Equal(single.StatusCode, batch.StatusCode);
    }

    [Fact]
    public async Task Batch_IsLearnerOnly()
    {
        using var client = CreateClient("admin");

        var response = await client.PostAsJsonAsync(BatchRoute, new { events = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", $"listening-batch-{role}");
        client.DefaultRequestHeaders.Add("X-Debug-Role", role);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"listening-batch-{role}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", "Listening Batch");
        return client;
    }
}
