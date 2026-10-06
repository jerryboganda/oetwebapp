using System.Text.Json;
using System.Text.RegularExpressions;
using Fleet.Core.Ssh;
using Fleet.Manager.Tests.Infrastructure;

namespace Fleet.Manager.Tests.Provisioning;

/// <summary>
/// The helper side of OET-RWP/1 section 7.4: the SSH forced command <c>oet-fleet-gate</c>, the restricted <c>oet-fleet-ctl</c> and the sudoers
/// drop-in. The verb tables must be identical in three places (C#, gate, ctl); the gate and ctl are exercised for real with python3
/// (those facts are reported as skipped, never as a silent pass, on a machine without python3). Only REFUSAL paths run: nothing here can
/// change the machine, whoever runs the tests.
/// </summary>
public sealed partial class FleetCtlGateTests : IDisposable
{
    private static readonly string Gate = RepoPaths.AnsibleFile("files", "oet-fleet-gate");
    private static readonly string Ctl = RepoPaths.AnsibleFile("files", "oet-fleet-ctl");
    private static readonly string Sudoers = RepoPaths.AnsibleFile("files", "oet-fleet-sudoers");

    private readonly string _scratch = Directory.CreateTempSubdirectory("fleet-gate-tests-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    [GeneratedRegex("^VERBS = \\{\\n(?<body>.*?)\\n\\}", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex VerbBlock();

    [GeneratedRegex("^\\s+\"(?<verb>[a-z-]+)\":\\s+r\"(?<pattern>.*)\",$", RegexOptions.Multiline)]
    private static partial Regex VerbLine();

    private static Dictionary<string, string> ReadVerbTable(string path)
    {
        var text = File.ReadAllText(path);
        var block = VerbBlock().Match(text);
        Assert.True(block.Success, path + " has no VERBS table");
        return VerbLine().Matches(block.Groups["body"].Value).ToDictionary(m => m.Groups["verb"].Value, m => m.Groups["pattern"].Value, StringComparer.Ordinal);
    }

    // ---- three tables, one vocabulary ------------------------------------------------------

    [Fact]
    public void The_gate_the_ctl_and_the_manager_agree_on_the_exact_vocabulary_and_patterns()
    {
        var expected = FleetCtlVerbs.All.ToDictionary(spec => spec.Verb, spec => spec.ArgsPattern, StringComparer.Ordinal);

        var gate = ReadVerbTable(Gate);
        var ctl = ReadVerbTable(Ctl);

        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), gate.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), ctl.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        foreach (var (verb, pattern) in expected)
        {
            Assert.True(gate[verb] == pattern, "gate pattern for '" + verb + "' differs: " + gate[verb] + " vs " + pattern);
            Assert.True(ctl[verb] == pattern, "ctl pattern for '" + verb + "' differs: " + ctl[verb] + " vs " + pattern);
        }
    }

    [Fact]
    public void The_ctl_agent_environment_rules_are_the_ones_the_manager_renders()
    {
        var text = File.ReadAllText(Ctl);

        var keys = Regex.Match(text, "ENV_KEYS = \\((?<body>.*?)\\)", RegexOptions.Singleline).Groups["body"].Value;
        var ctlKeys = Regex.Matches(keys, "\"(OET_[A-Z_]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(AgentEnv.AllowedKeys.ToArray(), ctlKeys);

        var value = Regex.Match(text, "ENV_VALUE = re\\.compile\\(r\"(?<pattern>[^\"]+)\"\\)").Groups["pattern"].Value;
        Assert.Equal(AgentEnv.ValuePattern, value);

        Assert.Contains("AGENT_REPO = \"" + FleetCtlVerbs.AgentRepository + "\"", text);
        Assert.Contains("CONTAINER = \"oet-fleet-agent\"", text);
    }

    [Fact]
    public void The_sudoers_drop_in_allows_exactly_one_program_to_exactly_one_user()
    {
        var rules = File.ReadAllLines(Sudoers).Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')).ToArray();

        var rule = Assert.Single(rules);
        Assert.Equal("oetfleet ALL=(root) NOPASSWD: /usr/local/sbin/oet-fleet-ctl", rule);
        Assert.DoesNotContain("*", rule);
        Assert.DoesNotContain(",", rule);
        Assert.Contains("CTL = \"/usr/local/sbin/oet-fleet-ctl\"", File.ReadAllText(Gate));
    }

    [Fact]
    public void The_ctl_never_uses_a_shell_and_never_installs_or_builds_anything()
    {
        var text = File.ReadAllText(Ctl);

        Assert.DoesNotContain("shell=True", text);
        Assert.DoesNotContain("os.system", text);
        Assert.DoesNotContain("os.popen", text);
        Assert.DoesNotContain("docker\", \"build", text);
        Assert.DoesNotContain("apt-get", text);
        Assert.DoesNotContain("pip install", text);

        // The uninstall verb touches only fleet-owned things: no volume, no network, no system prune, no other container.
        var uninstall = text[text.IndexOf("def verb_uninstall", StringComparison.Ordinal)..text.IndexOf("HANDLERS = {", StringComparison.Ordinal)];
        Assert.DoesNotContain("volume", uninstall);
        Assert.DoesNotContain("system", uninstall.Replace("systemctl", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("network", uninstall);
        Assert.DoesNotContain("nft", uninstall);
        Assert.DoesNotContain("apt", uninstall);
    }

    // ---- the gate, for real ----------------------------------------------------------------

    private (int Exit, string Stdout, string Stderr) RunGate(string originalCommand) =>
        Python.Run(Gate, new[] { "--print-argv" }, new Dictionary<string, string> { ["SSH_ORIGINAL_COMMAND"] = originalCommand });

    public static IEnumerable<object[]> AcceptedCommands()
    {
        var digest = "sha256:" + new string('a', 64);
        yield return new object[] { "oet-fleet-ctl status" };
        yield return new object[] { "oet-fleet-ctl harden-check" };
        yield return new object[] { "oet-fleet-ctl login --registry ghcr.io" };
        yield return new object[] { "oet-fleet-ctl pull " + FleetCtlVerbs.PullReference(digest) };
        yield return new object[] { "oet-fleet-ctl verify " + digest };
        yield return new object[] { "oet-fleet-ctl verify " + digest + " sha256:" + new string('b', 64) };
        yield return new object[] { "oet-fleet-ctl logout" };
        yield return new object[] { "oet-fleet-ctl put-env" };
        yield return new object[] { "oet-fleet-ctl run " + digest };
        yield return new object[] { "oet-fleet-ctl stop" };
        yield return new object[] { "oet-fleet-ctl stop --grace 120" };
        yield return new object[] { "oet-fleet-ctl restart" };
        yield return new object[] { "oet-fleet-ctl logs --tail 200" };
        yield return new object[] { "oet-fleet-ctl prune" };
        yield return new object[] { "oet-fleet-ctl wipe-scratch" };
        yield return new object[] { "oet-fleet-ctl unit-sync" };
        yield return new object[] { "oet-fleet-ctl uninstall" };
    }

    [PythonTheory]
    [MemberData(nameof(AcceptedCommands))]
    public void The_gate_turns_an_allowed_command_into_exactly_one_sudo_call_of_the_ctl(string command)
    {
        var (exit, stdout, _) = RunGate(command);

        Assert.Equal(0, exit);
        var argv = JsonSerializer.Deserialize<string[]>(stdout)!;
        Assert.Equal(new[] { "sudo", "-n", "/usr/local/sbin/oet-fleet-ctl" }, argv.Take(3).ToArray());
        Assert.Equal(command.Split(' ').Skip(1).ToArray(), argv.Skip(3).ToArray());
    }

    public static IEnumerable<object[]> RefusedCommands()
    {
        var digest = "sha256:" + new string('a', 64);
        yield return new object[] { string.Empty };
        yield return new object[] { "oet-fleet-ctl" };
        yield return new object[] { "id" };
        yield return new object[] { "rm -rf /" };
        yield return new object[] { "/usr/local/sbin/oet-fleet-ctl status" };
        yield return new object[] { "sudo oet-fleet-ctl status" };
        yield return new object[] { "bash -c oet-fleet-ctl" };
        yield return new object[] { "oet-fleet-ctl STATUS" };
        yield return new object[] { "oet-fleet-ctl shell" };
        yield return new object[] { "oet-fleet-ctl status; id" };
        yield return new object[] { "oet-fleet-ctl status && id" };
        yield return new object[] { "oet-fleet-ctl status | cat" };
        yield return new object[] { "oet-fleet-ctl status $(id)" };
        yield return new object[] { "oet-fleet-ctl status `id`" };
        yield return new object[] { "oet-fleet-ctl status > /tmp/x" };
        yield return new object[] { "oet-fleet-ctl  status" };
        yield return new object[] { "oet-fleet-ctl status " };
        yield return new object[] { " oet-fleet-ctl status" };
        yield return new object[] { "oet-fleet-ctl status\tstatus" };
        yield return new object[] { "oet-fleet-ctl status\nid" };
        yield return new object[] { "oet-fleet-ctl status extra" };
        yield return new object[] { "oet-fleet-ctl login --registry evil.example" };
        yield return new object[] { "oet-fleet-ctl pull ghcr.io/someone-else/agent@" + digest };
        yield return new object[] { "oet-fleet-ctl pull " + FleetCtlVerbs.AgentRepository + ":latest" };
        yield return new object[] { "oet-fleet-ctl pull " + FleetCtlVerbs.AgentRepository + "@sha256:abc" };
        yield return new object[] { "oet-fleet-ctl run sha256:" + new string('A', 64) };
        yield return new object[] { "oet-fleet-ctl run " + digest + " --privileged" };
        yield return new object[] { "oet-fleet-ctl verify " + digest + " " + digest + " " + digest };
        yield return new object[] { "oet-fleet-ctl stop --grace 121" };
        yield return new object[] { "oet-fleet-ctl stop --grace 0" };
        yield return new object[] { "oet-fleet-ctl logs --tail 201" };
        yield return new object[] { "oet-fleet-ctl logs --tail all" };
        yield return new object[] { "oet-fleet-ctl " + new string('a', 600) };
    }

    [PythonTheory]
    [MemberData(nameof(RefusedCommands))]
    public void The_gate_refuses_everything_else_with_status_126_and_prints_nothing_to_run(string command)
    {
        var (exit, stdout, stderr) = RunGate(command);

        Assert.Equal(126, exit);
        Assert.Equal(string.Empty, stdout.Trim());
        Assert.StartsWith("oet-fleet-gate:", stderr);
    }

    [PythonFact]
    public void The_gate_and_the_manager_builder_accept_and_refuse_exactly_the_same_commands()
    {
        var accepted = AcceptedCommands().Select(row => (string)row[0]);
        var refused = RefusedCommands().Select(row => (string)row[0]).Where(command => command.Length is > 0 and <= 400);

        foreach (var command in accepted.Concat(refused))
        {
            var parts = command.Split(' ');
            var managerAccepts = parts.Length >= 2
                && parts[0] == "oet-fleet-ctl"
                && parts.Skip(2).All(part => part.Length > 0 && !part.Any(char.IsWhiteSpace))
                && FleetCtlVerbs.TryBuild(parts[1], parts.Skip(2).ToArray(), out _, out _);
            var gateAccepts = RunGate(command).Exit == 0;
            Assert.True(managerAccepts == gateAccepts, "gate and manager disagree about: " + command);
        }
    }

    [PythonFact]
    public void Without_the_test_only_flag_a_refused_command_is_refused_just_the_same_and_nothing_is_executed()
    {
        // A remote client can only set SSH_ORIGINAL_COMMAND; it can never pass program arguments to the gate itself.
        var (exit, stdout, stderr) = Python.Run(Gate, Array.Empty<string>(), new Dictionary<string, string> { ["SSH_ORIGINAL_COMMAND"] = "id" });

        Assert.Equal(126, exit);
        Assert.Equal(string.Empty, stdout.Trim());
        Assert.Contains("only oet-fleet-ctl", stderr);
    }

    // ---- the ctl, refusal paths only -------------------------------------------------------

    private (int Exit, JsonElement Json) RunCtl(string[] arguments, string? stdin = null)
    {
        var (exit, stdout, _) = Python.Run(Ctl, arguments, null, stdin);
        using var document = JsonDocument.Parse(stdout);
        return (exit, document.RootElement.Clone());
    }

    [PythonFact]
    public void The_ctl_refuses_an_unknown_verb_and_arguments_outside_the_verb_pattern_with_status_2()
    {
        var noVerb = RunCtl(Array.Empty<string>());
        Assert.Equal(2, noVerb.Exit);
        Assert.False(noVerb.Json.GetProperty("ok").GetBoolean());

        var unknown = RunCtl(new[] { "rm" });
        Assert.Equal(2, unknown.Exit);
        Assert.Equal("unknown verb", unknown.Json.GetProperty("error").GetString());

        var extra = RunCtl(new[] { "status", "extra" });
        Assert.Equal(2, extra.Exit);
        Assert.Equal("arguments rejected", extra.Json.GetProperty("error").GetString());

        var whitespace = RunCtl(new[] { "run", "sha256:" + new string('a', 64) + " --privileged" });
        Assert.Equal(2, whitespace.Exit);
        Assert.Equal("malformed argument", whitespace.Json.GetProperty("error").GetString());

        var emptyArgument = RunCtl(new[] { "run", string.Empty });
        Assert.Equal(2, emptyArgument.Exit);
    }

    [PythonFact]
    public void The_ctl_refuses_a_malformed_registry_credential_before_it_touches_docker()
    {
        foreach (var stdin in new[] { string.Empty, "user-only\n", "bad user!\nghp_0123456789abcdef\n", "user\nshort\n", "user\nnot a token!\n", "user\n" + new string('a', 400) + "\n" })
        {
            var (exit, json) = RunCtl(new[] { "login", "--registry", "ghcr.io" }, stdin);

            Assert.Equal(2, exit);
            Assert.False(json.GetProperty("ok").GetBoolean());
        }
    }

    [PythonFact]
    public void The_ctl_refuses_an_unsafe_agent_environment_before_it_writes_anything()
    {
        var refusals = new (string Stdin, string Error)[]
        {
            ("OET_NODE_TOKEN=abc def\n", "env value not allowed for OET_NODE_TOKEN"),
            ("PATH=/usr/bin\n", "env key not allowed: PATH"),
            ("OET_API_BASE=https://a.example\nOET_API_BASE=https://b.example\n", "duplicate env key"),
            ("not a pair\n", "malformed env line"),
            ("OET_LOG_LEVEL=$(id)\n", "env value not allowed for OET_LOG_LEVEL"),
            ("OET_API_BASE=" + new string('a', 201) + "\n", "env value not allowed for OET_API_BASE"),
            (new string('x', 5000), "stdin is too large"),
        };

        foreach (var (stdin, error) in refusals)
        {
            var (exit, json) = RunCtl(new[] { "put-env" }, stdin);

            Assert.Equal(2, exit);
            Assert.Equal(error, json.GetProperty("error").GetString());
        }
    }

    // ---- the ctl as a module: single verbs with docker replaced ----------------------------

    /// <summary>
    /// Runs a python snippet with the ctl LOADED AS A MODULE (never as a program, nothing is executed on the machine): <c>ctl</c> is the module and
    /// <c>call(verb, *args)</c> runs one verb and returns (exit code or the Failed/Refused text, what it printed). The snippet prints one JSON line.
    /// </summary>
    private (int Exit, JsonElement Json) RunHarness(params string[] snippet)
    {
        var helper = Path.Combine(_scratch, "harness-" + Guid.NewGuid().ToString("N") + ".py");
        File.WriteAllText(
            helper,
            string.Join(
                '\n',
                new[]
                {
                    "import contextlib, importlib.machinery, importlib.util, io, json, os, sys, tempfile",
                    "sys.dont_write_bytecode = True",
                    "loader = importlib.machinery.SourceFileLoader('oet_fleet_ctl', sys.argv[1])",
                    "spec = importlib.util.spec_from_loader('oet_fleet_ctl', loader)",
                    "ctl = importlib.util.module_from_spec(spec)",
                    "loader.exec_module(ctl)",
                    "def call(verb, *args):",
                    "    buffer = io.StringIO()",
                    "    code = None",
                    "    try:",
                    "        with contextlib.redirect_stdout(buffer):",
                    "            verb(*args)",
                    "    except SystemExit as stop:",
                    "        code = stop.code",
                    "    except (ctl.Failed, ctl.Refused) as error:",
                    "        code = 'raised: ' + str(error)",
                    "    return code, buffer.getvalue()",
                }.Concat(snippet).Append(string.Empty)));

        var (exit, stdout, stderr) = Python.Run(helper, new[] { Ctl }, new Dictionary<string, string> { ["PYTHONDONTWRITEBYTECODE"] = "1" });
        Assert.True(exit == 0, "the harness failed: " + stderr);
        using var document = JsonDocument.Parse(stdout);
        return (exit, document.RootElement.Clone());
    }

    [PythonFact]
    public void The_ctl_reports_a_missing_program_as_a_failed_command_not_as_a_crash()
    {
        var (_, json) = RunHarness(
            "missing = os.path.join(tempfile.gettempdir(), 'oet-fleet-no-such-dir', 'no-such-program')",
            "quiet = ctl.run([missing], check=False)",
            "try:",
            "    ctl.run([missing])",
            "    raised = None",
            "except ctl.Failed as error:",
            "    raised = str(error)",
            "print(json.dumps({'code': quiet[0], 'stdout': quiet[1], 'raised': raised}))");

        Assert.Equal(127, json.GetProperty("code").GetInt32());
        Assert.Equal(string.Empty, json.GetProperty("stdout").GetString());
        Assert.Equal("no-such-program: not found", json.GetProperty("raised").GetString());
    }

    [PythonFact]
    public void The_ctl_status_works_on_a_helper_that_has_no_docker_yet()
    {
        // S3 proves the restricted login with `status` BEFORE S4 installs Docker. A missing docker binary used to raise FileNotFoundError, which the
        // ctl turned into {"ok":false,"error":"FileNotFoundError"} with exit 1, so the very first enrollment of every clean helper failed at S3.
        var (_, json) = RunHarness(
            "ctl.DOCKER = os.path.join(tempfile.gettempdir(), 'oet-fleet-no-such-dir', 'docker')",
            "code, out = call(ctl.verb_status, [])",
            "print(json.dumps({'code': code, 'status': json.loads(out)}))");

        Assert.Equal(0, json.GetProperty("code").GetInt32());
        var status = json.GetProperty("status");
        Assert.Equal("oet-fleet-ctl.status/1", status.GetProperty("schema").GetString());
        Assert.False(status.GetProperty("docker").GetProperty("running").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("docker").GetProperty("version").ValueKind);
        Assert.False(status.GetProperty("agent").GetProperty("present").GetBoolean());
        Assert.True(status.GetProperty("host").GetProperty("cpuCores").GetInt32() >= 1);
    }

    /// <summary>Env file lines as python string-literal text (the two characters backslash and n stand for a newline inside the python snippet).</summary>
    private static readonly string AgentEnvLines =
        "OET_API_BASE=https://api.example.test\\n"
        + "OET_NODE_ID=rw_00000000000000000000000001\\n"
        + "OET_NODE_TOKEN=orw1_0123456789abcdef_tokenvalue\\n"
        + "OET_AGENT_IMAGE_DIGEST=sha256:" + new string('a', 64) + "\\n";

    [PythonFact]
    public void The_ctl_restart_recreates_the_container_from_the_env_file_instead_of_docker_restart()
    {
        // `docker restart` keeps the environment the container was CREATED with, so a rotated node token would never reach the agent.
        var (_, json) = RunHarness(
            "scratch = tempfile.mkdtemp()",
            "ctl.ENV_FILE = os.path.join(scratch, 'agent.env')",
            "with open(ctl.ENV_FILE, 'w') as handle:",
            "    handle.write('" + AgentEnvLines + "OET_BUDGET_CPU_MILLI=64000\\nOET_BUDGET_MEM_MIB=262144\\nOET_BUDGET_TMP_MIB=3072\\n')",
            "calls = []",
            "def fake_docker(*args, **kwargs):",
            "    calls.append(list(args))",
            "    return 0, 'c0ffee\\n', ''",
            "ctl.docker = fake_docker",
            "code, out = call(ctl.verb_restart, [])",
            "print(json.dumps({'code': code, 'out': json.loads(out), 'calls': calls, 'envFile': ctl.ENV_FILE, 'cores': os.cpu_count() or 1, 'memTotalMiB': ctl.meminfo('MemTotal') // 1024}))");

        Assert.Equal(0, json.GetProperty("code").GetInt32());
        Assert.True(json.GetProperty("out").GetProperty("ok").GetBoolean());
        Assert.Equal("c0ffee", json.GetProperty("out").GetProperty("containerId").GetString());

        var calls = json.GetProperty("calls").EnumerateArray().Select(call => call.EnumerateArray().Select(part => part.GetString()!).ToList()).ToList();
        Assert.Equal(new[] { "rm", "-f", "oet-fleet-agent" }, calls[0]);
        Assert.Equal("run", calls[1][0]);
        Assert.DoesNotContain(calls, call => call[0] == "restart");
        var run = calls[1];
        Assert.Equal(json.GetProperty("envFile").GetString(), run[run.IndexOf("--env-file") + 1]);
        Assert.Equal(FleetCtlVerbs.AgentRepository + "@sha256:" + new string('a', 64), run[^1]);
        Assert.Equal(2, calls.Count);

        // The stored budgets (64 cores, 256 GiB) are clamped to what this machine has: Docker refuses --cpus above the core count.
        var cpus = double.Parse(run[run.IndexOf("--cpus") + 1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(cpus, 0.5, json.GetProperty("cores").GetInt32());
        var memory = int.Parse(run[run.IndexOf("--memory") + 1].TrimEnd('m'), System.Globalization.CultureInfo.InvariantCulture);
        var memTotal = json.GetProperty("memTotalMiB").GetInt32();
        Assert.InRange(memory, 512, memTotal > 0 ? Math.Max(512, memTotal) : 262144);
    }

    [PythonFact]
    public void The_ctl_restart_refuses_to_guess_when_there_is_no_env_file_or_no_digest_in_it()
    {
        var (_, json) = RunHarness(
            "scratch = tempfile.mkdtemp()",
            "ctl.ENV_FILE = os.path.join(scratch, 'agent.env')",
            "calls = []",
            "def fake_docker(*args, **kwargs):",
            "    calls.append(list(args))",
            "    return 0, '', ''",
            "ctl.docker = fake_docker",
            "missing = call(ctl.verb_restart, [])",
            "with open(ctl.ENV_FILE, 'w') as handle:",
            "    handle.write('OET_API_BASE=https://api.example.test\\nOET_NODE_ID=rw_1\\nOET_NODE_TOKEN=orw1_x\\n')",
            "no_digest = call(ctl.verb_restart, [])",
            "print(json.dumps({'missing': missing[0], 'noDigest': no_digest[0], 'calls': calls}))");

        Assert.Equal("raised: no env file; put-env first", json.GetProperty("missing").GetString());
        Assert.Equal("raised: the env file names no agent image digest", json.GetProperty("noDigest").GetString());
        Assert.Equal(0, json.GetProperty("calls").GetArrayLength());
    }

    [Fact]
    public void The_ctl_uninstall_closes_the_managers_way_in_last_so_a_retry_can_tell_that_it_ran()
    {
        var text = File.ReadAllText(Ctl);
        var uninstall = text[text.IndexOf("def verb_uninstall", StringComparison.Ordinal)..text.IndexOf("HANDLERS = {", StringComparison.Ordinal)];

        var removeConfig = uninstall.IndexOf("shutil.rmtree(CONFIG_DIR", StringComparison.Ordinal);
        var removeKey = uninstall.IndexOf("/home/oetfleet/.ssh/authorized_keys", StringComparison.Ordinal);
        var lockAccount = uninstall.IndexOf("usermod", StringComparison.Ordinal);
        Assert.True(removeConfig >= 0 && removeKey > removeConfig, "everything fleet-owned is removed before the key");
        Assert.True(lockAccount > removeKey, "the account lock is the very last act, after the key and the gate are gone");
        Assert.True(uninstall.IndexOf("emit(", StringComparison.Ordinal) > lockAccount);
    }

    [PythonFact]
    public void The_ctl_env_parser_accepts_what_the_manager_renders_and_refuses_what_the_manager_refuses()
    {
        var helper = Path.Combine(_scratch, "parse_env.py");
        File.WriteAllText(
            helper,
            string.Join(
                '\n',
                "import importlib.machinery, importlib.util, json, sys",
                "sys.dont_write_bytecode = True",
                "loader = importlib.machinery.SourceFileLoader('oet_fleet_ctl', sys.argv[1])",
                "spec = importlib.util.spec_from_loader('oet_fleet_ctl', loader)",
                "module = importlib.util.module_from_spec(spec)",
                "loader.exec_module(module)",
                "try:",
                "    print(json.dumps({'ok': True, 'values': module.parse_env_text(sys.stdin.read())}))",
                "except module.Refused as error:",
                "    print(json.dumps({'ok': False, 'error': str(error)}))",
                string.Empty));

        var rendered = AgentEnv.Render(new Dictionary<string, string>
        {
            ["OET_API_BASE"] = "https://api.oetwithdrhesham.co.uk",
            ["OET_NODE_ID"] = "rw_00000000000000000000000001",
            ["OET_NODE_TOKEN"] = "orw1_0123456789abcdef_" + new string('A', 43),
            ["OET_AGENT_IMAGE_DIGEST"] = "sha256:" + new string('a', 64),
            ["OET_BUDGET_CPU_MILLI"] = "3000",
            ["OET_BUDGET_MEM_MIB"] = "5120",
            ["OET_BUDGET_TMP_MIB"] = "3072",
            ["OET_LOG_LEVEL"] = "Information",
        });
        var (exit, stdout, _) = Python.Run(helper, new[] { Ctl }, null, rendered);
        using var accepted = JsonDocument.Parse(stdout);
        Assert.Equal(0, exit);
        Assert.True(accepted.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(8, accepted.RootElement.GetProperty("values").EnumerateObject().Count());

        foreach (var unsafeValue in new[] { "a b", "a;b", "$(id)", "`id`", "\"q\"", "back\\slash" })
        {
            Assert.Throws<ArgumentException>(() => AgentEnv.Render(new Dictionary<string, string> { ["OET_API_BASE"] = unsafeValue }));
            var (_, refusedOut, _) = Python.Run(helper, new[] { Ctl }, null, "OET_API_BASE=" + unsafeValue + "\n");
            using var refused = JsonDocument.Parse(refusedOut);
            Assert.False(refused.RootElement.GetProperty("ok").GetBoolean());
        }
    }
}

/// <summary>A theory that is SKIPPED (reported as skipped, never as a silent pass) when python3 is not installed.</summary>
public sealed class PythonTheoryAttribute : TheoryAttribute
{
    public PythonTheoryAttribute()
    {
        if (Python.Find() is null)
        {
            Skip = "python3 is not installed on this machine";
        }
    }
}
