using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using OetLearner.Api.Security;

namespace OetLearner.Api.Services.OwnerAgent;

public sealed record OwnerAgentStepUpToken(string Token, DateTimeOffset ExpiresAt);

public sealed record OwnerAgentStepUpResult(bool IsValid, string? FailureCode)
{
    public static readonly OwnerAgentStepUpResult Ok = new(true, null);
    public static OwnerAgentStepUpResult Fail(string code) => new(false, code);
}

public static class OwnerAgentStepUpFailureCodes
{
    public const string Required = "owner_agent_step_up_required";
    public const string Invalid = "owner_agent_step_up_invalid";
    public const string Expired = "owner_agent_step_up_expired";
    public const string AlreadyUsed = "owner_agent_step_up_used";

    public static string Describe(string code) => code switch
    {
        Required => "This action needs a fresh authenticator code. Confirm with your authenticator app and retry.",
        Expired => "The authenticator confirmation expired. Confirm again and retry.",
        AlreadyUsed => "That authenticator confirmation was already used. Confirm again for this action.",
        _ => "The authenticator confirmation is invalid. Confirm again and retry.",
    };
}

public interface IOwnerAgentStepUpService
{
    /// <summary>Mints a 5-minute, single-use step-up token after a fresh TOTP verification.</summary>
    OwnerAgentStepUpToken Issue(ClaimsPrincipal principal, OwnerAgentUnlockValidation unlock);

    /// <summary>Validates and burns a presented token. A token can succeed at most once.</summary>
    OwnerAgentStepUpResult Consume(ClaimsPrincipal principal, OwnerAgentUnlockValidation unlock, string? token);
}

/// <summary>
/// Step-up tokens for the high-risk console actions listed in CONTRACT.md §5
/// (<c>X-Owner-Agent-StepUp</c>): enabling Autopilot, Ship, GitHub token change,
/// engine connect/logout.
///
/// <para>
/// The token is a time-limited DataProtection payload bound to the account, the
/// session family (<c>sfam</c>) and the unlock ticket family, so it cannot be
/// used from another session or after the console was re-unlocked.
/// </para>
///
/// <para>
/// <b>Single use is tracked in process memory</b> (<see cref="OwnerAgentStepUpReplayCache"/>,
/// per API slot). Justification: the API serves from exactly one active blue/green
/// slot at a time (see the SignalR backplane note in Program.cs), tokens live 5 minutes,
/// and replaying one on the other slot after a cut-over would still require the owner's
/// live JWT, the same <c>sfam</c> and a valid unlock ticket. A race-free DB-backed
/// consume marker needs a unique constraint, i.e. a new table/migration, which this
/// delivery rules out; an insert into <c>SecurityEvents</c> cannot provide atomic
/// "first writer wins" semantics without one.
/// </para>
/// </summary>
public sealed class OwnerAgentStepUpService(
    IDataProtectionProvider dataProtectionProvider,
    OwnerAgentStepUpReplayCache replayCache,
    TimeProvider timeProvider) : IOwnerAgentStepUpService
{
    public const string Purpose = "OwnerAgent.StepUp.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int PayloadVersion = 1;

    private readonly ITimeLimitedDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public OwnerAgentStepUpToken Issue(ClaimsPrincipal principal, OwnerAgentUnlockValidation unlock)
    {
        var accountId = OwnerAgentIdentity.GetAuthAccountId(principal);
        var familyId = OwnerAgentIdentity.GetSessionFamilyId(principal);
        if (!unlock.IsValid || accountId is null || familyId is null || unlock.TicketId is null)
        {
            throw ApiException.Forbidden(OwnerAgentFailureCodes.UnlockRequired, OwnerAgentFailureCodes.Describe(OwnerAgentFailureCodes.UnlockRequired));
        }

        var now = timeProvider.GetUtcNow();
        var expires = now.Add(Lifetime);
        // Never outlive the unlock it is bound to.
        if (unlock.ExpiresAt is { } unlockExpires && unlockExpires < expires)
        {
            expires = unlockExpires;
        }

        var payload = new StepUpPayload(
            PayloadVersion,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            accountId,
            familyId.Value.ToString("D"),
            unlock.TicketId,
            expires.ToUnixTimeMilliseconds());
        var token = _protector.Protect(JsonSerializer.Serialize(payload), expires);
        return new OwnerAgentStepUpToken(token, DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresAtMs));
    }

    public OwnerAgentStepUpResult Consume(ClaimsPrincipal principal, OwnerAgentUnlockValidation unlock, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return OwnerAgentStepUpResult.Fail(OwnerAgentStepUpFailureCodes.Required);
        }

        if (token.Length > OwnerAgentHeaders.MaxTokenLength)
        {
            return OwnerAgentStepUpResult.Fail(OwnerAgentStepUpFailureCodes.Invalid);
        }

        StepUpPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StepUpPayload>(_protector.Unprotect(token.Trim(), out _));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            return OwnerAgentStepUpResult.Fail(OwnerAgentStepUpFailureCodes.Invalid);
        }

        if (payload is null || payload.Version != PayloadVersion || string.IsNullOrWhiteSpace(payload.TokenId))
        {
            return OwnerAgentStepUpResult.Fail(OwnerAgentStepUpFailureCodes.Invalid);
        }

        var accountId = OwnerAgentIdentity.GetAuthAccountId(principal);
        var familyId = OwnerAgentIdentity.GetSessionFamilyId(principal);
        if (!unlock.IsValid
            || accountId is null
            || familyId is null
            || !string.Equals(payload.AccountId, accountId, StringComparison.Ordinal)
            || !string.Equals(payload.SessionFamilyId, familyId.Value.ToString("D"), StringComparison.Ordinal)
            || !string.Equals(payload.UnlockTicketId, unlock.TicketId, StringComparison.Ordinal))
        {
            return OwnerAgentStepUpResult.Fail(OwnerAgentStepUpFailureCodes.Invalid);
        }

        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresAtMs);
        var now = timeProvider.GetUtcNow();
        if (now >= expiresAt)
        {
            return OwnerAgentStepUpResult.Fail(OwnerAgentStepUpFailureCodes.Expired);
        }

        return replayCache.TryConsume(payload.TokenId, expiresAt, now)
            ? OwnerAgentStepUpResult.Ok
            : OwnerAgentStepUpResult.Fail(OwnerAgentStepUpFailureCodes.AlreadyUsed);
    }

    private sealed record StepUpPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("id")] string TokenId,
        [property: JsonPropertyName("a")] string AccountId,
        [property: JsonPropertyName("f")] string SessionFamilyId,
        [property: JsonPropertyName("u")] string UnlockTicketId,
        [property: JsonPropertyName("exp")] long ExpiresAtMs);
}

/// <summary>
/// Process-wide "already used" set for step-up token ids (singleton). Atomic via
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/>; entries are pruned once their
/// token has expired, so the set stays tiny (owner-only, a handful per hour).
/// </summary>
public sealed class OwnerAgentStepUpReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _consumed = new(StringComparer.Ordinal);

    public bool TryConsume(string tokenId, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        foreach (var entry in _consumed)
        {
            if (entry.Value <= now)
            {
                _consumed.TryRemove(entry.Key, out _);
            }
        }

        return _consumed.TryAdd(tokenId, expiresAt);
    }
}
