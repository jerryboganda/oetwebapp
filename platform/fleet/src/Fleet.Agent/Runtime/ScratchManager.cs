namespace Fleet.Agent;

/// <summary>
/// Owns the helper's only writable areas (protocol 10.1 H1/H2): the per-job directory /scratch/{jobId}-{fence}/ (0700),
/// deleted on EVERY exit path, plus the start-up wipe of /scratch and /tmp.
/// </summary>
internal sealed class ScratchManager
{
    private readonly string _root;
    private readonly string _tmp;
    private readonly ILogger _log;

    public ScratchManager(string scratchRoot, string tmpRoot, ILogger log)
    {
        _root = scratchRoot;
        _tmp = tmpRoot;
        _log = log;
    }

    public string Root => _root;

    /// <summary>Where the input cache lives (inside the scratch tmpfs so it shares its size cap and its wipe).</summary>
    public string CacheDirectory => Path.Combine(_root, ".cache");

    /// <summary>Wipe /scratch/* and /tmp/* at process start (H2). Runtime-owned names under /tmp are left alone.</summary>
    public void WipeAtStart()
    {
        Directory.CreateDirectory(_root);
        WipeChildren(_root, skipRuntime: false);
        if (Directory.Exists(_tmp)) WipeChildren(_tmp, skipRuntime: true);
        Directory.CreateDirectory(CacheDirectory);
        Protect(CacheDirectory);
    }

    public string CreateJobDirectory(string jobId, long fence)
    {
        if (!Wire.JobIdPattern.IsMatch(jobId)) throw new ArgumentException("job id has an unexpected shape");
        var path = Path.Combine(_root, jobId + "-" + fence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(path);
        Protect(path);
        return path;
    }

    /// <summary>Delete the job directory; retried because a just-killed child may still hold a handle for a moment.</summary>
    public void DeleteJobDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }

        _log.LogWarning("scratch directory could not be removed after retries");
    }

    private static void Protect(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // Best effort: the tmpfs mount is already private to the container user.
        }
    }

    private void WipeChildren(string directory, bool skipRuntime)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(entry);
            if (skipRuntime && (name.StartsWith(".dotnet", StringComparison.Ordinal) || name.StartsWith("clr-debug-pipe", StringComparison.Ordinal)
                || name.StartsWith("dotnet-diagnostic", StringComparison.Ordinal)))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning("start-up wipe skipped an entry: {Error}", Redact.Exception(ex));
            }
        }
    }
}
