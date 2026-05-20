using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OetLearner.Api.Services.AiAssistant.Execution;

// Sandboxed devbox executor: runs commands inside a dedicated Docker
// container with the working tree mounted read/write, network egress
// restricted, and CPU/memory caps. NEVER reaches into prod containers.
public sealed class DevboxCodebaseExecutor : ICodebaseExecutor
{
    public DevboxCodebaseExecutor()
    {
        // TODO Phase 1: inject options bound from IRuntimeSettingsProvider
        // (sandbox image, mount path, command whitelist, timeout caps).
    }

    public Task<string> ReadFileAsync(string repoRelativePath, int? startLine, int? endLine, CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 1.");

    public Task WriteFileAsync(string repoRelativePath, string content, string? expectedContentHash, CancellationToken ct)
    {
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 1: enforce path whitelist + hash check.");
    }

    public Task<IReadOnlyList<string>> ListDirectoryAsync(string repoRelativePath, CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 1.");

    public Task<RunCommandResult> RunCommandAsync(string command, string cwd, int timeoutSec, CancellationToken ct)
    {
        // TODO: write AuditEvent
        throw new NotImplementedException("TODO Phase 1: docker exec + timeout + stream capture.");
    }

    public Task<GitResult> RunGitAsync(string subcommand, string[] args, CancellationToken ct)
        => throw new NotImplementedException("TODO Phase 1.");
}
