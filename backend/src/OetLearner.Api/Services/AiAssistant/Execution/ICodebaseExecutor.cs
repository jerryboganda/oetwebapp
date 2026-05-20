using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Execution;

public sealed class RunCommandResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
}

public sealed class GitResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
}

public interface ICodebaseExecutor
{
    // TODO: via IFileStorage or ICodebaseExecutor (sandboxed)
    Task<string> ReadFileAsync(string repoRelativePath, int? startLine, int? endLine, CancellationToken ct);

    // expectedContentHash enables optimistic concurrency (refuse if file changed).
    Task WriteFileAsync(string repoRelativePath, string content, string? expectedContentHash, CancellationToken ct);

    Task<IReadOnlyList<string>> ListDirectoryAsync(string repoRelativePath, CancellationToken ct);

    Task<RunCommandResult> RunCommandAsync(string command, string cwd, int timeoutSec, CancellationToken ct);

    Task<GitResult> RunGitAsync(string subcommand, string[] args, CancellationToken ct);
}
