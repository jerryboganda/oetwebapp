using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Security;
using OetLearner.Api.Services;
using Xunit;

namespace OetLearner.Api.Tests;

public sealed class CookieBackedAuthCsrfGuardTests
{
    [Fact]
    public void ValidateCookieBackedAuthMutation_AllowsNativeBodyTokenPath()
    {
        var guard = CreateGuard();
        var context = CreateContext();
        context.Request.Headers.Cookie = "oet_rt=refresh-token";
        context.Request.Headers["X-OET-Client-Platform"] = "capacitor";

        guard.ValidateCookieBackedAuthMutation(context, "body-refresh-token");
    }

    [Fact]
    public void ValidateCookieBackedAuthMutation_RejectsMissingCsrfToken()
    {
        var guard = CreateGuard();
        var context = CreateContext();
        context.Request.Headers.Cookie = "oet_rt=refresh-token";
        context.Request.Headers.Origin = "https://app.example.test";

        var ex = Assert.Throws<ApiException>(() => guard.ValidateCookieBackedAuthMutation(context, null));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal("csrf_token_required", ex.ErrorCode);
    }

    [Fact]
    public void ValidateCookieBackedAuthMutation_RejectsUnknownOrigin()
    {
        var guard = CreateGuard();
        var context = CreateContext();
        context.Request.Headers.Cookie = "oet_rt=refresh-token; oet_csrf=csrf-token";
        context.Request.Headers["x-csrf-token"] = "csrf-token";
        context.Request.Headers.Origin = "https://evil.example";

        var ex = Assert.Throws<ApiException>(() => guard.ValidateCookieBackedAuthMutation(context, null));

        Assert.Equal(StatusCodes.Status403Forbidden, ex.StatusCode);
        Assert.Equal("csrf_origin_forbidden", ex.ErrorCode);
    }

    [Fact]
    public void ValidateCookieBackedAuthMutation_AllowsTrustedOriginAndMatchingCsrf()
    {
        var guard = CreateGuard();
        var context = CreateContext();
        context.Request.Headers.Cookie = "oet_rt=refresh-token; oet_csrf=csrf-token";
        context.Request.Headers["x-csrf-token"] = "csrf-token";
        context.Request.Headers.Origin = "https://app.example.test";

        guard.ValidateCookieBackedAuthMutation(context, null);
    }

    private static CookieBackedAuthCsrfGuard CreateGuard()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOriginsCsv"] = "https://app.example.test"
            })
            .Build();

        return new CookieBackedAuthCsrfGuard(
            Options.Create(new PlatformOptions
            {
                PublicApiBaseUrl = "https://api.example.test",
                PublicWebBaseUrl = "https://app.example.test",
            }),
            config,
            new TestWebHostEnvironment());
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.example.test");
        return context;
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
