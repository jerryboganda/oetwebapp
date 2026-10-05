using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Fleet.Core.Ssh;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Provisioning;

/// <summary>Host-key trust, the one ssh command line, the agent env allow-list and the ctl vocabulary (OET-RWP/1 sections 7.4 and 8.6).</summary>
public sealed class SshPrimitivesTests
{
    /// <summary>A well-formed OpenSSH public key blob: string(algorithm) + string(material).</summary>
    private static string BlobFor(string algorithm, int seed, int materialLength = 32)
    {
        var material = SHA512.HashData(Encoding.UTF8.GetBytes(algorithm + ":" + seed)).AsSpan(0, Math.Min(materialLength, 64)).ToArray();
        using var stream = new MemoryStream();
        foreach (var part in new[] { Encoding.ASCII.GetBytes(algorithm), material })
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)part.Length);
            stream.Write(length);
            stream.Write(part);
        }

        return Convert.ToBase64String(stream.ToArray());
    }

    private static string ForbiddenTofuOption() => "accept" + "-new";

    // ---- host keys -------------------------------------------------------------------------

    [Fact]
    public void A_fingerprint_is_sha256_of_the_decoded_blob_in_the_format_openssh_prints()
    {
        var blob = BlobFor("ssh-ed25519", 1);

        var fingerprint = HostKeys.FingerprintSha256(blob);

        Assert.StartsWith("SHA256:", fingerprint);
        Assert.Equal(7 + 43, fingerprint.Length);
        Assert.DoesNotContain('=', fingerprint);
        Assert.Equal("SHA256:" + Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(blob))).TrimEnd('='), fingerprint);
        Assert.NotEqual(fingerprint, HostKeys.FingerprintSha256(BlobFor("ssh-ed25519", 2)));
    }

    [Fact]
    public void The_key_scan_is_parsed_defensively_and_only_well_formed_known_keys_survive()
    {
        var ed25519 = BlobFor("ssh-ed25519", 1);
        var ecdsa = BlobFor("ecdsa-sha2-nistp256", 2, 64);
        var rsa = BlobFor("ssh-rsa", 3, 64);
        var output = string.Join(
            '\n',
            "# 203.0.113.10:22 SSH-2.0-OpenSSH_9.6p1",
            "203.0.113.10 ssh-rsa " + rsa,
            "203.0.113.10 ssh-ed25519 " + ed25519,
            "203.0.113.10 ssh-ed25519 " + ed25519,
            "203.0.113.10 ecdsa-sha2-nistp256 " + ecdsa,
            "203.0.113.10 ssh-dss " + BlobFor("ssh-dss", 4),
            "203.0.113.10 ssh-rsa " + ed25519,
            "203.0.113.10 ssh-ed25519 not-base64!!",
            "203.0.113.10 ssh-ed25519",
            string.Empty,
            "garbage");

        var keys = HostKeys.ParseKeyScan(output);

        Assert.Equal(new[] { "ssh-rsa", "ssh-ed25519", "ecdsa-sha2-nistp256" }, keys.Select(k => k.Algorithm).ToArray());
        Assert.All(keys, key => Assert.Equal(HostKeys.FingerprintSha256(key.PublicKeyBase64), key.Fingerprint));
        Assert.Empty(HostKeys.ParseKeyScan(string.Empty));
    }

    [Fact]
    public void The_preferred_key_is_ed25519_then_ecdsa_then_rsa()
    {
        var rsa = new ScannedHostKey("ssh-rsa", BlobFor("ssh-rsa", 3, 64), "SHA256:r");
        var p384 = new ScannedHostKey("ecdsa-sha2-nistp384", BlobFor("ecdsa-sha2-nistp384", 4, 64), "SHA256:e");
        var p256 = new ScannedHostKey("ecdsa-sha2-nistp256", BlobFor("ecdsa-sha2-nistp256", 5, 64), "SHA256:f");
        var ed = new ScannedHostKey("ssh-ed25519", BlobFor("ssh-ed25519", 6), "SHA256:d");

        Assert.Same(ed, HostKeys.Preferred(new[] { rsa, p384, p256, ed }));
        Assert.Same(p256, HostKeys.Preferred(new[] { rsa, p384, p256 }));
        Assert.Same(p384, HostKeys.Preferred(new[] { rsa, p384 }));
        Assert.Same(rsa, HostKeys.Preferred(new[] { rsa }));
        Assert.Null(HostKeys.Preferred(Array.Empty<ScannedHostKey>()));
        Assert.Null(HostKeys.Preferred(new[] { new ScannedHostKey("ssh-dss", "AAAA", "SHA256:x") }));
    }

    [Fact]
    public void A_known_hosts_line_is_rebuilt_from_validated_parts_only()
    {
        var blob = BlobFor("ssh-ed25519", 1);

        Assert.Equal("203.0.113.10 ssh-ed25519 " + blob, HostKeys.KnownHostsLine("203.0.113.10", 22, "ssh-ed25519", blob));
        Assert.Equal("[203.0.113.10]:2222 ssh-ed25519 " + blob, HostKeys.KnownHostsLine("203.0.113.10", 2222, "ssh-ed25519", blob));

        Assert.Throws<ArgumentException>(() => HostKeys.KnownHostsLine("203.0.113.10", 22, "ssh-dss", blob));
        Assert.Throws<ArgumentException>(() => HostKeys.KnownHostsLine("203.0.113.10", 22, "ssh-ed25519", "short"));
        Assert.Throws<ArgumentException>(() => HostKeys.KnownHostsLine("203.0.113.10", 22, "ssh-ed25519", blob + "\nevil.example ssh-ed25519 " + blob));
        Assert.Throws<ArgumentException>(() => HostKeys.KnownHostsLine("203.0.113.10", 22, "ssh-ed25519 # comment", blob));
    }

    [Fact]
    public void The_owner_confirms_a_key_by_typing_the_first_eight_characters_of_its_fingerprint()
    {
        var fingerprint = HostKeys.FingerprintSha256(BlobFor("ssh-ed25519", 1));
        var body = fingerprint["SHA256:".Length..];

        Assert.True(HostKeys.PrefixMatches(fingerprint, body[..8]));
        Assert.True(HostKeys.PrefixMatches(fingerprint, "SHA256:" + body[..8]));
        Assert.True(HostKeys.PrefixMatches(fingerprint, "  " + body[..12] + "  "));
        Assert.True(HostKeys.PrefixMatches(fingerprint, body));

        Assert.False(HostKeys.PrefixMatches(fingerprint, body[..7]));
        Assert.False(HostKeys.PrefixMatches(fingerprint, null));
        Assert.False(HostKeys.PrefixMatches(fingerprint, string.Empty));
        Assert.False(HostKeys.PrefixMatches(fingerprint, body + "x"));
        Assert.False(HostKeys.PrefixMatches(fingerprint, SwapCase(body[..8])));
        Assert.False(HostKeys.PrefixMatches(fingerprint, "AAAAAAAA"));
        Assert.False(HostKeys.PrefixMatches("not-a-fingerprint", "not-a-fi"));
    }

    private static string SwapCase(string value) =>
        new(value.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.IsLower(c) ? char.ToUpperInvariant(c) : (char)(c == '+' ? '/' : c == '/' ? '+' : c == '0' ? '1' : c)).ToArray());

    // ---- ssh command line ------------------------------------------------------------------

    [Fact]
    public void The_ssh_options_are_fixed_strict_and_in_the_documented_order()
    {
        var options = SshOptions.BaselineOptions("/tmp/known_hosts");

        Assert.Equal(
            new[]
            {
                "StrictHostKeyChecking=yes",
                "UserKnownHostsFile=/tmp/known_hosts",
                "HashKnownHosts=no",
                "VerifyHostKeyDNS=no",
                "IdentitiesOnly=yes",
                "BatchMode=yes",
                "PreferredAuthentications=publickey",
                "ConnectTimeout=10",
                "ServerAliveInterval=15",
                "ServerAliveCountMax=3",
                "ControlMaster=no",
            },
            options.ToArray());
        Assert.DoesNotContain(options, option => option.Contains(ForbiddenTofuOption(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(options, option => option.Equals("StrictHostKeyChecking=no", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_ssh_command_line_separates_options_identity_destination_and_remote_command()
    {
        var args = SshOptions.BuildArguments("/s/known_hosts", "/s/key", "203.0.113.10", 2222, SshOptions.FleetUser, new[] { "oet-fleet-ctl", "status" });

        var optionValues = args.Where((_, i) => i > 0 && args[i - 1] == "-o").ToArray();
        Assert.Equal(SshOptions.BaselineOptions("/s/known_hosts").ToArray(), optionValues);
        Assert.Equal(new[] { "-i", "/s/key", "-p", "2222", "oetfleet@203.0.113.10", "--", "oet-fleet-ctl", "status" }, args.Skip(args.Count - 8).ToArray());
        Assert.Equal(11 * 2 + 8, args.Count);
    }

    [Fact]
    public void A_hostile_remote_command_stays_one_argument_after_the_double_dash()
    {
        var args = SshOptions.BuildArguments("/s/known_hosts", "/s/key", "203.0.113.10", 22, "oetfleet", new[] { "ls; rm -rf /", "$(id)" });

        var dashes = args.ToList().IndexOf("--");
        Assert.Equal(new[] { "ls; rm -rf /", "$(id)" }, args.Skip(dashes + 1).ToArray());
        Assert.DoesNotContain(args.Take(dashes), argument => argument.Contains(';', StringComparison.Ordinal));
    }

    [Fact]
    public void Ansible_gets_the_same_strict_options_as_one_string()
    {
        var common = SshOptions.AnsibleCommonArgs("/s/known_hosts");

        Assert.Contains("-o StrictHostKeyChecking=yes", common);
        Assert.Contains("-o UserKnownHostsFile=/s/known_hosts", common);
        Assert.Contains("-o ControlMaster=no", common);
        Assert.DoesNotContain(ForbiddenTofuOption(), common, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(11, common.Split(" -o ").Length - 1 + (common.StartsWith("-o ", StringComparison.Ordinal) ? 1 : 0));
    }

    [Theory]
    [InlineData("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@\n@    WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!     @", true)]
    [InlineData("Host key verification failed.", true)]
    [InlineData("Offending ED25519 key in /tmp/known_hosts:1", true)]
    [InlineData("ssh: connect to host 203.0.113.10 port 22: Connection refused", false)]
    [InlineData("Permission denied (publickey).", false)]
    [InlineData("", false)]
    public void A_changed_host_key_is_recognised_from_ssh_stderr(string stderr, bool expected)
    {
        Assert.Equal(expected, SshOptions.IndicatesChangedHostKey(stderr));
        Assert.False(SshOptions.IndicatesChangedHostKey(null));
    }

    // ---- agent env -------------------------------------------------------------------------

    [Fact]
    public void The_agent_env_is_rendered_in_allow_list_order_with_a_trailing_newline()
    {
        var values = new Dictionary<string, string>
        {
            ["OET_LOG_LEVEL"] = "Information",
            ["OET_NODE_TOKEN"] = "orw1_abcdef0123456789_" + new string('A', 43),
            ["OET_API_BASE"] = "https://api.oetwithdrhesham.co.uk",
            ["OET_NODE_ID"] = "rw_00000000000000000000000001",
        };

        var text = AgentEnv.Render(values);

        Assert.EndsWith("\n", text);
        var keys = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line[..line.IndexOf('=')]).ToArray();
        Assert.Equal(new[] { "OET_API_BASE", "OET_NODE_ID", "OET_NODE_TOKEN", "OET_LOG_LEVEL" }, keys);
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("line\nbreak")]
    [InlineData("$(id)")]
    [InlineData("`id`")]
    [InlineData("a;b")]
    [InlineData("\"quoted\"")]
    [InlineData("back\\slash")]
    [InlineData("")]
    public void An_unsafe_env_value_is_refused(string value)
    {
        Assert.Throws<ArgumentException>(() => AgentEnv.Render(new Dictionary<string, string> { ["OET_API_BASE"] = value }));
    }

    [Fact]
    public void An_unknown_env_key_or_a_value_over_two_hundred_characters_is_refused()
    {
        Assert.Throws<ArgumentException>(() => AgentEnv.Render(new Dictionary<string, string> { ["PATH"] = "/usr/bin" }));
        Assert.Throws<ArgumentException>(() => AgentEnv.Render(new Dictionary<string, string> { ["OET_API_BASE"] = new string('a', 201) }));
        Assert.Contains("OET_API_BASE=" + new string('a', 200), AgentEnv.Render(new Dictionary<string, string> { ["OET_API_BASE"] = new string('a', 200) }));
    }

    // ---- ctl vocabulary --------------------------------------------------------------------

    private static readonly string Digest = "sha256:" + new string('a', 64);
    private static readonly string OtherDigest = "sha256:" + new string('b', 64);

    [Fact]
    public void Every_documented_ctl_call_builds_exactly_the_command_it_names()
    {
        Assert.Equal(new[] { "oet-fleet-ctl", "status" }, FleetCtlVerbs.Build("status"));
        Assert.Equal(new[] { "oet-fleet-ctl", "login", "--registry", "ghcr.io" }, FleetCtlVerbs.Build("login", "--registry", "ghcr.io"));
        Assert.Equal(new[] { "oet-fleet-ctl", "pull", FleetCtlVerbs.PullReference(Digest) }, FleetCtlVerbs.Build("pull", FleetCtlVerbs.PullReference(Digest)));
        Assert.Equal(new[] { "oet-fleet-ctl", "verify", Digest }, FleetCtlVerbs.Build("verify", Digest));
        Assert.Equal(new[] { "oet-fleet-ctl", "verify", Digest, OtherDigest }, FleetCtlVerbs.Build("verify", Digest, OtherDigest));
        Assert.Equal(new[] { "oet-fleet-ctl", "run", Digest }, FleetCtlVerbs.Build("run", Digest));
        Assert.Equal(new[] { "oet-fleet-ctl", "stop" }, FleetCtlVerbs.Build("stop"));
        Assert.Equal(new[] { "oet-fleet-ctl", "stop", "--grace", "120" }, FleetCtlVerbs.Build("stop", "--grace", "120"));
        Assert.Equal(new[] { "oet-fleet-ctl", "logs", "--tail", "200" }, FleetCtlVerbs.Build("logs", "--tail", "200"));
        foreach (var verb in new[] { "harden-check", "logout", "put-env", "restart", "prune", "wipe-scratch", "unit-sync", "uninstall" })
        {
            Assert.Equal(new[] { "oet-fleet-ctl", verb }, FleetCtlVerbs.Build(verb));
        }
    }

    public static IEnumerable<object[]> RefusedCalls()
    {
        var digest = "sha256:" + new string('a', 64);
        yield return new object[] { "rm", Array.Empty<string>() };
        yield return new object[] { "STATUS", Array.Empty<string>() };
        yield return new object[] { "status", new[] { "extra" } };
        yield return new object[] { "pull", new[] { "ghcr.io/someone-else/agent@" + digest } };
        yield return new object[] { "pull", new[] { FleetCtlVerbs.AgentRepository + ":latest" } };
        yield return new object[] { "pull", new[] { FleetCtlVerbs.AgentRepository + "@sha256:abc" } };
        yield return new object[] { "run", new[] { "sha256:" + new string('A', 64) } };
        yield return new object[] { "run", new[] { digest, "--privileged" } };
        yield return new object[] { "verify", new[] { digest, digest, digest } };
        yield return new object[] { "login", new[] { "--registry", "evil.example" } };
        yield return new object[] { "stop", new[] { "--grace", "121" } };
        yield return new object[] { "stop", new[] { "--grace", "0" } };
        yield return new object[] { "logs", new[] { "--tail", "201" } };
        yield return new object[] { "logs", new[] { "--tail", "all" } };
        yield return new object[] { "run", new[] { digest + " --privileged" } };
        yield return new object[] { "run", new[] { digest + "\nstatus" } };
        yield return new object[] { "run", new[] { string.Empty } };
    }

    [Theory]
    [MemberData(nameof(RefusedCalls))]
    public void Anything_outside_the_vocabulary_cannot_be_built(string verb, string[] args)
    {
        Assert.False(FleetCtlVerbs.TryBuild(verb, args, out var command, out var error));
        Assert.Empty(command);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Throws<ArgumentException>(() => FleetCtlVerbs.Build(verb, args));
    }

    [Fact]
    public void Only_login_and_put_env_take_stdin_and_each_has_a_size_cap()
    {
        var withStdin = FleetCtlVerbs.All.Where(spec => spec.UsesStdin).Select(spec => spec.Verb).OrderBy(v => v, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "login", "put-env" }, withStdin);
        Assert.All(FleetCtlVerbs.All.Where(spec => spec.UsesStdin), spec => Assert.InRange(spec.MaxStdinBytes, 1, 4096));
        Assert.All(FleetCtlVerbs.All.Where(spec => !spec.UsesStdin), spec => Assert.Equal(0, spec.MaxStdinBytes));
        Assert.Equal(FleetCtlVerbs.All.Count, FleetCtlVerbs.All.Select(spec => spec.Verb).Distinct(StringComparer.Ordinal).Count());
    }
}
