using System.Net;
using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

/// <summary>Job execution and reporting rules (RW-054, RW-055, RW-087, RW-088, RW-150, RW-154; protocol 4.5, 4.6, 5.5).</summary>
public sealed class JobRunnerTests
{
    private static async Task<(ClaimedJob Job, JobLease Lease)> RunAsync(Harness h, ClaimedJob job, Action<JobLease>? beforeRun = null)
    {
        var lease = TestJobs.Lease(job, h.Clock);
        beforeRun?.Invoke(lease);
        await h.Runner.RunAsync(job, lease);
        return (job, lease);
    }

    private static string ScratchEntries(Harness h) => string.Join(",", Directory.GetFileSystemEntries(h.Dir.File("scratch")));

    [Fact]
    public async Task RW088_a_successful_job_completes_with_the_hash_of_the_exact_result_bytes_and_leaves_no_scratch()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        h.Io.Inputs["pdf"] = pdf;

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), pdf));

        var complete = Assert.Single(h.Api.Completes);
        var request = JsonSerializer.Deserialize<CompleteRequest>(complete.Body, ProtocolJson.Options)!;
        Assert.Equal(1, request.Fence);
        Assert.Equal(TestIds.Sha(Encoding.UTF8.GetBytes(request.ResultJson)), request.ResultSha256);
        using var result = JsonDocument.Parse(request.ResultJson);
        Assert.Equal("pdf.extract.result/1", result.RootElement.GetProperty("schema").GetString());
        Assert.Equal(EngineVersions.Pdf, result.RootElement.GetProperty("engineVersion").GetString());
        Assert.Equal(TestIds.Sha(pdf), result.RootElement.GetProperty("inputSha256").GetString());
        Assert.False(result.RootElement.GetProperty("needsOcr").GetBoolean());
        Assert.Empty(h.Api.Fails);
        Assert.Equal("", ScratchEntries(h));
        Assert.Equal(new[] { TestIds.Sha(pdf) }, h.Io.CacheOffers);
    }

    [Fact]
    public async Task RW087_complete_is_retried_with_a_byte_identical_body_until_it_succeeds()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        h.Io.Inputs["pdf"] = pdf;
        var attempts = 0;
        h.Api.OnComplete = (_, _) => ++attempts < 3
            ? Api.Error<CompleteResponse>(ApiKind.ServerError, 503, "apply_failed")
            : Api.Ok(new CompleteResponse { Status = "succeeded", Outcome = "Applied" });

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), pdf));

        Assert.Equal(3, h.Api.Completes.Count);
        Assert.Equal(h.Api.Completes[0].Body, h.Api.Completes[1].Body);
        Assert.Equal(h.Api.Completes[0].Body, h.Api.Completes[2].Body);
    }

    [Fact]
    public async Task RW055_a_409_on_complete_means_the_lease_is_lost_and_nothing_else_is_sent()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        h.Io.Inputs["pdf"] = pdf;
        h.Api.OnComplete = (_, _) => Api.Error<CompleteResponse>(ApiKind.Conflict, 409, "lease_lost", "superseded");

        var (_, lease) = await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), pdf));

        Assert.Single(h.Api.Completes);
        Assert.Empty(h.Api.Fails);
        Assert.Equal(AbortReason.LeaseLost, lease.Reason);
    }

    [Fact]
    public async Task RW055_a_lease_lost_during_execution_never_calls_complete_or_fail()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Executors.Replace([new CancelAwareExecutor()]);
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build());
        var lease = TestJobs.Lease(job, h.Clock);

        var running = h.Runner.RunAsync(job, lease);
        await Harness.WaitForAsync(() => CancelAwareExecutor.Started);
        lease.Abort(AbortReason.LeaseLost);
        await running;

        Assert.Empty(h.Api.Completes);
        Assert.Empty(h.Api.Fails);
        Assert.Equal("", ScratchEntries(h));
    }

    [Fact]
    public async Task RW054_past_the_local_expiry_the_job_is_never_completed_and_one_best_effort_fail_is_tried()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        h.Io.Inputs["pdf"] = pdf;
        var job = TestJobs.Pdf(TestIds.Job(1), pdf);
        var lease = TestJobs.Lease(job, h.Clock);
        h.Clock.Advance(TimeSpan.FromSeconds(200)); // beyond leaseRemaining - 5 s

        await h.Runner.RunAsync(job, lease);

        Assert.Empty(h.Api.Completes);
        var fail = Assert.Single(h.Api.Fails);
        Assert.Equal(FailCodes.Shutdown, fail.Request.Code);
    }

    [Fact]
    public async Task graceful_shutdown_aborts_the_job_and_reports_fail_shutdown_as_a_refund_code()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Executors.Replace([new CancelAwareExecutor()]);
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build());
        var lease = TestJobs.Lease(job, h.Clock);

        var running = h.Runner.RunAsync(job, lease);
        await Harness.WaitForAsync(() => CancelAwareExecutor.Started);
        lease.Abort(AbortReason.Shutdown);
        await running;

        var fail = Assert.Single(h.Api.Fails);
        Assert.Equal(FailCodes.Shutdown, fail.Request.Code);
        Assert.True(fail.Request.Retryable);
        Assert.Empty(h.Api.Completes);
    }

    [Fact]
    public async Task memory_pressure_shedding_reports_fail_pressure_shed()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Executors.Replace([new CancelAwareExecutor()]);
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build());
        var lease = TestJobs.Lease(job, h.Clock);
        h.Supervisor.Start(job, lease);
        await Harness.WaitForAsync(() => CancelAwareExecutor.Started);

        Assert.True(h.Supervisor.ShedYoungest());
        await Harness.WaitForAsync(() => h.Supervisor.RunningCount == 0);

        Assert.Equal(FailCodes.PressureShed, Assert.Single(h.Api.Fails).Request.Code);
    }

    [Fact]
    public async Task the_job_timeout_reports_fail_timeout()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Executors.Replace([new CancelAwareExecutor()]);
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build());
        job.Limits.TimeoutSeconds = 1;

        await RunAsync(h, job);

        var fail = Assert.Single(h.Api.Fails);
        Assert.Equal(FailCodes.Timeout, fail.Request.Code);
        Assert.True(fail.Request.Retryable);
    }

    [Fact]
    public async Task an_engine_version_the_agent_does_not_offer_fails_non_retryably_without_running()
    {
        using var h = new Harness();
        h.MakeReady();
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build(), engine: "pdfpig:9.9.9/oet-text:9");

        await RunAsync(h, job);

        var fail = Assert.Single(h.Api.Fails);
        Assert.Equal(FailCodes.InternalError, fail.Request.Code);
        Assert.False(fail.Request.Retryable);
    }

    [Fact]
    public async Task a_job_that_arrives_while_the_node_drains_is_released_with_the_drain_refund_code()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Status.OnServerDraining();

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build()));

        Assert.Equal(FailCodes.Drain, Assert.Single(h.Api.Fails).Request.Code);
    }

    [Fact]
    public async Task limits_beyond_the_agent_budgets_are_a_limits_exceeded_failure()
    {
        using var h = new Harness();
        h.MakeReady();
        var job = TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build());
        job.Limits.MemMiB = 6000;

        await RunAsync(h, job);

        var fail = Assert.Single(h.Api.Fails);
        Assert.Equal(FailCodes.LimitsExceeded, fail.Request.Code);
        Assert.False(fail.Request.Retryable);
    }

    [Fact]
    public async Task RW154_fail_messages_always_match_the_allowed_grammar_and_carry_no_path()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Io.FetchFault = _ => new JobFailureException(FailCodes.InputUnavailable, true, "cannot read /scratch/rj_x-1/in.pdf: access denied é\n");

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build()));

        var fail = Assert.Single(h.Api.Fails);
        Assert.Matches(Wire.FailMessagePattern, fail.Request.Message);
        Assert.DoesNotContain("/", fail.Request.Message);
        Assert.Equal("", ScratchEntries(h));
    }

    [Fact]
    public async Task RW150_scratch_is_removed_when_the_executor_throws_unexpectedly()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Io.FetchFault = _ => new InvalidOperationException("secret learner text must not leak");

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build()));

        var fail = Assert.Single(h.Api.Fails);
        Assert.Equal(FailCodes.InternalError, fail.Request.Code);
        Assert.DoesNotContain("secret", fail.Request.Message);
        Assert.Equal("", ScratchEntries(h));
    }

    [Fact]
    public async Task an_abandoned_stale_input_sends_no_report()
    {
        using var h = new Harness();
        h.MakeReady();
        h.Io.FetchFault = _ => new JobAbandonException(AbortReason.StaleInput);

        var (_, lease) = await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), CanaryPdf.Build()));

        Assert.Empty(h.Api.Completes);
        Assert.Empty(h.Api.Fails);
        Assert.Equal(AbortReason.StaleInput, lease.Reason);
    }

    [Fact]
    public async Task a_422_on_complete_is_logged_and_never_retried()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        h.Io.Inputs["pdf"] = pdf;
        h.Api.OnComplete = (_, _) => Api.Error<CompleteResponse>(ApiKind.Unprocessable, 422, "result_invalid");

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), pdf));

        Assert.Single(h.Api.Completes);
        Assert.Empty(h.Api.Fails);
    }

    [Fact]
    public async Task a_too_large_result_is_reported_as_limits_exceeded()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        h.Io.Inputs["pdf"] = pdf;
        h.Api.OnComplete = (_, _) => Api.Error<CompleteResponse>(ApiKind.PayloadTooLarge, 413, "result_too_large");

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), pdf));

        Assert.Equal(FailCodes.LimitsExceeded, Assert.Single(h.Api.Fails).Request.Code);
    }

    [Fact]
    public async Task the_frozen_applies_switch_stops_the_agent_without_retry_or_fail()
    {
        using var h = new Harness();
        h.MakeReady();
        var pdf = CanaryPdf.Build();
        h.Io.Inputs["pdf"] = pdf;
        h.Api.OnComplete = (_, _) => Api.Error<CompleteResponse>(ApiKind.ServerError, 503, "applies_frozen");

        await RunAsync(h, TestJobs.Pdf(TestIds.Job(1), pdf));

        Assert.Single(h.Api.Completes);
        Assert.Empty(h.Api.Fails);
    }

    [Fact]
    public async Task RW087_a_lost_success_response_replays_against_the_real_http_client()
    {
        // Same runner, real HttpRemoteWorkerApi against the in-memory server: the first complete is processed but its
        // answer is lost; the retry must hit the replay path, not apply twice.
        var server = new FakeServerHandler { DropCompleteResponses = 1 };
        var pdf = CanaryPdf.Build();
        server.Enqueue(new ServerJob { Id = TestIds.Job(7), Inputs = { ["pdf"] = pdf } });
        var http = new HttpClient(server) { BaseAddress = new Uri("https://api.example.test"), Timeout = Timeout.InfiniteTimeSpan };
        using var api = new HttpRemoteWorkerApi(http, TestIds.NodeToken(), "1.0.0", new ProtocolNegotiator());
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("scratch"));
        Directory.CreateDirectory(dir.File("tmp"));
        var clock = new FakeClock();
        var status = new AgentStatus(TestLog.Instance, TestIds.Digest, "1.0.0");
        status.OnNodeHeartbeatOk(new NodeHeartbeatResponse { Node = new NodeRef { Status = "Active" }, Desired = Api.Desired() });
        status.SetSelfCheck(true);
        var executors = new ExecutorRegistry();
        executors.Replace([new PdfChildExecutor(JobKinds.PdfExtract, EngineVersions.Pdf, new InProcessChildRunner(), TestLog.Instance)]);
        var capacity = new CapacityAccountant(new Budgets { CpuMilli = 3000, MemMiB = 5120, TmpMiB = 3072 });
        var cache = new InputCache(Path.Combine(dir.File("scratch"), ".cache"), 0, 0, TimeSpan.Zero, clock);
        var io = new ApiJobIo(api, cache, status, clock, TestLog.Instance, (_, _) => Task.CompletedTask);
        var runner = new JobRunner(api, executors, new ScratchManager(dir.File("scratch"), dir.File("tmp"), TestLog.Instance), io, status, capacity, clock, TestLog.Instance, (_, _) => Task.CompletedTask);

        var claim = await api.ClaimAsync(new ClaimRequest { ClaimId = Guid.NewGuid().ToString("D"), InstanceId = Guid.NewGuid().ToString("D") }, CancellationToken.None);
        var job = claim.Value!.Job!;
        await runner.RunAsync(job, new JobLease(job.Id, job.Fence, job.HeartbeatEverySeconds, clock.NowMs, clock.NowMs + 115_000));

        var completes = server.Requests.Where(r => r.Path.EndsWith("/complete", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, completes.Count);
        Assert.Equal(completes[0].Body, completes[1].Body);
        Assert.NotNull(server.Job(job.Id).CompletedHash);
        Assert.Empty(server.Requests.Where(r => r.Path.EndsWith("/fail", StringComparison.Ordinal)));
    }

    /// <summary>Waits until cancelled, like a long extraction that honours its token.</summary>
    private sealed class CancelAwareExecutor : IJobExecutor
    {
        private static volatile bool _started;

        public static bool Started => _started;

        public string Kind => JobKinds.PdfExtract;
        public int SchemaVersion => 1;
        public string EngineVersion => EngineVersions.Pdf;

        public async Task<ExecutionResult> ExecuteAsync(JobContext context, CancellationToken ct)
        {
            _started = true;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                _started = false;
            }

            return ExecutionResult.Ok([], null, 0, 0);
        }
    }
}
