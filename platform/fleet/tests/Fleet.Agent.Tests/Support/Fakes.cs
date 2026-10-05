using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fleet.Agent.Tests;

internal sealed class FakeClock : IMonotonicClock
{
    public long NowMs { get; set; } = 1_000_000;

    public void Advance(long milliseconds) => NowMs += milliseconds;

    public void Advance(TimeSpan span) => NowMs += (long)span.TotalMilliseconds;
}

/// <summary>Identifiers and secrets for tests. Tokens are assembled at run time so no literal token exists in the source tree.</summary>
internal static class TestIds
{
    public const string NodeId = "rw_01j9z3k8m2q4x7v5b6n0c1d2ef";

    public static readonly string Digest = "sha256:" + new string('e', 64);

    public static string Job(int n) => "rj_" + n.ToString("D26", System.Globalization.CultureInfo.InvariantCulture);

    public static string NodeToken() => "orw1_" + new string('a', 16) + "_" + new string('B', 43);

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal static class TestLog
{
    public static ILogger Instance => NullLogger.Instance;
}

internal static class TestPaths
{
    private static string? _root;

    /// <summary>Walks up from the test binary to the repository root (the folder holding global.json).</summary>
    public static string RepoRoot()
    {
        if (_root is not null) return _root;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json"))) directory = directory.Parent;
        _root = directory?.FullName ?? throw new InvalidOperationException("repository root not found");
        return _root;
    }

    public static string Combine(params string[] parts) => Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
}

internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fleet-agent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class TestJobs
{
    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static ClaimedJob Pdf(string id, byte[] pdf, string kind = JobKinds.PdfExtract, string? parameters = null, long fence = 1, string? engine = null) => new()
    {
        Id = id,
        Kind = kind,
        SchemaVersion = 1,
        Purpose = "apply",
        EngineVersion = engine ?? (kind == JobKinds.PdfExtract ? EngineVersions.Pdf : EngineVersions.CompanionIndexPrep),
        Attempt = 1,
        MaxAttempts = 3,
        Fence = fence,
        LeaseRemainingMs = 120_000,
        HeartbeatEverySeconds = 20,
        DeadlineSeconds = 300,
        Inputs = [new InputRef { Name = "pdf", SizeBytes = pdf.Length, Sha256 = TestIds.Sha(pdf), ContentType = "application/pdf" }],
        Params = Json(parameters ?? "{\"mode\":\"flat\",\"minTextLength\":5,\"provider\":\"auto\",\"includePages\":true}"),
        Limits = new JobLimits
        {
            Weight = 1, CpuMilli = 1000, MemMiB = 2048, TmpMiB = 256, TimeoutSeconds = 120,
            MaxInputBytes = 104_857_600, MaxResultBytes = 8_388_608,
        },
    };

    public static JobLease Lease(ClaimedJob job, FakeClock clock) =>
        new(job.Id, job.Fence, job.HeartbeatEverySeconds, clock.NowMs, clock.NowMs + job.LeaseRemainingMs - Wire.LeaseSafetyMarginMs);
}

/// <summary>Scripted API: every endpoint is a delegate, every call is recorded. Defaults answer "ok / no work".</summary>
internal sealed class FakeApi : IRemoteWorkerApi
{
    public int CurrentProtocol { get; set; } = 1;

    public List<NodeHeartbeatRequest> NodeHeartbeats { get; } = [];
    public List<ClaimRequest> Claims { get; } = [];
    public List<(string JobId, JobHeartbeatRequest Request)> JobHeartbeats { get; } = [];
    public List<(string JobId, byte[] Body)> Completes { get; } = [];
    public List<(string JobId, FailRequest Request)> Fails { get; } = [];
    public List<(string JobId, string Name, string Sha)> Outputs { get; } = [];

    public Func<NodeHeartbeatRequest, ApiResponse<NodeHeartbeatResponse>> OnNodeHeartbeat { get; set; } = _ => Api.Ok(new NodeHeartbeatResponse
    {
        Node = new NodeRef { Id = TestIds.NodeId, Status = "Active" },
        Desired = Api.Desired(),
    });

    public Func<ClaimRequest, ApiResponse<ClaimResponse>> OnClaim { get; set; } = _ => Api.Empty<ClaimResponse>("no_work", 10);

    public Func<string, JobHeartbeatRequest, ApiResponse<JobHeartbeatResponse>> OnJobHeartbeat { get; set; } =
        (_, _) => Api.Ok(new JobHeartbeatResponse { LeaseRemainingMs = 120_000 });

    public Func<string, byte[], ApiResponse<CompleteResponse>> OnComplete { get; set; } =
        (_, _) => Api.Ok(new CompleteResponse { Status = "succeeded", Outcome = "Applied" });

    public Func<string, FailRequest, ApiResponse<FailResponse>> OnFail { get; set; } =
        (_, _) => Api.Ok(new FailResponse { Status = "queued" });

    public Func<string, string, long, long, string?, InputResponse> OnOpenInput { get; set; } =
        (_, _, _, _, _) => InputResponse.Failed(ApiResponse.Failure(ApiKind.NotFound, 404, "input_not_found"));

    public Func<string, string, long, string, string, ApiResponse<OutputAck>> OnPutOutput { get; set; } = (_, name, _, sha, path) =>
        Api.Ok(new OutputAck { Name = name, Sha256 = sha, SizeBytes = new FileInfo(path).Length });

    public Task<ApiResponse<NodeHeartbeatResponse>> NodeHeartbeatAsync(NodeHeartbeatRequest request, CancellationToken ct)
    {
        NodeHeartbeats.Add(request);
        return Task.FromResult(OnNodeHeartbeat(request));
    }

    public Task<ApiResponse<ClaimResponse>> ClaimAsync(ClaimRequest request, CancellationToken ct)
    {
        Claims.Add(request);
        return Task.FromResult(OnClaim(request));
    }

    public Task<ApiResponse<JobHeartbeatResponse>> JobHeartbeatAsync(string jobId, JobHeartbeatRequest request, CancellationToken ct)
    {
        lock (JobHeartbeats) JobHeartbeats.Add((jobId, request));
        return Task.FromResult(OnJobHeartbeat(jobId, request));
    }

    public Task<ApiResponse<CompleteResponse>> CompleteAsync(string jobId, byte[] body, CancellationToken ct)
    {
        Completes.Add((jobId, body));
        return Task.FromResult(OnComplete(jobId, body));
    }

    public Task<ApiResponse<FailResponse>> FailAsync(string jobId, FailRequest request, TimeSpan? timeout, CancellationToken ct)
    {
        Fails.Add((jobId, request));
        return Task.FromResult(OnFail(jobId, request));
    }

    public Task<InputResponse> OpenInputAsync(string jobId, string name, long fence, long offset, string? ifRangeEtag, CancellationToken ct) =>
        Task.FromResult(OnOpenInput(jobId, name, fence, offset, ifRangeEtag));

    public Task<ApiResponse<OutputAck>> PutOutputAsync(string jobId, string name, long fence, string sha256Hex, string filePath, CancellationToken ct)
    {
        Outputs.Add((jobId, name, sha256Hex));
        return Task.FromResult(OnPutOutput(jobId, name, fence, sha256Hex, filePath));
    }
}

internal static class Api
{
    public static ApiResponse<T> Ok<T>(T value) where T : class => new() { Kind = ApiKind.Ok, Status = 200, Value = value };

    public static ApiResponse<T> Empty<T>(string reason, int retryAfterSeconds) where T : class => new()
    {
        Kind = ApiKind.NoContent,
        Status = 204,
        NoContentReason = reason,
        RetryAfter = TimeSpan.FromSeconds(retryAfterSeconds),
    };

    public static ApiResponse<T> Error<T>(ApiKind kind, int status, string? code = null, string? reason = null, int? retryAfterSeconds = null) where T : class => new()
    {
        Kind = kind,
        Status = status,
        Code = code,
        Reason = reason,
        RetryAfter = retryAfterSeconds is { } s ? TimeSpan.FromSeconds(s) : null,
    };

    public static DesiredState Desired(long revision = 1, bool drain = false, bool paused = false, string[]? kinds = null, int maxConcurrency = 2) => new()
    {
        Revision = revision,
        Drain = drain,
        Paused = paused,
        AllowedKinds = (kinds ?? [JobKinds.PdfExtract]).ToList(),
        MaxConcurrency = maxConcurrency,
        PerKind = new Dictionary<string, int> { [JobKinds.PdfExtract] = maxConcurrency },
        Budgets = new Budgets { CpuMilli = 3000, MemMiB = 5120, TmpMiB = 3072 },
        Pressure = new PressureSettings(),
        PollSeconds = new PollSettings(),
        AgentImage = new AgentImageDesired { ApprovedDigests = [TestIds.Digest], Target = TestIds.Digest, MinVersion = "1.0.0" },
    };
}

internal sealed class FakeIo : IJobIo
{
    public Dictionary<string, byte[]> Inputs { get; } = new();
    public List<(string Name, byte[] Bytes)> Uploaded { get; } = [];
    public List<string> CacheOffers { get; } = [];
    public Func<InputRef, Exception?>? FetchFault { get; set; }

    public Task<InputFile> FetchInputAsync(ClaimedJob job, JobLease lease, InputRef input, string destinationPath, bool cacheable, CancellationToken ct)
    {
        if (FetchFault?.Invoke(input) is { } fault) throw fault;
        var bytes = Inputs[input.Name];
        File.WriteAllBytes(destinationPath, bytes);
        return Task.FromResult(new InputFile(destinationPath, bytes.Length, TestIds.Sha(bytes), false, null));
    }

    public Task<OutputRef> UploadOutputAsync(ClaimedJob job, JobLease lease, string name, string filePath, CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(filePath);
        Uploaded.Add((name, bytes));
        return Task.FromResult(new OutputRef { Name = name, SizeBytes = bytes.Length, Sha256 = TestIds.Sha(bytes) });
    }

    public void OfferToCache(string sha256, string verifiedFile, long size) => CacheOffers.Add(sha256);
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public Func<ProcessSpec, CancellationToken, Task<ProcessRunResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(new ProcessRunResult(0, false, ""));

    public List<string[]> Invocations { get; } = [];

    public Task<ProcessRunResult> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        Invocations.Add(spec.Arguments.ToArray());
        return Handler(spec, ct);
    }
}

/// <summary>Executes the child work in this process so executor tests need no second binary.</summary>
internal sealed class RecordingChildRunner : IChildRunner
{
    private readonly InProcessChildRunner _inner = new();

    public List<ChildRun> Runs { get; } = [];

    public Task<ChildOutcome> RunAsync(ChildRun run, JobLease? lease, CancellationToken ct)
    {
        Runs.Add(run);
        return _inner.RunAsync(run, lease, ct);
    }
}
