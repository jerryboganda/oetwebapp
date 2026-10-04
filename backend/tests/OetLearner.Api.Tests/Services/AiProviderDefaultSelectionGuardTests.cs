using System.Text.RegularExpressions;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Repo scan: every place that picks an AI provider row IMPLICITLY ("the first active credentialed row")
/// must apply <c>AiProviderDefaultEligibility</c>, so a keyless subscription sidecar or an explicit-only
/// provider (OpenCode) can never receive traffic nobody chose. A new caller fails here until it either
/// uses the shared predicate or is listed with the reason it cannot reach a default. Source text only:
/// nothing is compiled or executed beyond reading files.
/// </summary>
public sealed class AiProviderDefaultSelectionGuardTests
{
    private const string Api = "OetLearner.Api/";
    private const string GuardToken = "IsDefaultEligible";

    /// <param name="Calls">Expected number of matches in the file; null = not pinned (file owned elsewhere).</param>
    /// <param name="RequiredTexts">Code (comments stripped) the file must still contain.</param>
    /// <param name="Why">Why this site cannot hand a default pick to a row nobody chose.</param>
    private sealed record Site(int? Calls, string[] RequiredTexts, string Why);

    // ── Callers of IAiProviderRegistry.ListByCategoryAsync / ListActiveAsync ──────────────────
    private static readonly Regex RegistryListCall = new(
        @"\.(?:ListByCategoryAsync|ListActiveAsync)\(", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, Site> RegistryCallers = new(StringComparer.Ordinal)
    {
        [Api + "Services/AiAssistant/AiAssistantGateway.cs"] = new(
            null,
            [GuardToken],
            "FirstCredentialedOpenAiCompatibleRowAsync / FirstCredentialedTextChatRowAsync pick 'the first credentialed row' for the assistant."),
        [Api + "Services/Rulebook/AiGatewayService.cs"] = new(
            1,
            ["AiProviderDefaultEligibility." + GuardToken],
            "The topRow fallback for a call that names neither a provider nor a route."),
        [Api + "Services/Rulebook/AiProviderRegistry.cs"] = new(
            2,
            ["AiProviderDefaultEligibility." + GuardToken, "AiProviderDialect.Cloudflare"],
            "RegistryBackedProvider with no provider code takes eligible rows only; CloudflareWorkersAiProvider filters Dialect == Cloudflare."),
        [Api + "Services/Rulebook/CopilotAiModelProvider.cs"] = new(
            1,
            ["AiProviderDialect.Copilot"],
            "Filters Dialect == Copilot, so it can never return an OpenAI-compatible row such as OpenCode."),
    };

    // ── Direct queries that order provider rows by failover priority (a pick that skips the registry) ──
    private static readonly Regex FailoverOrdering = new(
        @"\bOrderBy\(\s*\w+\s*=>\s*\w+\.FailoverPriority\s*\)", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, Site> FailoverPickers = new(StringComparer.Ordinal)
    {
        [Api + "Endpoints/AiUsageAdminEndpoints.cs"] = new(
            1,
            [],
            "Admin list endpoint: orders rows for display, picks nothing."),
        [Api + "Services/AiAssistant/AiAssistantFeatureRouteSeeder.cs"] = new(
            1,
            ["ExplicitOnlyCodes"],
            "First-boot assistant route default: excludes explicit-only codes (and keyless sidecars) in the query."),
        [Api + "Services/Content/PaperExtractionProvider.cs"] = new(
            1,
            ["AiProviderCategory.Ocr"],
            "Selects within the Ocr / PdfExtraction categories only; an OpenCode row is TextChat."),
        [Api + "Services/Rulebook/AiGatewayService.cs"] = new(
            1,
            ["AiProviderDefaultEligibility." + GuardToken],
            "The topRow fallback (same site as above)."),
        [Api + "Services/Rulebook/AiProviderRegistry.cs"] = new(
            2,
            [],
            "The registry's own list implementations; their consumers are covered by the caller scan."),
    };

    [Fact]
    public void EveryProviderRegistryListCaller_AppliesTheDefaultEligibilityGuard_OrIsFilteredWithAReason()
    {
        var problems = Verify(Scan(RegistryListCall), RegistryCallers, "RegistryCallers",
            "Use AiProviderDefaultEligibility.IsDefaultEligible for any implicit 'first row' pick, or add the file with the reason it cannot reach a default.");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryDirectFailoverPriorityPick_AppliesTheGuard_OrIsScopedWithAReason()
    {
        var problems = Verify(Scan(FailoverOrdering), FailoverPickers, "FailoverPickers",
            "A query that orders AiProviders by FailoverPriority and takes the first row must exclude explicit-only and marker rows (AiProviderDefaultEligibility / OpenCodeProviderDefaults.ExplicitOnlyCodes).");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheDefaultSelectionSites_UseTheSharedPredicate_NotACopyOfIt()
    {
        Assert.Matches(
            @"\.Where\(\s*AiProviderDefaultEligibility\.IsDefaultEligible\s*\)",
            ReadCode("Services/Rulebook/AiGatewayService.cs"));
        Assert.Matches(
            @"FirstOrDefault\(\s*AiProviderDefaultEligibility\.IsDefaultEligible\s*\)",
            ReadCode("Services/Rulebook/AiProviderRegistry.cs"));
        // The route seeder queries the table directly, so it applies the same code list inside the query.
        Assert.Contains(
            "ExplicitOnlyCodes.Contains(",
            ReadCode("Services/AiAssistant/AiAssistantFeatureRouteSeeder.cs"),
            StringComparison.Ordinal);
        // The assistant gateway's First*Row helpers are guarded by their own change.
        Assert.Contains(GuardToken, ReadCode("Services/AiAssistant/AiAssistantGateway.cs"), StringComparison.Ordinal);
    }

    private static List<string> Verify(
        Dictionary<string, (int Count, string Code)> found,
        Dictionary<string, Site> allowed,
        string listName,
        string remedy)
    {
        var problems = new List<string>();
        foreach (var (path, hit) in found.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!allowed.TryGetValue(path, out var site))
            {
                problems.Add($"{path}: {hit.Count} unlisted match(es). {remedy}");
                continue;
            }

            if (site.Calls is { } expected && hit.Count != expected)
                problems.Add($"{path}: expected {expected} match(es), found {hit.Count}. Re-review the change and update {listName}.");
            foreach (var required in site.RequiredTexts)
            {
                if (!hit.Code.Contains(required, StringComparison.Ordinal))
                    problems.Add($"{path}: no longer contains '{required}'. {site.Why}");
            }
        }

        foreach (var stale in allowed.Keys.Where(path => !found.ContainsKey(path)).OrderBy(path => path, StringComparer.Ordinal))
            problems.Add($"{stale}: listed in {listName} but no longer matches; remove the stale entry.");

        return problems;
    }

    /// <summary>Every non-test source file with at least one match, comments stripped so that prose
    /// mentioning a method or the guard cannot satisfy or trigger the scan.</summary>
    private static Dictionary<string, (int Count, string Code)> Scan(Regex pattern)
    {
        var srcRoot = FindSrcRoot();
        var found = new Dictionary<string, (int Count, string Code)>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(srcRoot, file).Replace('\\', '/');
            if (IsGenerated(relative)) continue;

            var code = StripComments(File.ReadAllText(file));
            var count = pattern.Matches(code).Count;
            if (count > 0) found[relative] = (count, code);
        }

        return found;
    }

    private static string ReadCode(string pathUnderApi)
        => StripComments(File.ReadAllText(Path.Combine(FindSrcRoot(), "OetLearner.Api", pathUnderApi)));

    private static bool IsGenerated(string relative)
        => relative.Contains("/obj/", StringComparison.Ordinal)
           || relative.Contains("/bin/", StringComparison.Ordinal)
           || relative.Contains("/Migrations/", StringComparison.Ordinal);

    private static string StripComments(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return Regex.Replace(text, @"//[^\r\n]*", string.Empty, RegexOptions.CultureInvariant);
    }

    private static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "src");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("backend/src not found from " + AppContext.BaseDirectory);
    }
}
