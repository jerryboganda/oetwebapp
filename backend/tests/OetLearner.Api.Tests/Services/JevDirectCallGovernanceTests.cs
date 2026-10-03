using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Ai;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.AiManagement;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Services;

/// <summary>
/// Governance of the direct (non-gateway) Jev path in
/// <see cref="DirectAiCallRecorder.BeginOperationAsync"/>: the owner emergency
/// levers (global kill switch + per-feature kill list) stop "jev." features
/// and ONLY those, and the platform budget hold can be sized by the caller.
/// Uses the real recorder over fake stores so the assertions are about what
/// the control plane did (operation inserted? budget held?), not about mocks.
/// </summary>
public sealed class JevDirectCallGovernanceTests
{
    private static DirectAiOperationRequest Request(string featureCode) => new()
    {
        FeatureCode = featureCode,
        Module = "jev",
        UserId = "user-1",
        RequestHash = "hash-" + featureCode,
    };

    // ── (a) kill switch + kill list, Jev only ───────────────────────────────

    [Theory]
    [InlineData(AiKillSwitchScope.PlatformKeysOnly)]
    [InlineData(AiKillSwitchScope.AllCalls)]
    public async Task KillSwitch_RefusesJevBeforeAnyOperationOrBudgetHold(AiKillSwitchScope scope)
    {
        using var h = Harness.Create(new AiGlobalPolicy { KillSwitchEnabled = true, KillSwitchScope = scope });

        var lease = await h.Recorder.BeginOperationAsync(Request(AiFeatureCodes.JevWritingGuard), CancellationToken.None);

        Assert.False(lease.CanProceed);
        Assert.Equal(DirectAiOperationDisposition.PolicyRefused, lease.Disposition);
        Assert.Equal("kill_switch", lease.Reason);
        Assert.Equal(0, h.Store.Inserts);
        Assert.Empty(h.Budget.Holds);
    }

    [Fact]
    public async Task KillList_RefusesOnlyTheListedJevFeature_CaseAndSpaceInsensitive()
    {
        using var h = Harness.Create(new AiGlobalPolicy
        {
            DisabledFeaturesCsv = " speaking.grade , JEV.WRITING.GUARD ",
        });

        var blocked = await h.Recorder.BeginOperationAsync(Request(AiFeatureCodes.JevWritingGuard), CancellationToken.None);
        var allowed = await h.Recorder.BeginOperationAsync(Request(AiFeatureCodes.JevWritingRoute), CancellationToken.None);

        Assert.Equal(DirectAiOperationDisposition.PolicyRefused, blocked.Disposition);
        Assert.Equal("feature_disabled", blocked.Reason);
        Assert.True(allowed.CanProceed);
        Assert.Equal(1, h.Store.Inserts);
        Assert.Single(h.Budget.Holds);
    }

    [Fact]
    public async Task KillSwitchAndKillList_LeaveNonJevDirectCallersUntouched()
    {
        using var h = Harness.Create(new AiGlobalPolicy
        {
            KillSwitchEnabled = true,
            DisabledFeaturesCsv = AiFeatureCodes.OcrListeningPartA,
        });

        var lease = await h.Recorder.BeginOperationAsync(Request(AiFeatureCodes.OcrListeningPartA), CancellationToken.None);

        Assert.True(lease.CanProceed);
        Assert.Equal(1, h.Store.Inserts);
    }

    [Fact]
    public async Task Jev_ProceedsWhenNoLeverIsPulled_OrNoQuotaServiceIsWired()
    {
        using var open = Harness.Create(new AiGlobalPolicy());
        using var unwired = Harness.Create(policy: null);

        var withPolicy = await open.Recorder.BeginOperationAsync(Request(AiFeatureCodes.JevWritingGuard), CancellationToken.None);
        var withoutQuota = await unwired.Recorder.BeginOperationAsync(Request(AiFeatureCodes.JevWritingGuard), CancellationToken.None);

        Assert.True(withPolicy.CanProceed);
        Assert.True(withoutQuota.CanProceed);
    }

    [Fact]
    public async Task Service_WithKillSwitchOn_IsUnavailableAndNeverSends()
    {
        using var h = Harness.Create(new AiGlobalPolicy { KillSwitchEnabled = true });
        var client = new CountingClient();
        var service = new TypeSafeJudgmentService(
            client,
            h.Recorder,
            Options.Create(new TypeSafeOptions { Enabled = true, ApiKey = "apikey_test" }),
            TimeProvider.System,
            NullLogger<TypeSafeJudgmentService>.Instance);

        var result = await service.AskAsync(
            new JevJudgmentRequest
            {
                StateText = "sample",
                Questions =
                [
                    new JevQuestion
                    {
                        Id = "is_urgent",
                        Kind = JevQuestionKind.Noul,
                        Instructions = "Is this urgent?",
                        NoulCriteria = new Dictionary<string, string?> { ["true"] = "yes", ["false"] = "no" },
                    },
                ],
            },
            new JevCallMetadata { FeatureCode = AiFeatureCodes.JevWritingGuard, UserId = "user-1" },
            CancellationToken.None);

        Assert.Equal(JevCallStatus.Unavailable, result.Status);
        Assert.Contains("kill_switch", result.Reason);
        Assert.Equal(0, client.Calls);
        Assert.Equal(0, h.Store.Inserts);
    }

    // ── (b) caller-sized budget hold ────────────────────────────────────────

    [Fact]
    public async Task BudgetHold_UsesTheCallerEstimateWhenSupplied()
    {
        using var h = Harness.Create(new AiGlobalPolicy());

        var lease = await h.Recorder.BeginOperationAsync(
            Request(AiFeatureCodes.JevWritingGuard), CancellationToken.None, reservationEstimateUsd: 0.000123m);

        Assert.True(lease.CanProceed);
        var hold = Assert.Single(h.Budget.Holds);
        Assert.Equal(0.000123m, hold.Usd);
        Assert.Equal(0.000123m, lease.BudgetReservation!.ReservedUsd);
    }

    [Fact]
    public async Task BudgetHold_FallsBackToTheFlatDefaultWhenNoEstimateIsSupplied()
    {
        using var h = Harness.Create(new AiGlobalPolicy());

        await h.Recorder.BeginOperationAsync(Request(AiFeatureCodes.OcrListeningPartA), CancellationToken.None);

        var hold = Assert.Single(h.Budget.Holds);
        Assert.Equal(AiBudgetService.DefaultReservationEstimateUsd, hold.Usd);
    }

    // ── (f) class alignment ─────────────────────────────────────────────────

    [Fact]
    public void DevelopmentTriage_IsAdminBatchInBothTheRegistryAndTheBudgetClasses()
    {
        Assert.Equal(
            AiOperationClass.AdminBatch,
            AiFeaturePolicyDefaults.All[AiFeatureCodes.JevDevelopmentTriage].OperationClass);
        Assert.Equal(
            AiOperationClass.AdminBatch,
            AiBudgetClasses.ClassForFeature(AiFeatureCodes.JevDevelopmentTriage));
    }

    // ── fakes ───────────────────────────────────────────────────────────────

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;

        private Harness(ServiceProvider provider, CountingStore store, CapturingBudget budget)
        {
            _provider = provider;
            Store = store;
            Budget = budget;
            Recorder = new DirectAiCallRecorder(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<DirectAiCallRecorder>.Instance,
                hostEnvironment: null,
                budgetService: budget);
        }

        public DirectAiCallRecorder Recorder { get; }
        public CountingStore Store { get; }
        public CapturingBudget Budget { get; }

        public static Harness Create(AiGlobalPolicy? policy)
        {
            var store = new CountingStore();
            var services = new ServiceCollection().AddSingleton<IAiOperationStore>(store);
            if (policy is not null) services.AddSingleton<IAiQuotaService>(new FakeQuota(policy));
            return new Harness(services.BuildServiceProvider(), store, new CapturingBudget());
        }

        public void Dispose() => _provider.Dispose();
    }

    private sealed class FakeQuota(AiGlobalPolicy policy) : IAiQuotaService
    {
        public Task<AiGlobalPolicy> GetGlobalPolicyAsync(CancellationToken ct) => Task.FromResult(policy);

        public Task<AiQuotaDecision> TryReserveAsync(
            string? userId, string featureCode, AiKeySource prospectiveKeySource, CancellationToken ct)
            => throw new NotSupportedException();

        public Task CommitAsync(
            string? userId, string featureCode, int promptTokens, int completionTokens,
            decimal costEstimateUsd, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<AiUserPolicySnapshot> GetUserPolicyAsync(string userId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class CountingStore : IAiOperationStore
    {
        public int Inserts { get; private set; }

        public Task<AiOperationInsertResult> TryInsertAsync(AiOperation operation, CancellationToken ct)
        {
            Inserts++;
            return Task.FromResult(new AiOperationInsertResult(AiOperationInsertOutcome.Inserted, operation));
        }

        public Task<bool> TryMarkTerminalAsync(
            string operationId, AiOperationState state, string? resultRef,
            string? selectedProviderId, string? selectedModel, CancellationToken ct)
            => Task.FromResult(true);

        public Task<AiOperation?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
            => Task.FromResult<AiOperation?>(null);

        public Task<AiOperation?> FindByResourceSlotAsync(string resourceSlotKey, CancellationToken ct)
            => Task.FromResult<AiOperation?>(null);

        public Task<bool> DeleteIfSafeFailureAsync(string resourceSlotKey, CancellationToken ct)
            => Task.FromResult(false);
    }

    private sealed class CapturingBudget : IAiBudgetService
    {
        public List<(AiOperationClass Class, decimal Usd)> Holds { get; } = new();

        public Task<AiBudgetReservation> ReserveForCallAsync(
            AiOperationClass operationClass, decimal estimatedUsd, CancellationToken ct)
        {
            Holds.Add((operationClass, estimatedUsd));
            return Task.FromResult(new AiBudgetReservation(true, null, "period-1", AiBudgetClasses.GlobalScope, estimatedUsd));
        }

        public Task<AiBudgetReservation> ReserveAsync(string scope, decimal estimatedUsd, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<AiBudgetReservation> ReserveForOperationAsync(string? featureCode, decimal estimatedUsd, CancellationToken ct)
            => throw new NotSupportedException();

        public Task CommitAsync(AiBudgetReservation reservation, decimal actualUsd, CancellationToken ct)
            => Task.CompletedTask;

        public Task ReleaseAsync(AiBudgetReservation reservation, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class CountingClient : ITypeSafeJudgmentClient
    {
        public int Calls { get; private set; }

        public Task<TypeSafeRawResponse> SendAsync(string payloadJson, CancellationToken ct, string? platformApiKey = null)
        {
            Calls++;
            throw new InvalidOperationException("A killed judgment must not reach the transport.");
        }
    }
}
