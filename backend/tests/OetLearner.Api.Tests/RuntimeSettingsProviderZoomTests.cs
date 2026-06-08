using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Settings;

namespace OetLearner.Api.Tests;

public sealed class RuntimeSettingsProviderZoomTests
{
    [Fact]
    public async Task GetAsync_MergesZoomDatabaseOverridesOverConfiguredDefaults()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<LearnerDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddDataProtection();
        await using var serviceProvider = services.BuildServiceProvider();

        var protector = serviceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("RuntimeSettings.Secret.v1");
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.RuntimeSettings.Add(new RuntimeSettingsRow
            {
                Id = "default",
                ZoomEnabled = true,
                ZoomAccountId = "runtime-account",
                ZoomClientId = "runtime-client",
                ZoomClientSecretEncrypted = protector.Protect("runtime-client-secret"),
                ZoomApiBaseUrl = "https://api.zoom.us/v2",
                ZoomTokenUrl = "https://zoom.us/oauth/token",
                ZoomHostUserId = "runtime-host@example.test",
                ZoomMeetingSdkKey = "runtime-sdk-key",
                ZoomMeetingSdkSecretEncrypted = protector.Protect("runtime-sdk-secret"),
                ZoomWebhookSecretTokenEncrypted = protector.Protect("runtime-webhook-secret"),
                ZoomWebhookRetryToleranceSeconds = 900,
                ZoomAllowSandboxFallback = false,
            });
            await db.SaveChangesAsync();
        }

        var provider = new RuntimeSettingsProvider(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            serviceProvider.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(new BrevoOptions()),
            Options.Create(new BillingOptions()),
            Options.Create(new ExternalAuthOptions()),
            Options.Create(new UploadScannerOptions()),
            Options.Create(new WebPushOptions()),
            Options.Create(new ZoomOptions
            {
                Enabled = false,
                AccountId = "env-account",
                ClientId = "env-client",
                ClientSecret = "env-client-secret",
                MeetingSdkKey = "env-sdk-key",
                MeetingSdkSecret = "env-sdk-secret",
                WebhookSecretToken = "env-webhook-secret",
                WebhookRetryToleranceSeconds = 60,
                AllowSandboxFallback = true,
            }),
            new StaticOptionsMonitor<SmtpOptions>(new SmtpOptions()),
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment("Development"));

        var settings = await provider.GetAsync();

        Assert.True(settings.Zoom.Enabled);
        Assert.Equal("runtime-account", settings.Zoom.AccountId);
        Assert.Equal("runtime-client", settings.Zoom.ClientId);
        Assert.Equal("runtime-client-secret", settings.Zoom.ClientSecret);
        Assert.Equal("https://api.zoom.us/v2", settings.Zoom.ApiBaseUrl);
        Assert.Equal("https://zoom.us/oauth/token", settings.Zoom.TokenUrl);
        Assert.Equal("runtime-host@example.test", settings.Zoom.HostUserId);
        Assert.Equal("runtime-sdk-key", settings.Zoom.MeetingSdkKey);
        Assert.Equal("runtime-sdk-secret", settings.Zoom.MeetingSdkSecret);
        Assert.Equal("runtime-webhook-secret", settings.Zoom.WebhookSecretToken);
        Assert.Equal(900, settings.Zoom.WebhookRetryToleranceSeconds);
        Assert.False(settings.Zoom.AllowSandboxFallback);
    }

    [Fact]
    public async Task GetAsync_FailsClosedWhenStoredZoomSecretCannotDecrypt()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<LearnerDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddDataProtection();
        await using var serviceProvider = services.BuildServiceProvider();

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.RuntimeSettings.Add(new RuntimeSettingsRow
            {
                Id = "default",
                ZoomEnabled = true,
                ZoomClientSecretEncrypted = "not-valid-data-protection-ciphertext",
            });
            await db.SaveChangesAsync();
        }

        var provider = new RuntimeSettingsProvider(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            serviceProvider.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(new BrevoOptions()),
            Options.Create(new BillingOptions()),
            Options.Create(new ExternalAuthOptions()),
            Options.Create(new UploadScannerOptions()),
            Options.Create(new WebPushOptions()),
            Options.Create(new ZoomOptions { ClientSecret = "env-client-secret" }),
            new StaticOptionsMonitor<SmtpOptions>(new SmtpOptions()),
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment("Development"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync());
        Assert.Contains("zoom.clientSecret", exception.Message);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
