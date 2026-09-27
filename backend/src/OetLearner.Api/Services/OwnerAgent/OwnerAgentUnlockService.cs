using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Data;
using OetLearner.Api.Security;

namespace OetLearner.Api.Services.OwnerAgent;

/// <summary>
/// A freshly minted console unlock ticket. The browser receives it as the HttpOnly
/// <c>oet_owner_unlock</c> cookie (<see cref="OwnerAgentUnlockCookie"/>); non-browser
/// callers may present it in the <c>X-Owner-Agent-Unlock</c> header instead.
/// </summary>
public sealed record OwnerAgentUnlockTicket(
    string Ticket,
    string TicketId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset AbsoluteExpiresAt);

/// <summary>Outcome of validating a presented unlock ticket against the current principal.</summary>
public sealed record OwnerAgentUnlockValidation(
    bool IsValid,
    string? FailureCode,
    string? AccountId = null,
    Guid? SessionFamilyId = null,
    string? TicketId = null,
    DateTimeOffset? IssuedAt = null,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? AbsoluteExpiresAt = null)
{
    /// <summary><see cref="HttpContext.Items"/> key the authorization handler stores a valid result under.</summary>
    public const string HttpContextItemKey = "OwnerAgent.UnlockValidation";

    public static OwnerAgentUnlockValidation Fail(string code) => new(false, code);
}

public interface IOwnerAgentUnlockService
{
    /// <summary>Mints a new ticket family for the principal's current session (after password + TOTP).</summary>
    OwnerAgentUnlockTicket Issue(ClaimsPrincipal principal);

    /// <summary>
    /// Re-mints a validated ticket with the SAME expiry. The unlock lifetime is fixed from the
    /// original unlock (<see cref="OwnerAgentOptions.UnlockMinutes"/>); a refresh never extends it.
    /// </summary>
    OwnerAgentUnlockTicket Refresh(OwnerAgentUnlockValidation validated);

    Task<OwnerAgentUnlockValidation> ValidateAsync(ClaimsPrincipal principal, string? ticket, CancellationToken cancellationToken);

    /// <summary>
    /// Re-checks a previously validated ticket FAMILY for a long-lived stream: same account and
    /// session, fixed expiry not reached, session alive, not locked / re-enrolled since.
    /// </summary>
    Task<OwnerAgentUnlockValidation> RevalidateFamilyAsync(ClaimsPrincipal principal, OwnerAgentUnlockValidation validated, CancellationToken cancellationToken);

    /// <summary>Records a successful unlock in the security-event feed (ticket id only, never the ticket).</summary>
    Task RecordUnlockedAsync(string accountId, Guid? sessionFamilyId, string ticketId, CancellationToken cancellationToken);

    /// <summary>Revokes every ticket the account holds (all sessions), durably.</summary>
    Task LockAsync(string accountId, Guid? sessionFamilyId, string reason, CancellationToken cancellationToken);

    /// <summary>Non-null while a recent authenticator re-enrolment blocks unlocking.</summary>
    Task<DateTimeOffset?> GetUnlockBlockedUntilAsync(string accountId, CancellationToken cancellationToken);
}

/// <summary>
/// Console unlock tickets (plan Phase 3 "Unlock"; CONTRACT.md §5).
///
/// <list type="bullet">
/// <item>Payload <c>{ id, accountId, sfam, issuedAt, exp, absExpiry }</c> protected with
/// <c>IDataProtector.ToTimeLimitedDataProtector()</c>, purpose <see cref="Purpose"/>, using the
/// app's shared, persisted key ring — so a ticket survives a blue/green slot switch.</item>
/// <item>Fixed lifetime <see cref="OwnerAgentOptions.UnlockMinutes"/> (default 60, clamped 5..480)
/// counted from the unlock: <c>exp == absExpiry == issuedAt + lifetime</c>. There is no sliding
/// extension; <see cref="Refresh"/> re-mints with the same expiry.</item>
/// <item>Bound to the access token's <c>auth_account_id</c> AND <c>sfam</c> (refresh-token
/// family): it survives access-token refresh (same family) and dies with the session — both
/// through the JWT pipeline's family check and the explicit family-alive check here, which
/// matters for long-lived hub streams.</item>
/// <item>Revocation without a new table: the newest <c>owner_agent.locked</c> /
/// <c>auth.authenticator_reenrolled</c> SecurityEvent for the account is a watermark; any
/// ticket issued before it is rejected.</item>
/// </list>
/// The browser holds the ticket only in the HttpOnly, Secure, SameSite=Strict
/// <c>oet_owner_unlock</c> cookie (page script never sees it); the API never persists it.
/// </summary>
public sealed class OwnerAgentUnlockService(
    IDataProtectionProvider dataProtectionProvider,
    LearnerDbContext db,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor,
    IOptions<OwnerAgentOptions> options) : IOwnerAgentUnlockService
{
    public const string Purpose = "OwnerAgent.Unlock.v1";

    /// <summary>Lifetime used when <see cref="OwnerAgentOptions.UnlockMinutes"/> is left at its default.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(OwnerAgentOptions.DefaultUnlockMinutes);

    private const int PayloadVersion = 1;

    /// <summary>The configured (clamped) fixed unlock lifetime.</summary>
    public TimeSpan Lifetime => options.Value.UnlockLifetime;

    private readonly ITimeLimitedDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public OwnerAgentUnlockTicket Issue(ClaimsPrincipal principal)
    {
        var accountId = OwnerAgentIdentity.GetAuthAccountId(principal)
            ?? throw ApiException.Forbidden("owner_agent_account_required", "Sign in again before unlocking the agent console.");
        var familyId = OwnerAgentIdentity.GetSessionFamilyId(principal)
            ?? throw ApiException.Forbidden("owner_agent_session_family_required", "Sign in again before unlocking the agent console.");

        var now = TruncateToMilliseconds(timeProvider.GetUtcNow());
        // Fixed lifetime: the ticket expires exactly Lifetime after the unlock, never later.
        var expires = now.Add(Lifetime);
        var absolute = expires;
        var ticketId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return Mint(new UnlockTicketPayload(PayloadVersion, ticketId, accountId, familyId.ToString("D"),
            now.ToUnixTimeMilliseconds(), expires.ToUnixTimeMilliseconds(), absolute.ToUnixTimeMilliseconds()));
    }

    public OwnerAgentUnlockTicket Refresh(OwnerAgentUnlockValidation validated)
    {
        if (!validated.IsValid
            || validated.AccountId is null
            || validated.SessionFamilyId is null
            || validated.TicketId is null
            || validated.IssuedAt is null
            || validated.AbsoluteExpiresAt is null)
        {
            throw ApiException.Forbidden(OwnerAgentFailureCodes.UnlockInvalid, OwnerAgentFailureCodes.Describe(OwnerAgentFailureCodes.UnlockInvalid));
        }

        var now = timeProvider.GetUtcNow();
        var absolute = validated.AbsoluteExpiresAt.Value;
        // Same expiry as the original unlock: a refresh never extends the lifetime.
        var expires = Min(validated.ExpiresAt ?? absolute, absolute);
        if (now >= expires)
        {
            throw ApiException.Forbidden(OwnerAgentFailureCodes.UnlockExpired, OwnerAgentFailureCodes.Describe(OwnerAgentFailureCodes.UnlockExpired));
        }

        return Mint(new UnlockTicketPayload(PayloadVersion, validated.TicketId, validated.AccountId,
            validated.SessionFamilyId.Value.ToString("D"), validated.IssuedAt.Value.ToUnixTimeMilliseconds(),
            expires.ToUnixTimeMilliseconds(), absolute.ToUnixTimeMilliseconds()));
    }

    public async Task<OwnerAgentUnlockValidation> ValidateAsync(
        ClaimsPrincipal principal,
        string? ticket,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockRequired);
        }

        if (ticket.Length > OwnerAgentHeaders.MaxTokenLength)
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockInvalid);
        }

        UnlockTicketPayload? payload;
        try
        {
            var json = _protector.Unprotect(ticket.Trim(), out _);
            payload = JsonSerializer.Deserialize<UnlockTicketPayload>(json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            // Tampered, foreign-purpose, or past the protector's own expiry.
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockInvalid);
        }

        if (payload is null
            || payload.Version != PayloadVersion
            || string.IsNullOrWhiteSpace(payload.TicketId)
            || string.IsNullOrWhiteSpace(payload.AccountId)
            || !Guid.TryParse(payload.SessionFamilyId, out var ticketFamilyId))
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockInvalid);
        }

        var accountId = OwnerAgentIdentity.GetAuthAccountId(principal);
        var familyId = OwnerAgentIdentity.GetSessionFamilyId(principal);
        if (accountId is null
            || familyId is null
            || !string.Equals(accountId, payload.AccountId, StringComparison.Ordinal)
            || familyId.Value != ticketFamilyId)
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockSessionMismatch);
        }

        var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(payload.IssuedAtMs);
        var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresAtMs);
        var absoluteExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(payload.AbsoluteExpiresAtMs);
        var now = timeProvider.GetUtcNow();
        // A ticket never lives longer than the configured fixed lifetime from its unlock (this
        // also retires tickets minted under the former 45-minute sliding / 8-hour scheme).
        if (expiresAt > absoluteExpiresAt
            || absoluteExpiresAt - issuedAt > Lifetime
            || now >= expiresAt
            || now >= absoluteExpiresAt)
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockExpired);
        }

        if (await CheckFamilyStateAsync(accountId, ticketFamilyId, issuedAt, now, cancellationToken) is { } failure)
        {
            return OwnerAgentUnlockValidation.Fail(failure);
        }

        return new OwnerAgentUnlockValidation(
            IsValid: true,
            FailureCode: null,
            AccountId: accountId,
            SessionFamilyId: ticketFamilyId,
            TicketId: payload.TicketId,
            IssuedAt: issuedAt,
            ExpiresAt: expiresAt,
            AbsoluteExpiresAt: absoluteExpiresAt);
    }

    public async Task<OwnerAgentUnlockValidation> RevalidateFamilyAsync(
        ClaimsPrincipal principal,
        OwnerAgentUnlockValidation validated,
        CancellationToken cancellationToken)
    {
        if (!validated.IsValid
            || validated.AccountId is null
            || validated.SessionFamilyId is null
            || validated.IssuedAt is null
            || validated.AbsoluteExpiresAt is null)
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockInvalid);
        }

        if (!string.Equals(OwnerAgentIdentity.GetAuthAccountId(principal), validated.AccountId, StringComparison.Ordinal)
            || OwnerAgentIdentity.GetSessionFamilyId(principal) != validated.SessionFamilyId)
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockSessionMismatch);
        }

        var now = timeProvider.GetUtcNow();
        if (now >= validated.AbsoluteExpiresAt.Value)
        {
            return OwnerAgentUnlockValidation.Fail(OwnerAgentFailureCodes.UnlockExpired);
        }

        return await CheckFamilyStateAsync(validated.AccountId, validated.SessionFamilyId.Value, validated.IssuedAt.Value, now, cancellationToken) is { } failure
            ? OwnerAgentUnlockValidation.Fail(failure)
            : validated;
    }

    /// <summary>
    /// DB-backed checks shared by ticket validation and stream re-validation: the session
    /// (refresh-token family) is still alive, and no lock / authenticator re-enrolment
    /// happened at or after the ticket family's issue time. Returns a failure code or null.
    /// </summary>
    private async Task<string?> CheckFamilyStateAsync(
        string accountId,
        Guid familyId,
        DateTimeOffset issuedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Session still alive? (The JWT pipeline checks this per HTTP request; a hub
        // stream re-validates through here, so check it explicitly.)
        var familyAlive = await db.RefreshTokenRecords
            .AsNoTracking()
            .AnyAsync(token => token.FamilyId == familyId
                               && token.ApplicationUserAccountId == accountId
                               && token.RevokedAt == null
                               && token.ExpiresAt > now, cancellationToken);
        if (!familyAlive)
        {
            return OwnerAgentFailureCodes.SessionRevoked;
        }

        // Revocation watermark (lock / authenticator re-enrolment). Only events newer than
        // the ticket's issue time matter, which also keeps this a narrow index range scan.
        var revocations = await OwnerAgentSecurityEvents.RecentAsync(
            db,
            accountId,
            OwnerAgentSecurityEventKinds.UnlockRevocationKinds,
            issuedAt.AddSeconds(-1),
            cancellationToken);
        return revocations.Any(row => row.OccurredAt >= issuedAt)
            ? OwnerAgentFailureCodes.UnlockRevoked
            : null;
    }

    public async Task RecordUnlockedAsync(string accountId, Guid? sessionFamilyId, string ticketId, CancellationToken cancellationToken)
    {
        db.SecurityEvents.Add(OwnerAgentSecurityEvents.Create(
            accountId,
            OwnerAgentSecurityEventKinds.OwnerAgentUnlocked,
            timeProvider.GetUtcNow(),
            httpContextAccessor.HttpContext,
            details: new { ticketId },
            sessionFamilyId: sessionFamilyId));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task LockAsync(string accountId, Guid? sessionFamilyId, string reason, CancellationToken cancellationToken)
    {
        // Strictly after any ticket minted in this same millisecond.
        var now = TruncateToMilliseconds(timeProvider.GetUtcNow()).AddMilliseconds(1);
        db.SecurityEvents.Add(OwnerAgentSecurityEvents.Create(
            accountId,
            OwnerAgentSecurityEventKinds.OwnerAgentLocked,
            now,
            httpContextAccessor.HttpContext,
            details: new { reason = reason.Length > 64 ? reason[..64] : reason },
            sessionFamilyId: sessionFamilyId));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task<DateTimeOffset?> GetUnlockBlockedUntilAsync(string accountId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await OwnerAgentSecurityEvents.RecentAsync(
            db,
            accountId,
            [OwnerAgentSecurityEventKinds.AuthenticatorReenrolled],
            now - OwnerAgentSecurityEventKinds.ReenrolmentUnlockCooldown,
            cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var blockedUntil = rows.Max(row => row.OccurredAt) + OwnerAgentSecurityEventKinds.ReenrolmentUnlockCooldown;
        return blockedUntil > now ? blockedUntil : null;
    }

    private OwnerAgentUnlockTicket Mint(UnlockTicketPayload payload)
    {
        var expires = DateTimeOffset.FromUnixTimeMilliseconds(payload.ExpiresAtMs);
        var ticket = _protector.Protect(JsonSerializer.Serialize(payload), expires);
        return new OwnerAgentUnlockTicket(
            ticket,
            payload.TicketId,
            DateTimeOffset.FromUnixTimeMilliseconds(payload.IssuedAtMs),
            expires,
            DateTimeOffset.FromUnixTimeMilliseconds(payload.AbsoluteExpiresAtMs));
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value)
        => DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());

    private sealed record UnlockTicketPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("id")] string TicketId,
        [property: JsonPropertyName("a")] string AccountId,
        [property: JsonPropertyName("f")] string SessionFamilyId,
        [property: JsonPropertyName("iat")] long IssuedAtMs,
        [property: JsonPropertyName("exp")] long ExpiresAtMs,
        [property: JsonPropertyName("abs")] long AbsoluteExpiresAtMs);
}
