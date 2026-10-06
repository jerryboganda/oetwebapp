using System.Text.Json;

namespace Fleet.Agent;

/// <summary>A deterministic or retryable failure the agent reports with POST fail (section 4.6).</summary>
internal sealed class JobFailureException : Exception
{
    public JobFailureException(string code, bool retryable, string detail)
        : base(code)
    {
        Code = code;
        Retryable = retryable;
        Detail = detail;
    }

    public string Code { get; }
    public bool Retryable { get; }

    /// <summary>Fixed-vocabulary detail for fail.message; never content.</summary>
    public string Detail { get; }
}

/// <summary>The job must be dropped without any report (stale input, lease lost while fetching).</summary>
internal sealed class JobAbandonException : Exception
{
    public JobAbandonException(AbortReason reason)
        : base(reason.ToString())
    {
        Reason = reason;
    }

    public AbortReason Reason { get; }
}

/// <summary>What an executor returns: result bytes for complete, or a failure for fail.</summary>
internal sealed class ExecutionResult
{
    public bool Success { get; private init; }

    /// <summary>The exact UTF-8 bytes of the kind's result object; resultSha256 covers these bytes (4.5.1).</summary>
    public byte[] ResultUtf8 { get; private init; } = [];

    public List<OutputRef> Outputs { get; private init; } = [];
    public long InputBytes { get; init; }
    public long PeakRssMiB { get; init; }
    public string FailCode { get; private init; } = "";
    public bool Retryable { get; private init; }
    public string FailMessage { get; private init; } = "";

    public static ExecutionResult Ok(byte[] resultUtf8, List<OutputRef>? outputs, long inputBytes, long peakRssMiB) => new()
    {
        Success = true,
        ResultUtf8 = resultUtf8,
        Outputs = outputs ?? [],
        InputBytes = inputBytes,
        PeakRssMiB = peakRssMiB,
    };

    public static ExecutionResult Fail(string code, bool retryable, string message) => new()
    {
        Success = false,
        FailCode = code,
        Retryable = retryable,
        FailMessage = message,
    };
}

/// <summary>A downloaded input: a file whose size and SHA-256 were verified against the manifest.</summary>
internal sealed class InputFile : IDisposable
{
    private readonly IDisposable? _pin;

    public InputFile(string path, long size, string sha256, bool fromCache, IDisposable? pin)
    {
        Path = path;
        Size = size;
        Sha256 = sha256;
        FromCache = fromCache;
        _pin = pin;
    }

    public string Path { get; }
    public long Size { get; }
    public string Sha256 { get; }
    public bool FromCache { get; }

    public void Dispose() => _pin?.Dispose();
}

/// <summary>Job-scoped I/O an executor may use: fetch a manifest input, upload a binary output.</summary>
internal interface IJobIo
{
    Task<InputFile> FetchInputAsync(ClaimedJob job, JobLease lease, InputRef input, string destinationPath, bool cacheable, CancellationToken ct);

    Task<OutputRef> UploadOutputAsync(ClaimedJob job, JobLease lease, string name, string filePath, CancellationToken ct);

    /// <summary>After a verified, finished job the input file may be adopted by the cache (renamed, no copy).</summary>
    void OfferToCache(string sha256, string verifiedFile, long size);
}

internal sealed class JobContext
{
    public required ClaimedJob Job { get; init; }
    public required JobLease Lease { get; init; }
    public required string ScratchDirectory { get; init; }
    public required IJobIo Io { get; init; }
}

internal interface IJobExecutor
{
    string Kind { get; }
    int SchemaVersion { get; }

    /// <summary>The engineVersion this build offers for the kind (section 6.0); a job is accepted only when it matches.</summary>
    string EngineVersion { get; }

    Task<ExecutionResult> ExecuteAsync(JobContext context, CancellationToken ct);
}

/// <summary>Reads typed values out of the job's params object (section 6) with explicit defaults and ranges.</summary>
internal static class ParamReader
{
    public static int Int(JsonElement parameters, string name, int fallback, int min, int max)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed)) throw BadParam(name);
        if (parsed < min || parsed > max) throw BadParam(name);
        return parsed;
    }

    public static bool Bool(JsonElement parameters, string name, bool fallback)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw BadParam(name),
        };
    }

    public static string String(JsonElement parameters, string name, string fallback)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.String) throw BadParam(name);
        return value.GetString() ?? fallback;
    }

    private static JobFailureException BadParam(string name) =>
        new(FailCodes.InternalError, false, "unsupported job parameter " + name);
}
