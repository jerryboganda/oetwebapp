using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fleet.Agent;

/// <summary>
/// Argument lists of the normative ffmpeg procedures (protocol 6.3 and 6.4). Pure functions so the exact vectors are testable;
/// the speaking decode/encode vectors are identical to FfmpegSpeakingAudioTranscoder in the API.
/// </summary>
internal static class FfmpegArgs
{
    public const int SampleRate = 16_000;
    public const int BytesPerSecond = SampleRate * 2;

    private static readonly string[] Quiet = ["-hide_banner", "-loglevel", "error", "-nostdin"];

    /// <summary>media.audio-extract step 2: decode ONCE to raw PCM on a pipe from a seekable tmpfs file.</summary>
    public static string[] DecodeFileToPcm(string inputFile) =>
        [.. Quiet, "-i", inputFile, "-map", "0:a:0", "-vn", "-sn", "-dn", "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "pipe:1"];

    /// <summary>media.speaking-join: decode one clip from stdin to raw PCM on stdout.</summary>
    public static string[] DecodeClipFromStdin() =>
        [.. Quiet, "-i", "pipe:0", "-vn", "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "pipe:1"];

    /// <summary>Encode raw 16 kHz mono PCM on stdin to one mp3 on stdout.</summary>
    public static string[] EncodePcmToMp3(int bitrateKbps) =>
        [.. Quiet, "-f", "s16le", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture), "-ac", "1", "-i", "pipe:0",
            "-c:a", "libmp3lame", "-b:a", bitrateKbps.ToString(CultureInfo.InvariantCulture) + "k", "-f", "mp3", "pipe:1"];

    /// <summary>Self-check source: exactly one second of 16 kHz mono silence as raw PCM.</summary>
    public static string[] SilenceOneSecond() =>
        [.. Quiet, "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono", "-t", "1", "-f", "s16le", "pipe:1"];
}

internal sealed class ProcessSpec
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>Copied into the process stdin, which is then closed.</summary>
    public Stream? Stdin { get; init; }

    /// <summary>Reads the process stdout to its end. Null discards stdout.</summary>
    public Func<Stream, CancellationToken, Task>? ConsumeStdout { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <param name="ExitCode">Process exit code; <see cref="ProcessRunner.NotStarted"/> when the binary could not be started.</param>
/// <param name="StderrTail">Last bytes of stderr. Inspected for known markers, NEVER logged (it can echo file content).</param>
internal sealed record ProcessRunResult(int ExitCode, bool TimedOut, string StderrTail);

internal interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessSpec spec, CancellationToken ct);
}

internal static class ProcessRunner
{
    public const int NotStarted = -127;
}

/// <summary>Runs one short child process with stdin/stdout streaming and a hard timeout (tree kill).</summary>
internal sealed class SystemProcessRunner : IProcessRunner
{
    private const int StderrTailBytes = 2048;

    public async Task<ProcessRunResult> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        var start = new ProcessStartInfo(spec.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in spec.Arguments) start.ArgumentList.Add(argument);
        ChildEnvironment.Scrub(start.Environment); // ffmpeg parses hostile media: it never sees OET_NODE_TOKEN

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new Win32Exception("process did not start");
        }
        catch (Win32Exception)
        {
            return new ProcessRunResult(ProcessRunner.NotStarted, false, "");
        }

        using (process)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(spec.Timeout);
            var tail = new MemoryStream();
            try
            {
                var feed = Task.Run(async () =>
                {
                    try
                    {
                        if (spec.Stdin is not null) await spec.Stdin.CopyToAsync(process.StandardInput.BaseStream, budget.Token).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // The tool closed its input early (it had all it needed, or it failed): the exit code decides.
                    }
                    finally
                    {
                        try
                        {
                            process.StandardInput.Close();
                        }
                        catch (Exception ex) when (ex is IOException or InvalidOperationException)
                        {
                            // Already closed.
                        }
                    }
                }, CancellationToken.None);
                var read = spec.ConsumeStdout is null
                    ? DrainAsync(process.StandardOutput.BaseStream, budget.Token)
                    : spec.ConsumeStdout(process.StandardOutput.BaseStream, budget.Token);
                // A consumer fault must stop the tool at once: it would otherwise block on a full pipe until the timeout.
                _ = read.ContinueWith(_ => TryKill(process), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                var errors = CaptureTailAsync(process.StandardError.BaseStream, tail, budget.Token);
                await Task.WhenAll(feed, read, errors).ConfigureAwait(false);
                await process.WaitForExitAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                return new ProcessRunResult(-1, true, "");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }
            catch (Exception)
            {
                // A consumer fault (for example a duration limit) must not leave the tool running.
                TryKill(process);
                throw;
            }

            return new ProcessRunResult(process.ExitCode, false, Encoding.UTF8.GetString(tail.ToArray()));
        }
    }

    private static async Task DrainAsync(Stream stream, CancellationToken ct)
    {
        var scratch = new byte[16384];
        while (await stream.ReadAsync(scratch, ct).ConfigureAwait(false) > 0)
        {
            // Output is not wanted.
        }
    }

    private static async Task CaptureTailAsync(Stream stream, MemoryStream tail, CancellationToken ct)
    {
        var chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            tail.Write(chunk, 0, read);
            if (tail.Length > StderrTailBytes * 2)
            {
                var keep = tail.ToArray().AsSpan((int)tail.Length - StderrTailBytes).ToArray();
                tail.SetLength(0);
                tail.Write(keep, 0, keep.Length);
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone.
        }
    }
}

/// <summary>Detects the ffmpeg build the agent would run, for the engineVersion string of the media kinds.</summary>
internal static class FfmpegProbe
{
    private static readonly Regex VersionPattern = new(@"^ffmpeg version (\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses "ffmpeg version 7.1.1-1~deb13u1 ..." into "7.1.1". Git builds (N-12345-g...) return null.</summary>
    public static string? ParseVersion(string? firstLine)
    {
        if (string.IsNullOrWhiteSpace(firstLine)) return null;
        var match = VersionPattern.Match(firstLine.Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    public static async Task<string?> DetectVersionAsync(IProcessRunner runner, string ffmpegPath, CancellationToken ct)
    {
        string? first = null;
        var result = await runner.RunAsync(new ProcessSpec
        {
            FileName = ffmpegPath,
            Arguments = ["-version"],
            Timeout = TimeSpan.FromSeconds(10),
            ConsumeStdout = async (stdout, token) =>
            {
                using var reader = new StreamReader(stdout, Encoding.UTF8, false, 1024, leaveOpen: true);
                first = await reader.ReadLineAsync(token).ConfigureAwait(false);
                // Drain the rest so the tool can exit.
                var scratch = new char[1024];
                while (await reader.ReadAsync(scratch.AsMemory(), token).ConfigureAwait(false) > 0)
                {
                }
            },
        }, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? ParseVersion(first) : null;
    }
}
