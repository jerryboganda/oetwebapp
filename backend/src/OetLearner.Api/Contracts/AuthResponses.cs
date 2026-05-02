namespace OetLearner.Api.Contracts;

public sealed record CurrentUserResponse(
    string UserId,
    string Email,
    string Role,
    string? DisplayName,
    bool IsEmailVerified,
    bool IsAuthenticatorEnabled,
    bool RequiresEmailVerification,
    bool RequiresMfa,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? AuthenticatorEnabledAt,
    string[]? AdminPermissions = null);

public sealed record AuthSessionResponse(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserResponse CurrentUser);

public sealed record OtpChallengeResponse(
    string ChallengeId,
    string Purpose,
    string DeliveryChannel,
    string DestinationHint,
    DateTimeOffset ExpiresAt,
    int RetryAfterSeconds);

public sealed record AuthenticatorSetupResponse(
    string SecretKey,
    string OtpAuthUri,
    string QrCodeDataUrl,
    IReadOnlyList<string> RecoveryCodes);

public sealed record SignupCatalogResponse(
    IReadOnlyList<SignupExamTypeResponse> ExamTypes,
    IReadOnlyList<SignupProfessionResponse> Professions,
    IReadOnlyList<string> ExternalAuthProviders);

public sealed record SignupExamTypeResponse(
    string Id,
    string Label,
    string Code,
    string Description);

public sealed record SignupProfessionResponse(
    string Id,
    string Label,
    IReadOnlyList<string> CountryTargets,
    IReadOnlyList<string> ExamTypeIds,
    string Description);

public sealed record ExternalRegistrationPromptResponse(
    string RegistrationToken,
    string Provider,
    string Email,
    string? FirstName,
    string? LastName,
    string? NextPath);

public sealed record ExternalAuthExchangeResponse(
    string Status,
    AuthSessionResponse? Session,
    ExternalRegistrationPromptResponse? Registration);

public sealed record ActiveSessionResponse(
    Guid Id,
    string? DeviceInfo,
    string? IpAddress,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    bool IsCurrent);

public sealed record ActiveSessionListResponse(
    IReadOnlyList<ActiveSessionResponse> Sessions);
