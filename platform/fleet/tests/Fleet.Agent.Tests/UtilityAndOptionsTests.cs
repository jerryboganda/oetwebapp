using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

/// <summary>Small contracts: options, fail messages, backoff, versions, JSON shapes (RW-014, RW-151, RW-154).</summary>
public sealed class UtilityAndOptionsTests
{
    private static Func<string, string?> Env(Dictionary<string, string> values) => name => values.TryGetValue(name, out var value) ? value : null;

    private static Dictionary<string, string> ValidEnv() => new()
    {
        ["OET_API_BASE"] = "https://api.oetwithdrhesham.co.uk",
        ["OET_NODE_ID"] = TestIds.NodeId,
        ["OET_NODE_TOKEN"] = TestIds.NodeToken(),
        ["OET_AGENT_IMAGE_DIGEST"] = TestIds.Digest,
    };

    [Fact]
    public void a_complete_environment_parses_with_the_documented_default_budgets()
    {
        var (options, problems) = AgentOptions.FromEnvironment(Env(ValidEnv()));

        Assert.Empty(problems);
        Assert.NotNull(options);
        Assert.Equal(new Uri("https://api.oetwithdrhesham.co.uk"), options!.ApiBase);
        Assert.Equal((3000L, 5120L, 3072L), (options.Budgets.CpuMilli, options.Budgets.MemMiB, options.Budgets.TmpMiB));
        Assert.Equal(LogLevelDefault, options.LogLevel);
    }

    private const Microsoft.Extensions.Logging.LogLevel LogLevelDefault = Microsoft.Extensions.Logging.LogLevel.Information;

    [Fact]
    public void explicit_budgets_and_log_level_are_honoured()
    {
        var env = ValidEnv();
        env["OET_BUDGET_CPU_MILLI"] = "1500";
        env["OET_BUDGET_MEM_MIB"] = "2048";
        env["OET_BUDGET_TMP_MIB"] = "1024";
        env["OET_LOG_LEVEL"] = "warning";

        var (options, problems) = AgentOptions.FromEnvironment(Env(env));

        Assert.Empty(problems);
        Assert.Equal((1500L, 2048L, 1024L), (options!.Budgets.CpuMilli, options.Budgets.MemMiB, options.Budgets.TmpMiB));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, options.LogLevel);
    }

    [Theory]
    [InlineData("OET_API_BASE", "http://api.example.test")]
    [InlineData("OET_API_BASE", "https://user:pw@api.example.test")]
    [InlineData("OET_API_BASE", "https://api.example.test/some/path")]
    [InlineData("OET_API_BASE", "not a url")]
    [InlineData("OET_NODE_ID", "rw_short")]
    [InlineData("OET_NODE_TOKEN", "orw1_nope")]
    [InlineData("OET_AGENT_IMAGE_DIGEST", "sha256:abc")]
    [InlineData("OET_BUDGET_MEM_MIB", "lots")]
    [InlineData("OET_LOG_LEVEL", "shouty")]
    public void malformed_configuration_is_rejected_without_echoing_any_value(string name, string value)
    {
        var env = ValidEnv();
        env[name] = value;

        var (options, problems) = AgentOptions.FromEnvironment(Env(env));

        Assert.Null(options);
        Assert.NotEmpty(problems);
        Assert.All(problems, problem =>
        {
            Assert.DoesNotContain(value, problem);
            Assert.DoesNotContain(TestIds.NodeToken(), problem);
        });
    }

    [Fact]
    public void a_tmp_budget_above_the_memory_budget_is_rejected_because_tmpfs_is_ram()
    {
        var env = ValidEnv();
        env["OET_BUDGET_MEM_MIB"] = "1024";
        env["OET_BUDGET_TMP_MIB"] = "2048";

        Assert.Contains(AgentOptions.FromEnvironment(Env(env)).Problems, p => p.Contains("must not exceed"));
    }

    [Fact]
    public void options_never_print_the_token()
    {
        var (options, _) = AgentOptions.FromEnvironment(Env(ValidEnv()));

        Assert.DoesNotContain(TestIds.NodeToken(), options!.ToString());
    }

    [Fact]
    public void RW154_fail_messages_are_sanitised_to_the_allowed_grammar()
    {
        var dirty = "failed at /scratch/rj_x-1/in.pdf: café \"quoted\" <b>\n";

        var clean = FailMessage.Sanitize(dirty);

        Assert.Matches(Wire.FailMessagePattern, clean);
        Assert.DoesNotContain("/", clean);
        Assert.Equal("", FailMessage.Sanitize(null));
        Assert.Equal(200, FailMessage.Sanitize(new string('a', 500)).Length);
    }

    [Fact]
    public void backoff_follows_1_2_4_8_16_30_seconds_with_quarter_jitter()
    {
        var expected = new[] { 1, 2, 4, 8, 16, 30, 30, 30 };
        for (var attempt = 0; attempt < expected.Length; attempt++)
        {
            for (var sample = 0; sample < 50; sample++)
            {
                var seconds = Backoff.Compute(attempt).TotalSeconds;
                Assert.InRange(seconds, expected[attempt] * 0.75, expected[attempt] * 1.25);
            }
        }

        Assert.InRange(Backoff.RetryAfterWithJitter(TimeSpan.FromSeconds(10)).TotalSeconds, 10.0, 12.0);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.0", "1.10.0", true)]
    [InlineData("2.0.0", "1.9.9", false)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    [InlineData("garbage", "1.0.0", false)]
    [InlineData("1.0.0", null, false)]
    public void minimum_version_comparison_is_numeric_and_never_blocks_on_unparseable_input(string actual, string? minimum, bool below)
    {
        Assert.Equal(below, SemVer.IsBelow(actual, minimum));
    }

    [Fact]
    public void hashes_compare_in_constant_time_and_ignore_case()
    {
        Assert.True(Hashing.HexEquals("ABCDEF", "abcdef"));
        Assert.False(Hashing.HexEquals("abcdef", "abcdee"));
        Assert.False(Hashing.HexEquals("abc", "abcd"));
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Hashing.Sha256Hex(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void timestamps_are_rfc3339_utc_with_milliseconds()
    {
        Assert.Equal("2026-10-05T12:00:00.123Z", TimeFormat.Rfc3339(new DateTimeOffset(2026, 10, 5, 14, 0, 0, 123, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void the_claim_request_serialises_exactly_the_documented_members()
    {
        var request = new ClaimRequest
        {
            ClaimId = "5b0f6c52-8a61-4c0b-9d2e-3a7f4f1e8c10",
            InstanceId = "c2f0a6a4-3a55-4d6e-b7f1-0a9b8c7d6e5f",
            AppliedRevision = 17,
            Kinds = [new KindOffer { Kind = JobKinds.PdfExtract, SchemaVersions = [1], EngineVersion = "pdfpig:1.7.0-custom-5/oet-text:1" }],
            Capacity = new ClaimCapacity { CpuBudgetFreeMilli = 2000, MemBudgetFreeMiB = 3500, TmpFreeMiB = 2900, HeavySlotsFree = 1, EffectiveConcurrency = 2 },
            Agent = new AgentInfo { Version = "1.0.0", ImageDigest = TestIds.Digest, Protocol = 1, ProtocolsSupported = [1] },
        };

        using var json = JsonDocument.Parse(ProtocolJson.ToUtf8(request));
        var root = json.RootElement;

        Assert.Equal(new[] { "claimId", "instanceId", "appliedRevision", "kinds", "capacity", "agent" }, root.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "cpuBudgetFreeMilli", "memBudgetFreeMiB", "tmpFreeMiB", "heavySlotsFree", "effectiveConcurrency" },
            root.GetProperty("capacity").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "kind", "schemaVersions", "engineVersion" }, root.GetProperty("kinds")[0].EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "version", "imageDigest", "protocol", "protocolsSupported" }, root.GetProperty("agent").EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void the_node_heartbeat_serialises_the_documented_capacity_and_load_members()
    {
        using var h = new Harness();
        h.MakeReady();

        using var json = JsonDocument.Parse(ProtocolJson.ToUtf8(h.Node.BuildRequest()));
        var root = json.RootElement;

        Assert.Equal(
            new[] { "cpuCoresTotal", "cpuBudgetMilli", "cpuBudgetFreeMilli", "memTotalMiB", "memAvailableMiB", "memBudgetMiB", "memBudgetFreeMiB", "tmpBudgetMiB", "tmpFreeMiB", "diskFreeMiB", "heavySlotsTotal", "heavySlotsFree", "configuredConcurrency", "effectiveConcurrency" },
            root.GetProperty("capacity").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "cpuPct", "cpuPct15s", "memFreePct", "load1", "pressure" }, root.GetProperty("load").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("leases").ValueKind);
        Assert.True(root.GetProperty("agent").TryGetProperty("startedAt", out _));
        Assert.True(root.GetProperty("agent").TryGetProperty("clockSkewMs", out _));
    }

    [Fact]
    public void RW151_the_agent_project_carries_no_database_storage_or_provider_dependencies()
    {
        var project = File.ReadAllText(TestPaths.Combine("platform", "fleet", "src", "Fleet.Agent", "Fleet.Agent.csproj"));
        var packages = System.Text.RegularExpressions.Regex.Matches(project, "<PackageReference\\s+Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(new[] { "Microsoft.Extensions.Hosting", "UglyToad.PdfPig" }, packages.OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("<ProjectReference", project);
    }

    [Fact]
    public void RW151_no_agent_source_names_a_credential_store_the_docker_socket_or_a_provider()
    {
        var root = TestPaths.Combine("platform", "fleet", "src", "Fleet.Agent");
        var forbidden = new[] { "docker.sock", "Npgsql", "EntityFramework", "AWSSDK", "OPENAI", "ANTHROPIC", "ConnectionString", "tesseract" };

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (var word in forbidden)
            {
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase), Path.GetFileName(file) + " mentions " + word);
            }
        }
    }

    [Fact]
    public void the_agent_dockerfile_is_non_root_read_only_friendly_and_secret_free()
    {
        var dockerfile = File.ReadAllText(TestPaths.Combine("platform", "fleet", "src", "Fleet.Agent", "Dockerfile"));

        Assert.Contains("USER 10001:10001", dockerfile);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Fleet.Agent.dll\"]", dockerfile);
        Assert.Contains("HEALTHCHECK", dockerfile);
        Assert.Contains("ffmpeg", dockerfile);
        Assert.DoesNotContain("tesseract", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker.sock", dockerfile);
        Assert.DoesNotMatch("(?mi)^\\s*(ENV|ARG)\\s+.*(TOKEN|SECRET|PASSWORD|KEY)", dockerfile);
        Assert.DoesNotMatch("(?m)^\\s*(RUN|CMD)\\s+.*:latest", dockerfile);
    }

    [Fact]
    public void the_helper_never_sends_client_version_headers_anywhere_in_the_agent_source()
    {
        var root = TestPaths.Combine("platform", "fleet", "src", "Fleet.Agent");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("X-Client-Platform", text);
            Assert.DoesNotContain("X-App-Version", text);
        }
    }

    [Fact]
    public void ParamReader_applies_defaults_ranges_and_types()
    {
        var parameters = TestJobs.Json("{\"n\":5,\"s\":\"x\",\"b\":true,\"big\":99999999999}");

        Assert.Equal(5, ParamReader.Int(parameters, "n", 1, 0, 10));
        Assert.Equal(7, ParamReader.Int(parameters, "missing", 7, 0, 10));
        Assert.Equal("x", ParamReader.String(parameters, "s", "d"));
        Assert.True(ParamReader.Bool(parameters, "b", false));
        Assert.Throws<JobFailureException>(() => ParamReader.Int(parameters, "n", 1, 6, 10));
        Assert.Throws<JobFailureException>(() => ParamReader.Int(parameters, "s", 1, 0, 10));
        Assert.Throws<JobFailureException>(() => ParamReader.Int(parameters, "big", 1, 0, 10));
        Assert.Throws<JobFailureException>(() => ParamReader.Bool(parameters, "n", false));
        Assert.Equal(3, ParamReader.Int(default, "n", 3, 0, 10));
    }
}
