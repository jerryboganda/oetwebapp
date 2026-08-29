using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// HTTP mapping for the control-plane exception vocabulary.
///
/// <para>
/// The defect: <see cref="AiOperationConflictException"/>,
/// <see cref="AiOperationDuplicateResultUnavailableException"/>,
/// <see cref="AiOperationInFlightException"/> and
/// <see cref="AiFeaturePolicyRefusedException"/> had no branch in the top-level
/// handler, so every deliberate, truthful refusal surfaced as
/// <c>500 internal_server_error</c>. That lies to the client (nothing crashed),
/// gives it nothing to act on ("already done" is indistinguishable from "try
/// again in a moment"), and pages an on-call engineer for a working system.
/// </para>
///
/// <para>
/// The <c>Program.cs</c> handler is a top-level-statement lambda and cannot be
/// invoked directly, so the decision table lives in
/// <see cref="AiControlPlaneProblemMapper"/> and is unit tested here. The last
/// test is the static anchor that keeps the handler wired to it.
/// </para>
/// </summary>
public sealed class AiControlPlaneProblemMappingTests
{
    /// <summary>Conflict: the same key is bound to a DIFFERENT request. Retrying
    /// the identical payload cannot resolve it, so retryable is false.</summary>
    [Fact]
    public void Conflict_MapsTo409_StableCode_NotRetryable()
    {
        var problem = AiControlPlaneProblemMapper.TryMap(new AiOperationConflictException("idem-key"));

        Assert.NotNull(problem);
        Assert.Equal(409, problem!.StatusCode);
        Assert.Equal("ai_operation_conflict", problem.Code);
        Assert.False(problem.Retryable);
        Assert.Null(problem.RetryAfterSeconds);
        Assert.DoesNotContain("idem-key", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>Duplicate-result-unavailable: the work really happened. W2 stores
    /// a durable pointer, not the completion body, so 409 plus the operation
    /// identity is the only truthful answer that does not buy a second call.</summary>
    [Fact]
    public void DuplicateResultUnavailable_MapsTo409_AndCarriesTheOperationIdentity()
    {
        var problem = AiControlPlaneProblemMapper.TryMap(
            new AiOperationDuplicateResultUnavailableException("op-77", AiOperationState.Completed, "usage-9"));

        Assert.NotNull(problem);
        Assert.Equal(409, problem!.StatusCode);
        Assert.Equal("ai_operation_duplicate_result_unavailable", problem.Code);
        Assert.False(problem.Retryable);
        Assert.Equal("op-77", problem.OperationId);
        Assert.Equal(nameof(AiOperationState.Completed), problem.State);
    }

    /// <summary>In-flight: genuinely retryable, and the client is told when.
    /// 409 keeps it in the same family as the existing concurrency_conflict
    /// convention rather than implying the server is down.</summary>
    [Fact]
    public void InFlight_MapsTo409_Retryable_WithRetryAfter()
    {
        var problem = AiControlPlaneProblemMapper.TryMap(new AiOperationInFlightException("idem-key"));

        Assert.NotNull(problem);
        Assert.Equal(409, problem!.StatusCode);
        Assert.Equal("ai_operation_in_flight", problem.Code);
        Assert.True(problem.Retryable);
        Assert.Equal(AiControlPlaneProblemMapper.InFlightRetryAfterSeconds, problem.RetryAfterSeconds);
        Assert.True(problem.RetryAfterSeconds > 0);
    }

    /// <summary>Policy refused: the capability is switched off, which is an
    /// availability answer, not a client error and not a crash.</summary>
    [Fact]
    public void PolicyRefused_MapsTo503_WithStableCode()
    {
        var problem = AiControlPlaneProblemMapper.TryMap(
            new AiFeaturePolicyRefusedException(AiFeatureCodes.WritingGrade, "policy_disabled"));

        Assert.NotNull(problem);
        Assert.Equal(503, problem!.StatusCode);
        Assert.Equal("ai_feature_policy_refused", problem.Code);
        Assert.False(problem.Retryable);
        Assert.Contains("policy_disabled", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// No internal detail may reach a client body. Only a short sanitized
    /// machine reason is echoed — never free-form text, a stack, a provider
    /// body or an exception message.
    /// </summary>
    [Fact]
    public void PolicyRefused_SanitizesTheReason_AndLeaksNoInternals()
    {
        var problem = AiControlPlaneProblemMapper.TryMap(new AiFeaturePolicyRefusedException(
            AiFeatureCodes.WritingGrade,
            "Npgsql.PostgresException: 42P01 relation \"AiFeaturePolicies\" does not exist at Server=db;Password=hunter2"));

        Assert.NotNull(problem);
        Assert.Equal(503, problem!.StatusCode);
        Assert.DoesNotContain("hunter2", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", problem.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"", problem.Message, StringComparison.Ordinal);
        Assert.Matches(@"^[^(]+\([A-Za-z0-9_\-]{1,48}\).*$", problem.Message);
    }

    [Fact]
    public void BudgetExhausted_MapsTo402_StableCode_NotRetryable()
    {
        var problem = AiControlPlaneProblemMapper.TryMap(new AiBudgetExhaustedException("global_budget_exhausted"));

        Assert.NotNull(problem);
        Assert.Equal(402, problem!.StatusCode);
        Assert.Equal("global_budget_exhausted", problem.Code);
        Assert.False(problem.Retryable);
        Assert.Null(problem.RetryAfterSeconds);
        Assert.DoesNotContain("Exception", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankReason_StillProducesAStableMachineToken()
    {
        var problem = AiControlPlaneProblemMapper.TryMap(
            new AiFeaturePolicyRefusedException(AiFeatureCodes.WritingGrade, "   "));

        Assert.NotNull(problem);
        Assert.Contains("policy_unknown", problem!.Message, StringComparison.Ordinal);
    }

    /// <summary>Everything else must fall through untouched so the existing
    /// handler branches keep their behaviour.</summary>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    public void NonControlPlaneExceptions_AreNotMapped(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        Assert.Null(AiControlPlaneProblemMapper.TryMap(exception));
        Assert.Null(AiControlPlaneProblemMapper.TryMap(null));
    }

    /// <summary>
    /// Static anchor: the mapper is only useful if the pipeline actually calls
    /// it. Asserting on the source keeps the branch from being dropped by a
    /// later edit to the handler — the handler lambda itself is not invokable
    /// from a unit test.
    /// </summary>
    [Fact]
    public void ProgramExceptionHandler_InvokesTheMapper_BeforeTheGenericFallback()
    {
        var source = File.ReadAllText(LocateProgramCs());

        var mapperIndex = source.IndexOf("AiControlPlaneProblemMapper.TryMap", StringComparison.Ordinal);
        Assert.True(mapperIndex >= 0, "Program.cs no longer maps AI control-plane exceptions.");

        var fallbackIndex = source.IndexOf("\"internal_server_error\"", StringComparison.Ordinal);
        Assert.True(fallbackIndex >= 0, "Program.cs no longer has the generic fallback branch.");
        Assert.True(mapperIndex < fallbackIndex,
            "The AI control-plane branch must run before the generic 500 fallback.");

        Assert.Contains("Headers.RetryAfter", source, StringComparison.Ordinal);
    }

    private static string LocateProgramCs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "OetLearner.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate src/OetLearner.Api/Program.cs from the test output directory.");
    }
}
