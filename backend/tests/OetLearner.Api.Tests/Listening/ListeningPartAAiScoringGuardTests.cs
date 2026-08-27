using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Listening;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Listening;

/// <summary>
/// W0 cost/reliability guards for the Listening Part A AI advisory scorer
/// (incident INC-2026-CLAUDE-01).
///
/// The defect: an attempt with no effective approved rationale produced an empty
/// prompt, was POSTed to Anthropic anyway, could not match any verdict back to a
/// gap, left <c>AiScoredAt</c> null, and was therefore re-selected by the 20 s
/// worker for ever — an unbounded paid retry loop.
///
/// These tests pin the repaired contract:
///   • zero eligible evidence  => zero HTTP calls, terminal skip, no AiScoredAt
///   • eligible evidence       => exactly one HTTP call
///   • out-of-prompt verdict   => discarded, gap stays unscored (skipped_no_evidence)
///   • 401                     => terminal, quarantined, no retry armed
///   • 400 / 404 (bad model or configuration) => terminal, no retry armed
///   • 429                     => bounded retry scheduled, not terminal
///   • no Retry-After          => jittered backoff inside the expected band
///   • safe pre-send failure   => retryable (DNS / proxy-tunnel only); connect,
///                                TLS, ambiguous post-send and body-read
///                                failures => terminal indeterminate_timeout
///   • caller cancellation     => rethrown, response disposed, nothing persisted
///   • provider not configured => cool-off only, no attempt spent, no HTTP call
///   • empty/unmatchable 200   => terminal, and no second paid call
///   • deterministic marks (IsCorrect / PointsEarned) are never touched
///   • the worker query excludes skipped / attempt-capped / future-scheduled rows
///     and returns the remainder oldest-work-first so a backlog cannot starve
/// </summary>
public sealed class ListeningPartAAiScoringGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static LearnerDbContext NewDb() => new(
        new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    // ── Evidence guard ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ZeroEligibleRationales_MakesNoProviderCall_AndTerminallySkips()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: false);

        var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("provider must not be called without evidence"));
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        Assert.Empty(handler.Requests);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.NoEvidence, answer.AiSkipReason);
        Assert.Null(answer.AiScoredAt);
        Assert.Null(answer.AiVerdict);
        Assert.Null(answer.AiNextAttemptAt);
        Assert.Equal(0, answer.AiAttemptCount);

        // Deterministic mark is untouched.
        Assert.True(answer.IsCorrect);
        Assert.Equal(1, answer.PointsEarned);
    }

    [Fact]
    public async Task ZeroEligibleRationales_SecondPass_StillMakesNoProviderCall()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: false);

        var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("provider must not be called"));
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EligibleEvidence_MakesExactlyOneProviderCall_AndStampsAdvisoryVerdict()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        var handler = new RecordingHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.OK, ToolUseBody("[{\"number\":1,\"verdict\":\"correct\",\"rationale\":\"Matches the key.\"}]"))));
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        Assert.Single(handler.Requests);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal("correct", answer.AiVerdict);
        Assert.NotNull(answer.AiScoredAt);
        Assert.Equal(Now, answer.AiScoredAt!.Value);
        Assert.Null(answer.AiSkipReason);
        Assert.Equal(1, answer.AiAttemptCount);
        Assert.Null(answer.AiNextAttemptAt);

        // Advisory only — the deterministic mark is still authoritative.
        Assert.True(answer.IsCorrect);
        Assert.Equal(1, answer.PointsEarned);
    }

    [Fact]
    public async Task OutOfPromptVerdictNumber_IsDiscarded_AndThatGapStaysUnscored()
    {
        await using var db = NewDb();
        // Gap #1 has an effective approved rationale; gap #2 does not, so only
        // gap #1 is eligible and only gap #1 is put in the prompt.
        await SeedAttemptAsync(db, withApprovedRationale: true);
        await AddIneligibleGapAsync(db);

        // The provider answers about gap #2 — a number it was never given.
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.OK, ToolUseBody(
                "[{\"number\":2,\"verdict\":\"correct\",\"rationale\":\"Invented for a gap that was never sent.\"}]"))));
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        // Only the eligible gap was ever sent.
        var prompt = Assert.Single(handler.Bodies);
        Assert.Contains("(1) candidate:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("(2) candidate:", prompt, StringComparison.Ordinal);

        // The hallucinated gap must NOT be stamped as AI-scored.
        var hallucinated = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0-b");
        Assert.Equal(ListeningPartAAiSkipReasons.NoEvidence, hallucinated.AiSkipReason);
        Assert.Null(hallucinated.AiScoredAt);
        Assert.Null(hallucinated.AiVerdict);
        Assert.Null(hallucinated.AiRationale);
        Assert.Null(hallucinated.AiModel);
        Assert.Null(hallucinated.AiNextAttemptAt);

        // The gap that WAS in the prompt got no usable verdict back.
        var eligible = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.NoMatchingVerdicts, eligible.AiSkipReason);
        Assert.Null(eligible.AiScoredAt);
        Assert.Null(eligible.AiVerdict);

        // Deterministic marks untouched on both rows.
        Assert.True(hallucinated.IsCorrect);
        Assert.Equal(1, hallucinated.PointsEarned);
        Assert.True(eligible.IsCorrect);
        Assert.Equal(1, eligible.PointsEarned);

        // Both rows are terminal => a second pass buys no further call.
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    // ── Provider failure classification ─────────────────────────────────────────

    [Fact]
    public async Task AuthFailure_IsTerminal_AndDoesNotArmARetry()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        var handler = new RecordingHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"invalid x-api-key sk-ant-secret\"}}")));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        Assert.Single(handler.Requests);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.CredentialQuarantined, answer.AiSkipReason);
        Assert.Null(answer.AiNextAttemptAt);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(1, answer.AiAttemptCount);

        // Exactly one usage record, and it never carries the raw provider body.
        var failure = Assert.Single(recorder.Failures);
        Assert.Equal("http_401", failure.ErrorCode);
        Assert.DoesNotContain("sk-ant-secret", failure.ErrorMessage ?? string.Empty, StringComparison.Ordinal);

        // A second worker pass must not buy another call.
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TransientFailure_SchedulesBoundedRetry_HonouringRetryAfter()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        var handler = new RecordingHandler((_, _) =>
        {
            var response = JsonResponse(HttpStatusCode.TooManyRequests, "{\"error\":{\"type\":\"rate_limit_error\"}}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(4));
            return Task.FromResult(response);
        });
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Null(answer.AiSkipReason);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(1, answer.AiAttemptCount);
        Assert.NotNull(answer.AiNextAttemptAt);
        Assert.Equal(Now.AddMinutes(4), answer.AiNextAttemptAt!.Value);

        // Still scheduled in the future => the scorer refuses to spend another call.
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TransientFailure_IsTerminalOnceTheAttemptCapIsReached()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        // Two durable attempts already spent; this call is the third and last.
        var seeded = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        seeded.AiAttemptCount = ListeningPartAAiRetryPolicy.MaxAttempts - 1;
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"type\":\"overloaded_error\"}}")));
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.RetriesExhausted, answer.AiSkipReason);
        Assert.Null(answer.AiNextAttemptAt);
        Assert.Equal(ListeningPartAAiRetryPolicy.MaxAttempts, answer.AiAttemptCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EmptyButValidProviderResponse_IsTerminal_AndBuysNoSecondCall()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        var handler = new RecordingHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.OK, ToolUseBody("[]"))));
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        Assert.Single(handler.Requests);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.NoMatchingVerdicts, answer.AiSkipReason);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(1, answer.AiAttemptCount);
        Assert.True(answer.IsCorrect);
        Assert.Equal(1, answer.PointsEarned);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "http_400")]
    [InlineData(HttpStatusCode.NotFound, "http_404")]
    public async Task InvalidModelOrConfiguration_IsTerminal_AndDoesNotRetry(
        HttpStatusCode status, string expectedErrorClass)
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        // Anthropic reports an unknown model as 404 and a malformed/invalid
        // request as 400. Replaying the identical payload cannot succeed.
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            JsonResponse(status, "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"model: claude-nope\"}}")));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.ProviderRejected, answer.AiSkipReason);
        Assert.Null(answer.AiNextAttemptAt);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(1, answer.AiAttemptCount);

        var failure = Assert.Single(recorder.Failures);
        Assert.Equal(expectedErrorClass, failure.ErrorCode);
        Assert.DoesNotContain("claude-nope", failure.ErrorMessage ?? string.Empty, StringComparison.Ordinal);

        // Terminal means terminal: no second physical call, ever.
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SafePreSendConnectionFailure_IsRetryable()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        // DNS never resolved, so no socket was opened to the provider and nothing
        // can have been billed. This is one of only two errors we treat as
        // provably pre-send.
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.NameResolutionError, "no such host"));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Null(answer.AiSkipReason);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(1, answer.AiAttemptCount);
        Assert.NotNull(answer.AiNextAttemptAt);

        var failure = Assert.Single(recorder.Failures);
        Assert.Equal("anthropic_connect", failure.ErrorCode);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task ConnectionAndTlsFailures_AreTreatedAsIndeterminate_AndAreNeverRecalled(
        HttpRequestError error)
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        // .NET raises ConnectionError / SecureConnectionError for mid-flight
        // resets and renegotiation failures too, so the runtime does NOT
        // guarantee the request never left the process. Anything that might
        // already have been accepted (and billed) must not be auto-repeated.
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException(error, "connection failed"));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.IndeterminateTimeout, answer.AiSkipReason);
        Assert.Null(answer.AiNextAttemptAt);
        Assert.Null(answer.AiScoredAt);

        var failure = Assert.Single(recorder.Failures);
        Assert.Equal("anthropic_indeterminate", failure.ErrorCode);

        // Terminal means terminal: no second physical call, ever.
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AmbiguousSendFailure_IsTerminalIndeterminate_AndIsNeverRecalled()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        // Unclassified transport failure: the request may already have been
        // accepted (and billed), so it must never be repeated automatically.
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException("unknown transport failure"));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.IndeterminateTimeout, answer.AiSkipReason);
        Assert.Null(answer.AiNextAttemptAt);
        Assert.Null(answer.AiScoredAt);

        var failure = Assert.Single(recorder.Failures);
        Assert.Equal("anthropic_indeterminate", failure.ErrorCode);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task BodyReadFailureAfter2xx_IsTerminalIndeterminate_AndIsNeverRecalled()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        // Headers said 200 — the call is spent and may be billed — but the body
        // was lost. Retrying risks paying twice for the same evidence.
        var content = new FailingHttpContent(() => new IOException("connection reset while reading the body"));
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Equal(ListeningPartAAiSkipReasons.IndeterminateTimeout, answer.AiSkipReason);
        Assert.Null(answer.AiNextAttemptAt);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(1, answer.AiAttemptCount);

        var failure = Assert.Single(recorder.Failures);
        Assert.Equal("anthropic_body_read", failure.ErrorCode);
        Assert.DoesNotContain("connection reset", failure.ErrorMessage ?? string.Empty, StringComparison.Ordinal);

        // The response (and therefore its content) is disposed on this path too.
        Assert.True(content.Disposed);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CallerCancellation_DisposesTheResponse_Rethrows_AndPersistsNothing()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        using var cts = new CancellationTokenSource();
        // Cancel while the body is being read: the response object exists and
        // must still be disposed on the rethrow path.
        var content = new FailingHttpContent(() =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        });
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ScoreAttemptAsync("att-w0", cts.Token));

        Assert.True(content.Disposed);

        // Shutdown is not an outcome: nothing recorded, nothing stamped.
        Assert.Empty(recorder.Failures);
        Assert.Equal(0, recorder.Successes);
        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Null(answer.AiSkipReason);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(0, answer.AiAttemptCount);
        Assert.Null(answer.AiNextAttemptAt);
    }

    [Fact]
    public async Task TransientFailure_WithoutRetryAfter_UsesJitteredBackoff()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        var handler = new RecordingHandler((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"type\":\"overloaded_error\"}}")));
        var service = NewService(db, handler);

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        Assert.Null(answer.AiSkipReason);
        Assert.Equal(1, answer.AiAttemptCount);
        Assert.NotNull(answer.AiNextAttemptAt);

        // 30 s base ±20% jitter for the first failed attempt.
        var delay = answer.AiNextAttemptAt!.Value - Now;
        Assert.InRange(delay, TimeSpan.FromSeconds(24), TimeSpan.FromSeconds(36));
    }

    [Fact]
    public void JitteredBackoff_StaysInBand_AndIsNotAFixedValue()
    {
        var samples = Enumerable.Range(0, 64)
            .Select(_ => ListeningPartAAiRetryPolicy.NextDelay(1, null))
            .ToList();

        Assert.All(samples, d => Assert.InRange(d, TimeSpan.FromSeconds(24), TimeSpan.FromSeconds(36)));
        Assert.True(samples.Distinct().Count() > 1, "backoff must be jittered, not a constant.");

        // Second attempt doubles the base before jitter (60 s ±20%).
        Assert.All(
            Enumerable.Range(0, 32).Select(_ => ListeningPartAAiRetryPolicy.NextDelay(2, null)),
            d => Assert.InRange(d, TimeSpan.FromSeconds(48), TimeSpan.FromSeconds(72)));
    }

    [Fact]
    public async Task UnconfiguredProvider_DefersWithCooldown_WithoutSpendingAnAttempt()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);

        var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("no call may be made without a platform credential"));
        var recorder = new RecordingUsageRecorder();
        var service = NewService(db, handler, recorder, new StubProviderRegistry(platformKey: null));

        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);

        Assert.Empty(handler.Requests);
        Assert.Empty(recorder.Failures);

        var answer = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        // Not terminal (an admin can still add or rotate the key) and not an
        // attempt (nothing left the process) — just a cool-off.
        Assert.Null(answer.AiSkipReason);
        Assert.Null(answer.AiScoredAt);
        Assert.Equal(0, answer.AiAttemptCount);
        Assert.Equal(
            Now + ListeningPartAAiRetryPolicy.UnconfiguredProviderCooldown,
            answer.AiNextAttemptAt);

        // The cool-off is in the future, so a second pass stays silent.
        await service.ScoreAttemptAsync("att-w0", CancellationToken.None);
        Assert.Empty(handler.Requests);
    }

    // ── Worker eligibility ──────────────────────────────────────────────────────

    [Fact]
    public async Task WorkerQuery_SelectsOnlyEligibleAttempts()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);
        await AddAttemptAsync(db, "att-skipped", a => a.AiSkipReason = ListeningPartAAiSkipReasons.NoEvidence);
        await AddAttemptAsync(db, "att-capped", a => a.AiAttemptCount = ListeningPartAAiRetryPolicy.MaxAttempts);
        await AddAttemptAsync(db, "att-future", a => a.AiNextAttemptAt = Now.AddMinutes(5));
        await AddAttemptAsync(db, "att-due", a => a.AiNextAttemptAt = Now.AddMinutes(-1));
        await AddAttemptAsync(db, "att-scored", a => a.AiScoredAt = Now.AddMinutes(-10));

        var eligible = await ListeningPartAAiScoringWorker.EligibleAttemptIds(db, Now).ToListAsync();

        Assert.Contains("att-w0", eligible);
        Assert.Contains("att-due", eligible);
        Assert.DoesNotContain("att-skipped", eligible);
        Assert.DoesNotContain("att-capped", eligible);
        Assert.DoesNotContain("att-future", eligible);
        Assert.DoesNotContain("att-scored", eligible);
    }

    [Fact]
    public async Task WorkerQuery_ReturnsOldestDueWorkFirst_SoABacklogCannotStarve()
    {
        await using var db = NewDb();
        // att-w0: no explicit SubmittedAt, so it orders on StartedAt (Now-20m).
        await SeedAttemptAsync(db, withApprovedRationale: true);

        // Two attempts submitted at the same instant: the attempt id is the
        // stable tie-break, so the order is total and reproducible across slots.
        await AddSubmittedAttemptAsync(db, "att-b-oldest", Now.AddHours(-3));
        await AddSubmittedAttemptAsync(db, "att-a-oldest", Now.AddHours(-3));
        await AddSubmittedAttemptAsync(db, "att-mid", Now.AddHours(-1));
        // Submitted earliest of all, but a backoff was armed and only just came
        // due, so it must queue behind everything that has been waiting longer.
        await AddSubmittedAttemptAsync(
            db,
            "att-backoff-just-due",
            Now.AddHours(-5),
            a => a.AiNextAttemptAt = Now.AddMinutes(-1));

        var ordered = await ListeningPartAAiScoringWorker.EligibleAttemptIds(db, Now).ToListAsync();

        Assert.Equal(
            new[] { "att-a-oldest", "att-b-oldest", "att-mid", "att-w0", "att-backoff-just-due" },
            ordered);

        // Starvation protection: a batch slice always takes the oldest work, so a
        // backlog larger than one batch still drains front-to-back.
        var firstBatch = await ListeningPartAAiScoringWorker.EligibleAttemptIds(db, Now)
            .Take(2)
            .ToListAsync();
        Assert.Equal(new[] { "att-a-oldest", "att-b-oldest" }, firstBatch);
    }

    [Fact]
    public void WorkerQuery_IsFullyTranslatedToSql_WithDeterministicOrderingBeforeTheBatchSlice()
    {
        // Relational compilation only — no connection is opened. If the ordering
        // expression were not EF-translatable, EF would either throw here or fall
        // back to client evaluation, and the batch Take() would slice an
        // unordered set in production.
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new LearnerDbContext(options);

        var sql = ListeningPartAAiScoringWorker.EligibleAttemptIds(db, Now).Take(10).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MIN(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkerRunOnce_SkipsTerminalRowsAndMakesNoProviderCall()
    {
        await using var db = NewDb();
        await SeedAttemptAsync(db, withApprovedRationale: true);
        var seeded = await db.ListeningAnswers.SingleAsync(a => a.Id == "ans-w0");
        seeded.AiSkipReason = ListeningPartAAiSkipReasons.NoEvidence;
        await db.SaveChangesAsync();

        var handler = new RecordingHandler((_, _) =>
            throw new InvalidOperationException("worker must not schedule terminal rows"));

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => db);
        services.AddScoped<IListeningPartAAiScoringService>(_ => NewService(db, handler));
        await using var provider = services.BuildServiceProvider();

        var worker = new ListeningPartAAiScoringWorker(
            provider, NullLogger<ListeningPartAAiScoringWorker>.Instance);

        var processed = await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Empty(handler.Requests);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static ListeningPartAAiScoringService NewService(
        LearnerDbContext db,
        RecordingHandler handler,
        IDirectAiCallRecorder? recorder = null,
        IAiProviderRegistry? registry = null)
        => new(
            db,
            registry ?? new StubProviderRegistry(),
            new StaticHttpClientFactory(handler),
            recorder ?? new RecordingUsageRecorder(),
            new FixedClock(Now),
            NullLogger<ListeningPartAAiScoringService>.Instance);

    private static async Task SeedAttemptAsync(LearnerDbContext db, bool withApprovedRationale)
    {
        var now = Now.AddMinutes(-30);

        db.ContentPapers.Add(new ContentPaper
        {
            Id = "paper-w0",
            SubtestCode = "listening",
            Title = "W0",
            Slug = "w0",
            Status = ContentStatus.Published,
            Difficulty = "standard",
            ExtractedTextJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningParts.Add(new ListeningPart
        {
            Id = "part-w0",
            PaperId = "paper-w0",
            PartCode = ListeningPartCode.A1,
            MaxRawScore = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningExtracts.Add(new ListeningExtract
        {
            Id = "extract-w0",
            ListeningPartId = "part-w0",
            DisplayOrder = 0,
            Kind = ListeningExtractKind.Consultation,
            Title = "Consultation",
            AccentCode = "en-GB",
            SpeakersJson = "[]",
            TranscriptSegmentsJson = "[]",
            NotesBodyMarkdown = "Dose: ____",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "q-w0",
            PaperId = "paper-w0",
            ListeningPartId = "part-w0",
            ListeningExtractId = "extract-w0",
            QuestionNumber = 1,
            DisplayOrder = 1,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Dose: ____",
            CorrectAnswerJson = "\"five\"",
            AcceptedSynonymsJson = "[\"5\"]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });

        if (withApprovedRationale)
        {
            db.AssessmentRationales.Add(new AssessmentRationale
            {
                Id = "rationale-w0",
                Assessment = "listening",
                QuestionRevisionId = "q-w0",
                SourceSentence = "The doctor prescribes five milligrams.",
                RationaleText = "The speaker states the dose is five milligrams.",
                EvidenceCount = 1,
                Status = AssessmentGovernanceStatus.Effective,
                CreatedByUserId = "owner",
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        db.AiProviders.Add(new AiProvider
        {
            Id = "provider-anthropic",
            Code = ListeningPartAAiScoringService.AnthropicProviderCode,
            Name = "Anthropic",
            Dialect = AiProviderDialect.Anthropic,
            BaseUrl = "https://api.anthropic.test",
            EncryptedApiKey = "encrypted",
            ApiKeyHint = "...test",
            DefaultModel = "claude-sonnet-5",
            PricePer1kPromptTokens = 0.003m,
            PricePer1kCompletionTokens = 0.015m,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        await AddAttemptAsync(db, "att-w0", answerId: "ans-w0");
    }

    private static Task AddAttemptAsync(LearnerDbContext db, string attemptId, Action<ListeningAnswer> mutate)
        => AddAttemptAsync(db, attemptId, attemptId + "-ans", mutate);

    /// <summary>Add a submitted attempt with an explicit submission instant, used
    /// by the worker ordering test to pin oldest-work-first behaviour.</summary>
    private static Task AddSubmittedAttemptAsync(
        LearnerDbContext db,
        string attemptId,
        DateTimeOffset submittedAt,
        Action<ListeningAnswer>? mutate = null)
        => AddAttemptAsync(db, attemptId, attemptId + "-ans", mutate, at => at.SubmittedAt = submittedAt);

    private static async Task AddAttemptAsync(
        LearnerDbContext db,
        string attemptId,
        string answerId,
        Action<ListeningAnswer>? mutate = null,
        Action<ListeningAttempt>? mutateAttempt = null)
    {
        var now = Now.AddMinutes(-20);
        var attempt = new ListeningAttempt
        {
            Id = attemptId,
            UserId = "user-w0",
            PaperId = "paper-w0",
            StartedAt = now,
            LastActivityAt = now,
            Status = ListeningAttemptStatus.Submitted,
            Mode = ListeningAttemptMode.Exam,
            MaxRawScore = 1,
        };
        mutateAttempt?.Invoke(attempt);
        db.ListeningAttempts.Add(attempt);

        var answer = new ListeningAnswer
        {
            Id = answerId,
            ListeningAttemptId = attemptId,
            ListeningQuestionId = "q-w0",
            UserAnswerJson = "\"five\"",
            IsCorrect = true,
            PointsEarned = 1,
            AnsweredAt = now,
        };
        mutate?.Invoke(answer);
        db.ListeningAnswers.Add(answer);

        await db.SaveChangesAsync();
    }

    /// <summary>Add a second Part A gap (#2) with NO effective approved
    /// rationale, so it is never eligible and never appears in the prompt.</summary>
    private static async Task AddIneligibleGapAsync(LearnerDbContext db)
    {
        var now = Now.AddMinutes(-30);

        db.ListeningQuestions.Add(new ListeningQuestion
        {
            Id = "q-w0-b",
            PaperId = "paper-w0",
            ListeningPartId = "part-w0",
            ListeningExtractId = "extract-w0",
            QuestionNumber = 2,
            DisplayOrder = 2,
            Points = 1,
            QuestionType = ListeningQuestionType.ShortAnswer,
            Stem = "Duration: ____",
            CorrectAnswerJson = "\"two weeks\"",
            AcceptedSynonymsJson = "[]",
            CaseSensitive = false,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ListeningAnswers.Add(new ListeningAnswer
        {
            Id = "ans-w0-b",
            ListeningAttemptId = "att-w0",
            ListeningQuestionId = "q-w0-b",
            UserAnswerJson = "\"two weeks\"",
            IsCorrect = true,
            PointsEarned = 1,
            AnsweredAt = now,
        });

        await db.SaveChangesAsync();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string ToolUseBody(string verdictsJsonArray)
        => "{\"usage\":{\"input_tokens\":10,\"output_tokens\":5},\"content\":[{\"type\":\"tool_use\","
           + "\"name\":\"emit_part_a_verdicts\",\"input\":{\"verdicts\":" + verdictsJsonArray + "}}]}";

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>Prompt bodies captured at send time — the request object
        /// itself is disposed by the caller before a test can read it.</summary>
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return await respond(request, cancellationToken);
        }
    }

    /// <summary>Response content whose body read always fails. Models the
    /// ambiguous post-send outcome: headers arrived, the body did not.</summary>
    private sealed class FailingHttpContent(Func<Exception> failure) : HttpContent
    {
        public bool Disposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw failure();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => throw failure();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class StaticHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubProviderRegistry(string? platformKey = "test-platform-key") : IAiProviderRegistry
    {
        public Task<AiProvider?> FindByCodeAsync(string code, CancellationToken ct)
            => Task.FromResult<AiProvider?>(new AiProvider
            {
                Id = "provider-anthropic",
                Code = ListeningPartAAiScoringService.AnthropicProviderCode,
                Name = "Anthropic",
                Dialect = AiProviderDialect.Anthropic,
                BaseUrl = "https://api.anthropic.test",
                DefaultModel = "claude-sonnet-5",
                PricePer1kPromptTokens = 0.003m,
                PricePer1kCompletionTokens = 0.015m,
                IsActive = true,
            });

        public Task<IReadOnlyList<AiProvider>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());

        public Task<IReadOnlyList<AiProvider>> ListByCategoryAsync(AiProviderCategory category, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AiProvider>>(Array.Empty<AiProvider>());

        public Task<string?> GetPlatformKeyAsync(string providerCode, CancellationToken ct)
            => Task.FromResult(platformKey);
    }

    private sealed record RecordedFailure(string ErrorCode, string? ErrorMessage);

    private sealed class RecordingUsageRecorder : IDirectAiCallRecorder
    {
        public List<RecordedFailure> Failures { get; } = new();
        public int Successes { get; private set; }

        public Task RecordSuccessAsync(
            AiUsageContext context, string providerId, string model, AiUsage? usage,
            int latencyMs, string? policyTrace, decimal costEstimateUsd, CancellationToken ct)
        {
            Successes++;
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(
            AiUsageContext context, string? providerId, string? model, AiCallOutcome outcome,
            string errorCode, string? errorMessage, int latencyMs, string? policyTrace, CancellationToken ct)
        {
            Failures.Add(new RecordedFailure(errorCode, errorMessage));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
