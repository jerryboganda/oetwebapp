namespace Fleet.Agent.Tests;

/// <summary>The OET_TRUST_* block of the agent environment (decision D3): off by default, and an
/// enabled block must name three distinct paths and a sane port. These run everywhere.</summary>
public sealed class TrustOptionsTests
{
    private static Func<string, string?> Env(Dictionary<string, string> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;

    private static Dictionary<string, string> ValidEnv() => new()
    {
        ["OET_API_BASE"] = "https://api.oetwithdrhesham.co.uk",
        ["OET_NODE_ID"] = TestIds.NodeId,
        ["OET_NODE_TOKEN"] = TestIds.NodeToken(),
        ["OET_AGENT_IMAGE_DIGEST"] = TestIds.Digest,
    };

    [Fact]
    public void trust_is_disabled_by_default()
    {
        var (options, problems) = AgentOptions.FromEnvironment(Env(ValidEnv()));

        Assert.Empty(problems);
        Assert.False(options!.Trust.Enabled);
        Assert.Equal(7443, options.Trust.Port);
        Assert.Equal("/certs/ubag/node.crt", options.Trust.CertPath);
    }

    [Fact]
    public void an_enabled_block_takes_the_documented_defaults()
    {
        var env = ValidEnv();
        env["OET_TRUST_ENABLED"] = "true";

        var (options, problems) = AgentOptions.FromEnvironment(Env(env));

        Assert.Empty(problems);
        Assert.True(options!.Trust.Enabled);
        Assert.Equal(7443, options.Trust.Port);
        Assert.Equal("/certs/ubag/node.crt", options.Trust.CertPath);
        Assert.Equal("/certs/ubag/node.key", options.Trust.KeyPath);
        Assert.Equal("/certs/ubag/ca.crt", options.Trust.CaPath);
    }

    [Fact]
    public void port_and_paths_are_honoured_when_given()
    {
        var env = ValidEnv();
        env["OET_TRUST_ENABLED"] = "true";
        env["OET_TRUST_PORT"] = "8443";
        env["OET_TRUST_CERT_PATH"] = "/certs/ubag/other.crt";

        var (options, problems) = AgentOptions.FromEnvironment(Env(env));

        Assert.Empty(problems);
        Assert.Equal(8443, options!.Trust.Port);
        Assert.Equal("/certs/ubag/other.crt", options.Trust.CertPath);
    }

    [Fact]
    public void a_bad_flag_or_duplicate_paths_is_a_configuration_error()
    {
        var bad = ValidEnv();
        bad["OET_TRUST_ENABLED"] = "maybe";
        Assert.NotEmpty(AgentOptions.FromEnvironment(Env(bad)).Problems);

        var duplicate = ValidEnv();
        duplicate["OET_TRUST_ENABLED"] = "true";
        duplicate["OET_TRUST_KEY_PATH"] = "/certs/ubag/node.crt";
        Assert.NotEmpty(AgentOptions.FromEnvironment(Env(duplicate)).Problems);

        var outOfRange = ValidEnv();
        outOfRange["OET_TRUST_ENABLED"] = "true";
        outOfRange["OET_TRUST_PORT"] = "80";
        Assert.NotEmpty(AgentOptions.FromEnvironment(Env(outOfRange)).Problems);
    }

    [Fact]
    public void explicitly_disabled_mirrors_absent()
    {
        var env = ValidEnv();
        env["OET_TRUST_ENABLED"] = "false";
        env["OET_TRUST_PORT"] = "8443";

        var (options, problems) = AgentOptions.FromEnvironment(Env(env));

        Assert.Empty(problems);
        Assert.False(options!.Trust.Enabled);
    }
}
