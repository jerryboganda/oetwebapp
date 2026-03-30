using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests.Infrastructure;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"oet-learner-tests-storage-{Guid.NewGuid():N}");
    private readonly string _databaseName = $"oet-learner-tests-{Guid.NewGuid():N}";
    private readonly bool _useFirstPartyAuth;
    private readonly Dictionary<string, string?> _previousEnvironmentValues = new();

    public TestWebApplicationFactory()
        : this(useFirstPartyAuth: false)
    {
    }

    protected TestWebApplicationFactory(bool useFirstPartyAuth)
    {
        _useFirstPartyAuth = useFirstPartyAuth;

        if (!_useFirstPartyAuth)
        {
            return;
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"InMemory:{_databaseName}",
            ["Auth:UseDevelopmentAuth"] = "false",
            ["Bootstrap:AutoMigrate"] = "false",
            ["Bootstrap:SeedDemoData"] = "true",
            ["Platform:PublicApiBaseUrl"] = "http://localhost",
            ["Platform:PublicWebBaseUrl"] = "http://localhost",
            ["Platform:FallbackEmailDomain"] = "example.test",
            ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
            ["Storage:LocalRootPath"] = _storageRoot,
            [$"{AuthTokenOptions.SectionName}:Issuer"] = "https://api.example.test",
            [$"{AuthTokenOptions.SectionName}:Audience"] = "oet-learner-web",
            [$"{AuthTokenOptions.SectionName}:AccessTokenSigningKey"] = "access-token-signing-key-12345678901234567890",
            [$"{AuthTokenOptions.SectionName}:RefreshTokenSigningKey"] = "refresh-token-signing-key-1234567890123456789",
            [$"{AuthTokenOptions.SectionName}:AccessTokenLifetime"] = "00:15:00",
            [$"{AuthTokenOptions.SectionName}:RefreshTokenLifetime"] = "30.00:00:00",
            [$"{AuthTokenOptions.SectionName}:OtpLifetime"] = "00:10:00",
            [$"{AuthTokenOptions.SectionName}:AuthenticatorIssuer"] = "OET Learner"
        };

        foreach (var (key, value) in settings)
        {
            var environmentVariableName = ToEnvironmentVariableName(key);
            _previousEnvironmentValues[environmentVariableName] = Environment.GetEnvironmentVariable(environmentVariableName);
            Environment.SetEnvironmentVariable(environmentVariableName, value);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        if (_useFirstPartyAuth)
        {
            return;
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"InMemory:{_databaseName}",
                ["Auth:UseDevelopmentAuth"] = "true",
                ["Platform:PublicApiBaseUrl"] = "http://localhost",
                ["Platform:PublicWebBaseUrl"] = "http://localhost",
                ["Platform:FallbackEmailDomain"] = "example.test",
                ["Billing:CheckoutBaseUrl"] = "https://app.example.test/billing/checkout",
                ["Storage:LocalRootPath"] = _storageRoot
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(string email, string password, string? expectedRole = null)
    {
        var client = CreateClient();
        var signInResponse = client.PostAsJsonAsync(
                "/v1/auth/sign-in",
                new PasswordSignInRequest(email, password, RememberMe: true))
            .GetAwaiter()
            .GetResult();
        signInResponse.EnsureSuccessStatusCode();

        var session = signInResponse.Content
            .ReadFromJsonAsync<AuthSessionResponse>(JsonSupport.Options)
            .GetAwaiter()
            .GetResult();

        if (session is null)
        {
            throw new InvalidOperationException("Expected a sign-in session for the seeded auth account.");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var currentUserResponse = client.GetAsync("/v1/auth/me").GetAwaiter().GetResult();
        currentUserResponse.EnsureSuccessStatusCode();
        var currentUser = currentUserResponse.Content
            .ReadFromJsonAsync<CurrentUserResponse>(JsonSupport.Options)
            .GetAwaiter()
            .GetResult();
        if (currentUser is null)
        {
            throw new InvalidOperationException("Expected current-user details for the seeded auth account.");
        }

        if ((string.Equals(currentUser.Role, "expert", StringComparison.Ordinal)
                || string.Equals(currentUser.Role, "admin", StringComparison.Ordinal))
            && !currentUser.IsEmailVerified)
        {
            throw new InvalidOperationException($"Seeded privileged account {email} is not marked as email verified.");
        }

        if (!string.IsNullOrWhiteSpace(expectedRole)
            && !string.Equals(currentUser.Role, expectedRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected seeded auth client for {email} to resolve role '{expectedRole}', but '/v1/auth/me' returned '{currentUser.Role}'.");
        }

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        foreach (var (key, value) in _previousEnvironmentValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only for test temp files.
        }
    }

    private static string ToEnvironmentVariableName(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);
}

public sealed class FirstPartyAuthTestWebApplicationFactory : TestWebApplicationFactory
{
    public FirstPartyAuthTestWebApplicationFactory()
        : base(useFirstPartyAuth: true)
    {
    }
}
