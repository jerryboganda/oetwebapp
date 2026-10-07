using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai.Review;
using OetLearner.Api.Services.Rulebook;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Tests.Assessment;

/// <summary>
/// The SHARED secondary-reviewer pipeline used by Writing AND Speaking: one bounded FIFO capacity gate, Codex
/// first, automatic API fallback, never a wait on a quota reset. Every test drives the real
/// <see cref="SharedReviewerRunner"/> and <see cref="CodexReviewerGate"/> with fake provider delegates.
/// </summary>
public sealed class SharedReviewerPipelineTests
{
    private static SharedReviewerOptions Fast(string apiProvider = "anthropic", string apiModel = "claude-opus-5-5") => new()
    {
        MaxConcurrency = 2,
        MaxQueueWaitSeconds = 2,
        CodexAttempts = 2,
        CodexAttemptSeconds = 1,
        CodexBudgetSeconds = 1,
        CodexRetryDelaySeconds = 0,
        ApiAttempts = 1,
        ApiAttemptSeconds = 5,
        ApiFallbackProvider = apiProvider,
        ApiFallbackModel = apiModel,
    };

    private static Task<string> Fail(Exception ex) => Task.FromException<string>(ex);

    private static Exception Quota() => new AiProviderHttpException("writing-codex-sub", (int)HttpStatusCode.PaymentRequired, "Payment Required");
    private static Exception Auth() => new AiProviderHttpException("writing-codex-sub", (int)HttpStatusCode.Unauthorized, "Unauthorized");
    private static Exception Unavailable() => new AiProviderHttpException("writing-codex-sub", (int)HttpStatusCode.ServiceUnavailable, "Service Unavailable");
    private static Exception RateLimited() => new AiProviderHttpException("writing-codex-sub", 429, "Too Many Requests");

    // ── Codex success: no API call at all ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Writing_codex_success_never_calls_the_api_fallback()
    {
        var apiCalls = 0;
        var (value, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "writing", "sub-1",
            _ => Task.FromResult("codex-decision"),
            _ => { apiCalls++; return Task.FromResult("api-decision"); },
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal("codex-decision", value);
        Assert.False(info.UsedApiFallback);
        Assert.Equal(0, apiCalls);
    }

    [Fact]
    public async Task Speaking_codex_success_never_calls_the_api_fallback()
    {
        var apiCalls = 0;
        var (value, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "speaking", "session-1",
            _ => Task.FromResult("codex-decision"),
            _ => { apiCalls++; return Task.FromResult("api-decision"); },
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal("codex-decision", value);
        Assert.False(info.UsedApiFallback);
        Assert.Equal(0, apiCalls);
    }

    // ── Codex failure classes → automatic API fallback (both assessment types) ─────────────────────────

    [Theory]
    [InlineData("writing")]
    [InlineData("speaking")]
    public async Task Codex_quota_exhausted_falls_back_to_the_api(string assessmentType)
    {
        var (value, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), assessmentType, "job-1",
            _ => Fail(Quota()),
            _ => Task.FromResult("api-decision"),
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal("api-decision", value);
        Assert.True(info.UsedApiFallback);
        Assert.Equal("codex_quota", info.FallbackReason);
        // Quota is permanent for the pass: Codex is not retried at all, so a quota reset is never waited on.
        Assert.Equal(1, info.CodexAttempts);
    }

    [Fact]
    public async Task Codex_unavailable_falls_back_to_the_api()
    {
        var (value, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "speaking", "session-2",
            _ => Fail(Unavailable()),
            _ => Task.FromResult("api-decision"),
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal("api-decision", value);
        Assert.True(info.UsedApiFallback);
        Assert.Equal("codex_unavailable", info.FallbackReason);
    }

    [Fact]
    public async Task Codex_timeout_falls_back_to_the_api()
    {
        var (value, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "writing", "sub-2",
            // The attempt budget fires: a cancelled attempt is a provider timeout, not caller cancellation.
            async token => { await Task.Delay(Timeout.Infinite, token); return "never"; },
            _ => Task.FromResult("api-decision"),
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal("api-decision", value);
        Assert.True(info.UsedApiFallback);
        Assert.Equal("codex_timeout", info.FallbackReason);
    }

    [Fact]
    public async Task Codex_auth_failure_falls_back_to_the_api()
    {
        var (value, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "speaking", "session-3",
            _ => Fail(Auth()),
            _ => Task.FromResult("api-decision"),
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal("api-decision", value);
        Assert.True(info.UsedApiFallback);
    }

    [Fact]
    public async Task Transient_codex_failure_is_retried_once_before_the_api()
    {
        var attempts = 0;
        var (_, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "speaking", "session-4",
            _ => { attempts++; return attempts == 1 ? Fail(RateLimited()) : Fail(Unavailable()); },
            _ => Task.FromResult("api-decision"),
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(2, info.CodexAttempts);
        Assert.True(info.UsedApiFallback);
    }

    [Fact]
    public async Task Both_routes_failing_throws_with_both_failures_attached()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "writing", "sub-3",
            _ => Fail(Quota()),
            _ => Fail(Unavailable()),
            NullLogger.Instance, CancellationToken.None));

        Assert.Contains("codex_quota", ex.Message, StringComparison.Ordinal);
        Assert.IsType<AggregateException>(ex.InnerException);
    }

    // ── Shared capacity, fairness, bounded wait ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Concurrency_never_exceeds_the_configured_limit_across_writing_and_speaking()
    {
        var gate = new CodexReviewerGate(2, 30);
        var options = Fast();
        options.CodexAttemptSeconds = 30;
        options.CodexBudgetSeconds = 30;
        options.CodexAttempts = 1;

        var inFlight = 0;
        var peak = 0;
        var sync = new object();

        async Task RunOne(string type, string id)
        {
            await SharedReviewerRunner.RunAsync(
                options, gate, type, id,
                async _ =>
                {
                    var now = Interlocked.Increment(ref inFlight);
                    lock (sync) peak = Math.Max(peak, now);
                    await Task.Delay(30);
                    Interlocked.Decrement(ref inFlight);
                    return id;
                },
                _ => Task.FromResult("api"),
                NullLogger.Instance, CancellationToken.None);
        }

        var jobs = new List<Task>();
        for (var i = 0; i < 6; i++) jobs.Add(RunOne(i % 2 == 0 ? "writing" : "speaking", $"job-{i}"));
        await Task.WhenAll(jobs);

        Assert.Equal(2, gate.Capacity);
        Assert.True(peak <= 2, $"peak in-flight was {peak}, cap is 2");
        Assert.Equal(0, inFlight);
    }

    [Fact]
    public async Task A_saturated_gate_falls_back_instead_of_waiting_forever()
    {
        var gate = new CodexReviewerGate(1, 1);
        var blocker = await gate.WaitAsync("writing", "blocker", CancellationToken.None);
        var codexCalls = 0;
        var apiCalls = 0;
        var started = DateTimeOffset.UtcNow;

        var (_, info) = await SharedReviewerRunner.RunAsync(
            Fast(), gate, "speaking", "session-5",
            _ => { codexCalls++; return Task.FromResult("codex"); },
            _ => { apiCalls++; return Task.FromResult("api-decision"); },
            NullLogger.Instance, CancellationToken.None);

        var elapsed = DateTimeOffset.UtcNow - started;
        blocker.Dispose();

        Assert.Equal(0, codexCalls);
        Assert.Equal(1, apiCalls);
        Assert.True(info.UsedApiFallback);
        Assert.Equal("gate_saturated", info.FallbackReason);
        // Bounded by the queue policy (1s here), never by a quota reset.
        Assert.True(elapsed < TimeSpan.FromSeconds(20), $"elapsed {elapsed}");
    }

    [Fact]
    public async Task Fifo_gate_serves_writing_and_speaking_alternately_so_neither_starves()
    {
        var gate = new CodexReviewerGate(1, 30);
        var options = Fast();
        options.CodexAttemptSeconds = 30;
        options.CodexBudgetSeconds = 30;
        options.CodexAttempts = 1;
        options.CodexRetryDelaySeconds = 0;

        var order = new List<string>();
        var sync = new object();
        var release = new SemaphoreSlim(0);
        var running = 0;

        async Task RunOne(string type, int index)
        {
            await SharedReviewerRunner.RunAsync(
                options, gate, type, $"job-{index}",
                async _ =>
                {
                    lock (sync) { order.Add($"{type}:{index}"); running++; }
                    await release.WaitAsync();
                    lock (sync) running--;
                    return type;
                },
                _ =>
                {
                    // A saturation would be a bug here, but never let the pump below stall on it.
                    lock (sync) { order.Add($"{type}:{index}:api"); running++; running--; }
                    return Task.FromResult("api");
                },
                NullLogger.Instance, CancellationToken.None);
        }

        var jobs = new List<Task>();
        for (var i = 0; i < 6; i++) jobs.Add(RunOne(i % 2 == 0 ? "writing" : "speaking", i));

        // Hand out one slot at a time (the gate only has one) until every job is through.
        using var pump = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (jobs.Any(j => !j.IsCompleted) && !pump.IsCancellationRequested)
        {
            release.Release();
            await Task.Delay(10);
        }

        await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(6, order.Count);
        // Global FIFO: with a single slot the queue drains in arrival order, so the first job of each
        // type cannot be pushed behind a late flood of the other type.
        Assert.Equal(6, order.Distinct().Count());
        Assert.Equal("writing:0", order[0]);
        Assert.Equal("speaking:1", order[1]);
        Assert.Equal(0, running);
    }

    [Fact]
    public async Task Queued_work_drains_when_a_slot_frees()
    {
        var gate = new CodexReviewerGate(1, 30);
        var blocker = await gate.WaitAsync("writing", "blocker", CancellationToken.None);
        var second = gate.WaitAsync("speaking", "waiting", CancellationToken.None);
        Assert.False(second.IsCompleted);

        blocker.Dispose();

        var permit = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(permit);
        permit.Dispose();
    }

    // ── Control-plane refusals and caller cancellation are never failed over ─────────────────────────────

    [Fact]
    public async Task Caller_cancellation_never_falls_back_to_the_api()
    {
        using var cts = new CancellationTokenSource();
        var apiCalls = 0;
        var started = Task.Run(() => SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 30), "writing", "sub-4",
            async token => { await Task.Delay(Timeout.Infinite, token); return "never"; },
            _ => { apiCalls++; return Task.FromResult("api"); },
            NullLogger.Instance, cts.Token));

        await Task.Delay(50);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => started);
        Assert.Equal(0, apiCalls);
    }

    [Fact]
    public async Task A_control_plane_refusal_is_never_failed_over()
    {
        var apiCalls = 0;
        await Assert.ThrowsAsync<AiFeaturePolicyRefusedException>(() => SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "writing", "sub-5",
            _ => Task.FromException<string>(new AiFeaturePolicyRefusedException("writing.grade.review", "disabled")),
            _ => { apiCalls++; return Task.FromResult("api"); },
            NullLogger.Instance, CancellationToken.None));

        Assert.Equal(0, apiCalls);
    }

    // ── Idempotency: a late Codex completion after the fallback started cannot overwrite it ────────────

    [Fact]
    public async Task A_late_codex_completion_after_fallback_never_overwrites_the_api_result()
    {
        var lateCodex = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var (value, info) = await SharedReviewerRunner.RunAsync(
            Fast(), new CodexReviewerGate(2, 5), "speaking", "session-6",
            // The Codex CLI keeps running past its attempt budget and answers only afterwards.
            async token => await lateCodex.Task.WaitAsync(token),
            _ => Task.FromResult("api-decision"),
            NullLogger.Instance, CancellationToken.None);

        // The Codex reply lands afterwards (a late CLI answer); the finalized result is the API one.
        lateCodex.SetResult("late-codex-decision");

        Assert.Equal("api-decision", value);
        Assert.True(info.UsedApiFallback);
        Assert.Equal("codex_timeout", info.FallbackReason);
    }

    [Fact]
    public async Task Re_running_the_same_job_is_deterministic()
    {
        var gate = new CodexReviewerGate(2, 5);
        var options = Fast();

        var first = await SharedReviewerRunner.RunAsync(
            options, gate, "writing", "sub-6", _ => Task.FromResult("d"), _ => Task.FromResult("api"), NullLogger.Instance, CancellationToken.None);
        var second = await SharedReviewerRunner.RunAsync(
            options, gate, "writing", "sub-6", _ => Task.FromResult("d"), _ => Task.FromResult("api"), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(first.Info.UsedApiFallback, second.Info.UsedApiFallback);
    }

    // ── The Writing and Speaking reviewers really share the one pipeline ────────────────────────────────

    [Fact]
    public void Both_reviewers_pin_the_same_codex_route_and_the_same_fallback_route()
    {
        Assert.Equal("writing-codex-sub", WritingSubscriptionProviders.Codex);
        Assert.Equal("gpt-6.1-sol", WritingSubscriptionProviders.CodexModel);
        // The API fallback reuses the repo's existing Anthropic row (the same one Writing's grade chain uses as L2).
        Assert.Equal("anthropic", WritingSubscriptionProviders.ClaudeApi);
        Assert.Equal(WritingSubscriptionProviders.ClaudeApi, SharedReviewerOptions.Current.ApiFallbackProvider);
        Assert.Equal(WritingSubscriptionProviders.ClaudeModel, SharedReviewerOptions.Current.ApiFallbackModel);
        Assert.Equal("speaking.grade.review", AiFeatureCodes.SpeakingGradeReview);
        Assert.Equal("writing.grade.review", AiFeatureCodes.WritingGradeReview);
    }

    [Fact]
    public void The_api_fallback_review_slot_can_never_collide_with_a_grading_or_codex_review_slot()
    {
        for (var epoch = 0; epoch < 40; epoch++)
        {
            var fallback = WritingGradeChain.ReviewApiResourceVersion(epoch, 3);
            for (var hop = 0; hop <= 2; hop++)
            {
                for (var attempt = 0; attempt <= 2; attempt++)
                {
                    Assert.NotEqual(fallback, WritingGradeChain.ResourceVersion(epoch, (WritingGradeHop)hop, attempt));
                    Assert.NotEqual(fallback, WritingGradeChain.ReviewResourceVersion(epoch, 3, attempt));
                }
            }
        }
    }

    [Fact]
    public void Writing_reviewer_holds_stay_bounded_so_a_letter_is_never_left_queued()
    {
        var chain = new WritingGradeChainOptions();

        // Even if BOTH reviewer routes are down, the letter is re-queued at most ReviewMaxHolds times and the
        // give-up window is far below the 15-minute release window - so a Codex outage can never park a
        // Writing assessment for hours.
        Assert.True(chain.ReviewMaxHolds >= 0);
        Assert.True(chain.ReviewMaxHolds <= 3);
        Assert.True(chain.ReviewGiveUpMinutes is > 0 and <= 20);
        Assert.True(chain.ReviewGiveUpMinutes < OetLearner.Api.Services.Writing.WritingGradeTimings.ResultReleaseWindow.TotalMinutes);
        Assert.True(chain.ReviewDeadlineSeconds < OetLearner.Api.Services.Writing.WritingGradeTimings.StaleClaimLease.TotalSeconds);
    }
}