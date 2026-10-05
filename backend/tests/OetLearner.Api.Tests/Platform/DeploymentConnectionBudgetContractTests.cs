namespace OetLearner.Api.Tests.Platform;

/// <summary>
/// Static contract for the database connection budget (docs/ops/db-connection-budget.md):
/// every app process names itself to Postgres so pg_stat_activity can tell blue, green and
/// the ai-worker apart, and the manual rollout path retires the previous slot by default
/// without ever turning a good rollout into a failed one or weakening rollback.
/// </summary>
public sealed class DeploymentConnectionBudgetContractTests
{
    [Theory]
    [InlineData("oet-api-blue")]
    [InlineData("oet-api-green")]
    [InlineData("oet-ai-worker")]
    public void EachAppProcess_HasItsOwnNpgsqlApplicationName(string container)
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "docker-compose.production.yml"));
        var start = Array.FindIndex(lines, line => line.Trim() == $"container_name: {container}");
        Assert.True(start >= 0, $"{container} must exist in docker-compose.production.yml");
        var end = Array.FindIndex(lines, start + 1, line => line.TrimStart().StartsWith("container_name:", StringComparison.Ordinal));
        var block = lines[start..(end < 0 ? lines.Length : end)];

        Assert.Contains(block, line =>
            line.Contains("ConnectionStrings__DefaultConnection:", StringComparison.Ordinal)
            && line.Contains($";Application Name={container}", StringComparison.Ordinal)
            && line.Contains("Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}", StringComparison.Ordinal));
    }

    [Fact]
    public void ManualRolloutPath_StopsThePreviousSlotByDefault_OnlyAfterTheReleaseIsRecorded_AndNeverFailsTheRollout()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "deploy", "rollout-release.sh"));

        Assert.Contains("KEEP_PREVIOUS_SLOT_RUNNING:-false", script);
        Assert.DoesNotContain("KEEP_PREVIOUS_SLOT_RUNNING:-true", script);

        var routerRollback = script.IndexOf("rolling stable routers back to $previous_slot", StringComparison.Ordinal);
        var recorded = script.IndexOf("echo \"ACTIVE_SLOT=$target_slot\" > .deploy/active-slot.env", StringComparison.Ordinal);
        var stop = script.IndexOf("compose stop \"learner-api-$previous_slot\" \"web-$previous_slot\"", StringComparison.Ordinal);
        Assert.True(routerRollback >= 0, "the automatic router rollback must remain");
        Assert.True(recorded > routerRollback, "the release is recorded after the rollback branch");
        Assert.True(stop > recorded, "the previous slot is only stopped after the release is live and recorded");

        // Best effort: a failure to stop the old slot must not fail a rollout that is already live.
        var afterStop = script[stop..];
        Assert.Contains("|| echo", afterStop[..Math.Min(afterStop.Length, 300)]);
    }

    [Fact]
    public void LiveRolloutScript_RecreatesAStoppedInactiveSlot_SoRollbackDoesNotNeedItRunning()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "deploy", "auto-deploy-ghcr.sh"));

        // service_matches treats a non-running container as "needs update", and the update
        // path force-recreates it: `gh workflow run production-deploy.yml -f sha=<previous-sha>`
        // therefore works whether the inactive slot is running, stopped or missing.
        Assert.Contains("[ \"$running\" = \"true\" ] || return 1", script);
        Assert.Contains("--no-build --no-deps --pull never --force-recreate \"${update_services[@]}\"", script);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend", "src", "OetLearner.Api")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
