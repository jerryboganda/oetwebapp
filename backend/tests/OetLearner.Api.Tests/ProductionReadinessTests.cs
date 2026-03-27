using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

public class ProductionReadinessTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProductionReadinessTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SpeakingUploadPipeline_StoresBinary_AndStreamsItToExperts()
    {
        using var learner = CreateLearnerClient("audio-owner");
        var attemptId = await CreateSpeakingAttemptAsync(learner, "practice");

        var uploadSessionResponse = await learner.PostAsync($"/v1/speaking/attempts/{attemptId}/audio/upload-session", content: null);
        uploadSessionResponse.EnsureSuccessStatusCode();
        using var uploadSessionJson = JsonDocument.Parse(await uploadSessionResponse.Content.ReadAsStringAsync());
        var uploadSessionId = uploadSessionJson.RootElement.GetProperty("uploadSessionId").GetString()!;
        var uploadUrl = uploadSessionJson.RootElement.GetProperty("uploadUrl").GetString()!;
        var storageKey = uploadSessionJson.RootElement.GetProperty("storageKey").GetString()!;

        var audioPayload = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x10, 0x20, 0x30, 0x40 };
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(audioPayload)
        };
        uploadRequest.Headers.Add("X-Debug-UserId", "audio-owner");
        uploadRequest.Headers.Add("X-Debug-Role", "learner");
        uploadRequest.Headers.Add("X-Debug-Email", "audio-owner@example.test");
        uploadRequest.Headers.Add("X-Debug-Name", "audio-owner");
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");

        var uploadBinaryResponse = await learner.SendAsync(uploadRequest);
        uploadBinaryResponse.EnsureSuccessStatusCode();

        var uploadCompleteResponse = await learner.PostAsJsonAsync($"/v1/speaking/attempts/{attemptId}/audio/complete", new
        {
            uploadSessionId,
            storageKey,
            fileName = "practice.webm",
            sizeBytes = audioPayload.Length,
            durationSeconds = 42,
            captureMethod = "browser-recording",
            contentType = "audio/webm"
        });
        uploadCompleteResponse.EnsureSuccessStatusCode();

        var submitResponse = await learner.PostAsync($"/v1/speaking/attempts/{attemptId}/submit", content: null);
        submitResponse.EnsureSuccessStatusCode();
        using var submitJson = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        var evaluationId = submitJson.RootElement.GetProperty("evaluationId").GetString()!;

        await WaitForAsync(
            async () =>
            {
                var response = await learner.GetAsync($"/v1/speaking/evaluations/{evaluationId}/summary");
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("state").GetString() == "completed";
            },
            "speaking evaluation to complete");

        var reviewResponse = await learner.PostAsJsonAsync("/v1/reviews/requests", new
        {
            attemptId,
            subtest = "speaking",
            turnaroundOption = "standard",
            focusAreas = new[] { "fluency" },
            learnerNotes = "Please review the audio quality.",
            paymentSource = "invoice",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        reviewResponse.EnsureSuccessStatusCode();
        using var reviewJson = JsonDocument.Parse(await reviewResponse.Content.ReadAsStringAsync());
        var reviewRequestId = reviewJson.RootElement.GetProperty("reviewRequestId").GetString()!;

        using var expert = CreateExpertClient("expert-audio-reviewer");
        var claimResponse = await expert.PostAsync($"/v1/expert/queue/{reviewRequestId}/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var audioResponse = await expert.GetAsync($"/v1/expert/reviews/{reviewRequestId}/speaking/audio");
        audioResponse.EnsureSuccessStatusCode();
        Assert.Equal("audio/webm", audioResponse.Content.Headers.ContentType?.MediaType);
        var streamedPayload = await audioResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(audioPayload, streamedPayload);
    }

    [Fact]
    public async Task CheckoutSession_UsesConfiguredProductionCheckoutBaseUrl()
    {
        using var learner = CreateLearnerClient("checkout-user");

        var response = await learner.PostAsJsonAsync("/v1/billing/checkout-sessions", new
        {
            productType = "review_credits",
            quantity = 3,
            priceId = (string?)null,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checkoutUrl = json.RootElement.GetProperty("checkoutUrl").GetString();
        Assert.NotNull(checkoutUrl);
        Assert.StartsWith("https://app.example.test/billing/checkout", checkoutUrl, StringComparison.Ordinal);
        Assert.Contains("productType=review_credits", checkoutUrl, StringComparison.Ordinal);
        Assert.Contains("quantity=3", checkoutUrl, StringComparison.Ordinal);
    }

    private HttpClient CreateLearnerClient(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private HttpClient CreateExpertClient(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "expert");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private static async Task<string> CreateSpeakingAttemptAsync(HttpClient client, string context)
    {
        var response = await client.PostAsJsonAsync("/v1/speaking/attempts", new
        {
            contentId = "st-001",
            context,
            mode = "exam",
            deviceType = "desktop",
            parentAttemptId = (string?)null
        });
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("attemptId").GetString()
            ?? throw new InvalidOperationException("Speaking attempt id was missing.");
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string description)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }
}
