using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services;

namespace OetLearner.Api.Tests;

/// <summary>
/// H15: prove the standard resilience handler is wired onto the BrevoEmailSender HttpClient.
/// We use the SAME registration pattern as Program.cs (AddHttpClient + AddStandardResilienceHandler)
/// and inject a fake primary handler that fails twice with a transient 5xx then succeeds.
/// If retries are wired, the third attempt succeeds. If they are NOT wired, the very first 5xx
/// surfaces as InvalidOperationException from BrevoEmailSender.
/// </summary>
public class BrevoResilienceTests
{
    [Fact]
    public async Task BrevoEmailSender_RetriesTransient5xx_WhenStandardResilienceHandlerWired()
    {
        // Arrange - mirror Program.cs registration so the test reflects production wiring.
        var counting = new CountingTransientHandler(failuresBeforeSuccess: 2);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
        services.Configure<BrevoOptions>(o =>
        {
            o.Enabled = true;
            o.ApiKey = "test-key";
            o.FromEmail = "noreply@example.test";
            o.FromName = "OET Test";
            o.BaseUrl = "https://api.brevo.test/v3";
        });

        services
            .AddHttpClient<BrevoEmailSender>(client =>
            {
                client.BaseAddress = new Uri("https://api.brevo.test/v3");
            })
            // SAME line as Program.cs - this is the unit under test.
            .AddStandardResilienceHandler();

        // Inject our counting handler as the primary so we can observe retries.
        services.ConfigureHttpClientDefaults(b => { /* no-op, kept for symmetry */ });
        services.AddTransient<CountingTransientHandler>(_ => counting);

        // Replace the primary handler factory for the BrevoEmailSender named client.
        services.AddSingleton<IConfigureOptions<HttpClientFactoryOptions>>(
            new ConfigureNamedOptions<HttpClientFactoryOptions>(
                nameof(BrevoEmailSender),
                opts => opts.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = counting)));

        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<BrevoEmailSender>();

        var message = new EmailMessage(
            To: "user@example.test",
            Subject: "H15 resilience test",
            TextBody: "hello",
            HtmlBody: null,
            TemplateKey: null,
            TemplateParameters: null);

        // Act
        await sender.SendAsync(message);

        // Assert - 2 failures + 1 success means the resilience handler retried at least twice.
        Assert.True(counting.CallCount >= 3,
            $"Expected at least 3 HTTP attempts (2 transient failures + 1 success). Actual: {counting.CallCount}.");
    }

    private sealed class CountingTransientHandler(int failuresBeforeSuccess) : HttpMessageHandler
    {
        private int _calls;
        public int CallCount => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _calls);
            if (current <= failuresBeforeSuccess)
            {
                // 503 is in the standard resilience handler's transient classification set.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
