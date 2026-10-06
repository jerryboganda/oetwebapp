using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fleet.Core.Audit;

/// <summary>
/// Redaction for everything the manager logs, audits or shows (OET-RWP/1 section 8.5, 8.9):
/// the credential shapes of the platform's <c>OwnerAgentAuditSanitizer</c> (JWTs, GitHub/OpenAI/Slack/AWS
/// tokens, connection strings, PEM private keys) plus the fleet's own <c>orw1_</c>/<c>ofs1_</c> tokens
/// and OpenSSH key bodies. Helper-originated text is also stripped of ANSI/control characters,
/// capped and HTML-encoded because a helper is untrusted.
/// </summary>
public static class LogScrubber
{
    public const string Redacted = "[redacted]";
    public const int DefaultMaxLength = 500;

    private static readonly Regex SecretPattern = new(
        @"eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}"
        + @"|github_pat_[A-Za-z0-9_]{10,}"
        + @"|gh[pousr]_[A-Za-z0-9]{16,}"
        + @"|sk-[A-Za-z0-9_-]{12,}"
        + @"|xox[abprs]-[A-Za-z0-9-]{8,}"
        + @"|AKIA[0-9A-Z]{16}"
        + @"|(?:postgres(?:ql)?|mysql|mongodb(?:\+srv)?|redis|amqps?)://\S+"
        + @"|o(?:rw|fs)1_[0-9a-f]{16}_[A-Za-z0-9_-]{43}"
        + @"|-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|\z)"
        + @"|b3BlbnNzaC1rZXktdjE[A-Za-z0-9+/=]{20,}",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnsiPattern = new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B[@-Z\\-_]", RegexOptions.CultureInvariant);

    private static readonly Regex ControlPattern = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.CultureInvariant);

    /// <summary>Redacts credential shapes, then caps the length.</summary>
    public static string Scrub(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = SecretPattern.Replace(value, Redacted);
        return redacted.Length > maxLength ? redacted[..maxLength] : redacted;
    }

    /// <summary>Scrub for text that came from a helper or a child process: no ANSI, no control characters, capped, HTML-encoded.</summary>
    public static string SanitizeUntrusted(string? value, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = AnsiPattern.Replace(value, string.Empty);
        text = ControlPattern.Replace(text, " ");
        text = SecretPattern.Replace(text, Redacted);
        if (text.Length > maxLength)
        {
            text = text[..maxLength];
        }

        // Cap first, encode second: the encoded form may exceed maxLength by the entity overhead only.
        return System.Net.WebUtility.HtmlEncode(text);
    }
}

public static class AuditDetails
{
    public const int MaxStringLength = 200;

    private static readonly JsonSerializerOptions CanonicalOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>Sorted-key, primitive-only JSON used both for storage and hashing; strings are scrubbed and capped.</summary>
    public static string ToCanonicalJson(IReadOnlyDictionary<string, object?>? details)
    {
        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        if (details is not null)
        {
            foreach (var (key, value) in details)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                sorted[key.Length > 64 ? key[..64] : key] = Normalize(value);
            }
        }

        return JsonSerializer.Serialize(sorted, CanonicalOptions);
    }

    private static object? Normalize(object? value) => value switch
    {
        null => null,
        string text => LogScrubber.Scrub(text, MaxStringLength),
        bool or int or long or short or byte or double or float or decimal => value,
        DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        Enum enumValue => enumValue.ToString(),
        _ => LogScrubber.Scrub(Convert.ToString(value, CultureInfo.InvariantCulture), MaxStringLength),
    };
}

public sealed record AuditRecord(
    long Id,
    DateTimeOffset At,
    string Actor,
    string Action,
    string? Target,
    string DetailsJson,
    string PrevHash,
    string Hash);

public sealed record ChainVerification(bool Intact, int Checked, long? FirstBadId, string? Problem)
{
    public static ChainVerification Ok(int checkedCount) => new(true, checkedCount, null, null);
}

/// <summary>
/// The tamper-evident audit log (OET-RWP/1 section 8.3, RW-145): each row's hash is the SHA-256
/// of the previous hash and the canonical row text (the <c>OwnerAgentAuditService.ComputeHash</c>
/// pattern). Editing, deleting or re-ordering any earlier row breaks every later link.
/// </summary>
public static class AuditChain
{
    public static readonly string Genesis = new('0', 64);

    public static string ComputeHash(
        string prevHash,
        long id,
        DateTimeOffset at,
        string actor,
        string action,
        string? target,
        string detailsJson)
    {
        var material = string.Join(
            '\n',
            prevHash,
            id.ToString(CultureInfo.InvariantCulture),
            at.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            actor,
            action,
            target ?? string.Empty,
            detailsJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>Verifies rows given oldest first. The first row must chain from <see cref="Genesis"/> unless <paramref name="anchor"/> is given.</summary>
    public static ChainVerification Verify(IEnumerable<AuditRecord> oldestFirst, string? anchor = null)
    {
        var expectedPrev = anchor ?? Genesis;
        var count = 0;
        foreach (var record in oldestFirst)
        {
            count++;
            if (!string.Equals(record.PrevHash, expectedPrev, StringComparison.Ordinal))
            {
                return new ChainVerification(false, count, record.Id, "previous hash does not match the preceding row");
            }

            var recomputed = ComputeHash(record.PrevHash, record.Id, record.At, record.Actor, record.Action, record.Target, record.DetailsJson);
            if (!string.Equals(recomputed, record.Hash, StringComparison.Ordinal))
            {
                return new ChainVerification(false, count, record.Id, "row hash does not match its content");
            }

            expectedPrev = record.Hash;
        }

        return ChainVerification.Ok(count);
    }
}

/// <summary>The same chain for the operations table: it seals the immutable creation fields of each operation.</summary>
public static class OperationSeal
{
    public static string ComputeHash(
        string prevHash,
        long seq,
        string id,
        string kind,
        string? hostId,
        string paramsJson,
        string actor,
        string? requestId,
        DateTimeOffset startedAt)
    {
        var material = string.Join(
            '\n',
            prevHash,
            seq.ToString(CultureInfo.InvariantCulture),
            id,
            kind,
            hostId ?? string.Empty,
            paramsJson,
            actor,
            requestId ?? string.Empty,
            startedAt.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}
