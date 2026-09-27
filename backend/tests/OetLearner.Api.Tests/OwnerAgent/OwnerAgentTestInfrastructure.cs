using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using OetLearner.Api.Services.OwnerAgent;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.OwnerAgent;

/// <summary>
/// Development-environment test host (dev-auth headers AND real JWTs both work — the
/// hybrid scheme forwards "Bearer" requests to JwtBearer) with the Owner Agent Console
/// configured against an in-process fake sidecar. No network, no real engines, no
/// real credentials: every token/password below is a throwaway test literal.
/// </summary>
public sealed class OwnerAgentWebApplicationFactory : TestWebApplicationFactory
{
    public const string OwnerAccountId = "auth_owner_agent_test_owner";
    public const string SecondOwnerAccountId = "auth_owner_agent_test_owner_b";
    public const string OwnerPassword = "Owner-Agent-Test-Passw0rd!";

    /// <summary>Fake control token for the fake sidecar (≥ 32 chars, not a secret).</summary>
    public const string InternalToken = "owner-agent-test-control-token-not-a-secret-000";

    public const string SidecarBaseUrl = "http://oet-agent-console.test:8410";

    public FakeSidecarHandler Sidecar { get; } = new();

    public OwnerAgentRecordingEmailSender Emails { get; } = new();

    /// <summary>Env switch; set before the first request.</summary>
    public bool OwnerAgentEnabled { get; init; } = true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<OwnerAgentOptions>(options =>
            {
                options.Enabled = OwnerAgentEnabled;
                options.BaseUrl = SidecarBaseUrl;
                options.InternalToken = InternalToken;
                options.OwnerAccountIds = $"{OwnerAccountId}, {SecondOwnerAccountId}";
            });

            // Same typed client, primary handler swapped for the in-process fake.
            services.AddHttpClient<OwnerAgentClient>().ConfigurePrimaryHttpMessageHandler(() => Sidecar);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public async Task SetFeatureFlagAsync(bool enabled)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var flag = await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == OwnerAgentFeatureGate.FlagKey);
        if (flag is null)
        {
            flag = new FeatureFlag
            {
                Id = $"ff-owner-agent-{Guid.NewGuid():N}",
                Name = "Owner Agent Console",
                Key = OwnerAgentFeatureGate.FlagKey,
                CreatedAt = now,
            };
            db.FeatureFlags.Add(flag);
        }

        flag.Enabled = enabled;
        flag.UpdatedAt = now;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds an admin account with password, a TOTP authenticator (optionally enabled),
    /// eight recovery codes and one live refresh-token family, and mints an access token
    /// bound to that family (<c>sfam</c>).
    /// </summary>
    public async Task<OwnerSeed> SeedOwnerAsync(
        string accountId = OwnerAccountId,
        bool authenticatorEnabled = true,
        string[]? permissions = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUserAccount>>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("AuthService.AuthenticatorSecret");
        var now = DateTimeOffset.UtcNow;
        var email = $"{accountId}@example.test";
        var secret = AuthenticatorTotp.GenerateSecretKey();
        var recoveryCodes = AuthenticatorTotp.GenerateRecoveryCodes();

        var account = await db.ApplicationUserAccounts.SingleOrDefaultAsync(a => a.Id == accountId);
        if (account is null)
        {
            account = new ApplicationUserAccount { Id = accountId, CreatedAt = now };
            db.ApplicationUserAccounts.Add(account);
        }

        account.Email = email;
        account.NormalizedEmail = email.ToUpperInvariant();
        account.Role = ApplicationUserRoles.Admin;
        account.EmailVerifiedAt = now;
        account.PasswordHash = hasher.HashPassword(account, OwnerPassword);
        account.ProtectedAuthenticatorSecret = protector.Protect(secret);
        account.AuthenticatorEnabledAt = authenticatorEnabled ? now : null;
        account.UpdatedAt = now;

        db.MfaRecoveryCodes.AddRange(recoveryCodes.Select(code => new MfaRecoveryCode
        {
            Id = Guid.NewGuid(),
            ApplicationUserAccountId = accountId,
            CodeHash = AuthenticatorTotp.HashRecoveryCode(code),
            CreatedAt = now,
        }));
        await db.SaveChangesAsync();

        var (familyId, token) = await MintSessionAsync(accountId, email, ApplicationUserRoles.Admin, permissions ?? [AdminPermissions.SystemAdmin]);
        return new OwnerSeed(accountId, email, OwnerPassword, secret, recoveryCodes, familyId, token);
    }

    /// <summary>A fresh refresh-token family + access token for an existing account.</summary>
    public async Task<(Guid FamilyId, string AccessToken)> MintSessionAsync(
        string accountId,
        string email,
        string role,
        string[]? permissions,
        Guid? familyId = null,
        bool emailVerified = true)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        var now = DateTimeOffset.UtcNow;
        var family = familyId ?? Guid.NewGuid();
        if (!await db.RefreshTokenRecords.AnyAsync(r => r.FamilyId == family))
        {
            db.RefreshTokenRecords.Add(new RefreshTokenRecord
            {
                Id = Guid.NewGuid(),
                ApplicationUserAccountId = accountId,
                TokenHash = $"owner-agent-test-{Guid.NewGuid():N}",
                FamilyId = family,
                CreatedAt = now,
                ExpiresAt = now.AddDays(1),
            });
            await db.SaveChangesAsync();
        }

        var tokenService = scope.ServiceProvider.GetRequiredService<AuthTokenService>();
        var accessToken = tokenService.IssueSession(
            new AuthenticatedSessionSubject(
                accountId,
                accountId,
                email,
                role,
                "Owner Agent Test",
                IsEmailVerified: emailVerified,
                IsAuthenticatorEnabled: true,
                RequiresEmailVerification: false,
                RequiresMfa: false,
                EmailVerifiedAt: now,
                AuthenticatorEnabledAt: now,
                AdminPermissions: permissions),
            Guid.NewGuid(),
            family).AccessToken;
        return (family, accessToken);
    }

    public async Task RevokeFamilyAsync(Guid familyId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
        foreach (var record in await db.RefreshTokenRecords.Where(r => r.FamilyId == familyId).ToListAsync())
        {
            record.RevokedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public HttpClient CreateBearerClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>Dev-auth identity (no <c>sfam</c> claim — such a principal can never unlock).</summary>
    public HttpClient CreateDevClient(string userId, string role, string? adminPermissions = null, bool emailVerified = true)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Debug-UserId", userId);
        client.DefaultRequestHeaders.Add("X-Debug-Role", role);
        client.DefaultRequestHeaders.Add("X-Debug-Email", $"{userId}@example.test");
        client.DefaultRequestHeaders.Add("X-Debug-EmailVerified", emailVerified ? "true" : "false");
        if (!string.IsNullOrWhiteSpace(adminPermissions))
        {
            client.DefaultRequestHeaders.Add("X-Debug-AdminPermissions", adminPermissions);
        }

        return client;
    }

    /// <summary>POST /unlock with password + the TOTP for <paramref name="codeTime"/> (default now).</summary>
    public static async Task<string> UnlockAsync(HttpClient client, OwnerSeed owner, DateTimeOffset? codeTime = null)
    {
        var response = await client.PostAsJsonAsync("/v1/owner-agent/unlock", new
        {
            password = owner.Password,
            code = GenerateTotpCode(owner.SecretKey, codeTime ?? DateTimeOffset.UtcNow),
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"unlock failed: {(int)response.StatusCode} {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("ticket").GetString()!;
    }

    /// <summary>POST /step-up with the NEXT time-step's code (the unlock burned the current one).</summary>
    public static async Task<string> StepUpAsync(HttpClient client, OwnerSeed owner, string ticket)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/owner-agent/step-up")
        {
            Content = JsonContent.Create(new { code = GenerateTotpCode(owner.SecretKey, DateTimeOffset.UtcNow.AddSeconds(30)) }),
        };
        request.Headers.Add(OwnerAgentHeaders.Unlock, ticket);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"step-up failed: {(int)response.StatusCode} {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("stepUpToken").GetString()!;
    }

    public static HttpRequestMessage Unlocked(HttpMethod method, string url, string ticket, object? body = null, string? stepUp = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(OwnerAgentHeaders.Unlock, ticket);
        if (stepUp is not null)
        {
            request.Headers.Add(OwnerAgentHeaders.StepUp, stepUp);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    public static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("code", out var code)
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ClaimsPrincipal Principal(string accountId, Guid? familyId = null, string role = ApplicationUserRoles.Admin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accountId),
            new(AuthTokenService.AuthAccountIdClaimType, accountId),
            new(ClaimTypes.Role, role),
            new(ClaimTypes.Name, "Owner Agent Test"),
            new(AuthTokenService.IsEmailVerifiedClaimType, "true"),
            new(AuthTokenService.AdminPermissionsClaimType, AdminPermissions.SystemAdmin),
        };
        if (familyId is not null)
        {
            claims.Add(new Claim(AuthTokenService.SessionFamilyClaimType, familyId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}

public sealed record OwnerSeed(
    string AccountId,
    string Email,
    string Password,
    string SecretKey,
    IReadOnlyList<string> RecoveryCodes,
    Guid FamilyId,
    string AccessToken);

public sealed record CapturedSidecarRequest(
    string Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string> Headers,
    string? Body);

/// <summary>
/// In-process stand-in for the sidecar control server. Records every request (method,
/// path, headers, body) and answers from <see cref="Responder"/> or a small default table
/// that follows CONTRACT.md §3 shapes.
/// </summary>
public sealed class FakeSidecarHandler : HttpMessageHandler
{
    public const string SessionId = "01J9ZX7Q2V3M4N5P6R7S8T9V0W";
    public const string ApprovalId = "01J9ZX7Q2V3M4N5P6R7S8T9V1A";
    public const string FlowId = "01J9ZX7Q2V3M4N5P6R7S8T9V2B";

    private readonly ConcurrentQueue<CapturedSidecarRequest> _requests = new();

    public Func<CapturedSidecarRequest, HttpResponseMessage>? Responder { get; set; }

    public IReadOnlyList<CapturedSidecarRequest> Requests => _requests.ToArray();

    public void Reset()
    {
        _requests.Clear();
        Responder = null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var captured = new CapturedSidecarRequest(request.Method.Method, request.RequestUri!.PathAndQuery, headers, body);
        _requests.Enqueue(captured);
        return (Responder ?? Default)(captured);
    }

    // The same instance is handed to every HttpClientFactory handler rotation; never dispose it.
    protected override void Dispose(bool disposing)
    {
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Sse(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    public static string EventStream(params (long? Seq, string Type, string DataJson)[] events)
    {
        var builder = new StringBuilder();
        builder.Append(": sidecar comment line\n\n");
        foreach (var (seq, type, dataJson) in events)
        {
            var envelope = seq is null
                ? $"{{\"sessionId\":\"{SessionId}\",\"ts\":\"2026-09-27T00:00:00Z\",\"type\":\"{type}\",\"data\":{dataJson}}}"
                : $"{{\"seq\":{seq},\"sessionId\":\"{SessionId}\",\"ts\":\"2026-09-27T00:00:00Z\",\"type\":\"{type}\",\"data\":{dataJson}}}";
            if (seq is not null)
            {
                builder.Append("id: ").Append(seq).Append('\n');
            }

            builder.Append("event: ").Append(type).Append('\n');
            builder.Append("data: ").Append(envelope).Append("\n\n");
        }

        return builder.ToString();
    }

    public static HttpResponseMessage Default(CapturedSidecarRequest request)
    {
        var path = request.PathAndQuery;
        if (path.StartsWith("/v1/status", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """
                {"version":"test","draining":false,"killed":false,"updatePending":false,"activeTurns":0,"maxConcurrentTurns":2,
                 "lease":{"expiresAt":null},
                 "engines":{"claude":{"engine":"claude","version":null,"auth":{"state":"signed_out"},"models":[],"rateLimits":null},
                            "codex":{"engine":"codex","version":null,"auth":{"state":"signed_out"},"models":[],"rateLimits":null}},
                 "github":{"agentTokenSet":false,"shipTokenSet":false}}
                """);
        }

        if (path.StartsWith("/v1/github-tokens", StringComparison.Ordinal))
        {
            // A badly behaved sidecar that echoes the input: the API must not relay it.
            return Json(HttpStatusCode.OK, $$"""
                {"agentTokenSet":true,"shipTokenSet":true,"login":"oet-owner-bot","echo":{{request.Body ?? "null"}} }
                """);
        }

        if (path.StartsWith($"/v1/sessions/{SessionId}/events", StringComparison.Ordinal))
        {
            return Sse(EventStream(
                (1, "turn_started", """{"model":"opaque-model","mode":"read_only"}"""),
                (null, "heartbeat", "{}"),
                (2, "text_delta", """{"messageId":"m1","text":"hello"}"""),
                (3, "turn_complete", """{"status":"ok","durationMs":12}""")));
        }

        if (path.StartsWith("/v1/sessions", StringComparison.Ordinal) && request.Method == "POST" && path == "/v1/sessions")
        {
            return Json(HttpStatusCode.OK, $$"""{"id":"{{SessionId}}","title":"t","engine":"claude","model":"m","mode":"read_only","status":"idle"}""");
        }

        if (path.StartsWith("/v1/admin/apply-update", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """{"draining":true,"activeTurns":1,"dispatched":true}""");
        }

        if (path.StartsWith("/v1/admin/drain", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """{"draining":true,"activeTurns":1}""");
        }

        if (path.StartsWith("/v1/admin/stop-all", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, """{"stoppedTurns":1,"killedProcesses":2}""");
        }

        return Json(HttpStatusCode.OK, """{"ok":true}""");
    }
}

public sealed class OwnerAgentRecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => _messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>Mutable clock for unit tests; starts at the real "now" so time-limited
/// DataProtection payloads (which use the system clock) stay valid.</summary>
public sealed class OwnerAgentTestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
