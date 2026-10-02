using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Data.Migrations;
using OetLearner.Api.Domain;
using OetLearner.Api.Services;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Seeding;
using OetLearner.Api.Services.Settings;
using OetLearner.Api.Services.Writing;
using OetLearner.Api.Services.Writing.Configuration;
using OetLearner.Api.Tests.Infrastructure;

namespace OetLearner.Api.Tests.Writing;

/// <summary>
/// Owner hard rule MAX-ALWAYS-ON (2 Oct 2026, after Max had been skipped for a week by a
/// sticky "quota" marker written on two sidecar-redeploy blips): every Writing grading run —
/// first grade, automatic re-queue or Retry — starts on the Claude Max subscription. No persisted
/// or computed state may switch it off; failover happens only inside one run after Max failed.
/// </summary>
public sealed class WritingMaxAlwaysOnTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LearnerDbContext _db;

    public WritingMaxAlwaysOnTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new LearnerDbContext(new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public static TheoryData<string?, int?, double?> Matrix()
    {
        var data = new TheoryData<string?, int?, double?>();
        foreach (var mode in new[] { "auto", "claude", "codex", "garbage", null })
        foreach (var markerMinutes in new int?[] { null, -60, 30, 7 * 24 * 60 })
        foreach (var utilisation in new double?[] { null, 50, 95, 100 })
        {
            data.Add(mode, markerMinutes, utilisation);
        }
        return data;
    }

    /// <summary>(a) Every provider mode, quota marker and utilisation figure an admin, a legacy
    /// release or the sidecar could leave behind. The selector is resolved through DI with all of
    /// them registered, so a future dependency on any of them is exercised here too.</summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Selector_AlwaysStartsOnMax(string? mode, int? markerMinutes, double? utilisation)
    {
        var row = new RuntimeSettingsRow
        {
            Id = "default",
            WritingAiProviderMode = mode,
            WritingAiClaudeQuotaExceededUntil = markerMinutes is { } m ? DateTimeOffset.UtcNow.AddMinutes(m) : null,
            WritingAiWarnPct = 80,
            WritingAiFailoverPct = 90,
        };
        await using var services = new ServiceCollection()
            .AddSingleton<IRuntimeSettingsProvider>(new TestRuntimeSettingsProvider(TestRuntimeSettingsProvider.Base(), row))
            .AddSingleton<IWritingSubscriptionQuotaService>(new FixedQuota(utilisation))
            .AddScoped<IWritingSubscriptionSelector, WritingSubscriptionSelector>()
            .BuildServiceProvider();

        var decision = await services.GetRequiredService<IWritingSubscriptionSelector>().DecideAsync(default);

        Assert.Equal(WritingSubscriptionProviders.Claude, decision.ProviderCode);
        Assert.Equal("claude-opus-5-5", decision.Model);
        Assert.Equal("max_always_first", decision.Reason);
        Assert.False(decision.IsFallback);
    }

    /// <summary>(b) Architecture guard: nothing outside the allow-list may write the legacy marker,
    /// and the deleted "skip Max" mechanisms may not come back.</summary>
    [Fact]
    public void Source_NeverWritesTheSkipMaxMarker_NorReintroducesSkipMaxRouting()
    {
        var apiRoot = Path.Combine(FindRepoRoot(), "backend", "src", "OetLearner.Api");
        var markerWrite = new Regex(@"WritingAiClaudeQuotaExceededUntil\s*=(?!=)", RegexOptions.CultureInvariant);
        var removedApis = new Regex(@"RecordClaudeQuotaSignal|RecordClaudeCooldown|GetClaudeReadiness", RegexOptions.CultureInvariant);
        string[] allowMarkerWrite =
        [
            Path.Combine("Domain", "RuntimeSettingsRow.cs"),
            Path.Combine("Endpoints", "AiUsageAdminEndpoints.cs"), // admin "clear marker" button
        ];
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(apiRoot, path);
            if (relative.StartsWith("bin", StringComparison.Ordinal) || relative.StartsWith("obj", StringComparison.Ordinal)) continue;
            var source = File.ReadAllText(path);
            var migration = relative.StartsWith(Path.Combine("Data", "Migrations"), StringComparison.Ordinal);
            if (!migration && !allowMarkerWrite.Contains(relative) && markerWrite.IsMatch(source))
                violations.Add($"{relative}: writes WritingAiClaudeQuotaExceededUntil");
            if (removedApis.Match(source) is { Success: true } removed)
                violations.Add($"{relative}: {removed.Value}");
        }

        var selectorFile = File.ReadAllText(Path.Combine(apiRoot, "Services", "Writing", "WritingSubscriptionSelector.cs"));
        var selectorClass = selectorFile[selectorFile.IndexOf("public sealed class WritingSubscriptionSelector", StringComparison.Ordinal)..];
        // Utilisation can only come from the quota snapshot, so "Snapshot" covers the weekly estimate.
        foreach (var forbidden in new[] { "Codex", "ProviderMode", "QuotaExceeded", "Snapshot", "FailoverPct", "WarnPct", "Readiness", "ClaudeApi", "RuntimeSettings", "IRuntimeSettingsProvider" })
        {
            if (selectorClass.Contains(forbidden, StringComparison.Ordinal))
                violations.Add($"WritingSubscriptionSelector routes on {forbidden}");
        }

        Assert.Empty(violations);
    }

    /// <summary>(c) A run whose every route failed leaves nothing behind: the NEXT run (the automatic
    /// re-queue) starts on Max again, run after run, even with a legacy week-long marker and a
    /// forced-Codex mode stored.</summary>
    [Fact]
    public async Task EveryRun_StartsOnMax_AfterARunWhoseMaxAttemptsAllFailed()
    {
        _db.RuntimeSettings.Add(new RuntimeSettingsRow
        {
            Id = "default",
            WritingAiProviderMode = "codex",
            WritingAiClaudeQuotaExceededUntil = DateTimeOffset.UtcNow.AddDays(7),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var scenarioId = Guid.NewGuid();
        _db.WritingScenarios.Add(new WritingScenario
        {
            Id = scenarioId,
            Title = "Max first task",
            Profession = "medicine",
            LetterType = "routine_referral",
            Status = "published",
            AuthorId = "admin-1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var id = Guid.NewGuid();
        _db.WritingSubmissions.Add(new WritingSubmission
        {
            Id = id,
            UserId = "max-learner",
            ScenarioId = scenarioId,
            Mode = "practice",
            LetterContent = "Dear Dr Green,\n\nI am writing to refer Mr Lee.\n\nYours sincerely,\nDoctor",
            LetterContentHash = "hash-max-first",
            WordCount = 14,
            Status = WritingSubmissionStatuses.Queued,
            GradingTier = "express",
            InputSource = "typed",
            StartedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        var gateway = new FailingGateway();
        var pipeline = new WritingSubmissionEvaluationPipeline(
            _db,
            gateway,
            new EmptyCanonEngine(),
            mistakeService: null!,
            events: new NoopWritingEventBus(),
            TimeProvider.System,
            TestRuntimeSettingsProvider.FromWritingOptions(new WritingV2Options()),
            NullLogger<WritingSubmissionEvaluationPipeline>.Instance,
            subscriptionSelector: new WritingSubscriptionSelector(),
            assessmentPreflight: new PassThroughPreflight(),
            gradeChainOptions: Microsoft.Extensions.Options.Options.Create(new WritingGradeChainOptions { MaxAutoRetries = 10 }));

        const int runs = 4;
        for (var run = 0; run < runs; run++)
        {
            await Assert.ThrowsAsync<ApiException>(() => pipeline.EvaluateAsync(id, default));
        }

        var providers = gateway.Providers;
        Assert.Equal(runs * 5, providers.Count);
        for (var run = 0; run < runs; run++)
        {
            Assert.Equal(
                new[]
                {
                    WritingSubscriptionProviders.Claude, WritingSubscriptionProviders.Claude,
                    WritingSubscriptionProviders.ClaudeApi,
                    WritingSubscriptionProviders.Codex, WritingSubscriptionProviders.Codex,
                },
                providers.Skip(run * 5).Take(5));
        }

        // Nothing switched Max off for the next grade either.
        var settings = await _db.RuntimeSettings.AsNoTracking().SingleAsync();
        Assert.Equal("codex", settings.WritingAiProviderMode); // inert, untouched by grading
    }

    /// <summary>Point 4: the seeder keeps the Max row active on every boot and moves a Codex row that
    /// still has the old default to GPT-6.1 Sol, leaving a deliberately chosen model alone.</summary>
    [Fact]
    public async Task Seeder_SelfHealsTheMaxRow_AndMovesCodexToGpt61Sol()
    {
        await WritingSubscriptionProviderSeeder.SeedAsync(_db);
        Assert.True((await ProviderAsync(WritingSubscriptionProviderDefaults.ClaudeCode)).IsActive);

        var max = await _db.AiProviders.SingleAsync(p => p.Code == WritingSubscriptionProviderDefaults.ClaudeCode);
        max.IsActive = false;
        var codex = await _db.AiProviders.SingleAsync(p => p.Code == WritingSubscriptionProviderDefaults.CodexCode);
        codex.DefaultModel = "gpt-6-sol";
        codex.AllowedModelsCsv = "gpt-6-sol";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await WritingSubscriptionProviderSeeder.SeedAsync(_db);

        Assert.True((await ProviderAsync(WritingSubscriptionProviderDefaults.ClaudeCode)).IsActive);
        var healed = await ProviderAsync(WritingSubscriptionProviderDefaults.CodexCode);
        Assert.Equal("gpt-6.1-sol", healed.DefaultModel);
        Assert.Equal("gpt-6.1-sol", healed.AllowedModelsCsv);
        Assert.Equal("gpt-6.1-sol", WritingSubscriptionProviders.CodexModel);

        var chosen = await _db.AiProviders.SingleAsync(p => p.Code == WritingSubscriptionProviderDefaults.CodexCode);
        chosen.DefaultModel = "gpt-7-admin-choice";
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await WritingSubscriptionProviderSeeder.SeedAsync(_db);
        Assert.Equal("gpt-7-admin-choice", (await ProviderAsync(WritingSubscriptionProviderDefaults.CodexCode)).DefaultModel);
    }

    /// <summary>Point 3: the deploy itself clears the live week-long marker and a forced-Codex mode.</summary>
    [Fact]
    public void Migration_ClearsTheLegacyMarker_AndForcedCodexMode()
    {
        var sql = string.Join('\n', new AddWritingGradeRecoveryState().UpOperations.OfType<SqlOperation>().Select(o => o.Sql));

        Assert.Contains("SET \"WritingAiClaudeQuotaExceededUntil\" = NULL", sql);
        Assert.Contains("SET \"WritingAiProviderMode\" = 'auto'", sql);
        Assert.Contains("WHERE \"WritingAiProviderMode\" = 'codex'", sql);
    }

    private Task<AiProvider> ProviderAsync(string code)
        => _db.AiProviders.AsNoTracking().SingleAsync(p => p.Code == code);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the OET repository root.");
    }

    private sealed class FixedQuota(double? utilisation) : IWritingSubscriptionQuotaService
    {
        public Task<WritingQuotaSnapshot> GetSnapshotAsync(CancellationToken ct)
            => Task.FromResult(new WritingQuotaSnapshot(utilisation, null, "reported", 0, 0, DateTimeOffset.UtcNow));
    }

    private sealed class FailingGateway : IAiGatewayService
    {
        public List<string> Providers { get; } = new();

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context)
            => new()
            {
                SystemPrompt = "# OET AI — Rulebook-Grounded System Prompt\n**This call concerns WRITING**",
                TaskInstruction = "score",
            };

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Providers.Add(request.Provider);
            throw new HttpRequestException("every route is down");
        }
    }

    private sealed class PassThroughPreflight : IWritingAssessmentPreflightService
    {
        public Task<WritingAssessmentPreflightResult> ValidateAsync(WritingSubmission submission, CancellationToken ct)
            => Task.FromResult(new WritingAssessmentPreflightResult(
                true, WritingAssessmentV11Status.CandidateReady,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                "medicine", "routine_referral", "test", "task", "Patient name: Adam Lee\nAge: 54"));
    }
}
