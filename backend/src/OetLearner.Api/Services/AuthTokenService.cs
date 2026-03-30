using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OetLearner.Api.Configuration;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed record AuthenticatedSessionSubject(
    string UserId,
    string AuthAccountId,
    string Email,
    string Role,
    string DisplayName,
    bool IsEmailVerified,
    bool IsAuthenticatorEnabled,
    bool RequiresEmailVerification,
    bool RequiresMfa,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? AuthenticatorEnabledAt);

public sealed record IssuedAuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    string RefreshTokenHash,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class AuthTokenService(IOptions<AuthTokenOptions> authTokenOptions, TimeProvider timeProvider)
{
    public const string AuthAccountIdClaimType = "auth_account_id";
    public const string IsEmailVerifiedClaimType = "email_verified";
    public const string IsAuthenticatorEnabledClaimType = "authenticator_enabled";
    public const string RequiresEmailVerificationClaimType = "requires_email_verification";
    public const string RequiresMfaClaimType = "requires_mfa";
    public const string EmailVerifiedAtClaimType = "email_verified_at";
    public const string AuthenticatorEnabledAtClaimType = "authenticator_enabled_at";

    private readonly AuthTokenOptions _options = authTokenOptions.Value;

    public IssuedAuthSession IssueSession(AuthenticatedSessionSubject subject)
    {
        var now = timeProvider.GetUtcNow();
        var accessTokenExpiresAt = now.Add(_options.AccessTokenLifetime);
        var refreshTokenExpiresAt = now.Add(_options.RefreshTokenLifetime);
        var refreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.AccessTokenSigningKey!));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.UserId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AuthAccountIdClaimType, subject.AuthAccountId),
            new(ClaimTypes.NameIdentifier, subject.UserId),
            new(ClaimTypes.Role, subject.Role),
            new(ClaimTypes.Email, subject.Email),
            new(ClaimTypes.Name, subject.DisplayName),
            new(IsEmailVerifiedClaimType, subject.IsEmailVerified.ToString().ToLowerInvariant()),
            new(IsAuthenticatorEnabledClaimType, subject.IsAuthenticatorEnabled.ToString().ToLowerInvariant()),
            new(RequiresEmailVerificationClaimType, subject.RequiresEmailVerification.ToString().ToLowerInvariant()),
            new(RequiresMfaClaimType, subject.RequiresMfa.ToString().ToLowerInvariant())
        };

        if (subject.EmailVerifiedAt is not null)
        {
            claims.Add(new Claim(EmailVerifiedAtClaimType, subject.EmailVerifiedAt.Value.ToString("O")));
        }

        if (subject.AuthenticatorEnabledAt is not null)
        {
            claims.Add(new Claim(AuthenticatorEnabledAtClaimType, subject.AuthenticatorEnabledAt.Value.ToString("O")));
        }

        var accessTokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = accessTokenExpiresAt.UtcDateTime,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.OutboundClaimTypeMap.Clear();
        var accessToken = tokenHandler.WriteToken(tokenHandler.CreateToken(accessTokenDescriptor));

        return new IssuedAuthSession(
            accessToken,
            accessTokenExpiresAt,
            refreshToken,
            HashRefreshToken(refreshToken),
            refreshTokenExpiresAt);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var bytes = Encoding.UTF8.GetBytes(refreshToken);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
