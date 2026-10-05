using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Fleet.Core.Validation;

public sealed record ValidationIssue(string Field, string Code, string Message);

public sealed class FleetValidationException : Exception
{
    public FleetValidationException(IReadOnlyList<ValidationIssue> issues)
        : base(string.Join("; ", issues.Select(i => i.Field + ": " + i.Code)))
    {
        Issues = issues;
    }

    public FleetValidationException(ValidationIssue issue)
        : this(new[] { issue })
    {
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }
}

public enum AddressKind
{
    Ipv4,
    Ipv6,
    Hostname,
}

/// <summary>
/// Strict, allow-list input validation for everything an owner types into the manager
/// (OET-RWP/1 section 8.9). Every value that survives is later passed to child processes as
/// an argument or as JSON data (<c>-e @file</c>), never interpolated into a shell string.
/// All patterns use <c>\A..\z</c> (not <c>$</c>) so a trailing newline can never slip through.
/// </summary>
public static class InputValidator
{
    private static readonly Regex NodeRefPattern = new(@"\A[a-z0-9][a-z0-9-]{2,62}\z", RegexOptions.CultureInvariant);
    private static readonly Regex SshUserPattern = new(@"\A[a-z_][a-z0-9_-]{0,31}\z", RegexOptions.CultureInvariant);
    private static readonly Regex DisplayNamePattern = new(@"\A[A-Za-z0-9][A-Za-z0-9 ._()/#-]{0,79}\z", RegexOptions.CultureInvariant);
    private static readonly Regex OptionalLabelPattern = new(@"\A[A-Za-z0-9 ._()/-]{0,64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex HostLabelPattern = new(@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant);
    private static readonly Regex NumericLastLabelPattern = new(@"\A(?:[0-9]+|0[xX][0-9a-fA-F]*)\z", RegexOptions.CultureInvariant);
    private static readonly Regex Ipv6CharsPattern = new(@"\A[0-9A-Fa-f:.]{2,45}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256HexPattern = new(@"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ImageDigestPattern = new(@"\Asha256:[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

    public static bool IsValidNodeRef(string? value) => value is not null && NodeRefPattern.IsMatch(value);

    public static bool IsValidSshUser(string? value) => value is not null && SshUserPattern.IsMatch(value);

    public static bool IsValidImageDigest(string? value) => value is not null && ImageDigestPattern.IsMatch(value);

    public static bool IsValidSha256Hex(string? value) => value is not null && Sha256HexPattern.IsMatch(value);

    public static ValidationIssue? ValidateNodeRef(string? value) =>
        IsValidNodeRef(value)
            ? null
            : new ValidationIssue("nodeRef", "node_ref_invalid", "nodeRef must match ^[a-z0-9][a-z0-9-]{2,62}$.");

    public static ValidationIssue? ValidateSshUser(string? value) =>
        IsValidSshUser(value)
            ? null
            : new ValidationIssue("user", "ssh_user_invalid", "SSH user must match ^[a-z_][a-z0-9_-]{0,31}$.");

    public static ValidationIssue? ValidateDisplayName(string? value) =>
        value is not null && DisplayNamePattern.IsMatch(value)
            ? null
            : new ValidationIssue("displayName", "display_name_invalid", "displayName must be 1-80 characters of letters, digits, space and . _ ( ) / # -.");

    public static ValidationIssue? ValidateOptionalLabel(string field, string? value) =>
        value is null || OptionalLabelPattern.IsMatch(value)
            ? null
            : new ValidationIssue(field, field + "_invalid", field + " must be at most 64 characters of letters, digits, space and . _ ( ) / -.");

    public static ValidationIssue? ValidatePort(int port) =>
        port is >= 1 and <= 65535
            ? null
            : new ValidationIssue("sshPort", "port_invalid", "sshPort must be between 1 and 65535.");

    /// <summary>
    /// Syntactic parse only (no DNS, no policy): a canonical dotted-quad IPv4 (no leading zeros, no
    /// decimal/hex/octal shorthand), an IPv6 literal, or an FQDN whose last label is not numeric.
    /// </summary>
    public static bool TryParseAddress(
        string? input,
        out AddressKind kind,
        out string normalized,
        out IPAddress? ip,
        out ValidationIssue? issue)
    {
        kind = AddressKind.Hostname;
        normalized = string.Empty;
        ip = null;
        issue = null;

        if (string.IsNullOrEmpty(input))
        {
            issue = new ValidationIssue("address", "address_invalid", "address is required.");
            return false;
        }

        if (input.Length > 253 || !string.Equals(input, input.Trim(), StringComparison.Ordinal))
        {
            issue = new ValidationIssue("address", "address_invalid", "address must be at most 253 characters without surrounding whitespace.");
            return false;
        }

        if (input.Contains(':'))
        {
            if (Ipv6CharsPattern.IsMatch(input)
                && IPAddress.TryParse(input, out var v6)
                && v6.AddressFamily == AddressFamily.InterNetworkV6)
            {
                kind = AddressKind.Ipv6;
                ip = v6;
                normalized = v6.ToString();
                return true;
            }

            issue = new ValidationIssue("address", "address_invalid", "address is not a valid IPv6 literal.");
            return false;
        }

        if (TryParseStrictIpv4(input, out var v4))
        {
            kind = AddressKind.Ipv4;
            ip = v4;
            normalized = v4!.ToString();
            return true;
        }

        var labels = input.Split('.');
        if (labels.Length < 2)
        {
            issue = new ValidationIssue("address", "address_invalid", "address must be a fully qualified host name or an IP literal.");
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63 || !HostLabelPattern.IsMatch(label))
            {
                issue = new ValidationIssue("address", "address_invalid", "address contains an invalid host name label.");
                return false;
            }
        }

        // WHATWG "ends in a number": 3113781690 or 0xb9.0xfc.0xe9.0xba are IPv4 shorthand to
        // inet_aton and would sidestep the primary-address check, so they are not host names.
        if (NumericLastLabelPattern.IsMatch(labels[^1]))
        {
            issue = new ValidationIssue("address", "address_invalid", "address looks like an IPv4 shorthand; use a canonical dotted quad or a host name.");
            return false;
        }

        kind = AddressKind.Hostname;
        normalized = input.ToLowerInvariant();
        return true;
    }

    private static bool TryParseStrictIpv4(string value, out IPAddress? ip)
    {
        ip = null;
        var parts = value.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var part = parts[i];
            if (part.Length is < 1 or > 3 || (part.Length > 1 && part[0] == '0'))
            {
                return false;
            }

            foreach (var ch in part)
            {
                if (ch is < '0' or > '9')
                {
                    return false;
                }
            }

            var number = int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture);
            if (number > 255)
            {
                return false;
            }

            bytes[i] = (byte)number;
        }

        ip = new IPAddress(bytes);
        return true;
    }
}
