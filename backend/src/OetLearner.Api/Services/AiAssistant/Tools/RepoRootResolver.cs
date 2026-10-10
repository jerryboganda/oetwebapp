namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>What a root-resolution attempt found.</summary>
/// <param name="Root">A directory that actually contains the project's source tree, or null.</param>
/// <param name="Reason">Why it resolved the way it did — shown to the model so a tool can explain
/// itself instead of reporting a false zero.</param>
/// <param name="SearchablePrefixes">The allowed source prefixes that actually exist under
/// <paramref name="Root"/>. Empty means this root holds no project source at all.</param>
internal readonly record struct RepoRootResolution(
    string? Root,
    string Reason,
    IReadOnlyList<string> SearchablePrefixes)
{
    /// <summary>True only when a root was found AND it contains project source.</summary>
    internal bool HasSource => Root is not null && SearchablePrefixes.Count > 0;
}

/// <summary>
/// The SINGLE definition of "where is this project's source", for every component that needs it:
/// the filesystem tools (ReadFileTool, ListDirectoryTool, SearchCodebaseTool, WriteFileTool) and
/// the indexer (CodebaseIndexer).
///
/// <para>
/// <b>Why this existed.</b> There were two contradictory answers for one question. The tools fell
/// back to <c>Directory.GetCurrentDirectory()</c> and so cheerfully "worked" against the
/// <c>dotnet publish</c> directory in the production image (<c>/app</c>, ~60 DLLs and no source),
/// while <c>CodebaseIndexer.FindRepositoryRoot()</c> returned null and the index never built. The
/// result in production was an assistant that listed DLLs, reported "Directory not found" for
/// <c>backend/</c>, and answered codebase questions with <c>totalMatches: 0</c> — all as SUCCESS.
/// Two notions of the root made both symptoms invisible.
/// </para>
///
/// <para>
/// Resolution order, deliberately:
/// </para>
/// <list type="number">
/// <item><c>CODEBASE_INDEX_ROOT</c> when set AND the directory exists. This is how a source
/// checkout is mounted into the container.</item>
/// <item>Walk up from <see cref="AppContext.BaseDirectory"/> for <c>.git</c> (local dev).</item>
/// <item>Walk up from the current directory for <c>.git</c> (some dev layouts).</item>
/// </list>
///
/// <para>
/// If none of those produces a directory that actually CONTAINS project source, this returns
/// <see langword="null"/> with a reason. <b>It does not fall back to the current directory</b>: a
/// caller that received a root containing no source would then search a publish folder and report a
/// confident zero. "No source available" and "searched and found nothing" must never look alike.
/// </para>
/// </summary>
internal static class RepoRootResolver
{
    /// <summary>The allowed project source prefixes. Kept in one place so the tools, the indexer
    /// and this resolver cannot drift apart on what counts as source.</summary>
    internal static readonly string[] AllowedPrefixes =
    [
        "app/", "components/", "lib/", "hooks/", "contexts/", "types/",
        "backend/", "tests/", "docs/", "rulebooks/", "scripts/",
        "messages/", "config/", "public/", "agent-gateway/",
        "android/", "ios/", "capacitor-web/", "tools/", "ops/", "agents/",
    ];

    /// <summary>Directories never descended into, whatever the root.</summary>
    internal static readonly string[] BlockedSegments =
        [".env", "secrets", "node_modules", ".git", "bin", "obj", ".next", "dist", "coverage"];

    /// <summary>
    /// Resolve the root and report, for the candidate root, which allowed prefixes really exist.
    /// Cached per configuration value so a tool call does not re-stat twenty directories.
    /// </summary>
    internal static RepoRootResolution Resolve(IConfiguration configuration)
    {
        var candidates = new List<(string Path, string Source)>();

        var configured = configuration["CODEBASE_INDEX_ROOT"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add((configured.Trim(), "CODEBASE_INDEX_ROOT"));
        }
        else
        {
            candidates.Add((AppContext.BaseDirectory, "app base directory"));
        }

        foreach (var walked in WalkUpForGit(AppContext.BaseDirectory).Prepend(Path.GetDirectoryName(AppContext.BaseDirectory) ?? ""))
        {
            if (!string.IsNullOrWhiteSpace(walked)) candidates.Add((walked!, "git walk-up"));
        }
        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (var walked in WalkUpForGit(Directory.GetCurrentDirectory()))
            {
                candidates.Add((walked, "git walk-up"));
            }
        }

        var rejected = new List<string>();
        foreach (var (path, source) in candidates)
        {
            if (!Directory.Exists(path))
            {
                rejected.Add($"{source} '{path}' does not exist");
                continue;
            }

            var present = AllowedPrefixes
                .Where(p => Directory.Exists(Path.Combine(path, p.TrimEnd('/'))))
                .ToArray();
            if (present.Length == 0)
            {
                rejected.Add($"{source} '{path}' exists but contains no project source (none of the {AllowedPrefixes.Length} known prefixes)");
                continue;
            }

            return new RepoRootResolution(
                path,
                $"{source} '{path}' contains {present.Length}/{AllowedPrefixes.Length} source prefixes",
                present);
        }

        return new RepoRootResolution(
            null,
            "No project source is available to this deployment. " + string.Join("; ", rejected)
            + ". A source checkout must be mounted and CODEBASE_INDEX_ROOT must point at it before the codebase tools can search anything.",
            Array.Empty<string>());
    }

    private static IEnumerable<string?> WalkUpForGit(string? start)
    {
        var dir = start;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (Directory.Exists(Path.Combine(dir!, ".git"))) yield return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
    }

    /// <summary>
    /// Convenience wrapper for callers that genuinely want a string and do not care WHY. Prefer
    /// <see cref="Resolve"/> so an unavailable root is never mistaken for an empty one.
    /// </summary>
    internal static string ResolveOrCurrent(IConfiguration configuration)
        => Resolve(configuration).Root ?? Directory.GetCurrentDirectory();
}
