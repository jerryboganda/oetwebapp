using System.Globalization;

namespace Fleet.Agent;

/// <summary>
/// Configuration of the helper, read from the environment file written by <c>oet-fleet-ctl put-env</c>
/// (allow-list: OET_API_BASE, OET_NODE_ID, OET_NODE_TOKEN, OET_AGENT_IMAGE_DIGEST, OET_BUDGET_*,
/// OET_TRUST_*, OET_LOG_LEVEL). The token is held in memory only and never printed (H8).
/// </summary>
internal sealed class AgentOptions
{
    public required Uri ApiBase { get; init; }
    public required string NodeId { get; init; }
    public required string NodeToken { get; init; }
    public required string ImageDigest { get; init; }
    public required Budgets Budgets { get; init; }
    public LogLevel LogLevel { get; init; } = LogLevel.Information;
    public string ScratchDirectory { get; init; } = "/scratch";
    public string TmpDirectory { get; init; } = "/tmp";
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string HealthFile { get; init; } = "/tmp/oet-agent-health";

    /// <summary>The UBAG trust plane (decision D3). Off unless the manager rendered OET_TRUST_ENABLED=true;
    /// the certificate files are mounted read-only by <c>oet-fleet-ctl run</c> when it provisioned them.</summary>
    public TrustOptions Trust { get; init; } = new();

    /// <summary>Test seam: allows plain http for loopback hosts only.</summary>
    public bool AllowInsecureLoopback { get; init; }

    public override string ToString() => "AgentOptions(node=" + NodeId + ")";

    /// <summary>
    /// Parses and validates the environment. Returns the options, or a list of problem codes that never contain a value
    /// (a malformed token must not end up in a log).
    /// </summary>
    public static (AgentOptions? Options, IReadOnlyList<string> Problems) FromEnvironment(Func<string, string?> get)
    {
        var problems = new List<string>();

        Uri? api = null;
        var apiText = get("OET_API_BASE");
        if (string.IsNullOrWhiteSpace(apiText) || !Uri.TryCreate(apiText.Trim(), UriKind.Absolute, out api)
            || api.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(api.UserInfo) || api.AbsolutePath.Length > 1
            || !string.IsNullOrEmpty(api.Query))
        {
            problems.Add("OET_API_BASE must be an https origin without credentials, path or query");
        }

        var nodeId = get("OET_NODE_ID")?.Trim() ?? "";
        if (!Wire.NodeIdPattern.IsMatch(nodeId)) problems.Add("OET_NODE_ID is missing or malformed");

        var token = get("OET_NODE_TOKEN")?.Trim() ?? "";
        if (token.Length > 80 || !Wire.NodeTokenPattern.IsMatch(token)) problems.Add("OET_NODE_TOKEN is missing or malformed");

        var digest = get("OET_AGENT_IMAGE_DIGEST")?.Trim() ?? "";
        if (!Wire.DigestPattern.IsMatch(digest)) problems.Add("OET_AGENT_IMAGE_DIGEST is missing or malformed");

        var cpu = ReadInt(get, "OET_BUDGET_CPU_MILLI", 3000, 0, 64_000, problems);
        var mem = ReadInt(get, "OET_BUDGET_MEM_MIB", 5120, 0, 262_144, problems);
        var tmp = ReadInt(get, "OET_BUDGET_TMP_MIB", 3072, 0, 65_536, problems);
        if (tmp > mem) problems.Add("OET_BUDGET_TMP_MIB must not exceed OET_BUDGET_MEM_MIB");

        var level = LogLevel.Information;
        var levelText = get("OET_LOG_LEVEL");
        if (!string.IsNullOrWhiteSpace(levelText) && !Enum.TryParse(levelText.Trim(), ignoreCase: true, out level))
        {
            problems.Add("OET_LOG_LEVEL is not a log level");
        }

        var trust = ReadTrust(get, problems);
        if (problems.Count > 0 || api is null) return (null, problems);
        return (new AgentOptions
        {
            ApiBase = new Uri(api.GetLeftPart(UriPartial.Authority)),
            NodeId = nodeId,
            NodeToken = token,
            ImageDigest = digest,
            Budgets = new Budgets { CpuMilli = cpu, MemMiB = mem, TmpMiB = tmp },
            LogLevel = level,
            Trust = trust,
        }, problems);
    }

    /// <summary>Reads the OET_TRUST_* block. A disabled (or absent) block tolerates anything; an enabled
    /// one must name a port and three paths inside the mounted certificate directory.</summary>
    private static TrustOptions ReadTrust(Func<string, string?> get, List<string> problems)
    {
        var enabledText = get("OET_TRUST_ENABLED")?.Trim();
        if (string.IsNullOrEmpty(enabledText))
        {
            return new TrustOptions();
        }

        if (enabledText is not ("true" or "false"))
        {
            problems.Add("OET_TRUST_ENABLED must be true or false");
            return new TrustOptions();
        }

        if (enabledText == "false")
        {
            return new TrustOptions();
        }

        var trust = new TrustOptions
        {
            Enabled = true,
            Port = ReadInt(get, "OET_TRUST_PORT", 7443, 1024, 65_535, problems),
            CertPath = ReadPath(get, "OET_TRUST_CERT_PATH", "/certs/ubag/node.crt", problems),
            KeyPath = ReadPath(get, "OET_TRUST_KEY_PATH", "/certs/ubag/node.key", problems),
            CaPath = ReadPath(get, "OET_TRUST_CA_PATH", "/certs/ubag/ca.crt", problems),
        };
        if (trust.CertPath == trust.KeyPath || trust.CertPath == trust.CaPath || trust.KeyPath == trust.CaPath)
        {
            problems.Add("OET_TRUST_* paths must be three distinct files");
        }

        return trust;
    }

    private static string ReadPath(Func<string, string?> get, string name, string fallback, List<string> problems)
    {
        var text = get(name)?.Trim() ?? "";
        if (text.Length == 0)
        {
            return fallback;
        }

        if (text.Length > 200 || text.Contains('\0') || text.Contains(".."))
        {
            problems.Add(name + " is not an allowed absolute path");
            return fallback;
        }

        return text;
    }

    private static int ReadInt(Func<string, string?> get, string name, int fallback, int min, int max, List<string> problems)
    {
        var text = get(name);
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            problems.Add(name + " is not an integer in range");
            return fallback;
        }

        return value;
    }
}

/// <summary>The helper side of the UBAG trust plane (decision D3): the mTLS listener UBAG dials.
/// Disabled by default; the manager only renders these keys once it has provisioned the node
/// certificate through the restricted <c>put-certs</c> verb.</summary>
internal sealed record TrustOptions
{
    public bool Enabled { get; init; }

    /// <summary>The inbound port UBAG dials; the allocation endpoint's <c>{address}:7443</c> contract.</summary>
    public int Port { get; init; } = 7443;

    public string CertPath { get; init; } = "/certs/ubag/node.crt";

    public string KeyPath { get; init; } = "/certs/ubag/node.key";

    /// <summary>The manager CA, used to validate a client certificate the dialer presents (when it presents one).</summary>
    public string CaPath { get; init; } = "/certs/ubag/ca.crt";
}
