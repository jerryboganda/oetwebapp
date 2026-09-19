using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
    private readonly FailAccommodationUseSaveInterceptor _failUseSave = new();
    private readonly CapturingLoggerProvider _logs = new();

    public PlacementEndpointsTests()
    {
        _engine = new StubPlacementEngineHandler();
        _factory = new StubPlacementApiWebApplicationFactory(placementEnabled: true, configureServices: services =>
        {
            // Lets a test simulate the accommodation-use insert failing AFTER
            // the engine created the session. Inert until Armed.
            services.ConfigureDbContext<LearnerDbContext>(options => options.AddInterceptors(_failUseSave));
            // Lets a test assert an operational alarm string was logged.
            services.AddSingleton<ILoggerProvider>(_logs);
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
        // The engine-issued candidate bearer never reaches the browser.
        Assert.False(created.TryGetProperty("token", out _));
        Assert.Equal("worked_example", created.GetProperty("next_step").GetString());

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
    public async Task Upload_AcceptsChromiumCodecParameterizedWebm()
    {
        await RegisterLearnerAsync();
        _engine.Enqueue("""
            {"storage_path":"rec_stub.webm","metrics":{"duration_sec":4.0,"container":"webm","metrics_provenance":"pcm_analysis"}}
            """);

        using var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x00, 0x01 });
        // Chromium's MediaRecorder reports the codec as a media-type
        // parameter. The gateway used to rebuild the part header with the
        // MediaTypeHeaderValue ctor, which rejects parameters — a 500.
        audio.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm;codecs=opus");
        content.Add(audio, "file", "speaking-SPK-1.webm");

        var response = await _client.PostAsync("/v1/placement/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = _engine.LastRequest!;
        Assert.Equal("/api/media/upload", request.Request.RequestUri!.AbsolutePath);
        Assert.NotNull(request.Body);
        Assert.Contains("audio/webm", request.Body);
    }

    [Fact]
    public async Task UnitStart_ForwardsToTheEngineUnitStartRoute()
    {
        var learner = await RegisterLearnerAsync();
        _engine.Enqueue("""
            {"items":[],"deadline_at":"2026-09-19T10:02:00Z","started_at":"2026-09-19T10:00:00Z","time_budget_sec":120,"module_complete":false}
            """);

        var response = await _client.PostAsync("/v1/placement/session/ses_stub0000000001/module/LSN/unit/start", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = _engine.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Request.Method);
        Assert.Equal("/api/sessions/ses_stub0000000001/modules/LSN/unit/start", request.Request.RequestUri!.AbsolutePath);
        Assert.Equal(learner.LearnerId, request.Request.Headers.GetValues("X-GEPA-Candidate-Uid").Single());
    }

    [Fact]
    public async Task UnitTechnical_RejectsUnknownReasons_AndForwardsKnownOnes()
    {
        await RegisterLearnerAsync();
        const string path = "/v1/placement/session/ses_stub0000000001/module/LSN/unit/technical";

        var rejected = await _client.PostAsJsonAsync(path, new { reason = "made_up" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Null(_engine.LastRequest);

        _engine.Enqueue("""{"next":null}""");
        var accepted = await _client.PostAsJsonAsync(path, new { reason = "audio_zero_duration" });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var request = _engine.LastRequest!;
        Assert.Equal("/api/sessions/ses_stub0000000001/modules/LSN/unit/technical", request.Request.RequestUri!.AbsolutePath);
        Assert.NotNull(request.Body);
        Assert.Contains("\"reason\":\"audio_zero_duration\"", request.Body);
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

    // ── Admin-approved extra time (accommodations) ───────────────────

    private const string AccommodationsRoute = "/v1/admin/placement/accommodations";

    [Fact]
    public async Task AdminGrant_RecordsApproverAndTime_AndReturns201()
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");

        var response = await AdminPostAsync(admin, AccommodationsRoute, new
        {
            learnerEmail = learner.Email.ToUpperInvariant(), // normalized-email lookup
            extraTimePercent = 25,
            reference = "  TICKET-42  ",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetString()!;
        Assert.StartsWith("pacc_", id);
        Assert.Equal(learner.LearnerId, dto.GetProperty("learnerUserId").GetString());
        Assert.Equal(learner.Email, dto.GetProperty("learnerEmail").GetString());
        Assert.Equal(JsonValueKind.String, dto.GetProperty("learnerName").ValueKind);
        Assert.Equal(25, dto.GetProperty("extraTimePercent").GetInt32());
        Assert.Equal("TICKET-42", dto.GetProperty("reference").GetString());
        Assert.Equal("active", dto.GetProperty("status").GetString());
        Assert.Equal(admin.UserId, dto.GetProperty("approvedByUserId").GetString());
        Assert.Equal("Dr Placement Admin", dto.GetProperty("approvedByName").GetString());
        var approvedAt = ParseIso(dto.GetProperty("approvedAt").GetString()!);
        Assert.True(Math.Abs((approvedAt - DateTimeOffset.UtcNow).TotalMinutes) < 5);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("revokedByUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("revokedByName").ValueKind);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("revokedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("revokedReason").ValueKind);
        Assert.Equal(0, dto.GetProperty("uses").GetArrayLength());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var row = await db.PlacementAccommodations.SingleAsync(a => a.Id == id);
        Assert.Equal(learner.LearnerId, row.LearnerUserId);
        Assert.Equal(25, row.ExtraTimePercent);
        Assert.Equal("TICKET-42", row.Reference);
        Assert.Equal(admin.UserId, row.ApprovedByUserId);
        Assert.Equal("Dr Placement Admin", row.ApprovedByName);
        Assert.Equal(approvedAt, row.ApprovedAt);
        Assert.Null(row.RevokedAt);

        var audit = await db.AuditEvents.SingleAsync(e => e.ResourceId == id);
        Assert.Equal("PlacementAccommodationGranted", audit.Action);
        Assert.Equal("PlacementAccommodation", audit.ResourceType);
        Assert.Equal(admin.UserId, audit.ActorId);
        Assert.Equal("Dr Placement Admin", audit.ActorName);
    }

    [Fact]
    public async Task AdminGrant_SupersedesTheLearnersActiveGrant()
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");

        var first = await AdminGrantAsync(admin, learner, 25);
        await AdvanceClockAsync();
        var second = await AdminGrantAsync(admin, learner, 50);

        var all = await AdminGetAsync(admin,
            $"{AccommodationsRoute}?learner={Uri.EscapeDataString(learner.Email)}&includeRevoked=true");
        Assert.Equal(2, all.GetArrayLength());
        // Newest first: the replacement is active, the old grant is revoked
        // by the same admin with the "superseded" reason.
        Assert.Equal(second.GetProperty("id").GetString(), all[0].GetProperty("id").GetString());
        Assert.Equal("active", all[0].GetProperty("status").GetString());
        Assert.Equal(50, all[0].GetProperty("extraTimePercent").GetInt32());
        Assert.Equal(first.GetProperty("id").GetString(), all[1].GetProperty("id").GetString());
        Assert.Equal("revoked", all[1].GetProperty("status").GetString());
        Assert.Equal("superseded", all[1].GetProperty("revokedReason").GetString());
        Assert.Equal(admin.UserId, all[1].GetProperty("revokedByUserId").GetString());
        Assert.Equal("Dr Placement Admin", all[1].GetProperty("revokedByName").GetString());
        Assert.Equal(JsonValueKind.String, all[1].GetProperty("revokedAt").ValueKind);

        // Default listing hides revoked grants.
        var activeOnly = await AdminGetAsync(admin, $"{AccommodationsRoute}?learner={Uri.EscapeDataString(learner.LearnerId)}");
        Assert.Equal(1, activeOnly.GetArrayLength());
        Assert.Equal(second.GetProperty("id").GetString(), activeOnly[0].GetProperty("id").GetString());

        // Exactly one active grant per learner in the store.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(1, await db.PlacementAccommodations.CountAsync(a => a.LearnerUserId == learner.LearnerId && a.RevokedAt == null));
    }

    [Fact]
    public async Task AdminRevoke_RevokesTheGrant_AndIsIdempotent()
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");
        var grant = await AdminGrantAsync(admin, learner, 30);
        var id = grant.GetProperty("id").GetString()!;
        var revokeRoute = $"{AccommodationsRoute}/{id}/revoke";

        var revoked = await AdminPostAsync(admin, revokeRoute, new { reason = "No longer required" });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var dto = await revoked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("revoked", dto.GetProperty("status").GetString());
        Assert.Equal("No longer required", dto.GetProperty("revokedReason").GetString());
        Assert.Equal(admin.UserId, dto.GetProperty("revokedByUserId").GetString());
        Assert.Equal("Dr Placement Admin", dto.GetProperty("revokedByName").GetString());
        var revokedAt = dto.GetProperty("revokedAt").GetString();
        Assert.NotNull(revokedAt);

        // A second revoke changes nothing (the original reason/time stand).
        var again = await AdminPostAsync(admin, revokeRoute, new { reason = "A different reason" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var againDto = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("revoked", againDto.GetProperty("status").GetString());
        Assert.Equal("No longer required", againDto.GetProperty("revokedReason").GetString());
        Assert.Equal(revokedAt, againDto.GetProperty("revokedAt").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            // One grant audit + exactly one revoke audit (the repeat wrote none).
            Assert.Equal(1, await db.AuditEvents.CountAsync(e => e.ResourceId == id && e.Action == "PlacementAccommodationRevoked"));
        }

        var missing = await AdminPostAsync(admin, $"{AccommodationsRoute}/pacc_doesnotexist/revoke", new { reason = "x" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task AccommodationRoutes_RequireAdmin()
    {
        var learner = await RegisterLearnerAsync();

        var grant = await _client.PostAsJsonAsync(AccommodationsRoute, new
        {
            learnerUserId = learner.LearnerId,
            extraTimePercent = 50,
        });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);

        var list = await _client.GetAsync(AccommodationsRoute);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var revoke = await _client.PostAsJsonAsync($"{AccommodationsRoute}/pacc_anything/revoke", new { reason = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);

        // The candidate cannot self-grant: nothing was written.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await db.PlacementAccommodations.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task AdminGrant_RejectsPercentOutsideOneToHundred(int percent)
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");

        var response = await AdminPostAsync(admin, AccommodationsRoute, new
        {
            learnerUserId = learner.LearnerId,
            extraTimePercent = percent,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await db.PlacementAccommodations.CountAsync());
    }

    [Fact]
    public async Task AdminGrant_RejectsMissingLearnerIdentifier_AndOverlongReference()
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");

        var noLearner = await AdminPostAsync(admin, AccommodationsRoute, new { extraTimePercent = 25 });
        Assert.Equal(HttpStatusCode.BadRequest, noLearner.StatusCode);

        var longReference = await AdminPostAsync(admin, AccommodationsRoute, new
        {
            learnerUserId = learner.LearnerId,
            extraTimePercent = 25,
            reference = new string('r', 201),
        });
        Assert.Equal(HttpStatusCode.BadRequest, longReference.StatusCode);
    }

    [Fact]
    public async Task AdminGrant_UnknownLearner_Returns404()
    {
        var admin = await IssueAdminAsync("Dr Placement Admin");

        var byEmail = await AdminPostAsync(admin, AccommodationsRoute, new
        {
            learnerEmail = $"nobody.{Guid.NewGuid():N}@example.com",
            extraTimePercent = 25,
        });
        Assert.Equal(HttpStatusCode.NotFound, byEmail.StatusCode);

        var byId = await AdminPostAsync(admin, AccommodationsRoute, new
        {
            learnerUserId = "learner_doesnotexist",
            extraTimePercent = 25,
        });
        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
    }

    [Fact]
    public async Task SessionCreate_WithActiveGrant_ForwardsAccommodations_AndRecordsOneUse()
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");
        var grant = await AdminGrantAsync(admin, learner, 25);
        Authenticate(learner.AccessToken);

        var grantId = grant.GetProperty("id").GetString()!;
        var engineSession = CreatedSessionJson("ses_stubacc0000001", 25, grantId);
        _engine.Enqueue(engineSession);
        var response = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The echo matched, so the create succeeds; the learner still gets no
        // engine bearer and no approval id, only THAT (and how much) extra time applies.
        var createdRaw = await response.Content.ReadAsStringAsync();
        using (var created = JsonDocument.Parse(createdRaw))
        {
            Assert.False(created.RootElement.TryGetProperty("token", out _));
            Assert.Equal(25, created.RootElement.GetProperty("accommodations_applied").GetProperty("extra_time_percent").GetInt32());
        }
        Assert.DoesNotContain("engine-token-ignored", createdRaw);
        Assert.DoesNotContain(grantId, createdRaw);

        using (var sent = JsonDocument.Parse(_engine.LastRequest!.Body!))
        {
            var accommodations = sent.RootElement.GetProperty("accommodations");
            Assert.Equal(25, accommodations.GetProperty("extra_time_percent").GetInt32());
            Assert.True(accommodations.GetProperty("extended_time").GetBoolean());
            var approval = accommodations.GetProperty("extra_time_approval");
            Assert.Equal(grant.GetProperty("id").GetString(), approval.GetProperty("approval_id").GetString());
            Assert.Equal(admin.UserId, approval.GetProperty("approved_by").GetString());
            Assert.Equal("Dr Placement Admin", approval.GetProperty("approved_by_name").GetString());
            var approvedAt = approval.GetProperty("approved_at").GetString()!;
            Assert.EndsWith("Z", approvedAt);
            Assert.Equal(ParseIso(grant.GetProperty("approvedAt").GetString()!), ParseIso(approvedAt));
        }

        // The engine returning the SAME session id again (a retried create)
        // must not write a second use row.
        _engine.Enqueue(engineSession);
        var retried = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            var use = Assert.Single(await db.PlacementAccommodationUses.ToListAsync());
            Assert.Equal("ses_stubacc0000001", use.SessionId);
            Assert.Equal(grant.GetProperty("id").GetString(), use.AccommodationId);
            Assert.Equal(learner.LearnerId, use.LearnerUserId);
            Assert.Equal(25, use.ExtraTimePercent);
        }

        // The admin listing shows which attempt used the grant.
        var list = await AdminGetAsync(admin, $"{AccommodationsRoute}?learner={Uri.EscapeDataString(learner.LearnerId)}");
        var uses = list[0].GetProperty("uses");
        Assert.Equal(1, uses.GetArrayLength());
        Assert.Equal("ses_stubacc0000001", uses[0].GetProperty("sessionId").GetString());
        Assert.Equal(25, uses[0].GetProperty("extraTimePercent").GetInt32());
        Assert.Equal(JsonValueKind.String, uses[0].GetProperty("appliedAt").ValueKind);
    }

    [Fact]
    public async Task SessionCreate_WithoutGrant_SendsNoAccommodations()
    {
        await RegisterLearnerAsync();
        _engine.Enqueue("""
            {"session_id":"ses_stubnone000001","token":"engine-token-ignored","next_step":"worked_example","ruleset_version":"2.0.0-beta"}
            """);

        var response = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = _engine.LastRequest!.Body!;
        Assert.DoesNotContain("accommodations", body);
        Assert.DoesNotContain("extra_time", body);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await db.PlacementAccommodationUses.CountAsync());
    }

    [Fact]
    public async Task SessionCreate_IgnoresClientSuppliedAccommodations()
    {
        var learner = await RegisterLearnerAsync();
        var spoof = new
        {
            targetGoal = "OET",
            accommodations = new { extra_time_percent = 100, extended_time = true },
            extraTimePercent = 100,
            extended_time = true,
        };

        // No grant: nothing the client sends reaches the engine.
        _engine.Enqueue("""
            {"session_id":"ses_stubspoof00001","token":"engine-token-ignored","next_step":"worked_example","ruleset_version":"2.0.0-beta"}
            """);
        var withoutGrant = await _client.PostAsJsonAsync("/v1/placement/session", spoof);
        Assert.Equal(HttpStatusCode.OK, withoutGrant.StatusCode);
        var body = _engine.LastRequest!.Body!;
        Assert.DoesNotContain("accommodations", body);
        Assert.DoesNotContain("extra_time", body);
        Assert.DoesNotContain("extended_time", body);

        // With a 25% grant the engine sees the ADMIN'S 25, never the client's 100.
        var admin = await IssueAdminAsync("Dr Placement Admin");
        var grant = await AdminGrantAsync(admin, learner, 25);
        Authenticate(learner.AccessToken);
        _engine.Enqueue(CreatedSessionJson("ses_stubspoof00002", 25, grant.GetProperty("id").GetString()!));
        var withGrant = await _client.PostAsJsonAsync("/v1/placement/session", spoof);
        Assert.Equal(HttpStatusCode.OK, withGrant.StatusCode);
        using var sent = JsonDocument.Parse(_engine.LastRequest!.Body!);
        Assert.Equal(25, sent.RootElement.GetProperty("accommodations").GetProperty("extra_time_percent").GetInt32());
        Assert.False(sent.RootElement.TryGetProperty("extraTimePercent", out _));
    }

    [Fact]
    public async Task SessionCreate_DoesNotForwardRevokedGrants()
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");
        var grant = await AdminGrantAsync(admin, learner, 25);
        var revoke = await AdminPostAsync(admin,
            $"{AccommodationsRoute}/{grant.GetProperty("id").GetString()}/revoke", new { reason = "withdrawn" });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Authenticate(learner.AccessToken);

        _engine.Enqueue("""
            {"session_id":"ses_stubrevoked001","token":"engine-token-ignored","next_step":"worked_example","ruleset_version":"2.0.0-beta"}
            """);
        var response = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("accommodations", _engine.LastRequest!.Body!);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await db.PlacementAccommodationUses.CountAsync());

        var status = await _client.GetFromJsonAsync<JsonElement>("/v1/placement/status");
        Assert.Equal(JsonValueKind.Null, status.GetProperty("extraTimePercent").ValueKind);
    }

    [Fact]
    public async Task Status_ShowsApprovedExtraTime_ButNeverTheApprover()
    {
        var learner = await RegisterLearnerAsync();
        var before = await _client.GetFromJsonAsync<JsonElement>("/v1/placement/status");
        Assert.Equal(JsonValueKind.Null, before.GetProperty("extraTimePercent").ValueKind);

        var admin = await IssueAdminAsync("Dr Placement Admin");
        await AdminGrantAsync(admin, learner, 40);
        Authenticate(learner.AccessToken);

        var raw = await _client.GetStringAsync("/v1/placement/status");
        using var status = JsonDocument.Parse(raw);
        Assert.Equal(40, status.RootElement.GetProperty("extraTimePercent").GetInt32());
        Assert.DoesNotContain(admin.UserId, raw);
        Assert.DoesNotContain("Dr Placement Admin", raw);
        Assert.DoesNotContain("approv", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionState_StripsTheApprover_ButKeepsTheExtraTime()
    {
        await RegisterLearnerAsync();
        // The engine echoes the stored accommodations (with the approving
        // admin's id + name) on GET session state — both key spellings here.
        _engine.Enqueue("""
            {"session_id":"ses_stubstate000001","state_version":3,"ls_status":"not_started","accommodations":{"extendedTime":true,"extraTimePercent":40,"extraTimeApproval":{"approvalId":"pacc_stubapproval","approvedBy":"admin-user-123","approvedByName":"Dr Placement Admin","approvedAt":"2026-09-20T00:00:00Z"},"extra_time_approval":{"approval_id":"pacc_stubapproval","approved_by":"admin-user-123","approved_by_name":"Dr Placement Admin"}}}
            """);

        var raw = await _client.GetStringAsync("/v1/placement/session/ses_stubstate000001");

        using var state = JsonDocument.Parse(raw);
        Assert.Equal("ses_stubstate000001", state.RootElement.GetProperty("session_id").GetString());
        Assert.Equal(3, state.RootElement.GetProperty("state_version").GetInt32());
        var accommodations = state.RootElement.GetProperty("accommodations");
        Assert.Equal(40, accommodations.GetProperty("extraTimePercent").GetInt32());
        Assert.True(accommodations.GetProperty("extendedTime").GetBoolean());
        Assert.False(accommodations.TryGetProperty("extraTimeApproval", out _));
        Assert.False(accommodations.TryGetProperty("extra_time_approval", out _));
        Assert.DoesNotContain("admin-user-123", raw);
        Assert.DoesNotContain("Dr Placement Admin", raw);
        Assert.DoesNotContain("pacc_stubapproval", raw);
        Assert.DoesNotContain("approv", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionState_WithoutAccommodations_IsReturnedUnchanged()
    {
        await RegisterLearnerAsync();
        _engine.Enqueue("""{"session_id":"ses_stubstate000002","state_version":1,"ls_status":"not_started"}""");

        var raw = await _client.GetStringAsync("/v1/placement/session/ses_stubstate000002");

        using var state = JsonDocument.Parse(raw);
        Assert.Equal("ses_stubstate000002", state.RootElement.GetProperty("session_id").GetString());
        Assert.False(state.RootElement.TryGetProperty("accommodations", out _));
    }

    [Fact]
    public async Task SessionCreate_WhenTheUseRecordCannotBeSaved_StillReturnsTheCreatedSession()
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");
        var grant = await AdminGrantAsync(admin, learner, 25);
        Authenticate(learner.AccessToken);

        _engine.Enqueue(CreatedSessionJson("ses_stubnorecord001", 25, grant.GetProperty("id").GetString()!));
        _failUseSave.Armed = true;

        // The engine already created the session with extra time applied, so
        // the learner request must succeed (a retry would mint a second one).
        var response = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ses_stubnorecord001", created.GetProperty("session_id").GetString());
        Assert.True(_failUseSave.Tripped);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await db.PlacementAccommodationUses.CountAsync());
    }

    [Theory]
    [InlineData("missing")]        // older engine: no accommodations_applied at all
    [InlineData("null")]           // engine applied no extra time
    [InlineData("wrong_percent")]  // engine applied a different percentage
    [InlineData("wrong_approval")] // engine echoed a different approval
    public async Task SessionCreate_WithActiveGrant_FailsClosed_WhenTheEngineDoesNotEchoTheGrant(string echo)
    {
        var learner = await RegisterLearnerAsync();
        var admin = await IssueAdminAsync("Dr Placement Admin");
        var grant = await AdminGrantAsync(admin, learner, 25);
        var grantId = grant.GetProperty("id").GetString()!;
        Authenticate(learner.AccessToken);

        _engine.Enqueue(echo switch
        {
            "missing" => """{"session_id":"ses_stubfail0000001","token":"engine-token-ignored","next_step":"worked_example","ruleset_version":"2.0.0-beta"}""",
            "null" => CreatedSessionJson("ses_stubfail0000001"),
            "wrong_percent" => CreatedSessionJson("ses_stubfail0000001", 50, grantId),
            "wrong_approval" => CreatedSessionJson("ses_stubfail0000001", 25, "pacc_someoneelse"),
            _ => throw new ArgumentOutOfRangeException(nameof(echo), echo, null),
        });

        var response = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });

        // Never a silent standard-clock attempt for a learner with approved extra time.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        using (var problem = JsonDocument.Parse(raw))
        {
            Assert.Equal("placement_accommodation_not_applied", problem.RootElement.GetProperty("code").GetString());
            Assert.Contains("extra time", problem.RootElement.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        }
        // Neither the engine session nor its bearer is handed to the learner.
        Assert.DoesNotContain("ses_stubfail0000001", raw);
        Assert.DoesNotContain("engine-token-ignored", raw);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            Assert.Equal(0, await db.PlacementAccommodationUses.CountAsync());
        }

        // A greppable ERROR carries the session, the grant and what was expected.
        var logged = Assert.Single(
            _logs.Entries,
            e => e.Level == LogLevel.Error && e.Message.Contains("PLACEMENT_ACCOMMODATION_NOT_APPLIED", StringComparison.Ordinal));
        Assert.Contains("ses_stubfail0000001", logged.Message);
        Assert.Contains(grantId, logged.Message);
        Assert.Contains("25", logged.Message);
    }

    [Fact]
    public async Task SessionCreate_WithoutGrant_IgnoresTheEngineEcho()
    {
        await RegisterLearnerAsync();
        // No grant exists, so a stray echo must neither fail the create nor
        // produce a use record.
        _engine.Enqueue(CreatedSessionJson("ses_stubbogus000001", 50, "pacc_bogus"));

        var response = await _client.PostAsJsonAsync("/v1/placement/session", new { targetGoal = "OET" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ses_stubbogus000001", created.GetProperty("session_id").GetString());
        Assert.False(created.TryGetProperty("token", out _));
        Assert.DoesNotContain("accommodations", _engine.LastRequest!.Body!);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        Assert.Equal(0, await db.PlacementAccommodationUses.CountAsync());
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("PLACEMENT_ACCOMMODATION_NOT_APPLIED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewerSurface_RequiresAReviewPermission_NotJustAnyAdmin()
    {
        // Learner-admin permissions only (no review_ops, no system_admin).
        var admin = await IssueAdminAsync("Dr Learner Admin");
        Authenticate(admin.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/v1/admin/placement/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/v1/admin/placement/inventory")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/v1/admin/placement/review/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/v1/admin/placement/review/ses_x")).StatusCode);
        // Authorization runs before taskId validation: no 400 detail for an unauthorised caller.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/v1/admin/placement/review/ses_x?taskId=bad%20id")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/v1/admin/placement/review/ses_x/audio/task_x")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _client.PostAsJsonAsync("/v1/admin/placement/review/ses_x/rescore", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _client.PostAsJsonAsync("/v1/admin/placement/review/ses_x/human-score", new { })).StatusCode);

        // The same route opens for a reviewer holding review_ops.
        var reviewer = await IssueAdminAsync("Dr Reviewer", AdminPermissions.ReviewOps);
        Authenticate(reviewer.AccessToken);
        _engine.Enqueue("[]");
        var queue = await _client.GetAsync("/v1/admin/placement/review/queue");
        Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
    }

    // ── Per-task review proxy ────────────────────────────────────────

    private const string ReviewSessionRoute = "/v1/admin/placement/review/ses_stubreview01";

    private const string ReviewSessionJson =
        """{"session_id":"ses_stubreview01","task_id":"SPK-2","module":"SPK","task_type":"role_play","traits":["fluency","intelligibility"],"tasks":[{"task_id":"SPK-1","module":"SPK","task_type":"listen_repeat","status":"rated","has_recording":true},{"task_id":"SPK-2","module":"SPK","task_type":"role_play","status":"pending_review","has_recording":true}]}""";

    [Theory]
    [InlineData("SPK-2")]
    [InlineData("task_1.a-B")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 64 chars, the maximum
    public async Task AdminReviewSession_ForwardsTheTaskIdQuery_AndPassesTheResponseThrough(string taskId)
    {
        var reviewer = await IssueAdminAsync("Dr Reviewer", AdminPermissions.ReviewOps);
        _engine.Enqueue(ReviewSessionJson);
        Authenticate(reviewer.AccessToken);

        var response = await _client.GetAsync($"{ReviewSessionRoute}?taskId={Uri.EscapeDataString(taskId)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = _engine.LastRequest!.Request.RequestUri!;
        Assert.Equal("/api/review/sessions/ses_stubreview01", sent.AbsolutePath);
        Assert.Equal($"?task_id={taskId}", sent.Query);

        // The engine payload, including the per-task fields, reaches the console as sent.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SPK-2", body.GetProperty("task_id").GetString());
        Assert.Equal(2, body.GetProperty("traits").GetArrayLength());
        var tasks = body.GetProperty("tasks");
        Assert.Equal(2, tasks.GetArrayLength());
        Assert.Equal("pending_review", tasks[1].GetProperty("status").GetString());
        Assert.True(tasks[1].GetProperty("has_recording").GetBoolean());
    }

    [Fact]
    public async Task AdminReviewSession_WithoutTaskId_SendsNoQueryToTheEngine()
    {
        var reviewer = await IssueAdminAsync("Dr Reviewer", AdminPermissions.ReviewOps);
        _engine.Enqueue(ReviewSessionJson);
        Authenticate(reviewer.AccessToken);

        var response = await _client.GetAsync(ReviewSessionRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = _engine.LastRequest!.Request.RequestUri!;
        Assert.Equal("/api/review/sessions/ses_stubreview01", sent.AbsolutePath);
        Assert.Equal(string.Empty, sent.Query);
    }

    public static TheoryData<string> InvalidReviewTaskIds() => new()
    {
        "bad id",
        "../secret",
        "task?x=1",
        "a,b",
        "über",
        "trailing\n", // a "$" anchor would let this through
        new string('a', 65),
    };

    [Theory]
    [MemberData(nameof(InvalidReviewTaskIds))]
    public async Task AdminReviewSession_RejectsAMalformedTaskId_WithoutCallingTheEngine(string taskId)
    {
        var reviewer = await IssueAdminAsync("Dr Reviewer", AdminPermissions.ReviewOps);
        Authenticate(reviewer.AccessToken);

        var response = await _client.GetAsync($"{ReviewSessionRoute}?taskId={Uri.EscapeDataString(taskId)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("placement_review_task_invalid", problem.GetProperty("code").GetString());
        Assert.Null(_engine.LastRequest);
    }

    // ── Engine failure mapping ───────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "placement_engine_rejected")]
    [InlineData(HttpStatusCode.Conflict, "placement_stale_submission")]
    public async Task EngineRejections_AreMappedToGenericMessages_NeverTheEngineBody(HttpStatusCode engineStatus, string expectedCode)
    {
        await RegisterLearnerAsync();
        const string internalDetail = "internal detail /srv/engine/handlers.rs line 42";
        _engine.Enqueue("{\"error\":\"" + internalDetail + "\"}", engineStatus);

        var response = await _client.GetAsync("/v1/placement/session/ses_stubmapping01");

        Assert.Equal(engineStatus, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("internal detail", raw);
        Assert.DoesNotContain("handlers.rs", raw);
        using var problem = JsonDocument.Parse(raw);
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EngineForbidden_OnTheReviewerSurface_DoesNotClaimTheSessionBelongsToAnotherAccount()
    {
        await RegisterLearnerAsync();

        // Learner session route: ownership wording is accurate there.
        _engine.Enqueue("{}", HttpStatusCode.Forbidden);
        var learnerCall = await _client.GetAsync("/v1/placement/session/ses_stubmapping02");
        Assert.Equal(HttpStatusCode.Forbidden, learnerCall.StatusCode);
        var learnerProblem = await learnerCall.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("placement_not_your_session", learnerProblem.GetProperty("code").GetString());
        Assert.Contains("another account", learnerProblem.GetProperty("message").GetString()!);

        // Reviewer/admin route: the service token was refused, not a session ownership check.
        var reviewer = await IssueAdminAsync("Dr Reviewer", AdminPermissions.ReviewOps);
        Authenticate(reviewer.AccessToken);
        _engine.Enqueue("{}", HttpStatusCode.Forbidden);
        var reviewCall = await _client.GetAsync("/v1/admin/placement/review/queue");
        Assert.Equal(HttpStatusCode.Forbidden, reviewCall.StatusCode);
        var reviewProblem = await reviewCall.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("placement_engine_forbidden", reviewProblem.GetProperty("code").GetString());
        var message = reviewProblem.GetProperty("message").GetString()!;
        Assert.DoesNotContain("another account", message);
        Assert.DoesNotContain("belongs", message);
    }

    // ── helpers ──────────────────────────────────────────────────────

    /// <summary>A create-session body as the engine now emits it: the
    /// <c>accommodations_applied</c> echo is null unless a percent and approval
    /// id are given. Includes the engine bearer <c>token</c> the proxy must strip.</summary>
    private static string CreatedSessionJson(string sessionId, int? extraTimePercent = null, string? approvalId = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["token"] = "engine-token-ignored",
            ["next_step"] = "worked_example",
            ["ruleset_version"] = "2.0.0-beta",
            ["accommodations_applied"] = extraTimePercent is null
                ? null
                : new { extra_time_percent = extraTimePercent, approval_id = approvalId },
        });

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

    private sealed record AdminIdentity(string UserId, string AccessToken, string DisplayName);

    private void Authenticate(string accessToken)
        => _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>Seeds a verified admin account and issues a real access token
    /// for it (same route the production JWT pipeline validates), carrying the
    /// learner-admin permissions the accommodation routes require (or exactly
    /// <paramref name="permissions"/> when given).</summary>
    private async Task<AdminIdentity> IssueAdminAsync(string displayName, params string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var authAccountId = $"auth_admin_{Guid.NewGuid():N}";
        var email = $"placement.admin.{Guid.NewGuid():N}@example.com";
        db.ApplicationUserAccounts.Add(new ApplicationUserAccount
        {
            Id = authAccountId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "not-used",
            Role = ApplicationUserRoles.Admin,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var accessToken = scope.ServiceProvider.GetRequiredService<AuthTokenService>().IssueSession(
            new AuthenticatedSessionSubject(
                authAccountId,
                authAccountId,
                email,
                ApplicationUserRoles.Admin,
                displayName,
                IsEmailVerified: true,
                IsAuthenticatorEnabled: false,
                RequiresEmailVerification: false,
                RequiresMfa: false,
                EmailVerifiedAt: now,
                AuthenticatorEnabledAt: null,
                AdminPermissions: permissions.Length > 0
                    ? permissions
                    : new[] { AdminPermissions.LearnerRead, AdminPermissions.LearnerWrite })).AccessToken;
        return new AdminIdentity(authAccountId, accessToken, displayName);
    }

    private async Task<HttpResponseMessage> AdminPostAsync(AdminIdentity admin, string url, object? body)
    {
        Authenticate(admin.AccessToken);
        return body is null
            ? await _client.PostAsync(url, null)
            : await _client.PostAsJsonAsync(url, body);
    }

    private async Task<JsonElement> AdminGetAsync(AdminIdentity admin, string url)
    {
        Authenticate(admin.AccessToken);
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Admin grants extra time to <paramref name="learner"/>; returns
    /// the 201 DTO. Leaves the client authenticated as the admin.</summary>
    private async Task<JsonElement> AdminGrantAsync(AdminIdentity admin, LearnerIdentity learner, int percent)
    {
        var response = await AdminPostAsync(admin, AccommodationsRoute, new
        {
            learnerUserId = learner.LearnerId,
            extraTimePercent = percent,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static DateTimeOffset ParseIso(string value)
        => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Moves the test clock forward so two grants never share a
    /// timestamp (the stub time provider is frozen at factory start).</summary>
    private async Task AdvanceClockAsync()
    {
        if (_factory.Services.GetRequiredService<TimeProvider>() is MutableTimeProvider clock)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
        }
        await Task.Delay(20);
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
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        /// <summary>The body string is captured INSIDE the handler: HttpClient
        /// disposes request content after send, so the test cannot read it later.</summary>
        public CapturedRequest? LastRequest { get; private set; }

        public void Enqueue(string json, HttpStatusCode status = HttpStatusCode.OK) => _responses.Enqueue((status, json));

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
            var (status, json) = _responses.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Throws when a save would insert a <see cref="PlacementAccommodationUse"/>,
    /// once armed — simulates the OET-side use record failing to persist after
    /// the engine already created the session.</summary>
    private sealed class FailAccommodationUseSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Tripped { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed
                && eventData.Context is { } context
                && context.ChangeTracker.Entries<PlacementAccommodationUse>().Any(e => e.State == EntityState.Added))
            {
                Tripped = true;
                throw new InvalidOperationException("Simulated accommodation-use persistence failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>Keeps every formatted log entry so a test can assert an
    /// operational alarm string (e.g. PLACEMENT_ACCOMMODATION_NOT_APPLIED) fired.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Enqueue((logLevel, formatter(state, exception)));
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
