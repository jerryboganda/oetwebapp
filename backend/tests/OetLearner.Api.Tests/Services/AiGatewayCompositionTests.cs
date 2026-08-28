using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Rulebook;
using Xunit;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// W2 of the AI cost/reliability remediation (incident INC-2026-CLAUDE-01) —
/// structural proof that the coordinated gateway composes as a DAG.
///
/// <para>
/// The reviewed defect these tests exist to prevent: the coordinator injected
/// <see cref="IAiGatewayService"/>. The moment production resolved that
/// interface to the coordinating facade, every AI call would recurse until the
/// stack blew. These are reflection/DI assertions rather than behavioural ones
/// on purpose — they fail at the shape of the code, so the cycle cannot be
/// reintroduced by a well-meaning refactor.
/// </para>
/// </summary>
public class AiGatewayCompositionTests
{
    /// <summary>
    /// A-2: the coordinator must depend on the CORE executor seam, never on the
    /// public gateway interface it sits behind.
    /// </summary>
    [Fact]
    public void AiExecutionCoordinator_HasNoIAiGatewayServiceDependency()
    {
        var ctor = Assert.Single(typeof(AiExecutionCoordinator).GetConstructors());
        var parameters = ctor.GetParameters();

        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(IAiGatewayService));
        Assert.Contains(parameters, p => p.ParameterType == typeof(IAiGatewayCoreExecutor));
    }

    /// <summary>A-3: the facade must depend on the core seam too, so a request
    /// carrying an OperationId can be dispatched without re-entering itself.</summary>
    [Fact]
    public void CoordinatedAiGatewayService_DependsOnCoreExecutor_NotOnItself()
    {
        var ctor = Assert.Single(typeof(CoordinatedAiGatewayService).GetConstructors());
        var parameters = ctor.GetParameters();

        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(IAiGatewayService));
        Assert.Contains(parameters, p => p.ParameterType == typeof(IAiGatewayCoreExecutor));
        Assert.Contains(parameters, p => p.ParameterType == typeof(IAiExecutionCoordinator));
    }

    /// <summary>
    /// A-1: <see cref="AiGatewayService"/> implements BOTH interfaces from one
    /// class, so no adapter/duplicate instance can drift, and it stays directly
    /// constructible by the pre-W2 unit tests.
    /// </summary>
    [Fact]
    public void AiGatewayService_ImplementsBothGatewayAndCoreExecutor()
    {
        Assert.True(typeof(IAiGatewayService).IsAssignableFrom(typeof(AiGatewayService)));
        Assert.True(typeof(IAiGatewayCoreExecutor).IsAssignableFrom(typeof(AiGatewayService)));

        // Same member surface: the core seam is a pure re-exposure, not a
        // reduced capability the coordinator would have to work around.
        var gatewayMembers = typeof(IAiGatewayService).GetMethods().Select(m => m.Name).OrderBy(n => n);
        var coreMembers = typeof(IAiGatewayCoreExecutor).GetMethods().Select(m => m.Name).OrderBy(n => n);
        Assert.Equal(gatewayMembers, coreMembers);
    }

    /// <summary>
    /// A-2/A-3: the production registration graph resolves
    /// <see cref="IAiGatewayService"/> to the facade, the core seam to the
    /// concrete gateway, and — crucially — building the whole graph terminates
    /// (a self-referential registration would stack-overflow here).
    /// </summary>
    [Fact]
    public void ProductionRegistrations_ResolveGatewayToTheFacade()
    {
        using var provider = BuildProductionLikeGraph();
        using var scope = provider.CreateScope();

        var gateway = scope.ServiceProvider.GetRequiredService<IAiGatewayService>();
        Assert.IsType<CoordinatedAiGatewayService>(gateway);

        var core = scope.ServiceProvider.GetRequiredService<IAiGatewayCoreExecutor>();
        Assert.IsType<AiGatewayService>(core);

        var coordinator = scope.ServiceProvider.GetRequiredService<IAiExecutionCoordinator>();
        Assert.IsType<AiExecutionCoordinator>(coordinator);
    }

    /// <summary>
    /// A-3 recursion fence: a request that already carries an OperationId goes
    /// straight to the core, so the coordinator is never re-entered. Proven by
    /// a coordinator double that fails the test if it is called at all.
    /// </summary>
    [Fact]
    public async Task Facade_WithOperationIdAlreadySet_CallsCoreDirectly()
    {
        var core = new CountingCoreExecutor();
        var coordinator = new ExplodingCoordinator();
        var facade = new CoordinatedAiGatewayService(core, coordinator, null, NullLogger<CoordinatedAiGatewayService>.Instance);

        var result = await facade.CompleteAsync(new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.AdminWritingDraft,
            OperationId = "op-existing",
        });

        Assert.Equal("core", result.Completion);
        Assert.Equal(1, core.Calls);
        Assert.Equal(0, coordinator.Calls);
    }

    /// <summary>A-3: the kill switch is fail-safe and DEFAULT-ENABLED — absent
    /// configuration must coordinate, not silently bypass.</summary>
    [Fact]
    public async Task Facade_WithNoOptionsConfigured_CoordinatesByDefault()
    {
        var core = new CountingCoreExecutor();
        var coordinator = new CountingCoordinator();
        var facade = new CoordinatedAiGatewayService(core, coordinator, null, NullLogger<CoordinatedAiGatewayService>.Instance);

        await facade.CompleteAsync(new AiGatewayRequest { FeatureCode = AiFeatureCodes.AdminWritingDraft });

        Assert.Equal(1, coordinator.Calls);
        Assert.Equal(0, core.Calls); // the coordinator owns the core call
    }

    /// <summary>D-1: a duplicate that reached a terminal state without a
    /// reconstructable payload is reported truthfully — never as a success with
    /// a null result, and never with a second provider call.</summary>
    [Fact]
    public async Task Facade_WithDuplicateTerminalOperation_ThrowsTruthfully()
    {
        var core = new CountingCoreExecutor();
        var coordinator = new DuplicateCoordinator();
        var facade = new CoordinatedAiGatewayService(core, coordinator, null, NullLogger<CoordinatedAiGatewayService>.Instance);

        var ex = await Assert.ThrowsAsync<AiOperationDuplicateResultUnavailableException>(
            () => facade.CompleteAsync(new AiGatewayRequest { FeatureCode = AiFeatureCodes.AdminWritingDraft }));

        Assert.Equal("op-dup", ex.OperationId);
        Assert.Equal(AiOperationState.Completed, ex.State);
        Assert.Equal("usage-1", ex.ResultRef);
        Assert.Equal("ai_operation_duplicate_result_unavailable", ex.ErrorCode);
        Assert.Equal(0, core.Calls);
    }

    /// <summary>A-4: the request hash is deterministic and content-sensitive,
    /// and never contains the raw prompt.</summary>
    [Fact]
    public void RequestHash_IsDeterministic_ContentSensitive_AndCarriesNoRawPrompt()
    {
        var prompt = new AiGroundedPrompt { SystemPrompt = "SYSTEM RULES", TaskInstruction = "DO THE THING" };
        var a = new AiGatewayRequest
        {
            FeatureCode = AiFeatureCodes.AdminWritingDraft,
            UserId = "u1",
            Prompt = prompt,
            UserInput = "learner secret text",
        };
        var b = a with { };
        var c = a with { UserInput = "learner secret text!" };

        var hashA = CoordinatedAiGatewayService.BuildRequestHash(a);
        Assert.Equal(hashA, CoordinatedAiGatewayService.BuildRequestHash(b));
        Assert.NotEqual(hashA, CoordinatedAiGatewayService.BuildRequestHash(c));

        Assert.DoesNotContain("learner", hashA, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SYSTEM", hashA, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{64}$", hashA);
    }

    /// <summary>
    /// W2 item 7 — <see cref="AiOperationRequest.AttemptLimit"/> counts PHYSICAL
    /// provider turns, and one coordinated business operation can legitimately
    /// make several: the gateway's bounded tool loop bills one call per turn.
    /// Leaving it hard-coded at 1 would tell W4 "this operation may only ever
    /// have made one billed call", which is false for every tool-using feature.
    /// It is therefore derived from the configured tool-loop ceiling.
    /// </summary>
    [Fact]
    public async Task Facade_DerivesAttemptLimitFromTheConfiguredToolLoopCeiling()
    {
        var coordinator = new CapturingCoordinator();
        var facade = new CoordinatedAiGatewayService(
            new CountingCoreExecutor(), coordinator, null,
            NullLogger<CoordinatedAiGatewayService>.Instance,
            Microsoft.Extensions.Options.Options.Create(
                new OetLearner.Api.Services.AiTools.AiToolOptions { MaxToolCallsPerCompletion = 7 }));

        await facade.CompleteAsync(new AiGatewayRequest { FeatureCode = AiFeatureCodes.AdminWritingDraft });

        Assert.Equal(7, coordinator.LastRequest!.AttemptLimit);
    }

    /// <summary>Absent configuration must still be truthful — never 1 — and must
    /// never be zero/negative, which would make the bound meaningless.</summary>
    [Fact]
    public async Task Facade_WithNoToolOptions_UsesTheProductionDefaultCeiling()
    {
        var coordinator = new CapturingCoordinator();
        var facade = new CoordinatedAiGatewayService(
            new CountingCoreExecutor(), coordinator, null,
            NullLogger<CoordinatedAiGatewayService>.Instance);

        await facade.CompleteAsync(new AiGatewayRequest { FeatureCode = AiFeatureCodes.AdminWritingDraft });

        var expected = new OetLearner.Api.Services.AiTools.AiToolOptions().MaxToolCallsPerCompletion;
        Assert.Equal(expected, coordinator.LastRequest!.AttemptLimit);
        Assert.True(coordinator.LastRequest.AttemptLimit > 1);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProductionLikeGraph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LearnerDbContext>(o => o.UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared"));
        services.AddSingleton<IHostEnvironment>(new CompositionHostEnvironment());
        services.AddSingleton<IRulebookLoader, UnusedRulebookLoader>();
        services.AddScoped<IAiFeaturePolicyRegistry, AiFeaturePolicyRegistry>();
        services.AddScoped<IAiOperationStore, AiOperationStore>();
        services.AddScoped<IAiExecutionCoordinator, AiExecutionCoordinator>();

        // Mirrors Program.cs: one concrete gateway exposed as the core seam,
        // with the public interface pointing at the facade.
        services.AddScoped<AiGatewayService>();
        services.AddScoped<IAiGatewayCoreExecutor>(sp => sp.GetRequiredService<AiGatewayService>());
        services.AddScoped<IAiGatewayService, CoordinatedAiGatewayService>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class CompositionHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "OetLearner.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>Resolution-only stub: the composition test builds the graph but
    /// never runs a completion, so no member is ever invoked.</summary>
    private sealed class UnusedRulebookLoader : IRulebookLoader
    {
        public OetRulebook Load(RuleKind kind, ExamProfession profession) => throw new NotSupportedException();
        public IEnumerable<OetRulebook> All() => [];
        public OetRule? FindRule(RuleKind kind, ExamProfession profession, string ruleId) => null;
        public System.Text.Json.JsonElement GetAssessmentCriteria(RuleKind kind) => default;
    }

    private sealed class CountingCoreExecutor : IAiGatewayCoreExecutor
    {
        public int Calls { get; private set; }

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AiGatewayResult { Completion = "core", UsagePersisted = true, UsageRecordId = "usage-1" });
        }

        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new();
    }

    private sealed class ExplodingCoordinator : IAiExecutionCoordinator
    {
        public int Calls { get; private set; }

        public Task<AiOperationExecutionResult> ExecuteAsync(AiOperationRequest request, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("The recursion fence let a coordinated request back into the coordinator.");
        }
    }

    private sealed class CountingCoordinator : IAiExecutionCoordinator
    {
        public int Calls { get; private set; }

        public Task<AiOperationExecutionResult> ExecuteAsync(AiOperationRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new AiOperationExecutionResult
            {
                Operation = new AiOperation { Id = "op-1", Module = "admin", FeatureCode = request.GatewayRequest.FeatureCode!, IdempotencyKey = "k" },
                GatewayResult = new AiGatewayResult { Completion = "coordinated" },
            });
        }
    }

    private sealed class DuplicateCoordinator : IAiExecutionCoordinator
    {
        public Task<AiOperationExecutionResult> ExecuteAsync(AiOperationRequest request, CancellationToken ct)
            => Task.FromResult(new AiOperationExecutionResult
            {
                Operation = new AiOperation
                {
                    Id = "op-dup",
                    Module = "admin",
                    FeatureCode = request.GatewayRequest.FeatureCode!,
                    IdempotencyKey = "k",
                    State = AiOperationState.Completed,
                    ResultRef = "usage-1",
                },
                WasDuplicate = true,
                GatewayResult = null,
            });
    }

    /// <summary>Captures the operation request the facade builds, so structural
    /// fields such as AttemptLimit can be pinned without a database.</summary>
    private sealed class CapturingCoordinator : IAiExecutionCoordinator
    {
        public AiOperationRequest? LastRequest { get; private set; }

        public Task<AiOperationExecutionResult> ExecuteAsync(AiOperationRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new AiOperationExecutionResult
            {
                Operation = new AiOperation
                {
                    Id = "op-capture",
                    Module = "admin",
                    FeatureCode = request.GatewayRequest.FeatureCode!,
                    IdempotencyKey = "k",
                },
                GatewayResult = new AiGatewayResult { Completion = "coordinated" },
            });
        }
    }
}
