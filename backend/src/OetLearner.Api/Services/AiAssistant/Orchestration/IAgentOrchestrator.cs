using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OetLearner.Api.Contracts.AiAssistant;

namespace OetLearner.Api.Services.AiAssistant.Orchestration;

public sealed class AgentContext
{
    public Guid ThreadId { get; init; }
    public Guid MessageId { get; init; }
    public Guid ActorUserId { get; init; }
    public ChannelWriter<StreamFrame>? FrameSink { get; init; }
}

public interface IAgentOrchestrator
{
    // TODO: route via IAiGatewayService.BuildGroundedPrompt + CompleteAsync
    // TODO: write AuditEvent on each completed turn.
    Task RunTurnAsync(AgentContext ctx, string userMessage, CancellationToken ct);
}
