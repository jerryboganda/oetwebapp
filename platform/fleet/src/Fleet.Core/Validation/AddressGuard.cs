using System.Net;
using System.Net.Sockets;

namespace Fleet.Core.Validation;

public interface IHostResolver
{
    /// <summary>Resolves A and AAAA records. An unresolvable name returns an empty list or throws <see cref="SocketException"/>.</summary>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string hostname, CancellationToken cancellationToken);
}

public sealed class DnsHostResolver : IHostResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string hostname, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(hostname, cancellationToken);
        return addresses;
    }
}

/// <summary>What a helper address may never be (OET-RWP/1 section 8.9).</summary>
public static class AddressPolicy
{
    /// <summary>The production VPS. It is never a helper.</summary>
    public const string PrimaryAddress = "185.252.233.186";

    /// <summary>A host name containing this fragment names the production compose project.</summary>
    public const string ProductionProjectFragment = "oetwebsite";

    /// <summary>The production domain; its apex and every sub-domain point at the primary.</summary>
    public const string ProductionDomain = "oetwithdrhesham.co.uk";

    public static IPAddress Unwrap(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                return ip.MapToIPv4();
            }

            // NAT64 well-known prefix 64:ff9b::/96 embeds an IPv4 address in the last 32 bits.
            var b = ip.GetAddressBytes();
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b.Skip(4).Take(8).All(x => x == 0))
            {
                return new IPAddress(new[] { b[12], b[13], b[14], b[15] });
            }
        }

        return ip;
    }

    public static bool IsPrimary(IPAddress ip) =>
        string.Equals(Unwrap(ip).ToString(), PrimaryAddress, StringComparison.Ordinal);

    public static bool IsProductionHostName(string lowerCaseHostName) =>
        lowerCaseHostName.Contains(ProductionProjectFragment, StringComparison.Ordinal)
        || string.Equals(lowerCaseHostName, ProductionDomain, StringComparison.Ordinal)
        || lowerCaseHostName.EndsWith("." + ProductionDomain, StringComparison.Ordinal);

    /// <summary>
    /// Returns a stable reason when the address class is never a legitimate public helper
    /// (loopback, link-local, RFC 1918, CGNAT, unique-local, unspecified, multicast, reserved,
    /// the primary), otherwise null.
    /// </summary>
    public static string? ForbiddenReason(IPAddress address)
    {
        var ip = Unwrap(address);
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b6 = ip.GetAddressBytes();
            if (b6.All(x => x == 0))
            {
                return "unspecified";
            }

            if (IPAddress.IsLoopback(ip))
            {
                return "loopback";
            }

            if (ip.IsIPv6LinkLocal)
            {
                return "link_local";
            }

            if (ip.IsIPv6SiteLocal || (b6[0] & 0xFE) == 0xFC)
            {
                return "private";
            }

            if (ip.IsIPv6Multicast)
            {
                return "multicast";
            }

            return null;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return "unsupported_family";
        }

        if (IsPrimary(ip))
        {
            return "primary";
        }

        var b = ip.GetAddressBytes();
        if (b[0] == 0)
        {
            return "unspecified";
        }

        if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168))
        {
            return "private";
        }

        if (b[0] == 127)
        {
            return "loopback";
        }

        if (b[0] == 169 && b[1] == 254)
        {
            return "link_local";
        }

        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
        {
            return "cgnat";
        }

        if (b[0] >= 224)
        {
            return b[0] <= 239 ? "multicast" : "reserved";
        }

        return null;
    }
}

public sealed record AddressCheck(
    bool Allowed,
    string Normalized,
    AddressKind Kind,
    string? Code,
    string? Message,
    IReadOnlyList<string> Resolved);

/// <summary>
/// The inventory validator that runs before ANY connection (OET-RWP/1 section 8.9, RW-135):
/// strict syntax, then the forbidden classes, then DNS (every A/AAAA record must be allowed)
/// and finally the manager's own addresses. A host name is resolved once here; the result is
/// advisory (a later DNS change is re-checked when the next operation starts).
/// </summary>
public sealed class AddressGuard
{
    private readonly IHostResolver _resolver;
    private readonly HashSet<string> _ownAddresses;
    private readonly HashSet<string> _extraForbidden;

    public AddressGuard(
        IHostResolver resolver,
        IEnumerable<string>? ownAddresses = null,
        IEnumerable<string>? extraForbiddenAddresses = null)
    {
        _resolver = resolver;
        _ownAddresses = Normalize(ownAddresses);
        _extraForbidden = Normalize(extraForbiddenAddresses);
    }

    public async Task<AddressCheck> CheckAsync(string? input, CancellationToken cancellationToken)
    {
        if (!InputValidator.TryParseAddress(input, out var kind, out var normalized, out var ip, out var issue))
        {
            return Denied(string.Empty, AddressKind.Hostname, "address_invalid", issue?.Message ?? "address is invalid.", []);
        }

        if (kind != AddressKind.Hostname)
        {
            var reason = Classify(ip!);
            return reason is null
                ? new AddressCheck(true, normalized, kind, null, null, [normalized])
                : Denied(normalized, kind, ForbiddenCode, "address is not allowed (" + reason + ").", [normalized]);
        }

        if (AddressPolicy.IsProductionHostName(normalized))
        {
            return Denied(normalized, kind, ForbiddenCode, "host names of the production deployment are never helpers.", []);
        }

        IReadOnlyList<IPAddress> resolved;
        try
        {
            resolved = await _resolver.ResolveAsync(normalized, cancellationToken);
        }
        catch (SocketException)
        {
            resolved = Array.Empty<IPAddress>();
        }

        if (resolved.Count == 0)
        {
            return Denied(normalized, kind, "address_unresolvable", "the host name does not resolve.", []);
        }

        var resolvedText = resolved.Select(a => AddressPolicy.Unwrap(a).ToString()).ToList();
        foreach (var address in resolved)
        {
            var reason = Classify(address);
            if (reason is not null)
            {
                return Denied(normalized, kind, ForbiddenCode, "the host name resolves to a forbidden address (" + reason + ").", resolvedText);
            }
        }

        return new AddressCheck(true, normalized, kind, null, null, resolvedText);
    }

    public const string ForbiddenCode = "forbidden_host";

    private string? Classify(IPAddress address)
    {
        var reason = AddressPolicy.ForbiddenReason(address);
        if (reason is not null)
        {
            return reason;
        }

        var text = AddressPolicy.Unwrap(address).ToString();
        if (_ownAddresses.Contains(text))
        {
            return "own_address";
        }

        return _extraForbidden.Contains(text) ? "forbidden_list" : null;
    }

    private static AddressCheck Denied(string normalized, AddressKind kind, string code, string message, IReadOnlyList<string> resolved) =>
        new(false, normalized, kind, code, message, resolved);

    private static HashSet<string> Normalize(IEnumerable<string>? values)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (values is null)
        {
            return set;
        }

        foreach (var value in values)
        {
            if (IPAddress.TryParse(value, out var ip))
            {
                set.Add(AddressPolicy.Unwrap(ip).ToString());
            }
        }

        return set;
    }
}
