using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services.StepUp;

/// <summary>Outcome of verifying a presented step-up token. Returned rather than
/// thrown so the endpoint filter can map it to a response.</summary>
public sealed record StepUpVerification(bool Ok, string? FailureCode, string? FailureMessage);

/// <summary>Issues and verifies the short-lived, single-scope proof-of-recent-TOTP
/// that guards the money-moving admin actions (PAY-16 / IAM-08).</summary>
public interface IStepUpService
{
    Task<StepUpResponse> IssueAsync(string authAccountId, string scope, string code, CancellationToken ct);

    Task<StepUpVerification> VerifyAsync(string authAccountId, string? token, string requiredScope, CancellationToken ct);
}

/// <summary>
/// Stateless, HMAC-signed step-up tokens. The token is signed with the shared
/// <c>AuthTokens:AccessTokenSigningKey</c> (never ASP.NET DataProtection) so a
/// token minted on one blue/green container verifies on the other.
/// </summary>
public sealed class StepUpService(
    LearnerDbContext db,
    IOptions<StepUpOptions> stepUpOptions,
    IOptions<AuthTokenOptions> authTokenOptions,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : IStepUpService
{
    private const int AllowedDriftWindows = 1;
    private const string AuthenticatorSecretPurpose = "AuthService.AuthenticatorSecret";
    private const int DefaultTokenLifetimeSeconds = 300;
    private const string InvalidCodeMessage = "That code is not valid. Check your authenticator app and try again.";

    private readonly StepUpOptions _stepUpOptions = stepUpOptions.Value;
    private readonly AuthTokenOptions _authTokenOptions = authTokenOptions.Value;
    private readonly IDataProtector _authenticatorSecretProtector =
        dataProtectionProvider.CreateProtector(AuthenticatorSecretPurpose);

    public async Task<StepUpResponse> IssueAsync(string authAccountId, string scope, string code, CancellationToken ct)
    {
        var account = await db.ApplicationUserAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == authAccountId, ct)
            ?? throw ApiException.Forbidden("step_up_not_enrolled", "Set up an authenticator app before confirming this action.");

        if (account.AuthenticatorEnabledAt is null)
        {
            throw ApiException.Forbidden("step_up_not_enrolled", "Set up an authenticator app before confirming this action.");
        }

        var normalizedScope = scope?.Trim() ?? string.Empty;
        if (normalizedScope.Length == 0)
        {
            throw ApiException.Validation("step_up_scope_required", "A step-up scope is required.");
        }

        var normalizedCode = code?.Trim() ?? string.Empty;
        if (normalizedCode.Length != 6 || normalizedCode.Any(character => !char.IsDigit(character)))
        {
            throw ApiException.Forbidden("step_up_invalid_code", InvalidCodeMessage);
        }

        var secretKey = ReadAuthenticatorSecretOrThrow(account);
        if (!AuthenticatorTotp.VerifyCode(secretKey, normalizedCode, timeProvider.GetUtcNow(), AllowedDriftWindows))
        {
            throw ApiException.Forbidden("step_up_invalid_code", InvalidCodeMessage);
        }

        var lifetimeSeconds = _stepUpOptions.TokenLifetimeSeconds > 0
            ? _stepUpOptions.TokenLifetimeSeconds
            : DefaultTokenLifetimeSeconds;
        var expiresAt = timeProvider.GetUtcNow().AddSeconds(lifetimeSeconds);
        var token = MintToken(authAccountId, normalizedScope, expiresAt);
        return new StepUpResponse(token, expiresAt, normalizedScope);
    }

    public Task<StepUpVerification> VerifyAsync(string authAccountId, string? token, string requiredScope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(Failure("step_up_required"));
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return Task.FromResult(Failure("step_up_malformed"));
        }

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = WebEncoders.Base64UrlDecode(parts[0]);
            providedSignature = WebEncoders.Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return Task.FromResult(Failure("step_up_malformed"));
        }

        var expectedSignature = ComputeSignature(payloadBytes);
        if (expectedSignature.Length != providedSignature.Length
            || !CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return Task.FromResult(Failure("step_up_invalid"));
        }

        StepUpTokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StepUpTokenPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return Task.FromResult(Failure("step_up_malformed"));
        }

        if (payload is null)
        {
            return Task.FromResult(Failure("step_up_malformed"));
        }

        if (!string.Equals(payload.Sub, authAccountId, StringComparison.Ordinal))
        {
            return Task.FromResult(Failure("step_up_subject_mismatch"));
        }

        if (!string.Equals(payload.Scope, requiredScope, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Failure("step_up_scope_mismatch"));
        }

        if (payload.Exp <= timeProvider.GetUtcNow().ToUnixTimeSeconds())
        {
            return Task.FromResult(Failure("step_up_expired"));
        }

        return Task.FromResult(new StepUpVerification(true, null, null));
    }

    private string MintToken(string authAccountId, string scope, DateTimeOffset expiresAt)
    {
        var payload = new StepUpTokenPayload(
            authAccountId,
            scope,
            expiresAt.ToUnixTimeSeconds(),
            Guid.NewGuid().ToString("N"));
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = ComputeSignature(payloadBytes);
        return $"{WebEncoders.Base64UrlEncode(payloadBytes)}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    private byte[] ComputeSignature(byte[] payloadBytes)
    {
        var signingKey = _authTokenOptions.AccessTokenSigningKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "AuthTokens:AccessTokenSigningKey must be configured to issue or verify step-up tokens.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return hmac.ComputeHash(payloadBytes);
    }

    private string ReadAuthenticatorSecretOrThrow(ApplicationUserAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.ProtectedAuthenticatorSecret))
        {
            throw ApiException.Forbidden("step_up_not_enrolled", "Set up an authenticator app before confirming this action.");
        }

        try
        {
            return _authenticatorSecretProtector.Unprotect(account.ProtectedAuthenticatorSecret);
        }
        catch
        {
            throw ApiException.Forbidden("step_up_not_enrolled", "Set up an authenticator app before confirming this action.");
        }
    }

    private static StepUpVerification Failure(string code)
        => new(false, code, "Confirm this action with your authenticator code.");

    private sealed record StepUpTokenPayload(
        [property: JsonPropertyName("sub")] string Sub,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("exp")] long Exp,
        [property: JsonPropertyName("jti")] string Jti);
}
