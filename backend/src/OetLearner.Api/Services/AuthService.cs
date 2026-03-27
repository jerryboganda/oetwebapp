using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OetLearner.Api.Configuration;
using OetLearner.Api.Contracts;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;

namespace OetLearner.Api.Services;

public sealed class AuthService(
    LearnerDbContext db,
    IOptions<AuthOptions> authOptions,
    ILogger<AuthService> logger)
{
    private readonly AuthOptions _authOptions = authOptions.Value;

    public async Task<AuthLoginResponse> LoginAsync(AuthLoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw ApiException.Validation(
                "auth_invalid_credentials",
                "Email and password are required.",
                [
                    new ApiFieldError("email", "required", "Email is required."),
                    new ApiFieldError("password", "required", "Password is required.")
                ]);
        }

        var account = await db.AuthAccounts.FirstOrDefaultAsync(candidate => candidate.Email == email, ct);
        if (account is null || !account.IsActive || !VerifyPassword(request.Password, account.PasswordHash))
        {
            throw ApiException.Forbidden("auth_invalid_credentials", "Incorrect email or password.");
        }

        if (string.IsNullOrWhiteSpace(_authOptions.SigningKey) || _authOptions.SigningKey.Length < 32)
        {
            throw ApiException.Validation("auth_signing_key_missing", "JWT signing key is not configured correctly.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(8);
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_authOptions.SigningKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim("user_id", account.SubjectId),
                new Claim("role", account.Role),
                new Claim(ClaimTypes.NameIdentifier, account.SubjectId),
                new Claim(ClaimTypes.Role, account.Role),
                new Claim(ClaimTypes.Email, account.Email),
                new Claim(ClaimTypes.Name, account.DisplayName)
            ]),
            Expires = expiresAt.UtcDateTime,
            Issuer = _authOptions.Issuer,
            Audience = _authOptions.Audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        account.LastLoginAt = now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Issued JWT for {Email} with role {Role}", account.Email, account.Role);

        return new AuthLoginResponse(
            tokenHandler.WriteToken(token),
            expiresAt,
            ToUserResponse(account));
    }

    public async Task<AuthUserResponse> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var subjectId = principal.FindFirstValue("user_id") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = principal.FindFirstValue("role") ?? principal.FindFirstValue(ClaimTypes.Role);

        if (string.IsNullOrWhiteSpace(subjectId) || string.IsNullOrWhiteSpace(role))
        {
            throw ApiException.Forbidden("auth_invalid_token", "Your session token is invalid.");
        }

        var account = await db.AuthAccounts.FirstOrDefaultAsync(candidate => candidate.SubjectId == subjectId && candidate.Role == role, ct);
        if (account is null || !account.IsActive)
        {
            throw ApiException.Forbidden("auth_account_not_found", "Your account is no longer active.");
        }

        return ToUserResponse(account);
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        const int iterations = 100_000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string passwordHash)
    {
        var parts = passwordHash.Split('.', 4);
        if (parts.Length != 4 || parts[0] != "v1")
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static AuthUserResponse ToUserResponse(AuthAccount account)
        => new(account.SubjectId, account.Role, account.Email, account.DisplayName, account.IsActive);
}
