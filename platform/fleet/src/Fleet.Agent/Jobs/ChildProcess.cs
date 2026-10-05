using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Fleet.Agent;

/// <summary>Exit codes of the isolated worker process (<c>Fleet.Agent --child</c>).</summary>
internal static class ChildExit
{
    public const int Ok = 0;
    public const int Unrepresentable = 10;
    public const int BadArguments = 11;
    public const int OutOfMemory = 12;
    public const int Unexpected = 13;
    public const int Timeout = 14;
}

/// <summary>Parameters handed to the child through a file (never argv: they can carry hashes and versions only).</summary>
internal sealed class ChildParams
{
    public string Mode { get; set; } = "flat";
    public int MinTextLength { get; set; } = 50;
    public bool IncludePages { get; set; } = true;
    public string EngineVersion { get; set; } = "";
    public string InputSha256 { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>The CPU-heavy part of the parsing kinds, runnable in the child process or in-process (tests).</summary>
internal static class ChildWork
{
    public static byte[] Execute(string kind, byte[] pdf, ChildParams parameters, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        switch (kind)
        {
            case JobKinds.PdfExtract:
            {
                var extraction = PdfExtractCore.Extract(pdf, parameters.Mode, parameters.MinTextLength, ct);
                var request = new PdfExtractRequest(parameters.Mode, parameters.MinTextLength, parameters.IncludePages, parameters.EngineVersion, parameters.InputSha256);
                return PdfExtractCore.Serialize(extraction, request, pdf.Length, started.ElapsedMilliseconds, PeakRssMiB());
            }

            case JobKinds.CompanionIndexPrep:
                return CompanionPrepCore.Build(pdf, parameters.MinTextLength, parameters.EngineVersion, parameters.InputSha256, ct);

            default:
                throw new NotSupportedException("unknown child kind");
        }
    }

    public static long PeakRssMiB()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            return self.PeakWorkingSet64 / (1024 * 1024);
        }
        catch (Exception)
        {
            return 0;
        }
    }
}

/// <summary>
/// Entry point of the isolated worker: <c>Fleet.Agent --child &lt;kind&gt; --in &lt;file&gt; --params &lt;file&gt; --out &lt;file&gt;</c>.
/// A poison PDF that exhausts memory or crashes the parser takes down only this process, never the agent (per-job isolation,
/// protocol 3.6 poison handling). Prints nothing: stdout and stderr are discarded by the parent (H3).
/// </summary>
internal static class ChildEntry
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length < 8 || args[0] != "--child") return ChildExit.BadArguments;
            var kind = args[1];
            string? input = null;
            string? parameters = null;
            string? output = null;
            for (var i = 2; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "--in": input = args[i + 1]; break;
                    case "--params": parameters = args[i + 1]; break;
                    case "--out": output = args[i + 1]; break;
                    default: return ChildExit.BadArguments;
                }
            }

            if (input is null || parameters is null || output is null) return ChildExit.BadArguments;
            var parsed = JsonSerializer.Deserialize<ChildParams>(File.ReadAllBytes(parameters), ProtocolJson.Options);
            if (parsed is null) return ChildExit.BadArguments;

            var pdf = File.ReadAllBytes(input);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(parsed.TimeoutSeconds, 1, 3600)));
            var result = ChildWork.Execute(kind, pdf, parsed, budget.Token);
            File.WriteAllBytes(output, result);
            return ChildExit.Ok;
        }
        catch (UnrepresentableResultException)
        {
            return ChildExit.Unrepresentable;
        }
        catch (OperationCanceledException)
        {
            return ChildExit.Timeout;
        }
        catch (OutOfMemoryException)
        {
            return ChildExit.OutOfMemory;
        }
        catch (Exception)
        {
            return ChildExit.Unexpected;
        }
    }
}

internal sealed record ChildRun(string Kind, string InputPath, string OutputPath, ChildParams Params, long MemLimitMiB, TimeSpan Timeout);

internal sealed record ChildOutcome(int ExitCode, bool TimedOut, bool Canceled, long PeakRssMiB);

internal interface IChildRunner
{
    Task<ChildOutcome> RunAsync(ChildRun run, JobLease? lease, CancellationToken ct);
}

/// <summary>Runs the child work inside this process. Used by unit tests; production uses <see cref="ProcessChildRunner"/>.</summary>
internal sealed class InProcessChildRunner : IChildRunner
{
    public Task<ChildOutcome> RunAsync(ChildRun run, JobLease? lease, CancellationToken ct)
    {
        try
        {
            var result = ChildWork.Execute(run.Kind, File.ReadAllBytes(run.InputPath), run.Params, ct);
            File.WriteAllBytes(run.OutputPath, result);
            return Task.FromResult(new ChildOutcome(ChildExit.Ok, false, false, ChildWork.PeakRssMiB()));
        }
        catch (UnrepresentableResultException)
        {
            return Task.FromResult(new ChildOutcome(ChildExit.Unrepresentable, false, false, 0));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new ChildOutcome(ChildExit.Timeout, false, true, 0));
        }
    }
}

/// <summary>
/// Spawns <c>dotnet Fleet.Agent.dll --child ...</c> with a GC heap hard limit and below-normal priority, samples its RSS and
/// CPU for the heartbeat metrics, and kills the whole tree on timeout or abort.
/// </summary>
internal sealed class ProcessChildRunner : IChildRunner
{
    private readonly string? _agentDll;

    public ProcessChildRunner(string? agentDllPath = null) => _agentDll = agentDllPath;

    public async Task<ChildOutcome> RunAsync(ChildRun run, JobLease? lease, CancellationToken ct)
    {
        var paramsPath = run.OutputPath + ".params.json";
        await File.WriteAllBytesAsync(paramsPath, JsonSerializer.SerializeToUtf8Bytes(run.Params, ProtocolJson.Options), ct).ConfigureAwait(false);

        var (file, prefix) = ResolveSelf(_agentDll);
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var part in prefix) start.ArgumentList.Add(part);
        foreach (var part in new[] { "--child", run.Kind, "--in", run.InputPath, "--params", paramsPath, "--out", run.OutputPath }) start.ArgumentList.Add(part);

        // The GC heap limit turns "memory exhaustion" into an OutOfMemoryException inside the child (exit 12) instead of an
        // OOM kill of the whole container. The variable is hexadecimal.
        var heapBytes = (long)(Math.Max(64, run.MemLimitMiB) * 0.85) * 1024 * 1024;
        start.Environment["DOTNET_GCHeapHardLimit"] = heapBytes.ToString("X", CultureInfo.InvariantCulture);
        start.Environment["DOTNET_gcServer"] = "0";
        start.Environment["DOTNET_TieredPGO"] = "0";
        start.Environment["DOTNET_EnableDiagnostics"] = "0";

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ChildOutcome(ChildExit.BadArguments, false, false, 0);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        TrySetBelowNormal(process);

        long peakBytes = 0;
        var timedOut = false;
        var canceled = false;
        var wall = Stopwatch.StartNew();
        var lastWall = TimeSpan.Zero;
        var lastCpu = TimeSpan.Zero;
        while (!HasExited(process))
        {
            if (ct.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            if (wall.Elapsed >= run.Timeout)
            {
                timedOut = true;
                break;
            }

            try
            {
                process.Refresh();
                peakBytes = Math.Max(peakBytes, process.WorkingSet64);
                var cpu = process.TotalProcessorTime;
                var elapsed = wall.Elapsed;
                if (lease is not null)
                {
                    lease.RssMiB = (int)Math.Min(int.MaxValue, peakBytes / (1024 * 1024));
                    if (elapsed > lastWall) lease.CpuPct = 100.0 * (cpu - lastCpu).TotalMilliseconds / (elapsed - lastWall).TotalMilliseconds;
                }

                lastCpu = cpu;
                lastWall = elapsed;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
            {
                // The child exited between the check and the sample.
            }

            try
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                break;
            }
        }

        if (!HasExited(process))
        {
            TryKill(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
            {
                // Best effort: a kill that does not land within 5 s is reported through the timeout/cancel flags anyway.
            }
        }

        var exit = HasExited(process) ? process.ExitCode : -1;
        return new ChildOutcome(exit, timedOut, canceled, peakBytes / (1024 * 1024));
    }

    /// <summary>
    /// "dotnet Fleet.Agent.dll" when hosted by the dotnet muxer (the container). An explicitly supplied agent assembly (a
    /// published agent driven from another host process, such as the parity test run) is always started through the muxer.
    /// Otherwise the process is its own apphost.
    /// </summary>
    internal static (string File, string[] Prefix) ResolveSelf(string? agentDll)
    {
        var processPath = Environment.ProcessPath ?? "dotnet";
        var name = Path.GetFileNameWithoutExtension(processPath);
        if (string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = agentDll ?? typeof(ChildEntry).Assembly.Location;
            return (processPath, new[] { dll });
        }

        if (agentDll is not null) return ("dotnet", new[] { agentDll });
        return (processPath, Array.Empty<string>());
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TrySetBelowNormal(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // Priority is a courtesy to the heartbeat thread; the cgroup CPU limit is the real control.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }
}
