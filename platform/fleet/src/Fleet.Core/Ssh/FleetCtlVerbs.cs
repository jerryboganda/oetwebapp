using System.Text.RegularExpressions;

namespace Fleet.Core.Ssh;

public sealed record CtlVerbSpec(string Verb, string ArgsPattern, bool UsesStdin, int MaxStdinBytes);

/// <summary>
/// The complete vocabulary of the restricted <c>oet-fleet-ctl</c> on a helper (OET-RWP/1 section 7.4,
/// RW-141). The manager can only build these commands; <c>oet-fleet-gate</c> (the SSH forced command)
/// and <c>oet-fleet-ctl</c> itself validate the same patterns on the helper, and a repository test
/// proves the three lists agree. <c>uninstall</c> is an additive verb used by host removal: it removes
/// ONLY fleet-owned components (the agent container, agent-repository images, <c>/etc/oet-fleet</c>, the
/// systemd unit) and locks the <c>oetfleet</c> account; it never touches Docker itself, the firewall,
/// other containers, other images or any volume.
/// </summary>
public static class FleetCtlVerbs
{
    public const string ProgramName = "oet-fleet-ctl";
    public const string Registry = "ghcr.io";
    public const string AgentRepository = "ghcr.io/jerryboganda/oetwebapp-fleet-agent";

    public const string DigestPattern = @"sha256:[0-9a-f]{64}";
    public const string GracePattern = @"([1-9]|[1-9][0-9]|1[01][0-9]|120)";
    public const string TailPattern = @"([1-9]|[1-9][0-9]|1[0-9][0-9]|200)";

    public static readonly IReadOnlyList<CtlVerbSpec> All = new CtlVerbSpec[]
    {
        new("status", "", false, 0),
        new("harden-check", "", false, 0),
        new("login", @"--registry ghcr\.io", true, 512),
        new("pull", @"ghcr\.io/jerryboganda/oetwebapp-fleet-agent@" + DigestPattern, false, 0),
        new("verify", DigestPattern + "( " + DigestPattern + ")?", false, 0),
        new("logout", "", false, 0),
        new("put-env", "", true, 4096),
        // Trust plane (decision D3): one JSON object {ca, cert, key} of PEM strings for the UBAG node identity.
        new("put-certs", "", true, 16384),
        new("ubag-reconcile", "", true, 8192),
        new("run", DigestPattern, false, 0),
        new("stop", "(--grace " + GracePattern + ")?", false, 0),
        new("restart", "", false, 0),
        new("logs", "--tail " + TailPattern, false, 0),
        new("prune", "", false, 0),
        new("wipe-scratch", "", false, 0),
        new("unit-sync", "", false, 0),
        new("uninstall", "", false, 0),
    };

    private static readonly IReadOnlyDictionary<string, Regex> Compiled = All.ToDictionary(
        spec => spec.Verb,
        spec => new Regex(@"\A(?:" + spec.ArgsPattern + @")\z", RegexOptions.CultureInvariant),
        StringComparer.Ordinal);

    public static CtlVerbSpec? Find(string verb) => All.FirstOrDefault(spec => string.Equals(spec.Verb, verb, StringComparison.Ordinal));

    /// <summary>
    /// Validates a verb and its arguments and returns the exact remote command (<c>oet-fleet-ctl verb args...</c>).
    /// Arguments are matched as one space-joined string, so none may itself contain a space.
    /// </summary>
    public static bool TryBuild(string verb, IReadOnlyList<string> args, out IReadOnlyList<string> command, out string? error)
    {
        command = Array.Empty<string>();
        error = null;
        if (!Compiled.TryGetValue(verb, out var pattern))
        {
            error = "unknown verb";
            return false;
        }

        if (args.Any(a => a.Length == 0 || a.Contains(' ') || a.Contains('\n') || a.Contains('\r') || a.Contains('\t')))
        {
            error = "arguments must be non-empty and contain no whitespace";
            return false;
        }

        if (!pattern.IsMatch(string.Join(' ', args)))
        {
            error = "arguments do not match the verb pattern";
            return false;
        }

        var built = new List<string> { ProgramName, verb };
        built.AddRange(args);
        command = built;
        return true;
    }

    public static IReadOnlyList<string> Build(string verb, params string[] args)
    {
        if (!TryBuild(verb, args, out var command, out var error))
        {
            throw new ArgumentException("oet-fleet-ctl " + verb + ": " + error);
        }

        return command;
    }

    public static string PullReference(string digest) => AgentRepository + "@" + digest;
}
