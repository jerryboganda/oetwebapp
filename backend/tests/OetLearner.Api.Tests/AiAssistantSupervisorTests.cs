using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OetLearner.Api.Contracts.AiAssistant;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Domain.AiAssistant;
using OetLearner.Api.Services.AiAssistant;
using OetLearner.Api.Services.AiAssistant.Orchestration;
using OetLearner.Api.Services.Rulebook;
using AiAssistantChatMessage = global::OetLearner.Api.Domain.AiAssistant.AiChatMessage;

namespace OetLearner.Api.Tests;

public class AiAssistantSupervisorTests
{
    [Fact]
    public void TurnRegistry_CancelRequiresOwningUser()
    {
        var registry = new AiAssistantTurnRegistry();
        var messageId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var cts = registry.Register(messageId, threadId, ownerId, CancellationToken.None);

        Assert.False(registry.TryCancel(messageId, otherUserId));
        Assert.False(cts.IsCancellationRequested);
        Assert.True(registry.TryCancel(messageId, ownerId));
        Assert.True(cts.IsCancellationRequested);

        cts.Dispose();
    }

    [Fact]
    public async Task RunTurnAsync_UsesGroundedGateway_AndRecordsOneCanonicalUsageRow()
    {
        var (db, agent, provider) = BuildAgent(enabled: true);
        var threadId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        db.AiChatThreads.Add(new AiChatThread
        {
            Id = threadId,
            OwnerUserId = actorId,
            Title = "Ops check",
            Model = "unapproved-thread-model",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Messages =
            {
                new AiAssistantChatMessage
                {
                    Id = Guid.NewGuid(),
                    ThreadId = threadId,
                    Role = AiChatMessageRole.User,
                    Content = "Previous admin question",
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                }
            },
        });
        await db.SaveChangesAsync();

        var channel = Channel.CreateUnbounded<StreamFrame>();
        await agent.RunTurnAsync(new AgentContext
        {
            ThreadId = threadId,
            ActorUserId = actorId,
            MessageId = messageId,
            FrameSink = channel.Writer,
        }, "Summarise the AI Assistant status.", CancellationToken.None);

        var frames = await ReadFramesAsync(channel.Reader);
        Assert.Contains(frames, frame => frame is MessageStartFrame { ThreadId: var tid, MessageId: var id } && tid == threadId && id == messageId);
        Assert.Contains(frames, frame => frame is TokenDeltaFrame { ThreadId: var tid, Delta: "Grounded admin answer." } && tid == threadId);
        Assert.Contains(frames, frame => frame is MessageEndFrame { ThreadId: var tid, MessageId: var id } && tid == threadId && id == messageId);

        var assistant = await db.AiChatMessages.SingleAsync(m => m.Id == messageId);
        Assert.Equal(AiChatMessageRole.Assistant, assistant.Role);
        Assert.Equal("Grounded admin answer.", assistant.Content);

        var usage = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal(AiFeatureCodes.AdminAiChatbot, usage.FeatureCode);
        Assert.Equal(AiCallOutcome.Success, usage.Outcome);
        Assert.Equal("admin.ai_assistant.safety.v1", usage.RulebookVersion);
        Assert.Equal("mock", usage.ProviderId);
        Assert.Empty(await db.AiUsageLogs.ToListAsync());

        Assert.NotNull(provider.LastRequest);
        Assert.Contains("ADMIN AI ASSISTANT", provider.LastRequest!.SystemPrompt);
        Assert.Contains("Current admin message", provider.LastRequest.UserPrompt);
        Assert.Contains("Summarise the AI Assistant status.", provider.LastRequest.UserPrompt);
        Assert.Equal(string.Empty, provider.LastRequest.Model);
        await db.DisposeAsync();
    }

    [Fact]
    public async Task RunTurnAsync_KillSwitch_RecordsCanonicalRefusalWithoutCallingProvider()
    {
        var (db, agent, provider) = BuildAgent(enabled: false);
        var threadId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var channel = Channel.CreateUnbounded<StreamFrame>();
        await agent.RunTurnAsync(new AgentContext
        {
            ThreadId = threadId,
            ActorUserId = actorId,
            MessageId = messageId,
            FrameSink = channel.Writer,
        }, "Can you check prod?", CancellationToken.None);

        var frames = await ReadFramesAsync(channel.Reader);
        Assert.Contains(frames, frame => frame is ErrorFrame { ThreadId: var tid, Code: "kill_switch" } && tid == threadId);
        Assert.Null(provider.LastRequest);

        var usage = Assert.Single(await db.AiUsageRecords.ToListAsync());
        Assert.Equal(AiFeatureCodes.AdminAiChatbot, usage.FeatureCode);
        Assert.Equal(AiCallOutcome.GatewayRefused, usage.Outcome);
        Assert.Equal("kill_switch", usage.ErrorCode);
        Assert.Empty(await db.AiUsageLogs.ToListAsync());
        await db.DisposeAsync();
    }

    private static (LearnerDbContext Db, SupervisorAgent Agent, CapturingProvider Provider) BuildAgent(bool enabled)
    {
        var options = new DbContextOptionsBuilder<LearnerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new LearnerDbContext(options);
        var provider = new CapturingProvider();
        var recorder = new AiUsageRecorder(db, NullLogger<AiUsageRecorder>.Instance);
        var gateway = new AiGatewayService(new RulebookLoader(), new IAiModelProvider[] { provider }, recorder);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiAssistant:GlobalEnabled"] = enabled ? "true" : "false",
                ["AiAssistant:DefaultModel"] = "mock-chat",
            })
            .Build();
        var settings = new AiAssistantSettingsService(config);
        var agent = new SupervisorAgent(
            db,
            gateway,
            settings,
            recorder,
            NullLogger<SupervisorAgent>.Instance);

        return (db, agent, provider);
    }

    private static async Task<List<StreamFrame>> ReadFramesAsync(ChannelReader<StreamFrame> reader)
    {
        var frames = new List<StreamFrame>();
        await foreach (var frame in reader.ReadAllAsync())
        {
            frames.Add(frame);
        }
        return frames;
    }

    private sealed class CapturingProvider : IAiModelProvider
    {
        public string Name => "mock";
        public AiProviderRequest? LastRequest { get; private set; }

        public Task<AiProviderCompletion> CompleteAsync(AiProviderRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new AiProviderCompletion
            {
                Text = "Grounded admin answer.",
                Usage = new AiUsage { PromptTokens = 12, CompletionTokens = 4 },
            });
        }
    }
}