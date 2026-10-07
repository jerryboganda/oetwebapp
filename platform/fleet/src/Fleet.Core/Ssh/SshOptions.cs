using System.Globalization;
using System.Text.RegularExpressions;

namespace Fleet.Core.Ssh;

/// <summary>
/// The one and only way the manager builds an <c>ssh</c> command line (OET-RWP/1 section 7.4,
/// RW-136). The option list is fixed: <c>StrictHostKeyChecking=yes</c> against a per-host pinned
/// known_hosts file, key-only auth, no multiplexing. Trust-on-first-use host key policies are
/// forbidden everywhere in the fleet code (a repository test scans for them).
/// </summary>
public static class SshOptions
{
    public const string FleetUser = "oetfleet";

    /// <summary>The <c>-o</c> values in the exact order of section 7.4.</summary>
    public static IReadOnlyList<string> BaselineOptions(string knownHostsFile) => new[]
    {
        "StrictHostKeyChecking=yes",
        "UserKnownHostsFile=" + knownHostsFile,
        "HashKnownHosts=no",
        "VerifyHostKeyDNS=no",
        "IdentitiesOnly=yes",
        "BatchMode=yes",
        "PreferredAuthentications=publickey",
        "ConnectTimeout=10",
        "ServerAliveInterval=15",
        "ServerAliveCountMax=3",
        "ControlMaster=no",
    };

    /// <summary>Arguments for <c>ssh</c> (no program name): options, identity, port, destination, <c>--</c>, remote command.</summary>
    public static IReadOnlyList<string> BuildArguments(
        string knownHostsFile,
        string identityFile,
        string host,
        int port,
        string user,
        IReadOnlyList<string> remoteCommand)
    {
        var args = new List<string>();
        foreach (var option in BaselineOptions(knownHostsFile))
        {
            args.Add("-o");
            args.Add(option);
        }

        args.Add("-i");
        args.Add(identityFile);
        args.Add("-p");
        args.Add(port.ToString(CultureInfo.InvariantCulture));
        args.Add(user + "@" + host);
        args.Add("--");
        args.AddRange(remoteCommand);
        return args;
    }

    /// <summary>The same strict options as one string for Ansible's <c>ansible_ssh_common_args</c> (no remote command, no identity).</summary>
    public static string AnsibleCommonArgs(string knownHostsFile)
    {
        var parts = new List<string>();
        foreach (var option in BaselineOptions(knownHostsFile))
        {
            parts.Add("-o");
            parts.Add(option);
        }

        return string.Join(' ', parts);
    }

    /// <summary>True when the stderr of a failed ssh run says the pinned key no longer matches.</summary>
    public static bool IndicatesChangedHostKey(string? stderr) =>
        stderr is not null
        && (stderr.Contains("REMOTE HOST IDENTIFICATION HAS CHANGED", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Offending", StringComparison.Ordinal));
}

/// <summary>The allow-listed keys of the helper's <c>/etc/oet-fleet/agent.env</c> (section 7.4 <c>put-env</c>).</summary>
public static class AgentEnv
{
    public static readonly IReadOnlyList<string> AllowedKeys = new[]
    {
        "OET_API_BASE", "OET_NODE_ID", "OET_NODE_TOKEN", "OET_AGENT_IMAGE_DIGEST",
        "OET_BUDGET_CPU_MILLI", "OET_BUDGET_MEM_MIB", "OET_BUDGET_TMP_MIB", "OET_LOG_LEVEL",
        // Trust plane (decision D3): rendered only when the manager itself runs in trust mode.
        "OET_TRUST_ENABLED", "OET_TRUST_PORT", "OET_TRUST_CERT_PATH", "OET_TRUST_KEY_PATH", "OET_TRUST_CA_PATH",
    };

    /// <summary>Characters a value may contain. Identical to the pattern enforced by <c>oet-fleet-ctl put-env</c>.</summary>
    public const string ValuePattern = @"[A-Za-z0-9._:/@+=-]{1,200}";

    private static readonly Regex ValueRegex = new(@"\A" + ValuePattern + @"\z", RegexOptions.CultureInvariant);

    /// <summary>Renders <c>KEY=value</c> lines in allow-list order. Throws on an unknown key or an unsafe value.</summary>
    public static string Render(IReadOnlyDictionary<string, string> values)
    {
        foreach (var key in values.Keys)
        {
            if (!AllowedKeys.Contains(key, StringComparer.Ordinal))
            {
                throw new ArgumentException("Environment key '" + key + "' is not allowed.");
            }
        }

        var lines = new List<string>();
        foreach (var key in AllowedKeys)
        {
            if (!values.TryGetValue(key, out var value))
            {
                continue;
            }

            if (!ValueRegex.IsMatch(value))
            {
                throw new ArgumentException("Environment value for '" + key + "' contains characters that are not allowed.");
            }

            lines.Add(key + "=" + value);
        }

        return string.Join('\n', lines) + "\n";
    }
}
