using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OetLearner.Api.Security;

namespace OetLearner.Api.Tests;

public class DevelopmentAuthHandlerTests
{
    [Fact]
    public async Task DevelopmentAuthHandler_RejectsDebugHeadersOutsideDevelopment()
    {
        var handler = CreateHandler("Production");
        await InitializeHandlerAsync(handler, "X-Debug-Role", "admin");

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.Contains("development", result.Failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DevelopmentAuthHandler_AllowsDebugHeadersInDevelopment()
    {
        var handler = CreateHandler("Development");
        await InitializeHandlerAsync(handler, "X-Debug-Role", "expert", "X-Debug-UserId", "expert-123");

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("expert-123", result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("expert", result.Principal.FindFirstValue(ClaimTypes.Role));
    }

    private static DevelopmentAuthHandler CreateHandler(string environmentName)
    {
        var optionsMonitor = new TestOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions());
        var loggerFactory = LoggerFactory.Create(builder => { });
        var environment = new TestHostEnvironment { EnvironmentName = environmentName };

        return new DevelopmentAuthHandler(optionsMonitor, loggerFactory, UrlEncoder.Default, environment);
    }

    private static async Task InitializeHandlerAsync(DevelopmentAuthHandler handler, params string[] headerPairs)
    {
        var context = new DefaultHttpContext();
        for (var index = 0; index < headerPairs.Length; index += 2)
        {
            context.Request.Headers[headerPairs[index]] = headerPairs[index + 1];
        }

        await ((IAuthenticationHandler)handler).InitializeAsync(
            new AuthenticationScheme(DevelopmentAuthHandler.SchemeName, DevelopmentAuthHandler.SchemeName, typeof(DevelopmentAuthHandler)),
            context);
    }

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => currentValue;

        public T Get(string? name) => currentValue;

        public IDisposable OnChange(Action<T, string> listener) => new NullDisposable();

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}