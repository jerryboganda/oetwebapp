using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Fleet.Core.Ssh;

public sealed record ScannedHostKey(string Algorithm, string PublicKeyBase64, string Fingerprint);

/// <summary>
/// SSH host-key trust (OET-RWP/1 section 8.6). <c>ssh-keyscan</c> output is only ever DISPLAYED:
/// the owner compares the SHA256 fingerprint with the VPS provider console out of band and types
/// the first 8 characters before the key is pinned. Everything written into a known_hosts file is
/// rebuilt here from validated parts, never copied from scanner output.
/// </summary>
public static class HostKeys
{
    /// <summary>Ordered by preference.</summary>
    public static readonly string[] AllowedAlgorithms =
    {
        "ssh-ed25519", "ecdsa-sha2-nistp256", "ecdsa-sha2-nistp384", "ecdsa-sha2-nistp521", "ssh-rsa",
    };

    private static readonly Regex Base64Pattern = new(@"\A[A-Za-z0-9+/]{16,2048}={0,2}\z", RegexOptions.CultureInvariant);

    /// <summary><c>SHA256:</c> + unpadded base64 of SHA-256 over the decoded key blob (the format OpenSSH prints).</summary>
    public static string FingerprintSha256(string publicKeyBase64)
    {
        var blob = Convert.FromBase64String(publicKeyBase64);
        return "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');
    }

    /// <summary>Parses <c>ssh-keyscan</c> stdout. Malformed lines, unknown algorithms and blobs whose embedded type disagrees are dropped.</summary>
    public static IReadOnlyList<ScannedHostKey> ParseKeyScan(string stdout)
    {
        var keys = new List<ScannedHostKey>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var algorithm = parts[1];
            var base64 = parts[2];
            if (!AllowedAlgorithms.Contains(algorithm, StringComparer.Ordinal) || !Base64Pattern.IsMatch(base64))
            {
                continue;
            }

            if (!BlobMatchesAlgorithm(base64, algorithm))
            {
                continue;
            }

            if (seen.Add(algorithm + " " + base64))
            {
                keys.Add(new ScannedHostKey(algorithm, base64, FingerprintSha256(base64)));
            }
        }

        return keys;
    }

    /// <summary>ed25519 first, then ecdsa (nistp256, 384, 521), then rsa.</summary>
    public static ScannedHostKey? Preferred(IEnumerable<ScannedHostKey> keys)
    {
        ScannedHostKey? best = null;
        var bestRank = int.MaxValue;
        foreach (var key in keys)
        {
            var rank = Array.IndexOf(AllowedAlgorithms, key.Algorithm);
            if (rank >= 0 && rank < bestRank)
            {
                best = key;
                bestRank = rank;
            }
        }

        return best;
    }

    /// <summary>One known_hosts line, built only from validated parts (host is a validated address, algorithm is allow-listed, key is base64).</summary>
    public static string KnownHostsLine(string host, int port, string algorithm, string publicKeyBase64)
    {
        if (!AllowedAlgorithms.Contains(algorithm, StringComparer.Ordinal) || !Base64Pattern.IsMatch(publicKeyBase64))
        {
            throw new ArgumentException("Host key is not valid.");
        }

        var hostField = port == 22 ? host : "[" + host + "]:" + port;
        return hostField + " " + algorithm + " " + publicKeyBase64;
    }

    /// <summary>
    /// The owner types the first 8 characters of the fingerprint. Both <c>SHA256:xxxx</c> and
    /// <c>xxxx</c> are accepted; the comparison is case-sensitive (base64) and constant-time.
    /// </summary>
    public static bool PrefixMatches(string fingerprint, string? typed)
    {
        const string marker = "SHA256:";
        if (string.IsNullOrEmpty(typed) || !fingerprint.StartsWith(marker, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = fingerprint[marker.Length..];
        var candidate = typed.Trim();
        if (candidate.StartsWith(marker, StringComparison.Ordinal))
        {
            candidate = candidate[marker.Length..];
        }

        if (candidate.Length < 8 || candidate.Length > expected.Length)
        {
            return false;
        }

        var left = Encoding.ASCII.GetBytes(expected[..candidate.Length]);
        var right = Encoding.ASCII.GetBytes(candidate);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static bool BlobMatchesAlgorithm(string base64, string algorithm)
    {
        byte[] blob;
        try
        {
            blob = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (blob.Length < 4)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(blob.AsSpan(0, 4));
        if (length == 0 || length > blob.Length - 4)
        {
            return false;
        }

        var embedded = Encoding.ASCII.GetString(blob, 4, (int)length);
        return string.Equals(embedded, algorithm, StringComparison.Ordinal);
    }
}
