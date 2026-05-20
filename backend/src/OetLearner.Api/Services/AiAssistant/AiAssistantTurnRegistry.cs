using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OetLearner.Api.Services.AiAssistant;

/// <summary>
/// In-process registry of in-flight assistant turns. Lets the hub cancel a
/// running turn by messageId. Singleton — turns are per-process so a multi-
/// instance deployment will only cancel turns running on the same node;
/// that is acceptable for V1 (turns are short-lived and stream over the
/// connection that issued them, which itself sticks to one node).
/// </summary>
public sealed class AiAssistantTurnRegistry
{
    private readonly ConcurrentDictionary<Guid, ActiveTurn> _turns = new();

    public CancellationTokenSource Register(Guid messageId, Guid threadId, Guid ownerUserId, CancellationToken hostToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        _turns[messageId] = new ActiveTurn(cts, threadId, ownerUserId);
        return cts;
    }

    public bool TryCancel(Guid messageId, Guid ownerUserId)
    {
        if (_turns.TryGetValue(messageId, out var turn) &&
            turn.OwnerUserId == ownerUserId &&
            _turns.TryRemove(messageId, out turn))
        {
            try { turn.Cancellation.Cancel(); } catch { /* fail-soft */ }
            return true;
        }
        return false;
    }

    public void Release(Guid messageId)
    {
        if (_turns.TryRemove(messageId, out var turn))
        {
            try { turn.Cancellation.Dispose(); } catch { /* fail-soft */ }
        }
    }

    private sealed record ActiveTurn(CancellationTokenSource Cancellation, Guid ThreadId, Guid OwnerUserId);
}
