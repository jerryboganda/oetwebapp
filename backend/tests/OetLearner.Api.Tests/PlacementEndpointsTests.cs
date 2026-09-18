using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Content;
using OetLearner.Api.Services.Placement;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests;

/// <summary>
/// Placement proxy endpoints against a STUB engine (no live dependency):
/// the flag gate, service-token/on-behalf-of forwarding, OET-owned result
/// persistence, and the admin boundary on the reviewer surface.
/// </summary>
[CollectionDefinition("PlacementEndpoints", DisableParallelization = true)]
public sealed class PlacementEndpointsCollectionDefinition
{
}

[Collection("PlacementEndpoints")]
public class PlacementEndpointsTests : IDisposable
{
    private const string ServiceToken = "test-placement-service-token";

    private readonly StubPlacementApiWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly StubPlacementEngineHandler _engine;

    public PlacementEndpointsTests()
    {
        _engine = new StubPlacementEngineHandler();
        _factory = new StubPlacementApiWebApplicationFactory(placementEnabled: true, configureServices: services =>
        {
            services.RemoveAll<PlacementGateway>();
            var http = new HttpClient(_engine)
            {
                BaseAddress = new Uri("http://placement-engine.test"),
            };
            // The gateway prefers the default Authorization header — the same
            // channel the production registration sets from GEPA_SERVICE_TOKEN.
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", ServiceToken);
            services.AddSingleton(new PlacementGateway(
                http, new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base()), NullLogger<PlacementGateway>.Instance));
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        _client.DefaultRequestHeaders.Add("X-OET-Device-Id", "placement-endpoints-tests-device");
        SeedInertEmailVerificationGate();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task PlacementRoutes_Are404_WhenTheFlagIsDisabled()
    {
        using var factory = new StubPlacementApiWebApplicationFactory(placementEnabled: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Add("X-OET-Device-Id", "placement-flag-off-device");
        SeedInertEmailVerificationGate(factory);

        // Registration is an auth route and still works with the flag off.
        var email = $"placement.off.{Guid.NewGuid():N}@example.com";
        var register = await client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest(
            email, "Password123!", ApplicationUserRoles.Learner, "Placement Off",
            "Placement", "Off", "+15550009876",
            AgreeToTerms: true, AgreeToPrivacy: true, MarketingOptIn: false,
            RegistrationPurpose: "placement"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var session = await register.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", session.GetProperty("accessToken").GetString());

        // Authenticated placement calls must look absent (404), not merely
        // forbidden — the flag gates before any engine interaction.
        var response = await client.GetAsync("/v1/placement/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Status_ReportsEnabled_WhenTheFlagIsOn()
    {
        await RegisterLearnerAsync();

        var response = await _client.GetAsync("/v1/placement/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task SessionCreate_ForwardsServiceToken_AndCandidateUid()
    {
        var learner = await RegisterLearnerAsync();

        _engine.Enqueue("""
            {"session_id":"ses_stub0000000001","token":"engine-token-ignored","next_step":"worked_example","ruleset_version":"2.0.0-beta"}
            """);

        var response = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ses_stub0000000001", created.GetProperty("session_id").GetString());
        Assert.Equal("2.0.0-beta", created.GetProperty("ruleset_version").GetString());

        var request = _engine.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Request.Method);
        Assert.Equal("/api/sessions", request.Request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Request.Headers.Authorization?.Scheme);
        Assert.Equal(ServiceToken, request.Request.Headers.Authorization?.Parameter);
        Assert.Equal(learner.LearnerId, request.Request.Headers.GetValues("X-GEPA-Candidate-Uid").Single());
        Assert.NotNull(request.Body);
        Assert.Contains("\"candidate_uid\":\"" + learner.LearnerId + "\"", request.Body);
    }

    [Fact]
    public async Task FullResult_PersistsTheOetOwnedHistoryRow()
    {
        var learner = await RegisterLearnerAsync();

        // Session create, then the full result report (same mixed-casing shape
        // the live engine emits — wordingVersion camelCase, session_id snake).
        _engine.Enqueue("""
            {"session_id":"ses_stub0000000002","token":"engine-token-ignored","next_step":"worked_example","ruleset_version":"2.0.0-beta"}
            """);
        _engine.Enqueue("""
            {
              "session_id": "ses_stub0000000002",
              "profile_type": "full",
              "profileType": "full",
              "skills": [
                {"skill":"RD","status":"measured","band":"B1","range":null,"notes":[],"flags":[],"can_do":[],"growth_areas":[]},
                {"skill":"LSN","status":"measured","band":"A2","range":null,"notes":[],"flags":[],"can_do":[],"growth_areas":[]},
                {"skill":"SPK","status":"insufficient_evidence","band":null,"range":null,"notes":["queued for review"],"flags":["pending_review"],"can_do":[],"growth_areas":[]},
                {"skill":"WRT","status":"insufficient_evidence","band":null,"range":null,"notes":["queued for review"],"flags":["pending_review"],"can_do":[],"growth_areas":[]}
              ],
              "headline": {"kind":"none","band":null,"range":null},
              "confidence": "Low",
              "confidence_reasons": [],
              "confidenceReasons": [],
              "readiness": null,
              "retest_advice": "Recommended study interval: retest after 2-4 weeks of focused study.",
              "retestAdvice": "Recommended study interval: retest after 2-4 weeks of focused study.",
              "wording_version": "2.0.0-beta",
              "wordingVersion": "2.0.0-beta",
              "generated_at": "2026-09-18T00:00:00Z",
              "generatedAt": "2026-09-18T00:00:00Z"
            }
            """);

        var create = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var result = await _client.GetAsync("/v1/placement/session/ses_stub0000000002/result/full");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // The OET-owned row must exist with honest partial status + provenance.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.PlacementResults.SingleAsync(r => r.SessionId == "ses_stub0000000002");
        Assert.Equal(learner.LearnerId, row.LearnerUserId);
        Assert.Equal("2.0.0-beta", row.RulesetVersion);
        Assert.Equal("partial", row.Status);
        Assert.Contains("insufficient_evidence", row.ResultJson);

        // History + stored-result fetch return it.
        var history = await _client.GetAsync("/v1/placement/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyJson = await history.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, historyJson.GetArrayLength());
        Assert.Equal("ses_stub0000000002", historyJson[0].GetProperty("sessionId").GetString());
        Assert.Equal("2.0.0-beta", historyJson[0].GetProperty("rulesetVersion").GetString());

        var resultId = historyJson[0].GetProperty("id").GetString()!;
        var stored = await _client.GetAsync($"/v1/placement/history/{resultId}");
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);
        var storedJson = await stored.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ses_stub0000000002", storedJson.GetProperty("session_id").GetString());
    }

    [Fact]
    public async Task BetaOnly_NarrowsAccessToTheAllowlist()
    {
        var betaEmail = $"placement.beta.{Guid.NewGuid():N}@example.com";
        using var factory = new StubPlacementApiWebApplicationFactory(
            placementEnabled: true,
            extraSettings: new Dictionary<string, string?>
            {
                ["Features:PlacementBetaOnly"] = "true",
                ["Features:PlacementBetaEmails"] = $"other.listed@example.com;{betaEmail}",
            });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Add("X-OET-Device-Id", "placement-beta-tests-device");
        SeedInertEmailVerificationGate(factory);

        // Allowlisted account: granted, and the status response says so.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await RegisterForTokenAsync(client, betaEmail));
        var status = await client.GetAsync("/v1/placement/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusJson = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(statusJson.GetProperty("enabled").GetBoolean());
        Assert.True(statusJson.GetProperty("betaOnly").GetBoolean());
        Assert.Equal("granted", statusJson.GetProperty("access").GetString());

        // Non-allowlisted account: the route looks absent (404) — the flag
        // gates without leaking that the feature exists.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await RegisterForTokenAsync(client, $"placement.outsider.{Guid.NewGuid():N}@example.com"));
        var denied = await client.GetAsync("/v1/placement/status");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    private static async Task<string> RegisterForTokenAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest(
            email, "Password123!", ApplicationUserRoles.Learner, "Placement Beta",
            "Placement", "Beta", "+15550001555",
            AgreeToTerms: true, AgreeToPrivacy: true, MarketingOptIn: false,
            RegistrationPurpose: "placement"));
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"register {(int)response.StatusCode}: {body}");
        }
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        return session.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task ReviewerSurface_RequiresAdmin()
    {
        await RegisterLearnerAsync();

        var queue = await _client.GetAsync("/v1/admin/placement/review/queue");
        Assert.Equal(HttpStatusCode.Forbidden, queue.StatusCode);

        var health = await _client.GetAsync("/v1/admin/placement/health");
        Assert.Equal(HttpStatusCode.Forbidden, health.StatusCode);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private sealed record LearnerIdentity(string LearnerId, string AccessToken, string Email);

    private async Task<LearnerIdentity> RegisterLearnerAsync()
    {
        var email = $"placement.proxy.{Guid.NewGuid():N}@example.com";
        var response = await _client.PostAsJsonAsync("/v1/auth/register", new RegisterRequest(
            email,
            "Password123!",
            ApplicationUserRoles.Learner,
            "Placement Proxy",
            "Placement", "Proxy",
            "+15550001234",
            AgreeToTerms: true,
            AgreeToPrivacy: true,
            MarketingOptIn: false,
            RegistrationPurpose: "placement"));
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"register {(int)response.StatusCode}: {body}");
        }
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = session.GetProperty("accessToken").GetString()!;
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var learnerId = await db.Users
            .Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync();
        return new LearnerIdentity(learnerId, accessToken, email);
    }

    private void SeedInertEmailVerificationGate()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.RuntimeSettings.Add(new RuntimeSettingsRow
        {
            Id = "default",
            SecurityRequireVerifiedEmailForLearners = false,
        });
        db.SaveChanges();
        scope.ServiceProvider.GetRequiredService<IRuntimeSettingsProvider>().Invalidate();
    }

    private static void SeedInertEmailVerificationGate(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        db.RuntimeSettings.Add(new RuntimeSettingsRow
        {
            Id = "default",
            SecurityRequireVerifiedEmailForLearners = false,
        });
        db.SaveChanges();
        scope.ServiceProvider.GetRequiredService<IRuntimeSettingsProvider>().Invalidate();
    }

    /// <summary>Serves queued raw-JSON bodies (engine shapes preserved verbatim)
    /// and captures the last outbound request for forwarding assertions.</summary>
    private sealed record CapturedRequest(HttpRequestMessage Request, string? Body);

    private sealed class StubPlacementEngineHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();

        /// <summary>The body string is captured INSIDE the handler: HttpClient
        /// disposes request content after send, so the test cannot read it later.</summary>
        public CapturedRequest? LastRequest { get; private set; }

        public void Enqueue(string json) => _responses.Enqueue(json);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastRequest = new CapturedRequest(request, body);
            if (_responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"stub queue empty\"}", Encoding.UTF8, "application/json"),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);

        public void Set(DateTimeOffset value) => _now = value;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class TestCleanUploadScanner : IUploadScanner
    {
        public Task<(bool clean, string? reason)> ScanAsync(Stream stream, string filename, CancellationToken ct)
            => Task.FromResult<(bool, string?)>((true, null));
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>Production-env test factory (mirrors JwtAuthApiWebApplicationFactory):
    /// every config the Production boot refuses to start without, plus the
    /// placement flag and an optional service-collection hook for the gateway.</summary>
    private sealed class StubPlacementApiWebApplicationFactory : WebApplicationFactory<Program>
    {
        /// <summary>Applied via builder.UseSetting (per-host configuration) in
        /// ConfigureWebHost — NOT via process env vars: xunit constructs every
        /// test-class instance up front, so ctor-time env mutation interleaves
        /// across instances and the flag leaks between tests.</summary>
        private readonly Dictionary<string, string?> _settings;

        public StubPlacementApiWebApplicationFactory(
            bool placementEnabled,
            Action<IServiceCollection>? configureServices = null,
            Dictionary<string, string?>? extraSettings = null)
        {
            ConfigureServicesHook = configureServices;

            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"InMemory:oet-learner-placement-tests-{Guid.NewGuid():N}",
                ["Auth:UseDevelopmentAuth"] = "false",
                ["Bootstrap:AutoMigrate"] = "false",
                ["Bootstrap:SeedDemoData"] = "false",
                ["Features:PlacementEnabled"] = placementEnabled ? "true" : "false",
                ["Platform:PublicApiBaseUrl"] = "https://api.example.test",
                ["Platform:FallbackEmailDomain"] = "example.test",
                ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
                ["Billing:AllowSandboxFallbacks"] = "false",
                ["Billing:Stripe:SecretKey"] = "sk_test_placement_tests",
                ["Billing:Stripe:SuccessUrl"] = "https://app.example.test/billing/checkout?checkout=success",
                ["Billing:Stripe:CancelUrl"] = "https://app.example.test/billing/checkout?checkout=cancelled",
                ["Billing:Stripe:WebhookSecret"] = "whsec_placement_tests",
                ["Storage:LocalRootPath"] = Path.Combine(Path.GetTempPath(), $"oet-learner-placement-storage-{Guid.NewGuid():N}"),
                ["Proxy:TrustForwardHeaders"] = "false",
                ["Proxy:EnforceHttps"] = "false",
                ["AuthTokens:Issuer"] = "https://api.example.test",
                ["AuthTokens:Audience"] = "oet-learner-web",
                ["AuthTokens:AccessTokenSigningKey"] = "access-token-signing-key-12345678901234567890",
                ["AuthTokens:RefreshTokenSigningKey"] = "refresh-token-signing-key-1234567890123456789",
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
                ["UploadScanner:Provider"] = "clamav",
                ["UploadScanner:Host"] = "clamav",
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
                ["Listening:TtsProvider"] = "elevenlabs",
                // Disable HIBP breach check (C7 password policy) — fixture
                // passwords are in the breach corpus; complexity stays enforced.
                ["PasswordPolicy:BreachCheckEnabled"] = "false"
            };

            if (extraSettings is not null)
            {
                foreach (var (key, value) in extraSettings)
                {
                    settings[key] = value;
                }
            }

            _settings = settings;
        }

        private Action<IServiceCollection>? ConfigureServicesHook { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            foreach (var (key, value) in _settings)
            {
                builder.UseSetting(key, value);
            }
            builder.ConfigureServices(services =>
            {
                var sender = new RecordingEmailSender();
                services.RemoveAll<IEmailSender>();
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IUploadScanner>();
                services.AddSingleton<IEmailSender>(sender);
                services.AddSingleton<TimeProvider>(new MutableTimeProvider(DateTimeOffset.UtcNow));
                services.AddSingleton<IUploadScanner, TestCleanUploadScanner>();
                ConfigureServicesHook?.Invoke(services);
            });
        }


    }
}
