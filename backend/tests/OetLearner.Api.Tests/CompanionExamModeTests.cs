using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Companion;
using OetLearner.Api.Services.Entitlements;
using Xunit;

namespace OetLearner.Api.Tests;

/// <summary>
/// F-155 — exam integrity. The companion must refuse hints, answers and coaching
/// while the learner is inside a protected attempt.
///
/// <para>
/// These tests exist because the guard was, for a while, <b>unreachable</b>. It
/// keyed off <c>CompanionContextEnvelope.AttemptId</c>, and nothing on the wire
/// ever populated the envelope — <c>StartTurn</c> had no parameter for it — so
/// exam mode was permanently false in the live path. Worse, even once the
/// envelope existed, a client that simply omitted the attempt id would have
/// unlocked hints mid-exam.
/// </para>
///
/// <para>
/// So the property under test is: <b>exam mode is decided from attempt state in
/// the database, and the client cannot influence it downward.</b> The envelope
/// may only ever ADD protection.
/// </para>
/// </summary>
public sealed class CompanionExamModeTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LearnerDbContext> _options;

    public CompanionExamModeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LearnerDbContext>().UseSqlite(_connection).Options;
        using var seed = new LearnerDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Theory]
    [InlineData(AttemptState.InProgress)]
    [InlineData(AttemptState.Paused)]
    [InlineData(AttemptState.NotStarted)]
    public async Task LiveAttempt_TriggersExamMode_EvenWithNoEnvelope(AttemptState state)
    {
        await SeedAttemptAsync("a1", "learner-1", state, DateTimeOffset.UtcNow.AddMinutes(-10));

        // The client sends nothing at all — the old failure mode.
        var context = await ResolveAsync("learner-1", envelope: null);

        Assert.True(context.ExamMode);
    }

    [Theory]
    [InlineData(AttemptState.Submitted)]
    [InlineData(AttemptState.Evaluating)]
    [InlineData(AttemptState.Completed)]
    [InlineData(AttemptState.Failed)]
    [InlineData(AttemptState.Abandoned)]
    public async Task FinishedAttempt_DoesNotTriggerExamMode(AttemptState state)
    {
        await SeedAttemptAsync("a1", "learner-1", state, DateTimeOffset.UtcNow.AddMinutes(-10));

        var context = await ResolveAsync("learner-1", envelope: null);

        Assert.False(context.ExamMode);
    }

    [Fact]
    public async Task NoAttemptsAtAll_DoesNotTriggerExamMode()
    {
        var context = await ResolveAsync("learner-1", envelope: null);
        Assert.False(context.ExamMode);
    }

    [Fact]
    public async Task AnotherLearnersLiveAttempt_DoesNotTriggerExamMode()
    {
        await SeedAttemptAsync("a1", "learner-2", AttemptState.InProgress, DateTimeOffset.UtcNow);

        var context = await ResolveAsync("learner-1", envelope: null);

        Assert.False(context.ExamMode);
    }

    [Fact]
    public async Task StaleUnfinishedAttempt_DoesNotPinTheLearnerIntoPermanentRefusal()
    {
        // Left InProgress days ago: stale data, not a live exam. Treating it as
        // one would lock this learner out of the companion forever.
        await SeedAttemptAsync("a1", "learner-1", AttemptState.InProgress, DateTimeOffset.UtcNow.AddDays(-3));

        var context = await ResolveAsync("learner-1", envelope: null);

        Assert.False(context.ExamMode);
    }

    [Fact]
    public async Task OmittingTheAttemptId_CannotUnlockHintsDuringALiveAttempt()
    {
        await SeedAttemptAsync("a1", "learner-1", AttemptState.InProgress, DateTimeOffset.UtcNow);

        // A crafted client sends an envelope for a harmless page instead.
        var envelope = new CompanionContextEnvelope(Surface: "dashboard", RouteId: "/");
        var context = await ResolveAsync("learner-1", envelope);

        Assert.True(context.ExamMode);
    }

    [Fact]
    public async Task ClientSuppliedExamModeFlag_IsDiscarded()
    {
        // The envelope carries an ExamMode bool; the server must overwrite it,
        // never read it. Claiming "false" during a live attempt must not work.
        await SeedAttemptAsync("a1", "learner-1", AttemptState.InProgress, DateTimeOffset.UtcNow);

        var context = await ResolveAsync("learner-1", new CompanionContextEnvelope(ExamMode: false));

        Assert.True(context.ExamMode);
        Assert.True(context.Envelope.ExamMode);
    }

    [Fact]
    public async Task UnknownAttemptIdInTheEnvelope_FailsClosed()
    {
        // No live attempt in the database, but the surface claims one exists.
        // The envelope may only ever ADD protection, so this refuses.
        var context = await ResolveAsync(
            "learner-1",
            new CompanionContextEnvelope(AttemptId: "does-not-exist"));

        Assert.True(context.ExamMode);
    }

    [Fact]
    public async Task ExamModeReachesThePrompt_AsAnAbsoluteRefusal()
    {
        await SeedAttemptAsync("a1", "learner-1", AttemptState.InProgress, DateTimeOffset.UtcNow);
        var context = await ResolveAsync("learner-1", envelope: null);

        var composer = new CompanionPromptComposer(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var prompt = await composer.ComposeAsync(
            context,
            new CompanionRetrievalResult([], false, false, false, []),
            CancellationToken.None);

        Assert.Contains("PROTECTED ATTEMPT", prompt, StringComparison.Ordinal);
        Assert.Contains("Refuse all hints", prompt, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<CompanionTurnContext> ResolveAsync(
        string userId,
        CompanionContextEnvelope? envelope)
    {
        await using var db = new LearnerDbContext(_options);
        var resolver = new CompanionContextResolver(
            db,
            new EffectiveEntitlementResolver(db, NullLogger<EffectiveEntitlementResolver>.Instance),
            new AllOffFlags(),
            NullLogger<CompanionContextResolver>.Instance,
            // No credit service: exam mode must not depend on a balance read.
            credits: null);

        return await resolver.ResolveAsync(userId, envelope, CancellationToken.None);
    }

    private async Task SeedAttemptAsync(
        string id,
        string userId,
        AttemptState state,
        DateTimeOffset startedAt)
    {
        await using var db = new LearnerDbContext(_options);
        db.Attempts.Add(new Attempt
        {
            Id = id,
            UserId = userId,
            ContentId = "content-1",
            SubtestCode = "writing",
            Context = "practice",
            Mode = "standard",
            State = state,
            StartedAt = startedAt,
        });
        await db.SaveChangesAsync();
    }

    private sealed class AllOffFlags : ICompanionFeatureFlags
    {
        public Task<bool> IsEnabledAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<bool> IsRetrievalEnabledAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<bool> AreActionsEnabledAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<bool> IsCreditConsumptionEnabledAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<bool> IsScoreDisplayEnabledAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<bool> IsPlatformFlagOnAsync(string key, CancellationToken ct) => Task.FromResult(false);
    }
}
