using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Fleet.Agent.Tests;

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Needs a Linux shell (the agent only ever runs on linux/amd64).";
    }
}

/// <summary>Runs only when FLEET_AGENT_DLL points at a PUBLISHED Fleet.Agent.dll (a manual tool: no workflow sets it).</summary>
public sealed class AgentDllFactAttribute : FactAttribute
{
    public const string Variable = "FLEET_AGENT_DLL";

    public AgentDllFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = "Set " + Variable + " to a published Fleet.Agent.dll to run the real child-process test.";
        }
    }
}

/// <summary>The real process wrappers: pipes, timeouts, kill-on-fault (protocol 6.3/6.4 procedure, H1/H2 isolation).</summary>
public sealed class ProcessTests
{
    private static ProcessSpec Sh(string script, Stream? stdin = null, Func<Stream, CancellationToken, Task>? consume = null, double timeoutSeconds = 20) => new()
    {
        FileName = "/bin/sh",
        Arguments = ["-c", script],
        Stdin = stdin,
        ConsumeStdout = consume,
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
    };

    [LinuxFact]
    public async Task stdout_is_streamed_to_the_consumer_and_the_exit_code_is_reported()
    {
        var output = new MemoryStream();

        var result = await new SystemProcessRunner().RunAsync(Sh("printf hello", consume: (s, t) => s.CopyToAsync(output, t)), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal("hello", Encoding.ASCII.GetString(output.ToArray()));
    }

    [LinuxFact]
    public async Task stdin_is_fed_and_closed_so_a_filter_can_finish()
    {
        var output = new MemoryStream();

        var result = await new SystemProcessRunner().RunAsync(
            Sh("cat", stdin: new MemoryStream(Encoding.ASCII.GetBytes("abc")), consume: (s, t) => s.CopyToAsync(output, t)), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("abc", Encoding.ASCII.GetString(output.ToArray()));
    }

    [LinuxFact]
    public async Task a_hard_timeout_kills_the_tool_instead_of_hanging()
    {
        var clock = Stopwatch.StartNew();

        var result = await new SystemProcessRunner().RunAsync(Sh("sleep 30", timeoutSeconds: 1), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "the kill did not land in time");
    }

    [LinuxFact]
    public async Task a_consumer_fault_stops_the_tool_at_once_even_when_it_would_write_forever()
    {
        var clock = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<JobFailureException>(() => new SystemProcessRunner().RunAsync(
            Sh("yes", consume: async (s, t) =>
            {
                var buffer = new byte[64];
                await s.ReadAsync(buffer, t);
                throw new JobFailureException(FailCodes.DurationExceeded, false, "limit");
            }, timeoutSeconds: 60), CancellationToken.None));

        Assert.Equal(FailCodes.DurationExceeded, failure.Code);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the tool was not killed when the consumer failed");
    }

    [LinuxFact]
    public async Task a_missing_binary_is_reported_as_not_started()
    {
        var result = await new SystemProcessRunner().RunAsync(new ProcessSpec { FileName = "/definitely/not/here", Arguments = [] }, CancellationToken.None);

        Assert.Equal(ProcessRunner.NotStarted, result.ExitCode);
    }

    [LinuxFact]
    public async Task stderr_is_captured_for_marker_checks_but_only_as_a_bounded_tail()
    {
        var result = await new SystemProcessRunner().RunAsync(Sh("echo \"Stream map matches no streams\" 1>&2; exit 3"), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("matches no streams", result.StderrTail);
    }

    [LinuxFact]
    public async Task cancellation_kills_the_tool_and_surfaces_as_cancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SystemProcessRunner().RunAsync(Sh("sleep 30"), cts.Token));
    }

    [Fact]
    public void the_child_is_started_through_the_dotnet_host_with_the_agent_assembly()
    {
        var hostedByDotnet = string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase);

        // An explicit agent assembly is always run through the muxer, whichever process drives the runner.
        var (file, prefix) = ProcessChildRunner.ResolveSelf("/app/Fleet.Agent.dll");
        Assert.Equal(new[] { "/app/Fleet.Agent.dll" }, prefix);
        Assert.False(string.IsNullOrEmpty(file));
        Assert.Equal(hostedByDotnet ? Environment.ProcessPath : "dotnet", file);

        // Without one, the process re-enters itself: through the muxer when hosted by it, as its own apphost otherwise.
        var (selfFile, selfPrefix) = ProcessChildRunner.ResolveSelf(null);
        Assert.False(string.IsNullOrEmpty(selfFile));
        if (hostedByDotnet) Assert.Single(selfPrefix);
        else Assert.Empty(selfPrefix);
    }

    [Fact]
    public void scrubbing_removes_every_oet_variable_whatever_its_case_and_keeps_the_rest()
    {
        var environment = new Dictionary<string, string?>
        {
            ["OET_NODE_TOKEN"] = "x",
            ["OET_API_BASE"] = "https://api.invalid",
            ["oet_budget_mem_mib"] = "1",
            ["DOTNET_gcServer"] = "0",
            ["HOME"] = "/tmp",
            ["PATH"] = "/usr/bin",
            ["NOT_OET_X"] = "kept",
        };

        ChildEnvironment.Scrub(environment);

        Assert.Equal(new[] { "DOTNET_gcServer", "HOME", "NOT_OET_X", "PATH" }, environment.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void the_pdf_child_never_inherits_the_node_token_or_any_other_oet_variable()
    {
        var secret = "OET_TEST_SECRET_" + Guid.NewGuid().ToString("N");
        var keep = "FLEET_TEST_KEEP_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(secret, "not-a-real-credential");
        Environment.SetEnvironmentVariable(keep, "kept");
        try
        {
            var run = new ChildRun(JobKinds.PdfExtract, "/scratch/in.pdf", "/scratch/out.json", new ChildParams(), 1024, TimeSpan.FromSeconds(60));

            var start = ProcessChildRunner.BuildStartInfo(run, "/scratch/out.json.params.json", "/app/Fleet.Agent.dll");

            Assert.DoesNotContain(start.Environment.Keys, key => key.StartsWith("OET_", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("kept", start.Environment[keep]);
            // The runner's own settings survive the scrub.
            Assert.Equal("0", start.Environment["DOTNET_gcServer"]);
            Assert.Equal("0", start.Environment["DOTNET_EnableDiagnostics"]);
            Assert.True(start.Environment.ContainsKey("DOTNET_GCHeapHardLimit"));
            Assert.Contains("--child", start.ArgumentList);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secret, null);
            Environment.SetEnvironmentVariable(keep, null);
        }
    }

    [LinuxFact]
    public async Task a_tool_started_by_the_process_runner_never_sees_oet_variables()
    {
        var secret = "OET_TEST_SECRET_" + Guid.NewGuid().ToString("N");
        var keep = "FLEET_TEST_KEEP_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(secret, "not-a-real-credential");
        Environment.SetEnvironmentVariable(keep, "kept");
        try
        {
            var output = new MemoryStream();

            var result = await new SystemProcessRunner().RunAsync(Sh("env", consume: (s, t) => s.CopyToAsync(output, t)), CancellationToken.None);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain(secret, text);
            Assert.Contains(keep + "=kept", text);
        }
        finally
        {
            Environment.SetEnvironmentVariable(secret, null);
            Environment.SetEnvironmentVariable(keep, null);
        }
    }

    [AgentDllFact]
    public async Task the_real_child_process_extracts_the_canary_and_honours_its_arguments()
    {
        using var dir = new TempDir();
        var pdf = CanaryPdf.Build();
        File.WriteAllBytes(dir.File("in.pdf"), pdf);
        var run = new ChildRun(JobKinds.PdfExtract, dir.File("in.pdf"), dir.File("out.json"),
            new ChildParams { Mode = "flat", MinTextLength = 5, IncludePages = true, EngineVersion = EngineVersions.Pdf, InputSha256 = TestIds.Sha(pdf), TimeoutSeconds = 60 },
            1024, TimeSpan.FromSeconds(60));
        var lease = new JobLease(TestIds.Job(1), 1, 20, 0, long.MaxValue / 2);

        var outcome = await new ProcessChildRunner(Environment.GetEnvironmentVariable(AgentDllFactAttribute.Variable)).RunAsync(run, lease, CancellationToken.None);

        Assert.Equal(ChildExit.Ok, outcome.ExitCode);
        Assert.False(outcome.TimedOut);
        using var json = JsonDocument.Parse(File.ReadAllBytes(dir.File("out.json")));
        Assert.Equal(CanaryPdf.Text, CanaryPdf.Normalise(json.RootElement.GetProperty("pages")[0].GetString()!));
        Assert.Equal(EngineVersions.Pdf, json.RootElement.GetProperty("engineVersion").GetString());
        Assert.True(File.Exists(dir.File("out.json.params.json")));
    }

    [AgentDllFact]
    public async Task the_real_child_process_reports_a_bad_input_without_taking_the_agent_down()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(dir.File("in.pdf"), Encoding.ASCII.GetBytes("this is not a pdf"));
        var run = new ChildRun(JobKinds.PdfExtract, dir.File("in.pdf"), dir.File("out.json"),
            new ChildParams { Mode = "flat", MinTextLength = 5, IncludePages = true, EngineVersion = EngineVersions.Pdf, InputSha256 = TestIds.Sha(new byte[] { 1 }), TimeoutSeconds = 60 },
            1024, TimeSpan.FromSeconds(60));

        var outcome = await new ProcessChildRunner(Environment.GetEnvironmentVariable(AgentDllFactAttribute.Variable)).RunAsync(run, null, CancellationToken.None);

        Assert.Equal(ChildExit.Ok, outcome.ExitCode);
        using var json = JsonDocument.Parse(File.ReadAllBytes(dir.File("out.json")));
        Assert.True(json.RootElement.GetProperty("needsOcr").GetBoolean());
        Assert.Equal("not_pdf", json.RootElement.GetProperty("needsOcrReason").GetString());
    }
}
