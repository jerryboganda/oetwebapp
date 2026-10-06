using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Fleet.Manager.Provisioning;

/// <summary>
/// One child process. <see cref="Arguments"/> is an argument LIST (never a shell string), the
/// environment is built from scratch (<see cref="Environment"/> plus PATH), and the process is killed
/// when <see cref="Timeout"/> passes. <see cref="StdinText"/> carries secrets (a registry token, a node
/// token) so they never appear in argv or the environment.
/// </summary>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment,
    string? StdinText,
    TimeSpan Timeout,
    int MaxOutputBytes = 262_144,
    string? WorkingDirectory = null);

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (spec.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = spec.WorkingDirectory;
        }

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Never inherit the manager's environment: it may hold paths to secrets. PATH only, plus what the caller says.
        startInfo.Environment.Clear();
        var inheritedPath = System.Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(inheritedPath))
        {
            startInfo.Environment["PATH"] = inheritedPath;
        }

        if (spec.Environment is not null)
        {
            foreach (var (key, value) in spec.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogWarning("Could not start '{FileName}': {Message}", spec.FileName, ex.Message);
            return new ProcessResult(127, string.Empty, "executable could not be started", false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(spec.Timeout);

        var stdout = ReadCappedAsync(process.StandardOutput, spec.MaxOutputBytes);
        var stderr = ReadCappedAsync(process.StandardError, spec.MaxOutputBytes);

        try
        {
            if (spec.StdinText is not null)
            {
                await process.StandardInput.WriteAsync(spec.StdinText.AsMemory(), timeout.Token);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, await stdout, await stderr, true);
        }
        catch (IOException)
        {
            // The child closed stdin early (it exited); its exit code below tells the story.
            await process.WaitForExitAsync(timeout.Token);
        }

        return new ProcessResult(process.ExitCode, await stdout, await stderr, false);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }

    private static async Task<string> ReadCappedAsync(StreamReader reader, int maxBytes)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            if (builder.Length < maxBytes)
            {
                builder.Append(buffer, 0, Math.Min(read, maxBytes - builder.Length));
            }
        }

        return builder.ToString();
    }
}
