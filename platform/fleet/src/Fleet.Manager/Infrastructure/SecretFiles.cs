using System.Security.Cryptography;
using System.Text;
using Fleet.Core.Crypto;
using Fleet.Manager.Configuration;
using Microsoft.Extensions.Options;

namespace Fleet.Manager.Infrastructure;

/// <summary>Reads small text secrets from files (compose <c>secrets:</c> under <c>/run/secrets</c>). Values are never logged.</summary>
public static class SecretFile
{
    /// <summary>The trimmed content, or null when the file is missing, unreadable or empty.</summary>
    public static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when the file exists and is not empty. It reads no content: a page that only says "configured or not" (and is polled) must not load the
    /// secret into managed memory to find out. A file holding only whitespace counts as present here; <see cref="TryRead"/> would call it empty.
    /// </summary>
    public static bool IsPresent(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Constant-time comparison of a presented bearer against an expected secret.</summary>
    public static bool FixedTimeEquals(string? presented, string? expected)
    {
        if (string.IsNullOrEmpty(presented) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        // Hash both sides first so the comparison length does not depend on the secret length.
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}

/// <summary>Source of the fleet-service credential (<c>ofs1_...</c>) the manager presents to the OET API.</summary>
public interface IApiCredentialProvider
{
    /// <summary>The bearer token, or null when it is not provisioned (the manager then reports <c>api_unreachable</c> style failures).</summary>
    string? GetBearer();
}

/// <summary>
/// Reads <c>/run/secrets/fleet_api_credential</c> with a 5-second cache, so rotating the secret
/// file takes effect without a restart. The value never leaves this class except as the bearer header.
/// </summary>
public sealed class FileApiCredentialProvider : IApiCredentialProvider
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    private readonly IOptions<FleetOptions> _options;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private string? _cached;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public FileApiCredentialProvider(IOptions<FleetOptions> options, TimeProvider time)
    {
        _options = options;
        _time = time;
    }

    public string? GetBearer()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (now - _loadedAt >= CacheFor)
            {
                var secrets = _options.Value.Secrets;
                _cached = SecretFile.TryRead(Path.Combine(secrets.Directory, secrets.ApiCredentialFile));
                _loadedAt = now;
            }

            return _cached;
        }
    }
}

/// <summary>A file that holds a secret for the length of one child process and is zeroed and deleted afterwards (tmpfs).</summary>
public sealed class TempSecretFile : IDisposable
{
    private TempSecretFile(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempSecretFile Create(string directory, string fileName, SecretBuffer secret)
    {
        var path = System.IO.Path.Combine(directory, fileName);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        using (var stream = new FileStream(path, options))
        {
            stream.Write(secret.AsSpan());
            stream.Flush(true);
        }

        return new TempSecretFile(path);
    }

    public void Dispose()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return;
            }

            var length = new FileInfo(Path).Length;
            using (var stream = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var zeros = new byte[(int)Math.Min(length, 65536)];
                var written = 0L;
                while (written < length)
                {
                    var chunk = (int)Math.Min(zeros.Length, length - written);
                    stream.Write(zeros, 0, chunk);
                    written += chunk;
                }

                stream.Flush(true);
            }

            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the file lives on tmpfs inside a per-run directory that is deleted next.
        }
    }
}

/// <summary>A per-run directory under the tmpfs scratch root (mode 0700), removed with everything in it.</summary>
public sealed class RunScratch : IDisposable
{
    private RunScratch(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static RunScratch Create(string root)
    {
        if (root.Contains(' '))
        {
            throw new InvalidOperationException("The scratch directory must not contain spaces.");
        }

        var path = System.IO.Path.Combine(root, "run-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new RunScratch(path);
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; secret files inside were already zeroed by TempSecretFile.Dispose.
        }
    }
}
