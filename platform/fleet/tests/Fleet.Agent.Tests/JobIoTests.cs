using System.Security.Cryptography;

namespace Fleet.Agent.Tests;

/// <summary>Input download, output upload and the input cache (protocol 4.3, 4.4; RW-060..RW-069).</summary>
public sealed class JobIoTests
{
    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Clock = new FakeClock();
            Dir = new TempDir();
            Directory.CreateDirectory(Dir.File("scratch"));
            Cache = new InputCache(Dir.File("cache"), 10_000_000, 5_000_000, TimeSpan.FromMinutes(10), Clock);
            Status = new AgentStatus(TestLog.Instance, TestIds.Digest, "1.0.0");
            Io = new ApiJobIo(Api, Cache, Status, Clock, TestLog.Instance, (_, _) => Task.CompletedTask);
        }

        public FakeClock Clock { get; }
        public TempDir Dir { get; }
        public FakeApi Api { get; } = new();
        public InputCache Cache { get; }
        public AgentStatus Status { get; }
        public ApiJobIo Io { get; }
        public int OpenCalls { get; set; }

        public ClaimedJob Job(byte[] data, long maxInput = 100_000_000)
        {
            var job = TestJobs.Pdf(TestIds.Job(1), data);
            job.Limits.MaxInputBytes = maxInput;
            return job;
        }

        public void Dispose() => Dir.Dispose();
    }

    private static InputResponse Body(byte[] data, string? sha = null, Stream? stream = null, bool partial = false, long? start = null) =>
        new(new ApiResponse { Kind = ApiKind.Ok, Status = partial ? 206 : 200 }, stream ?? new MemoryStream(data), data.Length, sha ?? TestIds.Sha(data), TestIds.Sha(data), partial, start, null);

    private static byte[] Bytes(int length)
    {
        var data = new byte[length];
        new Random(42).NextBytes(data);
        return data;
    }

    /// <summary>A stream that delivers part of the data and then fails like a dropped connection.</summary>
    private sealed class FlakyStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _failAfter;
        private int _position;

        public FlakyStream(byte[] data, int failAfter)
        {
            _data = data;
            _failAfter = failAfter;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _failAfter) throw new IOException("connection reset");
            var take = Math.Min(Math.Min(buffer.Length, 4096), _failAfter - _position);
            _data.AsMemory(_position, take).CopyTo(buffer);
            _position += take;
            return ValueTask.FromResult(take);
        }
    }

    [Fact]
    public async Task RW068_a_download_is_streamed_hashed_verified_and_moved_into_place()
    {
        using var rig = new Rig();
        var data = Bytes(300_000);
        rig.Api.OnOpenInput = (_, _, fence, offset, _) => Body(data);
        var job = rig.Job(data);

        using var file = await rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), cacheable: false, CancellationToken.None);

        Assert.Equal(data, File.ReadAllBytes(file.Path));
        Assert.Equal(TestIds.Sha(data), file.Sha256);
        Assert.False(file.FromCache);
        Assert.False(File.Exists(rig.Dir.File("scratch/in.pdf.part")));
    }

    [Fact]
    public async Task RW068_inputs_above_the_limit_are_refused_without_any_download()
    {
        using var rig = new Rig();
        var data = Bytes(1000);
        rig.Api.OnOpenInput = (_, _, _, _, _) =>
        {
            rig.OpenCalls++;
            return Body(data);
        };
        var job = rig.Job(data, maxInput: 999);

        var failure = await Assert.ThrowsAsync<JobFailureException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        Assert.Equal(FailCodes.InputTooLarge, failure.Code);
        Assert.False(failure.Retryable);
        Assert.Equal(0, rig.OpenCalls);
    }

    [Fact]
    public async Task RW068_a_digest_mismatch_is_a_retryable_input_hash_mismatch_and_leaves_nothing_behind()
    {
        using var rig = new Rig();
        var data = Bytes(5000);
        var tampered = (byte[])data.Clone();
        tampered[10] ^= 0xFF;
        rig.Api.OnOpenInput = (_, _, _, _, _) => Body(tampered, sha: TestIds.Sha(data));
        var job = rig.Job(data);

        var failure = await Assert.ThrowsAsync<JobFailureException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        Assert.Equal(FailCodes.InputHashMismatch, failure.Code);
        Assert.True(failure.Retryable);
        Assert.Empty(Directory.GetFiles(rig.Dir.File("scratch")));
    }

    [Fact]
    public async Task a_short_or_long_body_is_an_input_hash_mismatch()
    {
        using var rig = new Rig();
        var data = Bytes(5000);
        var job = rig.Job(data);

        rig.Api.OnOpenInput = (_, _, _, _, _) => Body(data[..4000], sha: TestIds.Sha(data));
        var shortFailure = await Assert.ThrowsAnyAsync<JobFailureException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        rig.Api.OnOpenInput = (_, _, _, _, _) => Body(data.Concat(new byte[10]).ToArray(), sha: TestIds.Sha(data));
        var longFailure = await Assert.ThrowsAnyAsync<JobFailureException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        Assert.Equal(FailCodes.InputHashMismatch, shortFailure.Code);
        Assert.Equal(FailCodes.InputHashMismatch, longFailure.Code);
    }

    [Fact]
    public async Task RW069_an_interrupted_download_resumes_with_a_range_inside_the_same_lease()
    {
        using var rig = new Rig();
        var data = Bytes(200_000);
        var offsets = new List<long>();
        rig.Api.OnOpenInput = (_, _, _, offset, _) =>
        {
            offsets.Add(offset);
            return offset == 0
                ? Body(data, stream: new FlakyStream(data, failAfter: 60_000))
                : Body(data[(int)offset..], partial: true, start: offset);
        };
        var job = rig.Job(data);

        using var file = await rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None);

        Assert.Equal(data, File.ReadAllBytes(file.Path));
        Assert.Equal(2, offsets.Count);
        Assert.Equal(0, offsets[0]);
        Assert.InRange(offsets[1], 1, 199_999);
    }

    [Fact]
    public async Task a_server_that_ignores_the_range_makes_the_agent_start_over_cleanly()
    {
        using var rig = new Rig();
        var data = Bytes(100_000);
        var calls = 0;
        rig.Api.OnOpenInput = (_, _, _, _, _) => ++calls == 1 ? Body(data, stream: new FlakyStream(data, 30_000)) : Body(data);
        var job = rig.Job(data);

        using var file = await rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None);

        Assert.Equal(data, File.ReadAllBytes(file.Path));
        Assert.Equal(2, calls);
    }

    // Theory parameters are plain strings: the agent's enums are internal and a public test method cannot expose them.
    [Theory]
    [InlineData("lease_lost", "LeaseLost")]
    [InlineData("stale_input", "StaleInput")]
    [InlineData("resource_gone", "StaleInput")]
    public async Task conflicts_on_download_abandon_the_job_without_a_report(string code, string expectedReason)
    {
        var expected = Enum.Parse<AbortReason>(expectedReason);
        using var rig = new Rig();
        var data = Bytes(1000);
        rig.Api.OnOpenInput = (_, _, _, _, _) => InputResponse.Failed(new ApiResponse { Kind = ApiKind.Conflict, Status = 409, Code = code });
        var job = rig.Job(data);

        var abandon = await Assert.ThrowsAsync<JobAbandonException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        Assert.Equal(expected, abandon.Reason);
    }

    [Fact]
    public async Task an_unauthorized_download_puts_the_agent_into_auth_failed()
    {
        using var rig = new Rig();
        var data = Bytes(1000);
        rig.Api.OnOpenInput = (_, _, _, _, _) => InputResponse.Failed(ApiResponse.Failure(ApiKind.Unauthorized, 401));
        var job = rig.Job(data);

        var abandon = await Assert.ThrowsAsync<JobAbandonException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        Assert.Equal(AbortReason.AuthFailed, abandon.Reason);
        Assert.Equal(AgentState.AuthFailed, rig.Status.Decision.State);
    }

    [Fact]
    public async Task persistent_transient_errors_end_as_a_retryable_input_unavailable_after_bounded_attempts()
    {
        using var rig = new Rig();
        var data = Bytes(1000);
        var calls = 0;
        rig.Api.OnOpenInput = (_, _, _, _, _) =>
        {
            calls++;
            return InputResponse.Failed(ApiResponse.Failure(ApiKind.Network));
        };
        var job = rig.Job(data);

        var failure = await Assert.ThrowsAsync<JobFailureException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        Assert.Equal(FailCodes.InputUnavailable, failure.Code);
        Assert.True(failure.Retryable);
        Assert.Equal(5, calls);
    }

    [Fact]
    public async Task a_manifest_with_an_unsafe_input_name_is_refused()
    {
        using var rig = new Rig();
        var data = Bytes(10);
        var job = rig.Job(data);
        job.Inputs[0].Name = "../../etc/passwd";

        var failure = await Assert.ThrowsAsync<JobFailureException>(() =>
            rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), false, CancellationToken.None));

        Assert.Equal(FailCodes.InternalError, failure.Code);
    }

    [Fact]
    public async Task a_cache_hit_skips_the_download_and_pins_the_entry_until_disposed()
    {
        using var rig = new Rig();
        var data = Bytes(50_000);
        var job = rig.Job(data);
        var staging = rig.Dir.File("staged.pdf");
        File.WriteAllBytes(staging, data);
        Assert.True(rig.Cache.TryAdopt(TestIds.Sha(data), staging, data.Length));
        rig.Api.OnOpenInput = (_, _, _, _, _) =>
        {
            rig.OpenCalls++;
            return Body(data);
        };

        var file = await rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), cacheable: true, CancellationToken.None);

        Assert.True(file.FromCache);
        Assert.Equal(0, rig.OpenCalls);
        Assert.Equal(data, File.ReadAllBytes(file.Path));
        rig.Cache.Clear();
        Assert.True(File.Exists(file.Path)); // pinned: not evicted while a job reads it
        file.Dispose();
        rig.Cache.Clear();
        Assert.False(File.Exists(file.Path));
    }

    [Fact]
    public async Task a_non_cacheable_fetch_never_consults_the_cache()
    {
        using var rig = new Rig();
        var data = Bytes(2000);
        var job = rig.Job(data);
        var staging = rig.Dir.File("staged.pdf");
        File.WriteAllBytes(staging, data);
        rig.Cache.TryAdopt(TestIds.Sha(data), staging, data.Length);
        rig.Api.OnOpenInput = (_, _, _, _, _) =>
        {
            rig.OpenCalls++;
            return Body(data);
        };

        using var file = await rig.Io.FetchInputAsync(job, TestJobs.Lease(job, rig.Clock), job.Inputs[0], rig.Dir.File("scratch/in.pdf"), cacheable: false, CancellationToken.None);

        Assert.False(file.FromCache);
        Assert.Equal(1, rig.OpenCalls);
    }

    // ---- outputs -------------------------------------------------------------------------------------------

    [Fact]
    public async Task RW066_an_output_is_uploaded_with_its_hash_and_the_acknowledgement_is_checked()
    {
        using var rig = new Rig();
        var data = Bytes(1234);
        var path = rig.Dir.File("chunk-0000.mp3");
        File.WriteAllBytes(path, data);
        var job = rig.Job(data);

        var output = await rig.Io.UploadOutputAsync(job, TestJobs.Lease(job, rig.Clock), "chunk-0000.mp3", path, CancellationToken.None);

        Assert.Equal((1234L, TestIds.Sha(data)), (output.SizeBytes, output.Sha256));
        Assert.Equal(("chunk-0000.mp3", TestIds.Sha(data)), (rig.Api.Outputs[0].Name, rig.Api.Outputs[0].Sha));
    }

    [Fact]
    public async Task an_output_upload_is_retried_on_transient_errors_until_it_lands()
    {
        using var rig = new Rig();
        var data = Bytes(100);
        var path = rig.Dir.File("o.bin");
        File.WriteAllBytes(path, data);
        var job = rig.Job(data);
        var calls = 0;
        rig.Api.OnPutOutput = (_, name, _, sha, p) => ++calls < 3
            ? Api.Error<OutputAck>(ApiKind.ServerError, 503, "storage_unavailable")
            : Api.Ok(new OutputAck { Name = name, Sha256 = sha, SizeBytes = new FileInfo(p).Length });

        await rig.Io.UploadOutputAsync(job, TestJobs.Lease(job, rig.Clock), "o.bin", path, CancellationToken.None);

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task an_output_upload_stops_at_the_local_lease_expiry()
    {
        using var rig = new Rig();
        var data = Bytes(100);
        var path = rig.Dir.File("o.bin");
        File.WriteAllBytes(path, data);
        var job = rig.Job(data);
        var lease = TestJobs.Lease(job, rig.Clock);
        rig.Clock.Advance(TimeSpan.FromSeconds(200));

        var abandon = await Assert.ThrowsAsync<JobAbandonException>(() => rig.Io.UploadOutputAsync(job, lease, "o.bin", path, CancellationToken.None));

        Assert.Equal(AbortReason.LocalExpiry, abandon.Reason);
        Assert.Empty(rig.Api.Outputs);
    }

    [Theory]
    [InlineData("PayloadTooLarge", FailCodes.LimitsExceeded)]
    [InlineData("Unprocessable", FailCodes.InternalError)]
    [InlineData("Forbidden", FailCodes.InternalError)]
    public async Task rejected_outputs_map_to_agent_failures(string kindName, string expectedCode)
    {
        var kind = Enum.Parse<ApiKind>(kindName);
        using var rig = new Rig();
        var data = Bytes(100);
        var path = rig.Dir.File("o.bin");
        File.WriteAllBytes(path, data);
        var job = rig.Job(data);
        rig.Api.OnPutOutput = (_, _, _, _, _) => Api.Error<OutputAck>(kind, 400, kind == ApiKind.Forbidden ? "outputs_not_permitted" : "x");

        var failure = await Assert.ThrowsAsync<JobFailureException>(() => rig.Io.UploadOutputAsync(job, TestJobs.Lease(job, rig.Clock), "o.bin", path, CancellationToken.None));

        Assert.Equal(expectedCode, failure.Code);
    }

    [Fact]
    public async Task a_lease_lost_on_upload_abandons_the_job()
    {
        using var rig = new Rig();
        var data = Bytes(100);
        var path = rig.Dir.File("o.bin");
        File.WriteAllBytes(path, data);
        var job = rig.Job(data);
        rig.Api.OnPutOutput = (_, _, _, _, _) => Api.Error<OutputAck>(ApiKind.Conflict, 409, "lease_lost");

        var abandon = await Assert.ThrowsAsync<JobAbandonException>(() => rig.Io.UploadOutputAsync(job, TestJobs.Lease(job, rig.Clock), "o.bin", path, CancellationToken.None));

        Assert.Equal(AbortReason.LeaseLost, abandon.Reason);
    }

    // ---- input cache ---------------------------------------------------------------------------------------

    private static (InputCache Cache, FakeClock Clock, TempDir Dir) NewCache(long max = 1000, long maxEntry = 600, int ttlSeconds = 60)
    {
        var clock = new FakeClock();
        var dir = new TempDir();
        return (new InputCache(dir.File("cache"), max, maxEntry, TimeSpan.FromSeconds(ttlSeconds), clock), clock, dir);
    }

    private static string Stage(TempDir dir, string name, int size)
    {
        var path = dir.File(name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    [Fact]
    public void the_cache_serves_only_an_unexpired_entry_of_the_exact_size()
    {
        var (cache, clock, dir) = NewCache();
        using var owned = dir;
        Assert.True(cache.TryAdopt("a", Stage(dir, "a.bin", 400), 400));

        Assert.Null(cache.TryAcquire("a", 399));
        Assert.True(cache.TryAdopt("b", Stage(dir, "b.bin", 400), 400));
        using (var hit = cache.TryAcquire("b", 400)) Assert.NotNull(hit);

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(cache.TryAcquire("b", 400));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void the_cache_is_size_capped_evicts_the_least_recently_used_and_never_a_pinned_entry()
    {
        var (cache, clock, dir) = NewCache(max: 1000, maxEntry: 600);
        using var owned = dir;
        Assert.True(cache.TryAdopt("a", Stage(dir, "a.bin", 450), 450));
        clock.Advance(1000);
        Assert.True(cache.TryAdopt("b", Stage(dir, "b.bin", 450), 450));
        clock.Advance(1000);
        using (var touch = cache.TryAcquire("a", 450)) Assert.NotNull(touch); // a is now more recent than b
        clock.Advance(1000);

        Assert.True(cache.TryAdopt("c", Stage(dir, "c.bin", 450), 450)); // evicts b
        Assert.Null(cache.TryAcquire("b", 450));
        using var pinned = cache.TryAcquire("a", 450);
        Assert.NotNull(pinned);

        // a is pinned, so admitting d evicts the unpinned c and keeps a.
        Assert.True(cache.TryAdopt("d", Stage(dir, "d.bin", 450), 450));
        Assert.Null(cache.TryAcquire("c", 450));
        using var pinnedD = cache.TryAcquire("d", 450);
        Assert.NotNull(pinnedD);

        // a and d are both pinned: nothing is evictable, so e is not admitted and its file stays where it was.
        var staged = Stage(dir, "e.bin", 450);
        Assert.False(cache.TryAdopt("e", staged, 450));
        Assert.True(File.Exists(staged));
        Assert.True(cache.TotalBytes <= 1000);
    }

    [Fact]
    public void entries_larger_than_the_per_entry_cap_or_a_disabled_cache_are_not_adopted()
    {
        var (cache, _, dir) = NewCache(max: 1000, maxEntry: 600);
        using var owned = dir;
        var big = Stage(dir, "big.bin", 700);

        Assert.False(cache.TryAdopt("big", big, 700));
        Assert.True(File.Exists(big));

        var disabled = new InputCache(dir.File("c2"), 0, 0, TimeSpan.Zero, new FakeClock());
        Assert.False(disabled.Enabled);
        Assert.False(disabled.TryAdopt("x", Stage(dir, "x.bin", 10), 10));
        Assert.Null(disabled.TryAcquire("x", 10));
    }

    // ---- scratch -------------------------------------------------------------------------------------------

    [Fact]
    public void RW150_start_up_wipes_scratch_and_tmp_but_leaves_runtime_owned_names_alone()
    {
        using var dir = new TempDir();
        var scratch = dir.File("scratch");
        var tmp = dir.File("tmp");
        Directory.CreateDirectory(Path.Combine(scratch, "rj_old-1"));
        File.WriteAllText(Path.Combine(scratch, "rj_old-1", "in.pdf"), "x");
        Directory.CreateDirectory(tmp);
        File.WriteAllText(Path.Combine(tmp, "leftover.txt"), "x");
        Directory.CreateDirectory(Path.Combine(tmp, ".dotnet"));
        var manager = new ScratchManager(scratch, tmp, TestLog.Instance);

        manager.WipeAtStart();

        Assert.Equal(new[] { Path.Combine(scratch, ".cache") }, Directory.GetFileSystemEntries(scratch));
        Assert.Equal(new[] { Path.Combine(tmp, ".dotnet") }, Directory.GetFileSystemEntries(tmp));
    }

    [Fact]
    public void job_directories_are_named_by_job_and_fence_removed_idempotently_and_reject_unsafe_ids()
    {
        using var dir = new TempDir();
        var manager = new ScratchManager(dir.File("scratch"), dir.File("tmp"), TestLog.Instance);
        Directory.CreateDirectory(dir.File("scratch"));

        var job = manager.CreateJobDirectory(TestIds.Job(5), 7);

        Assert.Equal(TestIds.Job(5) + "-7", Path.GetFileName(job));
        File.WriteAllText(Path.Combine(job, "a.bin"), "x");
        manager.DeleteJobDirectory(job);
        manager.DeleteJobDirectory(job);
        manager.DeleteJobDirectory(null);
        Assert.False(Directory.Exists(job));
        Assert.Throws<ArgumentException>(() => manager.CreateJobDirectory("../../etc", 1));
    }

    [Fact]
    public async Task sha256_of_a_file_is_computed_by_streaming()
    {
        using var dir = new TempDir();
        var data = Bytes(1_000_000);
        File.WriteAllBytes(dir.File("f.bin"), data);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), await Hashing.Sha256FileAsync(dir.File("f.bin"), CancellationToken.None));
    }
}
