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

/// <summary>
/// Wave 1 of the env→DB config migration: DataRetention, ExpertAutoAssignment,
/// and PasswordPolicy. Verifies the merge semantics (DB override wins; null DB
/// field falls back to the env/appsettings default).
/// </summary>
public sealed class RuntimeSettingsProviderWave1Tests
{
    [Fact]
    public async Task GetAsync_MergesWave1DatabaseOverridesOverConfiguredDefaults()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddDataProtection();
        await using var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LearnerDbContext>();
            db.RuntimeSettings.Add(new RuntimeSettingsRow
            {
                Id = "default",
                DataRetentionAuditEventsDays = 30,
                DataRetentionSweepIntervalHours = 6,
                DataRetentionBatchSize = 1000,
                ExpertAutoAssignmentEnabled = false,
                ExpertAutoAssignmentSlaHoursStandard = 24,
                PasswordPolicyMinimumLength = 16,
                PasswordPolicyBreachCheckEnabled = false,
                PasswordPolicyBreachApiBaseUrl = "https://hibp.internal/",
                PasswordPolicyBreachApiTimeoutSeconds = 7,
            });
            await db.SaveChangesAsync();
        }

        var provider = BuildProvider(sp,
            dataRetention: new DataRetentionOptions(),
            expert: new ExpertAutoAssignmentOptions(),
            password: new PasswordPolicyOptions());

        var settings = await provider.GetAsync();

        // DB overrides win.
        Assert.Equal(TimeSpan.FromDays(30), settings.DataRetention.AuditEvents);
        Assert.Equal(TimeSpan.FromHours(6), settings.DataRetention.SweepInterval);
        Assert.Equal(1000, settings.DataRetention.BatchSize);
        Assert.False(settings.ExpertAutoAssignment.Enabled);
        Assert.Equal(24, settings.ExpertAutoAssignment.SlaHoursStandard);
        Assert.Equal(16, settings.PasswordPolicy.MinimumLength);
        Assert.False(settings.PasswordPolicy.BreachCheckEnabled);
        Assert.Equal("https://hibp.internal/", settings.PasswordPolicy.BreachApiBaseUrl);
        Assert.Equal(TimeSpan.FromSeconds(7), settings.PasswordPolicy.BreachApiTimeout);

        // Fields with no DB override fall back to the env/appsettings default.
        Assert.Equal(TimeSpan.FromDays(365), settings.DataRetention.AnalyticsEvents);
        Assert.Equal(12, settings.ExpertAutoAssignment.SlaHoursExpress);
        Assert.True(settings.PasswordPolicy.RequireSymbol);
    }

    [Fact]
    public async Task GetAsync_FallsBackToConfiguredDefaultsWhenNoOverrides()
    {
        var services = new ServiceCollection();
        services.AddDbContext<LearnerDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddDataProtection();
        await using var sp = services.BuildServiceProvider();

        var provider = BuildProvider(sp,
            dataRetention: new DataRetentionOptions { BatchSize = 7777 },
            expert: new ExpertAutoAssignmentOptions { SlaHoursStandard = 72 },
            password: new PasswordPolicyOptions { MinimumLength = 12, BreachApiBaseUrl = "https://api.pwnedpasswords.com/" });

        var settings = await provider.GetAsync();

        Assert.Equal(7777, settings.DataRetention.BatchSize);
        Assert.Equal(72, settings.ExpertAutoAssignment.SlaHoursStandard);
        Assert.Equal(12, settings.PasswordPolicy.MinimumLength);
        Assert.True(settings.ExpertAutoAssignment.Enabled); // env default
        Assert.Equal("https://api.pwnedpasswords.com/", settings.PasswordPolicy.BreachApiBaseUrl);
    }

    private static RuntimeSettingsProvider BuildProvider(
        IServiceProvider sp,
        DataRetentionOptions dataRetention,
        ExpertAutoAssignmentOptions expert,
        PasswordPolicyOptions password)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()),
            sp.GetRequiredService<IDataProtectionProvider>(),
            Options.Create(new BrevoOptions()),
            Options.Create(new BillingOptions()),
            Options.Create(new ExternalAuthOptions()),
            Options.Create(new UploadScannerOptions()),
            Options.Create(new WebPushOptions()),
            Options.Create(new ZoomOptions()),
            Options.Create(new SoketiOptions()),
            Options.Create(dataRetention),
            Options.Create(expert),
            Options.Create(password),
            new StaticOptionsMonitor<SmtpOptions>(new SmtpOptions()),
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment("Development"));

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
