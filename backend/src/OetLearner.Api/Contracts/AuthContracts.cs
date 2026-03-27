namespace OetLearner.Api.Contracts;

public sealed record AuthLoginRequest(string Email, string Password);

public sealed record AuthUserResponse(
    string UserId,
    string Role,
    string Email,
    string DisplayName,
    bool IsActive);

public sealed record AuthLoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthUserResponse User);
