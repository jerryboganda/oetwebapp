using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Entitlements;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// F-023 / F-098…F-101 — server-resolved navigation.
///
/// <para>
/// The rule the source specification will not bend on is that the model never
/// produces a link. It names a destination id; the server decides whether that
/// id exists, whether this learner may open it, and what the URL is. These tests
/// exist because the failure mode is silent and expensive: a fabricated path
/// either 404s in front of a paying candidate, or — worse — reads as a working
/// shortcut past a paywall.
/// </para>
/// </summary>
public sealed class CompanionDestinationSecurityTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionDestinationSecurityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    // ── the model cannot supply a link ───────────────────────────────────────

    [Theory]
    [InlineData("/admin/flags")]
    [InlineData("https://app.oetwithdrhesham.co.uk/admin")]
    [InlineData("../../etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("writing.practice/../admin")]
    [InlineData("not_a_destination")]
    public async Task ModelSuppliedTarget_NeverResolvesToAUrl(string supplied)
    {
        var registry = BuildRegistry();

        var result = await registry.ResolveAsync(supplied, Context(), CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Null(result.Url);
        Assert.Equal("unknown_destination", result.Reason);
    }

    [Fact]
    public async Task KnownDestination_ResolvesToAServerBuiltUrl()
    {
        var registry = BuildRegistry();

        var result = await registry.ResolveAsync("writing.practice", Context(), CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal("https://app.test/writing", result.Url);
    }

    [Fact]
    public async Task RetiredId_ResolvesToItsCanonicalDestination()
    {
        var registry = BuildRegistry();

        var result = await registry.ResolveAsync("history", Context(), CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal("submissions.history", result.Id);
        Assert.Equal("https://app.test/submissions", result.Url);
    }

    // ── entitlement ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ModuleGatedDestination_IsRefusedWithoutTheModule()
    {
        // No subscription at all: Mocks and Recalls are opt-in, so they stay shut
        // even under the legacy fail-open module contract.
        var registry = BuildRegistry();

        var result = await registry.ResolveAsync("mock.exams", Context(), CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Null(result.Url);
        Assert.Equal("module_not_entitled", result.Reason);
        Assert.Equal("module:Mocks", result.RequiredScope);
    }

    [Fact]
    public async Task Search_NeverReturnsADestinationThatCannotBeOpened()
    {
        var registry = BuildRegistry();

        var results = await registry.SearchAsync("mock exam simulation", Context(), 10, CancellationToken.None);

        Assert.DoesNotContain(results, r => r.Id == "mock.exams");
        Assert.All(results, r =>
        {
            Assert.True(r.Allowed);
            Assert.False(string.IsNullOrWhiteSpace(r.Url));
        });
    }

    [Fact]
    public async Task Search_NeverAdvertisesARetiredAlias()
    {
        var registry = BuildRegistry();

        var results = await registry.SearchAsync("history", Context(), 10, CancellationToken.None);

        Assert.DoesNotContain(results, r => r.Id == "history");
    }

    [Fact]
    public async Task FeatureFlaggedDestination_IsRefusedWhileTheFlagIsOff()
    {
        // Nothing seeds `strategy_guides` here, and the flag reader fails closed.
        var registry = BuildRegistry();

        var result = await registry.ResolveAsync("strategies", Context(), CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal("surface_disabled", result.Reason);
    }

    [Fact]
    public async Task FeatureFlaggedDestination_OpensOnceTheFlagIsOn()
    {
        await SetFlagAsync("strategy_guides", enabled: true);
        var registry = BuildRegistry();

        var result = await registry.ResolveAsync("strategies", Context(), CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal("https://app.test/strategies", result.Url);
    }

    // ── catalog integrity ────────────────────────────────────────────────────

    [Fact]
    public void EveryDestinationIdIsUnique()
    {
        var registry = BuildRegistry();

        var duplicates = registry.All
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryPathIsRelativeAndSiteLocal()
    {
        var registry = BuildRegistry();

        foreach (var destination in registry.All)
        {
            Assert.True(
                destination.Path.StartsWith('/'),
                $"{destination.Id} path '{destination.Path}' must be site-relative.");
            Assert.False(
                destination.Path.StartsWith("//", StringComparison.Ordinal),
                $"{destination.Id} path '{destination.Path}' is protocol-relative and would leave the site.");
            Assert.DoesNotContain("..", destination.Path, StringComparison.Ordinal);
            Assert.DoesNotContain(":", destination.Path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryRetiredIdPointsAtALiveDestination()
    {
        var registry = BuildRegistry();
        var live = registry.All
            .Where(d => d.SupersededById is null)
            .Select(d => d.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var retired in registry.All.Where(d => d.SupersededById is not null))
        {
            Assert.True(
                live.Contains(retired.SupersededById!),
                $"{retired.Id} is superseded by '{retired.SupersededById}', which is not a live destination.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static CompanionTurnContext Context() => new()
    {
        UserId = "learner-1",
        Profession = ExamProfession.Medicine,
        ProfessionId = "medicine",
        Tier = "free",
        ActionsEnabled = true,
    };

    private async Task SetFlagAsync(string key, bool enabled)
    {
        await using var db = new LearnerDbContext(_options);
        db.FeatureFlags.Add(new OetLearner.Api.Domain.FeatureFlag
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = key,
            Key = key,
            Enabled = enabled,
            Description = "test",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private CompanionDestinationRegistry BuildRegistry()
    {
        var db = new LearnerDbContext(_options);
        return new CompanionDestinationRegistry(
            new EffectiveEntitlementResolver(db, NullLogger<EffectiveEntitlementResolver>.Instance),
            new CompanionFeatureFlags(db, NullLogger<CompanionFeatureFlags>.Instance),
            new PlatformLinkService(
                TestRuntimeSettingsProvider.FromPlatformOptions(new PlatformOptions
                {
                    PublicWebBaseUrl = "https://app.test",
                    FallbackEmailDomain = "example.test",
                }),
                Options.Create(new BillingOptions())));
    }
}
