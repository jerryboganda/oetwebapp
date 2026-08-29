namespace OetLearner.Api.Services.Ai;

/// <summary>
/// W4 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// process run mode for the dedicated <c>ai-worker</c> container.
///
/// <para>
/// <c>OET_RUN_MODE=worker</c> is the only value that enables the durable
/// <see cref="AiOperationWorker"/>. API slots default to <c>api</c> and must
/// not run cost-bearing hosted workers once <c>Ai:HostedWorkers:Enabled</c>
/// is false (the production drain flag).
/// </para>
/// </summary>
public static class AiRunMode
{
    public const string EnvVar = "OET_RUN_MODE";
    public const string Worker = "worker";
    public const string Api = "api";
    public const string HostedWorkersEnabledKey = "Ai:HostedWorkers:Enabled";

    public static bool IsWorker(IConfiguration configuration)
    {
        var raw = configuration[EnvVar];
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = configuration["Oet:RunMode"];
        }

        return string.Equals(raw, Worker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cost-bearing AI hosted services run on the worker always, and on the
    /// API only while the drain flag is on. Production API defaults the flag
    /// off so a missed compose env cannot keep duplicate crons after
    /// <c>ai-worker</c> is healthy. Development/test default on so local API
    /// without a worker container still advances exams and TTS jobs.
    /// </summary>
    public static bool EnableCostBearingHostedWorkers(IConfiguration configuration, IHostEnvironment environment)
    {
        if (IsWorker(configuration))
        {
            return true;
        }

        return configuration.GetValue(HostedWorkersEnabledKey, !environment.IsProduction());
    }
}
