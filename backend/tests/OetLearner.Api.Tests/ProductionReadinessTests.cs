using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

[CollectionDefinition("ProductionReadiness", DisableParallelization = true)]
public sealed class ProductionReadinessCollectionDefinitionMarker
{
}

[Collection("ProductionReadiness")]
public class ProductionReadinessTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProductionReadinessTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task App_BootsWithFirstPartyTokenConfiguration_InProduction()
    {
        using var factory = CreateProductionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var options = factory.Services.GetRequiredService<IOptions<AuthTokenOptions>>().Value;
        Assert.Equal("https://api.example.test", options.Issuer);
        Assert.Equal("oet-learner-web", options.Audience);
        Assert.Equal(TimeSpan.FromMinutes(15), options.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(30), options.RefreshTokenLifetime);
        Assert.Equal(TimeSpan.FromMinutes(10), options.OtpLifetime);
        Assert.Equal("OET Learner", options.AuthenticatorIssuer);
    }

    [Fact]
    public async Task App_IgnoresDevelopmentAuthHeaders_InProduction()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings["Auth:UseDevelopmentAuth"] = "true";
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-Role", "admin");
        client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", AdminPermissions.SystemAdmin);

        var response = await client.GetAsync("/v1/admin/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task App_BootsWithBrevoConfiguration_InProduction()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings["Brevo:Enabled"] = "true";
            settings["Brevo:ApiKey"] = "brevo-api-key-123";
            settings["Brevo:BaseUrl"] = "https://api.brevo.com/v3";
            settings["Brevo:FromEmail"] = "no-reply@example.test";
            settings["Brevo:FromName"] = "OET Learner";
            settings["Brevo:EmailVerificationTemplateId"] = "123";
            settings["Brevo:PasswordResetTemplateId"] = "124";
            settings["Smtp:Enabled"] = "false";
        });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var sender = factory.Services.GetRequiredService<IEmailSender>();
        Assert.Equal("BrevoEmailSender", sender.GetType().Name);
    }

    [Fact]
    public async Task App_BootsWithBrevoSmtpRelayConfiguration_InProduction()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings["Brevo:Enabled"] = "false";
            settings["Smtp:Enabled"] = "true";
            settings["Smtp:Host"] = "smtp-relay.brevo.com";
            settings["Smtp:Port"] = "587";
            settings["Smtp:EnableSsl"] = "true";
            settings["Smtp:Username"] = "brevo-login@example.test";
            settings["Smtp:Password"] = "brevo-smtp-key-placeholder";
            settings["Smtp:FromEmail"] = "no-reply@example.test";
        });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var sender = factory.Services.GetRequiredService<IEmailSender>();
        Assert.Equal("SmtpEmailSender", sender.GetType().Name);
    }

    [Fact]
    public void App_FailsFastInProduction_WhenTokenSecretIsMissing()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings.Remove("AuthTokens:AccessTokenSigningKey");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("AuthTokens", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("AccessTokenSigningKey", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void App_FailsFastInProduction_WhenTokenExpiriesAreMissing()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings["AuthTokens:AccessTokenLifetime"] = "00:00:00";
            settings["AuthTokens:RefreshTokenLifetime"] = "00:00:00";
            settings["AuthTokens:OtpLifetime"] = "00:00:00";
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("AuthTokens", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("AccessTokenLifetime", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void App_FailsFastInProduction_WhenSmtpConfigIsMissing()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings["Smtp:Enabled"] = "false";
            settings.Remove("Smtp:Host");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Smtp", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("FromEmail", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void App_FailsFastInProduction_WhenBrevoConfigIsMissing()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings["Brevo:Enabled"] = "true";
            settings.Remove("Brevo:ApiKey");
            settings.Remove("Brevo:EmailVerificationTemplateId");
            settings.Remove("Brevo:PasswordResetTemplateId");
            settings["Smtp:Enabled"] = "false";
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Brevo", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("EmailVerificationTemplateId", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void App_FailsFastInProduction_WhenBillingWebhookSecretIsMissing()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings.Remove("Billing:Stripe:WebhookSecret");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Billing:Stripe", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("WebhookSecret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void App_FailsFastInProduction_WhenSandboxBillingFallbacksAreEnabled()
    {
        using var factory = CreateProductionFactory(settings =>
        {
            settings["Billing:AllowSandboxFallbacks"] = "true";
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Billing:AllowSandboxFallbacks", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpeakingUploadPipeline_StoresBinary_AndStreamsItToExperts()
    {
        using var learner = await CreateLearnerClientAsync("audio-owner");
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
            contentType = "audio/webm",
            consentAccepted = true,
            consentText = "Test consent accepted"
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

        // Wave 1 regression (docs/SPEAKING-MODULE-PLAN.md §3): the summary
        // payload must carry the stable 9-key criterion contract and the
        // advisory readiness band so the results page can render its new
        // criterion-by-criterion card without re-deriving projections.
        var summaryResponse = await learner.GetAsync($"/v1/speaking/evaluations/{evaluationId}/summary");
        summaryResponse.EnsureSuccessStatusCode();
        using (var summaryJson = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync()))
        {
            var root = summaryJson.RootElement;
            Assert.Equal(350, root.GetProperty("passThreshold").GetInt32());
            Assert.Equal(39, root.GetProperty("rubricMax").GetInt32());

            var readiness = root.GetProperty("readinessBand").GetString();
            Assert.Contains(readiness, new[] { "not_ready", "developing", "borderline", "exam_ready", "strong" });
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("readinessBandLabel").GetString()));

            var criteria = root.GetProperty("criteria");
            Assert.Equal(JsonValueKind.Array, criteria.ValueKind);
            Assert.Equal(9, criteria.GetArrayLength());
            var seenCodes = new HashSet<string>();
            foreach (var entry in criteria.EnumerateArray())
            {
                var code = entry.GetProperty("criterionCode").GetString();
                Assert.False(string.IsNullOrWhiteSpace(code));
                Assert.True(seenCodes.Add(code!), $"duplicate criterionCode {code}");
                var family = entry.GetProperty("family").GetString();
                Assert.True(family == "linguistic" || family == "clinical");
                var max = entry.GetProperty("max").GetInt32();
                Assert.True(family == "linguistic" ? max == 6 : max == 3);
                var score = entry.GetProperty("score").GetInt32();
                Assert.InRange(score, 0, max);
            }
            Assert.Contains("intelligibility", seenCodes);
            Assert.Contains("structure", seenCodes);
        }

        await SetWalletCreditsAsync("audio-owner", 1);

        var reviewResponse = await learner.PostAsJsonAsync("/v1/reviews/requests", new
        {
            attemptId,
            subtest = "speaking",
            turnaroundOption = "standard",
            focusAreas = new[] { "fluency" },
            learnerNotes = "Please review the audio quality.",
            paymentSource = "credits",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        reviewResponse.EnsureSuccessStatusCode();
        using var reviewJson = JsonDocument.Parse(await reviewResponse.Content.ReadAsStringAsync());
        var reviewRequestId = reviewJson.RootElement.GetProperty("reviewRequestId").GetString()!;

        using var expert = await CreateExpertClientAsync("expert-audio-reviewer");
        var claimResponse = await expert.PostAsync($"/v1/expert/queue/{reviewRequestId}/claim", content: null);
        claimResponse.EnsureSuccessStatusCode();

        var audioResponse = await expert.GetAsync($"/v1/expert/reviews/{reviewRequestId}/speaking/audio");
        audioResponse.EnsureSuccessStatusCode();
        Assert.Equal("audio/webm", audioResponse.Content.Headers.ContentType?.MediaType);
        var streamedPayload = await audioResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(audioPayload, streamedPayload);
    }

    [Fact]
    public async Task CheckoutSession_UsesHostedProviderCheckoutUrl()
    {
        using var learner = await CreateLearnerClientAsync("checkout-user");

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
        Assert.StartsWith("https://checkout.stripe.com/pay/", checkoutUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("app.example.test/billing/checkout", checkoutUrl, StringComparison.Ordinal);
    }

    private async Task<HttpClient> CreateLearnerClientAsync(string userId)
    {
        await _factory.EnsureLearnerProfileAsync(userId, $"{userId}@example.test", userId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", "learner");
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-Name", userId);
        return client;
    }

    private async Task<HttpClient> CreateExpertClientAsync(string userId)
    {
        await _factory.EnsureExpertProfileAsync(userId, $"{userId}@example.test", userId);
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

    private async Task SetWalletCreditsAsync(string userId, int credits)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var wallet = await db.Wallets.FirstAsync(x => x.UserId == userId);
        wallet.CreditBalance = credits;
        wallet.LastUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private static ProductionAuthWebApplicationFactory CreateProductionFactory(Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"InMemory:oet-learner-production-tests-{Guid.NewGuid():N}",
            ["Auth:UseDevelopmentAuth"] = "false",
            ["Bootstrap:AutoMigrate"] = "false",
            ["Bootstrap:SeedDemoData"] = "false",
            ["Platform:PublicApiBaseUrl"] = "https://api.example.test",
            ["Platform:FallbackEmailDomain"] = "example.test",
            ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
            ["Billing:AllowSandboxFallbacks"] = "false",
            ["Billing:Stripe:SecretKey"] = "sk_test_production_readiness",
            ["Billing:Stripe:SuccessUrl"] = "https://app.example.test/billing/checkout?checkout=success",
            ["Billing:Stripe:CancelUrl"] = "https://app.example.test/billing/checkout?checkout=cancelled",
            ["Billing:Stripe:WebhookSecret"] = "whsec_production_readiness",
            ["Proxy:TrustForwardHeaders"] = "true",
            ["Proxy:EnforceHttps"] = "false",
            ["AuthTokens:Issuer"] = "https://api.example.test",
            ["AuthTokens:Audience"] = "oet-learner-web",
            ["AuthTokens:AccessTokenSigningKey"] = "local-access-token-signing-key-1234567890",
            ["AuthTokens:RefreshTokenSigningKey"] = "local-refresh-token-signing-key-1234567890",
            ["AuthTokens:AccessTokenLifetime"] = "00:15:00",
            ["AuthTokens:RefreshTokenLifetime"] = "30.00:00:00",
            ["AuthTokens:OtpLifetime"] = "00:10:00",
            ["AuthTokens:AuthenticatorIssuer"] = "OET Learner",
            ["Smtp:Enabled"] = "true",
            ["Smtp:Host"] = "smtp-relay.brevo.com",
            ["Smtp:Port"] = "587",
            ["Smtp:EnableSsl"] = "true",
            ["Smtp:Username"] = "brevo-login@example.test",
            ["Smtp:Password"] = "brevo-smtp-key-placeholder",
            ["Smtp:FromEmail"] = "no-reply@example.test",
            ["Smtp:FromName"] = "OET Learner",
            // C5 UploadScannerValidator refuses noop in Production. Boot-test factory has no DI
            // override, so use a non-noop stub provider keyword that passes the validator.
            // None of these tests exercise actual uploads, so the host never resolves a scanner.
            ["UploadScanner:Provider"] = "clamav",
            ["UploadScanner:Host"] = "localhost",
            ["UploadScanner:Port"] = "3310",
            ["AI:ProviderId"] = "digitalocean-serverless",
            ["AI:DefaultModel"] = "glm-5",
            ["AI:ApiKey"] = "real-ai-provider-key",
            ["AI:BaseUrl"] = "https://inference.do-ai.run/v1",
            ["Pronunciation:Provider"] = "azure",
            ["Pronunciation:AzureSpeechKey"] = "azure-pronunciation-key",
            ["Pronunciation:AzureSpeechRegion"] = "uksouth",
            ["Conversation:AsrProvider"] = "deepgram",
            ["Conversation:DeepgramApiKey"] = "deepgram-conversation-key",
            ["Conversation:TtsProvider"] = "off",
            // C7 PasswordPolicy HIBP check hits real network from tests; disable it here.
            ["PasswordPolicy:BreachCheckEnabled"] = "false"
        };

        configure?.Invoke(settings);
        return new ProductionAuthWebApplicationFactory(settings);
    }

    private sealed class ProductionAuthWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _previousValues;

        public ProductionAuthWebApplicationFactory(Dictionary<string, string?> settings)
        {
            _previousValues = settings.ToDictionary(
                entry => ToEnvironmentVariableName(entry.Key),
                entry => Environment.GetEnvironmentVariable(ToEnvironmentVariableName(entry.Key)));

            foreach (var (key, value) in settings)
            {
                Environment.SetEnvironmentVariable(ToEnvironmentVariableName(key), value);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            foreach (var (key, value) in _previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        private static string ToEnvironmentVariableName(string configurationKey)
        {
            return configurationKey.Replace(":", "__", StringComparison.Ordinal);
        }
    }
}
