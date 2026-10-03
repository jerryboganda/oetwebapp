using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Configuration;
using OetLearner.Api.Services.Ai.TypeSafe;
using OetLearner.Api.Services.Conversation;
using OetLearner.Api.Services.Rulebook;

namespace OetLearner.Api.Tests.Conversation;

/// <summary>
/// The Jev conversation advisory must never sit on the AI-patient turn path:
/// the Speaking v1.1 turn latency SLA includes <c>GenerateReplyAsync</c>, so a
/// slow or failing advisory must not delay, fail, or change the reply, and a
/// flag-off orchestrator must make zero Jev calls.
/// </summary>
public sealed class JevConversationOrchestratorTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private sealed class FakeGateway : IAiGatewayService
    {
        public AiGroundedPrompt BuildGroundedPrompt(AiGroundingContext context) => new();

        public Task<AiGatewayResult> CompleteAsync(AiGatewayRequest request, CancellationToken ct = default)
            => Task.FromResult(new AiGatewayResult { Completion = "{\"text\":\"Hello doctor.\"}" });
    }

    private sealed class FakeOptionsProvider : IConversationOptionsProvider
    {
        public Task<ConversationOptions> GetAsync(CancellationToken ct = default)
            => Task.FromResult(new ConversationOptions());

        public void Invalidate() { }
    }

    private sealed class RecordingAdvisor : IJevConversationAdvisor
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public bool Throw { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ConversationTurnSignal?> AssessLatestTurnAsync(
            string transcriptJson, int turnIndex, string? userId, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            Started.TrySetResult();
            if (Throw) throw new InvalidOperationException("boom");
            await Release.Task.WaitAsync(ct);
            return new ConversationTurnSignal(0.9, 0.9, 0.0, JevCallStatus.Ok);
        }
    }

    private static ConversationAiOrchestrator Create(RecordingAdvisor advisor, bool flagOn)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJevConversationAdvisor>(advisor);
        var provider = services.BuildServiceProvider();
        return new ConversationAiOrchestrator(
            new FakeGateway(),
            new FakeOptionsProvider(),
            NullLogger<ConversationAiOrchestrator>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new TypeSafeOptions
            {
                Enabled = flagOn,
                ConversationAdvisoryEnabled = flagOn,
            }));
    }

    private static ConversationAiContext Context() => new(
        SessionId: "session-1", UserId: "user-1", AuthAccountId: null, TenantId: null,
        Profession: ExamProfession.Medicine, TaskTypeCode: "speaking_roleplay",
        ScenarioJson: "{}", TranscriptJson: "[{\"role\":\"learner\",\"text\":\"Good morning.\"}]",
        TurnIndex: 1, ElapsedSeconds: 10, RemainingSeconds: 290, CandidateCountry: null);

    [Fact]
    public async Task Reply_DoesNotWaitForASlowJevAdvisory()
    {
        var advisor = new RecordingAdvisor();
        var orchestrator = Create(advisor, flagOn: true);

        // The advisor blocks until released: the reply can only return if it is not awaited.
        var reply = await orchestrator.GenerateReplyAsync(Context(), CancellationToken.None).WaitAsync(Patience);

        Assert.Equal("Hello doctor.", reply.Text);
        await advisor.Started.Task.WaitAsync(Patience);
        Assert.Equal(1, advisor.Calls);
        Assert.False(advisor.Release.Task.IsCompleted);
        advisor.Release.SetResult();
    }

    [Fact]
    public async Task Reply_IsUnaffectedByAFailingJevAdvisor()
    {
        var advisor = new RecordingAdvisor { Throw = true };
        var orchestrator = Create(advisor, flagOn: true);

        var reply = await orchestrator.GenerateReplyAsync(Context(), CancellationToken.None).WaitAsync(Patience);

        Assert.Equal("Hello doctor.", reply.Text);
        await advisor.Started.Task.WaitAsync(Patience);
        Assert.Equal(1, advisor.Calls);
    }

    [Fact]
    public async Task Reply_WithAdvisoryFlagOff_MakesNoJevCall()
    {
        var advisor = new RecordingAdvisor();
        var orchestrator = Create(advisor, flagOn: false);

        var reply = await orchestrator.GenerateReplyAsync(Context(), CancellationToken.None).WaitAsync(Patience);
        await Task.Delay(100);

        Assert.Equal("Hello doctor.", reply.Text);
        Assert.Equal(0, advisor.Calls);
    }

    [Fact]
    public async Task Opening_NeverAsksJev()
    {
        var advisor = new RecordingAdvisor();
        var orchestrator = Create(advisor, flagOn: true);

        await orchestrator.GenerateOpeningAsync(Context(), CancellationToken.None).WaitAsync(Patience);
        await Task.Delay(100);

        Assert.Equal(0, advisor.Calls);
    }
}
