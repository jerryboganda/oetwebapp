using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Services.Content;

namespace Fleet.Agent.Tests;

/// <summary>Determinism contract of pdf.extract (protocol 6.1.4; RW-101, RW-102, RW-103).</summary>
public sealed class ExtractorContractTests
{
    private const string ExtractorRelative = "backend/src/OetLearner.Api/Services/Content/PdfPigPdfTextExtractor.cs";

    private static string Normalised(string relative)
    {
        var bytes = File.ReadAllBytes(TestPaths.Combine(relative.Split('/')));
        // The repository is LF; a Windows checkout may be CRLF. Hash the LF form so the golden value is checkout independent.
        var text = Encoding.Latin1.GetString(bytes).Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(text))).ToLowerInvariant();
    }

    private static string PackageVersion(string csprojRelative)
    {
        var text = File.ReadAllText(TestPaths.Combine(csprojRelative.Split('/')));
        var match = Regex.Match(text, "<PackageReference\\s+Include=\"UglyToad\\.PdfPig\"\\s+Version=\"([^\"]+)\"");
        Assert.True(match.Success, "no UglyToad.PdfPig PackageReference in " + csprojRelative);
        return match.Groups[1].Value;
    }

    [Fact]
    public void RW101_the_agent_link_compiles_the_extractor_and_never_holds_a_copy()
    {
        var agentProject = File.ReadAllText(TestPaths.Combine("platform", "fleet", "src", "Fleet.Agent", "Fleet.Agent.csproj"));

        Assert.Contains("Content/PdfPigPdfTextExtractor.cs", agentProject);
        Assert.Matches("<Compile\\s+Include=\"\\$\\(FleetApiServicesDir\\)Content/PdfPigPdfTextExtractor\\.cs\"", agentProject);
        var copies = Directory.EnumerateFiles(TestPaths.Combine("platform", "fleet"), "PdfPigPdfTextExtractor*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToArray();
        Assert.Empty(copies);
    }

    [Fact]
    public void RW101_the_pdfpig_package_version_equals_the_one_the_api_ships()
    {
        Assert.Equal(PackageVersion("backend/src/OetLearner.Api/OetLearner.Api.csproj"), PackageVersion("platform/fleet/src/Fleet.Agent/Fleet.Agent.csproj"));
    }

    [Fact]
    public void RW102_changing_the_extractor_requires_a_layout_revision_bump_and_a_new_history_entry()
    {
        var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "pdf-extractor-history.json")));
        var history = golden.RootElement.GetProperty("history").EnumerateArray()
            .Select(e => (Revision: e.GetProperty("layoutRevision").GetInt32(), Sha: e.GetProperty("sha256").GetString()!))
            .ToList();
        var current = Normalised(ExtractorRelative);

        Assert.Equal(history.Count, history.Select(h => h.Revision).Distinct().Count());
        Assert.Equal(history.Count, history.Select(h => h.Sha).Distinct().Count());
        var entry = history.SingleOrDefault(h => h.Revision == PdfTextEngine.LayoutRevision);
        Assert.True(entry.Sha is not null,
            "PdfTextEngine.LayoutRevision " + PdfTextEngine.LayoutRevision + " has no entry in Golden/pdf-extractor-history.json. Append {layoutRevision, sha256: " + current + "}.");
        Assert.True(string.Equals(entry.Sha, current, StringComparison.Ordinal),
            "PdfPigPdfTextExtractor.cs changed (sha256 " + current + ") without a PdfTextEngine.LayoutRevision bump. Bump the revision and append a history entry; never edit an existing one.");
        Assert.Equal(history.Max(h => h.Revision), PdfTextEngine.LayoutRevision);
    }

    [Fact]
    public void RW103_the_agent_image_matches_the_api_image_environment_facts()
    {
        var agentDockerfile = File.ReadAllText(TestPaths.Combine("platform", "fleet", "src", "Fleet.Agent", "Dockerfile"));
        var apiDockerfile = File.ReadAllText(TestPaths.Combine("backend", "Dockerfile.runtime"));

        // Same distro family: both build on the official dotnet Debian images of the same major.
        Assert.Contains("mcr.microsoft.com/dotnet/aspnet:10.0", apiDockerfile);
        Assert.Matches(@"FROM(\s+--platform=\S+)?\s+mcr\.microsoft\.com/dotnet/runtime:10\.0", agentDockerfile);
        // Linux amd64 only, invariant globalization NEVER set, LANG/LC_ALL never set (invariant culture), in BOTH images.
        foreach (var dockerfile in new[] { agentDockerfile, apiDockerfile })
        {
            Assert.DoesNotContain("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", dockerfile);
            Assert.DoesNotMatch(@"(?m)^\s*(ENV|ARG)\s+.*\b(LANG|LC_ALL)\b", dockerfile);
        }

        Assert.Contains("--platform=linux/amd64", agentDockerfile);
        var agentProject = File.ReadAllText(TestPaths.Combine("platform", "fleet", "src", "Fleet.Agent", "Fleet.Agent.csproj"));
        Assert.DoesNotContain("InvariantGlobalization", agentProject);
    }
}

/// <summary>
/// Runs only when FLEET_PARITY_LIST names a file listing PDFs. An inert MANUAL tool (owner directive 2026-10-06, no automated QA): the owner
/// sets the variables by hand; no workflow runs it. The production safeguard is the API's runtime shadow compare (RemoteJobShadowComparer).
/// </summary>
public sealed class ParityFactAttribute : FactAttribute
{
    public const string ListVariable = "FLEET_PARITY_LIST";

    public ParityFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ListVariable)))
        {
            Skip = "Set " + ListVariable + " to a file listing PDFs (one path per line) to run corpus parity.";
        }
    }
}

/// <summary>
/// Gate 1 (protocol 6.1.4 item 5, RW-100): over the tracked corpus the agent's pdf.extract result is byte-equal to
/// PdfPigPdfTextExtractor run in-process: per-page text, flat text, embeddedChars and textSha256. Optionally against the
/// REAL API assembly (FLEET_ORACLE_API_DLL) and through the REAL child process of a published agent (FLEET_AGENT_DLL).
/// </summary>
public sealed class CorpusParityTests
{
    private static Func<byte[], IReadOnlyList<string>>? LoadApiOracle()
    {
        var dll = Environment.GetEnvironmentVariable("FLEET_ORACLE_API_DLL");
        if (string.IsNullOrWhiteSpace(dll)) return null;

        var assembly = Assembly.LoadFrom(dll);
        var type = assembly.GetType("OetLearner.Api.Services.Content.PdfPigPdfTextExtractor", throwOnError: true)!;
        var logger = typeof(NullLogger<>).MakeGenericType(type).GetField("Instance")!.GetValue(null);
        var instance = Activator.CreateInstance(type, logger)!;
        var method = type.GetMethod("ExtractPagesAsync", new[] { typeof(Stream), typeof(CancellationToken) })!;
        return bytes =>
        {
            using var stream = new MemoryStream(bytes);
            var task = (Task)method.Invoke(instance, new object[] { stream, CancellationToken.None })!;
            task.GetAwaiter().GetResult();
            return (IReadOnlyList<string>)task.GetType().GetProperty("Result")!.GetValue(task)!;
        };
    }

    [ParityFact]
    public async Task RW100_agent_output_is_byte_equal_to_the_in_process_extractor_over_the_tracked_corpus()
    {
        var files = File.ReadAllLines(Environment.GetEnvironmentVariable(ParityFactAttribute.ListVariable)!)
            .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        Assert.NotEmpty(files);

        var agentDll = Environment.GetEnvironmentVariable("FLEET_AGENT_DLL");
        IChildRunner runner = string.IsNullOrWhiteSpace(agentDll) ? new InProcessChildRunner() : new ProcessChildRunner(agentDll);
        var apiOracle = LoadApiOracle();
        var oracle = new PdfPigPdfTextExtractor(NullLogger<PdfPigPdfTextExtractor>.Instance);
        using var dir = new TempDir();
        var mismatches = new List<string>();
        var withText = 0;

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var input = dir.File("in.pdf");
            var output = dir.File("out.json");
            File.Delete(output);
            File.WriteAllBytes(input, bytes);

            IReadOnlyList<string> expectedPages;
            using (var stream = new MemoryStream(bytes)) expectedPages = await oracle.ExtractPagesAsync(stream, CancellationToken.None);
            string expectedFlat;
            using (var stream = new MemoryStream(bytes)) expectedFlat = await oracle.ExtractAsync(stream, CancellationToken.None);
            var textSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedFlat))).ToLowerInvariant();

            // A text that cannot be carried in a valid UTF-8 JSON result (an unpaired surrogate) or that exceeds a protocol bound is
            // refused by the agent on purpose (RW-109); the API's local path then takes over. The agent must refuse exactly those.
            var expectRefusal = false;
            try
            {
                PdfExtractCore.Evaluate(PdfExtractCore.HasPdfMagic(bytes), expectedPages, "flat", 1);
            }
            catch (UnrepresentableResultException)
            {
                expectRefusal = true;
            }

            var run = new ChildRun(JobKinds.PdfExtract, input, output,
                new ChildParams { Mode = "flat", MinTextLength = 1, IncludePages = true, EngineVersion = EngineVersions.Pdf, InputSha256 = TestIds.Sha(bytes), TimeoutSeconds = 300 },
                4096, TimeSpan.FromMinutes(5));
            var outcome = await runner.RunAsync(run, null, CancellationToken.None);
            if (expectRefusal)
            {
                if (outcome.ExitCode != ChildExit.Unrepresentable) mismatches.Add(Path.GetFileName(file) + ": expected a refusal, child exit " + outcome.ExitCode);
                continue;
            }

            if (outcome.ExitCode != ChildExit.Ok)
            {
                mismatches.Add(Path.GetFileName(file) + ": child exit " + outcome.ExitCode);
                continue;
            }

            using var json = JsonDocument.Parse(File.ReadAllBytes(output));
            var root = json.RootElement;
            var needsOcr = root.GetProperty("needsOcr").GetBoolean();
            if (needsOcr != (expectedFlat.Length < 1))
            {
                mismatches.Add(Path.GetFileName(file) + ": needsOcr " + needsOcr);
                continue;
            }

            if (needsOcr) continue;
            withText++;
            var pages = root.GetProperty("pages").EnumerateArray().Select(p => p.GetString()!).ToArray();
            if (!pages.SequenceEqual(expectedPages, StringComparer.Ordinal)) mismatches.Add(Path.GetFileName(file) + ": pages differ");
            if (root.GetProperty("embeddedChars").GetInt32() != expectedFlat.Length) mismatches.Add(Path.GetFileName(file) + ": embeddedChars differ");
            if (root.GetProperty("textSha256").GetString() != textSha) mismatches.Add(Path.GetFileName(file) + ": textSha256 differs");
            if (string.Join("\n\n", pages).Trim() != expectedFlat) mismatches.Add(Path.GetFileName(file) + ": flat differs");

            if (apiOracle is not null && !apiOracle(bytes).SequenceEqual(pages, StringComparer.Ordinal))
            {
                mismatches.Add(Path.GetFileName(file) + ": differs from the API assembly");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
        Assert.True(withText > 0, "the corpus produced no extractable text at all; the parity run proved nothing");
    }
}
