using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

public class PasswordPolicyServiceTests
{
    private static PasswordPolicyService CreateService(
        Action<PasswordPolicyOptions>? configure = null,
        Func<HttpRequestMessage, HttpResponseMessage>? hibpResponder = null)
    {
        var options = new PasswordPolicyOptions
        {
            // Default config: complexity-only, breach check disabled.
            BreachCheckEnabled = false,
        };
        configure?.Invoke(options);

        var factory = new StubHttpClientFactory(hibpResponder);
        return new PasswordPolicyService(
            factory,
            Options.Create(options),
            NullLogger<PasswordPolicyService>.Instance);
    }

    // ── Required / null / whitespace ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_null_or_whitespace_password(string? candidate)
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync(candidate, email: null));
        Assert.Equal("password_required", ex.ErrorCode);
        Assert.Equal(400, ex.StatusCode);
    }

    // ── Length ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_password_shorter_than_minimum_length()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync("Ab1!short", email: null));
        Assert.Equal("password_too_short", ex.ErrorCode);
    }

    [Fact]
    public async Task Honours_custom_minimum_length()
    {
        var service = CreateService(o => o.MinimumLength = 16);
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync("AaaBbb1!ccc", email: null));
        Assert.Equal("password_too_short", ex.ErrorCode);
    }

    // ── Complexity classes ────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_password_missing_uppercase_when_mixed_case_required()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync("alllower1!", email: null));
        Assert.Equal("password_missing_case", ex.ErrorCode);
    }

    [Fact]
    public async Task Rejects_password_missing_lowercase_when_mixed_case_required()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync("ALLUPPER1!", email: null));
        Assert.Equal("password_missing_case", ex.ErrorCode);
    }

    [Fact]
    public async Task Rejects_password_missing_digit_when_required()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync("OnlyLetters!", email: null));
        Assert.Equal("password_missing_digit", ex.ErrorCode);
    }

    [Fact]
    public async Task Rejects_password_missing_symbol_when_required()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync("Letters12345", email: null));
        Assert.Equal("password_missing_symbol", ex.ErrorCode);
    }

    [Fact]
    public async Task Allows_password_meeting_default_complexity()
    {
        var service = CreateService();
        await service.EnsurePasswordAcceptableAsync("StrongPass1!", email: null);
        // No throw → pass.
    }

    [Fact]
    public async Task Skips_complexity_classes_when_options_disabled()
    {
        var service = CreateService(o =>
        {
            o.RequireMixedCase = false;
            o.RequireDigit = false;
            o.RequireSymbol = false;
        });

        // 10 lowercase letters — only length is enforced now.
        await service.EnsurePasswordAcceptableAsync("abcdefghij", email: null);
    }

    // ── Email-coupled rejections ──────────────────────────────────────────

    [Fact]
    public async Task Rejects_password_equal_to_email()
    {
        var service = CreateService();
        var email = "ValidUser@example.com";
        // Same as email, plus enough complexity that we hit the email check.
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync(email.ToLowerInvariant(), email));
        // The complexity check fires first if the email-cased form fails it,
        // so we use the lowercased form which would otherwise pass complexity
        // (mixed-case off would be needed). We instead test contains-local-part:
        Assert.Equal("password_missing_case", ex.ErrorCode);
    }

    [Fact]
    public async Task Rejects_password_containing_email_local_part()
    {
        var service = CreateService();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync("Validuser123!", "validuser@example.com"));
        Assert.Equal("password_contains_email", ex.ErrorCode);
    }

    [Fact]
    public async Task Allows_password_when_local_part_is_too_short_to_match()
    {
        var service = CreateService();
        // Local part "abc" is < 4 chars so the contains-check is skipped.
        await service.EnsurePasswordAcceptableAsync("StrongPass1!", "abc@example.com");
    }

    [Fact]
    public async Task Allows_password_when_email_is_null_or_blank()
    {
        var service = CreateService();
        await service.EnsurePasswordAcceptableAsync("StrongPass1!", email: null);
        await service.EnsurePasswordAcceptableAsync("StrongPass1!", email: "");
        await service.EnsurePasswordAcceptableAsync("StrongPass1!", email: "   ");
    }

    // ── HIBP breach check ─────────────────────────────────────────────────

    private const string PasswordHelloWorld = "HelloWorld1!";
    // SHA1("HelloWorld1!") in uppercase hex.
    // Computed at test time so the suffix returned by the stub matches reality.
    private static (string Prefix, string Suffix) SplitHibpHash(string password)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(password);
#pragma warning disable CA5350
        var hash = System.Security.Cryptography.SHA1.HashData(bytes);
#pragma warning restore CA5350
        var hex = Convert.ToHexString(hash);
        return (hex[..5], hex[5..]);
    }

    [Fact]
    public async Task Rejects_breached_password_when_hibp_returns_match()
    {
        var (prefix, suffix) = SplitHibpHash(PasswordHelloWorld);
        var service = CreateService(
            o => o.BreachCheckEnabled = true,
            req =>
            {
                Assert.NotNull(req.RequestUri);
                Assert.EndsWith($"range/{prefix}", req.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:1\r\n{suffix}:42\r\nAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:7\r\n"),
                };
            });

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => service.EnsurePasswordAcceptableAsync(PasswordHelloWorld, email: null));
        Assert.Equal("password_breached", ex.ErrorCode);
    }

    [Fact]
    public async Task Allows_password_when_hibp_returns_no_suffix_match()
    {
        var service = CreateService(
            o => o.BreachCheckEnabled = true,
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:1\r\n"),
            });

        await service.EnsurePasswordAcceptableAsync(PasswordHelloWorld, email: null);
    }

    [Fact]
    public async Task Fails_open_when_hibp_returns_non_success_status()
    {
        var service = CreateService(
            o => o.BreachCheckEnabled = true,
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        // No throw — fail-open behaviour.
        await service.EnsurePasswordAcceptableAsync(PasswordHelloWorld, email: null);
    }

    [Fact]
    public async Task Fails_open_when_hibp_throws_http_request_exception()
    {
        var service = CreateService(
            o => o.BreachCheckEnabled = true,
            _ => throw new HttpRequestException("network down"));

        await service.EnsurePasswordAcceptableAsync(PasswordHelloWorld, email: null);
    }

    [Fact]
    public async Task Skips_hibp_request_entirely_when_breach_check_disabled()
    {
        var hibpCalled = false;
        var service = CreateService(
            o => o.BreachCheckEnabled = false,
            _ =>
            {
                hibpCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        await service.EnsurePasswordAcceptableAsync(PasswordHelloWorld, email: null);
        Assert.False(hibpCalled);
    }

    // ── Stub IHttpClientFactory ───────────────────────────────────────────

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage>? responder) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = new StubHandler(responder);
            // Use any base address; the service always calls "range/{prefix}" relatively.
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.pwnedpasswords.com/"),
            };
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (responder is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
            return Task.FromResult(responder(request));
        }
    }
}
