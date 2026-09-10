namespace OetLearner.Api.Services.AiAssistant.Tools;

/// <summary>
/// Shared repo-root resolution for the admin tools that touch the local
/// filesystem (ReadFileTool, ListDirectoryTool, SearchCodebaseTool,
/// WriteFileTool). Previously each tool duplicated its own ".git walk-up"
/// heuristic, which can never resolve in production: the API's Docker image
/// is a multi-stage `dotnet publish` output containing only compiled DLLs,
/// never a .git directory (see Indexing/CodebaseIndexer.FindRepositoryRoot
/// for the full root-cause writeup). CODEBASE_INDEX_ROOT is the same escape
/// hatch that fix introduced -- set it to a mounted source checkout to make
/// these tools resolve real paths outside local dev.
/// </summary>
internal static class RepoRootResolver
{
    internal static string Resolve(IConfiguration configuration)
    {
        var configuredRoot = configuration["CODEBASE_INDEX_ROOT"];
        if (!string.IsNullOrWhiteSpace(configuredRoot) && Directory.Exists(configuredRoot))
        {
            return configuredRoot;
        }

        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
