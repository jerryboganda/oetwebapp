namespace Fleet.Agent;

/// <summary>
/// Executor of the two PdfPig-backed kinds, pdf.extract and companion.index-prep (protocol 6.1, 6.2): download the single
/// "pdf" input, run the CPU-heavy part in an isolated child process, return the child's result bytes untouched.
/// </summary>
internal sealed class PdfChildExecutor : IJobExecutor
{
    private readonly IChildRunner _runner;
    private readonly ILogger _log;

    public PdfChildExecutor(string kind, string engineVersion, IChildRunner runner, ILogger log)
    {
        Kind = kind;
        EngineVersion = engineVersion;
        _runner = runner;
        _log = log;
    }

    public string Kind { get; }
    public int SchemaVersion => 1;
    public string EngineVersion { get; }

    public async Task<ExecutionResult> ExecuteAsync(JobContext context, CancellationToken ct)
    {
        var job = context.Job;
        if (job.Inputs.Count != 1 || job.Inputs[0].Name != "pdf")
        {
            throw new JobFailureException(FailCodes.InternalError, false, "unexpected input manifest");
        }

        var minText = ParamReader.Int(job.Params, "minTextLength", 50, 1, 1_000_000);
        var mode = "pages";
        var includePages = true;
        if (Kind == JobKinds.PdfExtract)
        {
            mode = ParamReader.String(job.Params, "mode", "flat");
            if (mode is not ("flat" or "pages")) throw new JobFailureException(FailCodes.InternalError, false, "unsupported mode");
            includePages = ParamReader.Bool(job.Params, "includePages", true);
        }
        else if (ParamReader.String(job.Params, "chunkerVersion", EngineVersions.CompanionChunkerVersion) != EngineVersions.CompanionChunkerVersion)
        {
            throw new JobFailureException(FailCodes.InternalError, false, "unsupported chunker version");
        }

        var input = job.Inputs[0];
        context.Lease.Stage = "downloading";
        using var file = await context.Io.FetchInputAsync(job, context.Lease, input, Path.Combine(context.ScratchDirectory, "in.pdf"), cacheable: true, ct).ConfigureAwait(false);

        context.Lease.Stage = "extracting";
        var outputPath = Path.Combine(context.ScratchDirectory, "result.json");
        var run = new ChildRun(
            Kind,
            file.Path,
            outputPath,
            new ChildParams
            {
                Mode = mode,
                MinTextLength = minText,
                IncludePages = includePages,
                EngineVersion = job.EngineVersion,
                InputSha256 = input.Sha256,
                TimeoutSeconds = Math.Max(1, job.Limits.TimeoutSeconds),
            },
            job.Limits.MemMiB,
            TimeSpan.FromSeconds(Math.Max(1, job.Limits.TimeoutSeconds)));

        var outcome = await _runner.RunAsync(run, context.Lease, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var failure = MapExit(outcome);
        if (failure is not null)
        {
            _log.LogWarning("child worker failed kind={Kind} exit={Exit} timedOut={TimedOut}", Kind, outcome.ExitCode, outcome.TimedOut);
            return failure;
        }

        var bytes = await File.ReadAllBytesAsync(outputPath, ct).ConfigureAwait(false);
        if (bytes.Length > job.Limits.MaxResultBytes)
        {
            return ExecutionResult.Fail(FailCodes.LimitsExceeded, false, "result larger than limits.maxResultBytes");
        }

        // A verified, finished input may serve the next job over the same asset: adopt it (rename, no copy).
        if (!file.FromCache) context.Io.OfferToCache(input.Sha256, file.Path, file.Size);
        return ExecutionResult.Ok(bytes, null, file.Size, outcome.PeakRssMiB);
    }

    /// <summary>Maps the child exit status onto the agent fail codes of section 4.6.</summary>
    internal static ExecutionResult? MapExit(ChildOutcome outcome)
    {
        if (outcome.TimedOut) return ExecutionResult.Fail(FailCodes.Timeout, true, "extraction exceeded the job timeout");
        return outcome.ExitCode switch
        {
            ChildExit.Ok => null,
            ChildExit.Timeout => ExecutionResult.Fail(FailCodes.Timeout, true, "extraction exceeded the job timeout"),
            ChildExit.OutOfMemory => ExecutionResult.Fail(FailCodes.Oom, true, "worker ran out of memory"),
            137 => ExecutionResult.Fail(FailCodes.Oom, true, "worker was killed for memory"),
            ChildExit.Unrepresentable => ExecutionResult.Fail(FailCodes.ExtractException, false, "result is not representable"),
            ChildExit.Unexpected => ExecutionResult.Fail(FailCodes.ExtractException, false, "worker raised an engine exception"),
            ChildExit.BadArguments => ExecutionResult.Fail(FailCodes.InternalError, true, "worker could not start"),
            _ => ExecutionResult.Fail(FailCodes.InternalError, true, "worker process crashed"),
        };
    }
}
