using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fleet.Manager.Persistence;
using Fleet.Manager.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Fleet.Manager.Tests.Web;

/// <summary>
/// Owner authentication on the real web application (OET-RWP/1 section 8.8): password plus TOTP, lockout, one-use codes, short sessions,
/// antiforgery, security headers, rate limiting. Every test builds its own application so lockout and replay state never leaks.
/// </summary>
public sealed class WebAuthTests : IAsyncLifetime
{
    private FleetWebFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new FleetWebFactory();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static readonly Regex ErrorParagraph = new("<p class=\"error\" role=\"alert\">(?<text>[^<]*)</p>", RegexOptions.CultureInvariant);

    private static string ErrorText(string html)
    {
        var match = ErrorParagraph.Match(html);
        return match.Success ? match.Groups["text"].Value : string.Empty;
    }

    private static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : new List<string>();

    private async Task<bool> AuditedAsync(string action) =>
        (await _factory.Services.GetRequiredService<IAuditService>().ListAsync(200)).Any(r => r.Action == action);

    /// <summary>
    /// A code that is certainly newer than any used so far: every code is accepted once, and the verifier looks one step ahead, so the
    /// test clock moves three steps (90 seconds) before the current step's code is taken.
    /// </summary>
    private string NextCode(string secret)
    {
        _factory.Time.Advance(TimeSpan.FromSeconds(90));
        return _factory.Code(secret);
    }

    private static async Task<HttpResponseMessage> AddHostAsync(HttpClient client, string csrf, string? totp, string nodeRef = "helper-eu-01", string address = "203.0.113.10")
    {
        using var request = FleetWebFactory.ApiRequest(
            HttpMethod.Post,
            "/api/v1/hosts",
            csrf,
            totp,
            new { nodeRef, displayName = "Helper EU 01", address, sshPort = 22, region = "eu-central", provider = "ExampleHost" });
        return await client.SendAsync(request);
    }

    // ---- who may see what ------------------------------------------------------------------

    [Fact]
    public async Task Without_a_session_nothing_but_the_login_page_and_the_minimal_health_probe_is_reachable()
    {
        using var client = _factory.CreateHttps();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/hosts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).StatusCode);

        foreach (var path in new[] { "/Health", "/" })
        {
            var page = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.StartsWith("https://localhost/Login", page.Headers.Location!.ToString());
        }

        var login = await client.GetAsync("/Login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var html = await login.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Password\"", html);
        Assert.Contains("name=\"Code\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain(SetCookies(login), cookie => cookie.StartsWith("fleet.session=", StringComparison.Ordinal));

        var probe = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await probe.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Every_response_carries_the_strict_security_headers()
    {
        using var client = _factory.CreateHttps();

        var response = await client.GetAsync("/healthz");

        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("form-action 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("unsafe-inline", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("unsafe-eval", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control").Single());
    }

    // ---- sign-in ---------------------------------------------------------------------------

    [Fact]
    public async Task A_correct_password_and_code_signs_the_owner_in_with_a_session_only_cookie_and_opens_the_health_page()
    {
        var secret = await _factory.CreateOwnerAsync();
        using var client = _factory.CreateHttps();

        var response = await FleetWebFactory.PostLoginAsync(client, FleetWebFactory.Password, _factory.Code(secret));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("/Health", response.Headers.Location!.ToString());
        var session = SetCookies(response).Single(cookie => cookie.StartsWith("fleet.session=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", session);
        Assert.Contains("secure", session);
        Assert.Contains("samesite=strict", session);
        Assert.Contains("path=/", session);
        Assert.DoesNotContain("expires=", session);
        Assert.DoesNotContain("max-age=", session);

        var health = await client.GetAsync("/Health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var html = await health.Content.ReadAsStringAsync();
        Assert.Contains("All startup and integrity checks passed.", html);
        Assert.Contains("intact", html);
        Assert.Contains("Sign out", html);
        Assert.True(await AuditedAsync("owner.login"));

        // Already signed in: the login page just moves on.
        var again = await client.GetAsync("/Login");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_and_a_wrong_code_fail_identically_and_nothing_typed_is_echoed()
    {
        var secret = await _factory.CreateOwnerAsync();
        using var client = _factory.CreateHttps();
        const string typedPassword = "definitely-not-the-password";

        var wrongPassword = await FleetWebFactory.PostLoginAsync(client, typedPassword, _factory.Code(secret));
        var wrongPasswordHtml = await wrongPassword.Content.ReadAsStringAsync();
        var farCode = _factory.Code(secret, 7);
        var wrongCode = await FleetWebFactory.PostLoginAsync(client, FleetWebFactory.Password, farCode);
        var wrongCodeHtml = await wrongCode.Content.ReadAsStringAsync();
        var neither = await FleetWebFactory.PostLoginAsync(client, string.Empty, string.Empty);
        var neitherHtml = await neither.Content.ReadAsStringAsync();

        Assert.All(new[] { wrongPassword, wrongCode, neither }, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal("Sign-in failed.", ErrorText(wrongPasswordHtml));
        Assert.Equal(ErrorText(wrongPasswordHtml), ErrorText(wrongCodeHtml));
        Assert.Equal(ErrorText(wrongPasswordHtml), ErrorText(neitherHtml));
        Assert.DoesNotContain(typedPassword, wrongPasswordHtml);
        Assert.DoesNotContain(FleetWebFactory.Password, wrongCodeHtml);
        Assert.DoesNotContain(farCode, wrongCodeHtml);
        Assert.All(new[] { wrongPassword, wrongCode, neither }, response => Assert.DoesNotContain(SetCookies(response), cookie => cookie.StartsWith("fleet.session=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Without_an_owner_account_nobody_can_sign_in_and_the_answer_is_the_same()
    {
        _ = _factory.Server;
        using var client = _factory.CreateHttps();

        var response = await FleetWebFactory.PostLoginAsync(client, FleetWebFactory.Password, "123456");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Sign-in failed.", ErrorText(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Five_failures_lock_the_owner_out_for_fifteen_minutes_even_for_the_right_credentials()
    {
        var secret = await _factory.CreateOwnerAsync();
        using var client = _factory.CreateHttps();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var failed = await FleetWebFactory.PostLoginAsync(client, "wrong password number " + attempt, _factory.Code(secret));
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var fifth = await FleetWebFactory.PostLoginAsync(client, "wrong password number 5", _factory.Code(secret));
        Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);
        Assert.Equal("Too many attempts. Try again later.", ErrorText(await fifth.Content.ReadAsStringAsync()));
        Assert.True(await AuditedAsync("owner.locked"));

        var correctButLocked = await FleetWebFactory.PostLoginAsync(client, FleetWebFactory.Password, _factory.Code(secret));
        Assert.Equal(HttpStatusCode.TooManyRequests, correctButLocked.StatusCode);

        _factory.Time.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await FleetWebFactory.PostLoginAsync(client, FleetWebFactory.Password, _factory.Code(secret))).StatusCode);

        _factory.Time.Advance(TimeSpan.FromMinutes(2));
        var unlocked = await FleetWebFactory.PostLoginAsync(client, FleetWebFactory.Password, _factory.Code(secret));
        Assert.Equal(HttpStatusCode.Redirect, unlocked.StatusCode);
    }

    [Fact]
    public async Task A_code_that_signed_somebody_in_cannot_sign_anybody_in_again()
    {
        var secret = await _factory.CreateOwnerAsync();
        var code = _factory.Code(secret);
        using var first = _factory.CreateHttps();
        Assert.Equal(HttpStatusCode.Redirect, (await FleetWebFactory.PostLoginAsync(first, FleetWebFactory.Password, code)).StatusCode);

        using var shoulderSurfer = _factory.CreateHttps();
        var replay = await FleetWebFactory.PostLoginAsync(shoulderSurfer, FleetWebFactory.Password, code);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await shoulderSurfer.GetAsync("/api/v1/hosts")).StatusCode);

        // The next step's code is fine, exactly once.
        var next = _factory.Code(secret, 1);
        Assert.Equal(HttpStatusCode.Redirect, (await FleetWebFactory.PostLoginAsync(shoulderSurfer, FleetWebFactory.Password, next)).StatusCode);
        using var again = _factory.CreateHttps();
        Assert.Equal(HttpStatusCode.Unauthorized, (await FleetWebFactory.PostLoginAsync(again, FleetWebFactory.Password, next)).StatusCode);
    }

    [Fact]
    public async Task The_login_form_is_protected_by_antiforgery()
    {
        var secret = await _factory.CreateOwnerAsync();
        using var client = _factory.CreateHttps();
        await client.GetAsync("/Login");

        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Password"] = FleetWebFactory.Password,
            ["Code"] = _factory.Code(secret),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/hosts")).StatusCode);
    }

    [Fact]
    public async Task The_login_is_rate_limited_per_source_before_the_password_is_even_checked()
    {
        await using var limited = new FleetWebFactory(settings => settings["Fleet:Auth:LoginRatePerMinute"] = "3");
        var secret = await limited.CreateOwnerAsync();
        using var client = limited.CreateHttps();

        // GET + POST of the first attempt use two of the three permits; the next page load uses the third; the next POST is refused.
        var first = await FleetWebFactory.PostLoginAsync(client, "first wrong password", limited.Code(secret));
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        var limitedResponse = await FleetWebFactory.PostLoginAsync(client, FleetWebFactory.Password, limited.Code(secret));

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.DoesNotContain("Sign-in failed.", await limitedResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/hosts")).StatusCode);
    }

    // ---- the session -----------------------------------------------------------------------

    [Fact]
    public async Task A_session_dies_an_hour_after_sign_in_however_active_it_was()
    {
        var (client, _, _) = await _factory.SignedInAsync();
        using (client)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/hosts")).StatusCode);

            _factory.Time.Advance(TimeSpan.FromMinutes(59));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/hosts")).StatusCode);

            _factory.Time.Advance(TimeSpan.FromMinutes(2));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/hosts")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Health")).StatusCode);
        }
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_needs_the_antiforgery_token()
    {
        var (client, _, _) = await _factory.SignedInAsync();
        using (client)
        {
            var unprotected = await client.PostAsync("/Logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Equal(HttpStatusCode.BadRequest, unprotected.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/hosts")).StatusCode);

            var token = await FleetWebFactory.AntiforgeryFieldAsync(client, "/Health");
            var signedOut = await client.PostAsync("/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

            Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
            Assert.EndsWith("/Login", signedOut.Headers.Location!.ToString());
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/hosts")).StatusCode);
        }
    }

    // ---- privileged actions ----------------------------------------------------------------

    [Fact]
    public async Task A_privileged_call_needs_an_authenticator_code_newer_than_the_last_one_used_and_each_code_works_once()
    {
        var (client, secret, csrf) = await _factory.SignedInAsync();
        using (client)
        {
            // The code that just signed the owner in is spent.
            var spent = await AddHostAsync(client, csrf, _factory.Code(secret));
            Assert.Equal(HttpStatusCode.Forbidden, spent.StatusCode);
            Assert.Contains("step_up_required", await spent.Content.ReadAsStringAsync());

            var fresh = _factory.Code(secret, 1);
            var accepted = await AddHostAsync(client, csrf, fresh);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

            var replay = await AddHostAsync(client, csrf, fresh, nodeRef: "helper-eu-02", address: "203.0.113.11");
            Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);

            // Missing and malformed codes (two more failures: five in a row would lock the account, see the next test).
            foreach (var missing in new string?[] { null, "12 456" })
            {
                var response = await AddHostAsync(client, csrf, missing, nodeRef: "helper-eu-02", address: "203.0.113.11");
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            var next = await AddHostAsync(client, csrf, NextCode(secret), nodeRef: "helper-eu-02", address: "203.0.113.11");
            Assert.Equal(HttpStatusCode.Created, next.StatusCode);
        }
    }

    [Fact]
    public async Task Repeated_bad_step_up_codes_lock_the_account_and_a_valid_code_stops_working_until_the_lockout_ends()
    {
        var (client, secret, csrf) = await _factory.SignedInAsync();
        using (client)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await AddHostAsync(client, csrf, "000000")).StatusCode);
            }

            Assert.True(await AuditedAsync("owner.locked"));
            Assert.Equal(HttpStatusCode.Forbidden, (await AddHostAsync(client, csrf, NextCode(secret))).StatusCode);

            _factory.Time.Advance(TimeSpan.FromMinutes(16));
            Assert.Equal(HttpStatusCode.Created, (await AddHostAsync(client, csrf, NextCode(secret))).StatusCode);
        }
    }

    [Fact]
    public async Task A_state_changing_call_without_the_antiforgery_header_is_refused_before_any_code_is_used()
    {
        var (client, secret, csrf) = await _factory.SignedInAsync();
        using (client)
        {
            var code = NextCode(secret);

            using (var noHeader = new HttpRequestMessage(HttpMethod.Post, "/api/v1/hosts"))
            {
                noHeader.Headers.Add("X-Fleet-Totp", code);
                noHeader.Content = JsonContent.Create(new { nodeRef = "helper-eu-01", displayName = "H", address = "203.0.113.10", sshPort = 22 });
                var refused = await client.SendAsync(noHeader);
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                Assert.Contains("csrf_invalid", await refused.Content.ReadAsStringAsync());
            }

            using (var wrongToken = FleetWebFactory.ApiRequest(HttpMethod.Post, "/api/v1/hosts", "not-a-valid-token", code, new { nodeRef = "helper-eu-01" }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(wrongToken)).StatusCode);
            }

            // The code was never consumed, so it still works with the right header.
            Assert.Equal(HttpStatusCode.Created, (await AddHostAsync(client, csrf, code)).StatusCode);
        }
    }

    [Fact]
    public async Task Reading_needs_no_antiforgery_header_and_the_csrf_endpoint_names_the_header()
    {
        var (client, _, _) = await _factory.SignedInAsync();
        using (client)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/hosts")).StatusCode);

            using var response = await client.GetAsync("/api/v1/csrf");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("X-Fleet-Csrf", document.RootElement.GetProperty("headerName").GetString());
            Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("token").GetString()));
        }
    }

    [Fact]
    public async Task The_antiforgery_cookie_is_strict_http_only_and_secure()
    {
        using var client = _factory.CreateHttps();

        var response = await client.GetAsync("/Login");

        var cookie = SetCookies(response).Single(c => c.StartsWith("fleet.csrf=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
    }
}
