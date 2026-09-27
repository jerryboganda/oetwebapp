using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Security;
using OetLearner.Api.Services.OwnerAgent;

namespace OetLearner.Api.Hubs;

/// <summary>
/// Owner Agent Console event relay (CONTRACT.md §5 "Hub"), mapped at
/// <c>/v1/owner-agent/hub</c> with the <c>OwnerAgent</c> policy, long polling only.
///
/// <para>
/// A single server-streaming method, <see cref="Stream"/>, relays the sidecar's
/// <c>GET /v1/sessions/{id}/events?after=seq</c> SSE feed. Being a streaming method it
/// is exempt from <c>MaximumParallelInvocationsPerClient</c>, so one connection can
/// follow a session while the page issues REST calls.
/// </para>
///
/// <para>
/// Authorization: the negotiate request and every long-poll request pass the endpoint
/// policy (owner + unlock ticket from the <c>X-Owner-Agent-Unlock</c> header or the
/// HttpOnly <c>oet_owner_unlock</c> cookie, never a query-string token).
/// In addition the stream re-validates the unlock, the owner allow-list and the kill
/// switch at least every <see cref="RevalidationInterval"/> of forwarded events
/// (heartbeats included, so idle streams are re-checked too), and the connection is
/// closed when the access token expires (<c>CloseOnAuthenticationExpiration</c>).
/// </para>
/// </summary>
[Authorize]
public sealed class OwnerAgentHub(
    OwnerAgentClient client,
    IServiceScopeFactory scopeFactory,
    IOptions<OwnerAgentOptions> options,
    TimeProvider timeProvider,
    ILogger<OwnerAgentHub> logger) : Hub
{
    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromSeconds(5);

    public async IAsyncEnumerable<JsonElement> Stream(
        string sessionId,
        long afterSeq,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!OwnerAgentIds.IsUlid(sessionId))
        {
            throw new HubException("invalid_session_id");
        }

        if (afterSeq < 0)
        {
            throw new HubException("invalid_after_seq");
        }

        var unlock = await RevalidateAsync(initial: null, cancellationToken);
        var lastValidated = timeProvider.GetUtcNow();

        await using var events = client
            .StreamEventsAsync(sessionId, afterSeq, unlock.AccountId!, Context.ConnectionId, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await events.MoveNextAsync();
            }
            catch (OwnerAgentSidecarException ex)
            {
                logger.LogWarning("Owner agent stream for session {SessionId} ended: {Code}", sessionId, ex.Code);
                throw new HubException(ex.Code);
            }

            if (!hasNext)
            {
                yield break;
            }

            var now = timeProvider.GetUtcNow();
            if (now - lastValidated >= RevalidationInterval)
            {
                unlock = await RevalidateAsync(unlock, cancellationToken);
                lastValidated = now;
            }

            yield return events.Current;
        }
    }

    /// <summary>
    /// Stream start (<paramref name="initial"/> null): full validation of the ticket the
    /// connection presented (header, else the <c>oet_owner_unlock</c> cookie). Every later batch:
    /// owner allow-list + kill switch + ticket-FAMILY re-validation (fixed expiry, session alive,
    /// not locked / re-enrolled), so a stream stops when the unlock's fixed lifetime ends.
    /// Each long-poll request is also authorized by the endpoint policy.
    /// Fresh DI scope per check so a long stream never holds one DbContext for hours.
    /// Throws <see cref="HubException"/> (message = stable code) when the caller must stop.
    /// </summary>
    private async Task<OwnerAgentUnlockValidation> RevalidateAsync(
        OwnerAgentUnlockValidation? initial,
        CancellationToken cancellationToken)
    {
        var principal = Context.User;
        if (principal is null || !OwnerAgentIdentity.IsOwner(principal, options.Value))
        {
            throw new HubException(OwnerAgentFailureCodes.NotOwner);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<IOwnerAgentFeatureGate>();
        var availability = await gate.GetAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable)
        {
            throw new HubException("owner_agent_disabled");
        }

        var unlock = scope.ServiceProvider.GetRequiredService<IOwnerAgentUnlockService>();
        var validation = initial is null
            ? await unlock.ValidateAsync(principal, OwnerAgentHeaders.ReadUnlockTicket(Context.GetHttpContext()), cancellationToken)
            : await unlock.RevalidateFamilyAsync(principal, initial, cancellationToken);
        if (!validation.IsValid || validation.AccountId is null)
        {
            throw new HubException(validation.FailureCode ?? OwnerAgentFailureCodes.UnlockInvalid);
        }

        return validation;
    }
}
