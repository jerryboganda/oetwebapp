using System.Diagnostics;
using Fleet.Manager.Provisioning;

namespace Fleet.Manager.Tests.Infrastructure;

/// <summary>Finds platform/fleet from wherever the test binaries run (bin/Debug/net10.0 inside a checkout).</summary>
public static class RepoPaths
{
    public static string FleetRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Fleet.sln")) && Directory.Exists(Path.Combine(directory.FullName, "ansible")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("platform/fleet was not found above " + AppContext.BaseDirectory);
    }

    public static string AnsibleFile(params string[] parts) => Path.Combine(new[] { FleetRoot(), "ansible" }.Concat(parts).ToArray());

    /// <summary>Every file of the fleet that ships (source, ansible, compose, Dockerfile), excluding tests, build output and docs.</summary>
    public static IEnumerable<string> ShippedFiles()
    {
        var root = FleetRoot();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.StartsWith("tests/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith("obj/", StringComparison.Ordinal)
                || relative.EndsWith(".md", StringComparison.Ordinal)
                || relative.EndsWith(".css", StringComparison.Ordinal))
            {
                continue;
            }

            yield return path;
        }
    }
}

public static class ByteSearch
{
    public static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}

/// <summary>A fact that is SKIPPED (reported as skipped, never as a silent pass) when python3 is not installed.</summary>
public sealed class PythonFactAttribute : FactAttribute
{
    public PythonFactAttribute()
    {
        if (Python.Find() is null)
        {
            Skip = "python3 is not installed on this machine";
        }
    }
}

public static class Python
{
    public static string? Find()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            try
            {
                var info = new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(info);
                if (process is null)
                {
                    continue;
                }

                process.WaitForExit(5000);
                if (process.HasExited && process.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Not installed under this name.
            }
        }

        return null;
    }

    /// <summary>Runs a python script with an argument list and a controlled environment. Returns (exit code, stdout, stderr).</summary>
    public static (int ExitCode, string Stdout, string Stderr) Run(string script, IEnumerable<string> arguments, IDictionary<string, string>? environment = null, string? stdin = null)
    {
        var info = new ProcessStartInfo(Find() ?? "python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                info.Environment[key] = value;
            }
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("python could not be started");
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);
        return (process.ExitCode, stdout, stderr);
    }
}

/// <summary>Records every child process the manager would start and answers with whatever the test scripted.</summary>
public sealed class RecordingProcessRunner : IProcessRunner
{
    private int _current;
    private int _max;

    public List<ProcessSpec> Specs { get; } = new();

    public Func<ProcessSpec, ProcessResult>? Handler { get; set; }

    public Func<ProcessSpec, Task<ProcessResult>>? AsyncHandler { get; set; }

    public int MaxConcurrent => _max;

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        lock (Specs)
        {
            Specs.Add(spec);
        }

        var running = Interlocked.Increment(ref _current);
        InterlockedMax(ref _max, running);
        try
        {
            if (AsyncHandler is not null)
            {
                return await AsyncHandler(spec);
            }

            return Handler?.Invoke(spec) ?? new ProcessResult(0, string.Empty, string.Empty, false);
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int snapshot;
        do
        {
            snapshot = Volatile.Read(ref target);
            if (value <= snapshot)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, snapshot) != snapshot);
    }
}

/// <summary>An HttpMessageHandler scripted per request; it records what the manager sent (method, URL, headers, body).</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public sealed record Recorded(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body);

    public List<Recorded> Requests { get; } = new();

    public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses { get; } = new();

    public Func<HttpRequestMessage, HttpResponseMessage>? Default { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add(new Recorded(request.Method, request.RequestUri!, headers, body));
        }

        var factory = Responses.Count > 0 ? Responses.Dequeue() : Default ?? throw new InvalidOperationException("no scripted response");
        return factory(request);
    }
}
